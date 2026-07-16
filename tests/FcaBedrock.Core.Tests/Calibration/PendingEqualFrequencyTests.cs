using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Calibration;

/// <summary>
/// The <c>equal_frequency</c> pending carrier (§11.5, M4 Slice D / D-103): the resolved
/// configuration calibration must fill in, and the one state that can represent an
/// uncalibrated <c>equal_frequency</c> attribute — a state that can never plan or emit.
/// </summary>
public sealed class PendingEqualFrequencyTests
{
    [Fact]
    public void Kind_WhenBuilt_ThenExactlyEqualFrequency() =>
        Assert.Equal("equal_frequency", new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue).Kind);

    [Fact]
    public void Properties_WhenBuilt_ThenCarryTheResolvedConfiguration()
    {
        var config = new PendingEqualFrequency(7, TiePolicy.Right, CutPlacement.Midpoint);

        Assert.Equal(7, config.Bins);
        Assert.Equal(TiePolicy.Right, config.TiePolicy);
        Assert.Equal(CutPlacement.Midpoint, config.CutPlacement);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WhenBinsIsBelowTwo_ThenThrows(int bins) =>
        // The reader owns the authored form (SpecFieldInvalid, §11.5); this is the P-10 backstop,
        // so a carrier that cannot produce two bins is unrepresentable.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PendingEqualFrequency(bins, TiePolicy.Left, CutPlacement.RightValue));

    [Fact]
    public void Create_WhenTiePolicyIsUndefined_ThenThrows() =>
        // A cast can smuggle an undefined member into an enum-typed parameter; the switches that
        // spell it for the fingerprint would then have no answer (P-10).
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PendingEqualFrequency(4, (TiePolicy)99, CutPlacement.RightValue));

    [Fact]
    public void Create_WhenCutPlacementIsUndefined_ThenThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PendingEqualFrequency(4, TiePolicy.Left, (CutPlacement)99));

    [Theory]
    [InlineData(TiePolicy.Left)]
    [InlineData(TiePolicy.Right)]
    public void Create_WhenEveryTiePolicyMember_ThenAccepted(TiePolicy policy) =>
        Assert.Equal(policy, new PendingEqualFrequency(4, policy, CutPlacement.RightValue).TiePolicy);

    [Theory]
    [InlineData(CutPlacement.RightValue)]
    [InlineData(CutPlacement.Midpoint)]
    public void Create_WhenEveryCutPlacementMember_ThenAccepted(CutPlacement placement) =>
        Assert.Equal(placement, new PendingEqualFrequency(4, TiePolicy.Left, placement).CutPlacement);

    [Fact]
    public void Equality_WhenSameConfiguration_ThenValueEqual() =>
        // A record over immutable scalars: no state can be shared or mutated after construction.
        Assert.Equal(
            new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue),
            new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue));

    [Fact]
    public void Equality_WhenConfigurationDiffers_ThenNotEqual()
    {
        var left = new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue);

        Assert.NotEqual(left, new PendingEqualFrequency(4, TiePolicy.Right, CutPlacement.RightValue));
        Assert.NotEqual(left, new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.Midpoint));
        Assert.NotEqual(left, new PendingEqualFrequency(5, TiePolicy.Left, CutPlacement.RightValue));
    }

    // --- The carrier can never execute ----------------------------------------

    [Fact]
    public void CalibrationPending_WhenCarryingEqualFrequency_ThenItsKindIsTheConfigsKind() =>
        Assert.Equal(
            "equal_frequency",
            new CalibrationPending(
                new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue),
                CultureInfo.InvariantCulture).Kind);

    [Fact]
    public void CalibrationPending_WhenDiscretizeIsCalled_ThenThrowsBecauseCalibrationWasSkipped()
    {
        // No public path makes an uncalibrated equal_frequency executable: reaching Discretize is
        // a mis-sequenced call, not user input (D-093).
        var pending = new CalibrationPending(
            new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue), CultureInfo.InvariantCulture);

        var ex = Assert.Throws<InvalidOperationException>(() => pending.Discretize("1"));
        Assert.Contains("equal_frequency", ex.Message, StringComparison.Ordinal);
    }
}
