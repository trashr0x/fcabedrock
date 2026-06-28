using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Discretization;

/// <summary>
/// Cut-spec validation via the discretizer smart factories (spec §11.2 / §11.8,
/// decisions.md D-056). Each rule maps to a distinct code; an invalid spec yields
/// no discretizer (P-10), so <see cref="ManualCutsDiscretizer.Create"/> /
/// <see cref="OrderedCutsDiscretizer.Create"/> never construct an illegal one.
/// </summary>
public sealed class CutValidationTests
{
    private static readonly string[] Order = ["A", "B", "C"];

    private static IReadOnlyList<DiagnosticCode> ManualCodes(double[] cuts, BinEnds ends) =>
        [.. ManualCutsDiscretizer.Create(cuts, ends, CultureInfo.InvariantCulture).Diagnostics.Select(d => d.Code)];

    private static IReadOnlyList<DiagnosticCode> OrderedCodes(string[] order, string[] cuts, BinEnds ends) =>
        [.. OrderedCutsDiscretizer.Create(order, cuts, ends).Diagnostics.Select(d => d.Code)];

    // --- manual_cuts (§11.2) ---

    [Fact]
    public void CreateManual_WhenNoCuts_ThenCutsTooFew() =>
        Assert.Contains(DiagnosticCode.DiscretizerCutsTooFew, ManualCodes([], BinEnds.Open));

    [Theory]
    [InlineData(40.0, 30.0)] // descending
    [InlineData(30.0, 30.0)] // duplicate is not strictly ascending
    public void CreateManual_WhenNotStrictlyAscending_ThenCutsNotAscending(double a, double b) =>
        Assert.Contains(DiagnosticCode.DiscretizerCutsNotAscending, ManualCodes([a, b], BinEnds.Open));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CreateManual_WhenSingleNonFiniteCut_ThenCutsNotAscending(double cut) =>
        // A non-finite cut produces nonsense bin edges (<NaN, >=Infinity); rejected even though
        // the pairwise ascending check does not catch a single value.
        Assert.Contains(DiagnosticCode.DiscretizerCutsNotAscending, ManualCodes([cut], BinEnds.Open));

    [Fact]
    public void CreateManual_WhenNonFiniteCutInAscendingPosition_ThenCutsNotAscending() =>
        // [1, +∞] is numerically "ascending" yet not finite — still rejected.
        Assert.Contains(
            DiagnosticCode.DiscretizerCutsNotAscending,
            ManualCodes([1.0, double.PositiveInfinity], BinEnds.Open));

    [Fact]
    public void CreateManual_WhenClosedEndsAndOneCut_ThenEndsClosedTooFewCuts() =>
        Assert.Contains(DiagnosticCode.DiscretizerEndsClosedTooFewCuts, ManualCodes([30], BinEnds.Closed));

    [Fact]
    public void CreateManual_WhenValid_ThenOkWithDiscretizer()
    {
        var result = ManualCutsDiscretizer.Create([30, 40, 50], BinEnds.Open, CultureInfo.InvariantCulture);

        Assert.True(result.IsOk);
        Assert.True(result.TryGetValue(out var discretizer));
        Assert.Equal([30, 40, 50], discretizer.Cuts);
    }

    // --- ordered_cuts (§11.8) ---

    [Fact]
    public void CreateOrdered_WhenOrderHasDuplicate_ThenOrderDomainInvalid() =>
        Assert.Contains(DiagnosticCode.OrderDomainInvalid, OrderedCodes(["A", "A", "B"], ["B"], BinEnds.Open));

    [Fact]
    public void CreateOrdered_WhenOrderHasEmptyEntry_ThenOrderDomainInvalid() =>
        Assert.Contains(DiagnosticCode.OrderDomainInvalid, OrderedCodes(["A", "", "B"], ["B"], BinEnds.Open));

    [Fact]
    public void CreateOrdered_WhenCutNotInOrder_ThenCutNotInDomain() =>
        Assert.Contains(DiagnosticCode.OrderedCutsCutNotInDomain, OrderedCodes(Order, ["Z"], BinEnds.Open));

    [Fact]
    public void CreateOrdered_WhenCutsDescendingByPosition_ThenNotAscending() =>
        Assert.Contains(DiagnosticCode.OrderedCutsNotAscending, OrderedCodes(Order, ["C", "B"], BinEnds.Open));

    [Fact]
    public void CreateOrdered_WhenClosedEndsAndOneCut_ThenEndsClosedTooFewCuts() =>
        Assert.Contains(DiagnosticCode.DiscretizerEndsClosedTooFewCuts, OrderedCodes(Order, ["B"], BinEnds.Closed));

    [Fact]
    public void CreateOrdered_WhenValid_ThenOkWithDiscretizer()
    {
        var result = OrderedCutsDiscretizer.Create(Order, ["B"], BinEnds.Open);

        Assert.True(result.IsOk);
        Assert.True(result.TryGetValue(out var discretizer));
        Assert.Equal(["B"], discretizer.Cuts);
    }

    // --- aggregation (Diagnosed over Result): independent problems reported together ---

    [Fact]
    public void CreateOrdered_WhenOrderInvalidAndCutNotInDomain_ThenBothReported()
    {
        var codes = OrderedCodes(["A", "A"], ["Z"], BinEnds.Open);

        Assert.Contains(DiagnosticCode.OrderDomainInvalid, codes);
        Assert.Contains(DiagnosticCode.OrderedCutsCutNotInDomain, codes);
    }

    // --- the factory snapshots its inputs: a valid discretizer cannot be desynced post-build (P-10) ---

    [Fact]
    public void CreateManual_WhenInputListMutatedAfterCreate_ThenDiscretizerUnaffected()
    {
        var cuts = new List<double> { 30, 40, 50 };
        var discretizer = ManualCutsDiscretizer.Create(cuts, BinEnds.Open, CultureInfo.InvariantCulture).Value!;

        cuts[0] = 999; // mutate the caller's list after construction

        Assert.Equal([30, 40, 50], discretizer.Cuts);
        Assert.Equal(BinResult.Bin("[30, 40)"), discretizer.Discretize("35"));
    }

    [Fact]
    public void CreateOrdered_WhenInputListsMutatedAfterCreate_ThenDiscretizerUnaffected()
    {
        var order = new List<string> { "A", "B", "C" };
        var cuts = new List<string> { "B" };
        var discretizer = OrderedCutsDiscretizer.Create(order, cuts, BinEnds.Open).Value!;

        order[0] = "Z";
        cuts[0] = "Z";

        Assert.Equal(["A", "B", "C"], discretizer.Order);
        Assert.Equal(["B"], discretizer.Cuts);
    }
}
