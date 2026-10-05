namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Cumulative threshold scale (spec §12.3): for N ordered bins, N formal
/// attributes, one per bin, each true when an object lies on one side of a cut.
/// v2's progressive scaling is <see cref="OrdinalDirection.Le"/> ("below": <c>&lt;</c>
/// at each bin's upper edge), the path the v2 golden fixtures pin.
///
/// <para>Over half-open cut bins the boundary is fixed by direction (D-047/D-060):
/// <c>le</c> ⇒ <c>&lt;</c> at upper edges, <c>ge</c> ⇒ <c>&gt;=</c> at lower edges, the
/// only whole-bin-clean pairings, so <see cref="Boundary"/> is not read there. Over
/// <b>value</b> bins (<c>identity</c>, <c>free_per_value</c> or <c>value_groups</c>,
/// thresholded on <see cref="Order"/>) there is no half-open geometry, so all four
/// <c>direction × boundary</c> combinations are well-defined and both knobs are live
/// (§12.3, D-081/D-096/D-104).</para>
///
/// <para>Over cut bins the threshold that every bin crosses (the top bin's for
/// <c>le</c>, the bottom bin's for <c>ge</c>) sits at an end of the cut range: an open
/// end (±∞) has no finite cut, so that threshold renders <c>all</c> (spec §12.3), and a
/// closed end is the outermost cut itself. <see cref="DropTop"/> suppresses that
/// threshold in both cases (§12.3). Value schemes have no open ends, hence no
/// <c>all</c> threshold.</para>
///
/// <para><see cref="BuildShapes"/> selects the path by
/// <see cref="BinScheme.CutBins"/>: cut schemes keep the geometry paths above;
/// value schemes go through <see cref="BuildValueThresholds"/>, thresholding on
/// <see cref="Order"/> under the resolved <see cref="Boundary"/>.</para>
/// </summary>
public sealed record OrdinalScale(
    OrdinalDirection Direction = OrdinalDirection.Ge,
    bool DropTop = false,
    OrdinalBoundary Boundary = OrdinalBoundary.Inclusive,
    IReadOnlyList<string>? Order = null) : Scale
{
    // The label an open-end (±∞) tautological threshold renders as (D-047).
    private const string OpenEndLabel = "all";

    public override string Kind => "ordinal";

    internal override IReadOnlyList<FormalAttributeShape> BuildShapes(BinScheme bins) =>
        bins.CutBins
            ? (Direction == OrdinalDirection.Le ? BuildBelow(bins) : BuildAtOrAbove(bins))
            : BuildValueThresholds();

    // "Below": bin i's threshold is its upper edge, `<`, crossed by objects in bin i and in
    // every bin below it. Every object that has a bin crosses the top bin's threshold, so it
    // is the tautological one drop_top suppresses: `all` when the top is open (no finite
    // edge), the last cut when it is closed.
    private List<FormalAttributeShape> BuildBelow(BinScheme bins)
    {
        var top = bins.Labels.Count - 1;
        var shapes = new List<FormalAttributeShape>(bins.Labels.Count);
        for (var i = 0; i <= top; i++)
        {
            if (DropTop && i == top)
            {
                continue;
            }

            IReadOnlyList<string> crossing = [.. bins.Labels.Take(i + 1)];
            shapes.Add(i == top && bins.OpenHigh
                ? OpenEnd(crossing)
                : CutThreshold(bins.Thresholds[LowerEdge(bins, i) + 1], "<", crossing));
        }

        return shapes;
    }

    // "At or above": bin i's threshold is its lower edge, `>=`, crossed by objects in bin i
    // and in every bin above it. Every object that has a bin crosses the bottom bin's
    // threshold, so it is the tautological one drop_top suppresses: `all` when the bottom is
    // open (no finite edge), the first cut when it is closed.
    private List<FormalAttributeShape> BuildAtOrAbove(BinScheme bins)
    {
        var shapes = new List<FormalAttributeShape>(bins.Labels.Count);
        for (var i = 0; i < bins.Labels.Count; i++)
        {
            if (DropTop && i == 0)
            {
                continue;
            }

            IReadOnlyList<string> crossing = [.. bins.Labels.Skip(i)];
            shapes.Add(i == 0 && bins.OpenLow
                ? OpenEnd(crossing)
                : CutThreshold(bins.Thresholds[LowerEdge(bins, i)], ">=", crossing));
        }

        return shapes;
    }

    // The index in Thresholds of bin i's lower edge; its upper edge is the next cut. With an
    // open bottom, bin 0 runs from −∞ to the first cut, so bin i starts at cut `i - 1`; with a
    // closed bottom, bin 0 starts at the first cut, so bin i starts at cut `i` (§11.2). Edges
    // are read from the cut list and the open flags, never from a rendered bin label.
    private static int LowerEdge(BinScheme bins, int bin) => bins.OpenLow ? bin - 1 : bin;

    private static FormalAttributeShape CutThreshold(string cut, string op, IReadOnlyList<string> crossing) =>
        new(cut, ScaleOp: op, BinKey: cut, Bin: new ValueBin(cut), CrossingBins: crossing);

    private static FormalAttributeShape OpenEnd(IReadOnlyList<string> crossing) =>
        new(ValueLabel: OpenEndLabel, ScaleOp: "", BinKey: OpenEndLabel, Bin: new ValueBin(OpenEndLabel),
            CrossingBins: crossing);

    // Value-bin ordinal (§12.3, D-081/D-096/D-104): identity, free_per_value or value_groups
    // bins ordered by scale.order (authored, or the planner's natural numeric order for a
    // numeric free_per_value). Each order position is a threshold at that raw value; direction ×
    // boundary pick the operator and which order positions it crosses:
    //   ge + inclusive → >= order[i], crosses order[i..]   (order[0]  is tautological)
    //   ge + strict    → >  order[i], crosses order[i+1..] (order[^1] is statically empty)
    //   le + inclusive → <= order[i], crosses order[..i+1] (order[^1] is tautological)
    //   le + strict    → <  order[i], crosses order[..i]   (order[0]  is statically empty)
    // Enumeration is by ascending order position in both directions. Value schemes have
    // no open end, so there is no `all` threshold; drop_top suppresses the inclusive
    // tautological threshold (ge → the first, le → the last) and is a no-op under strict
    // (whose tautological end is instead statically empty and simply never crosses). The
    // key and the shape's ValueLabel are the raw order value, never a display label; the
    // planner's RenderName applies value_labels to the name. Order is a validated
    // permutation of the bin universe by plan time (OrdinalOrderMissing /
    // OrdinalOrderHasUnknownValue); a null Order here means a resolve/plan-guard bypass,
    // a programmer error (EP-14).
    private List<FormalAttributeShape> BuildValueThresholds()
    {
        if (Order is not { } order)
        {
            throw new InvalidOperationException(
                "OrdinalScale over value bins requires an explicit order; the resolve/plan guards ensure it is present.");
        }

        var inclusive = Boundary == OrdinalBoundary.Inclusive;
        var op = (Direction, inclusive) switch
        {
            (OrdinalDirection.Ge, true) => ">=",
            (OrdinalDirection.Ge, false) => ">",
            (OrdinalDirection.Le, true) => "<=",
            _ => "<", // (Le, strict)
        };

        var shapes = new List<FormalAttributeShape>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            // drop_top suppresses the inclusive tautological threshold; under strict
            // there is none (the tautological end is statically empty), so it is a no-op.
            if (DropTop && inclusive
                && ((Direction == OrdinalDirection.Ge && i == 0)
                    || (Direction == OrdinalDirection.Le && i == order.Count - 1)))
            {
                continue;
            }

            var value = order[i];
            IReadOnlyList<string> crossing = (Direction, inclusive) switch
            {
                (OrdinalDirection.Ge, true) => [.. order.Skip(i)],
                (OrdinalDirection.Ge, false) => [.. order.Skip(i + 1)],
                (OrdinalDirection.Le, true) => [.. order.Take(i + 1)],
                _ => [.. order.Take(i)], // (Le, strict)
            };

            shapes.Add(new FormalAttributeShape(
                ValueLabel: value, ScaleOp: op, BinKey: value, Bin: new ValueBin(value), CrossingBins: crossing));
        }

        return shapes;
    }
}
