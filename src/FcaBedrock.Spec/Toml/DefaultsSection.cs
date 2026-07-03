using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[defaults]</c> section (§6): spec-wide fallbacks the
/// resolver merges into attributes that omit the corresponding field
/// (D-060(c)). <c>formal_attribute_format</c> joins with the naming-fidelity
/// slice (D-066).
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
    OrdinalBoundary? OrdinalBoundary);
