namespace FcaBedrock.Core.Discretization;

/// <summary>
/// How a discretizer renders a bin label's display form. Spec §8/§11.2/§14: the
/// canonical bin <i>key</i> (identity) is style-independent; only the rendered
/// name differs. Affects <c>output_fingerprint</c> only, never
/// <c>schema_fingerprint</c> (decisions.md D-011/D-035, D-044). A plan-time input;
/// the writers stay dumb (P-15).
/// </summary>
public enum LabelStyle
{
    /// <summary>vNext math notation: interior bins render <c>[a, b)</c>.</summary>
    Native,

    /// <summary>v2 byte-compat: interior bins render <c>{a}to&lt;{b}</c> (CLI <c>--v2-compat</c>).</summary>
    V2Compat,
}
