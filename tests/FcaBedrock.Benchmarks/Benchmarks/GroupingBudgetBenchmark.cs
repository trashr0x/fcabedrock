using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// The grouping backend's two internal knobs, varied <b>one axis at a time</b> over the case that
/// actually exercises them: interleaved triple input, which cannot stream and must group.
/// <para>
/// These are the only defaults M8 is allowed to move, and only after evidence (D-082/D-095: the
/// budget and fan-in are byte-neutral by construction, never a spec or fingerprint input; the
/// layout safety constants beside them are correctness constants and are not touched here). The
/// experiment therefore has two jobs at once. It measures the tradeoff — a smaller budget spills
/// sooner and merges more, a larger one holds more rows resident — and it <b>proves the
/// byte-neutrality that makes tuning legitimate at all</b>: every budget and every fan-in is
/// validated against the same expected output digest, so a knob that changed a single byte would
/// fail rather than quietly produce a faster wrong answer.
/// </para>
/// <para>
/// The parameter is swept one axis at a time rather than as a grid, because a Cartesian product of
/// budgets and fan-ins over four tiers is a great deal of machine time for a surface whose two axes
/// are not expected to interact strongly; a grid is what a genuine interaction in these results
/// would justify, not what starts the investigation.
/// </para>
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Convert, BenchmarkCategories.Triple, BenchmarkCategories.Grouping)]
public abstract class GroupingBudgetBenchmark
{
    /// <summary>The default budget and fan-in the production backend ships with today.</summary>
    public const long DefaultBudgetBytes = 64L * 1024 * 1024;

    /// <summary>The default merge fan-in.</summary>
    public const int DefaultFanIn = 16;

    private ConversionRun _run = null!;

    /// <summary>
    /// The in-memory budget before an intake spill, in MiB. <c>8</c> forces heavy spilling at every
    /// tier, <c>64</c> is today's default, and <c>256</c> asks whether holding more resident pays —
    /// an experiment, not a promised default.
    /// </summary>
    [Params(8, 64, 256)]
    public int BudgetMiB { get; set; } = 64;

    /// <summary>The corpus this class reads; supplied by the concrete classes below.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and the expectation. Outside every measured interval.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"unordered triple convert ({Corpus.Id}, {BudgetMiB} MiB budget)", Corpus)
        {
            Grouping = BenchmarkGrouping.Create(
                maxBufferedBytes: BudgetMiB * 1024L * 1024L, maxMergeFanIn: DefaultFanIn),
        };

        await _run.SetupAsync(
                corpus => T10DeclaredOracle.Expect(corpus.Records), T10Specs.DeclaredFormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Groups, emits, and writes the real artifact under this iteration's budget.</summary>
    [Benchmark(Description = "unordered triple convert under a grouping budget")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>
    /// Validates against the <b>same</b> expectation every other budget is validated against — the
    /// byte-neutrality proof, taken on every iteration rather than assumed from the contract.
    /// </summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The budget sweep at 10,000 input rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class GroupingBudgetSmall : GroupingBudgetBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);
}

/// <summary>
/// The budget sweep at 730,000 input rows: the working tier the tuning gate takes its one-axis
/// sweep on, before any candidate is confirmed at target scale. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class GroupingBudgetWorking : GroupingBudgetBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);
}

/// <summary>The budget sweep at 7.3M input rows: where the tradeoff has room to show. Opt-in.</summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale7M)]
public class GroupingBudgetScale7M : GroupingBudgetBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved);
}

/// <summary>
/// The budget sweep at 73M input rows: the second of the two target sizes a budget change must be
/// confirmed at before it may become a default. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Scale)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Scale73M)]
public class GroupingBudgetScale73M : GroupingBudgetBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Scale73M, TripleLayout.Interleaved);
}

/// <summary>
/// The merge fan-in sweep, at the budget that forces spilling: with a large budget nothing spills
/// and the fan-in is inert, so the axis is only measurable where merging actually happens.
/// </summary>
[BenchmarkCategory(
    BenchmarkCategories.Convert,
    BenchmarkCategories.Triple,
    BenchmarkCategories.Grouping)]
public abstract class GroupingFanInBenchmark
{
    /// <summary>The budget the fan-in sweep runs under: small enough that merging is unavoidable.</summary>
    public const long SpillForcingBudgetBytes = 64L * 1024;

    private ConversionRun _run = null!;

    /// <summary>Runs merged in one pass; above this the merge becomes multi-stage.</summary>
    [Params(4, 16, 32)]
    public int FanIn { get; set; } = GroupingBudgetBenchmark.DefaultFanIn;

    /// <summary>The corpus this class reads; supplied by the concrete classes below.</summary>
    private protected abstract CorpusCase Corpus { get; }

    /// <summary>Prepares the plan and the expectation under a deliberately spill-forcing budget.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _run = new ConversionRun($"unordered triple convert ({Corpus.Id}, fan-in {FanIn})", Corpus)
        {
            Grouping = BenchmarkGrouping.Create(
                maxBufferedBytes: SpillForcingBudgetBytes, maxMergeFanIn: FanIn),
        };

        await _run.SetupAsync(
                corpus => T10DeclaredOracle.Expect(corpus.Records), T10Specs.DeclaredFormalAttributeCount)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the previous iteration's artifact and its diagnostics sink. Outside timing.</summary>
    [IterationSetup]
    public void Reset() => _run.Reset();

    /// <summary>Groups, emits, and writes the real artifact under this iteration's fan-in.</summary>
    [Benchmark(Description = "unordered triple convert under a merge fan-in")]
    public Task Convert() => _run.ConvertAsync();

    /// <summary>Validates against the same expectation every fan-in is validated against.</summary>
    [IterationCleanup]
    public void Validate() => _run.Validate();

    /// <summary>Removes the output directory entry this case owned.</summary>
    [GlobalCleanup]
    public void Cleanup() => _run.Cleanup();
}

/// <summary>The fan-in sweep at 10,000 input rows.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Small)]
public class GroupingFanInSmall : GroupingFanInBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Small, TripleLayout.Interleaved);
}

/// <summary>
/// The fan-in sweep at 730,000 input rows: enough spilled runs for a multi-stage merge to be a
/// genuinely different amount of work from a single-stage one. Opt-in.
/// </summary>
[BenchmarkCategory(BenchmarkCategories.Working)]
[BenchmarkCorpus(CorpusCases.T10Family, "unordered", CorpusTier.Working)]
public class GroupingFanInWorking : GroupingFanInBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.T10(CorpusTier.Working, TripleLayout.Interleaved);
}
