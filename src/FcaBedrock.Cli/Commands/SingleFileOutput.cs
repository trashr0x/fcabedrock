using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// Delivers a finished canonical document to <c>--out PATH|-</c> for the single-file writing
/// commands (D-122 part 4, D-123 point 7).
/// <para>
/// <b>It owns every rendering decision these commands make</b>, so a handler never calls
/// <see cref="DiagnosticRenderer"/> and never touches the filesystem: the canonical bytes come
/// from <see cref="SpecWriter"/> and nothing else, and every mutation goes through
/// <see cref="SpecPublication"/> and nothing else.
/// </para>
/// <para>
/// <b>The two destinations are deliberately separate sequences.</b> Stdout keeps the
/// established check-cancellation, render, then write shape. <b>For a file target no diagnostic
/// becomes externally visible until the run reaches either a non-cancelled reportable failure or
/// a successful commit</b>; otherwise a run that produced a Warning and was then cancelled would
/// exit 3 with a non-empty stderr, and a formally silent cancellation would carry visible partial
/// output. Past the commit point no cancellation check runs at all: a signal arriving then must
/// not report a published file as cancelled.
/// </para>
/// <para>
/// A caller that owes stdout a report after its file commits supplies it as the committed
/// payload; <c>null</c> means the file target stays stdout-empty. It is written here, after the
/// commit and after the diagnostics, so "reported if and only if committed" is one straight-line
/// block rather than a property each handler has to preserve (D-123 point 14).
/// </para>
/// </summary>
internal static class SingleFileOutput
{
    /// <summary>
    /// Delivers <paramref name="document"/> under <paramref name="invocation"/>'s <c>--out</c>.
    /// <para>
    /// <paramref name="document"/> is null exactly when the command's library call produced none,
    /// which by the <c>Diagnosed</c> contract means <paramref name="diagnostics"/> carries an
    /// Error or Fatal, so it is only ever dereferenced past the check that returns on one.
    /// </para>
    /// </summary>
    /// <returns>0 on a delivered document, 1 on any Error/Fatal or host failure.</returns>
    public static Task<int> DeliverAsync(
        CliEnvironment environment,
        CommandInvocation invocation,
        SpecDocument? document,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        IReadOnlyList<SpecChainFile> chain,
        string? committedReport,
        CancellationToken cancellation) =>
        DeliverAsync(
            environment, invocation, document, diagnostics, chain, committedReport,
            verifyBeforeWrite: null, cancellation);

    /// <summary>
    /// Delivers <paramref name="document"/> as the other overload does, and for a file target runs
    /// <paramref name="verifyBeforeWrite"/> after preflight, and any recovery it performs, and before
    /// the run's first write. It answers null to proceed or the code-less refusal to report; a
    /// refusal there leaves nothing to roll back. Stdout never runs it.
    /// </summary>
    /// <returns>0 on a delivered document, 1 on any Error/Fatal or host failure.</returns>
    public static async Task<int> DeliverAsync(
        CliEnvironment environment,
        CommandInvocation invocation,
        SpecDocument? document,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        IReadOnlyList<SpecChainFile> chain,
        string? committedReport,
        Func<string?>? verifyBeforeWrite,
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

        return await PublishAsync(
                environment, invocation, spelling, document!, diagnostics, chain, committedReport,
                verifyBeforeWrite, cancellation)
            .ConfigureAwait(false);
    }

    // The established report-command shape (RunRendering.Complete): observe cancellation, render
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
        IReadOnlyList<SpecChainFile> chain,
        string? committedReport,
        Func<string?>? verifyBeforeWrite,
        CancellationToken cancellation)
    {
        // EVERY operand a single-file command consumed is a file the run read, so every one is an
        // input the identity-collision check compares the target against (D-122 part 4): probe's
        // DATA, migrate's BED, calibrate's and fingerprint's SPEC and DATA alike. The rule is total
        // over the command table rather than a per-command guess, so it cannot be forgotten. The
        // caller adds the composed extends chain it read, whose base paths are referrer-relative and
        // must not be re-derived against the process working directory.
        var preflight = SpecPublication.Preflight(
            new SpecDelivery
            {
                Target = spelling,
                ReplaceExisting = invocation.Has("--force"),
                Operands = invocation.Operands,
                Chain = chain,
            },
            environment.Orchestration,
            environment.OperationProgress,

            // The exact host token reaches residue classification, validated recovery, and every
            // mutation boundary inside it.
            cancellation);

        if (preflight is not OperationCompleted<PreflightedSpecFile> { Value: var handle })
        {
            return RunRendering.HostFailure(
                environment, diagnostics, ((OperationHostFailure<PreflightedSpecFile>)preflight).Error.Message, cancellation);
        }

        // The handle holds live references to every object whose identity authorizes a mutation,
        // so it is released on every exit: success, host failure, cancellation and unexpected fault
        // alike. That is not skippable cleanup: on Windows a surviving reference keeps an
        // already-requested deletion pending (D-125). A publish releases them itself.
        using (handle)
        {
            // The caller's last check of what it is about to write: after preflight and any
            // recovery, before Begin creates the first file of this run.
            if (verifyBeforeWrite?.Invoke() is { } refusal)
            {
                return RunRendering.HostFailure(environment, diagnostics, refusal, cancellation);
            }

            // A failure before the commit point has already been rolled back; cancellation and
            // genuine faults propagate for the host to classify (exit 3 or exit 4).
            var published = await SpecPublication.PublishAsync(document, handle, cancellation).ConfigureAwait(false);
            if (published is not OperationCompleted<Published> committed)
            {
                return RunRendering.HostFailure(
                    environment, diagnostics, ((OperationHostFailure<Published>)published).Error.Message, cancellation);
            }

            // Committed. Only now do Info and Warning reach stderr, once and in library order, and no
            // cancellation check runs after this point. A private cleanup that did not finish adds
            // one code-less warning after them. Stdout stays empty on a file target unless the
            // caller owed it a post-commit report.
            DiagnosticRenderer.Write(environment.Error, diagnostics);
            if (committed.Value.CleanupWarning is { } warning)
            {
                environment.Error.Write(DiagnosticRenderer.RenderHostWarning(warning));
            }

            if (committedReport is not null)
            {
                environment.Out.Write(committedReport);
            }

            return 0;
        }
    }
}
