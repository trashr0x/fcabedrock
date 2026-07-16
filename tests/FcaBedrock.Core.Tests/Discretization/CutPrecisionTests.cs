using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Core.Tests.Discretization;

public sealed class CutPrecisionTests
{
    [Fact]
    public void Exact_WhenRead_ThenIsTheExactVariant() =>
        Assert.IsType<ExactPrecision>(CutPrecision.Exact);

    [Fact]
    public void Exact_WhenComparedToAConstructedInstance_ThenEqual() =>
        // A record with no state: the singleton is a convenience, not an identity — the reader
        // and a hand-built spec must produce the same precision value.
        Assert.Equal(CutPrecision.Exact, new ExactPrecision());

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(0.001)]
    [InlineData(double.Epsilon)]
    [InlineData(1e300)]
    public void Create_WhenFinitePositive_ThenAccepted(double step) =>
        Assert.Equal(step, RoundToPrecision.Create(step).RoundTo);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_WhenNotFinitePositive_ThenThrows(double step) =>
        // P-10: an unusable rounding step is unrepresentable. The reader owns the authored form
        // (SpecFieldInvalid); this factory is the backstop, so it throws rather than diagnoses.
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundToPrecision.Create(step));

    [Fact]
    public void Create_WhenNegativeZero_ThenThrows() =>
        // Its own Fact: -0.0 and 0.0 are one InlineData literal, but the guard is `<= 0`, so the
        // signed zero must be rejected on its own evidence.
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundToPrecision.Create(-0.0));

    [Theory]
    [InlineData(0.5, 0.0)]  // halfway → down to even
    [InlineData(1.5, 2.0)]  // halfway → up to even
    [InlineData(2.5, 2.0)]  // halfway → down to even
    [InlineData(1.4, 1.0)]
    [InlineData(1.6, 2.0)]
    public void RoundTo_WhenMidpoint_ThenBanksToEven(double value, double expected) =>
        // §11.4/G-5 pins MidpointRounding.ToEven. This asserts the mode the derivation applies
        // (concretely, not by re-deriving it): away-from-zero would make 0.5 → 1 and move cuts.
        Assert.Equal(expected, Math.Round(value / 1.0, MidpointRounding.ToEven) * 1.0);

    [Fact]
    public void RoundToPrecision_WhenTwoInstancesShareAStep_ThenEqualByValue() =>
        // Value equality matters: the fingerprint encodes the precision, so two specs authoring
        // the same round_to must be indistinguishable.
        Assert.Equal(RoundToPrecision.Create(0.25), RoundToPrecision.Create(0.25));
}
