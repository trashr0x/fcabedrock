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
/// <param name="DeclaredDomain">Schema-bearing raw values, in column order (§10.3); empty when absent.</param>
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
    IReadOnlyList<string> DeclaredDomain,
    IReadOnlyList<RestrictToEntry> RestrictTo,
    IReadOnlyDictionary<string, string> ValueLabels,
    MissingPolicy MissingPolicy,
    UnknownValuePolicy UnknownValuePolicy);
