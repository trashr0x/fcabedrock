using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The pinned §11.5 numeric rules (D-088/D-103, the G-5 formulas): exact-rational rank
/// selection, the feasibility window, and sign-aware cut placement.
/// <para>
/// Every expected value here is <b>derived by hand in the test</b> — from the literal
/// population, the stated integer arithmetic, or a bit pattern — never by calling the
/// production rank/window/placement helper the test is checking. A test that re-ran the
/// formula would agree with any formula, including a wrong one.
/// </para>
/// </summary>
public sealed class QuantileSelectionTests
{
    // --- Exact rational rank selection (UInt128) ------------------------------

    [Fact]
    public void TargetReached_WhenTargetIsBelowTheGroupEdge_ThenReachedAtThatGroup()
    {
        // [1,2,2,2,3,4], bins = 3: C = [1,4,5,6], N = 6. Boundary k = 1 targets N·k = 6.
        // Group 1: C_1·bins = 1·3 = 3, and 3 >= 6 is false.
        // Group 2: C_2·bins = 4·3 = 12, and 12 >= 6 is true → the target lands in group 2.
        Assert.False(QuantileSelection.TargetReached(cumulative: 1, total: 6, bins: 3, k: 1));
        Assert.True(QuantileSelection.TargetReached(cumulative: 4, total: 6, bins: 3, k: 1));
    }

    [Fact]
    public void TargetIsEdge_WhenTargetLandsExactlyOnAGroupEdge_ThenTrue()
    {
        // Same population, boundary k = 2 targets N·k = 12; group 2's C_2·bins = 4·3 = 12.
        // Equal, so the boundary already separates whole groups: no tie exists to resolve.
        Assert.True(QuantileSelection.TargetIsEdge(cumulative: 4, total: 6, bins: 3, k: 2));

        // Boundary k = 1 targets 6, which is strictly inside group 2 (3 < 6 < 12) — not an edge.
        Assert.False(QuantileSelection.TargetIsEdge(cumulative: 4, total: 6, bins: 3, k: 1));
    }

    [Fact]
    public void TargetReached_WhenTotalExceedsTwoToThe53_ThenSelectsExactlyWhereIntegerArithmeticSays()
    {
        // The discriminating vector: N = 9,007,199,254,740,989 (just below 2^53), k = 2, bins = 3.
        //   target  = N·k     = 9007199254740989 · 2 = 18014398509481978
        //   group 1 : C_1·bins = 6004799503160659 · 3 = 18014398509481977
        // 18014398509481977 >= 18014398509481978 is FALSE, so the target is NOT reached at group 1.
        const long total = 9_007_199_254_740_989;
        const long cumulative = 6_004_799_503_160_659;

        Assert.False(QuantileSelection.TargetReached(cumulative, total, bins: 3, k: 2));

        // The next group reaches it: C_2·bins = 6004799503160660 · 3 = 18014398509481980 >= target.
        Assert.True(QuantileSelection.TargetReached(cumulative + 1, total, bins: 3, k: 2));
    }

    [Fact]
    public void TargetReached_WhenTotalExceedsTwoToThe53_ThenDoesNotAgreeWithTheDoubleRankPath()
    {
        // Why the UInt128 cross-multiplication is load-bearing rather than pedantry. The naive
        // implementation forms the rank target as a double and compares the cumulative to it:
        const long total = 9_007_199_254_740_989;
        const long cumulative = 6_004_799_503_160_659;
        var doubleRankTarget = (double)total * 2 / 3;

        // Above 2^52 doubles are spaced 1 apart, so the exact target 6004799503160659.333…
        // rounds DOWN to exactly the cumulative — and the naive test says "reached".
        Assert.Equal(6_004_799_503_160_659d, doubleRankTarget);
        Assert.True(cumulative >= doubleRankTarget);

        // Exact arithmetic says otherwise, and exact arithmetic is what §11.5 requires: this
        // boundary belongs one group later. The two paths genuinely disagree here.
        Assert.False(QuantileSelection.TargetReached(cumulative, total, bins: 3, k: 2));
    }

    // --- The group-edge and tie-policy rule -----------------------------------

    [Theory]
    [InlineData(TiePolicy.Left)]
    [InlineData(TiePolicy.Right)]
    public void DesiredGap_WhenTargetIsAnExactEdge_ThenGroupIndexRegardlessOfPolicy(TiePolicy policy) =>
        // §11.5: an exact group edge has no tie to resolve, so both policies agree on d = i.
        Assert.Equal(4, QuantileSelection.DesiredGap(groupIndex: 4, isEdge: true, policy));

    [Fact]
    public void DesiredGap_WhenTargetIsStrictlyInsideAGroup_ThenPolicyDecidesTheSide()
    {
        // "left" puts the whole tied group below the cut (d = i); "right" above it (d = i - 1).
        Assert.Equal(4, QuantileSelection.DesiredGap(groupIndex: 4, isEdge: false, TiePolicy.Left));
        Assert.Equal(3, QuantileSelection.DesiredGap(groupIndex: 4, isEdge: false, TiePolicy.Right));
    }

    // --- The feasibility window -----------------------------------------------

    [Fact]
    public void AllocateGap_WhenTheDesiredGapIsFeasible_ThenTakenUnchanged() =>
        // m = 6, bins = 3, k = 1, p = 0: lo = 1, hi = 6-1-(3-1-1) = 4. d = 2 is inside [1,4].
        Assert.Equal(2, QuantileSelection.AllocateGap(desired: 2, previousGap: 0, distinctCount: 6, bins: 3, k: 1));

    [Fact]
    public void AllocateGap_WhenTheDesiredGapCollidesWithThePrevious_ThenPushedUpByLo() =>
        // Two boundaries preferring the same tied group: lo = p + 1 forces strict ascent, so the
        // second takes the next gap rather than duplicating the first (§11.5's own example).
        Assert.Equal(3, QuantileSelection.AllocateGap(desired: 2, previousGap: 2, distinctCount: 6, bins: 3, k: 2));

    [Fact]
    public void AllocateGap_WhenTheDesiredGapIsZero_ThenPushedUpToTheFirstGap() =>
        // d = 0 from "right" on the first tied group is not a gap at all; lo = 1 rescues it.
        Assert.Equal(1, QuantileSelection.AllocateGap(desired: 0, previousGap: 0, distinctCount: 2, bins: 2, k: 1));

    [Fact]
    public void AllocateGap_WhenTheDesiredGapIsTheDomainEnd_ThenPulledDownByHi() =>
        // d = m from "left" on the last tied group: m = 5, bins = 2, k = 1 → hi = 5-1-0 = 4.
        Assert.Equal(4, QuantileSelection.AllocateGap(desired: 5, previousGap: 0, distinctCount: 5, bins: 2, k: 1));

    [Fact]
    public void AllocateGap_WhenLaterBoundariesNeedRoom_ThenHiReservesAGapForEachOfThem()
    {
        // The reservation term is what makes the distinct-gap obligation total: with m = 4 and
        // bins = 4, three boundaries must fit in gaps 1..3, so boundary 1 cannot take gap 3 even
        // if it prefers it — hi = 4-1-(4-1-1) = 1. A greedy allocator without the reservation
        // would strand the later boundaries.
        Assert.Equal(1, QuantileSelection.AllocateGap(desired: 3, previousGap: 0, distinctCount: 4, bins: 4, k: 1));
        Assert.Equal(2, QuantileSelection.AllocateGap(desired: 3, previousGap: 1, distinctCount: 4, bins: 4, k: 2));
        Assert.Equal(3, QuantileSelection.AllocateGap(desired: 3, previousGap: 2, distinctCount: 4, bins: 4, k: 3));
    }

    [Fact]
    public void AllocateGap_WhenEveryBoundaryIsAllocated_ThenGapsStrictlyAscendAndStayInRange()
    {
        // The window's two guarantees, over every (m, bins) shape with m >= bins and every
        // desired preference in 0..m: gaps strictly ascend, and each is a real gap (1..m-1).
        // Ascent is therefore structural — the post-hoc validity check is defense in depth.
        for (var m = 2; m <= 8; m++)
        {
            for (var bins = 2; bins <= m; bins++)
            {
                for (var desired = 0; desired <= m; desired++)
                {
                    var previous = 0;
                    for (var k = 1; k <= bins - 1; k++)
                    {
                        var gap = QuantileSelection.AllocateGap(desired, previous, m, bins, k);
                        Assert.True(gap > previous, $"m={m} bins={bins} d={desired} k={k}: {gap} must exceed {previous}");
                        Assert.InRange(gap, 1, m - 1);
                        previous = gap;
                    }
                }
            }
        }
    }

    // --- Cut placement --------------------------------------------------------

    [Fact]
    public void PlaceCut_WhenRightValue_ThenTheGapsUpperValue() =>
        Assert.Equal(9.0, QuantileSelection.PlaceCut(5.0, 9.0, CutPlacement.RightValue));

    [Fact]
    public void PlaceCut_WhenMidpointOverAPlainGap_ThenHalfway() =>
        Assert.Equal(7.0, QuantileSelection.PlaceCut(5.0, 9.0, CutPlacement.Midpoint));

    [Fact]
    public void PlaceCut_WhenMidpointOverSameSignExtremes_ThenFinite()
    {
        // Both bounds near the top of the double range and the SAME sign: the subtraction form is
        // safe (b - a is bounded by the larger magnitude) while the average form would overflow.
        // (1e308 + 1.7e308) is +∞; a + (b - a)/2 = 1e308 + 3.5e307 = 1.35e308.
        var cut = QuantileSelection.PlaceCut(1e308, 1.7e308, CutPlacement.Midpoint);

        Assert.True(double.IsFinite(cut));
        Assert.Equal(1.35e308, cut);
        Assert.True(double.IsInfinity(1e308 + 1.7e308)); // the form NOT used here would overflow
    }

    [Fact]
    public void PlaceCut_WhenMidpointOverOppositeSignExtremes_ThenFinite()
    {
        // Opposite signs at the extremes: now the SUBTRACTION overflows (b - a = 3.4e308 = +∞)
        // and the average form is the safe one. This is why the two branches cannot be folded
        // into one expression — each is the safe form for exactly the case the other breaks on.
        var cut = QuantileSelection.PlaceCut(-1.7e308, 1.7e308, CutPlacement.Midpoint);

        Assert.True(double.IsFinite(cut));
        Assert.Equal(0.0, cut);
        Assert.True(double.IsInfinity(1.7e308 - -1.7e308)); // the form NOT used here would overflow
    }

    [Fact]
    public void PlaceCut_WhenMidpointOverAsymmetricOppositeSignExtremes_ThenFiniteAndInsideTheGap()
    {
        // Real arithmetic says (-1.7e308 + 1.6e308)/2 = -5e306, but neither bound is exactly
        // representable, so binary64 lands a few ULPs away. The contract is finiteness and
        // membership — the cut must lie strictly inside its own gap — not a decimal ideal, so the
        // exact result is pinned by its bit pattern rather than by a tolerance that would hide a
        // reordered expression.
        var cut = QuantileSelection.PlaceCut(-1.7e308, 1.6e308, CutPlacement.Midpoint);

        Assert.True(double.IsFinite(cut));
        Assert.True(cut > -1.7e308 && cut < 1.6e308);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(-4.9999999999999981E+306),
            BitConverter.DoubleToInt64Bits(cut));
    }

    [Fact]
    public void PlaceCut_WhenMidpointFallsOnAdjacentRepresentableDoubles_ThenFallsBackToTheUpperValue()
    {
        // No double lies strictly between 1.0 and its successor, so the midpoint cannot land above
        // the lower value; the cut falls back to the upper one — membership-identical under the
        // half-open [lo, hi) geometry, and it keeps the cut inside its own gap.
        var upper = Math.BitIncrement(1.0);

        var cut = QuantileSelection.PlaceCut(1.0, upper, CutPlacement.Midpoint);

        Assert.Equal(BitConverter.DoubleToInt64Bits(upper), BitConverter.DoubleToInt64Bits(cut));
        Assert.True(cut > 1.0);
    }

    [Fact]
    public void PlaceCut_WhenMidpointRoundsUpOntoTheUpperValue_ThenLeftAloneRatherThanCorrected()
    {
        // The genuine "midpoint == upper" case, which needs the rounding to land there rather than
        // merely an interior value. `lower` is chosen with an ODD mantissa and the two values are
        // ADJACENT, so the exact midpoint is half an ulp above `lower` — a perfect tie — and
        // round-half-to-even carries it up onto `upper`, whose mantissa is even.
        var lower = Math.BitIncrement(1.0);          // mantissa …0001 (odd)
        var upper = Math.BitIncrement(lower);        // mantissa …0010 (even)

        var cut = QuantileSelection.PlaceCut(lower, upper, CutPlacement.Midpoint);

        // It landed strictly above `lower`, so the adjacent-double fallback did NOT fire — the cut
        // is the midpoint's own rounded result, and equalling `upper` is already
        // membership-correct under half-open geometry, so it must not be "corrected".
        Assert.True(cut > lower);
        Assert.Equal(BitConverter.DoubleToInt64Bits(upper), BitConverter.DoubleToInt64Bits(cut));
    }

    [Fact]
    public void PlaceCut_WhenMidpointLandsStrictlyInsideAThreeUlpGap_ThenTheInteriorValue()
    {
        // The complement: with room between them the midpoint is interior, not either endpoint.
        var lower = 1.0;
        var upper = Math.BitIncrement(Math.BitIncrement(1.0));

        var cut = QuantileSelection.PlaceCut(lower, upper, CutPlacement.Midpoint);

        Assert.True(cut > lower && cut < upper);
        Assert.Equal(BitConverter.DoubleToInt64Bits(Math.BitIncrement(1.0)), BitConverter.DoubleToInt64Bits(cut));
    }

    [Fact]
    public void PlaceCut_WhenRightValueLandsOnNegativeZero_ThenCanonicalizedToPositiveZero()
    {
        // G-6: a computed -0 must never reach a bin identity, label, or hash. -0.0 == 0.0 under
        // ==, so only the bit pattern can prove the canonicalization actually happened.
        var cut = QuantileSelection.PlaceCut(-1.0, -0.0, CutPlacement.RightValue);

        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(cut));
        Assert.False(double.IsNegative(cut));
    }

    [Fact]
    public void PlaceCut_WhenMidpointCrossesZero_ThenPositiveZero()
    {
        var cut = QuantileSelection.PlaceCut(-4.0, 4.0, CutPlacement.Midpoint);

        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(cut));
    }

    // --- Percentile positions -------------------------------------------------

    [Fact]
    public void PercentileReached_WhenExactlyAtThePosition_ThenTrue()
    {
        // N = 100: p1 needs C_i·100 >= 1·100 = 100 → C_i >= 1; p99 needs C_i·100 >= 99·100 = 9900
        // → C_i >= 99. Both are exact integer comparisons, never an interpolation.
        Assert.True(QuantileSelection.PercentileReached(cumulative: 1, total: 100, percent: 1));
        Assert.False(QuantileSelection.PercentileReached(cumulative: 0, total: 100, percent: 1));
        Assert.True(QuantileSelection.PercentileReached(cumulative: 99, total: 100, percent: 99));
        Assert.False(QuantileSelection.PercentileReached(cumulative: 98, total: 100, percent: 99));
    }

    [Fact]
    public void PercentileReached_WhenTotalIsHuge_ThenExactBeyondWhatALongCanHold()
    {
        // Both sides overflow a long at this scale, so the comparison MUST happen in 128 bits:
        //   99·N     = 99 · 9,000,000,000,000,000,000 = 891,000,000,000,000,000,000
        //   C_i·100  = 8,910,000,000,000,000,000 · 100 = 891,000,000,000,000,000,000
        // both far above long.MaxValue (≈ 9.22e18). p99 is therefore reached exactly at
        // C_i = 8,910,000,000,000,000,000 and not one observation earlier.
        const long total = 9_000_000_000_000_000_000;
        const long atP99 = 8_910_000_000_000_000_000;

        Assert.True(QuantileSelection.PercentileReached(atP99, total, percent: 99));
        Assert.False(QuantileSelection.PercentileReached(atP99 - 1, total, percent: 99));
    }
}
