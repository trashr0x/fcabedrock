using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// The Calibrate phase (§7 phase 2, D-036/D-093): reads data to resolve data-dependent
/// schema elements and produces the retained, immutable <see cref="CalibratedSpec"/> that
/// Plan consumes without re-derivation. Three classes of need, each with its own bound:
/// <list type="bullet">
/// <item><b>Discovery-class</b> (<c>identity</c> / <c>free_per_value</c>, D-098/D-101):
/// filling an absent <c>declared_domain</c> (<c>ObservedDomainUsed</c>) or extending an
/// explicit one under <c>unknown_value_policy = "include"</c>
/// (<c>UnknownValuePolicyInclude</c>). Set-idempotent, so bounded by the attribute
/// vocabulary — schema-scale metadata (P-16).</item>
/// <item><b>Count-insensitive cuts</b> (<c>equal_width</c> <c>range = "min_max"</c>,
/// D-102): a streaming minimum and maximum — two doubles, never the population.</item>
/// <item><b>Count-sensitive cuts</b> (<c>equal_frequency</c> and <c>equal_width</c>
/// <c>range = "percentile_p1_p99"</c>, D-103): the exact aggregated population, bounded by
/// the <see cref="QuantileAccumulator"/>'s fixed-capacity fill-and-spill model (D-095).</item>
/// </list>
/// <para>
/// A fully-declared spec skips the data pass: after the pairing guard the fast path returns
/// <see cref="CalibratedSpec.FromFullyDeclared"/> without enumerating rows. All binding
/// range checks are seam-owned (G-1); the calibrator emits no binding diagnostics.
/// </para>
/// <para>
/// <b>Source passes.</b> Wide always reads once. Triple reads once for
/// <c>subject_grouped</c>, and once more — a grouped pass — for <c>unordered</c> input
/// <i>only</i> when a count-sensitive need exists, because §5.3.1 counts each distinct
/// cleaned <c>(subject, predicate, value)</c> once per subject and that cannot be decided
/// while a subject's rows are scattered. Sources are replayable by contract; this is the
/// D-003-sanctioned bounded pre-pass, never a third read.
/// </para>
/// </summary>
public static class Calibrator
{
    /// <summary>
    /// Calibrates a wide resolved spec over its source. Requires
    /// <paramref name="resolved"/>.<see cref="ResolvedSpec.Schema"/> non-null
    /// (<see cref="ArgumentException"/>); throws <see cref="InvalidOperationException"/>
    /// when the shape is not wide, or when the source was not prepared against this
    /// resolution (the pairing guard, before any row). Expected failures are
    /// diagnostics (P-14); cancellation propagates.
    /// </summary>
    public static ValueTask<Diagnosed<CalibratedSpec>> CalibrateAsync(
        ResolvedSpec resolved, IRecordSource source, CancellationToken cancellationToken = default) =>
        CalibrateAsync(resolved, source, GroupingOptions.Default, observer: null, cancellationToken);

    /// <summary>
    /// Calibrates a triple resolved spec over its source. Enforces the G-3 triple structural
    /// checks — a structurally unusable subject (<c>ObjectKeyValueInvalid</c>) halts any read,
    /// and non-contiguity (<c>TripleSubjectNotContiguous</c>) halts a <c>subject_grouped</c>
    /// read — with the same codes/severities as emit; a structural Error yields no calibrated
    /// result. Requires a triple resolution.
    /// </summary>
    public static ValueTask<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(
        ResolvedSpec resolved, ITripleRowSource source, CancellationToken cancellationToken = default) =>
        CalibrateTripleAsync(resolved, source, GroupingOptions.Default, observer: null, cancellationToken);

    // The spill-forcing / accounting test seams (P-6), mirroring the emitter's internal
    // overloads: production always takes the public entry points above.
    internal static async ValueTask<Diagnosed<CalibratedSpec>> CalibrateAsync(
        ResolvedSpec resolved,
        IRecordSource source,
        GroupingOptions options,
        ICalibrationObserver? observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        RequireSchema(resolved);
        if (resolved.Spec.Binding.Shape != SourceShape.Wide)
        {
            throw new InvalidOperationException("CalibrateAsync requires a wide resolution; a triple resolution uses CalibrateTripleAsync.");
        }

        await SourcePairing.ValidateAsync(resolved, source, cancellationToken).ConfigureAwait(false);

        if (!CalibratedSpec.RequiresData(resolved.Spec))
        {
            return Diagnosed<CalibratedSpec>.Ok(CalibratedSpec.FromFullyDeclared(resolved));
        }

        var run = new CalibrationRun(resolved, options, observer, cancellationToken);
        CalibratedSpec? calibrated = null;
        try
        {
            var targets = run.BuildTargets(wide: true);

            // §7: every wide row is an independent observation — no deduplication by object key,
            // no duplicate_object_policy, no grouping. The population is row-scoped, which is
            // also why wide calibration never reads or validates the object-key column (D-099).
            await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var target in targets)
                {
                    target.Observer.Observe(record.Field(target.ColumnIndex));
                }
            }

            calibrated = run.Finish(targets, structural: null);
        }
        catch (GroupingStorageException)
        {
            // The ledger already recorded the in-path Error at source; Complete() surfaces it.
        }
        catch (CalibrationPopulationOverflowException ex)
        {
            run.Fail(Overflow(ex));
        }
        finally
        {
            // Before Complete, not after: teardown records its own cleanup-channel Warnings, and
            // the storage ledger is snapshotted into the result below (D-095/P-14).
            run.Cleanup();
        }

        return run.Complete(calibrated);
    }

    internal static async ValueTask<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(
        ResolvedSpec resolved,
        ITripleRowSource source,
        GroupingOptions options,
        ICalibrationObserver? observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        RequireSchema(resolved);
        if (resolved.Spec.Binding.Shape != SourceShape.Triple)
        {
            throw new InvalidOperationException("CalibrateTripleAsync requires a triple resolution; a wide resolution uses CalibrateAsync.");
        }

        await SourcePairing.ValidateAsync(resolved, source, cancellationToken).ConfigureAwait(false);

        if (!CalibratedSpec.RequiresData(resolved.Spec))
        {
            return Diagnosed<CalibratedSpec>.Ok(CalibratedSpec.FromFullyDeclared(resolved));
        }

        var run = new CalibrationRun(resolved, options, observer, cancellationToken);
        CalibratedSpec? calibrated = null;
        try
        {
            var targets = run.BuildTargets(wide: false);
            var byPredicate = IndexByPredicate(targets);
            var subjectGrouped = resolved.Settings.Ordering == TripleOrdering.SubjectGrouped;

            // Count-sensitive needs are the only reason to look at a subject's rows together;
            // when none exists, the raw pass covers everything and no second read is made.
            var countSensitive = targets.Any(t => t.Observer.IsCountSensitive);

            // Pass 1 (always): the raw-order stream. Discovery-class observation happens here —
            // §17 rule 3 fixes first-observation order as RAW input order, which grouping would
            // reorder — together with the G-3 structural checks. Under subject_grouped the
            // subject runs are already contiguous, so this same pass also does the §5.3.1
            // subject-local deduplication for count-sensitive needs; under unordered it does NOT
            // touch them at all (the grouped pass below owns them exclusively).
            var structural = await ReadRawAsync(
                source, byPredicate, subjectGrouped, countSensitive, cancellationToken)
                .ConfigureAwait(false);
            if (structural is not null)
            {
                calibrated = run.Finish(targets, structural);
            }
            else
            {
                // Pass 2 (unordered + count-sensitive only): the same rows, grouped by subject on
                // the bounded backend, so each subject's distinct cleaned observations can be
                // counted once. Discovery-class observers are NOT fed here — they were fed from
                // the raw stream — so no attribute is observed twice and no diagnostic is
                // double-reported.
                if (!subjectGrouped && countSensitive)
                {
                    await ReadGroupedAsync(source, byPredicate, run, cancellationToken).ConfigureAwait(false);
                }

                calibrated = run.Finish(targets, structural: null);
            }
        }
        catch (GroupingStorageException)
        {
            // The ledger already recorded the in-path Error at source; Complete() surfaces it.
        }
        catch (CalibrationPopulationOverflowException ex)
        {
            run.Fail(Overflow(ex));
        }
        finally
        {
            run.Cleanup();
        }

        return run.Complete(calibrated);
    }

    // The raw-order triple pass: discovery-class observation, the G-3 structural checks, and —
    // under subject_grouped ONLY — inline subject-local dedup for count-sensitive needs. Returns
    // the structural diagnostic that halted it, or null.
    //
    // Exactly one pass may feed a given observer (D-103): discovery-class observers belong to this
    // pass (§17 rule 3 needs raw order), and count-sensitive observers belong to whichever pass can
    // see a subject's rows together — this one under subject_grouped, the grouped pass under
    // unordered. Feeding a count-sensitive observer here under unordered would add every raw row's
    // multiplicity on top of the grouped pass's deduped contribution: the counts, and therefore the
    // cuts, would be wrong, and an unparseable value would be tallied twice in one phase.
    private static async ValueTask<BedrockDiagnostic?> ReadRawAsync(
        ITripleRowSource source,
        Dictionary<string, List<CalibrationTarget>> byPredicate,
        bool subjectGrouped,
        bool countSensitive,
        CancellationToken cancellationToken)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);

        // Non-null exactly when this pass owns the count-sensitive observers, which is also what
        // makes `fresh` below false — and so those observers untouched — under unordered.
        var seen = subjectGrouped && countSensitive
            ? new HashSet<(string Predicate, string? Value)>()
            : null;
        string? currentSubject = null;
        var started = false;

        await foreach (var row in source.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            // G-3/D-099: an unusable triple subject halts any calibration read (§5.4/§16.4).
            if (!ObjectNames.IsUsable(row.Subject))
            {
                return new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
                    $"The triple subject at record {row.RecordIndex} is empty, whitespace-only, a missing token, or contains a control character; it cannot name an object (§5.4).",
                    new DiagnosticLocation(RecordIndex: row.RecordIndex));
            }

            var subject = row.Subject!;
            if (subjectGrouped)
            {
                if (!started)
                {
                    currentSubject = subject;
                    started = true;
                }
                else if (!string.Equals(subject, currentSubject, StringComparison.Ordinal))
                {
                    completed.Add(currentSubject!);
                    if (completed.Contains(subject))
                    {
                        // G-3: a subject recurring after its group closed halts a subject_grouped read (§5.3).
                        return new BedrockDiagnostic(
                            DiagnosticCode.TripleSubjectNotContiguous, DiagnosticSeverity.Error,
                            $"Triple subject '{subject}' recurs at record {row.RecordIndex} after an intervening subject; ordering = \"subject_grouped\" requires contiguous subjects (§5.3).",
                            new DiagnosticLocation(RecordIndex: row.RecordIndex));
                    }

                    currentSubject = subject;
                    seen?.Clear(); // the dedup set never outlives its subject (D-095)
                }
            }

            if (row.Predicate is not { } predicate || !byPredicate.TryGetValue(predicate, out var matched))
            {
                continue;
            }

            // §5.3.1: each distinct cleaned (subject, predicate, value) contributes once. The key
            // is the RAW cleaned spelling, not the parsed number: "90" and "90.0" are distinct
            // observations that both count, and after parsing both increment the same numeric
            // value's count. Computed once per ROW — several attributes may share a predicate, and
            // the observation is deduplicated, not the attribute.
            var fresh = seen is not null && seen.Add((predicate, row.Value));
            foreach (var target in matched)
            {
                if (!target.Observer.IsCountSensitive)
                {
                    target.Observer.Observe(row.Value); // discovery-class: every row, raw order
                }
                else if (fresh)
                {
                    // Count-sensitive: subject_grouped only (`seen` is null under unordered, so
                    // `fresh` is false and the grouped pass keeps sole ownership).
                    target.Observer.Observe(row.Value);
                }
            }
        }

        return null;
    }

    // The grouped second pass for unordered + count-sensitive calibration: the same rows through
    // the bounded first-appearance grouping backend, so each subject's rows arrive contiguously
    // and the §5.3.1 subject-local rule applies exactly as it does under subject_grouped.
    private static async ValueTask ReadGroupedAsync(
        ITripleRowSource source,
        Dictionary<string, List<CalibrationTarget>> byPredicate,
        CalibrationRun run,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<(string Predicate, string? Value)>();
        string? currentSubject = null;
        var started = false;

        var grouped = FirstAppearanceGrouping.GroupByFirstAppearanceAsync(
            source.ReadRowsAsync(cancellationToken),
            static row => row.Subject,
            TripleRowCodec.Instance,
            run.Options,
            run.GroupingReports,
            onRow: null,
            cancellationToken);

        await foreach (var row in grouped.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var subject = row.Subject!; // the raw pass already halted on any unusable subject
            if (!started)
            {
                currentSubject = subject;
                started = true;
            }
            else if (!string.Equals(subject, currentSubject, StringComparison.Ordinal))
            {
                currentSubject = subject;
                seen.Clear();
            }

            if (row.Predicate is not { } predicate || !byPredicate.TryGetValue(predicate, out var matched))
            {
                continue;
            }

            if (!seen.Add((predicate, row.Value)))
            {
                continue;
            }

            foreach (var target in matched)
            {
                if (target.Observer.IsCountSensitive)
                {
                    target.Observer.Observe(row.Value);
                }
            }
        }
    }

    private static Dictionary<string, List<CalibrationTarget>> IndexByPredicate(List<CalibrationTarget> targets)
    {
        var byPredicate = new Dictionary<string, List<CalibrationTarget>>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!byPredicate.TryGetValue(target.Predicate!, out var list))
            {
                list = [];
                byPredicate[target.Predicate!] = list;
            }

            list.Add(target);
        }

        return byPredicate;
    }

    private static void RequireSchema(ResolvedSpec resolved)
    {
        if (resolved.Schema is null)
        {
            throw new ArgumentException("calibration requires a schema-aware resolution; resolved.Schema is null.", nameof(resolved));
        }
    }

    private static BedrockDiagnostic Overflow(CalibrationPopulationOverflowException ex) =>
        new(DiagnosticCode.CalibrationPopulationTooLarge, DiagnosticSeverity.Error,
            $"Attribute '{ex.AttributeName}' has a calibration population too large to count exactly; a value count, the total, or a merge sum would exceed {long.MaxValue} observations (§11.5).",
            new DiagnosticLocation(AttributeName: ex.AttributeName));

    // One attribute's calibration read location (wide column index or triple predicate), its
    // observer, and the unknown_value_policy that severities an unparseable aggregate (D-100).
    private sealed record CalibrationTarget(
        string AttributeName, CalibrationObserver Observer, int ColumnIndex, string? Predicate, UnknownValuePolicy Policy);

    /// <summary>
    /// One calibration invocation's mutable state: the spool workspace and its storage ledger,
    /// the budget shared across count-sensitive accumulators, and the diagnostics. Created per
    /// call — never on <see cref="GroupingOptions"/>, which is immutable configuration — so
    /// concurrent calibrations cannot cross-contaminate (the D-082 posture).
    /// </summary>
    private sealed class CalibrationRun
    {
        private readonly ResolvedSpec _resolved;
        private readonly ICalibrationObserver? _observer;
        private readonly CancellationToken _cancellationToken;
        private readonly SpoolWorkspace<ValueCount> _workspace;

        // Owned by the run, not by Finish: an attribute's diagnostics are produced one at a time,
        // and a LATER attribute's merge/replay failure must not discard an earlier one's (P-14 —
        // aggregating operations collect every diagnostic, not just the fatal one).
        private readonly List<BedrockDiagnostic> _diagnostics = [];
        private CalibrationBudget? _budget;

        public CalibrationRun(
            ResolvedSpec resolved, GroupingOptions options, ICalibrationObserver? observer, CancellationToken cancellationToken)
        {
            _resolved = resolved;
            Options = options;
            _observer = observer;
            _cancellationToken = cancellationToken;
            GroupingReports = new GroupingReports();

            // Lazy by construction: a calibration that never spills touches no disk at all, even
            // under an unusable temp root. The pending-deletion cap is the calibration
            // workspace's alone (P-1: the emit path keeps its existing uncapped semantics).
            _workspace = new SpoolWorkspace<ValueCount>(
                options, ValueCountCodec.Instance, GroupingReports, QuantileAccumulator.MaxPendingDeletions(options.MaxMergeFanIn));
        }

        public GroupingOptions Options { get; }

        public GroupingReports GroupingReports { get; }

        /// <summary>
        /// The included attributes needing a data pass, in spec-attribute order. Cut discretizers
        /// ignore <c>declared_domain</c> (§10.3), so the discovery-class and cut classes never
        /// overlap and every attribute has exactly one observer — which is what keeps a value
        /// from being tallied twice across the triple passes.
        /// </summary>
        public List<CalibrationTarget> BuildTargets(bool wide)
        {
            // The budget is divided across the count-sensitive attributes, so they must be counted
            // before any accumulator is sized.
            var countSensitive = 0;
            foreach (var attribute in _resolved.Spec.Attributes)
            {
                if (attribute.Include && IsCountSensitive(attribute.Discretizer))
                {
                    countSensitive++;
                }
            }

            _budget = new CalibrationBudget(Options.MaxBufferedBytes, countSensitive, _observer);

            var targets = new List<CalibrationTarget>();
            foreach (var attribute in _resolved.Spec.Attributes)
            {
                if (!attribute.Include)
                {
                    continue;
                }

                var observer = BuildObserver(attribute);
                if (observer is null)
                {
                    continue;
                }

                var columnIndex = wide && attribute.Source is ColumnSource column ? column.Index : -1;
                var predicate = !wide && attribute.Source is PredicateSource predicateSource ? predicateSource.Predicate : null;
                targets.Add(new CalibrationTarget(attribute.Name, observer, columnIndex, predicate, attribute.UnknownValuePolicy));
            }

            return targets;
        }

        /// <summary>
        /// Assembles the calibration outcomes and their mode-triggered warnings (spec-attribute
        /// order, zero counts included), then hands them to <c>CalibratedSpec.Create</c> and
        /// merges its diagnostics. A structural error aborts before any outcome (D-095/D-099).
        /// Returns the calibrated state, or <see langword="null"/> when the run failed; every
        /// diagnostic accrues to <see cref="_diagnostics"/> as it is produced, so a later
        /// attribute's failure cannot discard an earlier one's (P-14).
        /// </summary>
        public CalibratedSpec? Finish(List<CalibrationTarget> targets, BedrockDiagnostic? structural)
        {
            if (structural is { } error)
            {
                Fail(error);
                return null;
            }

            var diagnostics = _diagnostics;
            var outcomes = new List<AttributeCalibration>(targets.Count);

            // Phase 1: end intake everywhere BEFORE any post-intake merge, so a merge never runs
            // alongside the accumulator state it replaced. Online consolidations during intake are
            // the sole exception, and they are already done by now.
            foreach (var target in targets)
            {
                target.Observer.EndIntake();
            }

            _budget?.ReportAggregate();

            // Phase 2: finalize one attribute at a time — merges are sequential, so at most one
            // attribute's readers/writer exist at once.
            foreach (var target in targets)
            {
                switch (target.Observer)
                {
                    case DomainObserver domain:
                        FinishDomain(target, domain, outcomes, diagnostics);
                        break;

                    case MinMaxObserver minMax:
                        FinishMinMax(target, minMax, outcomes, diagnostics);
                        break;

                    case QuantileObserver quantile:
                        FinishQuantile(target, quantile, outcomes, diagnostics);
                        break;

                    case PassthroughObserver passthrough:
                        FinishPassthrough(target, passthrough, outcomes, diagnostics);
                        break;
                }

                // §11.5/D-100/G-4: numeric values excluded from the population because they are
                // present-but-unparseable are reported here as this phase's own aggregated
                // SourceValueUnparseable, at the severity unknown_value_policy selects (skip silent).
                var unparseable = target.Observer.Unparseable;
                if (unparseable.Count > 0 && SeverityFor(target.Policy) is { } severity)
                {
                    diagnostics.Add(new BedrockDiagnostic(
                        DiagnosticCode.SourceValueUnparseable, severity,
                        $"Attribute '{target.AttributeName}' had {unparseable.Count} present-but-unparseable numeric value(s) during calibration (e.g. {unparseable.Sample}); they were excluded from the calibration population (§11.5).",
                        new DiagnosticLocation(AttributeName: target.AttributeName)));
                }
            }

            // An Error here (a fail-policy unparseable, or an attribute whose population could not
            // bound its cuts) aborts with no calibrated result (D-095/D-100) — and it must abort
            // BEFORE Create, since a target that produced no outcome would otherwise reach the
            // completeness boundary as a contract violation rather than the data error it is.
            if (HasError(diagnostics))
            {
                return null;
            }

            var created = CalibratedSpec.Create(_resolved, outcomes);
            diagnostics.AddRange(created.Diagnostics);

            return created.TryGetValue(out var calibrated) && !HasError(diagnostics) ? calibrated : null;
        }

        /// <summary>
        /// Records <paramref name="diagnostic"/> as the failure that stopped the run, after
        /// whatever earlier attributes already diagnosed.
        /// </summary>
        public void Fail(BedrockDiagnostic diagnostic) => _diagnostics.Add(diagnostic);

        /// <summary>Releases every accumulator's retained state and tears the workspace down (non-throwing).</summary>
        public void Cleanup() => _workspace.Cleanup();

        /// <summary>
        /// Snapshots the storage ledger onto the accumulated diagnostics and builds the result.
        /// Called <b>after</b> <see cref="Cleanup"/>, so a failure confined to workspace teardown is
        /// still reported rather than being recorded after the value was already built (D-095).
        /// The ledger flushes last, in first-occurrence order (the existing rule): a cleanup-only
        /// failure is a Warning and calibration may still succeed; an in-path one is an Error that
        /// fails the result even if the walk itself completed.
        /// </summary>
        public Diagnosed<CalibratedSpec> Complete(CalibratedSpec? calibrated)
        {
            foreach (var (failure, severity) in GroupingReports.Aggregates())
            {
                _diagnostics.Add(GroupingStorageDiagnostics.Render(failure, severity));
            }

            return calibrated is not null && !HasError(_diagnostics)
                ? Diagnosed<CalibratedSpec>.Ok(calibrated, _diagnostics)
                : Diagnosed<CalibratedSpec>.Failed(_diagnostics);
        }

        // The observer one attribute's calibration needs, or null when it needs no data pass.
        private CalibrationObserver? BuildObserver(AttributeSpec attribute)
        {
            switch (attribute.Discretizer)
            {
                case IdentityDiscretizer or FreePerValueDiscretizer:
                {
                    var absentDomain = attribute.DeclaredDomain.Count == 0;
                    var include = attribute.UnknownValuePolicy == UnknownValuePolicy.Include;
                    if (!absentDomain && !include)
                    {
                        return null;
                    }

                    var numeric = attribute.Discretizer is FreePerValueDiscretizer { ValueType: SourceValueType.Number };
                    var culture = attribute.Discretizer is FreePerValueDiscretizer freePerValue
                        ? freePerValue.Culture
                        : CultureInfo.InvariantCulture;

                    var observer = new DomainObserver(isInclude: !absentDomain && include, numeric, culture);
                    if (observer.IsInclude)
                    {
                        // The explicit domain is already canonical for a numeric free_per_value (D-096),
                        // so seeding it verbatim matches the canonical keys observed values normalize to.
                        observer.Seed(attribute.DeclaredDomain);
                    }

                    return observer;
                }

                case CalibrationPending { Config: PendingEqualWidth { Range: EqualWidthRange.MinMax } config } pending:
                    return new MinMaxObserver(config, pending.Culture);

                case CalibrationPending { Config: PendingEqualWidth { Range: EqualWidthRange.PercentileP1P99 } config } pending:
                    return new QuantileObserver(NewAccumulator(attribute.Name, pending.Culture), percentile: config, equalFrequency: null);

                case CalibrationPending { Config: PendingEqualFrequency config } pending:
                    return new QuantileObserver(NewAccumulator(attribute.Name, pending.Culture), percentile: null, equalFrequency: config);

                case CalibrationPending { Config: PendingValueGroupsPassthrough config }:
                    return new PassthroughObserver(config.Groups);

                case CalibrationPending pending:
                    // equal_width range = "manual" is spec-determined and never pends, so a
                    // pending variant with no observer is a corrupted carrier (D-093).
                    throw new InvalidOperationException(
                        $"'{pending.Kind}' calibration is not implemented in this milestone; it lands with its own M4 slice (D-093).");

                default:
                    return null;
            }
        }

        private QuantileAccumulator NewAccumulator(string attributeName, CultureInfo culture) =>
            new(attributeName, culture, _budget!, _workspace, Options, _observer, _cancellationToken);

        // Discovery-class outcomes and their mode-triggered warnings: they fire whenever the mode
        // executes, zero discoveries included — the data-dependence exists regardless of the count.
        private static void FinishDomain(
            CalibrationTarget target, DomainObserver observer, List<AttributeCalibration> outcomes, List<BedrockDiagnostic> diagnostics)
        {
            var values = observer.Values;
            if (observer.IsInclude)
            {
                outcomes.Add(new IncludeAdditions(target.AttributeName, values));
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.UnknownValuePolicyInclude, DiagnosticSeverity.Warning,
                    $"Attribute '{target.AttributeName}' extended its declared_domain with {values.Count} observed value(s) under unknown_value_policy = \"include\"; schema_fingerprint is data-dependent (§10.6).",
                    new DiagnosticLocation(AttributeName: target.AttributeName)));
                return;
            }

            outcomes.Add(new ObservedDomain(target.AttributeName, values));
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.ObservedDomainUsed, DiagnosticSeverity.Warning,
                $"Attribute '{target.AttributeName}' had no declared_domain; it was calibrated from {values.Count} observed value(s), so the schema depends on this input (§10.3).",
                new DiagnosticLocation(AttributeName: target.AttributeName)));
        }

        // §11.6/D-055/D-090: the pass-through bins resolve once the pass completes. Like the other
        // discovery-class outcomes this warning is mode-triggered — it fires whenever passthrough
        // calibration executes, ZERO discoveries included, because the column set depends on this
        // input either way. An empty outcome is retained, not skipped: it is the legitimate
        // zero-discovery completeness marker CalibratedSpec.Create requires, and dropping it would
        // read as a skipped calibration.
        //
        // There is deliberately no data-insufficiency guard: unlike a cut discretizer, whose bins
        // need a span or enough distinct values, value_groups' declared groups already stand on
        // their own — discovering no ungrouped value means every value matched a group, which is a
        // perfectly good outcome, not a failure.
        private static void FinishPassthrough(
            CalibrationTarget target, PassthroughObserver observer, List<AttributeCalibration> outcomes, List<BedrockDiagnostic> diagnostics)
        {
            var values = observer.Values;
            outcomes.Add(new PassthroughBins(target.AttributeName, values));
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.ValueGroupsPassthroughDataDependent, DiagnosticSeverity.Warning,
                $"Attribute '{target.AttributeName}' uses value_groups with unmatched = \"passthrough\"; {values.Count} ungrouped value(s) were discovered and became bins, so the column set depends on this input (§11.6).",
                new DiagnosticLocation(AttributeName: target.AttributeName)));
        }

        // §11.4/D-089/D-102: the equal_width data range resolves once the pass completes. A
        // population with no usable spread cannot bound the span — no usable numeric values at all,
        // or every value equal — and is CalibrationDataInsufficient (Error, no calibrated result).
        // There is deliberately NO distinct-value guard: equal-width bins are placed by span, not by
        // count, so fewer distinct values than bins is valid as long as min < max.
        private static void FinishMinMax(
            CalibrationTarget target, MinMaxObserver observer, List<AttributeCalibration> outcomes, List<BedrockDiagnostic> diagnostics)
        {
            if (!observer.HasValues)
            {
                diagnostics.Add(NoSpread(target, "equal_width with a data-derived range", "the calibration population has no usable finite numeric value to bound it", "§11.4"));
                return;
            }

            if (observer.Min == observer.Max)
            {
                diagnostics.Add(NoSpread(
                    target, "equal_width with a data-derived range",
                    $"every usable value in the calibration population is {CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(observer.Min))}; the range has no spread", "§11.4"));
                return;
            }

            DeriveEqualWidth(target, observer.Config, observer.Min, observer.Max, observer.Culture, outcomes, diagnostics);
        }

        // §11.5/§11.4 (D-088/D-089/D-103): the count-sensitive cuts resolve once the pass
        // completes — merge to one consolidated run, then the two-pass walk over it.
        private static void FinishQuantile(
            CalibrationTarget target, QuantileObserver observer, List<AttributeCalibration> outcomes, List<BedrockDiagnostic> diagnostics)
        {
            var accumulator = observer.Accumulator;
            try
            {
                accumulator.PrepareReplay();

                if (observer.EqualFrequency is { } equalFrequency)
                {
                    var cuts = accumulator.TryExtractEqualFrequencyCuts(equalFrequency, out var distinct);
                    if (cuts is null)
                    {
                        // §11.5: fewer distinct values than bins stops rather than silently
                        // producing fewer bins. Unlike equal_width, count-placed bins genuinely
                        // cannot exist without enough distinct values to separate them.
                        diagnostics.Add(NoSpread(
                            target, "equal_frequency",
                            $"the calibration population has {distinct} distinct usable value(s), fewer than the {equalFrequency.Bins} bins requested", "§11.5"));
                        return;
                    }

                    outcomes.Add(new CalibratedCuts(target.AttributeName, cuts));
                    return;
                }

                var percentile = observer.Percentile!;
                if (!accumulator.TryExtractPercentileSpan(out var p1, out var p99))
                {
                    diagnostics.Add(NoSpread(
                        target, "equal_width with range = \"percentile_p1_p99\"",
                        accumulator.Total == 0
                            ? "the calibration population has no usable finite numeric value to bound it"
                            : $"the 1st and 99th percentiles are both {CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(p1))}; the span has no spread",
                        "§11.4"));
                    return;
                }

                // The percentile span feeds the SAME equal-width derivation boundary as min_max
                // (D-102): precision is applied after the span is selected, and there is no second
                // copy of the interpolation formula anywhere.
                DeriveEqualWidth(target, percentile, p1, p99, observer.Culture, outcomes, diagnostics);
            }
            finally
            {
                // The consolidated run is retained until cut extraction completes, then deleted
                // through the established cleanup channel.
                accumulator.Release();
            }
        }

        // The one equal_width derivation route for BOTH data-derived ranges: Core's public
        // CreateManual factory over the calibrated span. Only the cuts are kept — the instance is
        // a throwaway whose range mode is irrelevant — and CalibratedSpec.Create builds the real
        // discretizer, preserving the authored data-derived range and its absent vmin/vmax
        // (D-094). The span gate above means CreateManual's own range diagnostic is unreachable,
        // so its only possible failure is a cut collapse, which this phase owns as
        // CalibrationCutsInvalid (D-088/D-089).
        private static void DeriveEqualWidth(
            CalibrationTarget target,
            PendingEqualWidth config,
            double min,
            double max,
            CultureInfo culture,
            List<AttributeCalibration> outcomes,
            List<BedrockDiagnostic> diagnostics)
        {
            var derived = EqualWidthDiscretizer.CreateManual(config.Bins, min, max, config.Precision, culture);
            if (!derived.TryGetValue(out var discretizer))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.CalibrationCutsInvalid, DiagnosticSeverity.Error,
                    $"Attribute '{target.AttributeName}' uses equal_width with a data-derived range, but the calibrated span " +
                    $"[{CanonicalNumber.Format(min)}, {CanonicalNumber.Format(max)}] cannot be divided into " +
                    $"{config.Bins} distinct bins at this precision (§11.4).",
                    new DiagnosticLocation(AttributeName: target.AttributeName)));
                return;
            }

            outcomes.Add(new CalibratedCuts(target.AttributeName, discretizer.Cuts));
        }

        private static BedrockDiagnostic NoSpread(CalibrationTarget target, string what, string why, string section) =>
            new(DiagnosticCode.CalibrationDataInsufficient, DiagnosticSeverity.Error,
                $"Attribute '{target.AttributeName}' uses {what}, but {why} ({section}).",
                new DiagnosticLocation(AttributeName: target.AttributeName));

        private static bool IsCountSensitive(Discretizer? discretizer) => discretizer is CalibrationPending
        {
            Config: PendingEqualFrequency or PendingEqualWidth { Range: EqualWidthRange.PercentileP1P99 },
        };

        // §10.6/§16.4: the severity unknown_value_policy assigns an aggregated SourceValueUnparseable —
        // skip silent (null), fail Error, warn/include Warning (an unparseable value cannot join a
        // numeric domain, so include behaves as warn, D-097).
        private static DiagnosticSeverity? SeverityFor(UnknownValuePolicy policy) => policy switch
        {
            UnknownValuePolicy.Skip => null,
            UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
            _ => DiagnosticSeverity.Warning,
        };

        private static bool HasError(List<BedrockDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // What one attribute accumulates over the calibration pass. Missing values never reach an
    // observer (they are handled before calibration); a present-but-unparseable/non-finite
    // numeric value is excluded from the population and tallied for this phase's own aggregated
    // SourceValueUnparseable (§11.5/D-100).
    private abstract class CalibrationObserver
    {
        public DiagnosticTally Unparseable { get; } = new();

        /// <summary>
        /// Whether this observer's result depends on <b>how many</b> observations carry a value,
        /// not merely which values occur. Count-sensitive observers are the only ones that need
        /// the §5.3.1 subject-local triple deduplication — and therefore the only reason an
        /// unordered triple source is read a second time.
        /// </summary>
        public virtual bool IsCountSensitive => false;

        public abstract void Observe(string? raw);

        /// <summary>Ends intake; the default needs no phase split (nothing is retained beyond the result).</summary>
        public virtual void EndIntake()
        {
        }
    }

    // Accumulates the distinct non-missing observed values for one attribute in
    // first-observation order (ordinal dedup, P-12). Bounded by the attribute vocabulary —
    // schema-scale metadata, documented and not budget-gated (P-16, D-095). In numeric mode
    // (numeric free_per_value, D-096) each present value is parsed under the injected culture and
    // reduced to its canonical numeric identity before dedup, so equivalent spellings occupy one
    // bin at their first occurrence.
    private sealed class DomainObserver(bool isInclude, bool numeric, CultureInfo culture) : CalibrationObserver
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];

        public bool IsInclude { get; } = isInclude;

        public IReadOnlyList<string> Values => _values;

        // For include mode: seed the declared domain so only genuinely-new values are additions.
        // The domain is already canonical for a numeric free_per_value (D-096).
        public void Seed(IReadOnlyList<string> domain)
        {
            foreach (var value in domain)
            {
                _seen.Add(value);
            }
        }

        public override void Observe(string? raw)
        {
            if (raw is null)
            {
                return; // missing values are handled before calibration; never observed here.
            }

            string key;
            if (numeric)
            {
                if (!CanonicalNumber.TryParse(raw, culture, out var value))
                {
                    Unparseable.Record(raw);
                    return;
                }

                key = CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(value));
            }
            else
            {
                key = raw;
            }

            if (_seen.Add(key))
            {
                _values.Add(key);
            }
        }
    }

    // The value_groups pass-through bin set (§7/§11.6, D-055/D-090/D-095): the distinct raw
    // spellings that matched NO declared group, in first-observation order.
    //
    // Discovery-class, not count-sensitive: a bin either exists or it does not, so how MANY times
    // a value occurs is irrelevant and the set is idempotent under repetition. That is what keeps
    // it off the count-sensitive path entirely — no quantile accumulator, no value counts, no
    // spill runs, no merge or replay, no subject-local triple deduplication, and no contribution
    // to the budget divisor. Its bound is the attribute vocabulary — schema-scale metadata, the
    // same documented P-16 carve-out as an observed domain (D-095), not a budget-gated
    // population.
    //
    // Matching is delegated to Core rather than reimplemented: the observer classifies each value
    // through a ValueGroupsDiscretizer built over the SAME authored groups under the `skip`
    // policy, whose BinResult answers exactly the question discovery asks — a Bin means some group
    // claimed the value, an Unknown means none did (§11.6). So calibration and emit cannot drift
    // about what "unmatched" means: it is literally the same type, matcher, and first-match walk,
    // with each group's regex compiled once at construction rather than per observed value.
    //
    // The probe instance is a throwaway whose own unmatched policy is irrelevant — only its
    // matched-vs-not answer is read — the same shape as DeriveEqualWidth building a throwaway
    // CreateManual instance purely for its cuts (D-102). `skip` is the policy that makes "no group
    // matched" observable; `other`/`passthrough` would bin it and hide the answer.
    private sealed class PassthroughObserver : CalibrationObserver
    {
        private readonly ValueGroupsDiscretizer _probe;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];

        public PassthroughObserver(IReadOnlyList<ValueGroup> groups) =>
            // Cannot throw on a resolved spec: the seam diagnoses duplicate labels
            // (ValueGroupsLabelDuplicate) and the ResolvedSpec trust boundary re-checks them on
            // the pending carrier, so reaching here with a duplicate is corrupt Core state.
            _probe = ValueGroupsDiscretizer.Create(groups, ValueGroupsUnmatched.Skip);

        public IReadOnlyList<string> Values => _values;

        public override void Observe(string? raw)
        {
            if (raw is null)
            {
                return; // missing values are handled before calibration; never a passthrough bin.
            }

            if (_probe.Discretize(raw).Outcome != BinOutcome.Unknown)
            {
                return; // some group claimed it, so it is grouped — not a pass-through bin.
            }

            // Ordinal dedup (P-12); §17 rule 3 fixes the order as first-observation order, which
            // for triple input is RAW input order — hence discovery lives on the raw pass.
            if (_seen.Add(raw))
            {
                _values.Add(raw);
            }
        }
    }

    // The equal_width data range (§7/§11.4): a streaming minimum and maximum over the finite
    // parsed population. It retains two doubles and never the population — no sort, no spill, no
    // distinct-value tracking (D-095's bounded-memory rule is satisfied by construction here;
    // the count-sensitive quantile machinery belongs to equal_frequency/percentile). Order- and
    // count-insensitive, which is why a triple min/max pass needs no subject-local deduplication:
    // a repeated (subject, predicate, value) cannot move a min or a max.
    private sealed class MinMaxObserver(PendingEqualWidth config, CultureInfo culture) : CalibrationObserver
    {
        public PendingEqualWidth Config { get; } = config;

        // The resolved parsing culture, carried so cut derivation re-homes onto the same one the
        // population was read under (P-11).
        public CultureInfo Culture { get; } = culture;

        public bool HasValues { get; private set; }

        public double Min { get; private set; } = double.PositiveInfinity;

        public double Max { get; private set; } = double.NegativeInfinity;

        public override void Observe(string? raw)
        {
            if (raw is null)
            {
                return;
            }

            // §7: a numeric value contributes only when it parses to a FINITE number under
            // binding.locale; anything else is excluded and never influences a cut (§11.5).
            if (!CanonicalNumber.TryParse(raw, Culture, out var value))
            {
                Unparseable.Record(raw);
                return;
            }

            HasValues = true;
            if (value < Min)
            {
                Min = value;
            }

            if (value > Max)
            {
                Max = value;
            }
        }
    }

    // The count-sensitive population (§11.5 equal_frequency, §11.4 percentile_p1_p99): exactly
    // one of the two configs is non-null. Both need the same aggregated (value, count) population
    // and differ only in what they extract from it, so they share one accumulator rather than two
    // near-identical engines (P-5).
    private sealed class QuantileObserver : CalibrationObserver
    {
        public QuantileObserver(QuantileAccumulator accumulator, PendingEqualWidth? percentile, PendingEqualFrequency? equalFrequency)
        {
            Accumulator = accumulator;
            Percentile = percentile;
            EqualFrequency = equalFrequency;
        }

        public QuantileAccumulator Accumulator { get; }

        public PendingEqualWidth? Percentile { get; }

        public PendingEqualFrequency? EqualFrequency { get; }

        public CultureInfo Culture => Accumulator.Culture;

        public override bool IsCountSensitive => true;

        public override void Observe(string? raw)
        {
            if (raw is null)
            {
                return;
            }

            Accumulator.Observe(raw, Unparseable);
        }

        public override void EndIntake() => Accumulator.EndIntake();
    }
}
