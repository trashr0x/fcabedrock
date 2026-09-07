using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Drains the 1,559-column Ads-width source. Same measured interval as every other drain; the
/// difference is that each record carries a hundred times as many fields.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Micro)]
public class AdsSourceDrainMicro : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Micro);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedAds(prepared.Records);
}

/// <summary>The Ads-width drain at 10,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Small)]
public class AdsSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Small);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedAds(prepared.Records);
}

/// <summary>
/// Emits and exports the Ads-width case to a real <c>.dat</c>.
/// <para>
/// The width is the point: 1,568 formal attributes over a sparse incidence, so the per-object cost
/// of walking a plan this wide is separated from the per-cross cost of writing one. A row crosses
/// about twenty of those columns, which is what makes the file small and the plan traversal the
/// dominant term.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert)]
public abstract class AdsConvertDatBenchmark
{
    private ConversionRun _run = null!;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"ads-width emit + dat export ({Corpus.Id})", Corpus);
        await _run.SetupAsync(corpus => AdsOracle.Expect(corpus.Records), AdsSpecs.FormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Emits and writes the real <c>.dat</c> artifact.</summary>
    [Benchmark(Description = "ads-width emit + dat export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates the artifact after disposal, then reclaims its space.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The Ads-width conversion at 1,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Micro)]
public class AdsConvertDatMicro : AdsConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Micro);
}

/// <summary>The Ads-width conversion at 10,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Small)]
public class AdsConvertDatSmall : AdsConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Small);
}

/// <summary>
/// Probes the Ads-width source: 1,559 discovered attributes, each with a tiny domain.
/// <para>
/// This is the guard-1 shape — many attributes rather than many values — and it is the one the W16
/// and T10 families cannot produce. Probe's per-attribute accounting is what scales here, so the
/// case checks that a wide schema still yields a <em>complete</em> draft under the default limits:
/// 1,559 attributes is well inside the 10,000-attribute guard, and each column's handful of values
/// is nowhere near the retention limit.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Probe)]
public abstract class AdsProbeBenchmark : ProbeBenchmark
{
    private protected override ProbeOptions Options => ProbeOptions.Default;

    private protected override void Check(Diagnosed<SpecDocument> result) =>
        ProbeOracle.RequireComplete(result, AdsCorpus.ColumnCount, $"probe ({Corpus.Id})");
}

/// <summary>The Ads-width probe at 1,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Micro)]
public class AdsProbeMicro : AdsProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Micro);
}

/// <summary>The Ads-width probe at 10,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.AdsFamily, CorpusTier.Small)]
public class AdsProbeSmall : AdsProbeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Ads(CorpusTier.Small);
}
