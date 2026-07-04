using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// The emit-time pipeline for one included source attribute: where to read it,
/// how to discretize it, which bins are recognized, and which global formal
/// attribute ids each bin crosses. All ordering/identity decisions are already
/// baked in by the planner; the emitter only looks values up here.
/// </summary>
/// <param name="Name">The logical attribute name.</param>
/// <param name="SourceColumnIndex">Resolved 0-based source column.</param>
/// <param name="Discretizer">Maps a raw value to a bin label.</param>
/// <param name="KnownBins">
/// Recognized bin labels. A discretized value outside this set is an unknown value
/// (per <paramref name="UnknownValuePolicy"/>); a recognized bin that crosses
/// nothing (e.g. the false pole of a dichotomy) is not.
/// </param>
/// <param name="CrossesByBin">Bin label → ascending global formal-attribute ids it crosses.</param>
/// <param name="MissingFormalAttributeId">
/// Formal-attribute id crossed when the value is missing
/// (<c>missing_policy = "as_attribute"</c>); null = missing values skip.
/// </param>
/// <param name="UnknownValuePolicy">How out-of-domain values are handled.</param>
public sealed record PlannedAttribute(
    string Name,
    int SourceColumnIndex,
    Discretizer Discretizer,
    IReadOnlySet<string> KnownBins,
    IReadOnlyDictionary<string, IReadOnlyList<int>> CrossesByBin,
    int? MissingFormalAttributeId,
    UnknownValuePolicy UnknownValuePolicy);
