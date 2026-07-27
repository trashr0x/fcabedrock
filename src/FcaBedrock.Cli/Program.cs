using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli;

/// <summary>
/// The real entry point: wiring only, no behaviour. Everything decidable lives behind
/// <see cref="CliHost.RunAsync"/>, which the argv-boundary tests drive directly.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Writers first, so even a failure while wiring the rest has somewhere to report.
        var standardOut = CreateWriter(Console.OpenStandardOutput, Console.IsOutputRedirected, Console.Out);
        var standardError = CreateWriter(Console.OpenStandardError, Console.IsErrorRedirected, Console.Error);
        ISignalSource? signals = null;

        var exitCode = 4;
        try
        {
            signals = PosixSignalSource.Create();

            var environment = new CliEnvironment
            {
                Out = standardOut,
                Error = standardError,
                Clock = SystemClock.Instance,
                Signals = signals,
                ToolVersion = ToolVersion.Current,

                // The COMPLETE process command line, argv[0] included and verbatim
                // (CX-M7P-012). Main's `args` never carries argv[0], so the audit value
                // has to come from here; parsing continues over `args` alone.
                AuditArgv = [.. Environment.GetCommandLineArgs()],
                OpenInput = CliEnvironment.OpenFile,
                PublicationFiles = PublicationFileSystem.Instance,
            };

            exitCode = await CliHost.RunAsync(args, environment).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failure in the wiring above cannot reach CliHost's own handler, so the
            // exit-4 contract is honoured here too — one sanitized line, nothing leaked.
            // The write itself is best-effort for the same reason CliHost's is.
            try
            {
                standardError.Write(DiagnosticRenderer.RenderHostError(CliHost.UnexpectedFaultMessage));
            }
            catch (Exception report) when (report is IOException or ObjectDisposedException)
            {
                // Nowhere left to report; the exit code still carries the outcome.
            }

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

    // Redirected output is pinned to UTF-8 with NO byte-order mark, so a piped or
    // captured stream carries exactly the bytes the renderer produced. An interactive
    // console keeps its own writer, which already speaks the console's encoding.
    //
    // The writer is not disposed: it wraps a standard handle owned by the process, and
    // the finally above flushes it. Line endings never come from here — every rendered
    // string carries its own LF, so output is identical on every platform.
    private static TextWriter CreateWriter(Func<Stream> openStandard, bool redirected, TextWriter console) =>
        redirected
            ? new StreamWriter(openStandard(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = false,
            }
            : console;
}
