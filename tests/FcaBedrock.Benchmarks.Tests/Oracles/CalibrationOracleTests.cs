using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// Calibration is the phase whose <em>result</em> is data-dependent, so a benchmark over it is only
/// evidence if the result is checked. These tests hold the checks themselves to the same standard:
/// the exact expectation must match a real calibration, the two triple layouts must agree with each
/// other, and the resource contract must hold under a budget small enough to force spilling.
/// </summary>
public sealed class CalibrationOracleTests
{
    private const int WideRecords = 4_000;
    private const int TripleRecords = 2_000;

    [Fact]
    public async Task ExactCuts_ShouldMatchWhatTheCalibratorProducesForAKnownPopulation()
    {
        // n_seq is the row index, so its order statistics are arithmetic rather than something that
        // has to be sorted to be known. That makes it the one case where an oracle can state the
        // exact equal-frequency cuts and be believed - and where a disagreement means a real defect
        // rather than a re-derivation mistake.
        using var temp = TempDirectory.Create();
        var calibrated = await CalibrateWideAsync(temp, WideRecords);

        CalibrationOracle.RequireW16(calibrated, WideRecords, "wide calibration");

        var seq = (CalibratedCuts)CalibrationOracle.Outcome(calibrated, "n_seq")!;
        Assert.Equal(
            CalibrationOracle.ExpectedSeqCuts(WideRecords, W16CalibrationSpec.SeqBins),
            seq.Cuts);
        Assert.Equal(W16CalibrationSpec.SeqBins - 1, seq.Cuts.Count);
    }

    [Fact]
    public async Task ObservedDomain_ShouldBeDiscoveredInFirstObservationOrder()
    {
        using var temp = TempDirectory.Create();
        var calibrated = await CalibrateWideAsync(temp, WideRecords);

        var domain = (ObservedDomain)CalibrationOracle.Outcome(calibrated, "c2")!;
        var expected = CalibrationOracle.ExpectedC2Domain(WideRecords);

        Assert.Equal(expected, domain.Values);
        Assert.Equal(8, domain.Values.Count);
    }

    [Fact]
    public async Task ExactCuts_WhenTheExpectationIsWrong_ThenTheOracleRejectsTheCalibration()
    {
        // An oracle that cannot fail is not evidence. Asking it about a different population size
        // must make it disagree with a real calibration.
        using var temp = TempDirectory.Create();
        var calibrated = await CalibrateWideAsync(temp, WideRecords);

        Assert.Throws<InvalidOperationException>(
            () => CalibrationOracle.RequireW16(calibrated, WideRecords + 8, "wide calibration"));
    }

    [Fact]
    public async Task CountSensitiveCuts_ShouldBeIdenticalAcrossBothTripleLayouts()
    {
        // The two layouts take genuinely different code paths to the same population: contiguous
        // input deduplicates inline on the raw pass, while interleaved input needs a grouped second
        // pass. Identical cuts is the property that proves those paths agree; the discovered DOMAIN
        // legitimately differs in order, and is checked against its own layout-specific expectation.
        using var temp = TempDirectory.Create();

        var grouped = await CalibrateTripleAsync(temp, TripleLayout.Grouped);
        var interleaved = await CalibrateTripleAsync(temp, TripleLayout.Interleaved);

        var groupedCuts = (CalibratedCuts)CalibrationOracle.Outcome(grouped, "Stage")!;
        var interleavedCuts = (CalibratedCuts)CalibrationOracle.Outcome(interleaved, "Stage")!;

        Assert.Equal(groupedCuts.Cuts, interleavedCuts.Cuts);
        Assert.Equal(7, groupedCuts.Cuts.Count);

        CalibrationOracle.RequireT10(grouped, TripleRecords, "calibrate (t10-grouped)");
        CalibrationOracle.RequireT10(interleaved, TripleRecords, "calibrate (t10-unordered)");

        var groupedDomain = (ObservedDomain)CalibrationOracle.Outcome(grouped, "Tissue")!;
        var interleavedDomain = (ObservedDomain)CalibrationOracle.Outcome(interleaved, "Tissue")!;
        Assert.Equal(
            groupedDomain.Values.Order(StringComparer.Ordinal),
            interleavedDomain.Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CountSensitiveCalibration_UnderASpillForcingBudget_ShouldStayInsideItsModelledBound()
    {
        // The resource check runs as a separate, untimed validation with the observers attached -
        // never inside a timing configuration, where the observer itself would be measured. What it
        // asserts is D-095's tier-1 contract: the modelled resident total across live accumulators
        // stays within the budget, or within the honestly-stated floor when a share cannot buy even
        // one entry; and tier 2's run catalog stays bounded by the merge fan-in rather than by the
        // population.
        using var temp = TempDirectory.Create();
        var dataPath = temp.File("t10-observed.csv");
        await using (var data = File.Create(dataPath))
        {
            T10Corpus.Write(data, TripleRecords, TripleLayout.Interleaved);
        }

        const long budget = 64 * 1024;
        const int fanIn = 8;
        var (calibrated, trace) = await ResourceProbe.CalibrateAsync(
            T10Specs.Auto, dataPath, budget, fanIn, TestContext.Current.CancellationToken);

        // The calibration still has to be right; a bounded run that produced wrong cuts is not a
        // successful demonstration of anything.
        CalibrationOracle.RequireT10(calibrated, TripleRecords, "calibrate (t10-unordered)");

        Assert.NotEmpty(trace.ModelledResident);
        var bound = Math.Max(budget, ResourceProbe.AccumulatorFloorBytes);
        foreach (var resident in trace.ModelledResident)
        {
            Assert.True(resident <= bound, $"modelled resident {resident} exceeds the stated bound {bound}.");
        }

        foreach (var (attribute, liveRuns) in trace.RunCatalog)
        {
            Assert.True(liveRuns <= fanIn, $"{attribute} held {liveRuns} live runs, above the fan-in {fanIn}.");
        }
    }

    private static async Task<CalibratedSpec> CalibrateWideAsync(TempDirectory temp, int records)
    {
        var dataPath = temp.File($"w16-{records}.csv");
        var specPath = temp.File($"w16-{records}.toml");

        await using (var data = File.Create(dataPath))
        {
            W16Corpus.Write(data, records);
        }

        await File.WriteAllTextAsync(specPath, W16CalibrationSpec.Text, TestContext.Current.CancellationToken);

        var document = ConversionPipeline.RequireDocument(W16CalibrationSpec.Text);
        var settings = ConversionPipeline.RequireReadSettings(document);
        var session = (WideCsvSession)ConversionPipeline.CreateSession(settings, dataPath);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);
        var resolved = ConversionPipeline.RequireResolved(document, schema).Resolved;

        var result = await Calibrator.CalibrateAsync(
            resolved, session.Bind(resolved), TestContext.Current.CancellationToken);
        OutputValidation.RequireCleanCalibration(result.Diagnostics, "wide calibration");
        Assert.True(result.TryGetValue(out var calibrated));
        return calibrated;
    }

    private static async Task<CalibratedSpec> CalibrateTripleAsync(TempDirectory temp, TripleLayout layout)
    {
        var name = layout == TripleLayout.Grouped ? "grouped" : "unordered";
        var dataPath = temp.File($"t10-{name}.csv");

        if (!File.Exists(dataPath))
        {
            await using var data = File.Create(dataPath);
            T10Corpus.Write(data, TripleRecords, layout);
        }

        var specText = layout == TripleLayout.Grouped ? T10Specs.AsGrouped(T10Specs.Auto) : T10Specs.Auto;
        var document = ConversionPipeline.RequireDocument(specText);
        var settings = ConversionPipeline.RequireReadSettings(document);
        var session = (TripleCsvSession)ConversionPipeline.CreateSession(settings, dataPath);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);
        var resolved = ConversionPipeline.RequireResolved(document, schema).Resolved;

        var result = await Calibrator.CalibrateTripleAsync(
            resolved, session.Bind(resolved), TestContext.Current.CancellationToken);

        OutputValidation.RequireCleanCalibration(result.Diagnostics, $"t10 {name} calibration");
        Assert.True(result.TryGetValue(out var calibrated));
        return calibrated;
    }
}
