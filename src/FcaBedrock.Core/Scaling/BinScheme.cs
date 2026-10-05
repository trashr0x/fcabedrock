namespace FcaBedrock.Core.Scaling;

/// <summary>
/// What a <see cref="FcaBedrock.Core.Discretization.Discretizer"/> hands a
/// <see cref="Scale"/> at plan time: the ordered bins plus the structure an
/// ordinal scale needs (the cut edges and which ends are open). Internal: the
/// discretizer↔scale contract, not a public surface (EP-4).
/// </summary>
/// <param name="Labels">
/// The ordered bin labels: the crossing/match keys the emitter sees from
/// <see cref="FcaBedrock.Core.Discretization.Discretizer.Discretize"/>.
/// </param>
/// <param name="Thresholds">
/// The ordered canonical cut labels, which are the cut bins' finite <i>edges</i> (e.g.
/// <c>["30","40","50"]</c> / <c>["Managerial"]</c>). A cut between two bins is the upper
/// edge of one and the lower edge of the next; at a closed end the first or last cut is
/// the outer edge of the first or last bin, so three cuts bound four bins with open ends
/// but two with closed ones (§11.2). For a non-cut discretizer these are simply the bin
/// labels. Over cut bins an ordinal scale names its thresholds from these and
/// <paramref name="OpenLow"/>/<paramref name="OpenHigh"/>, never from the interval bin
/// labels.
/// </param>
/// <param name="Bins">
/// The structural twin of <paramref name="Labels"/>, index-aligned: each bin's
/// <see cref="CanonicalBin"/> for the fingerprint encoder (D-069/D-077). Value
/// bins mirror their label; cut discretizers supply interval structure.
/// </param>
/// <param name="OpenLow">
/// The lower end runs to −∞. In a non-empty cut scheme (<paramref name="CutBins"/> true), the
/// first bin then has no finite lower edge and a <c>ge</c> ordinal's tautological threshold
/// renders <c>all</c>; when false, the first cut is the first bin's lower edge and is that
/// threshold. Value schemes have no unbounded cut end, so this is false for them, and an
/// ordinal scale thresholds value bins on its order, not on these flags.
/// </param>
/// <param name="OpenHigh">
/// The upper end runs to +∞. In a non-empty cut scheme (<paramref name="CutBins"/> true), the
/// last bin then has no finite upper edge and a <c>le</c> ordinal's tautological threshold
/// renders <c>all</c>; when false, the last cut is the last bin's upper edge and is that
/// threshold. Value schemes have no unbounded cut end, so this is false for them, and an
/// ordinal scale thresholds value bins on its order, not on these flags.
/// </param>
/// <param name="CutBins">
/// Whether these bins come from a <b>cut</b> discretizer (half-open intervals with
/// geometry-fixed thresholds) or are <b>value</b> bins. An
/// <see cref="OrdinalScale"/> thresholds on the cut geometry for the former and on
/// the explicit <c>scale.order</c> for the latter (§12.3, D-060/D-081). This is the one
/// bit that selects the ordinal path.
/// </param>
internal sealed record BinScheme(
    IReadOnlyList<string> Labels,
    IReadOnlyList<CanonicalBin> Bins,
    IReadOnlyList<string> Thresholds,
    bool OpenLow,
    bool OpenHigh,
    bool CutBins);
