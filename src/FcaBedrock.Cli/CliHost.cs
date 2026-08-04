namespace FcaBedrock.Cli;

/// <summary>
/// The argv boundary and the owner of the process-exit contract (D-122 part 2). Every
/// argv-level test drives this method; <see cref="Program"/> adds only real-world wiring,
/// so nothing testable lives outside it.
/// <para>
/// Exit codes: <b>0</b> success — warnings and info never move it off 0; <b>1</b> any
/// Error/Fatal diagnostic or an ordinary host/runtime/input/output failure; <b>2</b> usage;
/// <b>3</b> cooperative cancellation, with no diagnostic; <b>4</b> an unexpected internal
/// fault.
/// </para>
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// The one message an unexpected internal fault produces. Deliberately constant: an
    /// exception's own text can carry the type, a stack, an internal path, or
    /// culture-dependent OS wording, none of which belongs on a user's stderr — and a
    /// fixed string keeps exit 4 byte-lockable.
    /// </summary>
    internal const string UnexpectedFaultMessage = "an unexpected internal error occurred.";

    /// <summary>The message for a failure to write the primary result — an ordinary environment failure.</summary>
    internal const string OutputFailureMessage = "cannot write to standard output.";

    /// <summary>Runs one invocation and returns its process exit code.</summary>
    /// <param name="argv">The ordinary application arguments — <b>not</b> the audit argv, which carries argv[0].</param>
    /// <param name="environment">The injected world.</param>
    public static async Task<int> RunAsync(string[] argv, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentNullException.ThrowIfNull(environment);

        try
        {
            switch (CommandLineParser.Parse(argv))
            {
                case HelpRequested:
                    return Write(environment.Out, UsageText.Help) ? 0 : OutputFailure(environment);

                case VersionRequested:
                    return Write(environment.Out, environment.ToolVersion + "\n") ? 0 : OutputFailure(environment);

                case UsageFailure failure:
                    // Both writes are attempted, then judged together: a usage error the
                    // user never receives is not a usage outcome — the run failed to deliver
                    // required output, which is an ordinary host failure (exit 1). Nothing is
                    // re-reported through the channel that just failed.
                    var reason = Write(environment.Error, DiagnosticRenderer.RenderHostError(failure.Message));
                    var usage = Write(
                        environment.Error, failure.Command is { } command ? UsageText.For(command) : UsageText.General);
                    return reason && usage ? 2 : 1;

                case CommandInvocation invocation:
                    return await ExecuteAsync(invocation, environment).ConfigureAwait(false);

                default:
                    throw new InvalidOperationException("Unrecognized parse outcome.");
            }
        }
        catch (OperationCanceledException exception) when (IsHostCancellation(exception, environment))
        {
            // Cooperative cancellation: cleanup has run, no run was committed, and by
            // contract nothing is reported — a cancelled run is not a failed one.
            return 3;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // An output sink that broke — a closed pipe, a released handle. That is an
            // ordinary environment failure, not an internal bug, so it keeps exit 1.
            // Command input failures never reach here: each command classifies its own.
            return OutputFailure(environment);
        }
        catch (Exception)
        {
            // The last line of defence. Exactly one sanitized line, and the exception
            // itself is not rendered, logged, or rethrown in a form a user could see.
            Write(environment.Error, DiagnosticRenderer.RenderHostError(UnexpectedFaultMessage));
            return 4;
        }
    }

    /// <summary>
    /// Completes output for a real process run: flushes the primary sink, and downgrades a
    /// deferred flush failure to the ordinary output-failure exit rather than letting it
    /// escape after another code was already chosen. Redirected output is buffered, so the
    /// flush — not the write — is where a broken pipe usually surfaces.
    /// </summary>
    internal static int Complete(int exitCode, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var result = exitCode;
        try
        {
            output.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Write(error, DiagnosticRenderer.RenderHostError(OutputFailureMessage));
            result = 1;
        }

        try
        {
            error.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Contained, and deliberately not reported back through the channel that just
            // failed. Buffered diagnostics or usage text that never reached the user means
            // required output was not delivered, so a success or usage outcome becomes the
            // ordinary output failure. Cancellation (3) owed stderr nothing, and an
            // unexpected internal fault (4) is the more specific classification of why the
            // run failed; both stand.
            if (result is 0 or 2)
            {
                result = 1;
            }
        }

        return result;
    }

    // Exit 3 means THIS host's cooperative token, not any cancellation that happens to be in
    // flight: an unrelated cancelled operation racing with a real signal must not be reported
    // as a clean user cancellation. It stays an unexpected fault.
    private static bool IsHostCancellation(OperationCanceledException exception, CliEnvironment environment) =>
        environment.Signals.Token.IsCancellationRequested
        && exception.CancellationToken == environment.Signals.Token;

    private static int OutputFailure(CliEnvironment environment)
    {
        Write(environment.Error, DiagnosticRenderer.RenderHostError(OutputFailureMessage));
        return 1;
    }

    // Writing must never throw out of the host: a failed sink is reported through the exit
    // code, and a failed report is simply lost rather than becoming a runtime crash.
    private static bool Write(TextWriter writer, string text)
    {
        try
        {
            writer.Write(text);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static async Task<int> ExecuteAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        // Cancellation is honoured uniformly at the command boundary, so a signal that
        // arrived during parsing stops every command — including the ones whose own work
        // has no natural cancellation point.
        environment.Signals.Token.ThrowIfCancellationRequested();

        var name = invocation.Command.Name;
        environment.Progress.CommandStarted(name);

        var exitCode = await invocation.Command.Handler(invocation, environment).ConfigureAwait(false);

        environment.Progress.CommandCompleted(name, exitCode);
        return exitCode;
    }
}
