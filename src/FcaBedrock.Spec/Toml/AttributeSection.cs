using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[attribute]]</c> (§10), presence-tracked (D-066). Flat by
/// design: <see cref="TemplateSection"/> mirrors the config fields rather than
/// sharing a record — the merge reads the two independently, field by field, so
/// a shared config record would buy nothing (D-078/D-121, P-3).
/// <para>
/// From M6 Slice B this type is also the <b>effective</b> attribute: template
/// application produces one of these per declared attribute, so every validation
/// owner sees a template-configured attribute in exactly the shape a flat
/// declaration would present (D-121).
/// </para>
/// </summary>
/// <param name="Name">Logical attribute name (§10.1); required — absent is <c>AttributeNameMissing</c> at resolve.</param>
/// <param name="Source">Where the raw value comes from (§10.2).</param>
/// <param name="Description">Free-text description (§10.1).</param>
/// <param name="Include">Whether the attribute emits formal attributes (§10.1); null falls back to <c>[defaults].include</c> then true.</param>
/// <param name="Template">Referenced <c>[[template]]</c> id (§9.2 tier 4) — it beats every matcher template and loses to explicit fields; an unknown id is <c>TemplateReferenceUnknown</c> at resolve (D-114/D-121).</param>
/// <param name="Discretizer">Raw value → bin label (§11); required when included (§10.9).</param>
/// <param name="Scale">Bin label → formal attribute(s) (§12); required when included (§10.9).</param>
/// <param name="DeclaredDomain">Schema-bearing raw values (§10.3). <see langword="null"/> = omitted at this layer; any non-null list, including <c>[]</c>, is authored-complete. After §9.2 layering, only an omitted effective domain requests observed-domain calibration when consumed; an authored <c>[]</c> is a fixed empty domain. The authored form round-trips verbatim (D-049 provenance; D-122 §15, revising D-071).</param>
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
