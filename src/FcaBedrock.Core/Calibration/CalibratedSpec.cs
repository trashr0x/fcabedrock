using System.Collections.Immutable;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Calibration;

/// <summary>
/// The single Plan input (D-093): the <b>effective</b> spec (every included
/// attribute executable — no <see cref="CalibrationPending"/>, no absent consumed
/// domain), the schema snapshot the conversion was prepared against, and the
/// retained calibration outcomes (manifest-ready, §15). Immutable; produced only
/// by the two factories below over a <see cref="ResolvedSpec"/> token, and paired
/// downstream by reference identity of that token (the calibrator, the emitter,
/// and the fingerprint calculator all read <see cref="Resolution"/>).
/// </summary>
public sealed class CalibratedSpec
{
    private CalibratedSpec(
        BedrockSpec spec, ResolvedSpec resolution, SourceSchema schema, ImmutableArray<AttributeCalibration> calibrations)
    {
        Spec = spec;
        Resolution = resolution;
        Schema = schema;
        Calibrations = calibrations;
    }

    /// <summary>The effective, executable spec the planner walks and the output fingerprints hash (D-094).</summary>
    public BedrockSpec Spec { get; }

    /// <summary>The opaque resolution token this state was prepared from (pairing is by reference identity).</summary>
    public ResolvedSpec Resolution { get; }

    /// <summary>The schema snapshot from the token; Plan consumes it and Emit re-validates the live source against it.</summary>
    public SourceSchema Schema { get; }

    /// <summary>The retained calibration outcomes, in spec-attribute order; empty for a fully-declared spec.</summary>
    public IReadOnlyList<AttributeCalibration> Calibrations { get; }

    /// <summary>
    /// Pass-through for a spec fully determined by its own text (§7). Requires a
    /// schema-aware <paramref name="resolved"/> (conversion is schema-aware) and
    /// throws <see cref="ArgumentException"/> when <see cref="RequiresData"/> — a
    /// mis-sequenced call that skipped the calibrator (D-093 programmer-error
    /// posture).
    /// </summary>
    public static CalibratedSpec FromFullyDeclared(ResolvedSpec resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (resolved.Schema is not { } schema)
        {
            throw new ArgumentException(
                "conversion requires a schema-aware resolution; resolved.Schema is null.", nameof(resolved));
        }

        if (RequiresData(resolved.Spec))
        {
            throw new ArgumentException(
                "FromFullyDeclared was called on a data-dependent spec; run the calibrator instead (D-093).", nameof(resolved));
        }

        RequireValidRestrictions(resolved.Spec, nameof(resolved));
        return new CalibratedSpec(resolved.Spec, resolved, schema, ImmutableArray<AttributeCalibration>.Empty);
    }

    /// <summary>
    /// Assembles the effective spec from the authored spec plus the
    /// calibrator-produced outcomes (D-093). Core owns the substitution so Plan and
    /// the fingerprints see one consistent state: an omitted (null) consumed domain plus its
    /// <see cref="ObservedDomain"/> becomes the effective domain, and
    /// <see cref="IncludeAdditions"/> are appended to an authored domain (incl. <c>[]</c>). Returns
    /// <see cref="Diagnosed{T}"/> — data-derived cut invalidity comes back as Error
    /// diagnostics so the calibrator can aggregate them (none at M4 Slice A; the
    /// cut-calibrated discretizers that produce them landed at M4 Slices C/D —
    /// D-102/D-103). Throws
    /// <see cref="ArgumentException"/> only for calibrator-contract mismatches
    /// (programmer error): an outcome naming an unknown/excluded attribute, a
    /// kind-mismatched/duplicate/unexpected outcome, a leftover
    /// <see cref="CalibrationPending"/>, or a required completeness marker that is
    /// missing. Completeness is required per calibration mode: an omitted-domain (null)
    /// consuming attribute must carry exactly one <see cref="ObservedDomain"/> (never
    /// co-occurring with <see cref="IncludeAdditions"/>), and an authored-domain (incl. <c>[]</c>)
    /// consuming attribute under <c>unknown_value_policy = "include"</c> must carry
    /// exactly one <see cref="IncludeAdditions"/> marker (an empty list is the
    /// zero-additions marker).
    /// </summary>
    public static Diagnosed<CalibratedSpec> Create(
        ResolvedSpec resolved, IReadOnlyList<AttributeCalibration> calibrations)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(calibrations);
        if (resolved.Schema is not { } schema)
        {
            throw new ArgumentException(
                "conversion requires a schema-aware resolution; resolved.Schema is null.", nameof(resolved));
        }

        RequireValidRestrictions(resolved.Spec, nameof(resolved));

        var included = new HashSet<string>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in resolved.Spec.Attributes)
        {
            known.Add(attribute.Name);
            if (attribute.Include)
            {
                included.Add(attribute.Name);
            }
        }

        // Index provided outcomes by attribute, rejecting unknown/excluded attributes
        // and duplicate outcomes (programmer error — the calibrator is the only caller).
        var byAttribute = new Dictionary<string, AttributeCalibration>(StringComparer.Ordinal);
        foreach (var outcome in calibrations)
        {
            if (!known.Contains(outcome.AttributeName))
            {
                throw new ArgumentException(
                    $"calibration outcome names unknown attribute '{outcome.AttributeName}'.", nameof(calibrations));
            }

            if (!included.Contains(outcome.AttributeName))
            {
                throw new ArgumentException(
                    $"calibration outcome names excluded attribute '{outcome.AttributeName}'.", nameof(calibrations));
            }

            if (!byAttribute.TryAdd(outcome.AttributeName, outcome))
            {
                throw new ArgumentException(
                    $"attribute '{outcome.AttributeName}' has more than one calibration outcome.", nameof(calibrations));
            }
        }

        var diagnostics = new List<BedrockDiagnostic>();
        var effectiveAttributes = ImmutableArray.CreateBuilder<AttributeSpec>(resolved.Spec.Attributes.Count);
        var retained = ImmutableArray.CreateBuilder<AttributeCalibration>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in resolved.Spec.Attributes)
        {
            byAttribute.TryGetValue(attribute.Name, out var outcome);
            var (effective, used) = Substitute(attribute, outcome, diagnostics);
            effectiveAttributes.Add(effective);
            if (used is not null)
            {
                retained.Add(used);
                consumed.Add(attribute.Name);
            }
        }

        // A provided outcome the attribute did not need is a contract mismatch.
        foreach (var outcome in calibrations)
        {
            if (!consumed.Contains(outcome.AttributeName))
            {
                throw new ArgumentException(
                    $"attribute '{outcome.AttributeName}' does not need a calibration outcome, but one was provided ({outcome.GetType().Name}).",
                    nameof(calibrations));
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return Diagnosed<CalibratedSpec>.Failed(diagnostics);
            }
        }

        var effectiveSpec = new BedrockSpec(resolved.Spec.Binding, effectiveAttributes.MoveToImmutable());
        return Diagnosed<CalibratedSpec>.Ok(
            new CalibratedSpec(effectiveSpec, resolved, schema, retained.ToImmutable()), diagnostics);
    }

    /// <summary>
    /// True when <paramref name="spec"/> needs a data-reading calibration pass (§7):
    /// any included attribute with a <see cref="CalibrationPending"/> discretizer, a
    /// consuming discretizer with an omitted (null) domain, or a consuming discretizer under
    /// <c>unknown_value_policy = "include"</c>. An authored <c>[]</c> is complete and never
    /// counts on its own (D-122 §15). Excluded attributes and
    /// <c>restrict_to</c> never count.
    /// </summary>
    public static bool RequiresData(BedrockSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        foreach (var attribute in spec.Attributes)
        {
            if (!attribute.Include)
            {
                continue;
            }

            if (attribute.Discretizer is CalibrationPending)
            {
                return true;
            }

            if (attribute.Discretizer is { } discretizer && ConsumesDomain(discretizer))
            {
                if (attribute.DeclaredDomain is null
                    || attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Applies the pending → executable and effective-domain substitutions for one
    // attribute and returns the outcome it consumed (null when the attribute needs no
    // calibration). Data-derived cut invalidity is appended to diagnostics (P-14); every
    // other mismatch is a calibrator-contract violation and throws.
    private static (AttributeSpec Effective, AttributeCalibration? Used) Substitute(
        AttributeSpec attribute, AttributeCalibration? outcome, List<BedrockDiagnostic> diagnostics)
    {
        if (!attribute.Include)
        {
            return (attribute, null); // excluded outcomes are rejected before this point
        }

        if (attribute.Discretizer is CalibrationPending pending)
        {
            return SubstitutePending(attribute, pending, outcome, diagnostics);
        }

        var consumes = attribute.Discretizer is { } discretizer && ConsumesDomain(discretizer);

        // Omitted (null) consumed domain → filled from the observed domain (complete population).
        // An authored [] is NOT omitted — it is a complete fixed empty domain (D-122 §15) and
        // falls through to the include check / no-calibration path below.
        if (consumes && attribute.DeclaredDomain is null)
        {
            if (outcome is not ObservedDomain observed)
            {
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' has an omitted consumed domain and requires exactly one ObservedDomain outcome.");
            }

            return (attribute with { DeclaredDomain = observed.Values }, observed);
        }

        // Authored consumed domain (incl. []) under include → the domain plus the observed
        // additions. The omitted-domain branch above already returned on null and this branch
        // requires `consumes`, so the domain is non-null here; an authored [] contributes no
        // declared values, so the effective domain is exactly the additions (D-122 §15).
        if (consumes && attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
        {
            if (outcome is not IncludeAdditions additions)
            {
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' has an authored domain under unknown_value_policy = \"include\" and requires exactly one IncludeAdditions outcome.");
            }

            var effectiveDomain = (attribute.DeclaredDomain ?? []).Concat(additions.Values).ToImmutableArray();
            return (attribute with { DeclaredDomain = effectiveDomain }, additions);
        }

        // No calibration need: an outcome here is unexpected.
        if (outcome is not null)
        {
            throw new ArgumentException(
                $"attribute '{attribute.Name}' does not need a calibration outcome, but a {outcome.GetType().Name} was provided.");
        }

        return (attribute, null);
    }

    // Replaces one CalibrationPending carrier with the executable discretizer its outcome
    // resolves (D-093). Core owns the substitution so Plan and the fingerprints see one
    // consistent state, and so an auto discretizer and its frozen form share the cut
    // machinery by construction (D-088). A missing or kind-mismatched outcome is a
    // calibrator-contract violation (programmer error); invalid calibrated cuts are a
    // data-derived expected failure and come back as CalibrationCutsInvalid (P-14).
    private static (AttributeSpec Effective, AttributeCalibration? Used) SubstitutePending(
        AttributeSpec attribute, CalibrationPending pending, AttributeCalibration? outcome, List<BedrockDiagnostic> diagnostics)
    {
        switch (pending.Config)
        {
            // §11.4/G-8/D-102/D-103: min_max and percentile_p1_p99 are the equal_width ranges
            // this milestone can resolve — percentile joined at Slice D with its calibration
            // (D-103), which is the ONLY reason the Slice C guard narrows here rather than
            // widening to "any non-manual range". `manual` is spec-determined and never pends
            // (its own carrier constructor rejects it), so it can only be a corrupted instance.
            case PendingEqualWidth { Range: not (EqualWidthRange.MinMax or EqualWidthRange.PercentileP1P99) } unsupported:
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' carries a pending equal_width calibration with range '{unsupported.Range}', " +
                    "which is not a data-derived range this milestone can substitute (D-093/D-103).");

            case PendingEqualWidth config:
            {
                var cuts = RequireCuts(attribute, pending, outcome);
                return Build(attribute, cuts, diagnostics, EqualWidthDiscretizer.FromCalibratedCuts(config, cuts.Cuts, pending.Culture));
            }

            case PendingEqualFrequency config:
            {
                var cuts = RequireCuts(attribute, pending, outcome);
                return Build(attribute, cuts, diagnostics, EqualFrequencyDiscretizer.FromCalibratedCuts(config, cuts.Cuts, pending.Culture));
            }

            case PendingValueGroupsPassthrough config:
            {
                // §11.6/D-090/D-093: the discovered bins are the resolved identity — the authored
                // groups are preserved verbatim and the bins appended in first-observation order.
                // Unlike the cut variants there is no data-derived failure mode: any set of
                // discovered raw spellings (including none) is a valid bin set, so this arm has no
                // Diagnosed channel. An empty outcome is the legitimate zero-discovery marker and
                // must NOT be reinterpreted as skip/other.
                var bins = outcome as PassthroughBins
                    ?? throw new ArgumentException(
                        $"attribute '{attribute.Name}' carries a pending value_groups passthrough calibration and requires exactly one PassthroughBins outcome" +
                        (outcome is null ? ", but none was provided." : $", but a {outcome.GetType().Name} was provided."));

                var discretizer = ValueGroupsDiscretizer.CreatePassthrough(config.Groups, bins.Values);
                return (attribute with { Discretizer = discretizer }, bins);
            }

            default:
                // A pending variant this milestone cannot substitute is a mis-sequenced call.
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' carries an unresolved '{pending.Kind}' calibration that this milestone cannot substitute (D-093).");
        }
    }

    // Every cut-calibrated pending variant requires exactly one CalibratedCuts outcome; a
    // missing or kind-mismatched one is a calibrator-contract violation (programmer error).
    private static CalibratedCuts RequireCuts(AttributeSpec attribute, CalibrationPending pending, AttributeCalibration? outcome) =>
        outcome as CalibratedCuts
        ?? throw new ArgumentException(
            $"attribute '{attribute.Name}' carries a pending '{pending.Kind}' calibration and requires exactly one CalibratedCuts outcome" +
            (outcome is null ? ", but none was provided." : $", but a {outcome.GetType().Name} was provided."));

    // Applies one built discretizer, attributing its data-derived diagnostics (P-14). On failure
    // the Error fails the whole result, so the un-substituted attribute is never planned;
    // retaining the outcome keeps the report honest.
    private static (AttributeSpec Effective, AttributeCalibration? Used) Build<TDiscretizer>(
        AttributeSpec attribute, CalibratedCuts cuts, List<BedrockDiagnostic> diagnostics, Diagnosed<TDiscretizer> built)
        where TDiscretizer : Discretization.Discretizer
    {
        foreach (var diagnostic in built.Diagnostics)
        {
            diagnostics.Add(diagnostic.Location is null
                ? diagnostic with { Location = new DiagnosticLocation(AttributeName: attribute.Name) }
                : diagnostic);
        }

        return built.Value is { } discretizer
            ? (attribute with { Discretizer = discretizer }, cuts)
            : (attribute, cuts);
    }

    // §10.4/D-091/D-105: the calibrated-state re-check of the restriction entry
    // boundary. Calibration never consumes or rewrites restrict_to — the substitutions above
    // only touch Discretizer/DeclaredDomain, so each effective attribute carries the token's
    // already-immutable entry list by reference — but this factory is the last gate before
    // Plan/Emit/fingerprints, and its contract states that an invalid restriction
    // entry surviving here throws (programmer error, P-14).
    //
    // The check is EXHAUSTIVE over the three recognized variants, not just a finiteness test: a
    // half-guard that waved a null or an unknown variant through would let corrupt state reach
    // the emitter's matcher or the fingerprint encoder, which can only answer with a
    // NullReferenceException or an "unreachable" throw far from the cause. Reject at the
    // boundary, then trust the type inward (P-10).
    //
    // Defence in depth, deliberately: ResolvedSpec.Create is the primary boundary and the only
    // way to mint a token, so this is unreachable through any honest chain. It is kept because
    // the assertion is cheap, states the invariant at the boundary that actually feeds the
    // planner, and would catch a future internal construction path that resolved a restriction
    // differently.
    private static void RequireValidRestrictions(BedrockSpec spec, string parameterName)
    {
        foreach (var attribute in spec.Attributes)
        {
            foreach (var entry in attribute.RestrictTo)
            {
                var problem = entry switch
                {
                    null => "a null entry",
                    RestrictToValue { Value: null } => "a string entry with a null value",
                    RestrictToValue => null,
                    RestrictToNumber { Value: var value } when !double.IsFinite(value) =>
                        $"a non-finite exact value ({value})",
                    RestrictToNumber => null,
                    RestrictToRange { From: { } from } when !double.IsFinite(from) =>
                        $"a non-finite range 'from' bound ({from})",
                    RestrictToRange { To: { } to } when !double.IsFinite(to) =>
                        $"a non-finite range 'to' bound ({to})",
                    RestrictToRange => null,
                    _ => $"an unrecognized entry variant '{entry.GetType().Name}'",
                };

                if (problem is not null)
                {
                    throw new ArgumentException(
                        $"attribute '{attribute.Name}' carries {problem} in restrict_to; the resolve seam and " +
                        "ResolvedSpec.Create validate restriction entries before any calibrated state exists (§10.4/D-091).",
                        parameterName);
                }
            }
        }
    }

    private static bool ConsumesDomain(Discretization.Discretizer discretizer) => discretizer.ConsumesDeclaredDomain;
}
