using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Converts the keyed-wide corpus under <c>duplicate_object_policy = "dedupe"</c>: four rows per
/// object, interleaved so no key's rows are adjacent.
/// <para>
/// Wide <c>dedupe</c> shares the grouping/sort-merge/spool backend with triple <c>unordered</c>
/// (§6.1), so this measures that backend from the <em>other</em> side — same machinery, wide records
/// instead of triple rows, and a merge that unions crosses rather than accumulating observations.
/// The pair is what tells a cost of the backend apart from a cost of the triple path.
/// </para>
/// <para>
/// The aggregated <c>DuplicateObjectKey</c> Info is expected here and nowhere else: it is the
/// conversion stating that it merged rows, which is precisely what the case asked it to do.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Grouping)]
public abstract class KeyedDedupeBenchmark
{
    private ConversionRun _run = null!;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"keyed wide dedupe ({Corpus.Id})", Corpus)
        {
            AlsoAllowed = [DiagnosticCode.DuplicateObjectKey],
        };

        await _run.SetupAsync(
                corpus => KeyedW16Oracle.Expect(corpus.Records), W16Specs.DeclaredFormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Groups by key, unions crosses, and writes the real <c>.dat</c> artifact.</summary>
    [Benchmark(Description = "keyed wide dedupe + dat export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates the artifact after disposal, then reclaims its space.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>Keyed dedupe at 10,000 rows — 2,500 objects.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.KeyedFamily, CorpusTier.Small)]
public class KeyedDedupeSmall : KeyedDedupeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Keyed(CorpusTier.Small);
}

/// <summary>Keyed dedupe at 730,000 rows — 182,500 objects. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.KeyedFamily, CorpusTier.Working)]
public class KeyedDedupeWorking : KeyedDedupeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Keyed(CorpusTier.Working);
}

/// <summary>Keyed dedupe at 7.3M rows — 1,825,000 objects. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.KeyedFamily, CorpusTier.Scale7M)]
public class KeyedDedupeScale7M : KeyedDedupeBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Keyed(CorpusTier.Scale7M);
}

/// <summary>
/// Drains the long-text source: four columns, but every record carries hundreds of characters.
/// <para>
/// A cleaned field is a fresh string, so this is where the reader's per-field allocation stops being
/// dominated by per-record overhead. The comparison worth making is against the W16 drain: sixteen
/// short fields against four long ones, at the same record count.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LongTextFamily, CorpusTier.Small)]
public class LongTextSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.LongText(CorpusTier.Small);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedLongText(prepared.Records);
}

/// <summary>The long-text drain at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.LongTextFamily, CorpusTier.Working)]
public class LongTextSourceDrainWorking : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.LongText(CorpusTier.Working);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedLongText(prepared.Records);
}

/// <summary>
/// Emits and exports the long-text case. Two crosses per object over a sixteen-column plan, so the
/// context is trivial and the reading is not — which is what isolates the cost of long values from
/// the cost of a wide or dense context.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert)]
public abstract class LongTextConvertDatBenchmark
{
    private ConversionRun _run = null!;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"long-text emit + dat export ({Corpus.Id})", Corpus);
        await _run.SetupAsync(corpus => LongTextOracle.Expect(corpus.Records), LongTextSpecs.FormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Emits and writes the real <c>.dat</c> artifact.</summary>
    [Benchmark(Description = "long-text emit + dat export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates the artifact after disposal, then reclaims its space.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The long-text conversion at 10,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LongTextFamily, CorpusTier.Small)]
public class LongTextConvertDatSmall : LongTextConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.LongText(CorpusTier.Small);
}

/// <summary>The long-text conversion at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.LongTextFamily, CorpusTier.Working)]
public class LongTextConvertDatWorking : LongTextConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.LongText(CorpusTier.Working);
}
