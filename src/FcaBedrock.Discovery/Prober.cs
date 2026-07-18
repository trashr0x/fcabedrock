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

    private static Diagnosed<SpecDocument> Failed(BedrockDiagnostic diagnostic) =>
        Diagnosed<SpecDocument>.Failed([diagnostic]);
}
