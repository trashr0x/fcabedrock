using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[template]]</c> (§9.1), presence-tracked (D-066): the
/// <see cref="AttributeSection"/> config fields minus the always-per-attribute
/// <c>name</c>/<c>source</c>/<c>description</c>, plus the identifying
/// <c>id</c>. Composed per §13 rule 3 (same-<c>id</c> replacement in place, so an
/// inherited reference re-targets late) and <b>applied</b> at the resolve seam
/// from M6 Slice B (D-114/D-121). An <b>unused</b> template stays semantically
/// dormant: its authored shape is parse-checked, but effective-semantic
/// combinations are validated only if it applies to some attribute (§9.2).
/// Deliberately flat rather than sharing a config record with
/// <see cref="AttributeSection"/> (D-078, P-3), and closed to these ten fields —
/// v1 templates never nest.
/// </summary>
/// <param name="Id">Template id referenced by matchers and attribute <c>template</c> (§9.1); required, and grammar-checked at parse.</param>
/// <param name="Include">Whether matched attributes emit formal attributes (§10.1).</param>
/// <param name="Discretizer">Raw value → bin label (§11).</param>
/// <param name="Scale">Bin label → formal attribute(s) (§12).</param>
/// <param name="DeclaredDomain">Schema-bearing raw values (§10.3). <see langword="null"/> = omitted; any non-null list, including <c>[]</c>, is an authored-complete whole value under D-114 layering (D-049 provenance; D-122 §15, revising D-071).</param>
/// <param name="RestrictTo">Object-level raw-value filter (§10.4).</param>
/// <param name="ValueLabels">Raw value → display label (§10.8).</param>
/// <param name="MissingPolicy">Missing-value policy (§10.5).</param>
/// <param name="UnknownValuePolicy">Unknown-value policy (§10.6).</param>
public sealed record TemplateSection(
    string? Id,
    bool? Include,
    DiscretizerSection? Discretizer,
    ScaleSection? Scale,
    IReadOnlyList<string>? DeclaredDomain,
    IReadOnlyList<RestrictToEntry>? RestrictTo,
    IReadOnlyDictionary<string, string>? ValueLabels,
    MissingPolicy? MissingPolicy,
    UnknownValuePolicy? UnknownValuePolicy)
{
    /// <summary>
    /// Authored <c>display_name</c> (§9.1/§10.1), or null when omitted. Like every
    /// other field here it participates in the §9.2 merge as a whole value: it
    /// beats <c>[defaults]</c> and loses to an explicit attribute
    /// <c>display_name</c> (D-114/D-121).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Authored <c>formal_attribute_format</c> (§9.1/§10.7), or null when
    /// omitted. Parse-validated wherever authored — including inside an unused
    /// template, since shape is the parser's concern and dormancy is semantic
    /// (§10.7/D-049) — and applied through the same whole-value merge, so a
    /// template-supplied format drives rendered <c>.cxt</c> names (D-121).
    /// </summary>
    public string? FormalAttributeFormat { get; init; }
}
