using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// The Calibrate phase alone: the data-reading pass that resolves auto cuts and observed domains,
/// with no planning, emission, or export after it.
/// <para>
/// <b>The measured interval</b> starts with a validated spec, an opened-but-unread schema, and a
/// bound source that has read no rows. It covers every data pass calibration requires — for a
/// count-sensitive attribute over interleaved triple input that is a grouped second pass as well as
/// the raw one — through to the retained, immutable calibrated outcome. Reading and resolving the
/// spec, deriving expectations, and checking the resolved cuts are outside it.
/// </para>
/// <para>
/// Calibration is measured separately from emission because it is the phase whose cost scales with
/// something different: emission is per-observation work, while count-sensitive calibration is
/// bounded-memory accumulation that spills and merges once its budget is exceeded. A single
/// end-to-end number would hide which of the two a scale result was actually about.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Calibrate)]
public abstract class CalibrateBenchmark
{
    private PreparedCorpus _corpus = null!;
    private ResolvedSpec _resolved = null!;
    private string _dataPath = string.Empty;
    private SourceReadSettings _settings = null!;
    private CalibratedSpec? _calibrated;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The spec this class calibrates, which is not the corpus's own declared spec.</summary>
    private protected abstract string SpecText { get; }

    /// <summary>Checks a completed calibration against independently derived expectations.</summary>
    private protected abstract void Validate(CalibratedSpec calibrated, long records);

    /// <summary>Resolves the spec against the corpus schema. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        _dataPath = _corpus.DataPath;

        var document = ConversionPipeline.RequireDocument(SpecText);
        _settings = ConversionPipeline.RequireReadSettings(document);
        var session = ConversionPipeline.CreateSession(_settings, _dataPath);
        var schema = await session.GetSchemaAsync().ConfigureAwait(false);
        _resolved = ConversionPipeline.RequireResolved(document, schema).Resolved;
    }

    /// <summary>Binds a fresh source and runs the whole calibration pass over it.</summary>
    [Benchmark(Description = "calibrate")]
    public async Task<int> Calibrate()
    {
        // A fresh session and a fresh binding per iteration: a calibrator handed a source that had
        // already been read would be measuring a warmed file and a reused cache, not a calibration.
        var session = ConversionPipeline.CreateSession(_settings, _dataPath);
        _ = await session.GetSchemaAsync().ConfigureAwait(false);

        var diagnostics = new List<BedrockDiagnostic>();
        _calibrated = session switch
        {
            TripleCsvSession triple => Unwrap(
                await Calibrator.CalibrateTripleAsync(
                        _resolved, triple.Bind(_resolved), BenchmarkGrouping.Default, observer: null, default)
                    .ConfigureAwait(false),
                diagnostics),
            WideCsvSession wide => Unwrap(
                await Calibrator.CalibrateAsync(
                        _resolved, wide.Bind(_resolved), BenchmarkGrouping.Default, observer: null, default)
                    .ConfigureAwait(false),
                diagnostics),
            _ => throw new InvalidOperationException("unrecognized session shape."),
        };

        return _calibrated.Calibrations.Count;
    }

    /// <summary>
    /// Validates the retained outcome after the pass has completed: the cuts and domains it
    /// resolved, against expectations derived from the corpus definition rather than from the
    /// calibrator.
    /// </summary>
    [IterationCleanup]
    public void Check()
    {
        if (_calibrated is null)
        {
            throw new InvalidOperationException($"calibrate ({Corpus.Id}) produced no calibrated state.");
        }

        Validate(_calibrated, _corpus.Records);
        _calibrated = null;
    }

    private static CalibratedSpec Unwrap(Diagnosed<CalibratedSpec> result, List<BedrockDiagnostic> diagnostics)
    {
        diagnostics.AddRange(result.Diagnostics);
        OutputValidation.RequireCleanCalibration(result.Diagnostics, "calibrate");
        return result.TryGetValue(out var calibrated)
            ? calibrated
            : throw new InvalidOperationException(
                $"calibration produced no result: {ConversionPipeline.Describe(result.Diagnostics)}");
    }
}

/// <summary>
/// Wide calibration at 10,000 records: equal-frequency over a strictly increasing column, min/max,
/// percentile range, and an observed domain, all in one pass.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class CalibrateWideSmall : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override string SpecText => W16CalibrationSpec.Text;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireW16(calibrated, records, "calibrate (w16-small)");
}

/// <summary>Wide calibration at 730,000 records: the working baseline. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CalibrateWideWorking : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override string SpecText => W16CalibrationSpec.Text;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireW16(calibrated, records, "calibrate (w16-working)");
}

/// <summary>Wide calibration at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class CalibrateWideScale7M : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);

    private protected override string SpecText => W16CalibrationSpec.Text;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireW16(calibrated, records, "calibrate (w16-scale7m)");
}

/// <summary>Wide calibration at 73M records: the case that forces the accumulator to spill. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale73M)]
public class CalibrateWideScale73M : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale73M);

    private protected override string SpecText => W16CalibrationSpec.Text;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireW16(calibrated, records, "calibrate (w16-scale73m)");
}

/// <summary>
/// Count-sensitive calibration over <b>interleaved</b> triple input at 10,000 rows: the case that
/// forces the calibrator's grouped second pass and the subject-local deduplication rule with it.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class CalibrateTripleUnorderedSmall : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);

    private protected override string SpecText => T10Specs.Auto;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireT10(calibrated, records, "calibrate (t10-unordered-small)");
}

/// <summary>
/// The same count-sensitive calibration over <b>contiguous</b> triple input: the deduplication is
/// inline on the raw pass, with no second read at all. Its cuts must be identical to the
/// interleaved case's, which is what proves the two paths agree rather than merely both finishing.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Small)]
public class CalibrateTripleGroupedSmall : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Grouped);

    private protected override string SpecText => T10Specs.AsGrouped(T10Specs.Auto);

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireT10(calibrated, records, "calibrate (t10-grouped-small)");
}

/// <summary>Count-sensitive triple calibration over interleaved input at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class CalibrateTripleUnorderedWorking : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);

    private protected override string SpecText => T10Specs.Auto;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireT10(calibrated, records, "calibrate (t10-unordered-working)");
}

/// <summary>Count-sensitive triple calibration over contiguous input at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Working)]
public class CalibrateTripleGroupedWorking : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Grouped);

    private protected override string SpecText => T10Specs.AsGrouped(T10Specs.Auto);

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireT10(calibrated, records, "calibrate (t10-grouped-working)");
}

/// <summary>Count-sensitive triple calibration at 7.3M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale, BenchmarkCategories.Triple)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class CalibrateTripleUnorderedScale7M : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);

    private protected override string SpecText => T10Specs.Auto;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireT10(calibrated, records, "calibrate (t10-unordered-scale7m)");
}

/// <summary>
/// <c>include</c> recovery at the working tier: one declared value, seven recovered from the data.
/// <para>
/// The observed-domain case starts empty; this one starts from a declared prefix and appends. The
/// retained order is therefore the declared values first and the discovered ones in
/// first-observation order after them, and that ordering is what the expectation asserts — a
/// recovery that appended in the wrong order would still produce the right SET.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CalibrateIncludeWorking : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override string SpecText => W16RecoverySpecs.Include;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequireIncludeRecovery(calibrated, records, "calibrate include (w16-working)");
}

/// <summary>
/// <c>value_groups</c> pass-through at the working tier: one authored group, and every unmatched
/// distinct raw value becomes its own bin.
/// <para>
/// The kind whose column set always depends on the input, and which therefore always reports
/// <c>ValueGroupsPassthroughDataDependent</c> — a Warning that states a fact about the mode rather
/// than a problem with the data, which is why the validation accepts it and asserts the bins instead.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class CalibratePassthroughWorking : CalibrateBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override string SpecText => W16RecoverySpecs.Passthrough;

    private protected override void Validate(CalibratedSpec calibrated, long records) =>
        CalibrationOracle.RequirePassthrough(calibrated, records, "calibrate pass-through (w16-working)");
}
