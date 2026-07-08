namespace FcaBedrock.Core.Scaling;

/// <summary>
/// Cumulative threshold scale (spec §12.3): for N ordered bins, N formal
/// attributes, one per bin, each true when an object lies on one side of a cut.
/// v2's progressive scaling is <see cref="OrdinalDirection.Le"/> ("below" — <c>&lt;</c>
/// at each bin's upper edge), the M1 golden path.
///
/// <para>Over half-open cut bins the boundary is fixed by direction (D-044):
/// <c>le</c> ⇒ <c>&lt;</c> at upper edges, <c>ge</c> ⇒ <c>&gt;=</c> at lower edges — the
/// only whole-bin-clean pairings, so <see cref="Boundary"/> is not read there. Over
/// <b>value</b> bins (<c>identity</c> with an explicit <see cref="Order"/>) there is
/// no half-open geometry, so all four <c>direction × boundary</c> combinations are
/// well-defined and both knobs are live (§12.3, D-081).</para>
///
/// <para>The threshold of an open end (±∞) has no finite cut and renders
/// <c>all</c> (spec §12.3); it is kept unless <see cref="DropTop"/>. Value schemes
/// have no open ends, hence no <c>all</c> threshold.</para>
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

    // "Below": bin i's threshold is its upper edge (cut i); it crosses every bin at
    // or below i. The open top has no finite edge → `all`, crossing all bins.
    private List<FormalAttributeShape> BuildBelow(BinScheme bins)
    {
        var shapes = new List<FormalAttributeShape>(bins.Labels.Count);
        for (var i = 0; i < bins.Thresholds.Count; i++)
        {
            var cut = bins.Thresholds[i];
            shapes.Add(new FormalAttributeShape(
                cut, ScaleOp: "<", BinKey: cut, Bin: new ValueBin(cut), CrossingBins: [.. bins.Labels.Take(i + 1)]));
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
            shapes.Add(new FormalAttributeShape(
                cut, ScaleOp: ">=", BinKey: cut, Bin: new ValueBin(cut), CrossingBins: [.. bins.Labels.Skip(fromBin)]));
        }

        return shapes;
    }

    private static FormalAttributeShape OpenEnd(IReadOnlyList<string> labels) =>
        new(ValueLabel: OpenEndLabel, ScaleOp: "", BinKey: OpenEndLabel, Bin: new ValueBin(OpenEndLabel),
            CrossingBins: [.. labels]);

    // Value-bin ordinal (§12.3, D-081): identity value bins ordered by the explicit
    // scale.order. Each order position is a threshold at that raw value; direction ×
    // boundary pick the operator and which order positions it crosses:
    //   ge + inclusive → >= order[i], crosses order[i..]   (order[0]  is tautological)
    //   ge + strict    → >  order[i], crosses order[i+1..] (order[^1] is statically empty)
    //   le + inclusive → <= order[i], crosses order[..i+1] (order[^1] is tautological)
    //   le + strict    → <  order[i], crosses order[..i]   (order[0]  is statically empty)
    // Enumeration is by ascending order position in both directions. Value schemes have
    // no open end, so there is no `all` threshold; drop_top suppresses the inclusive
    // tautological threshold (ge → the first, le → the last) and is a no-op under strict
    // (whose tautological end is instead statically empty and simply never crosses). The
    // key/name is the raw order value via RenderName (never a display label). Order is a
    // validated permutation of the domain by plan time (OrdinalOrderMissing /
    // OrdinalOrderHasUnknownValue); a null Order here means a resolve/plan-guard bypass —
    // a programmer error (P-14).
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
