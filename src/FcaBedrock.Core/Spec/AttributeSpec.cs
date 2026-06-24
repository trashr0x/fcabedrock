using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Spec;

/// <summary>
/// One logical attribute and how it becomes zero or more formal attributes. Spec
/// §10. <see cref="Discretizer"/>/<see cref="Scale"/> are required when
/// <see cref="Include"/> is true and absent when it is false; the planner enforces
/// the §10.9 rules.
/// </summary>
/// <param name="Name">Unique logical name.</param>
/// <param name="Source">Where the raw value comes from.</param>
/// <param name="Include">Whether the attribute emits formal attributes.</param>
/// <param name="Discretizer">Raw value → bin label (when included).</param>
/// <param name="Scale">Bin label → formal attribute(s) (when included).</param>
/// <param name="DeclaredDomain">Schema-bearing raw values, in column order (§10.3); empty when absent.</param>
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
    IReadOnlyDictionary<string, string> ValueLabels,
    MissingPolicy MissingPolicy,
    UnknownValuePolicy UnknownValuePolicy);
