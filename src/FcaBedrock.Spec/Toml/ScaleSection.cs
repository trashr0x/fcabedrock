using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// An authored attribute <c>scale</c> (§12): the three implemented scales plus
/// the §12.4 deferred kinds as parsable-but-rejected carriers (D-010).
/// </summary>
public abstract record ScaleSection;

/// <summary>The <c>nominal</c> scale (§12.1): one formal attribute per bin.</summary>
public sealed record NominalScaleSection : ScaleSection;

/// <summary>The <c>dichotomic</c> scale (§12.2).</summary>
/// <param name="TrueValue">The bin label mapping to "true"; required — absent is <c>AttributeScalingMissing</c> at resolve.</param>
public sealed record DichotomicScaleSection(string? TrueValue) : ScaleSection;

/// <summary>The <c>ordinal</c> scale (§12.3): cumulative threshold attributes.</summary>
/// <param name="Direction">Threshold direction; null falls back to <c>[defaults].ordinal_direction</c> then ge (D-060(c)).</param>
/// <param name="Boundary">Threshold boundary; null falls back to <c>[defaults].ordinal_boundary</c> then inclusive (D-060(c)).</param>
/// <param name="Order">Explicit value-bin order (§12.3); forbidden with cut discretizers — that check is the M2 validation slice (D-060).</param>
/// <param name="DropTop">Whether the tautological top threshold is suppressed (§12.3, default false).</param>
public sealed record OrdinalScaleSection(
    OrdinalDirection? Direction,
    OrdinalBoundary? Boundary,
    IReadOnlyList<string>? Order,
    bool? DropTop) : ScaleSection;

/// <summary>
/// A modelled-but-deferred scale (§12.4, D-010): <c>interordinal</c>,
/// <c>biordinal</c>, or <c>contranominal</c>, carried kind-only (the spec
/// defines no parameters for them). Resolves into the Core deferred-scale
/// marker so <c>ConversionPlanner</c> rejects with
/// <c>ScaleNotImplementedV1</c> (Fatal) — parse-but-fail-to-plan.
/// </summary>
/// <param name="Kind">The authored kind name.</param>
public sealed record DeferredScaleSection(string Kind) : ScaleSection;
