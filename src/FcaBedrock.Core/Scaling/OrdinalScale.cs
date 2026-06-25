namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Cumulative threshold scale (spec §12.3): for N ordered bins, N formal
/// attributes, one per bin, each true when an object lies on one side of a cut.
/// v2's progressive scaling is <see cref="OrdinalDirection.Le"/> ("below" — <c>&lt;</c>
/// at each bin's upper edge), the M1 golden path.
///
/// <para>Over half-open cut bins the boundary is fixed by direction (D-044):
/// <c>le</c> ⇒ <c>&lt;</c> at upper edges, <c>ge</c> ⇒ <c>&gt;=</c> at lower edges — the
/// only whole-bin-clean pairings. The independent <c>boundary</c> knob and the
/// explicit-<c>order</c> value-bin path are M2 (no M1 producer).</para>
///
/// <para>The threshold of an open end (±∞) has no finite cut and renders
/// <c>all</c> (spec §12.3); it is kept unless <see cref="DropTop"/>.</para>
/// </summary>
public sealed record OrdinalScale(OrdinalDirection Direction = OrdinalDirection.Ge, bool DropTop = false) : Scale
{
    // The label an open-end (±∞) tautological threshold renders as (D-047).
    private const string OpenEndLabel = "all";

    public override string Kind => "ordinal";

    internal override IReadOnlyList<FormalAttributeShape> BuildShapes(BinScheme bins) =>
        Direction == OrdinalDirection.Le ? BuildBelow(bins) : BuildAtOrAbove(bins);

    // "Below": bin i's threshold is its upper edge (cut i); it crosses every bin at
    // or below i. The open top has no finite edge → `all`, crossing all bins.
    private List<FormalAttributeShape> BuildBelow(BinScheme bins)
    {
        var shapes = new List<FormalAttributeShape>(bins.Labels.Count);
        for (var i = 0; i < bins.Thresholds.Count; i++)
        {
            var cut = bins.Thresholds[i];
            shapes.Add(new FormalAttributeShape(cut, ScaleOp: "<", BinKey: cut, CrossingBins: [.. bins.Labels.Take(i + 1)]));
        }

        if (bins.OpenHigh && !DropTop)
        {
            shapes.Add(OpenEnd(bins.Labels));
        }

        return shapes;
    }

    // "At or above": bin i's threshold is its lower edge (cut i-1); it crosses every
    // bin at or above it. The open bottom has no finite edge → `all`, crossing all.
    private List<FormalAttributeShape> BuildAtOrAbove(BinScheme bins)
    {
        var shapes = new List<FormalAttributeShape>(bins.Labels.Count);
        if (bins.OpenLow && !DropTop)
        {
            shapes.Add(OpenEnd(bins.Labels));
        }

        for (var i = 0; i < bins.Thresholds.Count; i++)
        {
            var cut = bins.Thresholds[i];
            var fromBin = bins.OpenLow ? i + 1 : i; // the open-bottom bin sits before the first cut
            shapes.Add(new FormalAttributeShape(cut, ScaleOp: ">=", BinKey: cut, CrossingBins: [.. bins.Labels.Skip(fromBin)]));
        }

        return shapes;
    }

    private static FormalAttributeShape OpenEnd(IReadOnlyList<string> labels) =>
        new(ValueLabel: OpenEndLabel, ScaleOp: "", BinKey: OpenEndLabel, CrossingBins: [.. labels]);
}
