using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Configuration;

/// <summary>
/// The suite's BenchmarkDotNet configuration.
/// <para>
/// BenchmarkDotNet owns discovery, jobs, warmup, measurement, worker lifecycle, statistics,
/// allocation reporting, logs, and exporters, so this adds only the four things it cannot know:
/// where the evidence goes, the selection policy, the domain denominators, and one job shape the
/// method boundary requires.
/// </para>
/// </summary>
internal static class BedrockBenchmarkConfig
{
    /// <summary>
    /// The job every file-and-pipeline benchmark runs under.
    /// <para>
    /// <b>One invocation per iteration, unroll factor one.</b> These cases open files, stream a
    /// whole corpus, and write a real artifact; an iteration that ran the operation many times
    /// would reuse warmed state, would defeat per-iteration validation, and could not have its
    /// output reset between runs. One invocation per iteration is what makes
    /// <c>[IterationSetup]</c>/<c>[IterationCleanup]</c> the documented fresh-state bracket, and it
    /// is where the reset-and-validate work lives — outside the measured interval.
    /// </para>
    /// </summary>
    public static Job FreshIteration { get; } = Job.Default
        .WithStrategy(RunStrategy.Throughput)
        .WithInvocationCount(1)
        .WithUnrollFactor(1)
        .WithId("fresh-iteration");

    /// <summary>
    /// The job the opt-in working and target-scale cases run under.
    /// <para>
    /// <b>Monitoring, not Throughput.</b> A single 730,000, 7.3M, or 73M-record operation already
    /// takes far longer than a measurement interval needs, so the pilot stage that Throughput uses
    /// to find an invocation count has nothing to find and would only multiply the run. Monitoring
    /// takes the operation as the unit and reports the distribution across iterations, which is the
    /// right shape for work measured in seconds and minutes.
    /// </para>
    /// <para>
    /// Two warmups and five measured iterations: enough to see the spread and to let the filesystem
    /// cache reach a steady state, without turning one case into an afternoon. Three samples alone
    /// would not support a tuning decision, and the tuning gate says so.
    /// </para>
    /// <para>
    /// The 73M tier runs the same job with fewer repetitions, supplied on the command line as
    /// BenchmarkDotNet's own <c>--warmupCount 1 --iterationCount 3</c> rather than as a second job
    /// declared here. A run that costs hours is a deliberate choice made at the point of running it,
    /// and BenchmarkDotNet already owns that option.
    /// </para>
    /// </summary>
    public static Job LongRun { get; } = Job.Default
        .WithStrategy(RunStrategy.Monitoring)
        .WithInvocationCount(1)
        .WithUnrollFactor(1)
        .WithLaunchCount(1)
        .WithWarmupCount(2)
        .WithIterationCount(5)
        .WithId("long-run");

    /// <summary>Builds the configuration for one invocation, given its <paramref name="policy"/>.</summary>
    public static IConfig Create(SelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        BenchmarkPaths.EnsureDirectories();

        var config = ManualConfig.Create(DefaultConfig.Instance)
            // Managed allocated bytes and GC counts per operation. This is process-wide managed
            // allocation - not peak live memory, not native allocation, and not another process's
            // memory. The two other memory meanings (modelled retained bytes from the internal
            // observers, and sampled whole-command working set) are separate evidence and are never
            // read off this column.
            .AddDiagnoser(MemoryDiagnoser.Default)
            // The raw evidence a later reader needs, beside the console table. The default
            // configuration already exports CSV, HTML, and GitHub-flavoured Markdown; full JSON is
            // the one machine-readable form it omits, and it is the form a later comparison reads.
            .AddExporter(JsonExporter.Full)
            // A Debug build measures nothing. The default configuration only warns; here it fails,
            // because a suite whose numbers are quoted in a milestone report must not be able to
            // produce them from an unoptimized assembly.
            .AddValidator(JitOptimizationsValidator.FailOnError)
            .AddColumn(CorpusDenominatorColumn.Records)
            .AddColumn(CorpusDenominatorColumn.InputBytes)
            .AddFilter(new TierSelectionFilter(policy))
            .WithArtifactsPath(BenchmarkPaths.ResultsDirectory);

        // A job named on the command line stands alone: adding ours as well would silently run
        // every selected case twice, once under each. This is what lets `--job dry` be exactly
        // BenchmarkDotNet's own dry run.
        //
        // Otherwise the job follows the selection: opting into a tier whose operations take seconds
        // opts into the job those cases need. One job per invocation rather than one per category,
        // deliberately - a config that attached a job per class would union with this one and run
        // every selected case twice.
        return policy.JobRequested ? config : config.AddJob(policy.LongRunRequested ? LongRun : FreshIteration);
    }
}
