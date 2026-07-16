namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Which side of a candidate <c>equal_frequency</c> boundary receives an entire
/// tied-value group (spec §11.5 <c>tie_policy</c>). A <b>calibration-time</b> rule:
/// it decides where a cut is placed, never how a value is classified — Emit does no
/// tie handling of its own and applies the ordinary §11.2 half-open <c>[lo, hi)</c>
/// geometry to the resolved cuts (D-088). A tied group is never split across bins.
/// <para>
/// The policy is a <i>preference</i>, not a guarantee: producing <c>bins - 1</c>
/// distinct ascending gaps can be mutually unsatisfiable with it, and §11.5 pins
/// <b>feasibility over tie-side preference</b> whenever the preferred gap is
/// unavailable — collisions and both domain edges included (D-103/G-5).
/// </para>
/// </summary>
public enum TiePolicy
{
    /// <summary>The whole tied group goes to the lower bin (the cut sits at the group's right edge).</summary>
    Left,

    /// <summary>The whole tied group goes to the upper bin (the cut sits at the group's left edge).</summary>
    Right,
}
