using System.Diagnostics;
using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The process boundary in front of <see cref="CliHost.RunAsync"/>: standard-stream setup and
/// its failures (D-122 parts 2 and 3), driven through a recording console and an injected signal
/// factory so that no test changes the test host's console or registers a real signal handler,
/// plus one real child process whose redirected bytes are read as bytes.
/// </summary>
public sealed class ProgramTests
{
    // An unknown command. Had setup let the command line be parsed, stderr would carry a usage
    // error, so an exact stderr proves that nothing ran after a failed setup.
    private static readonly string[] UnknownCommand = ["frobnicate"];

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Theory]
    [InlineData(nameof(IStandardConsole.OpenStandardOutput), true, nameof(IOException))]
    [InlineData(nameof(IStandardConsole.OpenStandardOutput), true, nameof(ObjectDisposedException))]
    [InlineData(nameof(IStandardConsole.ConsoleOutput), false, nameof(IOException))]
    [InlineData(nameof(IStandardConsole.ConsoleOutput), false, nameof(ObjectDisposedException))]
    public async Task RunAsync_WhenStdoutSetupFailsWithAnOutputFailure_ThenExitOneIsReportedThroughStderrAndNothingRuns(
        string failCall, bool outputRedirected, string exception)
    {
        var signals = new CountingSignalFactory();
        var console = new RecordingConsole
        {
            OutputRedirected = outputRedirected,
            ErrorRedirected = true,
            FailCall = failCall,
            FailWith = Failure(exception),
        };

        var exit = await Program.RunAsync(UnknownCommand, console, signals.Create);

        Assert.Equal(1, exit);
        Assert.Equal(Utf8.GetBytes($"error: {CliHost.OutputFailureMessage}\n"), console.StandardError.ToArray());
        Assert.Equal(0, signals.Created);
    }

    [Theory]
    [InlineData(nameof(IStandardConsole.UseUnicodeOutput), false, nameof(IOException))]
    [InlineData(nameof(IStandardConsole.ConsoleError), false, nameof(ObjectDisposedException))]
    [InlineData(nameof(IStandardConsole.OpenStandardError), true, nameof(IOException))]
    public async Task RunAsync_WhenSetupFailsWithAnOutputFailureBeforeStderrExists_ThenExitOneWithNoReportAndNothingRuns(
        string failCall, bool errorRedirected, string exception)
    {
        var signals = new CountingSignalFactory();
        var console = new RecordingConsole
        {
            ErrorRedirected = errorRedirected,
            FailCall = failCall,
            FailWith = Failure(exception),
        };

        var exit = await Program.RunAsync(UnknownCommand, console, signals.Create);

        // No fallback: the report goes only to a stderr that was built, never to stdout.
        Assert.Equal(1, exit);
        Assert.DoesNotContain(nameof(IStandardConsole.ConsoleOutput), console.Calls);
        Assert.DoesNotContain(nameof(IStandardConsole.OpenStandardOutput), console.Calls);
        AssertNothingWritten(console);
        Assert.Equal(0, signals.Created);
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]

    // Off Windows the runtime reports a standard descriptor it cannot duplicate for a permission or
    // bad-descriptor reason as this type, so that case is an unexpected fault too.
    [InlineData(nameof(UnauthorizedAccessException))]
    public async Task RunAsync_WhenStdoutSetupFailsUnexpectedly_ThenExitFourIsReportedThroughStderrAndNothingRuns(
        string exception)
    {
        var signals = new CountingSignalFactory();
        var console = new RecordingConsole
        {
            OutputRedirected = true,
            ErrorRedirected = true,
            FailCall = nameof(IStandardConsole.OpenStandardOutput),
            FailWith = Failure(exception),
        };

        var exit = await Program.RunAsync(UnknownCommand, console, signals.Create);

        Assert.Equal(4, exit);
        Assert.Equal(Utf8.GetBytes($"error: {CliHost.UnexpectedFaultMessage}\n"), console.StandardError.ToArray());
        Assert.Equal(0, signals.Created);
    }

    [Fact]
    public async Task RunAsync_WhenStderrSetupFailsUnexpectedly_ThenExitFourWithNoReportAndNothingRuns()
    {
        var signals = new CountingSignalFactory();
        var console = new RecordingConsole
        {
            ErrorRedirected = true,
            FailCall = nameof(IStandardConsole.OpenStandardError),
            FailWith = Failure(nameof(InvalidOperationException)),
        };

        var exit = await Program.RunAsync(UnknownCommand, console, signals.Create);

        Assert.Equal(4, exit);
        AssertNothingWritten(console);
        Assert.Equal(0, signals.Created);
    }

    [Fact]
    public async Task RunAsync_WhenTheSetupFailureReportItselfFails_ThenNothingEscapesAndTheOutcomeStands()
    {
        var console = new RecordingConsole
        {
            OutputRedirected = true,
            ConsoleErrorWriter = new ReportFailingWriter(),
            FailCall = nameof(IStandardConsole.OpenStandardOutput),
        };

        var exit = await Program.RunAsync(UnknownCommand, console, new CountingSignalFactory().Create);

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_WhenTheWiringFailureReportItselfFails_ThenNothingEscapesAndExitFourStands()
    {
        var console = new RecordingConsole
        {
            OutputRedirected = true,
            ConsoleErrorWriter = new ReportFailingWriter(),
        };

        var exit = await Program.RunAsync(
            UnknownCommand, console, static () => throw new InvalidOperationException("the signal registration failed."));

        Assert.Equal(4, exit);
    }

    [Fact]
    public async Task RunAsync_WhenSetupSucceeds_ThenTheCommandWritesThroughTheBuiltWritersAndCompletionFlushesThem()
    {
        var signals = new CountingSignalFactory();
        var console = new RecordingConsole { OutputRedirected = true, ErrorRedirected = true };

        var exit = await Program.RunAsync(["--version"], console, signals.Create);

        Assert.Equal(0, exit);
        Assert.Equal(Utf8.GetBytes(ToolVersion.Current + "\n"), console.StandardOutput.ToArray());
        Assert.Empty(console.StandardError.ToArray());
        Assert.Equal(1, signals.Created);
        Assert.True(signals.Last!.Disposed);
    }

    [Fact]
    public async Task Main_WhenBothStreamsArePipes_ThenEachCarriesTheRenderedTextAsExactUtf8()
    {
        // A real process, because only one shows the bytes its own writers produce. Both of its
        // streams are pipes, so this guards the redirected bytes; it cannot reach a console route.
        var tool = Path.Combine(AppContext.BaseDirectory, "FcaBedrock.Cli.dll");
        Assert.SkipUnless(
            File.Exists(tool) && File.Exists(Path.ChangeExtension(tool, ".runtimeconfig.json")),
            "the CLI assembly and its runtime configuration are not copied into this test's output.");

        using var temp = TempDirectory.Create();
        var spec = temp.Write("unicode.toml", UnicodeSpec);
        var data = temp.Write("unicode.csv", "value\nnaïve_Ω_Ж_漢_😀\n");

        // A report and a section-sign diagnostic, then canonical TOML on stdout.
        string[][] invocations = [["plan", spec, data], ["calibrate", spec, data, "--out", "-"]];
        foreach (var arguments in invocations)
        {
            // The oracle is the same command in-process: the same operands give the same text,
            // and argv[0], the only argument that differs, appears in neither output.
            var harness = new CliTestHarness { ToolVersion = ToolVersion.Current };
            var expectedExit = await harness.RunAsync(arguments);

            var (exit, stdout, stderr) = await RunWithRawStreamsAsync(tool, arguments, temp.Path);

            Assert.Equal(0, expectedExit);
            Assert.Equal(expectedExit, exit);
            Assert.Equal(Utf8.GetBytes(harness.StdOut), stdout);
            Assert.Equal(Utf8.GetBytes(harness.StdErr), stderr);
            Assert.True(Contains(stdout, Utf8.GetBytes("café_Ω_Ж_漢_😀")), "stdout lacks the authored Unicode name.");
            Assert.True(Contains(stderr, Utf8.GetBytes("(§10.3)")), "stderr lacks the section-sign citation.");
            foreach (var bytes in new[] { stdout, stderr })
            {
                Assert.False(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), "a byte order mark was written.");
                Assert.DoesNotContain((byte)'\r', bytes);
            }
        }
    }

    private const string UnicodeSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        [[attribute]]
        name = "café_Ω_Ж_漢_😀"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        """;

    private static Func<Exception> Failure(string exception) => exception switch
    {
        nameof(IOException) => static () => new IOException("the handle is invalid."),
        nameof(ObjectDisposedException) => static () => new ObjectDisposedException("standard stream"),
        nameof(UnauthorizedAccessException) => static () => new UnauthorizedAccessException("C:\\secret\\denied"),
        _ => static () => new InvalidOperationException("C:\\secret\\internal detail"),
    };

    private static void AssertNothingWritten(RecordingConsole console)
    {
        Assert.Equal(string.Empty, console.ConsoleOutputWriter.ToString());
        Assert.Equal(string.Empty, console.ConsoleErrorWriter.ToString());
        Assert.Empty(console.StandardOutput.ToArray());
        Assert.Empty(console.StandardError.ToArray());
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    // ToolProcess decodes what the child wrote, which would hide a byte order mark or an encoding
    // change, so this reads both pipes as bytes. One deadline bounds the wait.
    private static async Task<(int ExitCode, byte[] StandardOutput, byte[] StandardError)> RunWithRawStreamsAsync(
        string tool, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(ToolProcess.DotnetHost())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        info.ArgumentList.Add(tool);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("the CLI could not be started.");
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();

        // Both pipes are drained before anything is awaited, so neither can fill and stall the child.
        var drain = Task.WhenAll(
            process.StandardOutput.BaseStream.CopyToAsync(stdout),
            process.StandardError.BaseStream.CopyToAsync(stderr));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            await drain.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, stdout.ToArray(), stderr.ToArray());
    }

    private sealed class CountingSignalFactory
    {
        public int Created { get; private set; }

        public TestSignalSource? Last { get; private set; }

        public ISignalSource Create()
        {
            Created++;
            Last = new TestSignalSource();
            return Last;
        }
    }

    // A report sink that fails with something other than an I/O failure.
    private sealed class ReportFailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw new InvalidOperationException("the report failed.");

        public override void Write(string? value) => throw new InvalidOperationException("the report failed.");
    }
}
