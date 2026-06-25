using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Cut points over an <i>ordered categorical</i> domain (spec §11.x) — the
/// categorical sibling of <see cref="ManualCutsDiscretizer"/>, modelling v2's
/// <c>n</c> type. <see cref="Order"/> declares the domain low→high;
/// <see cref="Cuts"/> are members of it. A raw value not in <see cref="Order"/>
/// gets no bin. No numeric parse and no locale: the category strings are used
/// verbatim as cut values (P-11 n/a).
/// </summary>
public sealed record OrderedCutsDiscretizer(
    IReadOnlyList<string> Order,
    IReadOnlyList<string> Cuts,
    BinEnds Ends) : Discretizer
{
    private readonly IReadOnlyList<string> _binLabels = CutBinLabels.Build(Cuts, Ends);
    private readonly int[] _cutPositions = [.. Cuts.Select(cut => PositionOf(Order, cut))];
    private readonly Dictionary<string, int> _positionByValue = IndexPositions(Order);

    public override string Kind => "ordered_cuts";

    public override string? Discretize(string rawValue)
    {
        if (!_positionByValue.TryGetValue(rawValue, out var position))
        {
            return null; // not a known category → no bin
        }

        return CutBinLabels.LabelFor(_binLabels, FirstCutAbove(position), Cuts.Count, Ends);
    }

    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _binLabels;

    internal override BinScheme DescribeBins(IReadOnlyList<string> declaredDomain) =>
        new(_binLabels, Cuts, OpenLow: Ends == BinEnds.Open, OpenHigh: Ends == BinEnds.Open);

    internal override string RenderBinLabel(string canonicalLabel, LabelStyle style) =>
        CutBinLabels.Render(canonicalLabel, style);

    // Index of the first cut whose position is strictly above value's (count if none).
    private int FirstCutAbove(int position)
    {
        for (var i = 0; i < _cutPositions.Length; i++)
        {
            if (position < _cutPositions[i])
            {
                return i;
            }
        }

        return _cutPositions.Length;
    }

    private static int PositionOf(IReadOnlyList<string> order, string value)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        // Internal invariant (BedToSpec guarantees cuts ⊆ order); a violation is a bug.
        throw new ArgumentException($"ordered_cuts cut '{value}' is not a member of order.", nameof(value));
    }

    private static Dictionary<string, int> IndexPositions(IReadOnlyList<string> order)
    {
        var positions = new Dictionary<string, int>(order.Count, StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
        {
            positions[order[i]] = i;
        }

        return positions;
    }
}
