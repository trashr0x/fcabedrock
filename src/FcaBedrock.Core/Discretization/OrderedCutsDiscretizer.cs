using FcaBedrock.Core.Scaling;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Cut points over an <i>ordered categorical</i> domain (spec §11.8) — the
/// categorical sibling of <see cref="ManualCutsDiscretizer"/>, modelling v2's
/// <c>n</c> type. <see cref="Order"/> declares the domain low→high;
/// <see cref="Cuts"/> are members of it. A raw value not in <see cref="Order"/>
/// gets no bin. No numeric parse and no locale: the category strings are used
/// verbatim as cut values (P-11 n/a).
/// <para>
/// Constructed only through <see cref="Create"/>, which validates the order and
/// cut spec (<see cref="CutValidation.ValidateOrdered"/>) so an invalid one is
/// unrepresentable (P-10, D-056). The private constructor trusts its already-validated
/// inputs.
/// </para>
/// </summary>
public sealed record OrderedCutsDiscretizer : Discretizer
{
    /// <summary>The category domain low→high; entries distinct and non-empty (validated).</summary>
    public IReadOnlyList<string> Order { get; }

    /// <summary>The cuts, members of <see cref="Order"/>, strictly ascending by position (validated).</summary>
    public IReadOnlyList<string> Cuts { get; }

    /// <summary>Whether the outer bins extend to the ends (<see cref="BinEnds.Open"/>) or are dropped.</summary>
    public BinEnds Ends { get; }

    private readonly IReadOnlyList<string> _binLabels;
    private readonly int[] _cutPositions;
    private readonly Dictionary<string, int> _positionByValue;

    private OrderedCutsDiscretizer(IReadOnlyList<string> order, IReadOnlyList<string> cuts, BinEnds ends)
    {
        // Snapshot the caller's lists: mutable inputs must not desync Order/Cuts from the cached
        // labels and positions after construction (P-10 — validated invariants stay true for life).
        Order = [.. order];
        Cuts = [.. cuts];
        Ends = ends;
        _binLabels = CutBinLabels.Build(Cuts, ends);
        _positionByValue = IndexPositions(Order);
        _cutPositions = [.. Cuts.Select(cut => PositionOf(Order, cut))];
    }

    /// <summary>
    /// Validates the order and cut spec and, if valid, builds the discretizer
    /// (spec §11.8, D-056). On any problem returns <see cref="Diagnosed{T}.Failed"/>
    /// and never constructs. Wired into <c>BedToSpec</c> now; reused by the M2 TOML reader.
    /// </summary>
    public static Diagnosed<OrderedCutsDiscretizer> Create(
        IReadOnlyList<string> order, IReadOnlyList<string> cuts, BinEnds ends)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(cuts);

        var diagnostics = CutValidation.ValidateOrdered(order, cuts, ends);
        return diagnostics.Count == 0
            ? Diagnosed<OrderedCutsDiscretizer>.Ok(new OrderedCutsDiscretizer(order, cuts, ends))
            : Diagnosed<OrderedCutsDiscretizer>.Failed(diagnostics);
    }

    public override string Kind => "ordered_cuts";

    public override BinResult Discretize(string rawValue)
    {
        if (!_positionByValue.TryGetValue(rawValue, out var position))
        {
            return BinResult.Unknown(rawValue); // not a known category → unknown (§11.8, subject to policy)
        }

        var label = CutBinLabels.LabelFor(_binLabels, FirstCutAbove(position), Cuts.Count, Ends);
        return label is null ? BinResult.NoBin : BinResult.Bin(label); // null ⇒ out of a closed range (§11.2)
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

        // Internal invariant: Create validates cuts ⊆ order before constructing, so the
        // private ctor never reaches here; a violation is a bug (OrderedCutsCutNotInDomain).
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
