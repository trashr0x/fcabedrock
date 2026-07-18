using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery;

/// <summary>
/// Discovery's entry point (§7.1, D-106…D-112): reads a source's cleaned records once and
/// returns a <b>draft</b> Bedrock spec the user then curates.
/// <para>
/// <b>Probe is outside the conversion pipeline.</b> <c>convert</c> calibrates but never
/// discovers, and probe never runs as a side effect of a conversion (D-003/D-036). It infers
/// nothing structural either — no delimiter sniffing, header detection, shape detection, or
/// value typing — so the caller states the shape and read settings, and every discovered
/// attribute is authored as string-valued <c>identity</c> + <c>nominal</c> (D-106).
/// </para>
/// <para>
/// <b>The draft is guaranteed usable, not merely well-formed</b> (D-107): a successful probe
/// returns a document with at least one attribute that rereads under the strict reader,
/// resolves against the probed schema, and converts the same source under the same effective
/// settings — all four legs proven by test, since probe itself runs no conversion.
/// </para>
/// <para>
/// <b>Probe touches no files.</b> It consumes an <see cref="IWideSourceSession"/> — an ordered
/// schema plus cleaned records — and returns a document; the caller serializes it through
/// <c>SpecWriter</c> and owns all output (D-109).
/// </para>
/// </summary>
public static class Prober
{
    /// <summary>
    /// The §5.3 default triple role map, authored explicitly when the caller supplies none. Held
    /// as one shared immutable instance: every part of a <see cref="TripleColumnsSection"/> is a
    /// record, so there is nothing to copy per call.
    /// </summary>
    private static readonly TripleColumnsSection DefaultRoles =
        new(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2));

    /// <summary>
    /// Probes a wide source, returning a draft spec with its warnings, or diagnostics alone
    /// when no valid draft exists.
    /// <para>
    /// <b>Errors are values; misuse is an exception</b> (P-14). A source that cannot be read, a
    /// column-less source, and a breached boundedness guard are diagnostics with no document. A
    /// null argument, or a <paramref name="session"/>/<paramref name="readSettings"/> pair that
    /// is not both wide, is a programmer error and throws: the caller chose the shape, so a
    /// mismatch is a bug in the call, not a property of the data.
    /// </para>
    /// <para>
    /// <b>Cancellation is never a diagnostic</b> (D-112): it propagates as
    /// <see cref="OperationCanceledException"/> and leaves no document, not even a partial one.
    /// An already-canceled token wins before any work is done.
    /// </para>
    /// </summary>
    /// <param name="session">The unbound wide source. Never bound; its records are read exactly once.</param>
    /// <param name="readSettings">The effective read settings the draft authors verbatim into <c>[binding]</c>.</param>
    /// <param name="options">Retention limit, boundedness guards, and locale; null means <see cref="ProbeOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static ValueTask<Diagnosed<SpecDocument>> ProbeAsync(
        IWideSourceSession session,
        SourceReadSettings readSettings,
        ProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(readSettings);

        if (session.Shape != SourceShape.Wide)
        {
            throw new ArgumentException("ProbeAsync requires a wide source session.", nameof(session));
        }

        if (readSettings.Shape != SourceShape.Wide)
        {
            throw new ArgumentException("ProbeAsync requires wide read settings.", nameof(readSettings));
        }

        return ProbeWideAsync(session, readSettings, options ?? ProbeOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Probes a triple (subject–predicate–value) source, returning a draft spec with its
    /// warnings, or diagnostics alone when no valid draft exists.
    /// <para>
    /// Same errors-are-values posture as <see cref="ProbeAsync"/>: null arguments and a
    /// session/settings pair that is not both triple are programmer errors and throw. An invalid
    /// <paramref name="columns"/> map is <b>not</b> one of those — a role map is authored spec
    /// content, so it is diagnosed, not thrown. It is checked by resolving the exact
    /// <c>[binding]</c> this probe would author <em>before</em> any row is read, and the
    /// resolver's own §5.3 diagnostics are forwarded unchanged (M5-IP-CX-001): probe re-validates
    /// nothing and mints no code of its own, so a bad role map reads identically here and from
    /// <c>validate</c>.
    /// </para>
    /// <para>
    /// <b>Structural validity is checked, not assumed.</b> An unusable subject halts the probe
    /// under either ordering, and an explicitly selected <c>subject_grouped</c> additionally
    /// requires contiguity — because a draft that its own same-source conversion would reject is
    /// not a draft (D-106/D-107). There is no grouping or counting pass: the rows are read once,
    /// in input order.
    /// </para>
    /// </summary>
    /// <param name="session">The unbound triple source. Never bound; its rows are read exactly once.</param>
    /// <param name="readSettings">The effective read settings the draft authors verbatim into <c>[binding]</c>, including the ordering.</param>
    /// <param name="columns">A complete role map in one addressing mode (§5.3), authored into the draft as supplied; null means <c>{ subject = 0, predicate = 1, value = 2 }</c>, authored explicitly.</param>
    /// <param name="options">Retention limit, boundedness guards, and locale; null means <see cref="ProbeOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static ValueTask<Diagnosed<SpecDocument>> ProbeTripleAsync(
        ITripleSourceSession session,
        SourceReadSettings readSettings,
        TripleColumnsSection? columns = null,
        ProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(readSettings);

        if (session.Shape != SourceShape.Triple)
        {
            throw new ArgumentException("ProbeTripleAsync requires a triple source session.", nameof(session));
        }

        if (readSettings.Shape != SourceShape.Triple)
        {
            throw new ArgumentException("ProbeTripleAsync requires triple read settings.", nameof(readSettings));
        }

        return ProbeTripleCoreAsync(
            session, readSettings, columns ?? DefaultRoles, options ?? ProbeOptions.Default, cancellationToken);
    }

    private static async ValueTask<Diagnosed<SpecDocument>> ProbeWideAsync(
        IWideSourceSession session,
        SourceReadSettings readSettings,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        // Before anything observable: an already-canceled probe must cancel rather than complete,
        // including over a source that would have produced no records at all.
        cancellationToken.ThrowIfCancellationRequested();

        SourceSchema schema;
        try
        {
            // Not a data-record pass. The schema is metadata — a column count and, when present,
            // the ordered header names — and reading it is what D-106's "one record pass" is
            // measured against, not part of it.
            schema = await session.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SourceReadException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (IOException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (UnauthorizedAccessException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (ObjectDisposedException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (DecoderFallbackException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (InvalidDataException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }

        // Guard 1 is answered by the schema alone, so a runaway column count fails before a
        // single record is read rather than after paying for the pass (D-110).
        if (schema.ColumnCount > options.MaxDiscoveredAttributes)
        {
            return Failed(ProbeDiagnostics.AttributeLimitExceeded(schema.ColumnCount, options.MaxDiscoveredAttributes));
        }

        // One attribute per physical column, whatever the data holds: a header-only source and an
        // all-missing one both succeed with their domains omitted, because the attributes are
        // real and their emptiness is a convert-time signal, not a probe failure (D-107).
        if (schema.ColumnCount == 0)
        {
            return Failed(ProbeDiagnostics.NoAttributesDiscovered());
        }

        var observation = new WideObservation(schema.ColumnCount, options);
        if (await ObserveAsync(session, observation, options, cancellationToken).ConfigureAwait(false) is { } failure)
        {
            // A failure after partial observation still yields no document: half a draft reads as
            // a whole one (D-112).
            return Failed(failure);
        }

        var columns = AttributeNaming.Plan(schema);
        var warnings = new List<BedrockDiagnostic>();

        // Both aggregates are flushed at end of pass in physical attribute order, so their
        // counts, ordering, and bounded samples depend only on the schema and the record
        // sequence (D-112).
        var adjusted = new ProbeTally();
        var truncated = new ProbeTally();
        foreach (var column in columns)
        {
            if (column.NameAdjusted)
            {
                adjusted.Record(column.Name);
            }

            if (observation.Domain(column.Index).Truncated)
            {
                truncated.Record(column.Name);
            }
        }

        if (adjusted.Any)
        {
            warnings.Add(ProbeDiagnostics.AttributeNameAdjusted(adjusted));
        }

        if (truncated.Any)
        {
            warnings.Add(ProbeDiagnostics.DomainTruncated(truncated, options.ValueRetentionLimit));
        }

        var draft = ProbeDraft.Build(readSettings, options, columns, observation.Domain, truncated.Count);
        return Diagnosed<SpecDocument>.Ok(draft, warnings);
    }

    private static async ValueTask<Diagnosed<SpecDocument>> ProbeTripleCoreAsync(
        ITripleSourceSession session,
        SourceReadSettings readSettings,
        TripleColumnsSection columns,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SourceSchema schema;
        try
        {
            // Metadata, not a data pass — and needed before the preflight, because a name-addressed
            // role map resolves against this header and an index-addressed one is range-checked
            // against this column count.
            schema = await session.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SourceReadException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (IOException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (UnauthorizedAccessException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (ObjectDisposedException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (DecoderFallbackException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (InvalidDataException ex) { return Failed(ProbeDiagnostics.SourceReadFailed(ex)); }

        // The binding this probe will author, built once and used for both the preflight and the
        // draft — so what was validated and what is written are the same object, not two
        // constructions that could drift.
        var binding = ProbeDraft.TripleBinding(readSettings, options, columns);

        // The M5-IP-CX-001 role-map preflight, BEFORE any row is read. Discovery does not
        // re-implement §5.3: it asks the resolver, which owns those rules, and forwards whatever
        // it says. A partial, mixed-mode, negative, out-of-range, non-distinct, headerless-name,
        // missing, or ambiguous map fails here — with no enumeration started, so a bad map costs
        // no read at all.
        var preflight = SpecResolver.Resolve(ProbeDraft.BindingOnly(binding), schema);
        if (!preflight.TryGetValue(out var resolved))
        {
            // Forwarded unchanged and in order: relabelling these as probe-phase, or wrapping them
            // in a probe code, would give one condition two owners (D-067/D-111).
            return Diagnosed<SpecDocument>.Failed(preflight.Diagnostics);
        }

        // Resolved indices drive the read; the draft still authors the caller's own addressing.
        var roles = resolved.Resolved.Spec.Binding.TripleColumns!;

        var observation = new TripleObservation(
            options, subjectGrouped: readSettings.Ordering is TripleOrdering.SubjectGrouped);
        if (await ObserveTripleAsync(session, roles, observation, cancellationToken).ConfigureAwait(false) is { } failure)
        {
            return Failed(failure);
        }

        // Unlike wide, whose attributes are known from the schema, triple's vocabulary is only
        // known once the pass has finished — so the empty case is decided here rather than up front.
        if (observation.Predicates.Count == 0)
        {
            return Failed(ProbeDiagnostics.NoPredicatesDiscovered());
        }

        var predicates = PredicateNaming.Plan(observation.Predicates);
        var warnings = new List<BedrockDiagnostic>();

        // Flushed at end of pass in predicate first-appearance order — the same fixed shape as
        // wide's physical-column order, so counts and bounded samples depend only on the record
        // sequence (D-112).
        var adjusted = new ProbeTally();
        var truncated = new ProbeTally();
        foreach (var predicate in predicates)
        {
            if (predicate.NameAdjusted)
            {
                adjusted.Record(predicate.Name);
            }

            if (observation.Domain(predicate.Predicate).Truncated)
            {
                truncated.Record(predicate.Name);
            }
        }

        if (adjusted.Any)
        {
            warnings.Add(ProbeDiagnostics.AttributeNameAdjusted(adjusted));
        }

        if (truncated.Any)
        {
            warnings.Add(ProbeDiagnostics.DomainTruncated(truncated, options.ValueRetentionLimit));
        }

        var draft = ProbeDraft.BuildTriple(binding, options, predicates, observation.Domain, truncated.Count);
        return Diagnosed<SpecDocument>.Ok(draft, warnings);
    }

    /// <summary>
    /// Runs the single record pass, returning the diagnostic that ended it early or null on
    /// normal completion.
    /// </summary>
    private static async ValueTask<BedrockDiagnostic?> ObserveAsync(
        IWideSourceSession session,
        WideObservation observation,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        // Enumerated by hand rather than with `await foreach` so the catch clauses here wrap
        // ONLY the session's own record acquisition. An `await foreach`'s try block would also
        // cover the observation body, where an exception is a bug in this engine — and P-14
        // forbids dressing a bug up as an infrastructure diagnostic.
        //
        // Acquisition is TWO calls before the first record arrives — ReadAsync and
        // GetAsyncEnumerator — and both are guarded (see OpenRecords). A compiler-generated
        // async iterator cannot throw from either, which is exactly why leaving them unguarded
        // looks safe: every adapter in this repo happens to be one. A hand-written
        // IWideSourceSession — the whole point of the D-109 seam — can fail there, and its
        // failure is no less a read failure for arriving one call earlier.
        var (records, openFailure) = OpenRecords(session, cancellationToken);
        if (records is null)
        {
            return openFailure;
        }

        await using var _ = records.ConfigureAwait(false);
        while (true)
        {
            bool moved;
            ObjectRecord? record = null;
            try
            {
                moved = await records.MoveNextAsync().ConfigureAwait(false);
                if (moved)
                {
                    // Current is a provider-owned call on the same enumerator, so it can fail for
                    // the same reasons MoveNextAsync can — an enumerator that materializes its row
                    // lazily does its real work right here. Read inside the boundary and observed
                    // outside it, so a failure to PRODUCE the record is classified while a failure
                    // to OBSERVE it stays an engine bug.
                    record = records.Current;
                }
            }

            // The complete M5-IP-008 set, written as explicit narrow clauses: expected
            // provider/read failures become a diagnostic, and everything else keeps its own
            // identity. In particular OperationCanceledException matches none of these and
            // propagates unwrapped (D-111/D-112), and NotSupportedException is absent by design —
            // the CSV adapter already normalizes Sep's row/buffer ceiling to SourceReadException,
            // so catching it here would also swallow genuine "this source cannot do that"
            // programmer errors.
            catch (SourceReadException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (IOException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (UnauthorizedAccessException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (ObjectDisposedException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (DecoderFallbackException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (InvalidDataException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }

            if (!moved)
            {
                return null;
            }

            // `moved` implies Current was read. A provider that yields null violates the seam's own
            // non-nullable element contract, and must surface as the bug it is — treating null as
            // end-of-sequence would silently stop the pass early and author a draft that
            // understates the data, the exact outcome D-112 forbids.
            var breach = observation.Observe(record!);
            if (breach is BudgetBreach.None)
            {
                continue;
            }

            // A breached guard stops the pass immediately — reading on would retain nothing more
            // and produce nothing usable (D-110).
            return breach is BudgetBreach.Values
                ? ProbeDiagnostics.ValueLimitExceeded(options.MaxTotalRetainedValues)
                : ProbeDiagnostics.TextLimitExceeded(options.MaxTotalRetainedValueText);
        }
    }

    /// <summary>
    /// Opens the record stream, guarding the two acquisition calls that run before the first
    /// <c>MoveNextAsync</c>. Returns the enumerator, or no enumerator and the diagnostic that
    /// explains why — the two are mutually exclusive, so a null enumerator always carries one.
    /// </summary>
    private static (IAsyncEnumerator<ObjectRecord>? Records, BedrockDiagnostic? Failure) OpenRecords(
        IWideSourceSession session, CancellationToken cancellationToken)
    {
        try
        {
            return (session.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken), null);
        }
        catch (SourceReadException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (IOException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (UnauthorizedAccessException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (ObjectDisposedException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (DecoderFallbackException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (InvalidDataException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
    }

    /// <summary>
    /// The triple twin of <see cref="ObserveAsync"/>: one enumeration, the same four guarded
    /// provider-owned calls, and the same rule that observation happens outside the catch
    /// boundary — a structural subject problem is data, not a read failure, and must not be
    /// classified as one (D-111).
    /// </summary>
    private static async ValueTask<BedrockDiagnostic?> ObserveTripleAsync(
        ITripleSourceSession session,
        TripleColumns roles,
        TripleObservation observation,
        CancellationToken cancellationToken)
    {
        var (rows, openFailure) = OpenRows(session, roles, cancellationToken);
        if (rows is null)
        {
            return openFailure;
        }

        await using var _ = rows.ConfigureAwait(false);
        while (true)
        {
            bool moved;
            var row = default(TripleRow);
            try
            {
                moved = await rows.MoveNextAsync().ConfigureAwait(false);
                if (moved)
                {
                    row = rows.Current;
                }
            }
            catch (SourceReadException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (IOException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (UnauthorizedAccessException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (ObjectDisposedException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (DecoderFallbackException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }
            catch (InvalidDataException ex) { return ProbeDiagnostics.SourceReadFailed(ex); }

            if (!moved)
            {
                return null;
            }

            if (observation.Observe(row) is { } halt)
            {
                return halt;
            }
        }
    }

    /// <summary>
    /// Opens the row stream under the resolved role map, guarding the two acquisition calls that
    /// run before the first <c>MoveNextAsync</c>. Exactly one enumeration is ever opened.
    /// </summary>
    private static (IAsyncEnumerator<TripleRow>? Rows, BedrockDiagnostic? Failure) OpenRows(
        ITripleSourceSession session, TripleColumns roles, CancellationToken cancellationToken)
    {
        try
        {
            return (session.ReadRowsAsync(roles, cancellationToken).GetAsyncEnumerator(cancellationToken), null);
        }
        catch (SourceReadException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (IOException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (UnauthorizedAccessException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (ObjectDisposedException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (DecoderFallbackException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
        catch (InvalidDataException ex) { return (null, ProbeDiagnostics.SourceReadFailed(ex)); }
    }

    private static Diagnosed<SpecDocument> Failed(BedrockDiagnostic diagnostic) =>
        Diagnosed<SpecDocument>.Failed([diagnostic]);
}
