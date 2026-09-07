using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Drains the UCI Adult training split: fifteen headerless columns of real census-derived data,
/// missing cells and all.
/// <para>
/// Its expectation is derived by a <b>second, independent reader</b> written for the purpose — split
/// on the delimiter, trim, treat an empty cell or the missing token as missing — rather than by
/// enumerating a generator, because there is no generator. Adult's fields carry no quoting, which is
/// what makes that simple reader a legitimate oracle rather than an approximation; the oracle
/// asserts that property instead of assuming it.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdultFamily, "", CorpusTier.External)]
public class AdultSourceDrain : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Adult;

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        AdultOracle.ExpectedDrain(prepared.DataPath);
}

/// <summary>
/// Converts UCI Adult to a real <c>.dat</c> under the curated spec.
/// <para>
/// <b>How it is validated, and what that is worth.</b> Six of the spec's attributes omit their
/// domain, so the column set is discovered from the data and the expected bytes are not derivable
/// without re-implementing calibration — which would be an oracle that could not disagree with the
/// code. So this case asserts what can honestly be asserted: clean diagnostics, an object count
/// equal to the record count independently measured from the file, and byte-identical output across
/// every iteration of the run. The digest it records is <b>regression evidence</b> for a later run,
/// not proof that the semantics are right. The synthetic families own the derivable expectations;
/// the v2 minis own the external ones. This case owns being real.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdultFamily, "", CorpusTier.External)]
public class AdultConvertDat
{
    private ConversionRun _run = null!;
    private long _expectedObjects;

    /// <summary>Prepares the plan and reads the measured record count. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun("adult emit + dat export", CorpusCases.Adult);
        await _run.SetupAsync(expect: null).ConfigureAwait(false);
        _expectedObjects = _run.Prepared.Records;

        if (_expectedObjects <= 0)
        {
            throw new InvalidOperationException(
                "the Adult corpus catalog records no measured record count, so there is no denominator.");
        }

        AdultOracle.RequirePlanShape(_run.Plan, "adult emit + dat export");
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Emits and writes the real <c>.dat</c> artifact.</summary>
    [Benchmark(Description = "adult emit + dat export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates object count, diagnostics, and intra-run determinism. Outside timing.</summary>
    [IterationCleanup]
    public void Validate() => _run.ValidateBaseline(_expectedObjects);

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>
/// Exports UCI Adult to <c>.cxt</c>: the two-pass format over real data with discovered domains, so
/// the header carries names nobody in this repository chose.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdultFamily, "", CorpusTier.External)]
public class AdultConvertCxt
{
    private ConversionRun _run = null!;

    /// <summary>Prepares the plan. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun("adult emit + cxt export", CorpusCases.Adult, ExportFormat.Cxt);
        await _run.SetupAsync(expect: null).ConfigureAwait(false);
        AdultOracle.RequirePlanShape(_run.Plan, "adult emit + cxt export");
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Runs the name pass, replays the emission, and writes the real <c>.cxt</c> artifact.</summary>
    [Benchmark(Description = "adult emit + cxt export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>
    /// Validates diagnostics and intra-run determinism. The <b>line</b> count of a <c>.cxt</c> is not
    /// its object count — the format prefixes five header lines and every object and attribute name —
    /// so the object-count assertion is left to the <c>.dat</c> case, where a line really is an
    /// object, rather than restated here in a form that would only look like a check.
    /// </summary>
    [IterationCleanup]
    public void Validate() => _run.ValidateBaseline(AdultOracle.CxtLineCount(_run.Prepared.Records, _run.Plan));

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}
