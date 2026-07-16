using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// The Calibrate phase (§7 phase 2, D-036/D-093): reads data to resolve
/// data-dependent schema elements and produces the retained, immutable
/// <see cref="CalibratedSpec"/> that Plan consumes without re-derivation. In slice
/// A the only calibration is discovery-class: filling an absent
/// <c>declared_domain</c> from the observed domain (<c>ObservedDomainUsed</c>) and
/// extending an explicit domain under <c>unknown_value_policy = "include"</c>
/// (<c>UnknownValuePolicyInclude</c>). Auto-discretizer cut calibration and
/// <c>value_groups</c> passthrough land with the later M4 slices.
/// <para>
/// A fully-declared spec skips the data pass: after the pairing guard the fast path
/// returns <see cref="CalibratedSpec.FromFullyDeclared"/> without enumerating rows.
/// All binding range checks are seam-owned (G-1); the calibrator emits no binding
/// diagnostics.
/// </para>
/// <para>
/// M4 Slice B (D-101) adds <c>free_per_value</c> to the discovery-class calibration:
/// a numeric <c>free_per_value</c> observes its values as <b>canonical numeric
/// identities</b> (§11.3/D-096, so <c>90</c>/<c>90.0</c>/<c>9e1</c> contribute one bin at
/// their first occurrence and every zero spelling collapses to <c>0</c>), and a
/// present-but-unparseable/non-finite numeric value is excluded from the population and
/// reported as an aggregated <c>SourceValueUnparseable</c> at the severity
/// <c>unknown_value_policy</c> selects — its own per-phase aggregate (D-100/G-4).
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
    public static async ValueTask<Diagnosed<CalibratedSpec>> CalibrateAsync(
        ResolvedSpec resolved, IRecordSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(source);
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

        var targets = BuildTargets(resolved.Spec, wide: true);

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var target in targets)
            {
                target.Observer.Observe(record.Field(target.ColumnIndex));
            }
        }

        return Finish(resolved, targets, structural: null);
    }

    /// <summary>
    /// Calibrates a triple resolved spec over its source (raw-order discovery pass).
    /// Enforces the G-3 triple structural checks — a structurally unusable subject
    /// (<c>ObjectKeyValueInvalid</c>) halts any read, and non-contiguity
    /// (<c>TripleSubjectNotContiguous</c>) halts a <c>subject_grouped</c> read — with the
    /// same codes/severities as emit; a structural Error yields no calibrated result.
    /// Requires a triple resolution.
    /// </summary>
    public static async ValueTask<Diagnosed<CalibratedSpec>> CalibrateTripleAsync(
        ResolvedSpec resolved, ITripleRowSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(source);
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

        var targets = BuildTargets(resolved.Spec, wide: false);
        var byPredicate = new Dictionary<string, List<CalibrationObserver>>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!byPredicate.TryGetValue(target.Predicate!, out var list))
            {
                list = [];
                byPredicate[target.Predicate!] = list;
            }

            list.Add(target.Observer);
        }

        var subjectGrouped = resolved.Settings.Ordering == TripleOrdering.SubjectGrouped;
        var completed = new HashSet<string>(StringComparer.Ordinal);
        string? currentSubject = null;
        var started = false;

        await foreach (var row in source.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            // G-3: an unusable triple subject halts any calibration read (§5.4/§16.4).
            if (!ObjectNames.IsUsable(row.Subject))
            {
                return Finish(resolved, targets, structural: new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
                    $"The triple subject at record {row.RecordIndex} is empty, whitespace-only, a missing token, or contains a control character; it cannot name an object (§5.4).",
                    new DiagnosticLocation(RecordIndex: row.RecordIndex)));
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
                        return Finish(resolved, targets, structural: new BedrockDiagnostic(
                            DiagnosticCode.TripleSubjectNotContiguous, DiagnosticSeverity.Error,
                            $"Triple subject '{subject}' recurs at record {row.RecordIndex} after an intervening subject; ordering = \"subject_grouped\" requires contiguous subjects (§5.3).",
                            new DiagnosticLocation(RecordIndex: row.RecordIndex)));
                    }

                    currentSubject = subject;
                }
            }

            // Discovery-class observation on the raw stream (§11): set-idempotent, so no
            // subject-local dedup is applied; first-observation order is raw input order.
            if (row.Predicate is { } predicate && byPredicate.TryGetValue(predicate, out var observers))
            {
                foreach (var observer in observers)
                {
                    observer.Observe(row.Value);
                }
            }
        }

        return Finish(resolved, targets, structural: null);
    }

    private static void RequireSchema(ResolvedSpec resolved)
    {
        if (resolved.Schema is null)
        {
            throw new ArgumentException("calibration requires a schema-aware resolution; resolved.Schema is null.", nameof(resolved));
        }
    }

    // The included attributes needing a data pass, in spec-attribute order — two classes:
    //
    //  * discovery-class (identity / free_per_value, D-101): an absent domain calibrates its
    //    observed domain (any policy), an explicit domain under include extends it. A numeric
    //    free_per_value observes canonical numeric identities and tallies unparseable values
    //    (D-096/D-100); every other case observes verbatim strings.
    //  * auto-cut (a CalibrationPending carrier, D-093): equal_width's data-derived range
    //    observes a streaming min/max over the finite parsed population (§7/§11.4, D-102).
    //
    // Cut discretizers ignore declared_domain (§10.3), so the two classes never overlap.
    private static List<CalibrationTarget> BuildTargets(BedrockSpec spec, bool wide)
    {
        var targets = new List<CalibrationTarget>();
        foreach (var attribute in spec.Attributes)
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

    // The observer one attribute's calibration needs, or null when it needs no data pass.
    private static CalibrationObserver? BuildObserver(AttributeSpec attribute)
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

            case CalibrationPending { Config: PendingEqualWidth config } pending:
                // §11.4/D-089: min_max is the only data-derived range this milestone executes.
                // percentile_p1_p99 is modelled in Core but has no TOML spelling until its
                // calibration lands (D-102), so no authored spec can reach this — a hand-built
                // one that does is a mis-sequenced call, not user input (P-14).
                if (config.Range != EqualWidthRange.MinMax)
                {
                    throw new InvalidOperationException(
                        $"equal_width range '{config.Range}' calibration is not implemented in this milestone; " +
                        "percentile_p1_p99 lands at M4 Slice D, and the reader accepts only \"min_max\" and \"manual\" (§11.4/D-089).");
                }

                return new MinMaxObserver(config, pending.Culture);

            case CalibrationPending pending:
                throw new InvalidOperationException(
                    $"'{pending.Kind}' calibration is not implemented in this milestone; it lands with its own M4 slice (D-093).");

            default:
                return null;
        }
    }

    // Assembles the calibration outcomes and their mode-triggered warnings (spec-attribute
    // order, zero counts included), then hands them to CalibratedSpec.Create and merges its
    // diagnostics. A structural error aborts before any outcome (no calibrated result, D-095).
    private static Diagnosed<CalibratedSpec> Finish(
        ResolvedSpec resolved, List<CalibrationTarget> targets, BedrockDiagnostic? structural)
    {
        if (structural is { } error)
        {
            return Diagnosed<CalibratedSpec>.Failed([error]);
        }

        var diagnostics = new List<BedrockDiagnostic>();
        var outcomes = new List<AttributeCalibration>(targets.Count);
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
            }

            // §11.5/D-100/G-4: numeric values excluded from the calibration population because they
            // are present-but-unparseable are reported here as this phase's own aggregated
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
            return Diagnosed<CalibratedSpec>.Failed(diagnostics);
        }

        var created = CalibratedSpec.Create(resolved, outcomes);
        diagnostics.AddRange(created.Diagnostics);

        return created.TryGetValue(out var calibrated) && !HasError(diagnostics)
            ? Diagnosed<CalibratedSpec>.Ok(calibrated, diagnostics)
            : Diagnosed<CalibratedSpec>.Failed(diagnostics);
    }

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

    // §11.4/D-089/D-102: the equal_width data range resolves once the pass completes. A
    // population with no usable spread cannot bound the span — no usable numeric values at all,
    // or every value equal — and is CalibrationDataInsufficient (Error, no calibrated result).
    // There is deliberately NO distinct-value guard: equal-width bins are placed by span, not by
    // count, so fewer distinct values than bins is valid as long as min < max.
    //
    // The cuts come from Core's ONE derivation boundary — EqualWidthDiscretizer.CreateManual,
    // invoked over the observed span. That is what makes an auto spec and its calibrate-frozen
    // manual_cuts form carry the same numbers by construction (D-088) rather than by two copies of
    // the formula agreeing. Only the cuts are kept: the instance is a throwaway (its range mode is
    // irrelevant here), and CalibratedSpec.Create builds the real discretizer, which preserves the
    // authored data-derived range and its absent vmin/vmax (D-094). The span gate above means
    // CreateManual's own range diagnostic is unreachable, so its only possible failure is a cut
    // collapse — which this phase owns as CalibrationCutsInvalid (D-088/D-089).
    private static void FinishMinMax(
        CalibrationTarget target, MinMaxObserver observer, List<AttributeCalibration> outcomes, List<BedrockDiagnostic> diagnostics)
    {
        if (!observer.HasValues)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.CalibrationDataInsufficient, DiagnosticSeverity.Error,
                $"Attribute '{target.AttributeName}' uses equal_width with a data-derived range, but the calibration population has no usable finite numeric value to bound it (§11.4).",
                new DiagnosticLocation(AttributeName: target.AttributeName)));
            return;
        }

        if (observer.Min == observer.Max)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.CalibrationDataInsufficient, DiagnosticSeverity.Error,
                $"Attribute '{target.AttributeName}' uses equal_width with a data-derived range, but every usable value in the calibration population is {CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(observer.Min))}; the range has no spread (§11.4).",
                new DiagnosticLocation(AttributeName: target.AttributeName)));
            return;
        }

        var derived = EqualWidthDiscretizer.CreateManual(
            observer.Config.Bins, observer.Min, observer.Max, observer.Config.Precision, observer.Culture);
        if (!derived.TryGetValue(out var discretizer))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.CalibrationCutsInvalid, DiagnosticSeverity.Error,
                $"Attribute '{target.AttributeName}' uses equal_width with a data-derived range, but the calibrated span " +
                $"[{CanonicalNumber.Format(observer.Min)}, {CanonicalNumber.Format(observer.Max)}] cannot be divided into " +
                $"{observer.Config.Bins} distinct bins at this precision (§11.4).",
                new DiagnosticLocation(AttributeName: target.AttributeName)));
            return;
        }

        outcomes.Add(new CalibratedCuts(target.AttributeName, discretizer.Cuts));
    }

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

    // One attribute's calibration read location (wide column index or triple predicate), its
    // observer, and the unknown_value_policy that severities an unparseable aggregate (D-100).
    private sealed record CalibrationTarget(
        string AttributeName, CalibrationObserver Observer, int ColumnIndex, string? Predicate, UnknownValuePolicy Policy);

    // What one attribute accumulates over the calibration pass. Missing values never reach an
    // observer (they are handled before calibration); a present-but-unparseable/non-finite
    // numeric value is excluded from the population and tallied for this phase's own aggregated
    // SourceValueUnparseable (§11.5/D-100).
    private abstract class CalibrationObserver
    {
        public DiagnosticTally Unparseable { get; } = new();

        public abstract void Observe(string? raw);
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

    // The equal_width data range (§7/§11.4): a streaming minimum and maximum over the finite
    // parsed population. It retains two doubles and never the population — no sort, no spill, no
    // distinct-value tracking (D-095's bounded-memory rule is satisfied by construction here;
    // the count-sensitive quantile machinery belongs to equal_frequency's slice). Order-insensitive
    // and count-insensitive, which is why a triple pass needs no subject-local deduplication: a
    // repeated (subject, predicate, value) cannot move a min or a max.
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
}
