using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[template]]</c> (§9.1), presence-tracked (D-066): the
/// <see cref="AttributeSection"/> config fields minus the always-per-attribute
/// <c>name</c>/<c>source</c>/<c>description</c>, plus the identifying
/// <c>id</c>. M2 carries and composes templates (§13 rule 3) but never applies
/// them — application/resolution is M6 Slice B (D-078/D-120); an unreferenced
/// template is inert, though from M6 Slice A its naming keys are parse-validated
/// like any attribute's. Deliberately flat rather than sharing a config record
/// with <see cref="AttributeSection"/> (D-078, P-3).
/// </summary>
/// <param name="Id">Template id referenced by matchers and attribute <c>template</c> (§9.1).</param>
/// <param name="Include">Whether matched attributes emit formal attributes (§10.1).</param>
/// <param name="Discretizer">Raw value → bin label (§11).</param>
/// <param name="Scale">Bin label → formal attribute(s) (§12).</param>
/// <param name="DeclaredDomain">Schema-bearing raw values (§10.3); null = omitted, empty = authored <c>[]</c> (D-049/D-071 provenance).</param>
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
    /// Authored <c>display_name</c> (§9.1/§10.1), or null when omitted. Carried
    /// and parse-validated from Slice A; it participates in the merge when
    /// template application lands (D-114/D-120).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Authored <c>formal_attribute_format</c> (§9.1/§10.7), or null when
    /// omitted. Parse-validated wherever authored — including inside an unused
    /// template, since shape is the parser's concern and dormancy is semantic
    /// (§10.7/D-049). Inert until application lands.
    /// </summary>
    public string? FormalAttributeFormat { get; init; }
}
