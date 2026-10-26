using FcaBedrock.Orchestration;

namespace FcaBedrock.Cli;

/// <summary>
/// The real entry point: standard-stream setup and wiring only. Everything else decidable lives
/// behind <see cref="CliHost.RunAsync"/>, which the argv-boundary tests drive directly.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args) => RunAsync(args, SystemConsole.Instance, PosixSignalSource.Create);

    /// <summary>
    /// Sets up the standard writers (<see cref="StandardStreams"/>, D-122 part 3), wires the host
    /// and runs it, then returns through <see cref="CliHost.Complete"/>, which flushes both
    /// writers. The console and the signal registration are parameters so that tests can drive
    /// setup without the test host's own console or a real signal handler.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, IStandardConsole console, Func<ISignalSource> createSignals)
    {
        // Writers first, so even a failure while wiring the rest has somewhere to report. A
        // setup failure is classified as CliHost classifies a failure during the run (D-122
        // parts 2 and 3): IOException or ObjectDisposedException is the ordinary output
        // failure, exit 1, and anything else is an unexpected fault, exit 4. Either stops here,
        // before any signal, command, input or publication. The report can only reach a stderr
        // that setup already built; it never goes to stdout.
        var streams = new StandardStreams();
        try
        {
            streams.Open(console);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Report(streams.Error, CliHost.OutputFailureMessage);
            return CliHost.Complete(1, streams.Output, streams.Error);
        }
        catch (Exception)
        {
            Report(streams.Error, CliHost.UnexpectedFaultMessage);
            return CliHost.Complete(4, streams.Output, streams.Error);
        }

        var standardOut = streams.Output;
        var standardError = streams.Error;
        ISignalSource? signals = null;

        var exitCode = 4;
        try
        {
            signals = createSignals();

            var environment = new CliEnvironment
            {
                Out = standardOut,
                Error = standardError,
                Clock = SystemClock.Instance,
                Signals = signals,
                ToolVersion = ToolVersion.Current,

                // The COMPLETE process command line, argv[0] included and verbatim.
                // Main's `args` never carries argv[0], so the audit value
                // has to come from here; parsing continues over `args` alone.
                AuditArgv = [.. Environment.GetCommandLineArgs()],
                Orchestration = new OrchestrationHost(CliEnvironment.OpenFile),
            };

            exitCode = await CliHost.RunAsync(args, environment).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failure in the wiring above cannot reach CliHost's own handler, so the
            // exit-4 contract is honoured here too: one sanitized line, nothing leaked.
            Report(standardError, CliHost.UnexpectedFaultMessage);
            exitCode = 4;
        }
        finally
        {
            signals?.Dispose();
        }

        // Redirected output is buffered, so a broken pipe usually surfaces here rather than
        // at the write. Flushing through the host keeps that inside the 0–4 contract instead
        // of letting an exception escape Main after a code was already chosen.
        return CliHost.Complete(exitCode, standardOut, standardError);
    }

    // Best-effort, like CliHost's own writes, and wider: whatever the report itself throws must
    // not escape Main, because the exit code already carries the outcome.
    private static void Report(TextWriter error, string message)
    {
        try
        {
            error.Write(DiagnosticRenderer.RenderHostError(message));
        }
        catch (Exception)
        {
            // Nowhere left to report; the exit code still carries the outcome.
        }
    }
}
