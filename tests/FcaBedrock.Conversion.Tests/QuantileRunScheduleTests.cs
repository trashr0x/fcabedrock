using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// Drives the <b>real</b> accumulator and workspace over the counting filesystem and holds the
/// observed transitions against the independent model: never a handle simulator, and never the
/// product's own idea of what it did.
/// <para>
/// The central assertion is that wherever the two modelled schedules differ, the observation matches
/// the <b>generation-tiered</b> schedule and the superseded whole-catalogue schedule <b>rejects</b>
/// it: a product reverted to whole-catalogue consolidation is classified as
/// <c>QuantileRunScheduleKind.WholeCatalogue</c> and fails here. Nothing in that proof can pass by
/// agreeing with itself: the expected schedules come from the independent model.
/// </para>
/// </summary>
public sealed class QuantileRunScheduleTests
{
    private const int FanIn = 16;

    // The accepted capacity at a below-floor budget: the runtime's own prime rounding of one
    // requested entry. Read once from the product's floor rather than guessed, then asserted.
    private static readonly int FloorCapacity =
        new Dictionary<double, long>(1).EnsureCapacity(1);

    [Fact]
    public void FloorCapacity_WhenTheBudgetIsBelowTheFloor_ThenTheAcceptedCapacityIsStable()
    {
        using var driver = QuantileRunScheduleDriver.Create(budget: 1, FanIn);
        Assert.Equal(FloorCapacity, driver.Accumulator.Capacity);
        Assert.True(FloorCapacity >= 1);
    }

    // --- The literal F=2, C=3 table, driven through the product --------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    public void Intake_WhenAllDistinctAtFanInTwo_ThenTheGenerationTieredScheduleMatches(int spills)
    {
        const int capacity = 3;
        var leaves = QuantileRunScheduleModel.DisjointLiteralLeaves(spills, capacity);
        using var driver = QuantileRunScheduleDriver.Create(budget: QuantileAccumulator.Modeled(capacity), fanIn: 2);
        Assert.Equal(capacity, driver.Accumulator.Capacity);

        driver.Feed(leaves);
        var observed = driver.Complete();

        Assert.Equal(spills, observed.OriginalWrites);
        RequireGenerationTieredSchedule(2, new LiteralQuantileSpillLeaves(leaves), observed, spills);
    }

    // --- The real accumulator and workspace at the production fan-in ---------------------------

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(32)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(807)]
    public void Intake_WhenFanInSixteenAtFloorCapacity_ThenEveryTransitionMatchesTheGenerationTieredSchedule(int spills)
    {
        var leaves = QuantileRunScheduleModel.DisjointLiteralLeaves(spills, FloorCapacity);
        using var driver = QuantileRunScheduleDriver.Create(budget: 1, FanIn);
        Assert.Equal(FloorCapacity, driver.Accumulator.Capacity);

        driver.Feed(leaves);
        var observed = driver.Complete();

        // The F-th and F²-th leaf arrive two different ways across this set: at 16 and 256 the last
        // leaf is the EndIntake remainder flush, at 17 and 257 it was capacity-triggered inside Add.
        Assert.Equal(spills, observed.OriginalWrites);
        RequireGenerationTieredSchedule(FanIn, new LiteralQuantileSpillLeaves(leaves), observed, spills);
        RequireBounds(driver, observed, FanIn);
        RequireRunBytesAndPasses(driver, observed);
    }

    [Fact]
    public void Intake_WhenFanInSixteenThroughEightHundredAndSevenSpills_ThenCutsAndBoundsSurvive()
    {
        const int spills = 807;
        var leaves = QuantileRunScheduleModel.DisjointLiteralLeaves(spills, FloorCapacity);
        using var driver = QuantileRunScheduleDriver.Create(budget: 1, FanIn);

        driver.Feed(leaves);
        var observed = driver.Complete();
        RequireGenerationTieredSchedule(FanIn, new LiteralQuantileSpillLeaves(leaves), observed, spills);

        // The population is 1, 2, … N with one observation each, so the exact quartile cuts are
        // arithmetic, not something the product may decide.
        var population = spills * FloorCapacity;
        Assert.Equal(population, driver.Accumulator.Total);

        var cuts = driver.Accumulator.TryExtractEqualFrequencyCuts(
            new PendingEqualFrequency(4, TiePolicy.Left, CutPlacement.RightValue), out var distinct);
        Assert.Equal(population, distinct);
        Assert.NotNull(cuts);
        // Values are 1..N with one observation each, so C_i = i and boundary k takes the least i
        // with i*bins >= N*k; the right-value cut is then v_(i+1) = i + 1.
        var expectedCuts = new double[3];
        for (var k = 1; k <= 3; k++)
        {
            expectedCuts[k - 1] = (((population * k) + 3) / 4) + 1;
        }

        Assert.Equal(expectedCuts, cuts);

        // The consolidated run is replayed exactly twice, and nothing survives the release.
        var finalOrdinal = driver.Files.All.Single(run => run.Created && !run.Deleted).Ordinal;
        Assert.Equal(2, driver.Files.Counters(finalOrdinal).CompletedPasses);
        Assert.Equal(0, driver.Release());
    }

    // --- The catalogue and reader bounds -------------------------------------------------------

    [Fact]
    public void Catalogue_WhenManyTinySpills_ThenItStaysWithinTheFixedCeilingAndReadersWithinTheFanIn()
    {
        // A leaking implementation could still satisfy `<= K`, so the assertion is the exact
        // schedule the generation-tiered model fixes, plus `<= K`, plus the reader and file bounds.
        const int spills = 400;
        var leaves = QuantileRunScheduleModel.DisjointLiteralLeaves(spills, 3);
        using var driver = QuantileRunScheduleDriver.Create(budget: QuantileAccumulator.Modeled(3), fanIn: 3);

        driver.Feed(leaves);
        var observed = driver.Complete();

        var scheduleKind = RequireGenerationTieredSchedule(3, new LiteralQuantileSpillLeaves(leaves), observed, spills);
        Assert.True(observed.Writes.Any(write => write.Kind == QuantileRunWriteKind.Carry),
            "the population must genuinely consolidate during intake");
        Assert.True(observed.OriginalWrites > 3, "the population must genuinely spill many times");

        // The catalogue must respect the fixed ceiling K = (F-1)·L.
        Assert.True(
            observed.PeakCatalogue <= QuantileRunScheduleModel.Ceiling(3),
            $"the catalogue peaked at {observed.PeakCatalogue}, above K = {QuantileRunScheduleModel.Ceiling(3)}");

        // This population is divergent, so the observation must be classified generation-tiered
        // rather than indistinguishable; its exact per-transition catalogue counts are already
        // pinned by the model above.
        Assert.Equal(QuantileRunScheduleKind.GenerationTiered, scheduleKind);

        RequireBounds(driver, observed, fanIn: 3);
    }

    [Fact]
    public void Catalogue_WhenTheFanInIsConfiguredWrong_ThenTheBoundOracleRejectsIt()
    {
        // A reachable negative control: run at fan-in 4 and hold the observation to a fan-in-3
        // reader bound. It must fail, which is what proves the reader assertion in the passing
        // cases can fail rather than being vacuous.
        const int spills = 40;
        var leaves = QuantileRunScheduleModel.DisjointLiteralLeaves(spills, 3);
        using var driver = QuantileRunScheduleDriver.Create(budget: QuantileAccumulator.Modeled(3), fanIn: 4);
        driver.Feed(leaves);
        var observed = driver.Complete();

        // This is a real product observation too, so it carries the same generation-tiered requirement.
        RequireGenerationTieredSchedule(4, new LiteralQuantileSpillLeaves(leaves), observed, spills);

        Assert.True(driver.Files.PeakOpenReaders <= 4);
        Assert.Throws<Xunit.Sdk.TrueException>(() => RequireBounds(driver, observed, fanIn: 3));
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static QuantileRunScheduleKind RequireGenerationTieredSchedule(
        int fanIn, IQuantileSpillLeaves leaves, QuantileRunObservation observed, int spills)
    {
        var scheduleKind = QuantileRunScheduleOracle.Discriminate(fanIn, leaves, observed);
        var wholeCatalogueQuiescent = spills - (Math.Max(0, (spills - 2) / (fanIn - 1)) * (long)(fanIn - 1));
        var generationTieredQuiescent = QuantileRunScheduleModel.DigitSum(spills, fanIn);

        if (scheduleKind == QuantileRunScheduleKind.Indistinguishable)
        {
            // Below the fan-in neither schedule merges during intake, and from F+1 through 2F-1
            // leaves both have merged exactly leaves 1..F once: the models coincide, so there is
            // nothing to discriminate and both identities must agree.
            Assert.True(QuantileRunScheduleOracle.SameSchedule(
                QuantileRunScheduleModel.WholeCatalogue(fanIn, leaves), QuantileRunScheduleModel.GenerationTiered(fanIn, leaves)));
            Assert.Equal(wholeCatalogueQuiescent, generationTieredQuiescent);
            Assert.Equal(wholeCatalogueQuiescent, observed.QuiescentCatalogue);
            return scheduleKind;
        }

        // The two models predict different schedules for this population, so the observation
        // identifies which one the product follows, and the permanent requirement is the
        // generation-tiered schedule. A product reverted to whole-catalogue consolidation is
        // classified WholeCatalogue and fails on this line.
        Assert.Equal(QuantileRunScheduleKind.GenerationTiered, scheduleKind);

        // The whole-catalogue model is the negative control, and it must actively reject
        // the observation: an oracle that accepted both while predicting different schedules would
        // prove nothing, and Discriminate throws in that case.
        var rejected = QuantileRunScheduleModel.WholeCatalogue(fanIn, leaves);
        Assert.False(QuantileRunScheduleOracle.Accepts(rejected, observed));

        // The quiescent catalogue count is the generation-tiered schedule's own exact identity, not
        // merely a bound.
        Assert.Equal(generationTieredQuiescent, observed.QuiescentCatalogue);

        return scheduleKind;
    }

    private static void RequireBounds(QuantileRunScheduleDriver driver, QuantileRunObservation observed, int fanIn)
    {
        Assert.True(
            driver.Files.PeakOpenReaders <= fanIn,
            $"{driver.Files.PeakOpenReaders} readers were open at once, above the fan-in {fanIn}");
        Assert.True(driver.Files.PeakOpenWriters <= 1, "more than one writer was open at once");
        Assert.True(
            observed.PeakPendingDeletions <= QuantileAccumulator.MaxPendingDeletions(fanIn),
            "the pending-deletion cap was exceeded");

        // Exactly one run survives consolidation, and it is the one the replay reads.
        Assert.Equal(1, observed.LiveRunsAfterPrepare);
        Assert.Single(driver.Files.All, run => run.Created && !run.Deleted);

        // Healthy storage leaves no residue: every consumed run was deleted at its own size.
        foreach (var run in driver.Files.All.Where(run => run.Deleted))
        {
            Assert.Equal(run.WriteBytesRequested, run.DeletedBytes);
        }
    }

    private static void RequireRunBytesAndPasses(QuantileRunScheduleDriver driver, QuantileRunObservation observed)
    {
        // Every run's size, measured at the counting seam, is a whole number of framed records and
        // agrees with the size the workspace reported: two independent readings of one fact.
        var reported = observed.Writes.Select(write => write.Bytes).ToList();
        var counted = driver.Files.All
            .Where(run => run.Created)
            .Select(run => run.WriteBytesRequested)
            .ToList();
        Assert.Equal(reported, counted);
        Assert.All(counted, bytes => Assert.Equal(0, bytes % QuantileRunScheduleModel.RecordBytes));

        // Every consumed run was read exactly once, to its end.
        foreach (var run in driver.Files.All.Where(run => run.Deleted))
        {
            Assert.Equal(1, run.CompletedPasses);
            Assert.Equal(run.WriteBytesRequested, run.ReadBytesReturned);
        }
    }
}
