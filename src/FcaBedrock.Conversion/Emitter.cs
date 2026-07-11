using System.Runtime.CompilerServices;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// Streams object records through a <see cref="ConversionPlan"/>, producing one
/// <see cref="EmittedObject"/> per record (spec §7 step 4). Single-pass and
/// allocation-streaming: the incidence matrix is never materialized (P-16). All
/// ordering and naming are already decided by the planner; emit only looks values
/// up. Data diagnostics (unknown / unparseable values) are <b>aggregated per
/// attribute</b> — a count with a bounded sample, flushed in plan order after the
/// stream, never one diagnostic per row (spec §16.4, D-059) — and appended to the
/// caller-supplied collector.
/// </summary>
public static class Emitter
{
    // Per-attribute sample cap for aggregated diagnostics; bounded metadata (P-16).
    private const int SampleCap = 3;

    /// <summary>
    /// Emits the formal objects for <paramref name="plan"/> over <paramref name="source"/>. Object
    /// names follow <c>plan.ObjectKey</c>: <c>row_index</c> uses the source row index, while a wide
    /// <c>column</c> key names each object from its cleaned key cell and applies
    /// <c>duplicate_object_policy</c> — <c>fail</c> (a repeat halts with <c>DuplicateObjectKey</c>),
    /// <c>keep</c> (each row its own object, colliding names disambiguated by the converter), or
    /// <c>dedupe</c> (rows sharing a cleaned key collapse to one object, crosses unioned onto the first,
    /// first-occurrence order — §5.4/§6.1, P-15). <c>row_index</c>/<c>fail</c>/<c>keep</c> stream
    /// single-pass in source-row order; <c>dedupe</c> uses the shared grouping/spool backend (§17 rule 4).
    /// The matrix is never materialized (P-16). Emit diagnostics accrue to <paramref name="diagnostics"/>
    /// once per enumeration — a replaying caller (the <c>.cxt</c> two-pass) brackets this with
    /// <see cref="EmitReplay.Begin"/>.
    /// </summary>
    public static IAsyncEnumerable<EmittedObject> EmitAsync(
        ConversionPlan plan,
        IRecordSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        CancellationToken cancellationToken = default) =>
        EmitAsync(plan, source, diagnostics, GroupingOptions.Default, cancellationToken);

    // Internal overload: the grouping budget/fan-in is a spill-forcing test seam for the dedupe path
    // (P-6); production uses GroupingOptions.Default. Non-iterator, so the guards + dispatch run eagerly.
    internal static IAsyncEnumerable<EmittedObject> EmitAsync(
        ConversionPlan plan,
        IRecordSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        GroupingOptions groupingOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(groupingOptions);

        // The plan/entrypoint variant must match (§5 / D-082): the wide emit requires a wide plan.
        if (plan.Execution is not WideExecution)
        {
            throw new InvalidOperationException(
                "EmitAsync requires a wide plan (plan.Execution must be WideExecution); a triple plan uses EmitTripleAsync.");
        }

        // dedupe collapses rows sharing a cleaned key onto one object; non-contiguous keys cannot stream
        // in one pass (P-16), so it runs on the shared grouping/spool backend (§6.1/D-083). row_index /
        // fail / keep stream single-pass in source order.
        return plan.ObjectKey is ColumnObjectKey { Policy: DuplicateObjectPolicy.Dedupe } dedupeKey
            ? EmitDedupeAsync(plan, source, diagnostics, dedupeKey.Index, groupingOptions, cancellationToken)
            : EmitStreamingAsync(plan, source, diagnostics, cancellationToken);
    }

    // The single-pass wide path (row_index, or column fail/keep): one object per source row.
    private static async IAsyncEnumerable<EmittedObject> EmitStreamingAsync(
        ConversionPlan plan,
        IRecordSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);

        var columnKey = plan.ObjectKey as ColumnObjectKey;
        var failSeen = columnKey?.Policy == DuplicateObjectPolicy.Fail ? new HashSet<string>(StringComparer.Ordinal) : null;
        var keepNamer = columnKey?.Policy == DuplicateObjectPolicy.Keep ? new ColumnKeyNamer() : null;
        var keepDuplicates = new DiagnosticTally();
        var keepDisambiguations = new DiagnosticTally();
        var recordIndex = 0;

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string name;
            if (columnKey is null)
            {
                name = record.Name; // row_index: the source-assigned row index (unchanged)
            }
            else
            {
                var key = record.Field(columnKey.Index);
                if (!ObjectNames.IsUsable(key))
                {
                    // §5.4/§16.4/D-085: an empty / missing_token / whitespace / control-char key cell,
                    // or a cell absent from a ragged row, cannot name an object — halt this conversion.
                    diagnostics.Add(new BedrockDiagnostic(
                        DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
                        $"The wide object key at record {recordIndex} is empty, whitespace-only, a missing token, absent, or contains a control character; it cannot name an object (§5.4).",
                        new DiagnosticLocation(RecordIndex: recordIndex)));
                    yield break;
                }

                switch (columnKey.Policy)
                {
                    case DuplicateObjectPolicy.Fail:
                        if (!failSeen!.Add(key!))
                        {
                            // §6.1: a duplicate key means the key does not identify objects — stop.
                            diagnostics.Add(new BedrockDiagnostic(
                                DiagnosticCode.DuplicateObjectKey, DiagnosticSeverity.Error,
                                $"The wide object key '{key}' at record {recordIndex} duplicates an earlier record; duplicate_object_policy = \"fail\" (§6.1).",
                                new DiagnosticLocation(RecordIndex: recordIndex)));
                            yield break;
                        }

                        name = key!;
                        break;

                    case DuplicateObjectPolicy.Keep:
                        name = keepNamer!.Assign(key!, recordIndex, out var duplicate, out var disambiguated);
                        if (duplicate)
                        {
                            keepDuplicates.Record(key!);
                        }

                        if (disambiguated)
                        {
                            keepDisambiguations.Record($"{key}→{name}");
                        }

                        break;

                    default:
                        // dedupe dispatches to EmitDedupeAsync before reaching here; the streaming path
                        // sees only fail/keep. Kept for definite assignment of `name`.
                        throw new InvalidOperationException(
                            $"duplicate_object_policy '{columnKey.Policy}' is not a single-pass streaming policy.");
                }
            }

            var crossed = new SortedSet<int>();
            for (var i = 0; i < count; i++)
            {
                Accumulate(plan.Attributes[i], record, crossed, unknown[i], unparseable[i]);
            }

            yield return new EmittedObject(name, [.. crossed]);
            recordIndex++;
        }

        // Aggregated data-phase diagnostics: one per attribute, in plan order, so the diagnostic
        // sequence is deterministic (P-7) and bounded regardless of row count. Reached only on normal
        // completion — a structural yield break above (invalid/duplicate key) skips these, suppressing
        // any pending keep warnings from the partial stream (matching the triple path).
        for (var i = 0; i < count; i++)
        {
            Flush(plan.Attributes[i], unparseable[i], DiagnosticCode.SourceValueUnparseable, UnparseableSeverity, diagnostics);
            Flush(plan.Attributes[i], unknown[i], DiagnosticCode.UnknownValueObserved, UnknownSeverity, diagnostics);
        }

        // §6.1 keep: repeated cleaned keys aggregate to one DuplicateObjectKey (Warning); name-collision
        // escalations aggregate to one ObjectKeyNameDisambiguated (Warning). Two conditions, two codes.
        FlushPolicyAggregate(keepDuplicates, DiagnosticCode.DuplicateObjectKey, DiagnosticSeverity.Warning,
            "object name(s) reused a cleaned key and were kept as separate objects", "keep", diagnostics);
        FlushPolicyAggregate(keepDisambiguations, DiagnosticCode.ObjectKeyNameDisambiguated, DiagnosticSeverity.Warning,
            "object name(s) were disambiguated to stay unique", "keep", diagnostics);
    }

    /// <summary>
    /// Emits wide <c>column</c> objects under <c>duplicate_object_policy = "dedupe"</c> (§6.1 / D-083):
    /// rows sharing a <b>cleaned</b> key collapse to one object whose crosses are the union of all its
    /// rows', in <b>first-occurrence order</b> of the key (§17 rule 4). Runs on the shared grouping/spool
    /// backend (never the matrix, P-16); the intake hook tallies duplicates in source order for one
    /// aggregated <c>DuplicateObjectKey</c> (Info). An unusable key halts with
    /// <c>ObjectKeyValueInvalid</c> (Error), and grouping storage failures surface via the two-channel
    /// model — never exceptions across the seam (D-082).
    /// </summary>
    private static async IAsyncEnumerable<EmittedObject> EmitDedupeAsync(
        ConversionPlan plan,
        IRecordSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        int keyIndex,
        GroupingOptions groupingOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);
        var duplicates = new DiagnosticTally();
        var reports = new GroupingReports();

        // Group the keyed prefix by cleaned key (first-appearance order); the intake hook tallies
        // duplicates in source order, so no second ordinal seen-set is needed (one key structure, D-083).
        var grouped = FirstAppearanceGrouping.GroupByFirstAppearanceAsync(
            ReadKeyedPrefixAsync(source, keyIndex, cancellationToken),
            row => row.Field(keyIndex),
            DedupeRowCodec.Instance,
            groupingOptions,
            reports,
            onRow: (row, firstAppearance) =>
            {
                if (!firstAppearance)
                {
                    duplicates.Record(row.Field(keyIndex)!);
                }
            },
            cancellationToken: cancellationToken);

        string? currentKey = null;
        var crossed = new SortedSet<int>();
        var started = false;
        var halted = false;
        var storageFailed = false;

        try
        {
            var enumerator = grouped.GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (GroupingStorageException)
                    {
                        storageFailed = true; // recorded at its source; the emitter catches only to halt
                        break;
                    }

                    if (!moved)
                    {
                        break;
                    }

                    var row = enumerator.Current;
                    var key = row.Field(keyIndex);
                    if (!ObjectNames.IsUsable(key))
                    {
                        diagnostics.Add(new BedrockDiagnostic(
                            DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
                            $"The wide object key at record {row.Index} is empty, whitespace-only, a missing token, absent, or contains a control character; it cannot name an object (§5.4).",
                            new DiagnosticLocation(RecordIndex: row.Index)));
                        halted = true;
                        break;
                    }

                    if (!started)
                    {
                        currentKey = key;
                        started = true;
                    }
                    else if (!string.Equals(key, currentKey, StringComparison.Ordinal))
                    {
                        // Key change closes the group; later duplicates merged onto the first (§6.1).
                        yield return new EmittedObject(currentKey!, [.. crossed]);
                        currentKey = key;
                        crossed = new SortedSet<int>();
                    }

                    for (var i = 0; i < count; i++)
                    {
                        AccumulateDedupe(plan.Attributes[i], row, crossed, unknown[i], unparseable[i]);
                    }
                }
            }

            if (!storageFailed && !halted)
            {
                if (started)
                {
                    yield return new EmittedObject(currentKey!, [.. crossed]);
                }

                for (var i = 0; i < count; i++)
                {
                    Flush(plan.Attributes[i], unparseable[i], DiagnosticCode.SourceValueUnparseable, UnparseableSeverity, diagnostics);
                    Flush(plan.Attributes[i], unknown[i], DiagnosticCode.UnknownValueObserved, UnknownSeverity, diagnostics);
                }

                // §6.1 dedupe: repeated cleaned keys aggregate to one DuplicateObjectKey (Info) with a
                // bounded source-order sample; silent when every key is unique. Flushed on normal completion.
                FlushPolicyAggregate(duplicates, DiagnosticCode.DuplicateObjectKey, DiagnosticSeverity.Info,
                    "record(s) reused a cleaned key and merged onto the first occurrence", "dedupe", diagnostics);
            }
        }
        finally
        {
            // Storage aggregates always flush — even on early disposal — so cleanup Warnings recorded
            // during the grouping's disposal-time teardown reach the caller/session (D-082/P-14).
            FlushStorage(reports, diagnostics);
        }
    }

    // The keyed prefix for dedupe: each record wrapped as a live DedupeRow (zero copy; 0-based index; the
    // source Name is never parsed/persisted), truncated inclusive at the first row whose key field is
    // unusable — mirroring the triple unordered source (D-085), so the offender ranks strictly last and
    // the emitter halts there after the valid prefix.
    private static async IAsyncEnumerable<DedupeRow> ReadKeyedPrefixAsync(
        IRecordSource source,
        int keyIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var index = 0;
        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = DedupeRow.Live(record, index++);
            yield return row;
            if (!ObjectNames.IsUsable(row.Field(keyIndex)))
            {
                yield break;
            }
        }
    }

    private static void AccumulateDedupe(
        PlannedAttribute attribute, DedupeRow row, SortedSet<int> crossed, DiagnosticTally unknown, DiagnosticTally unparseable)
    {
        // Wide reads its value by resolved column index; classification is shared with the streaming and
        // triple paths (Classify), so live and decoded rows classify identically.
        var raw = attribute.Source is ColumnAttributeSource column
            ? row.Field(column.Index)
            : throw new InvalidOperationException(
                $"Wide emit requires a column source on attribute '{attribute.Name}'.");
        Classify(attribute, raw, crossed, unknown, unparseable);
    }

    /// <summary>
    /// Emits the formal objects for a triple <paramref name="plan"/> over <paramref name="source"/>,
    /// <b>owning ordering selection</b> from <c>plan.Execution</c> (§5.3 / D-082):
    /// <c>subject_grouped</c> consumes the raw source with a single-pass contiguity check, while
    /// <c>unordered</c> wraps it in a <see cref="UnorderedTripleRowSource"/> built here with a fresh
    /// per-enumeration <see cref="GroupingReports"/>, so report state is emitter-owned and a
    /// pass-2-only spool failure is never lost. Groups contiguous rows by cleaned subject, routes each
    /// row's predicate to the attribute(s) that bind it, and unions their crosses (§5.3.1 / §17 rule 8);
    /// object order is first-appearance of each cleaned subject (§17 rule 4). Structural failures
    /// (invalid subject, non-contiguity) and grouping <b>storage</b> failures are reported to
    /// <paramref name="diagnostics"/> and stop the stream — never exceptions across the seam
    /// (D-082/P-14). Single-pass over the grouped rows, no matrix (P-16). A replaying caller (the
    /// <c>.cxt</c> two-pass) brackets this with <see cref="EmitReplay.Begin"/>.
    /// </summary>
    public static IAsyncEnumerable<EmittedObject> EmitTripleAsync(
        ConversionPlan plan,
        ITripleRowSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        CancellationToken cancellationToken = default) =>
        EmitTripleAsync(plan, source, diagnostics, GroupingOptions.Default, cancellationToken);

    // Internal overload: the grouping budget/fan-in is a test seam for forcing spills (P-6); production
    // uses GroupingOptions.Default.
    internal static async IAsyncEnumerable<EmittedObject> EmitTripleAsync(
        ConversionPlan plan,
        ITripleRowSource source,
        ICollection<BedrockDiagnostic> diagnostics,
        GroupingOptions groupingOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(groupingOptions);

        // The plan/entrypoint variant must match (§5 / D-082): the triple emit requires a triple plan.
        if (plan.Execution is not TripleExecution triple)
        {
            throw new InvalidOperationException(
                "EmitTripleAsync requires a triple plan (plan.Execution must be TripleExecution); a wide plan uses EmitAsync.");
        }

        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);
        var byPredicate = IndexByPredicate(plan);

        // Ordering selection is emitter-owned (§5.3 / D-082): the unordered wrapper is built here over a
        // fresh per-enumeration reports channel, never by an external selector without an active sink.
        var reports = new GroupingReports();
        var rows = triple.Ordering == TripleOrdering.Unordered
            ? new UnorderedTripleRowSource(source, groupingOptions, reports)
            : source;

        // Subjects whose group has closed, for the §5.3 contiguity check (ordinal, P-12).
        var completed = new HashSet<string>(StringComparer.Ordinal);
        string? currentSubject = null;
        var crossed = new SortedSet<int>();
        var started = false;
        var halted = false; // a structural halt: skip the final object and the data-aggregate flush
        var storageFailed = false;

        try
        {
            var enumerator = rows.ReadRowsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        // Storage failures surface from advancement only (D-082); cancellation stays an OCE.
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (GroupingStorageException)
                    {
                        storageFailed = true; // recorded at its source; the emitter catches only to halt
                        break;
                    }

                    if (!moved)
                    {
                        break;
                    }

                    var row = enumerator.Current;

                    // §5.4 / §18.1 / D-085: the subject is the object name; a null (empty / missing_token
                    // / short, per the source), whitespace-only, or control-char-bearing subject has no
                    // usable identity — halt this conversion.
                    if (!ObjectNames.IsUsable(row.Subject))
                    {
                        diagnostics.Add(new BedrockDiagnostic(
                            DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
                            $"The triple subject at record {row.RecordIndex} is empty, whitespace-only, a missing token, or contains a control character; it cannot name an object (§5.4).",
                            new DiagnosticLocation(RecordIndex: row.RecordIndex)));
                        halted = true;
                        break;
                    }

                    var subject = row.Subject!;
                    if (!started)
                    {
                        currentSubject = subject;
                        started = true;
                    }
                    else if (!string.Equals(subject, currentSubject, StringComparison.Ordinal))
                    {
                        // Close the finished object in first-appearance order, then enforce contiguity.
                        yield return new EmittedObject(currentSubject!, [.. crossed]);
                        completed.Add(currentSubject!);

                        if (completed.Contains(subject))
                        {
                            // §5.3 / §5.3.1 / D-082: subject recurs after its group closed → not contiguous;
                            // the diagnostic identifies the subject and the offending record index.
                            diagnostics.Add(new BedrockDiagnostic(
                                DiagnosticCode.TripleSubjectNotContiguous, DiagnosticSeverity.Error,
                                $"Triple subject '{subject}' recurs at record {row.RecordIndex} after an intervening subject; ordering = \"subject_grouped\" requires contiguous subjects (§5.3).",
                                new DiagnosticLocation(RecordIndex: row.RecordIndex)));
                            halted = true;
                            break;
                        }

                        currentSubject = subject;
                        crossed = new SortedSet<int>();
                    }

                    // Route the observation: a present predicate that binds attribute(s) classifies its
                    // value into the object's crosses (null value → present-missing → missing_policy in
                    // Classify). Absent/empty/unknown predicate = no observation; the subject still forms
                    // its object (empty crosses are legal, §10.1).
                    if (row.Predicate is { } predicate && byPredicate.TryGetValue(predicate, out var attrs))
                    {
                        foreach (var i in attrs)
                        {
                            Classify(plan.Attributes[i], row.Value, crossed, unknown[i], unparseable[i]);
                        }
                    }
                }
            }

            if (!storageFailed && !halted)
            {
                if (started)
                {
                    yield return new EmittedObject(currentSubject!, [.. crossed]);
                }

                // Aggregated data-phase diagnostics, in plan order (P-7). Skipped on any halt above (the
                // conversion aborted; partial data diagnostics would be noise).
                for (var i = 0; i < count; i++)
                {
                    Flush(plan.Attributes[i], unparseable[i], DiagnosticCode.SourceValueUnparseable, UnparseableSeverity, diagnostics);
                    Flush(plan.Attributes[i], unknown[i], DiagnosticCode.UnknownValueObserved, UnknownSeverity, diagnostics);
                }
            }
        }
        finally
        {
            // Storage aggregates always flush — even on early disposal — so the in-path Error surfaces
            // and cleanup Warnings from teardown reach the caller/session (D-082/P-14). One per identity,
            // worst severity, first-occurrence order.
            FlushStorage(reports, diagnostics);
        }
    }

    // Predicate selector → the plan-order indices of the attributes that bind it. A list because
    // one predicate may bind several attributes (source repeat, D-033); ordinal keys (P-12).
    private static Dictionary<string, List<int>> IndexByPredicate(ConversionPlan plan)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < plan.Attributes.Count; i++)
        {
            if (plan.Attributes[i].Source is PredicateAttributeSource predicate)
            {
                if (!map.TryGetValue(predicate.Predicate, out var list))
                {
                    list = [];
                    map[predicate.Predicate] = list;
                }

                list.Add(i);
            }
        }

        return map;
    }

    private static void Accumulate(
        PlannedAttribute attribute,
        ObjectRecord record,
        SortedSet<int> crossed,
        DiagnosticTally unknown,
        DiagnosticTally unparseable)
    {
        // Wide reads its value by resolved column index; classification is shared with the
        // predicate-routed triple path (Classify).
        var raw = attribute.Source is ColumnAttributeSource column
            ? record.Field(column.Index)
            : throw new InvalidOperationException(
                $"Wide emit requires a column source on attribute '{attribute.Name}'.");
        Classify(attribute, raw, crossed, unknown, unparseable);
    }

    // Classifies one raw value (null = missing) for an attribute into the object's crosses,
    // applying missing_policy and the discretizer outcomes. Shared by the wide (column-addressed)
    // and triple (predicate-routed) emit paths so both classify values identically (P-7).
    private static void Classify(
        PlannedAttribute attribute,
        string? raw,
        SortedSet<int> crossed,
        DiagnosticTally unknown,
        DiagnosticTally unparseable)
    {
        if (raw is null)
        {
            if (attribute.MissingFormalAttributeId is { } missingId)
            {
                crossed.Add(missingId); // §10.5 missing_policy = "as_attribute" (D-068)
            }

            return; // skip (null id): no cross, no diagnostic
        }

        var result = attribute.Discretizer.Discretize(raw);
        switch (result.Outcome)
        {
            case BinOutcome.NoBin:
                return; // out of range → no cross, silent (§11.2)

            case BinOutcome.Unparseable:
                unparseable.Record(raw); // §11.5 / D-050: kept, no cross, diagnosable
                return;

            case BinOutcome.Unknown:
                RecordUnknown(attribute, raw, unknown); // ordered_cuts not-in-order (§11.8)
                return;

            case BinOutcome.Bin:
                var bin = result.Value!; // a Bin always carries a non-null label
                if (!attribute.KnownBins.Contains(bin))
                {
                    // Identity domain mismatch: the bin label is the raw value (§10.6).
                    RecordUnknown(attribute, bin, unknown);
                    return;
                }

                // A recognized bin that crosses nothing (e.g. a dichotomy's false pole) has
                // no entry here and is correctly distinguished from an unknown value.
                if (attribute.CrossesByBin.TryGetValue(bin, out var ids))
                {
                    foreach (var id in ids)
                    {
                        crossed.Add(id);
                    }
                }

                return;
        }
    }

    // "include" extends the domain during the Calibrate phase, never at emit, so an
    // unknown reaching emit under that policy is an impossible state (preserved from the
    // per-row path — P-14). Other policies accrue to the aggregate and surface at flush.
    private static void RecordUnknown(PlannedAttribute attribute, string value, DiagnosticTally unknown)
    {
        if (attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
        {
            throw new InvalidOperationException(
                "unknown_value_policy = \"include\" must be resolved during calibration, not emit.");
        }

        unknown.Record(value);
    }

    private static void Flush(
        PlannedAttribute attribute,
        DiagnosticTally tally,
        DiagnosticCode code,
        Func<UnknownValuePolicy, DiagnosticSeverity?> severityFor,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        if (tally.Count == 0 || severityFor(attribute.UnknownValuePolicy) is not { } severity)
        {
            return; // no occurrences, or a silent policy (skip)
        }

        var reason = code == DiagnosticCode.SourceValueUnparseable
            ? "could not be parsed as a number"
            : "are not in its declared domain";
        diagnostics.Add(new BedrockDiagnostic(
            code,
            severity,
            $"{tally.Count} value(s) on attribute '{attribute.Name}' {reason} (e.g. {tally.Sample}).",
            new DiagnosticLocation(AttributeName: attribute.Name)));
    }

    // Aggregated object-key policy diagnostic (§6.1, D-083/D-085): one diagnostic with a bounded count +
    // source-order sample, flushed after the stream on normal completion (P-7/P-16). No location — the
    // condition spans the object stream, not a single record or attribute. Used by keep (Warning) and
    // dedupe (Info); the keep messages are byte-identical to their prior form.
    private static void FlushPolicyAggregate(
        DiagnosticTally tally, DiagnosticCode code, DiagnosticSeverity severity, string reason, string policy, ICollection<BedrockDiagnostic> diagnostics)
    {
        if (tally.Count == 0)
        {
            return;
        }

        diagnostics.Add(new BedrockDiagnostic(
            code, severity,
            $"{tally.Count} {reason} (e.g. {tally.Sample}) — duplicate_object_policy = \"{policy}\" (§6.1)."));
    }

    // Renders the per-enumeration storage-failure ledger as aggregated GroupingStorageFailed diagnostics
    // (D-082): one per (operation, kind), worst severity, in first-occurrence order. In-path failures
    // (Error) and cleanup failures (Warning) were already recorded at their logical positions by the sites
    // that detected them, so no re-merge is needed here. A single-pass (.dat) caller gets these directly;
    // the .cxt replay session intercepts and re-aggregates them across passes.
    private static void FlushStorage(GroupingReports reports, ICollection<BedrockDiagnostic> diagnostics)
    {
        foreach (var (failure, severity) in reports.Aggregates())
        {
            diagnostics.Add(GroupingStorageDiagnostics.Render(failure, severity));
        }
    }

    // Unknown categorical value severity (§10.6). "include" cannot reach Flush — RecordUnknown
    // throws first — so it is mapped defensively to warn.
    private static DiagnosticSeverity? UnknownSeverity(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => null,
        UnknownValuePolicy.Warn => DiagnosticSeverity.Warning,
        UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
        _ => DiagnosticSeverity.Warning,
    };

    // Unparseable numeric severity (§10.6 / §11.5, D-050). "include" → warn: an unparseable
    // token cannot be added to a numeric domain, so it behaves as warn here.
    private static DiagnosticSeverity? UnparseableSeverity(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => null,
        UnknownValuePolicy.Warn => DiagnosticSeverity.Warning,
        UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
        UnknownValuePolicy.Include => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Warning,
    };

    private static DiagnosticTally[] NewTallies(int count)
    {
        var tallies = new DiagnosticTally[count];
        for (var i = 0; i < count; i++)
        {
            tallies[i] = new DiagnosticTally();
        }

        return tallies;
    }

    // Per-attribute occurrence count plus a bounded first-observed sample (deterministic
    // given source order). Bounded metadata, never the matrix (P-16).
    private sealed class DiagnosticTally
    {
        private readonly List<string> _sample = [];

        public long Count { get; private set; }

        public string Sample => string.Join(", ", _sample);

        public void Record(string value)
        {
            Count++;
            if (_sample.Count < SampleCap)
            {
                _sample.Add(value);
            }
        }
    }
}
