using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[attribute]]</c> (§10), presence-tracked (D-066). Flat by
/// design: <see cref="TemplateSection"/> mirrors the config fields rather than
/// sharing a record — extraction earns its keep when M6 applies templates
/// (D-078, P-3). The naming carriers landed with M6 Slice A (D-120).
/// </summary>
/// <param name="Name">Logical attribute name (§10.1); required — absent is <c>AttributeNameMissing</c> at resolve.</param>
/// <param name="Source">Where the raw value comes from (§10.2).</param>
/// <param name="Description">Free-text description (§10.1).</param>
/// <param name="Include">Whether the attribute emits formal attributes (§10.1); null falls back to <c>[defaults].include</c> then true.</param>
/// <param name="Template">Referenced <c>[[template]]</c> id (§9.1); carried, but a reference fails resolve until M6 Slice B (D-078/D-120).</param>
/// <param name="Discretizer">Raw value → bin label (§11); required when included (§10.9).</param>
/// <param name="Scale">Bin label → formal attribute(s) (§12); required when included (§10.9).</param>
/// <param name="DeclaredDomain">Schema-bearing raw values (§10.3). Null = omitted, empty = authored <c>[]</c> — both resolve as absent, but the authored form round-trips verbatim (D-049/D-071 provenance).</param>
/// <param name="RestrictTo">Object-level raw-value filter (§10.4); the Core union is reused — authored and resolved shapes coincide (D-057).</param>
/// <param name="ValueLabels">Raw value → display label (§10.8).</param>
/// <param name="MissingPolicy">Missing-value policy (§10.5); null falls back to <c>[defaults]</c> then skip.</param>
/// <param name="UnknownValuePolicy">Unknown-value policy (§10.6); null falls back to <c>[defaults]</c> then warn.</param>
public sealed record AttributeSection(
    string? Name,
    SourceSection? Source,
    string? Description,
    bool? Include,
    string? Template,
    DiscretizerSection? Discretizer,
    ScaleSection? Scale,
    IReadOnlyList<string>? DeclaredDomain,
    IReadOnlyList<RestrictToEntry>? RestrictTo,
    IReadOnlyDictionary<string, string>? ValueLabels,
    MissingPolicy? MissingPolicy,
    UnknownValuePolicy? UnknownValuePolicy)
{
    /// <summary>
    /// Authored <c>display_name</c> (§10.1), or null when omitted — the resolver
    /// then falls back to <see cref="Name"/>. An authored value is non-empty and
    /// CR/LF-free (parse-enforced, §10.1/D-117). Additive and non-positional, so
    /// the existing constructor and deconstruction are unchanged (D-087).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Authored <c>formal_attribute_format</c> (§10.7), or null when omitted — the
    /// scale-specific defaults then apply. An authored value parses under
    /// <c>NameFormat.TryCreate</c> (parse-enforced), and the authored spelling
    /// round-trips verbatim, alias and all.
    /// </summary>
    public string? FormalAttributeFormat { get; init; }
}
