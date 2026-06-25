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
/// <param name="OpenLow">The lower end runs to −∞: a <c>ge</c> ordinal's tautological threshold renders <c>all</c>.</param>
/// <param name="OpenHigh">The upper end runs to +∞: a <c>le</c> ordinal's tautological threshold renders <c>all</c>.</param>
internal sealed record BinScheme(
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Thresholds,
    bool OpenLow,
    bool OpenHigh);
