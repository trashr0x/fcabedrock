using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
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

    // The included attributes needing discovery-class calibration, in spec-attribute
    // order. Slice A: identity is the only discretizer that consumes the declared domain;
    // free_per_value joins at slice B. An absent domain calibrates observed (any policy);
    // an explicit domain under include extends it.
    private static List<CalibrationTarget> BuildTargets(BedrockSpec spec, bool wide)
    {
        var targets = new List<CalibrationTarget>();
        foreach (var attribute in spec.Attributes)
        {
            if (!attribute.Include || attribute.Discretizer is not IdentityDiscretizer)
            {
                continue;
            }

            var absentDomain = attribute.DeclaredDomain.Count == 0;
            var include = attribute.UnknownValuePolicy == UnknownValuePolicy.Include;
            if (!absentDomain && !include)
            {
                continue;
            }

            var observer = new CalibrationObserver(isInclude: !absentDomain && include);
            if (observer.IsInclude)
            {
                observer.Seed(attribute.DeclaredDomain);
            }

            var columnIndex = wide && attribute.Source is ColumnSource column ? column.Index : -1;
            var predicate = !wide && attribute.Source is PredicateSource predicateSource ? predicateSource.Predicate : null;
            targets.Add(new CalibrationTarget(attribute.Name, observer, columnIndex, predicate));
        }

        return targets;
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
            var values = target.Observer.Values;
            if (target.Observer.IsInclude)
            {
                outcomes.Add(new IncludeAdditions(target.AttributeName, values));
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.UnknownValuePolicyInclude, DiagnosticSeverity.Warning,
                    $"Attribute '{target.AttributeName}' extended its declared_domain with {values.Count} observed value(s) under unknown_value_policy = \"include\"; schema_fingerprint is data-dependent (§10.6).",
                    new DiagnosticLocation(AttributeName: target.AttributeName)));
            }
            else
            {
                outcomes.Add(new ObservedDomain(target.AttributeName, values));
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObservedDomainUsed, DiagnosticSeverity.Warning,
                    $"Attribute '{target.AttributeName}' had no declared_domain; it was calibrated from {values.Count} observed value(s), so the schema depends on this input (§10.3).",
                    new DiagnosticLocation(AttributeName: target.AttributeName)));
            }
        }

        var created = CalibratedSpec.Create(resolved, outcomes);
        diagnostics.AddRange(created.Diagnostics);
        return created.TryGetValue(out var calibrated)
            ? Diagnosed<CalibratedSpec>.Ok(calibrated, diagnostics)
            : Diagnosed<CalibratedSpec>.Failed(diagnostics);
    }

    // One attribute's calibration read location (wide column index or triple predicate) and
    // its observer.
    private sealed record CalibrationTarget(string AttributeName, CalibrationObserver Observer, int ColumnIndex, string? Predicate);

    // Accumulates the distinct non-missing observed values for one attribute in
    // first-observation order (ordinal dedup, P-12). Bounded by the attribute vocabulary —
    // schema-scale metadata, documented and not budget-gated (P-16, D-095).
    private sealed class CalibrationObserver(bool isInclude)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];

        public bool IsInclude { get; } = isInclude;

        public IReadOnlyList<string> Values => _values;

        // For include mode: seed the declared domain so only genuinely-new values are additions.
        public void Seed(IReadOnlyList<string> domain)
        {
            foreach (var value in domain)
            {
                _seen.Add(value);
            }
        }

        public void Observe(string? raw)
        {
            if (raw is not null && _seen.Add(raw))
            {
                _values.Add(raw);
            }
        }
    }
}
