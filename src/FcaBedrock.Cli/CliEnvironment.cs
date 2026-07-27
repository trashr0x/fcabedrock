using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli;

/// <summary>
/// Observes command progress. M7 ships <b>no</b> progress meter (D-122 part 3), so the
/// only production implementation is <see cref="NoProgressObserver"/>; the seam exists
/// because progress is a committed follow-up and D-122 requires it to centralize now, so
/// that adding it later is a renderer/observer implementation rather than a
/// run-orchestration refactor. Every command is bracketed here by
/// <see cref="CliHost"/>, so no handler has to remember to report.
/// </summary>
internal interface IProgressObserver
{
    /// <summary>A parsed command is about to run.</summary>
    void CommandStarted(string command);

    /// <summary>A command finished with <paramref name="exitCode"/>.</summary>
    void CommandCompleted(string command, int exitCode);
}

/// <summary>The no-op observer: M7's production behaviour — progress is silent.</summary>
internal sealed class NoProgressObserver : IProgressObserver
{
    private NoProgressObserver()
    {
    }

    /// <summary>The shared instance.</summary>
    public static NoProgressObserver Instance { get; } = new();

    /// <inheritdoc/>
    public void CommandStarted(string command)
    {
    }

    /// <inheritdoc/>
    public void CommandCompleted(string command, int exitCode)
    {
    }
}

/// <summary>
/// Everything the CLI is allowed to know about the world outside it. <see cref="Program"/>
/// builds the real one; argv-boundary tests build a deterministic one, which is what makes
/// exit codes, rendered bytes, signals, and input behaviour testable without a console, a
/// clock, or a race (D-123 parts 4/5).
/// </summary>
internal sealed class CliEnvironment
{
    /// <summary>Primary results. Never receives diagnostics.</summary>
    public required TextWriter Out { get; init; }

    /// <summary>Diagnostics, usage text, and code-less host errors. Never receives primary results.</summary>
    public required TextWriter Error { get; init; }

    /// <summary>The injected clock (§15 <c>timestamp</c>).</summary>
    public required IClock Clock { get; init; }

    /// <summary>The cooperative-cancellation source.</summary>
    public required ISignalSource Signals { get; init; }

    /// <summary>The shared <c>fcabedrock-vnext …</c> string (<c>--version</c> and §15 <c>tool_version</c>).</summary>
    public required string ToolVersion { get; init; }

    /// <summary>
    /// The <b>complete</b> process command-line array including its actual argv[0]
    /// (CX-M7P-007/012), captured separately from the parser's ordinary arguments and
    /// recorded verbatim by the run manifest. A non-<c>fcabedrock</c> argv[0] — a full
    /// host-executable path, a shim — is preserved unchanged: <c>command_line</c> is an
    /// audit record of what the process actually received, and synthesizing a constant
    /// would discard exactly the information the field exists to keep.
    /// </summary>
    public required IReadOnlyList<string> AuditArgv { get; init; }

    /// <summary>
    /// Opens a named input for reading (CX-M7P-008). Production opens the real file;
    /// argv tests return pass-specific streams, which is what makes input behaviour —
    /// including, later, input-stability replay — deterministic rather than racy. Every
    /// file the CLI <em>reads</em> goes through here: the root spec, each <c>extends</c>
    /// base, and the data source. It is no public API and no user option.
    /// </summary>
    public required Func<string, Stream> OpenInput { get; init; }

    /// <summary>
    /// Every filesystem operation publication performs. Production is the real filesystem;
    /// direct tests substitute one that fails a chosen boundary, which is how each commit,
    /// backup, restore, and recovery step is proven rather than assumed (D-123 point 7).
    /// </summary>
    public IPublicationFileSystem PublicationFiles { get; init; } = PublicationFileSystem.Instance;

    /// <summary>Progress observation; the no-op observer in production.</summary>
    public IProgressObserver Progress { get; init; } = NoProgressObserver.Instance;

    /// <summary>The production <see cref="OpenInput"/> implementation.</summary>
    internal static Stream OpenFile(string path) => File.OpenRead(path);
}
