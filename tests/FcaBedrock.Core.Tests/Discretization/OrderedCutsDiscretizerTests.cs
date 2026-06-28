using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class OrderedCutsDiscretizerTests
{
    private static readonly string[] Employment = ["Unskilled", "Clerical", "Professional", "Managerial"];

    private static OrderedCutsDiscretizer Cut(params string[] cuts) =>
        OrderedCutsDiscretizer.Create(Employment, cuts, BinEnds.Open).Value!;

    [Fact]
    public void Kind_WhenOrderedCuts_ThenIsTheOrderedCutsString() =>
        Assert.Equal("ordered_cuts", Cut("Managerial").Kind);

    [Theory]
    [InlineData("Unskilled", "<Managerial")]
    [InlineData("Clerical", "<Managerial")]
    [InlineData("Professional", "<Managerial")]
    [InlineData("Managerial", ">=Managerial")] // the cut category itself goes into the upper bin
    public void Discretize_WhenCutAtTopCategory_ThenSplitsBelowAndAtOrAbove(string raw, string expected) =>
        Assert.Equal(BinResult.Bin(expected), Cut("Managerial").Discretize(raw));

    [Fact]
    public void Discretize_WhenValueNotInOrder_ThenUnknown() =>
        // §11.8: a value not in order is unknown (subject to unknown_value_policy), not a silent no-bin.
        Assert.Equal(BinResult.Unknown("Director"), Cut("Managerial").Discretize("Director"));

    [Fact]
    public void BinLabels_WhenSingleCut_ThenBelowAndAtOrAbove() =>
        Assert.Equal(["<Managerial", ">=Managerial"], Cut("Managerial").BinLabels([]));

    [Theory]
    [InlineData("Unskilled", "<Clerical")]
    [InlineData("Clerical", "[Clerical, Managerial)")]
    [InlineData("Professional", "[Clerical, Managerial)")]
    [InlineData("Managerial", ">=Managerial")]
    public void Discretize_WhenMultipleCuts_ThenLocatesInteriorBin(string raw, string expected) =>
        Assert.Equal(BinResult.Bin(expected), Cut("Clerical", "Managerial").Discretize(raw));

    [Fact]
    public void Discretize_WhenAnyKnownValue_ThenResultIsAlwaysAKnownBinLabel()
    {
        var discretizer = Cut("Clerical", "Managerial");
        var labels = discretizer.BinLabels([]);

        foreach (var raw in Employment)
        {
            Assert.True(discretizer.Discretize(raw).TryGetLabel(out var label));
            Assert.Contains(label, labels);
        }
    }

    [Fact]
    public void RenderBinLabel_WhenV2CompatInterior_ThenBecomesToLess() =>
        Assert.Equal(
            "Clericalto<Managerial",
            Cut("Clerical", "Managerial").RenderBinLabel("[Clerical, Managerial)", LabelStyle.V2Compat));
}
