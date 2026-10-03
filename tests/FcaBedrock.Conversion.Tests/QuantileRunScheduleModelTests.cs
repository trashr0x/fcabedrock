namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// Checks the independent schedule model against hand-derived values before anything relies on it
/// as an oracle: the bound arithmetic, the generation-tiered schedule's digit-sum occupancy and
/// carry sequence, the whole-catalogue schedule's closed forms, and small literal populations under
/// both, all from literal leaf maps and base-F arithmetic, never from the product.
/// </summary>
public sealed class QuantileRunScheduleModelTests
{
    private const int RecordBytes = QuantileRunScheduleModel.RecordBytes;

    private static LiteralQuantileSpillLeaves Literal(int leaves, int rows) =>
        new(QuantileRunScheduleModel.DisjointLiteralLeaves(leaves, rows));

    // --- The bound arithmetic, computed here and never read from the product ------------------

    [Theory]
    [InlineData(2, 63, 63L)]
    [InlineData(3, 40, 80L)]
    [InlineData(16, 16, 240L)]
    public void Levels_WhenComputedByRepeatedDivision_ThenMatchTheHandDerivedCeilings(int fanIn, int levels, long ceiling)
    {
        Assert.Equal(levels, QuantileRunScheduleModel.Levels(fanIn));
        Assert.Equal(ceiling, QuantileRunScheduleModel.Ceiling(fanIn));

        // F^(L-1) must still fit a long and F^L must not: that is what "levels 0..L-1 suffice for
        // every admissible spill count" means, proved without ever forming F^L.
        var power = 1L;
        for (var i = 0; i < levels - 1; i++)
        {
            power = checked(power * fanIn);
        }

        Assert.True(power > long.MaxValue / fanIn, $"F^{levels} must exceed long.MaxValue");
    }

    [Fact]
    public void Ceiling_WhenFanInIsSixteen_ThenTheReachableMaximumIsTheDigitSumNotK()
    {
        // K = 240 is a valid ceiling but is not reachable: the largest base-16 digit sum over a
        // non-negative long is 0x7FFF…F, which is 7 + 15*15 = 232. The identity, not K, is what a
        // test must assert, because a test that only checked <= K would pass a leaking catalogue.
        Assert.Equal(240L, QuantileRunScheduleModel.Ceiling(16));
        Assert.Equal(232L, QuantileRunScheduleModel.DigitSum(long.MaxValue, 16));
        Assert.Equal(63L, QuantileRunScheduleModel.DigitSum(long.MaxValue, 2));
        Assert.Equal(63L, QuantileRunScheduleModel.Ceiling(2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    [InlineData(16, 1)]
    [InlineData(17, 2)]
    [InlineData(32, 2)]
    [InlineData(81, 6)]
    [InlineData(255, 30)]
    [InlineData(256, 1)]
    [InlineData(257, 2)]
    [InlineData(807, 12)]
    public void GenerationTiered_WhenQuiescent_ThenTheCatalogueCountIsTheBaseSixteenDigitSum(long spills, long expected)
    {
        Assert.Equal(expected, QuantileRunScheduleModel.DigitSum(spills, 16));

        if (spills == 0)
        {
            return;
        }

        var schedule = QuantileRunScheduleModel.GenerationTiered(16, Literal((int)spills, 1));
        Assert.Equal(expected, schedule.QuiescentCatalogue);
    }

    [Fact]
    public void GenerationTiered_WhenEightHundredAndSevenLeavesAtFanInSixteen_ThenSevenTwoThreeAcrossLevels()
    {
        // 807 = 3·16² + 2·16 + 7, so the surviving runs are 7 at generation 0, 2 at generation 1 and
        // 3 at generation 2 (twelve in all) after 50 level-0 carries and 3 level-1 carries.
        Assert.Equal([7, 2, 3], QuantileRunScheduleModel.GenerationOccupancy(16, 807));

        var schedule = QuantileRunScheduleModel.GenerationTiered(16, Literal(807, 1));
        Assert.Equal(12, schedule.QuiescentCatalogue);
        Assert.Equal(53, schedule.IntakeMerges);
        Assert.Equal(1, schedule.FinalMerges);
        Assert.Equal(807, schedule.OriginalWrites);
    }

    [Fact]
    public void GenerationTiered_WhenEightyOneLeavesAtFanInSixteen_ThenCarriesFollowEverySixteenthLeaf()
    {
        var leaves = Literal(81, 1);
        var generationTiered = QuantileRunScheduleModel.GenerationTiered(16, leaves);
        var wholeCatalogue = QuantileRunScheduleModel.WholeCatalogue(16, leaves);

        // Generation-tiered: a carry after originals 16, 32, 48, 64 and 80; five level-1 runs plus
        // one level-0 remainder survive, so exactly one final merge follows.
        // 81 = 5·16 + 1, so generation 0 holds one run and generation 1 holds five.
        Assert.Equal([1, 5], QuantileRunScheduleModel.GenerationOccupancy(16, 81));
        Assert.Equal(5, generationTiered.IntakeMerges);
        Assert.Equal(6, generationTiered.QuiescentCatalogue);
        Assert.Equal(1, generationTiered.FinalMerges);
        Assert.Equal(Enumerable.Repeat(16, 5), generationTiered.IntakeMergeRows);

        // Whole-catalogue: the whole catalogue is merged immediately before originals 17, 32, 47, 62
        // and 77, and each merge is larger than the last because it re-reads its own growing output.
        Assert.Equal(5, wholeCatalogue.IntakeMerges);
        Assert.Equal([16, 31, 46, 61, 76], wholeCatalogue.IntakeMergeRows);
        Assert.Equal(1, wholeCatalogue.FinalMerges);
        Assert.True(generationTiered.WrittenBytes < wholeCatalogue.WrittenBytes);
    }

    // --- The whole-catalogue schedule's closed form ---------------------------------------------

    [Theory]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(2, 9)]
    [InlineData(2, 16)]
    [InlineData(3, 20)]
    [InlineData(16, 81)]
    [InlineData(16, 200)]
    public void WholeCatalogue_WhenSimulated_ThenItMatchesTheClosedFormForQAndTheFinalCatalogue(int fanIn, int spills)
    {
        var schedule = QuantileRunScheduleModel.WholeCatalogue(fanIn, Literal(spills, 1));

        // q = max(0, floor((S-2)/(F-1))) online merges, immediately before originals 1 + k(F-1) + 1,
        // leaving S - q(F-1) runs in [2, F] at quiescence.
        var q = Math.Max(0, (spills - 2) / (fanIn - 1));
        Assert.Equal(q, schedule.IntakeMerges);

        var quiescent = spills - (q * (fanIn - 1));
        Assert.Equal(quiescent, schedule.QuiescentCatalogue);
        Assert.InRange(quiescent, Math.Min(2, spills), fanIn);
    }

    [Fact]
    public void WholeCatalogue_WhenAllDistinctAtFixedCapacity_ThenWMatchesTheClosedForm()
    {
        // W/32 = 2N + C·[q + (F-1)·q(q+1)/2] for S >= 2, over full leaves of C rows.
        foreach (var (fanIn, spills, capacity) in new[] { (2, 9, 3), (2, 16, 3), (3, 11, 5), (16, 81, 7) })
        {
            var n = spills * capacity;
            var q = Math.Max(0, (spills - 2) / (fanIn - 1));
            var expectedRows = (2L * n) + (capacity * (q + ((fanIn - 1) * (long)q * (q + 1) / 2)));

            var schedule = QuantileRunScheduleModel.WholeCatalogue(fanIn, Literal(spills, capacity));
            Assert.Equal(expectedRows * RecordBytes, schedule.WrittenBytes);
        }
    }

    // --- A literal F=2, C=3 table, both schedules -----------------------------------------------

    [Theory]
    [InlineData(1, 96L, 288L, 96L, 288L)]
    [InlineData(2, 384L, 960L, 384L, 960L)]
    [InlineData(3, 768L, 1824L, 768L, 1824L)]
    [InlineData(4, 1248L, 2880L, 1152L, 2688L)]
    [InlineData(7, 3264L, 7200L, 2592L, 5856L)]
    [InlineData(8, 4128L, 9024L, 3072L, 6912L)]
    [InlineData(9, 5088L, 11040L, 4032L, 8928L)]
    [InlineData(16, 14496L, 30528L, 7680L, 16896L)]
    public void TinyAllDistinct_WhenFanInTwoCapacityThree_ThenBothSchedulesMatchTheHandDerivedByteTotals(
        int spills, long wholeCatalogueWrites, long wholeCatalogueIo, long generationTieredWrites, long generationTieredIo)
    {
        var leaves = Literal(spills, 3);

        var wholeCatalogue = QuantileRunScheduleModel.WholeCatalogue(2, leaves);
        Assert.Equal(wholeCatalogueWrites, wholeCatalogue.WrittenBytes);
        Assert.Equal(wholeCatalogueIo, wholeCatalogue.TotalIoBytes);

        var generationTiered = QuantileRunScheduleModel.GenerationTiered(2, leaves);
        Assert.Equal(generationTieredWrites, generationTiered.WrittenBytes);
        Assert.Equal(generationTieredIo, generationTiered.TotalIoBytes);

        // The final run is the whole population either way: the schedules differ in how they get
        // there, never in what they end with.
        Assert.Equal(wholeCatalogue.FinalBytes, generationTiered.FinalBytes);
        Assert.Equal(spills * 3 * RecordBytes, generationTiered.FinalBytes);
    }

    [Fact]
    public void TinyAllDistinct_WhenFourAndSevenLeaves_ThenTheHandDerivedMergeRowSequencesFollow()
    {
        var four = Literal(4, 3);
        Assert.Equal([6, 9, 12], Rows(QuantileRunScheduleModel.WholeCatalogue(2, four)));
        Assert.Equal([6, 6, 12], Rows(QuantileRunScheduleModel.GenerationTiered(2, four)));

        var seven = Literal(7, 3);
        var generationTiered = QuantileRunScheduleModel.GenerationTiered(2, seven);
        Assert.Equal([6, 6, 12, 6], generationTiered.IntakeMergeRows);
        Assert.Equal([9, 21], generationTiered.FinalMergeRows);
    }

    [Fact]
    public void FoldingAdversary_WhenTwoIdenticalLeavesPrecedeADisjointPair_ThenTheHandDerivedCountsFollow()
    {
        // {1,2,3}, {1,2,3}, {4,5,6}, {7}: identical folded output sizes must not cause promotion by
        // size, and the generation-tiered schedule must not repeatedly re-merge the oldest dominant
        // run.
        static QuantileSpillLeaf Leaf(params double[] values) => new([.. values.Select(value => new QuantileModelRow(value, 1))]);
        var leaves = new LiteralQuantileSpillLeaves([Leaf(1, 2, 3), Leaf(1, 2, 3), Leaf(4, 5, 6), Leaf(7)]);

        var wholeCatalogue = QuantileRunScheduleModel.WholeCatalogue(2, leaves);
        Assert.Equal([3, 6, 7], Rows(wholeCatalogue));
        Assert.Equal(832L, wholeCatalogue.WrittenBytes);
        Assert.Equal(1888L, wholeCatalogue.TotalIoBytes);

        var generationTiered = QuantileRunScheduleModel.GenerationTiered(2, leaves);
        Assert.Equal([3, 4, 7], Rows(generationTiered));
        Assert.Equal(768L, generationTiered.WrittenBytes);
        Assert.Equal(1760L, generationTiered.TotalIoBytes);

        // Identical final map under both schedules: seven distinct values over ten observations, the
        // first three counted twice.
        Assert.Equal(wholeCatalogue.FinalRows, generationTiered.FinalRows);
        Assert.Equal(7, generationTiered.FinalRows);
        var union = QuantileRunScheduleModel.Union(leaves.Leaves, [0, 1, 2, 3]);
        Assert.Equal(7, union.Count);
        Assert.Equal([2L, 2L, 2L, 1L, 1L, 1L, 1L], union.Select(row => row.Count));
    }

    private static IReadOnlyList<int> Rows(QuantileRunSchedule schedule) =>
        [.. schedule.Writes.Where(write => write.Kind != QuantileRunWriteKind.Original).Select(write => write.Rows)];
}
