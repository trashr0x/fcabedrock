using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// Emits a fixed plan and consumes the object stream <b>without writing anything</b>.
/// <para>
/// Paired with the emit-and-export case over the same corpus and the same plan, and the pair is the
/// point: the difference between them is the <em>writer</em> — serializing ids, encoding, buffering,
/// and reaching the filesystem — while everything before it is common. Neither number means much
/// alone. A user choosing between formats, and a profiler deciding where to look, both want the
/// split rather than the total.
/// </para>
/// <para>
/// It is emphatically <b>not</b> a null-sink throughput claim. Nothing here is reported as export
/// performance; the export cases write real files precisely because a null sink would report a rate
/// no user can obtain. This case measures the production emission, and it consumes every object and
/// every cross into a fixed scalar summary so the work cannot be optimized away — a loop that merely
/// counted objects would let the crosses go unread.
/// </para>
/// <para>
/// <b>The measured interval</b> starts with the corpus prepared and the plan already resolved and
/// calibrated, with nothing open. It covers opening and streaming the source, discretizing and
/// scaling every observation, grouping where the case requires it, producing every emitted object,
/// and running the enumeration to completion. The summary is checked afterwards, outside timing,
/// against the same independent expectation the export case validates its bytes against.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert)]
public abstract class EmitDrainBenchmark
{
    private PreparedConversion _conversion = null!;
    private ContextExpectation _expected = null!;
    private List<BedrockDiagnostic> _diagnostics = [];
    private long _objects;
    private long _crossIdSum;

    /// <summary>The corpus this class reads.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>The expected shape, derived independently of the pipeline.</summary>
    private protected abstract ContextExpectation Expected(PreparedCorpus prepared);

    /// <summary>Diagnostic codes this case legitimately produces beyond the degenerate-shape set.</summary>
    private protected virtual IReadOnlyList<DiagnosticCode> AlsoAllowed => [];

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var prepared = CorpusPreparer.Require(Corpus);
        _conversion = await ConversionPipeline.FromSpecFileAsync(prepared.SpecPath, prepared.DataPath)
            .ConfigureAwait(false);
        _expected = Expected(prepared);
    }

    /// <summary>Discards the previous iteration's diagnostics and counters. Outside timing.</summary>
    [IterationSetup]
    public void Reset()
    {
        _diagnostics = [];
        _objects = 0;
        _crossIdSum = 0;
    }

    /// <summary>Emits and consumes every object and every cross into a fixed scalar summary.</summary>
    [Benchmark(Description = "emit drain (no export)")]
    public async Task<long> Drain()
    {
        var objects = 0L;
        var crossIdSum = 0L;

        await foreach (var emitted in _conversion.Emit(_diagnostics).ConfigureAwait(false))
        {
            objects++;

            // Indexed rather than foreach, and the ids are ADDED rather than merely counted: the
            // ids are the product of the whole pipeline, and a summary that only counted them would
            // leave every one of them unread and let the work be optimized away.
            //
            // Deliberately the ONLY accumulation in the timed loop. A cross COUNT would be a second
            // one, and the paired export case already validates the exact bytes; the marginal
            // strength is not worth adding an operation per object to a measured interval.
            var ids = emitted.CrossedFormalAttributeIds;
            for (var i = 0; i < ids.Count; i++)
            {
                crossIdSum += ids[i];
            }
        }

        _objects = objects;
        _crossIdSum = crossIdSum;
        return crossIdSum;
    }

    /// <summary>
    /// Validates the completed emission against the same expectation the export case validates its
    /// bytes against — the object count exactly, and the cross <em>identities</em> through their sum.
    /// </summary>
    [IterationCleanup]
    public void Validate()
    {
        var what = $"emit drain ({Corpus.Id})";
        OutputValidation.RequireCleanEmit(_diagnostics, what, AlsoAllowed);

        if (_objects != _expected.Objects)
        {
            throw new InvalidOperationException(
                $"{what}: emitted {_objects} objects, expected {_expected.Objects}.");
        }

        // The SUM of the crossed ids, not their count: a conversion that crossed the right NUMBER
        // of wrong columns would pass a count check and fail this one. The oracle accumulates it
        // while it streams the expectation, so this costs no extra traversal of the corpus.
        if (_crossIdSum != _expected.CrossIdSum)
        {
            throw new InvalidOperationException(
                $"{what}: the crossed ids sum to {_crossIdSum}, expected {_expected.CrossIdSum}.");
        }
    }
}

/// <summary>The W16 emit drain, paired with the W16 emit-and-export case.</summary>
[BenchmarkCategory(BenchmarkCategories.Convert)]
public abstract class WideEmitDrainBenchmark : EmitDrainBenchmark
{
    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        W16DeclaredOracle.Expect(prepared.Records);
}

/// <summary>The W16 emit drain at 10,000 records.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Small)]
public class WideEmitDrainSmall : WideEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Small);
}

/// <summary>The W16 emit drain at 730,000 records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Working)]
public class WideEmitDrainWorking : WideEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Working);
}

/// <summary>The W16 emit drain at 7.3M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale7M)]
public class WideEmitDrainScale7M : WideEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale7M);
}

/// <summary>The W16 emit drain at 73M records. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.W16Family, CorpusTier.Scale73M)]
public class WideEmitDrainScale73M : WideEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.W16(CorpusTier.Scale73M);
}

/// <summary>The T10 emit drain, over either physical layout.</summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Triple)]
public abstract class TripleEmitDrainBenchmark : EmitDrainBenchmark
{
    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        T10DeclaredOracle.Expect(prepared.Records);
}

/// <summary>Contiguous subjects at 10,000 rows: the single-pass path with no writer.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Small)]
public class TripleEmitDrainGroupedSmall : TripleEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Grouped);
}

/// <summary>Interleaved subjects at 10,000 rows: the grouping backend with no writer.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class TripleEmitDrainUnorderedSmall : TripleEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);
}

/// <summary>Contiguous subjects at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "grouped", CorpusTier.Working)]
public class TripleEmitDrainGroupedWorking : TripleEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Grouped);
}

/// <summary>Interleaved subjects at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class TripleEmitDrainUnorderedWorking : TripleEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);
}

/// <summary>Interleaved subjects at 7.3M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class TripleEmitDrainUnorderedScale7M : TripleEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);
}

/// <summary>Interleaved subjects at 73M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class TripleEmitDrainUnorderedScale73M : TripleEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);
}

/// <summary>
/// The keyed-dedupe emit drain: the merging path with no writer, at the two tiers the matrix asks
/// for it. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Grouping)]
public abstract class KeyedEmitDrainBenchmark : EmitDrainBenchmark
{
    private protected override IReadOnlyList<DiagnosticCode> AlsoAllowed => [DiagnosticCode.DuplicateObjectKey];

    private protected override ContextExpectation Expected(PreparedCorpus prepared) =>
        KeyedW16Oracle.Expect(prepared.Records);
}

/// <summary>The keyed-dedupe emit drain at 730,000 rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.KeyedFamily, CorpusTier.Working)]
public class KeyedEmitDrainWorking : KeyedEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Keyed(CorpusTier.Working);
}

/// <summary>The keyed-dedupe emit drain at 7.3M rows. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.KeyedFamily, CorpusTier.Scale7M)]
public class KeyedEmitDrainScale7M : KeyedEmitDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Keyed(CorpusTier.Scale7M);
}
