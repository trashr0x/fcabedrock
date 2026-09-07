using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Calibration;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The property that makes tuning the memory budget legitimate at all: <b>a budget changes where the
/// work happens, never what it produces</b>.
/// <para>
/// D-082 and D-095 say so, and M8 is allowed to move the budget and the merge fan-in precisely
/// because they are byte-neutral by construction. "By construction" is a claim, and a claim about
/// exact count-sensitive calibration under spilling is exactly the kind that should be measured
/// rather than trusted — a quantile accumulator that lost or double-counted a value while spilling
/// would produce plausible cuts, not obviously wrong ones.
/// </para>
/// <para>
/// So each test below runs the same calibration twice over the same corpus, under a budget small
/// enough to force many spills and a merge, and one large enough that nothing spills, and requires
/// the outcomes to be identical. The budget is the only thing that differs.
/// </para>
/// </summary>
public sealed class SpillEquivalenceTests
{
    // Small enough that the accumulator spills repeatedly and the merger runs multi-stage, and large
    // enough that the run is quick. The corpus is the high-cardinality one on purpose: n_seq has one
    // distinct value per row, so nothing folds and every value has to survive the round trip.
    private const long SpillForcingBudget = 64 * 1024;
    private const long AmpleBudget = 512L * 1024 * 1024;
    private const long Records = 40_000;

    private static async Task<CalibratedSpec> CalibrateAsync(string dataPath, long budget) =>
        (await ResourceProbe.CalibrateAsync(W16CalibrationSpec.Text, dataPath, budget, mergeFanIn: 16)).Calibrated;

    [Fact]
    public async Task ExactCuts_ShouldBeIdenticalWhetherTheAccumulatorSpilledOrNot()
    {
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, Records);
        }

        var spilled = await CalibrateAsync(dataPath, SpillForcingBudget);
        var resident = await CalibrateAsync(dataPath, AmpleBudget);

        // Every retained outcome, not just the count-sensitive one: an observed domain that lost its
        // first-observation order under spilling would change the column layout just as surely.
        Assert.Equal(resident.Calibrations.Count, spilled.Calibrations.Count);

        foreach (var expected in resident.Calibrations)
        {
            var actual = CalibrationOracle.Outcome(spilled, expected.AttributeName);
            Assert.NotNull(actual);
            Assert.Equal(Describe(expected), Describe(actual));
        }
    }

    [Fact]
    public async Task ExactCuts_ShouldStillMatchTheIndependentOracleAfterSpilling()
    {
        // The equality above would also hold if BOTH runs were wrong in the same way. This pins the
        // spilled run to the arithmetic expectation derived from the corpus definition: n_seq is the
        // row index, so its equal-frequency boundaries are order statistics over a known sequence.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, Records);
        }

        var spilled = await CalibrateAsync(dataPath, SpillForcingBudget);

        CalibrationOracle.RequireW16(spilled, Records, "spilled calibration");
    }

    [Fact]
    public async Task Spilling_ShouldActuallyHaveHappened()
    {
        // Without this, the two tests above could pass by never spilling at all - which would make
        // them agreements between two identical runs rather than evidence about the spill path.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, Records);
        }

        var (_, trace) = await ResourceProbe.CalibrateAsync(
            W16CalibrationSpec.Text, dataPath, SpillForcingBudget, mergeFanIn: 16);

        Assert.NotEmpty(trace.RunCatalog);
        Assert.Contains(trace.RunCatalog, entry => entry.LiveRuns > 1);
    }

    [Fact]
    public async Task ModelledResident_ShouldStayWithinTheBudgetFloorBound()
    {
        // D-095's bound is max(budget, attributeCount x FloorBytes): the floor arm exists because
        // every count-sensitive attribute needs at least one entry's worth of room whatever the
        // budget says. Asserting the bound rather than the budget is what keeps this honest for a
        // deliberately tiny budget.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, Records);
        }

        var (_, trace) = await ResourceProbe.CalibrateAsync(
            W16CalibrationSpec.Text, dataPath, SpillForcingBudget, mergeFanIn: 16);

        // Two count-sensitive attributes in this spec: n_seq (equal_frequency) and n_skew
        // (percentile range). n_wide is min/max and c2 is an observed domain; neither counts.
        var bound = Math.Max(SpillForcingBudget, 2 * ResourceProbe.AccumulatorFloorBytes);

        Assert.All(trace.ModelledResident, modelled => Assert.InRange(modelled, 0, bound));
    }

    [Theory]
    [InlineData(8L * 1024 * 1024)]
    [InlineData(64L * 1024 * 1024)]
    [InlineData(256L * 1024 * 1024)]
    public async Task ModelledResident_ShouldScaleWithTheBudgetRatherThanWithTheData(long budget)
    {
        // The COST side of any budget change, and the reason it is not free to raise the default.
        //
        // A count-sensitive accumulator is sized from its share of the budget before a single record
        // is read, so its footprint is a function of the BUDGET, not of the input. A conversion of
        // ten thousand rows therefore pays the same accumulator cost as one of seventy-three million,
        // and raising the default raises that floor for every conversion at every size.
        //
        // The assertion is deliberately weak — the peak is at least a quarter of the budget, and
        // within the documented bound — because the exact fraction is the accumulator's business. It
        // is the SHAPE that a tuning decision needs: this number tracks the knob, not the data.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, 20_000);
        }

        var (_, trace) = await ResourceProbe.CalibrateAsync(
            W16CalibrationSpec.Text, dataPath, budget, mergeFanIn: 16);

        var peak = trace.ModelledResident.Count == 0 ? 0 : trace.ModelledResident.Max();

        Assert.InRange(peak, budget / 4, Math.Max(budget, 2 * ResourceProbe.AccumulatorFloorBytes));
    }

    // --- The controlled attribute-count matrix (gated; not an ordinary test) --------------------

    /// <summary>
    /// Set to any value to run <see cref="ManyQuantiles_ShouldCalibrateAtEveryAttributeCountAndBudget"/>.
    /// </summary>
    private const string MatrixGate = "FCABEDROCK_CALIBRATION_MATRIX";

    // The reported reproduction's size. Kept here rather than at the 40,000 the tests above use,
    // because this check exists to re-run the controlled measurement that produced the recorded
    // 1/2/4/8/16 x 64/512 MiB table, and its size is part of what was measured.
    private const long MatrixRecords = 730_000;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public async Task ManyQuantiles_ShouldCalibrateAtEveryAttributeCountAndBudget(int attributes)
    {
        // Gated because it writes and calibrates 730,000 records ten times over: this is the
        // controlled measurement behind a recorded table, run deliberately, not part of the ordinary
        // suite. The same property at hand-checkable size is an ordinary test in
        // Conversion.Tests/MultiAttributeCalibrationTests.
        Assert.SkipUnless(
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(MatrixGate)),
            $"set {MatrixGate} to run the controlled attribute-count matrix.");

        using var temp = TempDirectory.Create();
        var dataPath = temp.File("w16.csv");
        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, MatrixRecords);
        }

        var spec = W16PressureSpecs.ManyQuantilesOf(attributes);

        // The number of attributes is the only variable across a row, and the budget the only one
        // down a column. Both budgets must produce a result, and the SAME cuts: how much room the
        // accumulators were given decides where the population lives, never what it says.
        var shipped = await ResourceProbe.CalibrateAsync(spec, dataPath, 64L * 1024 * 1024, mergeFanIn: 16);
        var ample = await ResourceProbe.CalibrateAsync(spec, dataPath, 512L * 1024 * 1024, mergeFanIn: 16);

        Assert.Equal(attributes, shipped.Calibrated.Calibrations.Count);
        Assert.Equal(
            shipped.Calibrated.Calibrations.Select(Describe),
            ample.Calibrated.Calibrations.Select(Describe));

        // Each accumulator's modelled share still honours the D-095 bound at every count.
        var bound = Math.Max(64L * 1024 * 1024, attributes * ResourceProbe.AccumulatorFloorBytes);
        Assert.All(shipped.Trace.ModelledResident, modelled => Assert.InRange(modelled, 0, bound));
    }

    // Rendered rather than compared as records. A calibration outcome holds an ImmutableArray, and
    // ImmutableArray's equality is REFERENCE equality of its backing array - so two runs that
    // produced byte-identical cuts would never be `Equal` and the test would pass for no reason,
    // then fail for no reason. Rendering the contents compares what the outcome actually says, and
    // a mismatch reports the two value lists rather than two type names.
    private static string Describe(AttributeCalibration outcome) => outcome switch
    {
        CalibratedCuts cuts => $"{cuts.AttributeName} cuts=[{string.Join(", ", cuts.Cuts)}]",
        ObservedDomain domain => $"{domain.AttributeName} observed=[{string.Join(", ", domain.Values)}]",
        IncludeAdditions include => $"{include.AttributeName} included=[{string.Join(", ", include.Values)}]",
        PassthroughBins bins => $"{bins.AttributeName} passthrough=[{string.Join(", ", bins.Values)}]",
        _ => throw new InvalidOperationException($"unrecognized calibration outcome {outcome.GetType().Name}."),
    };
}
