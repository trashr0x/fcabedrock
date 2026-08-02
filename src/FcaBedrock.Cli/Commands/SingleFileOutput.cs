using System.Text;
using FcaBedrock.Cli.Publication;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// Delivers a finished canonical document to <c>--out PATH|-</c> for the single-file writing
/// commands (D-122 part 4, D-123 point 7).
/// <para>
/// <b>It owns every rendering decision these commands make</b>, so a handler never calls
/// <see cref="DiagnosticRenderer"/> and never touches the filesystem: the canonical bytes come
/// from <see cref="SpecWriter"/> and nothing else, and every mutation goes through
/// <see cref="PublicationTransaction"/> and nothing else.
/// </para>
/// <para>
/// <b>The two destinations are deliberately separate sequences.</b> Stdout keeps the
/// established check-cancellation, render, then write shape. <b>For a file target no diagnostic
/// becomes externally visible until the run reaches either a non-cancelled reportable failure or
/// a successful commit</b> — otherwise a run that produced a Warning and was then cancelled would
/// exit 3 with a non-empty stderr, and a formally silent cancellation would carry visible partial
/// output. Past the commit point no cancellation check runs at all: a signal arriving then must
/// not report a published file as cancelled.
/// </para>
/// </summary>
internal static class SingleFileOutput
{
    /// <summary>
    /// The canonical bytes' encoder. Output encoding only — all <em>decoding</em> of authored
    /// text belongs to <see cref="SpecTextDecoding"/> (P-5).
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Delivers <paramref name="document"/> under <paramref name="invocation"/>'s <c>--out</c>.
    /// <para>
    /// <paramref name="document"/> is null exactly when the command's library call produced none,
    /// which by the <c>Diagnosed</c> contract means <paramref name="diagnostics"/> carries an
    /// Error or Fatal — so it is only ever dereferenced past the gate that returns on one.
    /// </para>
    /// </summary>
    /// <returns>0 on a delivered document, 1 on any Error/Fatal or host failure.</returns>
    public static async Task<int> DeliverAsync(
        CliEnvironment environment,
        CommandInvocation invocation,
        SpecDocument? document,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var spelling = invocation.Value("--out")!;

        if (string.Equals(spelling, "-", StringComparison.Ordinal))
        {
            return ToStdout(environment, document, diagnostics, cancellation);
        }

        // ---- the file-target sequence: nothing is rendered above this line ----------------

        if (DiagnosticRenderer.HasErrors(diagnostics))
        {
            // A cancelled run reports nothing at all, so the check precedes the render. No
            // transaction begins: a document that failed is never published, and the location
            // is not even inspected.
            cancellation.ThrowIfCancellationRequested();
            DiagnosticRenderer.Write(environment.Error, diagnostics);
            return 1;
        }

        return await PublishAsync(environment, invocation, spelling, document!, diagnostics, cancellation)
            .ConfigureAwait(false);
    }

    // The established report-command shape (RunPipeline.Complete): observe cancellation, render
    // in library order, then one logical write of the already-finished payload.
    private static int ToStdout(
        CliEnvironment environment,
        SpecDocument? document,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        DiagnosticRenderer.Write(environment.Error, diagnostics);
        if (DiagnosticRenderer.HasErrors(diagnostics))
        {
            return 1;
        }

        environment.Out.Write(SpecWriter.Write(document!));
        return 0;
    }

    private static async Task<int> PublishAsync(
        CliEnvironment environment,
        CommandInvocation invocation,
        string spelling,
        SpecDocument document,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        CancellationToken cancellation)
    {
        var bytes = Utf8NoBom.GetBytes(SpecWriter.Write(document));

        // Both single-file commands take exactly one operand — probe's DATA, migrate's BED — and
        // it is the one file the run read, so it is what the identity-collision check compares the
        // target against (D-122 part 4). The parser requires it, so it is present.
        var input = invocation.Operand(0)!;

        List<PublicationInput> inputs;
        try
        {
            inputs = [new PublicationInput(input, Path.GetFullPath(input))];
        }
        catch (Exception exception) when (FailureFamily.IsPublicationFailure(exception))
        {
            return RunPipeline.HostFailure(
                environment, diagnostics, PublicationMessages.RecordFailed(spelling), cancellation);
        }

        var preparation = PublicationTransaction.PreflightSingle(
            environment.PublicationFiles,

            // A factory, not an instance: identity is memoized per path, so the collision check
            // must run on a service acquired after any recovery has finished moving files.
            FileIdentity.CreateDefault,
            spelling,
            inputs,
            invocation.Has("--force"),

            // The exact host token reaches residue classification, validated recovery, and every
            // mutation boundary inside it.
            cancellation);

        if (preparation is not PublicationReady(var transaction))
        {
            return RunPipeline.HostFailure(
                environment, diagnostics, ((PublicationRefused)preparation).Message, cancellation);
        }

        try
        {
            if (transaction.Begin() is { } begun)
            {
                transaction.Rollback();
                return RunPipeline.HostFailure(environment, diagnostics, begun.Message, cancellation);
            }

            if (await transaction.StageAsync(PublicationTargetKind.Single, Writer(bytes, cancellation))
                .ConfigureAwait(false) is { } staged)
            {
                transaction.Rollback();
                return RunPipeline.HostFailure(environment, diagnostics, staged.Message, cancellation);
            }

            if (transaction.Seal(cancellation) is { } sealing)
            {
                transaction.Rollback();
                return RunPipeline.HostFailure(environment, diagnostics, sealing.Message, cancellation);
            }

            // Commit observes the host token before every pre-commit transition of its own; this
            // is simply the last check before the first one.
            cancellation.ThrowIfCancellationRequested();

            if (transaction.Commit(cancellation) is { } committing)
            {
                transaction.Rollback();
                return RunPipeline.HostFailure(environment, diagnostics, committing.Message, cancellation);
            }
        }
        catch
        {
            // Cancellation and genuine faults alike: undo everything staged before the commit
            // point, then let the host classify (exit 3 or exit 4). Past the commit point the
            // file is public and is never unwound.
            if (!transaction.Committed)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (PublicationFaultException)
                {
                    // A contract defect met while undoing does not get to REPLACE the outcome
                    // that sent the run here: letting it escape from inside this handler would
                    // silently promote an exact-host cancellation from exit 3 to exit 4.
                }
            }

            throw;
        }

        // Committed. Only now do Info and Warning reach stderr, once and in library order, and no
        // cancellation check runs after this point. Stdout stays empty on a file target.
        DiagnosticRenderer.Write(environment.Error, diagnostics);
        return 0;
    }

    // The output side is tagged at its origin so a stage write failure is reported as the output
    // failure it is; anything else — a cancellation, a contract defect — is left untouched for
    // the caller to classify.
    private static Func<Stream, Task> Writer(byte[] bytes, CancellationToken cancellation) => async stream =>
    {
        try
        {
            await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PublicationStreamException(exception);
        }
    };
}
