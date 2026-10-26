using System.Text;
using FcaBedrock.Orchestration;

namespace FcaBedrock.Cli.Tests;

/// <summary>A clock that never moves unless a test moves it.</summary>
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>A signal source a test cancels directly, without raising a real signal.</summary>
internal sealed class TestSignalSource : ISignalSource
{
    private readonly CancellationTokenSource _source = new();

    public CancellationToken Token => _source.Token;

    public bool Disposed { get; private set; }

    public void Cancel() => _source.Cancel();

    public void Dispose()
    {
        Disposed = true;
        _source.Dispose();
    }
}

/// <summary>Records the progress events the host brackets each command with.</summary>
internal sealed class RecordingProgressObserver : IProgressObserver
{
    public List<string> Events { get; } = [];

    public void CommandStarted(string command) => Events.Add($"started:{command}");

    public void CommandCompleted(string command, int exitCode) => Events.Add($"completed:{command}:{exitCode}");
}

/// <summary>
/// Drives <see cref="CliHost.RunAsync"/> with a fully injected world: captured writers, a
/// fixed clock and version, a cancellable signal source, and a recording input opener.
/// Nothing here touches the console, and no test depends on wall-clock time, the machine's
/// culture, or a real signal.
/// </summary>
internal sealed class CliTestHarness
{
    private readonly StringWriter _out = new(new StringBuilder());
    private readonly StringWriter _error = new(new StringBuilder());

    /// <summary>The version string the injected environment reports.</summary>
    public string ToolVersion { get; set; } = "fcabedrock-vnext 9.9.9-test";

    /// <summary>The complete audit argv, argv[0] included.</summary>
    public IReadOnlyList<string> AuditArgv { get; set; } = ["fcabedrock"];

    /// <summary>Replaces the input opener; the default opens real files.</summary>
    public Func<string, Stream>? OpenInput { get; set; }

    /// <summary>Replaces the captured stdout writer (used to inject sink failures).</summary>
    public TextWriter? OutOverride { get; set; }

    /// <summary>Replaces the captured stderr writer (used to inject sink failures).</summary>
    public TextWriter? ErrorOverride { get; set; }

    /// <summary>Every path the CLI asked to open, in order: one entry per open, not per distinct path.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>
    /// The publication filesystem the run uses. It is the real one, wrapped so a test can watch
    /// the exact sequence of operations and fail any single boundary.
    /// </summary>
    public RecordingPublicationFileSystem PublicationFiles { get; } = new();

    /// <summary>Replaces the spec chain's identity-service factory; the default is the platform's real one.</summary>
    public Func<FileIdentity>? FileIdentities { get; set; }

    public TestClock Clock { get; } = new();

    public TestSignalSource Signals { get; } = new();

    public RecordingProgressObserver Progress { get; } = new();

    /// <summary>Everything written to stdout.</summary>
    public string StdOut => _out.ToString();

    /// <summary>Everything written to stderr.</summary>
    public string StdErr => _error.ToString();

    /// <summary>The injected environment.</summary>
    public CliEnvironment Environment => new()
    {
        Out = OutOverride ?? _out,
        Error = ErrorOverride ?? _error,
        Clock = Clock,
        Signals = Signals,
        ToolVersion = ToolVersion,
        AuditArgv = AuditArgv,
        Orchestration = new OrchestrationHost(Open)
        {
            PublicationFiles = PublicationFiles,
            SpecIdentities = FileIdentities ?? FileIdentity.CreateDefault,
        },
        Progress = Progress,
    };

    /// <summary>Runs the CLI at the argv boundary.</summary>
    public Task<int> RunAsync(params string[] argv) => CliHost.RunAsync(argv, Environment);

    private Stream Open(string path)
    {
        Opened.Add(path);
        return (OpenInput ?? CliEnvironment.OpenFile)(path);
    }
}

/// <summary>
/// A text sink that fails the way a closed pipe or released handle does: immediately on
/// write, or only when the buffered bytes are finally flushed.
/// </summary>
internal sealed class ThrowingWriter(bool failOnWrite, bool failOnFlush) : TextWriter
{
    private readonly StringWriter _accepted = new();

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>Everything the sink accepted before failing.</summary>
    public string Accepted => _accepted.ToString();

    public override void Write(char value)
    {
        if (failOnWrite)
        {
            throw new IOException("the pipe has been ended.");
        }

        _accepted.Write(value);
    }

    public override void Write(string? value)
    {
        if (failOnWrite)
        {
            throw new IOException("the pipe has been ended.");
        }

        _accepted.Write(value);
    }

    public override void Flush()
    {
        if (failOnFlush)
        {
            throw new IOException("the pipe has been ended.");
        }

        _accepted.Flush();
    }
}

/// <summary>
/// The standard streams of one simulated process. A console stream hands out a captured
/// writer, a redirected one a captured byte stream. Every call is recorded in order and any
/// one call can be made to fail, so the choice of writer, its order and a setup failure are
/// observable. It never touches the test host's own console: no test may change the test
/// host's console encoding or writers.
/// </summary>
internal sealed class RecordingConsole : IStandardConsole
{
    public bool IsWindows { get; init; } = true;

    public bool OutputRedirected { get; init; }

    public bool ErrorRedirected { get; init; }

    /// <summary>The writer a console stdout gets.</summary>
    public TextWriter ConsoleOutputWriter { get; init; } = new StringWriter();

    /// <summary>The writer a console stderr gets.</summary>
    public TextWriter ConsoleErrorWriter { get; init; } = new StringWriter();

    /// <summary>Every byte written to a redirected stdout.</summary>
    public MemoryStream StandardOutput { get; } = new();

    /// <summary>Every byte written to a redirected stderr.</summary>
    public MemoryStream StandardError { get; } = new();

    /// <summary>Every call, by its <see cref="IStandardConsole"/> member name, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The <see cref="IStandardConsole"/> member name whose call fails, if any.</summary>
    public string? FailCall { get; init; }

    /// <summary>The exception the failing call raises.</summary>
    public Func<Exception> FailWith { get; init; } = static () => new IOException("the handle is invalid.");

    bool IStandardConsole.IsOutputRedirected => Record(nameof(IStandardConsole.IsOutputRedirected), OutputRedirected);

    bool IStandardConsole.IsErrorRedirected => Record(nameof(IStandardConsole.IsErrorRedirected), ErrorRedirected);

    void IStandardConsole.UseUnicodeOutput() => Record(nameof(IStandardConsole.UseUnicodeOutput), true);

    TextWriter IStandardConsole.ConsoleOutput() => Record(nameof(IStandardConsole.ConsoleOutput), ConsoleOutputWriter);

    TextWriter IStandardConsole.ConsoleError() => Record(nameof(IStandardConsole.ConsoleError), ConsoleErrorWriter);

    Stream IStandardConsole.OpenStandardOutput() => Record(nameof(IStandardConsole.OpenStandardOutput), StandardOutput);

    Stream IStandardConsole.OpenStandardError() => Record(nameof(IStandardConsole.OpenStandardError), StandardError);

    private T Record<T>(string call, T result)
    {
        Calls.Add(call);
        if (string.Equals(call, FailCall, StringComparison.Ordinal))
        {
            throw FailWith();
        }

        return result;
    }
}
