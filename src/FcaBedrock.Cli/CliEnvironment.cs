using FcaBedrock.Orchestration;

namespace FcaBedrock.Cli;

/// <summary>
/// Observes command progress. The CLI has <b>no</b> progress meter (D-122 part 3), so the
/// only production implementation is <see cref="NoProgressObserver"/>. The seam exists
/// because D-122 part 3 requires progress observation to be centralized: a progress meter,
/// which is in the deferred backlog, then needs only an observer implementation, not a
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

/// <summary>The no-op observer, used in production: progress is silent.</summary>
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

    /// <summary>The shared <c>fcabedrock-vnext-cli …</c> string (<c>--version</c> and §15 <c>tool_version</c>).</summary>
    public required string ToolVersion { get; init; }

    /// <summary>
    /// The <b>complete</b> process command-line array including its actual argv[0],
    /// captured separately from the parser's ordinary arguments and
    /// recorded verbatim by the run manifest. A non-<c>fcabedrock</c> argv[0] (a full
    /// host-executable path, a shim) is preserved unchanged: <c>command_line</c> is an
    /// audit record of what the process received, and synthesizing a constant
    /// would discard exactly the information the field exists to keep.
    /// </summary>
    public required IReadOnlyList<string> AuditArgv { get; init; }

    /// <summary>
    /// What the CLI lends the run and publication coordinator (D-140). Every file the CLI
    /// <em>reads</em> opens through its <see cref="OrchestrationHost.OpenInput"/>: the root spec,
    /// each <c>extends</c> base, the data source, probe's DATA and migrate's <c>.bed</c>. Production
    /// opens the real file; argv tests return pass-specific streams, which is what makes input
    /// behaviour, input-stability replay included, deterministic rather than racy, and they also set
    /// the host's publication filesystem and chain identity seams. None of it is a user option.
    /// </summary>
    public required OrchestrationHost Orchestration { get; init; }

    /// <summary>Progress observation; the no-op observer in production. The CLI's own, never the coordinator's.</summary>
    public IProgressObserver Progress { get; init; } = NoProgressObserver.Instance;

    /// <summary>
    /// The production input opener for <see cref="OrchestrationHost.OpenInput"/>. An empty path names no file, so it
    /// fails here as a missing file does, before <see cref="File.OpenRead(string)"/> sees it:
    /// every command reports it as a missing input (exit 1).
    /// </summary>
    internal static Stream OpenFile(string path) =>
        path.Length == 0 ? throw new FileNotFoundException("The path is empty.") : File.OpenRead(path);
}
