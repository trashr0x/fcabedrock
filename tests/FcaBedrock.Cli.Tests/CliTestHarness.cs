using System.Text;

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

    /// <summary>Replaces the captured stdout writer — used to inject sink failures.</summary>
    public TextWriter? OutOverride { get; set; }

    /// <summary>Replaces the captured stderr writer — used to inject sink failures.</summary>
    public TextWriter? ErrorOverride { get; set; }

    /// <summary>Every path the CLI asked to open, in order — one entry per open, not per distinct path.</summary>
    public List<string> Opened { get; } = [];

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
        OpenInput = Open,
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
/// A text sink that fails the way a closed pipe or released handle does — immediately on
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

/// <summary>A disposable temporary directory for tests that need real files on disk.</summary>
internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    /// <summary>The directory's full path.</summary>
    public string Path { get; }

    /// <summary>Creates a fresh, uniquely named temporary directory.</summary>
    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "fcabedrock-cli-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    /// <summary>Writes <paramref name="content"/> as UTF-8 without a byte-order mark and returns its full path.</summary>
    public string Write(string name, string content)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>The full path <paramref name="name"/> would have, without creating anything.</summary>
    public string Resolve(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
