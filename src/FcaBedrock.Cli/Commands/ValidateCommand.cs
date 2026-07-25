using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>validate SPEC [DATA]</c> (D-122 part 10).
/// <para>
/// Without DATA the existing no-schema resolution applies verbatim. With DATA the source
/// schema — the header, or the first record when headerless — is acquired and the spec is
/// resolved against it, so bindings by header name are actually checked.
/// </para>
/// <para>
/// <b>Validate is schema validation, not a dry run of conversion.</b> It reads no data
/// rows, and it does not plan, calibrate, freeze, verify stored fingerprints, hash inputs,
/// compute fingerprints, emit, publish, or mutate anything. A file whose schema is valid
/// but whose later rows are malformed validates successfully — that is the boundary, not
/// an oversight.
/// </para>
/// </summary>
internal static class ValidateCommand
{
    /// <summary>Runs the command; 0 when no Error/Fatal diagnostic was produced, otherwise 1.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var specPath = invocation.Operand(0)!;
        var dataPath = invocation.Operand(1);
        var host = new FileSpecTextSource(environment.OpenInput, FileIdentity.CreateDefault());
        var diagnostics = new List<BedrockDiagnostic>();

        string rootKey;
        string toml;
        try
        {
            rootKey = host.RegisterRoot(specPath);
            toml = host.ReadText(specPath);
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            // A missing or unreadable operand is an ordinary host failure, not a pipeline
            // condition: code-less, exit 1, and the registry does not grow for it
            // (D-122 part 2). The message names the operand and nothing else — no
            // exception type, no localized OS text, no internal path.
            return HostFailure(environment, diagnostics, $"cannot read the spec file '{specPath}'.");
        }

        // Phase boundaries observe the host token, so a signal delivered after dispatch stops
        // this command too. Without DATA nothing else here is cancellable — the read, parse,
        // compose and resolve stages are synchronous — so the first signal would otherwise be
        // suppressed and then ignored, and the run would report success after the user
        // cancelled it.
        var cancellation = environment.Signals.Token;
        cancellation.ThrowIfCancellationRequested();

        var read = SpecReader.Read(toml, rootKey);
        diagnostics.AddRange(read.Diagnostics);
        if (!read.TryGetValue(out var document))
        {
            return Finish(environment, diagnostics, cancellation);
        }

        cancellation.ThrowIfCancellationRequested();
        var composed = SpecComposer.Compose(document, rootKey, host);
        diagnostics.AddRange(composed.Diagnostics);
        if (!composed.TryGetValue(out var effective))
        {
            // A missing or unreadable extends base is a phase-owned condition
            // (SpecExtendsNotFound), so it keeps its registry code — unlike the root
            // operand above, which the CLI owns.
            return Finish(environment, diagnostics, cancellation);
        }

        SourceSchema? schema = null;
        if (dataPath is not null)
        {
            var settings = SpecResolver.ResolveReadSettings(effective);
            if (!settings.TryGetValue(out var readSettings))
            {
                diagnostics.AddRange(settings.Diagnostics);
                return Finish(environment, diagnostics, cancellation);
            }

            // On success this stage reports nothing at all: it fails its result on any
            // Error/Fatal and raises no warning. Its conditions are re-evaluated by the
            // full Resolve below through the same shared helpers (D-098, "no condition
            // gains a second owner"), so rendering them here could only duplicate them.
            var session = Open(readSettings, dataPath, environment);
            try
            {
                schema = await session.GetSchemaAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsInputFailure(exception))
            {
                return HostFailure(environment, diagnostics, $"cannot read the data file '{dataPath}'.");
            }
        }

        cancellation.ThrowIfCancellationRequested();
        var resolved = SpecResolver.Resolve(effective, schema);
        diagnostics.AddRange(resolved.Diagnostics);
        return Finish(environment, diagnostics, cancellation);
    }

    // The ONLY source call validate makes. The session is never bound and its record
    // stream is never enumerated, so no row is read: each row-reading path opens the
    // stream factory again, and this command opens it exactly once.
    private static ISourceSession Open(SourceReadSettings settings, string dataPath, CliEnvironment environment)
    {
        Stream OpenStream() => environment.OpenInput(dataPath);
        return settings.Shape == SourceShape.Triple
            ? new TripleCsvSession(OpenStream, settings)
            : new WideCsvSession(OpenStream, settings);
    }

    // Expected input failures only. OperationCanceledException is deliberately absent —
    // cancellation is exit 3, not an input error — and so are argument/state errors from
    // the session constructors, which are programmer errors the host reports as exit 4.
    // InvalidDataException (a non-UTF-8 byte-order mark) derives from SystemException rather
    // than IOException, so it is named; DecoderFallbackException (malformed UTF-8) arrives
    // as an ArgumentException.
    private static bool IsInputFailure(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or SourceReadException
            or ArgumentException
            or NotSupportedException;

    private static int HostFailure(CliEnvironment environment, List<BedrockDiagnostic> diagnostics, string message)
    {
        DiagnosticRenderer.Write(environment.Error, diagnostics);
        environment.Error.Write(DiagnosticRenderer.RenderHostError(message));
        return 1;
    }

    private static int Finish(
        CliEnvironment environment, List<BedrockDiagnostic> diagnostics, CancellationToken cancellation)
    {
        // The last boundary before anything is rendered: a cancelled run reports nothing at
        // all, so the check has to precede the write rather than follow it.
        cancellation.ThrowIfCancellationRequested();

        // Library order, verbatim: no sorting, grouping, deduplication, or rewording.
        DiagnosticRenderer.Write(environment.Error, diagnostics);
        return DiagnosticRenderer.HasErrors(diagnostics) ? 1 : 0;
    }
}
