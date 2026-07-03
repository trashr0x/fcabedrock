using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[attribute]]</c> (§10), presence-tracked (D-066). Flat by
/// design: a shared config record may be extracted when <c>[[template]]</c>
/// lands (noted evolution, not built now — P-3). <c>display_name</c> joins
/// with the naming-fidelity slice.
/// </summary>
/// <param name="Name">Logical attribute name (§10.1); required — absent is <c>AttributeNameMissing</c> at resolve.</param>
/// <param name="Source">Where the raw value comes from (§10.2).</param>
/// <param name="Description">Free-text description (§10.1).</param>
/// <param name="Include">Whether the attribute emits formal attributes (§10.1); null falls back to <c>[defaults].include</c> then true.</param>
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
    DiscretizerSection? Discretizer,
    ScaleSection? Scale,
    IReadOnlyList<string>? DeclaredDomain,
    IReadOnlyList<RestrictToEntry>? RestrictTo,
    IReadOnlyDictionary<string, string>? ValueLabels,
    MissingPolicy? MissingPolicy,
    UnknownValuePolicy? UnknownValuePolicy);
