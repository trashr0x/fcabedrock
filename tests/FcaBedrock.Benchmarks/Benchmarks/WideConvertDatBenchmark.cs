using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Emits a fixed plan over a wide source and writes a <b>real</b> FIMI <c>.dat</c> file.
/// <para>
/// <b>The measured interval</b> starts with the corpus prepared, the plan already resolved and
/// calibrated, and no file open at either end. It covers opening the destination, opening and
/// streaming the source, discretizing and scaling every observation, serializing every incidence
/// row, flushing, and closing the file. Reading the spec, resolving, calibrating, planning,
/// deriving the oracle, deleting the previous output, and validating the produced bytes are all
/// outside it.
/// </para>
/// <para>
/// The output is a real file on a real filesystem rather than a null sink, because a null sink
/// would report a throughput no user can obtain. Only one iteration's output exists at a time: it
/// is validated in iteration cleanup and then deleted, so a 73M-record case needs room for one
/// artifact, not for one per iteration.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert)]
public abstract class WideConvertDatBenchmark
{
    private ConversionRun _run = null!;

    /// <summary>The corpus this class reads; supplied by the concrete per-tier classes below.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>
    /// Requires a prepared corpus, drives the real spec-read/resolve/calibrate/plan sequence once,
    /// and derives the expected output length and digest independently — streaming the expectation
    /// rather than retaining it, so the same oracle serves every tier.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"wide emit + dat export ({Corpus.Id})", Corpus);
        await _run.SetupAsync(
                corpus => W16DeclaredOracle.Expect(corpus.Records), W16Specs.DeclaredFormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Emits and writes the real <c>.dat</c> artifact.</summary>
    [Benchmark(Description = "wide emit + dat export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>
    /// Validates the completed iteration once the writer's stream has been disposed and the emit
    /// diagnostics are final: the artifact must have exactly the expected length and digest, and
    /// the run must carry no diagnostic that would oblige a caller to discard it.
    /// </summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The 10,000-record wide conversion: the default, byte-validated case.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class WideConvertDatSmall : WideConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);
}

/// <summary>The 730,000-record wide conversion: the working baseline. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class WideConvertDatWorking : WideConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);
}

/// <summary>The 7.3M-record wide conversion. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class WideConvertDatScale7M : WideConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);
}

/// <summary>The 73M-record wide conversion. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale73M)]
public class WideConvertDatScale73M : WideConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale73M);
}
