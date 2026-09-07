using FcaBedrock.Benchmarks.Configuration;

namespace FcaBedrock.Benchmarks.Tests.Configuration;

/// <summary>
/// The process exit contract. A run that could not build, could not execute, or failed validation
/// must not exit 0 with a table that looks like a result.
/// </summary>
public sealed class BenchmarkLauncherTests
{
    private static SummaryOutcome Clean(int reports = 3) => new("run", 0, reports, 0, 0);

    [Fact]
    public void Judge_WhenEverySummaryIsClean_ThenTheRunSucceeds()
    {
        var (exitCode, report) = BenchmarkLauncher.Judge([Clean()], requireExecution: true);

        Assert.Equal(BenchmarkLauncher.Success, exitCode);
        Assert.Contains("3 benchmark case(s) completed", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_WhenAReportFailedToExecute_ThenTheRunFails()
    {
        var (exitCode, report) = BenchmarkLauncher.Judge(
            [new SummaryOutcome("run", 0, 3, 0, 1)], requireExecution: true);

        Assert.Equal(BenchmarkLauncher.Failed, exitCode);
        Assert.Contains("no publishable result", report, StringComparison.Ordinal);
        Assert.Contains("1 execution failure(s)", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_WhenAReportFailedToBuild_ThenTheRunFails()
    {
        var (exitCode, _) = BenchmarkLauncher.Judge(
            [new SummaryOutcome("run", 0, 1, 1, 0)], requireExecution: true);

        Assert.Equal(BenchmarkLauncher.Failed, exitCode);
    }

    [Fact]
    public void Judge_WhenValidationFailedCritically_ThenTheRunFails()
    {
        var (exitCode, _) = BenchmarkLauncher.Judge(
            [new SummaryOutcome("run", 2, 0, 0, 0)], requireExecution: true);

        Assert.Equal(BenchmarkLauncher.Failed, exitCode);
    }

    [Fact]
    public void Judge_WhenOneOfSeveralSummariesFailed_ThenTheWholeRunFails()
    {
        var (exitCode, report) = BenchmarkLauncher.Judge(
            [Clean(), new SummaryOutcome("second", 0, 1, 0, 1), Clean()], requireExecution: true);

        Assert.Equal(BenchmarkLauncher.Failed, exitCode);
        Assert.Contains("'second'", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_WhenARunMeasuredNothing_ThenItIsNotReportedAsASuccess()
    {
        // An over-narrow filter is the one failure a report cannot show: every summary is clean
        // because none of them ran anything.
        var (exitCode, report) = BenchmarkLauncher.Judge([Clean(reports: 0)], requireExecution: true);

        Assert.Equal(BenchmarkLauncher.NothingSelected, exitCode);
        Assert.Contains("nothing was measured", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_WhenAnInformationalCommandMeasuredNothing_ThenItSucceeds()
    {
        // `--list` and `--help` legitimately measure nothing.
        var (exitCode, _) = BenchmarkLauncher.Judge([], requireExecution: false);

        Assert.Equal(BenchmarkLauncher.Success, exitCode);
    }
}
