using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Planning;

/// <summary>
/// Ordinal scaling over the two authored cut discretizers (§11.2/§11.8/§12.3) across the whole
/// <c>ends × direction × drop_top</c> matrix, through the public factories and the planner.
/// <para>
/// The oracle is independent of the scale's index arithmetic: it models every bin as an explicit
/// interval over the cut axis (a number, or a category's position in the authored order), puts
/// one threshold at each bin's far edge (<c>all</c> where that edge is unbounded), and decides a
/// crossing by comparing the sample's value or position with that edge. It never compares
/// category strings and never reads a rendered bin label.
/// </para>
/// </summary>
public sealed class OrdinalCutsPlanningTests
{
    // Each cut list with its canonical cut labels, written out rather than formatted, so the
    // threshold keys are checked against exactly the labels the cuts render as.
    private static readonly (double[] Cuts, string[] Keys)[] ManualCutLists =
    [
        ([30], ["30"]),
        ([30, 40], ["30", "40"]),
        ([30, 40, 50], ["30", "40", "50"]),
        ([-2.5, 0, 0.25, 10, 1000], ["-2.5", "0", "0.25", "10", "1000"]),
        ([1, 2, 3, 5, 8, 13, 21], ["1", "2", "3", "5", "8", "13", "21"]),
    ];

    // Authored positions disagree with string order, so a lexical comparison would fail.
    private static readonly string[] Order = ["zeta", "alpha", "mid", "beta", "omega", "kappa"];

    [Theory]
    [InlineData(BinEnds.Open, OrdinalDirection.Le, false)]
    [InlineData(BinEnds.Open, OrdinalDirection.Le, true)]
    [InlineData(BinEnds.Open, OrdinalDirection.Ge, false)]
    [InlineData(BinEnds.Open, OrdinalDirection.Ge, true)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Le, false)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Le, true)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Ge, false)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Ge, true)]
    public void Plan_WhenManualCuts_ThenThresholdsAndCrossingsMatchTheEdgeOracle(
        BinEnds ends, OrdinalDirection direction, bool dropTop)
    {
        var checkedLists = 0;
        foreach (var (cuts, keys) in ManualCutLists)
        {
            if (ends == BinEnds.Closed && cuts.Length < 2)
            {
                continue; // closed ends need two cuts (§11.2)
            }

            // Below the first cut, every cut, every midpoint, above the last cut, and two far
            // values; all exactly representable.
            double[] values =
            [
                cuts[0] - 1,
                .. cuts,
                .. cuts.Zip(cuts.Skip(1), (lo, hi) => (lo + hi) / 2),
                cuts[^1] + 1,
                -1e9,
                1e9,
            ];
            var samples = values.Select(value => (value.ToString("R", CultureInfo.InvariantCulture), (double?)value));
            var attribute = new AttributeSpec(
                "age", new ColumnSource(0, SourceValueType.Number), Include: true,
                ManualCutsDiscretizer.Create(cuts, ends, CultureInfo.InvariantCulture).Value!,
                new OrdinalScale(direction, dropTop),
                DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

            AssertMatchesOracle(attribute, cuts, keys, ends, direction, dropTop, samples);
            checkedLists++;
        }

        Assert.True(checkedLists >= 4, $"only {checkedLists} cut lists were checked");
    }

    [Theory]
    [InlineData(BinEnds.Open, OrdinalDirection.Le, false)]
    [InlineData(BinEnds.Open, OrdinalDirection.Le, true)]
    [InlineData(BinEnds.Open, OrdinalDirection.Ge, false)]
    [InlineData(BinEnds.Open, OrdinalDirection.Ge, true)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Le, false)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Le, true)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Ge, false)]
    [InlineData(BinEnds.Closed, OrdinalDirection.Ge, true)]
    public void Plan_WhenOrderedCuts_ThenThresholdsAndCrossingsMatchTheCategoryPositionOracle(
        BinEnds ends, OrdinalDirection direction, bool dropTop)
    {
        // Every category by its authored position, plus a value the order does not list.
        IEnumerable<(string, double?)> samples =
            [.. Order.Select((category, position) => (category, (double?)position)), ("unlisted", null)];

        var checkedSubsets = 0;
        for (var mask = 1; mask < 1 << Order.Length; mask++)
        {
            // One cut subset, ascending by authored position.
            int[] positions = [.. Enumerable.Range(0, Order.Length).Where(position => (mask & (1 << position)) != 0)];
            if (ends == BinEnds.Closed && positions.Length < 2)
            {
                continue; // closed ends need two cuts (§11.2)
            }

            string[] keys = [.. positions.Select(position => Order[position])];
            var attribute = new AttributeSpec(
                "age", new ColumnSource(0, SourceValueType.String), Include: true,
                OrderedCutsDiscretizer.Create(Order, keys, ends).Value!,
                new OrdinalScale(direction, dropTop),
                DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

            AssertMatchesOracle(attribute, [.. positions.Select(position => (double)position)], keys, ends, direction, dropTop, samples);
            checkedSubsets++;
        }

        Assert.Equal(ends == BinEnds.Open ? 63 : 57, checkedSubsets);
    }

    // --- The oracle ---

    // One bin as an interval over the cut axis; a null edge is unbounded.
    private sealed record OracleBin(double? Lo, string? LoKey, double? Hi, string? HiKey)
    {
        public bool Contains(double value) => (Lo is not { } lo || value >= lo) && (Hi is not { } hi || value < hi);

        // A point at the bin's lower edge and one just below its upper edge (one unit inside an
        // unbounded end).
        public IEnumerable<double> Representatives =>
        [
            Lo ?? (Hi!.Value - 1),
            Hi is { } hi ? Math.BitDecrement(hi) : Lo!.Value + 1,
        ];
    }

    private sealed record OracleThreshold(string Op, string Key, Func<double, bool> Holds)
    {
        public string Identity => Pair(Op, Key);
    }

    // An (operator, bin key) identity, e.g. "(<, 40)", "(>=, beta)" or "(, all)".
    private static string Pair(string op, string key) => $"({op}, {key})";

    // The bins §11.2 defines: open ends add an unbounded bin below the first cut and above the
    // last; closed ends keep only the bins between consecutive cuts.
    private static List<OracleBin> Bins(IReadOnlyList<double> cuts, IReadOnlyList<string> keys, BinEnds ends)
    {
        var bins = new List<OracleBin>();
        if (ends == BinEnds.Open)
        {
            bins.Add(new OracleBin(null, null, cuts[0], keys[0]));
        }

        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            bins.Add(new OracleBin(cuts[i], keys[i], cuts[i + 1], keys[i + 1]));
        }

        if (ends == BinEnds.Open)
        {
            bins.Add(new OracleBin(cuts[^1], keys[^1], null, null));
        }

        return bins;
    }

    // One threshold per bin at its far edge; `all` where that edge is unbounded. drop_top removes
    // the threshold that holds at every bin's representatives, and the oracle requires exactly
    // one such threshold: the coverage definition of "tautological".
    private static List<OracleThreshold> Thresholds(List<OracleBin> bins, OrdinalDirection direction, bool dropTop)
    {
        var thresholds = bins.Select(bin => direction == OrdinalDirection.Le
                ? bin.Hi is { } hi ? new OracleThreshold("<", bin.HiKey!, value => value < hi) : Unbounded()
                : bin.Lo is { } lo ? new OracleThreshold(">=", bin.LoKey!, value => value >= lo) : Unbounded())
            .ToList();

        var tautological = Assert.Single(thresholds, threshold => bins.All(bin => bin.Representatives.All(threshold.Holds)));
        if (dropTop)
        {
            thresholds.Remove(tautological);
        }

        return thresholds;

        static OracleThreshold Unbounded() => new("", "all", _ => true);
    }

    private static void AssertMatchesOracle(
        AttributeSpec attribute,
        IReadOnlyList<double> cuts,
        IReadOnlyList<string> keys,
        BinEnds ends,
        OrdinalDirection direction,
        bool dropTop,
        IEnumerable<(string Raw, double? Position)> samples)
    {
        var context = $"cuts [{string.Join(", ", keys)}], ends {ends}, direction {direction}, drop_top {dropTop}";
        var bins = Bins(cuts, keys, ends);
        var thresholds = Thresholds(bins, direction, dropTop);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attribute]);

        var native = PlanOk(spec, LabelStyle.Native);
        var v2Compat = PlanOk(spec, LabelStyle.V2Compat);
        foreach (var plan in (ConversionPlan[])[native, v2Compat])
        {
            Assert.Equal(dropTop ? bins.Count - 1 : bins.Count, plan.FormalAttributes.Count);
            AssertSame(context, thresholds.Select(t => t.Identity), plan.FormalAttributes.Select(Identity));
            Assert.All(plan.FormalAttributes, f => Assert.Equal("ordinal", f.Identity.Scale));
            AssertSame(
                context,
                thresholds.Select(t => t.Op.Length == 0 ? "age-all" : $"age-{t.Op}{t.Key}"),
                plan.FormalAttributes.Select(f => f.RenderedName)); // the same in both label styles
            Assert.Equal(thresholds.Select(t => (CanonicalBin)new ValueBin(t.Key)), plan.FormalAttributes.Select(f => f.Bin));

            foreach (var (raw, position) in samples)
            {
                // The source value goes through the plan's discretizer and the bin's planned
                // crossings, against the oracle's comparison of the value with each edge.
                string[] actual = plan.Attributes[0].Discretizer.Discretize(raw).TryGetLabel(out var label)
                    && plan.Attributes[0].CrossesByBin.TryGetValue(label, out var ids)
                    ? [.. ids.Select(id => Identity(plan.FormalAttributes[id]))]
                    : [];
                string[] expected = position is { } value && bins.Any(bin => bin.Contains(value))
                    ? [.. thresholds.Where(t => t.Holds(value)).Select(t => t.Identity)]
                    : [];
                AssertSame($"{context}, raw {raw}", expected, actual);
            }
        }

        // The label style changes no identity, name or crossing, and planning is repeatable (EP-7).
        (ConversionPlan First, ConversionPlan Second)[] pairs =
        [
            (native, v2Compat),
            (native, PlanOk(spec, LabelStyle.Native)),
            (v2Compat, PlanOk(spec, LabelStyle.V2Compat)),
        ];
        foreach (var (first, second) in pairs)
        {
            Assert.Equal(first.FormalAttributes, second.FormalAttributes);
            Assert.Equal(Crossings(first), Crossings(second));
        }
    }

    private static string Identity(FormalAttribute attribute) => Pair(attribute.Identity.Operator, attribute.Identity.BinKey);

    private static ConversionPlan PlanOk(BedrockSpec spec, LabelStyle style)
    {
        var planned = ConversionPlanner.Plan(
            CalibratedSpec.FromFullyDeclared(SpecFixtures.Resolve(spec, new SourceSchema(1))), style);
        Assert.True(planned.TryGetValue(out var plan), string.Join("; ", planned.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return plan!;
    }

    // Compares two sequences with the case named in the failure message.
    private static void AssertSame(string context, IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal($"{context}: [{string.Join(" ", expected)}]", $"{context}: [{string.Join(" ", actual)}]");

    private static string Crossings(ConversionPlan plan) =>
        string.Join("; ", plan.Attributes[0].CrossesByBin
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key} -> {string.Join(",", entry.Value)}"));
}
