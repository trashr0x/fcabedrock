using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Emits a fixed triple plan and writes a real <c>.dat</c>, for both physical layouts of the same
/// observations.
/// <para>
/// The pair is the point. <c>subject_grouped</c> streams single-pass with no buffering, because a
/// subject's rows are contiguous; <c>unordered</c> accepts interleaved subjects and must group them
/// through the spool backend. They are the same observations in the same first-appearance order
/// under a data-independent spec, so a correct conversion produces <b>byte-identical</b> output from
/// both — and the difference in cost between them is the price of accepting unordered input, which
/// is a number the milestone actually needs.
/// </para>
/// <para>
/// The measured interval is the same as every other conversion case: prepared but unopened input and
/// output at the start; opening, streaming, grouping, discretizing, serializing, flushing, and
/// closing inside; preparation, oracle derivation, output reset, and validation outside.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Triple)]
public abstract class TripleConvertDatBenchmark
{
    private ConversionRun _run = null!;

    /// <summary>The corpus this class reads; supplied by the concrete classes below.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and derives the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"triple emit + dat export ({Corpus.Id})", Corpus);

        // The declared spec's column set is data-independent, so ONE expectation serves both
        // layouts: validating each against it is what proves them equal to each other.
        await _run.SetupAsync(
                corpus => T10DeclaredOracle.Expect(corpus.Records), T10Specs.DeclaredFormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Emits and writes the real <c>.dat</c> artifact.</summary>
    [Benchmark(Description = "triple emit + dat export")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates the artifact after disposal, then reclaims its space.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>Contiguous subjects at 10,000 input rows: the single-pass fast path.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Small)]
public class TripleConvertDatGroupedSmall : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Grouped);
}

/// <summary>Interleaved subjects at 10,000 input rows: the grouping/spool path.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class TripleConvertDatUnorderedSmall : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);
}

/// <summary>Contiguous subjects at 730,000 input rows: the working baseline. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Working)]
public class TripleConvertDatGroupedWorking : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Grouped);
}

/// <summary>Interleaved subjects at 730,000 input rows: the working baseline. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class TripleConvertDatUnorderedWorking : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);
}

/// <summary>Contiguous subjects at 7.3M input rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Scale7M)]
public class TripleConvertDatGroupedScale7M : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Grouped);
}

/// <summary>Interleaved subjects at 7.3M input rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class TripleConvertDatUnorderedScale7M : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);
}

/// <summary>Contiguous subjects at 73M input rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Scale73M)]
public class TripleConvertDatGroupedScale73M : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Grouped);
}

/// <summary>Interleaved subjects at 73M input rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class TripleConvertDatUnorderedScale73M : TripleConvertDatBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);
}
