using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Drains a triple source session: the production reader from an unopened input through schema
/// acquisition, every cleaned subject–predicate–value row, and the end of the stream.
/// <para>
/// The triple counterpart of the wide drain, and the two together are what separate a <em>reading</em>
/// cost from a <em>grouping</em> cost. A triple row carries three fields against a wide record's
/// sixteen, and a tier's rows are the same count on both families, so the difference between the two
/// drains is the per-field and per-record overhead of the reader alone — with none of the
/// conversion, calibration, or grouping that the triple path is usually blamed for.
/// </para>
/// <para>
/// <b>The measured interval</b> is the same contract as every other drain: prepared corpus, nothing
/// open, then constructing the session, reading the schema, opening the stream, tokenizing and
/// cleaning every row, and running the enumerator to completion. Resolving the read settings,
/// deriving the oracle, and validating the result are outside it.
/// </para>
/// <para>
/// Both physical layouts are measured. They carry identical bytes in a different order, so a
/// difference between them would be the reader responding to <em>ordering</em>, which it should not:
/// grouping is the conversion layer's job, and the source reads rows in input order either way.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Source, BenchmarkCategories.Triple)]
public abstract class TripleSourceDrainBenchmark
{
    private static readonly TripleColumns Roles = new(Subject: 0, Predicate: 1, Value: 2);

    private PreparedCorpus _corpus = null!;
    private SourceReadSettings _settings = null!;
    private DrainSummary _expected;
    private DrainSummary _actual;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The physical layout this class reads, which its expectation depends on.</summary>
    private protected abstract TripleLayout Layout { get; }

    /// <summary>Resolves the read settings and derives the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _corpus = CorpusPreparer.Require(Corpus);
        var session = await ConversionPipeline.OpenSessionAsync(_corpus.SpecPath, _corpus.DataPath)
            .ConfigureAwait(false);
        _settings = ((TripleCsvSession)session).ReadSettings;
        _expected = TripleDrainOracle.Expected(_corpus.Records, Layout);
    }

    /// <summary>Opens the session and consumes every cleaned row into a fixed scalar summary.</summary>
    [Benchmark(Description = "triple source drain")]
    public async Task<long> Drain()
    {
        var session = (ITripleSourceSession)ConversionPipeline.CreateSession(_settings, _corpus.DataPath);
        _ = await session.GetSchemaAsync().ConfigureAwait(false);

        var summary = default(DrainSummary);
        await foreach (var row in session.ReadRowsAsync(Roles).ConfigureAwait(false))
        {
            summary = summary.AddRecord().AddField(row.Subject).AddField(row.Predicate).AddField(row.Value);
        }

        _actual = summary;
        return summary.ValueCharacters;
    }

    /// <summary>Validates the completed iteration after the reader has closed. Outside timing.</summary>
    [IterationCleanup]
    public void Validate() =>
        OutputValidation.RequireSummary(_actual, _expected, $"triple source drain ({Corpus.Id})");
}

/// <summary>Contiguous subjects at 10,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Small)]
public class TripleSourceDrainGroupedSmall : TripleSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Grouped);

    private protected override TripleLayout Layout => TripleLayout.Grouped;
}

/// <summary>Interleaved subjects at 10,000 rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class TripleSourceDrainUnorderedSmall : TripleSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);

    private protected override TripleLayout Layout => TripleLayout.Interleaved;
}

/// <summary>Interleaved subjects at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class TripleSourceDrainUnorderedWorking : TripleSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);

    private protected override TripleLayout Layout => TripleLayout.Interleaved;
}

/// <summary>Interleaved subjects at 7.3M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class TripleSourceDrainUnorderedScale7M : TripleSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);

    private protected override TripleLayout Layout => TripleLayout.Interleaved;
}

/// <summary>Interleaved subjects at 73M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class TripleSourceDrainUnorderedScale73M : TripleSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);

    private protected override TripleLayout Layout => TripleLayout.Interleaved;
}
