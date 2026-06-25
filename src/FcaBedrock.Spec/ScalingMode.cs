namespace FcaBedrock.Spec;

/// <summary>
/// The discrete-vs-progressive choice for v2's cut types (<c>o</c>/<c>n</c>) — the
/// nominal-vs-ordinal scale selection. v2 never recorded it in the <c>.bed</c>
/// (the discrete and progressive files are byte-identical), so it is supplied
/// out-of-band, like the <see cref="FcaBedrock.Core.Spec.Binding"/>. Defaults to
/// <see cref="Discrete"/>; M7's <c>migrate</c> command will surface it as a flag.
/// </summary>
public enum ScalingMode
{
    /// <summary>One formal attribute per bin (the <c>nominal</c> scale). v2 "discrete".</summary>
    Discrete,

    /// <summary>Cumulative below-threshold attributes (the <c>ordinal</c> scale, le). v2 "progressive".</summary>
    Progressive,
}
