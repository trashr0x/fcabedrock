namespace FcaBedrock.Core.Scaling;

/// <summary>
/// What a <see cref="FcaBedrock.Core.Discretization.Discretizer"/> hands a
/// <see cref="Scale"/> at plan time: the ordered bins plus the structure an
/// ordinal scale needs (the cut edges and which ends are open). Internal — the
/// discretizer↔scale contract, not a public surface (P-4).
/// </summary>
/// <param name="Labels">
/// The ordered bin labels — the crossing/match keys the emitter sees from
/// <see cref="FcaBedrock.Core.Discretization.Discretizer.Discretize"/>.
/// </param>
/// <param name="Thresholds">
/// The ordered <i>edge</i> labels between bins (the cuts, e.g. <c>["30","40","50"]</c>
/// / <c>["Managerial"]</c>). For a non-cut discretizer these are simply the bin
/// labels. An ordinal scale names its thresholds from these, never from the
/// interval bin labels.
/// </param>
/// <param name="Bins">
/// The structural twin of <paramref name="Labels"/>, index-aligned: each bin's
/// <see cref="CanonicalBin"/> for the fingerprint encoder (D-069/D-077). Value
/// bins mirror their label; cut discretizers supply interval structure.
/// </param>
/// <param name="OpenLow">The lower end runs to −∞: a <c>ge</c> ordinal's tautological threshold renders <c>all</c>.</param>
/// <param name="OpenHigh">The upper end runs to +∞: a <c>le</c> ordinal's tautological threshold renders <c>all</c>.</param>
/// <param name="CutBins">
/// Whether these bins come from a <b>cut</b> discretizer (half-open intervals with
/// geometry-fixed thresholds) or are <b>value</b> bins. An
/// <see cref="OrdinalScale"/> thresholds on the cut geometry for the former and on
/// the explicit <c>scale.order</c> for the latter (§12.3, D-060/D-081) — the one
/// bit that selects the ordinal path.
/// </param>
internal sealed record BinScheme(
    IReadOnlyList<string> Labels,
    IReadOnlyList<CanonicalBin> Bins,
    IReadOnlyList<string> Thresholds,
    bool OpenLow,
    bool OpenHigh,
    bool CutBins);
