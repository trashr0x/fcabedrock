using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[defaults]</c> section (§6): spec-wide fallbacks the
/// resolver merges into attributes that omit the corresponding field
/// (D-060(c)). <c>formal_attribute_format</c> joined with M6 Slice A (D-120).
/// </summary>
/// <param name="Include">Default for <c>attribute.include</c> (§6; hard default true).</param>
/// <param name="MissingPolicy">Default missing-value policy (§6/§10.5; hard default skip).</param>
/// <param name="UnknownValuePolicy">Default unknown-value policy (§6/§10.6; hard default warn).</param>
/// <param name="DuplicateObjectPolicy">Default duplicate-key policy (§6.1; hard default fail).</param>
/// <param name="OrdinalDirection">Default ordinal direction (§6/§12.3; hard default ge).</param>
/// <param name="OrdinalBoundary">Default ordinal boundary (§6/§12.3; hard default inclusive).</param>
public sealed record DefaultsSection(
    bool? Include,
    MissingPolicy? MissingPolicy,
    UnknownValuePolicy? UnknownValuePolicy,
    DuplicateObjectPolicy? DuplicateObjectPolicy,
    OrdinalDirection? OrdinalDirection,
    OrdinalBoundary? OrdinalBoundary)
{
    /// <summary>
    /// The spec-wide <c>formal_attribute_format</c> override (§6/§10.7), or null
    /// when omitted — in which case the scale-specific defaults apply (there is no
    /// hard-coded <c>{column}-{value}</c>). An attribute's own format wins over it
    /// (§9.2 precedence). Unlike the whole-section attribute/template merge,
    /// <c>[defaults]</c> composes per field, so <c>SpecComposer.MergeDefaults</c>
    /// carries this explicitly (§13 rule 2, D-120).
    /// </summary>
    public string? FormalAttributeFormat { get; init; }
}
