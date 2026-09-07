using System.Globalization;
using BenchmarkDotNet.Running;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks;

/// <summary>
/// The benchmark host's entry point.
/// <para>
/// It does two things and nothing more: it owns the explicit corpus-preparation verb, and it hands
/// every other invocation to <see cref="BenchmarkSwitcher"/> verbatim. There is no scenario
/// language, no run planner, and no argument grammar of our own — the documented BenchmarkDotNet
/// command line <em>is</em> the interface, so anything BenchmarkDotNet supports works here without
/// this file knowing about it.
/// </para>
/// </summary>
public static class Program
{
    private const string PrepareVerb = "prepare";

    /// <summary>Runs one invocation and returns its process exit code.</summary>
    /// <param name="args">
    /// Either <c>prepare [tier ...]</c>, or any BenchmarkDotNet command line.
    /// </param>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0 && string.Equals(args[0], PrepareVerb, StringComparison.OrdinalIgnoreCase))
        {
            return Prepare(args.AsSpan(1));
        }

        var policy = SelectionPolicy.FromArguments(args);
        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, BedrockBenchmarkConfig.Create(policy));

        // `--list` and `--help` legitimately measure nothing; a run does not.
        var informational = args.Any(argument =>
            argument.StartsWith("--list", StringComparison.OrdinalIgnoreCase)
            || argument is "--help" or "-h" or "--version" or "--info");

        var (exitCode, report) = BenchmarkLauncher.JudgeSummaries(summaries, requireExecution: !informational);
        Console.Out.Write(report);
        return exitCode;
    }

    // Corpus preparation is explicit and separate: no benchmark run may start generating a
    // 73-million-record file as a side effect of a broad filter, and generation must never sit
    // inside a measured interval. Preparation is idempotent - an already-current corpus is
    // verified and reused rather than rewritten.
    private static int Prepare(ReadOnlySpan<string> tokens)
    {
        var selected = new List<CorpusCase>();
        if (tokens.Length == 0)
        {
            selected.AddRange(CorpusCases.ForTier(CorpusTier.Small));
        }

        foreach (var token in tokens)
        {
            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
            {
                selected.AddRange(CorpusCases.All);
                continue;
            }

            // A tier prepares every case of that size; a case id prepares exactly one. The second
            // form is what an externally acquired corpus needs, since its tier names no record
            // count - and it is also the shortest way to re-prepare a single large case.
            if (CorpusTiers.TryParse(token, out var tier))
            {
                selected.AddRange(CorpusCases.ForTier(tier));
                continue;
            }

            if (CorpusCases.Find(token) is { } single)
            {
                selected.Add(single);
                continue;
            }

            Console.Error.Write(
                $"unknown corpus tier or case '{token}'; expected a tier ("
                + string.Join(", ", CorpusTiers.All.Select(CorpusTiers.Token))
                + "), a case id (" + string.Join(", ", CorpusCases.All.Select(corpus => corpus.Id))
                + "), or 'all'.\n");
            return 2;
        }

        foreach (var corpus in selected.Distinct())
        {
            var started = DateTime.UtcNow;
            var prepared = CorpusPreparer.Prepare(corpus);
            Console.Out.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                {prepared.Entry.Id}
                  records     {prepared.Records:N0}
                  data        {prepared.DataPath}
                  bytes       {prepared.InputBytes:N0}
                  sha256      {prepared.Entry.Data.Sha256}
                  spec        {prepared.SpecPath}
                  spec sha256 {prepared.Entry.Spec.Sha256}
                  revision    {prepared.Entry.GeneratorRevision}
                  elapsed     {(DateTime.UtcNow - started).TotalSeconds:F1}s

                """));
        }

        return 0;
    }
}
