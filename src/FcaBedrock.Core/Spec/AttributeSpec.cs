using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Spec;

/// <summary>
/// One logical attribute and how it becomes zero or more formal attributes. Spec
/// §10. <see cref="Discretizer"/>/<see cref="Scale"/> are required when
/// <see cref="Include"/> is true; when it is false they may still be present but
/// are parked — the planner ignores all emitted config of an excluded attribute
/// (§10.9 / D-049, an authoring toggle).
/// </summary>
/// <param name="Name">Unique logical name.</param>
/// <param name="Source">Where the raw value comes from.</param>
/// <param name="Include">Whether the attribute emits formal attributes.</param>
/// <param name="Discretizer">Raw value → bin label (when included).</param>
/// <param name="Scale">Bin label → formal attribute(s) (when included).</param>
/// <param name="DeclaredDomain">Schema-bearing raw values, in column order (§10.3).
/// <see langword="null"/> = omitted, which requests observed-domain calibration where a
/// discretizer consumes it; any non-null list, including <c>[]</c>, is authored-complete —
/// <c>[]</c> is a fixed empty domain of zero declared value bins (D-122 §15, revising D-071).</param>
/// <param name="RestrictTo">Object-level raw-value filter entries (§10.4); empty when absent. A resolved carrier in this slice — not yet planned or executed (D-057/D-063, M4).</param>
/// <param name="ValueLabels">Raw value → display label for names (§10.8); empty when none.</param>
/// <param name="MissingPolicy">How missing values are handled.</param>
/// <param name="UnknownValuePolicy">How out-of-domain values are handled.</param>
public sealed record AttributeSpec(
    string Name,
    SourceBinding Source,
    bool Include,
    Discretizer? Discretizer,
    Scale? Scale,
    IReadOnlyList<string>? DeclaredDomain,
    IReadOnlyList<RestrictToEntry> RestrictTo,
    IReadOnlyDictionary<string, string> ValueLabels,
    MissingPolicy MissingPolicy,
    UnknownValuePolicy UnknownValuePolicy)
{
    /// <summary>
    /// The resolved display name behind <c>{display_name}</c> (§10.1/§10.7),
    /// defaulting to <see cref="Name"/>. Non-empty and CR/LF-free: the reader
    /// enforces it on the authored path and <c>ResolvedSpec.Create</c> backstops
    /// hand-built graphs. Additive and non-positional, so every construction site
    /// that predates naming stays valid and unchanged (D-087's precedent).
    /// </summary>
    public string DisplayName { get; init; } = Name;

    /// <summary>
    /// The effective post-precedence <c>formal_attribute_format</c> (§10.7), or
    /// <see langword="null"/> when the scale-specific defaults apply — which is
    /// the normal case, and the state every pre-M6 spec resolves to. Template and
    /// matcher syntax never reaches Core; only this resolved value does (D-118).
    /// </summary>
    public NameFormat? NameFormat { get; init; }
}
