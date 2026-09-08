using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>One finished child process: its exit code and both fully drained streams.</summary>
internal sealed record ToolProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// The bounded child-process runner this suite packs and drives the tool through.
/// <para>
/// Every wait is tokened and every path is bounded: a call ends within its own timeout plus one
/// <c>CleanupTimeout</c> budget. A cleanup that itself fails raises a
/// <see cref="ToolProcessCleanupException"/> carrying the trigger, the kill and probe outcomes and
/// both safe output tails — a finite, described failure instead of a hang or a context-free escape.
/// </para>
/// </summary>
internal static class ToolProcess
{
    /// <summary>What a cleanup probe could prove about the child's exit.</summary>
    internal enum ExitState
    {
        /// <summary>The child had already exited, so a kill failure here is the benign race.</summary>
        Exited,

        /// <summary>The child was still running.</summary>
        Live,

        /// <summary>The probe itself failed; its error is retained as context, never as an escape.</summary>
        Unknown,
    }

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    private const string NotDrained = "(stream not drained)";

    /// <summary>The host that runs a framework-dependent tool: the SDK's own, when it names a real file.</summary>
    internal static string DotnetHost()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }

    /// <summary>Runs <paramref name="fileName"/> to completion and returns its exit code and output.</summary>
    /// <remarks>
    /// A non-zero exit is a result, not an exception — the caller decides whether it matters.
    /// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/> only, which is the whole
    /// Windows-quoting answer, and environment entries are applied to the child alone.
    /// </remarks>
    internal static async Task<ToolProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = workingDirectory,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                info.Environment[entry.Key] = entry.Value;
            }
        }

        var effective = timeout ?? DefaultTimeout;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"'{fileName}' could not be started.");

        // Both pipes are read before anything is awaited: reading them in sequence would deadlock
        // on a child as talkative as `dotnet pack`. CancellationToken.None is deliberate — a pending
        // pipe read cannot be relied on to observe cancellation, so the bound comes from the
        // deadlines below, which abandon the WAIT and never the READ.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var drain = Task.WhenAll(stdoutTask, stderrTask);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(effective);

        try
        {
            await process.WaitForExitAsync(linked.Token);
            await drain.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException trigger)
        {
            // C1 — classify HERE, exactly once. The caller's cancellation wins only if its token
            // was already cancellation-requested at this instant; otherwise the trigger is this
            // helper's own timeout. Everything downstream carries the captured value, so a
            // cancellation arriving later — mid-kill, mid-probe, mid-drain — cannot rewrite it.
            var callerCancelled = cancellationToken.IsCancellationRequested;

            await CleanUpAsync(
                process, drain, stdoutTask, stderrTask, trigger, callerCancelled, fileName, arguments, effective,
                cancellationToken);

            // Unreachable: CleanUpAsync always throws. Kept so the compiler can see the catch never
            // falls through to a result composed from a child that never exited.
            throw;
        }

        return new ToolProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    /// <summary>Runs <paramref name="fileName"/> and throws unless it exited 0.</summary>
    internal static async Task<ToolProcessResult> RequireSuccessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(fileName, arguments, workingDirectory, environment, timeout, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"""
                {Display(fileName, arguments)} exited {result.ExitCode}.
                stdout: {result.StandardOutput}
                stderr: {result.StandardError}
                """);
        }

        return result;
    }

    /// <summary>The command line as it is reported in a failure — never re-parsed, only displayed.</summary>
    internal static string Display(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Count == 0 ? fileName : fileName + " " + string.Join(" ", arguments);
    }

    // The cancelled/timed-out path. Nothing thrown in here may escape before the deadline and the
    // diagnostic construction: every documented failure becomes retained context instead.
    private static async Task CleanUpAsync(
        Process process,
        Task drain,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        OperationCanceledException trigger,
        bool callerCancelled,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan effective,
        CancellationToken cancellationToken)
    {
        // C1 — the classification is the immutable one captured at catch entry, never a fresh
        // token read: by the time cleanup runs, the token may say something the trigger did not.
        var cause = callerCancelled ? "caller-cancelled" : "timed-out";

        // C2 — observe the drain FIRST, before anything can abandon it. Attaching this up front,
        // rather than only on a throwing path, is what completes the funnel: every later exit
        // leaves an abandoned read observed, so a faulted pipe read can never surface as an
        // unobserved task exception. It neither consumes nor replaces `drain`, still awaited at C5.
        Observe(drain);

        // C3 — kill, retaining every failure the .NET 10 reference pack documents for
        // Kill(Boolean). The AggregateException case is the partial descendant-tree failure this
        // helper is most likely to meet. No blanket catch, no discard, no rethrow: control ALWAYS
        // reaches C5, so a partial kill failure never bypasses cleanup.
        Exception? killError = null;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException exception)
        {
            killError = exception;
        }
        catch (Win32Exception exception)
        {
            killError = exception;
        }
        catch (NotSupportedException exception)
        {
            killError = exception;
        }
        catch (AggregateException exception)
        {
            killError = exception;
        }

        // C4 — a probe that never throws and never returns a Boolean. Its only use is diagnostic.
        var probe = ProbeExit(process);

        // C5 — one deadline over exit observation and BOTH drains, entered unconditionally: a
        // failed kill is precisely the case where a possibly live child must still be observed.
        // One 30 s budget spans all of it, independent of `effective` and of the caller's token.
        Exception? waitError = null;
        using var cleanup = new CancellationTokenSource(CleanupTimeout);

        // C6 — every fault funnels into one variable. The deadline's OperationCanceledException
        // (unambiguous: the reads use CancellationToken.None, so `cleanup` is the only token in
        // play) and any other failure — a faulted pipe read surfaced through WaitAsync, a Process
        // API failure — are the same thing here: a cleanup failure. Nothing escapes.
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (Exception exception)
        {
            waitError = exception;
        }

        if (waitError is null)
        {
            try
            {
                await drain.WaitAsync(cleanup.Token);
            }
            catch (Exception exception)
            {
                waitError = exception;
            }
        }

        var commandLine = Display(fileName, arguments);

        // C7 — cleanup succeeded, so the ORIGINAL outcome is surfaced unchanged. This holds even
        // when C3 or C4 failed: a kill that raced a natural exit changes nothing.
        if (waitError is null)
        {
            // Only the CAPTURED classification decides. A caller cancellation that arrived while
            // cleanup was draining is a later event, not the trigger, so the token is consulted
            // solely to raise the cancellation it was already requesting at catch entry — never
            // to convert a captured timeout into one.
            if (callerCancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new TimeoutException(
                $"""
                {commandLine} did not exit within {effective}.
                stdout: {SafeText(stdoutTask)}
                stderr: {SafeText(stderrTask)}
                """);
        }

        // C8 — one bounded contextual exception, built only from already-materialized values.
        throw new ToolProcessCleanupException(
            $"""
            {commandLine} was {cause} after {effective} and its cleanup did not complete within {CleanupTimeout}.
            exit probe: {Word(probe.State)}; kill: {Named(killError)}; probe: {Named(probe.Error)}; wait: {Named(waitError)}.
            stdout: {SafeText(stdoutTask)}
            stderr: {SafeText(stderrTask)}
            """,
            cause,
            trigger,
            commandLine,
            killError,
            probe.State,
            probe.Error,
            waitError,
            SafeText(stdoutTask),
            SafeText(stderrTask));
    }

    // Fire-and-forget, never awaited: it exists only so an abandoned read's fault is observed.
    private static void Observe(Task task) =>
        task.ContinueWith(
            static observed => _ = observed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // The three failures the reference pack documents for HasExited are retained, not raised:
    // `Unknown` is neither `Live` nor `Exited`, and its error becomes diagnostic context.
    private static ExitProbe ProbeExit(Process process)
    {
        try
        {
            return new ExitProbe(process.HasExited ? ExitState.Exited : ExitState.Live, null);
        }
        catch (InvalidOperationException exception)
        {
            return new ExitProbe(ExitState.Unknown, exception);
        }
        catch (Win32Exception exception)
        {
            return new ExitProbe(ExitState.Unknown, exception);
        }
        catch (NotSupportedException exception)
        {
            return new ExitProbe(ExitState.Unknown, exception);
        }
    }

    // Never blocks, never throws, and never invents partial text.
    private static string SafeText(Task<string> task) =>
        task.IsCompletedSuccessfully ? task.Result : NotDrained;

    private static string Word(ExitState state) => state switch
    {
        ExitState.Exited => "exited",
        ExitState.Live => "live",
        _ => "unknown",
    };

    private static string Named(Exception? error) => error?.GetType().Name ?? "none";

    // The probe's carrier: private, and only its two components ever cross the exception surface.
    private readonly record struct ExitProbe(ExitState State, Exception? Error);
}

/// <summary>
/// The one failure a bounded cleanup can still produce: the child was cancelled or timed out, and
/// observing its exit or draining its output did not finish inside the cleanup deadline.
/// <para>
/// It is contextual by construction — the trigger, the classification, the command line, each
/// retained error and both safe output tails are properties, so a failure is diagnosable without a
/// debugger and without re-running anything.
/// </para>
/// </summary>
internal sealed class ToolProcessCleanupException : Exception
{
    internal ToolProcessCleanupException(
        string message,
        string cause,
        OperationCanceledException trigger,
        string commandLine,
        Exception? killError,
        ToolProcess.ExitState exitProbe,
        Exception? exitProbeError,
        Exception? waitError,
        string standardOutput,
        string standardError)
        : base(message, SelectInner(killError, exitProbeError, waitError))
    {
        Cause = cause;
        Trigger = trigger;
        CommandLine = commandLine;
        KillError = killError;
        ExitProbe = exitProbe;
        ExitProbeError = exitProbeError;
        WaitError = waitError;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    /// <summary><c>caller-cancelled</c> or <c>timed-out</c> — classified once, at catch entry.</summary>
    public string Cause { get; }

    /// <summary>The initiating cancellation or timeout; never the inner exception, always retained.</summary>
    public OperationCanceledException Trigger { get; }

    /// <summary>The command line as displayed, never re-parsed.</summary>
    public string CommandLine { get; }

    /// <summary>The documented <c>Kill(true)</c> failure, when there was one.</summary>
    public Exception? KillError { get; }

    /// <summary>What the exit probe could prove.</summary>
    public ToolProcess.ExitState ExitProbe { get; }

    /// <summary>The probe's own failure, when it had one.</summary>
    public Exception? ExitProbeError { get; }

    /// <summary>The cleanup deadline, or the fault the exit/drain observation surfaced.</summary>
    public Exception? WaitError { get; }

    /// <summary>The drained stdout, or a marker when the stream was never drained.</summary>
    public string StandardOutput { get; }

    /// <summary>The drained stderr, or a marker when the stream was never drained.</summary>
    public string StandardError { get; }

    // Zero retained errors leave no inner exception; one becomes the inner exception; two or three
    // aggregate in a fixed order — kill, exit probe, wait/drain — so the shape is deterministic.
    private static Exception? SelectInner(Exception? killError, Exception? exitProbeError, Exception? waitError)
    {
        var errors = new List<Exception>(3);
        if (killError is not null)
        {
            errors.Add(killError);
        }

        if (exitProbeError is not null)
        {
            errors.Add(exitProbeError);
        }

        if (waitError is not null)
        {
            errors.Add(waitError);
        }

        return errors.Count switch
        {
            0 => null,
            1 => errors[0],
            _ => new AggregateException(errors),
        };
    }
}

/// <summary>
/// The tool package, produced once per test run: one offline <c>dotnet pack</c> of the CLI project
/// into a temporary directory, followed by the checkout's actual HEAD commit.
/// <para>
/// It is a COLLECTION fixture (see <see cref="ToolPackageCollection"/>) precisely because both
/// consumers must share it: two independent class fixtures would pack twice, concurrently, into the
/// same <c>bin\Release</c>/<c>obj\Release</c> trees.
/// </para>
/// </summary>
public sealed class ToolPackage : IAsyncLifetime
{
    // The child sees exactly the noise suppression, and nothing else is mutated anywhere.
    private static readonly Dictionary<string, string> QuietDotnet = new(StringComparer.Ordinal)
    {
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
    };

    private TempDirectory? _output;

    /// <summary>The single packed <c>.nupkg</c>.</summary>
    public string NupkgPath { get; private set; } = string.Empty;

    /// <summary>The directory holding it — usable verbatim as a local NuGet feed.</summary>
    public string FeedDirectory { get; private set; } = string.Empty;

    /// <summary>The package version, parsed from the packed file name.</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>The checkout's actual HEAD commit, captured right after the pack.</summary>
    public string HeadCommit { get; private set; } = string.Empty;

    /// <summary>The on-disk package README the pack is expected to have shipped verbatim.</summary>
    public string SourceReadmePath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        var root = RepositoryRoot();
        SourceReadmePath = Path.Combine(root, "src", "FcaBedrock.Cli", "README.md");
        _output = TempDirectory.Create();

        // `--no-restore` makes the pack provably offline: whatever ran this test restored the graph
        // already (this project references the CLI, and restore is not per-configuration). Missing
        // assets fail NETSDK1004 verbatim — loud, never a silent fetch.
        await ToolProcess.RequireSuccessAsync(
            ToolProcess.DotnetHost(),
            [
                "pack",
                Path.Combine("src", "FcaBedrock.Cli", "FcaBedrock.Cli.csproj"),
                "-c",
                "Release",
                "--no-restore",
                "--nologo",
                "-o",
                _output.Path,
            ],
            root,
            QuietDotnet,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        var packages = Directory.GetFiles(_output.Path, "*.nupkg");
        if (packages.Length != 1)
        {
            throw new InvalidOperationException(
                $"the pack produced {packages.Length} .nupkg files in '{_output.Path}': {string.Join(", ", packages)}");
        }

        NupkgPath = packages[0];
        FeedDirectory = _output.Path;
        Version = ParseVersion(Path.GetFileName(NupkgPath));

        // Immediately after the pack, so the window in which a curated commit could land between
        // the packed nuspec's commit and this capture is milliseconds wide.
        var head = await ToolProcess.RequireSuccessAsync(
            "git",
            ["rev-parse", "HEAD"],
            root,
            environment: null,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        HeadCommit = head.StandardOutput.Trim();
        if (!IsCommit(HeadCommit))
        {
            throw new InvalidOperationException($"`git rev-parse HEAD` returned '{head.StandardOutput}'.");
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _output?.Dispose();
        _output = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>True for a lowercase 40-character hexadecimal commit id, and nothing else.</summary>
    public static bool IsCommit(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 40)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A FRESH read-only archive, so no caller inherits another's stream lifetime.</summary>
    public ZipArchive OpenArchive() => ZipFile.OpenRead(NupkgPath);

    /// <summary>
    /// Every packed entry, ordinal-sorted, minus the three entries that are not reproducible
    /// across two back-to-back packs of identical sources.
    /// </summary>
    public IReadOnlyList<string> EntryNames()
    {
        using var archive = OpenArchive();
        var names = new List<string>();
        foreach (var entry in archive.Entries)
        {
            if (!IsNotReproducible(entry.FullName))
            {
                names.Add(entry.FullName);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>The entry's bytes.</summary>
    public byte[] ReadEntryBytes(string name)
    {
        using var archive = OpenArchive();
        using var content = Entry(archive, name).Open();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The entry's text, read as UTF-8.</summary>
    public string ReadEntryText(string name)
    {
        using var archive = OpenArchive();
        using var reader = new StreamReader(
            Entry(archive, name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return reader.ReadToEnd();
    }

    /// <summary>Everything an assertion failure needs in order to be diagnosable on its own.</summary>
    public string Describe() =>
        $"[package '{NupkgPath}', version '{Version}', HEAD '{HeadCommit}', entries: {string.Join(", ", EntryNames())}]";

    private static ZipArchiveEntry Entry(ZipArchive archive, string name) =>
        archive.GetEntry(name) ?? throw new InvalidOperationException($"the package has no entry '{name}'.");

    // The producer-named core-properties part, the relationship part, and the content-type map all
    // vary between two packs of identical sources, so nothing here ever asserts them. Their
    // CONSISTENCY is asserted, through the bounded validator in `PackageOpc`; their bytes are not.
    private static bool IsNotReproducible(string name) =>
        name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
        || string.Equals(name, "_rels/.rels", StringComparison.Ordinal)
        || string.Equals(name, "[Content_Types].xml", StringComparison.Ordinal);

    private static string ParseVersion(string fileName)
    {
        const string prefix = "FcaBedrock.Cli.";
        const string suffix = ".nupkg";

        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(suffix, StringComparison.Ordinal)
            || fileName.Length <= prefix.Length + suffix.Length)
        {
            throw new InvalidOperationException($"unexpected package file name '{fileName}'.");
        }

        return fileName[prefix.Length..^suffix.Length];
    }

    // Never a fixed `..` hop count: the answer is wherever the solution file actually is, and a
    // failure names every directory that was probed.
    private static string RepositoryRoot()
    {
        var probed = new List<string>();
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            probed.Add(directory.FullName);
            if (File.Exists(Path.Combine(directory.FullName, "FcaBedrock.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"FcaBedrock.slnx was not found above '{AppContext.BaseDirectory}'; probed: {string.Join(", ", probed)}");
    }
}

/// <summary>
/// The one collection both tool-package classes join, so the shared <see cref="ToolPackage"/> is
/// created once and the two classes can never pack — or install — concurrently.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ToolPackageCollection : ICollectionFixture<ToolPackage>
{
    /// <summary>The collection name both classes name in their <c>[Collection]</c> attribute.</summary>
    public const string Name = "tool package";
}
