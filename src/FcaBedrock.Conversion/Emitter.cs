using System.Runtime.CompilerServices;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// Streams object records through a <see cref="ConversionPlan"/>, producing one
/// <see cref="EmittedObject"/> per <b>surviving</b> formed object (spec §7 step 4). Single-pass
/// and allocation-streaming: the incidence matrix is never materialized (P-16). All ordering and
/// naming are already decided by the planner; emit only looks values up. Data diagnostics
/// (unknown / unparseable values) are <b>aggregated per attribute</b> — a count with a bounded
/// sample, flushed in plan order after the stream, never one diagnostic per row (spec §16.4,
/// D-059) — and appended to the caller-supplied collector.
/// <para>
/// <b>Restriction (§10.4/D-091).</b> Emit applies the plan's <c>restrict_to</c> filters to whole
/// formed objects: each object is classified first, then filtered, so a surviving object keeps
/// <b>all</b> its crosses — restrictions filter objects, not observations. Calibration and the
/// column vocabulary were computed over the input universe <em>before</em> filtering (§7), so
/// some columns may legitimately end up empty (<c>AttributeHasNoCrosses</c>).
/// </para>
/// <para>
/// <b>Artifact validity (§16.2/§18.1, G-12).</b> This emitter reports failures as diagnostics; it
/// does not — and cannot — retract bytes a writer has already put into a caller-owned sink. A
/// run's output is valid <b>only if</b> its collected diagnostics contain no Error or Fatal once
/// the run is complete (for the <c>.cxt</c> two-pass, only after
/// <see cref="EmitReplaySession"/> disposal, which is when the final cross-pass aggregates land).
/// On any Error/Fatal <b>the caller must discard the output</b>. An invalid run reaches that state
/// two ways, which differ in whether the stream stops:
/// <list type="bullet">
/// <item>a <b>structural or grouping-storage halt</b> stops the object stream, so both <c>.cxt</c>
/// passes truncate identically (the object-name-sequence invariant cannot catch it) and a
/// <c>.dat</c> holds only the rows before the halt;</item>
/// <item>an <b>`unknown_value_policy = "fail"` abort</b> does <b>not</b> stop the stream — the
/// aggregated per-attribute diagnostic (§16.4) is computed over the whole population, so
/// enumeration completes and the Error flushes at the end, leaving a <em>complete but invalid</em>
/// artifact. "Abort" is Error's operation-failed semantics (§16.2), not "stop reading rows"; this
/// preserves the pre-Slice-F included-attribute <c>fail</c> behaviour (D-050/D-059).</item>
/// </list>
/// The whole-stream observability warnings are suppressed on both (they would describe an artifact
/// the caller must discard). Transactional publication is M7's conversion-run abstraction, not a
/// writer or emitter concern (P-15).
/// </para>
/// </summary>
public static class Emitter
{
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
        // Preparation ↔ source pairing before any row (D-098): the emit source must be
        // the one this plan's calibration was prepared against.
        await SourcePairing.ValidateAsync(plan.Calibrated.Resolution, source, cancellationToken).ConfigureAwait(false);

        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);
        var restrictions = RestrictionFilter.Create(plan);
        var matched = restrictions.NewMatchBuffer();
        var observability = new EmitObservability(plan.FormalAttributes.Count);

        var columnKey = plan.ObjectKey as ColumnObjectKey;
        var failSeen = columnKey?.Policy == DuplicateObjectPolicy.Fail ? new HashSet<string>(StringComparer.Ordinal) : null;
        var keepNamer = columnKey?.Policy == DuplicateObjectPolicy.Keep ? new ColumnKeyNamer() : null;
        var keepDuplicates = new DiagnosticTally();
        var keepDisambiguations = new DiagnosticTally();
        var recordIndex = 0;

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // §10.4/G-2 sequencing — the row IS the formed object here, so: classify, then filter,
            // then (only for a survivor) name.
            //
            // 1. Classify every included attribute. This runs for EVERY row, filtered or not:
            //    restrictions filter objects, not observations (D-097), so an included attribute's
            //    ordinary unparseable/unknown diagnostics are owned here regardless of whether the
            //    row survives.
            var crossed = new SortedSet<int>();
            for (var i = 0; i < count; i++)
            {
                Accumulate(plan.Attributes[i], record, crossed, unknown[i], unparseable[i]);
            }

            // 2. Evaluate every restriction over this row's observations.
            RestrictionFilter.Reset(matched);
            restrictions.ObserveWide(record, matched);

            // 3. A non-surviving row is not an object: no key check, no `fail` duplicate check, no
            //    `keep` name assignment — nothing downstream may observe it. recordIndex still
            //    advances below, because it is the SOURCE position, not a survivor rank (§5.4).
            if (!RestrictionFilter.Passes(matched))
            {
                recordIndex++;
                continue;
            }

            // 4. The row survives, so it becomes an object and takes a name.
            string name;
            if (columnKey is null)
            {
                // row_index: the source-assigned input position, verbatim. Filtering NEVER
                // renumbers it (§5.4/G-2) — if row 0 is filtered and row 1 survives, the survivor
                // is still named "1".
                name = record.Name;
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
                            // Only survivors are recorded, so a filtered row's key never trips this
                            // (G-2): it is not an object, so it cannot duplicate one.
                            diagnostics.Add(new BedrockDiagnostic(
                                DiagnosticCode.DuplicateObjectKey, DiagnosticSeverity.Error,
                                $"The wide object key '{key}' at record {recordIndex} duplicates an earlier record; duplicate_object_policy = \"fail\" (§6.1).",
                                new DiagnosticLocation(RecordIndex: recordIndex)));
                            yield break;
                        }

                        name = key!;
                        break;

                    case DuplicateObjectPolicy.Keep:
                        // §6.1: keep names are assigned in EMISSION order, so a filtered row
                        // consumes no assigned name and produces no suffix or diagnostic (G-2).
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

            // The surviving object keeps ALL its crosses — not only the observations that matched.
            var emitted = new EmittedObject(name, [.. crossed]);
            observability.Record(emitted);
            yield return emitted;
            recordIndex++;
        }

        // Aggregated data-phase diagnostics: one per attribute, in plan order, so the diagnostic
        // sequence is deterministic (P-7) and bounded regardless of row count. Reached only on normal
        // completion — a structural yield break above (invalid/duplicate key) skips these, suppressing
        // any pending keep warnings from the partial stream (matching the triple path).
        var aborted = FlushData(plan, unparseable, unknown, restrictions, diagnostics);

        // §6.1 keep: repeated cleaned keys aggregate to one DuplicateObjectKey (Warning); name-collision
        // escalations aggregate to one ObjectKeyNameDisambiguated (Warning). Two conditions, two codes.
        FlushPolicyAggregate(keepDuplicates, DiagnosticCode.DuplicateObjectKey, DiagnosticSeverity.Warning,
            "object name(s) reused a cleaned key and were kept as separate objects", "keep", diagnostics);
        FlushPolicyAggregate(keepDisambiguations, DiagnosticCode.ObjectKeyNameDisambiguated, DiagnosticSeverity.Warning,
            "object name(s) were disambiguated to stay unique", "keep", diagnostics);

        observability.Flush(plan, aborted, diagnostics);
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
        // Preparation ↔ source pairing before any row (D-098).
        await SourcePairing.ValidateAsync(plan.Calibrated.Resolution, source, cancellationToken).ConfigureAwait(false);

        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);
        var duplicates = new DiagnosticTally();
        var reports = new GroupingReports();
        var restrictions = RestrictionFilter.Create(plan);
        var matched = restrictions.NewMatchBuffer();
        var observability = new EmitObservability(plan.FormalAttributes.Count);

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
                        // §6.1/§10.4/G-2: the key change closes the merged group — grouping strictly
                        // precedes filtering, so the restriction is evaluated existentially over ALL
                        // the merged observations. One match preserves the WHOLE object with all its
                        // crosses; otherwise the complete group is dropped, after grouping and
                        // classification, never before.
                        if (RestrictionFilter.Passes(matched))
                        {
                            var closed = new EmittedObject(currentKey!, [.. crossed]);
                            observability.Record(closed);
                            yield return closed;
                        }

                        currentKey = key;
                        crossed = new SortedSet<int>();
                        RestrictionFilter.Reset(matched);
                    }

                    for (var i = 0; i < count; i++)
                    {
                        AccumulateDedupe(plan.Attributes[i], row, crossed, unknown[i], unparseable[i]);
                    }

                    restrictions.ObserveWide(row, matched);
                }
            }

            if (!storageFailed && !halted)
            {
                if (started && RestrictionFilter.Passes(matched))
                {
                    var last = new EmittedObject(currentKey!, [.. crossed]);
                    observability.Record(last);
                    yield return last;
                }

                var aborted = FlushData(plan, unparseable, unknown, restrictions, diagnostics);

                // §6.1 dedupe: repeated cleaned keys aggregate to one DuplicateObjectKey (Info) with a
                // bounded source-order sample; silent when every key is unique. Flushed on normal
                // completion. The count is PRE-FILTER by construction (G-2): the intake hook observes
                // the raw stream as it is grouped, so a merged object that restriction later drops has
                // still already been counted here — the tally reports what the INPUT contained, which
                // is what a duplicate-key report is for.
                FlushPolicyAggregate(duplicates, DiagnosticCode.DuplicateObjectKey, DiagnosticSeverity.Info,
                    "record(s) reused a cleaned key and merged onto the first occurrence", "dedupe", diagnostics);

                observability.Flush(plan, aborted, diagnostics);
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

        // Preparation ↔ source pairing before any row (D-098): validated on the raw source,
        // before it is wrapped for unordered grouping.
        await SourcePairing.ValidateAsync(plan.Calibrated.Resolution, source, cancellationToken).ConfigureAwait(false);

        var count = plan.Attributes.Count;
        var unknown = NewTallies(count);
        var unparseable = NewTallies(count);
        var byPredicate = IndexByPredicate(plan);
        var restrictions = RestrictionFilter.Create(plan);
        var matched = restrictions.NewMatchBuffer();
        var observability = new EmitObservability(plan.FormalAttributes.Count);

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
                        // §10.4/G-2: the subject's COMPLETE group is the formed object, so the
                        // restriction decides emission only now — with every predicate/value
                        // observation for the subject seen. An absent restricted predicate therefore
                        // never matched, and the object fails. `completed` still records the subject
                        // either way: contiguity is a STRUCTURAL property of the input, independent of
                        // whether the object was emitted.
                        if (RestrictionFilter.Passes(matched))
                        {
                            var closed = new EmittedObject(currentSubject!, [.. crossed]);
                            observability.Record(closed);
                            yield return closed;
                        }

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
                        RestrictionFilter.Reset(matched);
                    }

                    // Route the observation: a present predicate that binds attribute(s) classifies its
                    // value into the object's crosses (null value → present-missing → missing_policy in
                    // Classify). Absent/empty/unknown predicate = no observation; the subject still forms
                    // its object (empty crosses are legal, §10.1). Repeated and multi-valued predicates
                    // union their crosses (§5.3.1/§17 r8) and OR their restriction matches (§10.4).
                    if (row.Predicate is { } predicate)
                    {
                        if (byPredicate.TryGetValue(predicate, out var attrs))
                        {
                            foreach (var i in attrs)
                            {
                                Classify(plan.Attributes[i], row.Value, crossed, unknown[i], unparseable[i]);
                            }
                        }

                        // Independent of the classification routing above: a filter-only attribute
                        // binds a predicate but plans no column, so it has no entry in byPredicate.
                        restrictions.ObserveTriple(predicate, row.Value, matched);
                    }
                }
            }

            if (!storageFailed && !halted)
            {
                if (started && RestrictionFilter.Passes(matched))
                {
                    var last = new EmittedObject(currentSubject!, [.. crossed]);
                    observability.Record(last);
                    yield return last;
                }

                // Aggregated data-phase diagnostics, in plan order (P-7). Skipped on any halt above (the
                // conversion aborted; partial data diagnostics would be noise).
                var aborted = FlushData(plan, unparseable, unknown, restrictions, diagnostics);
                observability.Flush(plan, aborted, diagnostics);
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
                unknown.Record(raw); // ordered_cuts not-in-order (§11.8)
                return;

            case BinOutcome.Bin:
                var bin = result.Value!; // a Bin always carries a non-null label
                if (!attribute.KnownBins.Contains(bin))
                {
                    // Identity domain mismatch: the bin label is the raw value (§10.6).
                    unknown.Record(bin);
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

    // The one data-diagnostic flush order, shared by all three emit paths so they cannot drift
    // (P-7 — the diagnostic sequence is part of deterministic output): every planned attribute's
    // unparseable then unknown aggregate in PLAN order, followed by the filter-only restriction
    // aggregates in restriction (spec-attribute) order.
    //
    // The two groups cannot interleave, and that is structural rather than a choice: a
    // filter-only attribute plans no column, so it is absent from plan.Attributes entirely, while
    // an included-and-restricted attribute's unparseable values are owned by its classification
    // pass in the first group and the restriction path stays silent for it (D-097). Each raw
    // observation is therefore counted at most once per attribute per pass.
    //
    // Called only past each path's halt guard — a structural halt suppresses all of it.
    //
    // Returns TRUE when any aggregate flushed at Error/Fatal — i.e. an `unknown_value_policy =
    // "fail"` abort (§10.6/§10.4/D-097). The caller uses it to suppress the whole-stream
    // observability aggregates: that is the same rule G-12 states for the artifact itself — any
    // Error/Fatal invalidates the run — so once the run is invalid, "your context has empty
    // columns" describes an artifact the caller must discard anyway.
    //
    // The `fail` abort deliberately does NOT truncate the stream. These diagnostics are
    // AGGREGATED (count + bounded sample, §16.4/D-059), which structurally requires reading to
    // the end, and the included-attribute `fail` path has always completed and reported at the
    // flush; halting mid-stream would change that established behaviour. "Abort" here is
    // §16.2's Error semantics — the OPERATION failed — not "stop reading rows".
    private static bool FlushData(
        ConversionPlan plan,
        DiagnosticTally[] unparseable,
        DiagnosticTally[] unknown,
        RestrictionFilter restrictions,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        var aborted = false;
        for (var i = 0; i < plan.Attributes.Count; i++)
        {
            aborted |= Flush(plan.Attributes[i], unparseable[i], DiagnosticCode.SourceValueUnparseable, UnparseableSeverity, diagnostics);
            aborted |= Flush(plan.Attributes[i], unknown[i], DiagnosticCode.UnknownValueObserved, UnknownSeverity, diagnostics);
        }

        return restrictions.Flush(diagnostics) || aborted;
    }

    // Returns true when the aggregate flushed at Error/Fatal (the `fail` policy).
    private static bool Flush(
        PlannedAttribute attribute,
        DiagnosticTally tally,
        DiagnosticCode code,
        Func<UnknownValuePolicy, DiagnosticSeverity?> severityFor,
        ICollection<BedrockDiagnostic> diagnostics)
    {
        if (tally.Count == 0 || severityFor(attribute.UnknownValuePolicy) is not { } severity)
        {
            return false; // no occurrences, or a silent policy (skip)
        }

        var reason = code == DiagnosticCode.SourceValueUnparseable
            ? "could not be parsed as a number"
            : "are not in its declared domain";
        diagnostics.Add(new BedrockDiagnostic(
            code,
            severity,
            $"{tally.Count} value(s) on attribute '{attribute.Name}' {reason} (e.g. {tally.Sample}).",
            new DiagnosticLocation(AttributeName: attribute.Name)));
        return severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;
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

    // Unknown categorical value severity (§10.6). "include" resolves at calibrate; a
    // between-pass unknown reaching emit under it degrades to Warning (D-088 include-crash
    // closure) — the explicit Include → Warning fallback.
    private static DiagnosticSeverity? UnknownSeverity(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => null,
        UnknownValuePolicy.Warn => DiagnosticSeverity.Warning,
        UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
        UnknownValuePolicy.Include => DiagnosticSeverity.Warning,
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
}
