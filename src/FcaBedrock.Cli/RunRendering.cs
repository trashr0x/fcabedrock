using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration;

namespace FcaBedrock.Cli;

/// <summary>
/// The CLI half of the shared preparation path: the mapping of a run command's operands into the
/// coordinator's request, and the rendering of its outcomes, which owns every stderr and stdout
/// byte those commands write (D-122 part 2, D-140). Orchestration returns outcomes and renders
/// nothing; the rules here decide the bytes and the exit code, and the cancellation checks that
/// keep a cancelled run silent.
/// </summary>
internal static class RunRendering
{
    /// <summary>
    /// Prepares the run a <c>SPEC DATA [--temp-dir DIR]</c> invocation names, observing the host's
    /// own cancellation token, so a cancellation it raises is classified as the host's.
    /// </summary>
    public static Task<Result<Diagnosed<PreparedFileRun>, HostFailure>> PrepareAsync(
        CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var request = new FileRunRequest
        {
            SpecPath = invocation.Operand(0)!,
            DataPath = invocation.Operand(1)!,
            TempDirectory = invocation.Value("--temp-dir"),
        };

        return RunPreparation.PrepareAsync(
            request, environment.Orchestration, environment.OperationProgress, environment.Signals.Token);
    }

    /// <summary>
    /// Renders an operation that did not complete: for a host failure, its accumulated diagnostics
    /// and then the one sanitized code-less line; otherwise the diagnostic failure, as the overload
    /// over the inner list renders it. Exit 1.
    /// </summary>
    public static int Fail<T>(
        CliEnvironment environment, Result<Diagnosed<T>, HostFailure> outcome, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (outcome.IsError)
        {
            var failure = outcome.Error;
            return HostFailure(environment, failure.Diagnostics, failure.Error.Message, cancellation);
        }

        return Fail(environment, outcome.Value, cancellation);
    }

    /// <summary>
    /// Renders a diagnostic failure: the list in library order, after the cancellation check that
    /// keeps a cancelled run silent. Exit 1, whatever severities the list holds.
    /// </summary>
    public static int Fail<T>(CliEnvironment environment, Diagnosed<T> outcome, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // A cancelled run reports nothing at all: the same rule the success path follows, so a
        // signal arriving as a phase failed cannot turn a cancellation into a reported failure.
        cancellation.ThrowIfCancellationRequested();

        // Library order, verbatim: no sorting, grouping, deduplication, or rewording.
        DiagnosticRenderer.Write(environment.Error, outcome.Diagnostics);
        return 1;
    }

    /// <summary>Renders <paramref name="diagnostics"/>, then one code-less host error; exit 1.</summary>
    public static int HostFailure(
        CliEnvironment environment,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        string message,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(diagnostics);

        cancellation.ThrowIfCancellationRequested();

        DiagnosticRenderer.Write(environment.Error, diagnostics);
        environment.Error.Write(DiagnosticRenderer.RenderHostError(message));
        return 1;
    }

    /// <summary>
    /// The single exit point of a successful report command: observe cancellation one last time,
    /// render the diagnostics in library order, and write the <b>already complete</b> report in
    /// one logical write, but only when nothing failed. An Error or Fatal produces exit 1 and
    /// no report bytes at all; Warnings and Info leave the report and exit 0 intact.
    /// </summary>
    public static int Complete(
        CliEnvironment environment,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        string report,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(report);

        // Before anything is rendered: a cancelled run reports nothing at all, so this has to
        // precede the writes rather than follow them.
        cancellation.ThrowIfCancellationRequested();

        DiagnosticRenderer.Write(environment.Error, diagnostics);
        if (DiagnosticRenderer.HasErrors(diagnostics))
        {
            return 1;
        }

        environment.Out.Write(report);
        return 0;
    }
}
