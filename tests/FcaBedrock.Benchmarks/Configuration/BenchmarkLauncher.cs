using System.Globalization;
using System.Text;
using BenchmarkDotNet.Reports;

namespace FcaBedrock.Benchmarks.Configuration;

/// <summary>
/// One summary reduced to the facts that decide whether a run may be believed.
/// <para>
/// Deliberately a plain value rather than a BenchmarkDotNet type, so the exit policy below is an
/// ordinary function over ordinary data and is tested directly instead of being inferred from a
/// real run's console output.
/// </para>
/// </summary>
internal readonly record struct SummaryOutcome(
    string Title,
    int CriticalValidationErrors,
    int TotalReports,
    int BuildFailures,
    int ExecutionFailures)
{
    /// <summary>True when this summary carries nothing that disqualifies its numbers.</summary>
    public bool IsClean => CriticalValidationErrors == 0 && BuildFailures == 0 && ExecutionFailures == 0;

    /// <summary>Reads the outcome from a real BenchmarkDotNet summary.</summary>
    public static SummaryOutcome From(Summary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var builds = 0;
        var executions = 0;
        foreach (var report in summary.Reports)
        {
            if (!report.BuildResult.IsBuildSuccess)
            {
                builds++;
            }
            else if (!report.Success)
            {
                executions++;
            }
        }

        return new SummaryOutcome(
            summary.Title,
            summary.ValidationErrors.Count(error => error.IsCritical),
            summary.Reports.Length,
            builds,
            executions);
    }
}

/// <summary>
/// The process exit contract.
/// <para>
/// A benchmark run that could not build, could not execute, or failed validation must not exit 0
/// with a table that looks like a result. BenchmarkDotNet already records all three on its own
/// reports, so this reads that contract rather than inventing a parallel one — and it additionally
/// refuses to call a run successful when it measured <em>nothing</em>, because an over-narrow
/// filter that silently selects zero cases is the one failure a report cannot show.
/// </para>
/// </summary>
internal static class BenchmarkLauncher
{
    /// <summary>Exit code for a clean run.</summary>
    public const int Success = 0;

    /// <summary>Exit code for a run with a build, execution, or validation failure.</summary>
    public const int Failed = 1;

    /// <summary>Exit code for a run that was asked to measure and measured nothing.</summary>
    public const int NothingSelected = 2;

    /// <summary>Decides the exit code and the explanation for a completed invocation.</summary>
    public static (int ExitCode, string Report) Judge(IReadOnlyList<SummaryOutcome> outcomes, bool requireExecution)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var message = new StringBuilder();
        var failed = false;

        foreach (var outcome in outcomes.Where(outcome => !outcome.IsClean))
        {
            failed = true;
            message.Append(CultureInfo.InvariantCulture, $"'{outcome.Title}': ");
            message.Append(CultureInfo.InvariantCulture, $"{outcome.CriticalValidationErrors} critical validation error(s), ");
            message.Append(CultureInfo.InvariantCulture, $"{outcome.BuildFailures} build failure(s), ");
            message.Append(CultureInfo.InvariantCulture, $"{outcome.ExecutionFailures} execution failure(s).");
            message.Append('\n');
        }

        if (failed)
        {
            message.Insert(0, "The benchmark run produced no publishable result:\n");
            return (Failed, message.ToString());
        }

        var measured = outcomes.Sum(outcome => outcome.TotalReports);
        if (requireExecution && measured == 0)
        {
            return (NothingSelected,
                "No benchmark case was selected, so nothing was measured. The "
                + $"'{BenchmarkCategories.Scale}' and '{BenchmarkCategories.External}' tiers are opt-in and are "
                + "not reachable by a name filter: name the category explicitly, for example "
                + $"--anyCategories {BenchmarkCategories.Scale} or --anyCategories {BenchmarkCategories.External}.\n");
        }

        return (Success, $"{measured} benchmark case(s) completed with no build, execution, or validation failure.\n");
    }

    /// <summary>
    /// Judges a real BenchmarkDotNet run. Deliberately a distinct name rather than an overload of
    /// <see cref="Judge"/>: the two differ only in element type, so an empty collection would bind
    /// ambiguously at every call site.
    /// </summary>
    public static (int ExitCode, string Report) JudgeSummaries(IEnumerable<Summary> summaries, bool requireExecution)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return Judge([.. summaries.Select(SummaryOutcome.From)], requireExecution);
    }
}
