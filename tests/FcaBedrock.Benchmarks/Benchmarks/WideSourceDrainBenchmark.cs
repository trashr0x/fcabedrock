using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Drains a wide source session: the production reader from an unopened input through schema
/// acquisition, every cleaned record, and the end of the stream.
/// <para>
/// <b>The measured interval</b> starts with the corpus prepared on disk and <em>nothing open</em>.
/// It covers constructing the session, reading the schema, opening the underlying stream (a session
/// opens lazily, per read), tokenizing and cleaning every field, and running the enumerator to
/// completion — which is what closes the stream. Resolving the spec's read settings, deriving the
/// oracle, and validating the result all sit outside it: none of them is source work.
/// </para>
/// <para>
/// <b>What it deliberately is not.</b> No binding, no calibration, no planning, no export. This is
/// the cost of getting cleaned records out of a file, which is the floor every other conversion
/// benchmark sits on top of; the bound conversion source shares the same read pipeline, so the
/// figure is the read cost on both paths.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Source)]
public abstract class WideSourceDrainBenchmark
{
    private PreparedCorpus _corpus = null!;
    private SourceReadSettings _settings = null!;
    private DrainSummary _expected;
    private DrainSummary _actual;

    /// <summary>The corpus this class reads; supplied by the concrete per-tier classes below.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>
    /// The summary a correct drain of <paramref name="prepared"/> must produce, derived
    /// independently of the reader. Each family supplies its own, because the summary is a fact
    /// about that family's values; everything else about a drain is identical across them.
    /// </summary>
    private protected abstract DrainSummary Expected(PreparedCorpus prepared);

    /// <summary>
    /// Requires an already-prepared corpus, resolves its read settings, and derives the expected
    /// drain summary independently from the corpus definition. All of it is outside every measured
    /// interval.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        var session = await ConversionPipeline.OpenSessionAsync(_corpus.SpecPath, _corpus.DataPath).ConfigureAwait(false);
        _settings = ((WideCsvSession)session).ReadSettings;
        _expected = Expected(_corpus);
    }

    /// <summary>Opens the session and consumes every cleaned record into a fixed scalar summary.</summary>
    [Benchmark(Description = "wide source drain")]
    public async Task<long> Drain()
    {
        var session = (IWideSourceSession)ConversionPipeline.CreateSession(_settings, _corpus.DataPath);
        _ = await session.GetSchemaAsync().ConfigureAwait(false);

        var summary = default(DrainSummary);
        await foreach (var record in session.ReadAsync().ConfigureAwait(false))
        {
            summary = summary.AddRecord();
            for (var field = 0; field < record.FieldCount; field++)
            {
                summary = summary.AddField(record.Field(field));
            }
        }

        _actual = summary;
        return summary.ValueCharacters;
    }

    /// <summary>
    /// Validates the completed iteration after the stream has run to completion and its reader has
    /// been closed. A drain that read the wrong values throws here, so it can carry no timing.
    /// </summary>
    [IterationCleanup]
    public void Validate() =>
        OutputValidation.RequireSummary(_actual, _expected, $"wide source drain ({Corpus.Id})");
}

/// <summary>The 10,000-record wide drain: the default, hand-checkable case.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class WideSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedW16(prepared.Records);
}

/// <summary>The 730,000-record wide drain: the working baseline. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class WideSourceDrainWorking : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedW16(prepared.Records);
}

/// <summary>The 7.3M-record wide drain: ten times the motivating workload. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class WideSourceDrainScale7M : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedW16(prepared.Records);
}

/// <summary>The 73M-record wide drain: a hundred times the motivating workload. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale73M)]
public class WideSourceDrainScale73M : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale73M);

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        DrainOracle.ExpectedW16(prepared.Records);
}
