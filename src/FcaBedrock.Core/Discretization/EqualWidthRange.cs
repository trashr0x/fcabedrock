namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Where an <c>equal_width</c> discretizer's span comes from (spec §11.4). The
/// mode decides the <b>phase</b> and the stored-fingerprint eligibility (D-089):
/// <see cref="Manual"/> is spec-determined — its cuts come from <c>vmin</c>/<c>vmax</c>
/// alone, it skips Calibrate (§7), and it is fully-frozen-eligible (§14); the
/// data-derived modes draw the span from the calibration population and make the
/// spec data-dependent.
/// </summary>
public enum EqualWidthRange
{
    /// <summary>The span is the observed minimum and maximum of the calibration population (§7). The default.</summary>
    MinMax,

    /// <summary>
    /// The span is the 1st and 99th percentiles of the calibration population.
    /// Modelled here (the D-089 range-mode contract); its TOML spelling and
    /// calibration land at M4 Slice D, so the reader rejects <c>"percentile_p1_p99"</c>
    /// as an unrecognized range until then (<c>SpecFieldInvalid</c>, D-070 tier 3).
    /// </summary>
    PercentileP1P99,

    /// <summary>The span is the authored <c>vmin</c>/<c>vmax</c>: spec-determined, no data pass (§11.4/§7).</summary>
    Manual,
}
