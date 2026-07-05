using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[template]]</c> (§9.1), presence-tracked (D-066): the
/// <see cref="AttributeSection"/> config fields minus the always-per-attribute
/// <c>name</c>/<c>source</c>/<c>description</c>, plus the identifying
/// <c>id</c>. M2 carries and composes templates (§13 rule 3) but never applies
/// them — application/resolution is M6 (D-078); an unreferenced template is
/// inert. Deliberately flat rather than sharing a config record with
/// <see cref="AttributeSection"/> (D-078, P-3).
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
    UnknownValuePolicy? UnknownValuePolicy);
