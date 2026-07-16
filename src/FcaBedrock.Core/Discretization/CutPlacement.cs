namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Where an <c>equal_frequency</c> cut is placed within the selected gap between two
/// adjacent distinct values (spec §11.5 <c>cut_placement</c>). Calibration-time, like
/// <see cref="TiePolicy"/>: it fixes the cut's numeric value, after which the resolved
/// cuts classify by ordinary §11.2 half-open geometry.
/// </summary>
public enum CutPlacement
{
    /// <summary>The cut equals the gap's upper value — the first value of the new bin (§11.5).</summary>
    RightValue,

    /// <summary>
    /// The cut sits midway between the gap's two values (§11.5). The expression is
    /// sign-aware so an opposite-sign extreme gap cannot overflow, and a midpoint that
    /// cannot land strictly above the lower value (adjacent representable doubles) falls
    /// back to the upper value — membership-identical to
    /// <see cref="RightValue"/> under half-open geometry (D-103/G-5).
    /// </summary>
    Midpoint,
}
