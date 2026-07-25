namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The injected environment facts themselves. The audit argv has no consumer until the run
/// manifest lands (S5's writer is already byte-locked against it), so its carrier is pinned
/// here: the value must survive the boundary verbatim, including an argv[0] that is not
/// simply <c>fcabedrock</c> (CX-M7P-012).
/// </summary>
public sealed class CliEnvironmentTests
{
    [Fact]
    public void AuditArgv_WhenArgvZeroIsAHostExecutablePath_ThenItIsCarriedVerbatim()
    {
        string[] argv =
        [
            @"C:\tools\fcabedrock.exe", "convert", "adult.toml", "adult.csv", "--out", "adult", "--format", "both",
        ];
        var harness = new CliTestHarness { AuditArgv = argv };

        Assert.Equal(argv, harness.Environment.AuditArgv);
    }

    [Fact]
    public void AuditArgv_WhenArgumentsCarryUnicodeAndControls_ThenTheyAreCarriedVerbatim()
    {
        string[] argv = ["/usr/local/bin/fcabedrock", "validate", "caf\u00e9/\u4e2d\u6587.toml", "a\tb", string.Empty];
        var harness = new CliTestHarness { AuditArgv = argv };

        Assert.Equal(argv, harness.Environment.AuditArgv);
    }

    [Fact]
    public void AuditArgv_WhenBuilt_ThenParsingNeverSeesIt()
    {
        // The parser consumes the ordinary arguments only; argv[0] is an audit fact and
        // must never be mistaken for a command word.
        var outcome = CommandLineParser.Parse([@"C:\tools\fcabedrock.exe", "validate", "s.toml"]);

        var failure = Assert.IsType<UsageFailure>(outcome);
        Assert.Contains(@"unknown command 'C:\tools\fcabedrock.exe'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_WhenInjected_ThenTheEnvironmentReportsTheInjectedInstant()
    {
        var harness = new CliTestHarness();
        harness.Clock.UtcNow = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero), harness.Environment.Clock.UtcNow);
    }

    [Fact]
    public void SystemClock_WhenRead_ThenItReportsUtc()
    {
        Assert.Equal(TimeSpan.Zero, SystemClock.Instance.UtcNow.Offset);
    }

    [Fact]
    public void OpenFile_WhenTheProductionOpenerIsUsed_ThenItReadsTheRequestedFile()
    {
        using var temp = TempDirectory.Create();
        var path = temp.Write("a.txt", "hello");

        using var stream = CliEnvironment.OpenFile(path);
        using var reader = new StreamReader(stream);

        Assert.Equal("hello", reader.ReadToEnd());
    }

    [Fact]
    public void Progress_WhenNotSupplied_ThenTheNoOpObserverIsTheDefault()
    {
        var environment = new CliEnvironment
        {
            Out = TextWriter.Null,
            Error = TextWriter.Null,
            Clock = SystemClock.Instance,
            Signals = new TestSignalSource(),
            ToolVersion = "x",
            AuditArgv = ["fcabedrock"],
            OpenInput = CliEnvironment.OpenFile,
        };

        Assert.Same(NoProgressObserver.Instance, environment.Progress);
    }
}
