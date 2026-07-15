using System.Collections.Immutable;
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

        return new CalibratedSpec(resolved.Spec, resolved, schema, ImmutableArray<AttributeCalibration>.Empty);
    }

    /// <summary>
    /// Assembles the effective spec from the authored spec plus the
    /// calibrator-produced outcomes (D-093). Core owns the substitution so Plan and
    /// the fingerprints see one consistent state: an absent consumed domain plus its
    /// <see cref="ObservedDomain"/> becomes the effective domain, and
    /// <see cref="IncludeAdditions"/> are appended to an explicit domain. Returns
    /// <see cref="Diagnosed{T}"/> — data-derived cut invalidity comes back as Error
    /// diagnostics so the calibrator can aggregate them (none in slice A). Throws
    /// <see cref="ArgumentException"/> only for calibrator-contract mismatches
    /// (programmer error): an outcome naming an unknown/excluded attribute, a
    /// kind-mismatched/duplicate/unexpected outcome, a leftover
    /// <see cref="CalibrationPending"/>, or a required completeness marker that is
    /// missing. Completeness is required per calibration mode: an absent-domain
    /// consuming attribute must carry exactly one <see cref="ObservedDomain"/> (never
    /// co-occurring with <see cref="IncludeAdditions"/>), and an explicit-domain
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
            var (effective, used) = Substitute(attribute, outcome);
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
    /// consuming discretizer with an absent domain, or a consuming discretizer under
    /// <c>unknown_value_policy = "include"</c>. Excluded attributes and
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
                if (attribute.DeclaredDomain.Count == 0
                    || attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Applies the effective-domain substitution for one attribute and returns the
    // outcome it consumed (null when the attribute needs no calibration).
    private static (AttributeSpec Effective, AttributeCalibration? Used) Substitute(
        AttributeSpec attribute, AttributeCalibration? outcome)
    {
        if (!attribute.Include)
        {
            return (attribute, null); // excluded outcomes are rejected before this point
        }

        if (attribute.Discretizer is CalibrationPending pending)
        {
            // The concrete pending → executable substitution lands with the M4 numeric/
            // value_groups slices; no auto discretizer is constructible in slice A, so a
            // CalibrationPending here is a leftover the milestone cannot resolve.
            throw new ArgumentException(
                $"attribute '{attribute.Name}' carries an unresolved '{pending.Kind}' calibration that this milestone cannot substitute (D-093).");
        }

        var consumes = attribute.Discretizer is { } discretizer && ConsumesDomain(discretizer);

        // Absent consumed domain → filled from the observed domain (complete population).
        if (consumes && attribute.DeclaredDomain.Count == 0)
        {
            if (outcome is not ObservedDomain observed)
            {
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' has an absent consumed domain and requires exactly one ObservedDomain outcome.");
            }

            return (attribute with { DeclaredDomain = observed.Values }, observed);
        }

        // Explicit consumed domain under include → the domain plus the observed additions.
        if (consumes && attribute.UnknownValuePolicy == UnknownValuePolicy.Include)
        {
            if (outcome is not IncludeAdditions additions)
            {
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' has an explicit domain under unknown_value_policy = \"include\" and requires exactly one IncludeAdditions outcome.");
            }

            var effectiveDomain = attribute.DeclaredDomain.Concat(additions.Values).ToImmutableArray();
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

    private static bool ConsumesDomain(Discretization.Discretizer discretizer) => discretizer.ConsumesDeclaredDomain;
}
