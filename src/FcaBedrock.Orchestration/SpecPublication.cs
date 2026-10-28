using System.Collections.Immutable;
using System.Text;
using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration.Publication;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration;

/// <summary>Where and how a single spec file is delivered, and the files the producing operation read.</summary>
public sealed record SpecDelivery
{
    /// <summary>The target file path, exactly as given; never <c>-</c>, which names no file.</summary>
    public required string Target { get; init; }

    /// <summary>Whether an existing distinct target may be replaced.</summary>
    public required bool ReplaceExisting { get; init; }

    /// <summary>
    /// Every file the producing command read, as spelled. Each is an input the target is compared
    /// against by identity, and an operand comes before a chain file, so a collision names the same
    /// input it always has.
    /// </summary>
    public IReadOnlyList<string> Operands { get; init; } = [];

    /// <summary>Every spec file the producing preparation read, in load order: inputs too.</summary>
    public IReadOnlyList<SpecChainFile> Chain { get; init; } = [];
}

/// <summary>
/// The single-file publication, in two steps with a caller step between (D-122 part 4, D-140): the
/// same transaction, record format and recovery routine as the convert's artifacts family,
/// differing only in its one target.
/// </summary>
public static class SpecPublication
{
    /// <summary>
    /// The canonical bytes' encoder. Output encoding only; all <em>decoding</em> of authored text
    /// belongs to <see cref="SpecTextDecoding"/> (EP-5).
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The single-file preflight: the operands' full paths, then residue recovery at the target, the
    /// identity-collision check against <see cref="SpecDelivery.Operands"/> and
    /// <see cref="SpecDelivery.Chain"/>, the directory check, and only then the existing-target
    /// refusal (<see cref="HostErrorKind.TargetOccupied"/> when replacement is not authorized).
    /// Produces no diagnostics. The request's lists are copied at entry.
    /// <para>
    /// A refusal or a thrown exception leaves no reference held. A completed preflight returns a
    /// handle that holds live references; they belong to the caller until the handle is published or
    /// disposed. Cancellation propagates as <paramref name="cancellation"/>'s own exception.
    /// Unobserved: exactly
    /// <see cref="Preflight(SpecDelivery, OrchestrationHost, OperationProgress, CancellationToken)"/>
    /// with no progress.
    /// </para>
    /// </summary>
    public static Result<PreflightedSpecFile, HostError> Preflight(
        SpecDelivery delivery, OrchestrationHost host, CancellationToken cancellation) =>
        Preflight(delivery, host, progress: null, cancellation);

    /// <summary>
    /// The single-file preflight as the other overload performs it, observed by
    /// <paramref name="progress"/> (D-141): it reports <see cref="WorkingStep.PreparingOutput"/> while
    /// it works and clears it when it returns or throws, and the returned handle carries the progress
    /// into its publish. A null progress is exactly the unobserved overload.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="progress"/> is completed; thrown before any path is resolved.
    /// </exception>
    public static Result<PreflightedSpecFile, HostError> Preflight(
        SpecDelivery delivery, OrchestrationHost host, OperationProgress? progress, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(host);
        OperationProgress.ThrowIfCompleted(progress, nameof(progress));

        progress?.Step(WorkingStep.PreparingOutput);
        try
        {
            return PreflightCore(delivery, host, progress, cancellation);
        }
        finally
        {
            progress?.Step(0);
        }
    }

    private static Result<PreflightedSpecFile, HostError> PreflightCore(
        SpecDelivery delivery, OrchestrationHost host, OperationProgress? progress, CancellationToken cancellation)
    {
        var target = delivery.Target;
        var replaceExisting = delivery.ReplaceExisting;
        var operands = delivery.Operands.ToImmutableArray();
        var chain = delivery.Chain.ToImmutableArray();

        // EVERY operand the producing command consumed is a file the run read, so every one is an
        // input the identity-collision check compares the target against (D-122 part 4). The chain
        // follows: its base paths are referrer-relative and already resolved, so they are passed
        // through verbatim and never re-derived against the process working directory.
        List<PublicationInput> inputs;
        try
        {
            inputs = new List<PublicationInput>(operands.Length + chain.Length);
            foreach (var operand in operands)
            {
                inputs.Add(new PublicationInput(operand, Path.GetFullPath(operand)));
            }

            foreach (var file in chain)
            {
                inputs.Add(new PublicationInput(file.Spelling, file.FullPath));
            }
        }
        catch (Exception exception) when (FailureFamily.IsPublicationFailure(exception))
        {
            return Result<PreflightedSpecFile, HostError>.Err(PublicationErrors.RecordFailed(target).ToHostError());
        }

        var preparation = PublicationTransaction.PreflightSingle(
            host.PublicationFiles,

            // A factory, not an instance: identity is memoized per path, so the collision check
            // must run on a service acquired after any recovery has finished moving files.
            FileIdentity.CreateDefault,
            target,
            inputs,
            replaceExisting,

            // The exact host token reaches residue classification, validated recovery, and every
            // mutation boundary inside it.
            cancellation);

        if (preparation is not PublicationReady(var transaction))
        {
            return Result<PreflightedSpecFile, HostError>.Err(((PublicationRefused)preparation).Error.ToHostError());
        }

        // The transfer: the handle's constructor assigns fields only, and nothing fallible sits
        // between the transaction's return and the handle's. The progress is one more assignment.
        transaction.Progress = progress;
        return Result<PreflightedSpecFile, HostError>.Ok(new PreflightedSpecFile(transaction));
    }

    /// <summary>
    /// Writes the canonical bytes of <paramref name="document"/> from <see cref="SpecWriter"/> through
    /// the handle's transaction: begin, stage, seal, commit; rollback before the commit point on
    /// every failure. Spends the handle and releases its references before it returns or throws.
    /// Produces no diagnostics.
    /// <para>
    /// Completed: the committed file, the same instance as <see cref="PreflightedPublication.Attempt"/>'s
    /// committed record. Host failure: the step's classified error, after its rollback.
    /// Cancellation before the commit point and an unexpected fault propagate unchanged, a tagged
    /// publication fault included, after the same rollback; a fault met while rolling back on that
    /// path never replaces the exception that caused it. Past the commit point no cancellation is
    /// observed: a committed file stands.
    /// </para>
    /// <para>
    /// When the handle carries an <see cref="OperationProgress"/>, the publish reports
    /// <see cref="WorkingStep.Staging"/> after its cancellation check before beginning, then
    /// <see cref="WorkingStep.Sealing"/>, <see cref="WorkingStep.Committing"/> and
    /// <see cref="WorkingStep.Cleaning"/>, and clears the step on every exit after the record is built
    /// and the references are released (D-141). A cancellation observed before beginning reports no
    /// step. The attempt record, never the progress, holds the facts.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The progress <paramref name="handle"/> carries is completed; thrown before any bytes are written
    /// and before the handle is spent: it stays unspent and disposable, its record stays
    /// <see cref="PublicationAttemptState.NotBegun"/>, and every later publish of it refuses again.
    /// </exception>
    public static async Task<Result<Published, HostError>> PublishAsync(
        SpecDocument document, PreflightedSpecFile handle, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(handle);

        // Read through the handle without spending it: the single-file family has no run to carry it.
        var progress = handle.Progress;
        OperationProgress.ThrowIfCompleted(progress, nameof(handle));

        var bytes = Utf8NoBom.GetBytes(SpecWriter.Write(document));
        var transaction = handle.Spend();
        try
        {
            try
            {
                return await PublishCoreAsync(transaction, handle, bytes, cancellation).ConfigureAwait(false);
            }
            catch
            {
                // Cancellation and genuine faults alike: undo everything staged before the commit
                // point, then let the host classify. Past the commit point the file is public and is
                // never unwound.
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
                        // silently turn an exact-host cancellation into an unexpected fault.
                    }
                }

                throw;
            }
        }
        finally
        {
            handle.Finish();

            // After the record is built and the references are released: a step never outlives the
            // call that reported it.
            progress?.Step(0);
        }
    }

    private static async Task<Result<Published, HostError>> PublishCoreAsync(
        PublicationTransaction transaction, PreflightedSpecFile handle, byte[] bytes, CancellationToken cancellation)
    {
        var progress = transaction.Progress;

        // A signal that arrived before this publish stops it here, before Begin creates anything:
        // the handle is spent, the record stays NotBegun and no rollback is owed. This stays inside
        // the caller's try, so its finally still builds the record and releases every reference.
        cancellation.ThrowIfCancellationRequested();

        progress?.Step(WorkingStep.Staging);
        if (transaction.Begin() is { } begun)
        {
            transaction.Rollback();
            return Failed(begun);
        }

        if (await transaction.StageAsync(PublicationTargetKind.Single, Writer(bytes, cancellation))
            .ConfigureAwait(false) is { } staged)
        {
            transaction.Rollback();
            return Failed(staged);
        }

        progress?.Step(WorkingStep.Sealing);
        if (transaction.Seal(cancellation) is { } sealing)
        {
            transaction.Rollback();
            return Failed(sealing);
        }

        // Commit observes the host token before every pre-commit transition of its own; this is
        // simply the last check before the first one.
        cancellation.ThrowIfCancellationRequested();

        progress?.Step(WorkingStep.Committing);
        if (transaction.Commit(cancellation) is { } committing)
        {
            transaction.Rollback();
            return Failed(committing);
        }

        // Committed: the record is built right here, so the outcome's value is its committed record.
        return Result<Published, HostError>.Ok(handle.Record().Committed!);
    }

    private static Result<Published, HostError> Failed(PublicationFailure failure) =>
        Result<Published, HostError>.Err(failure.Error.ToHostError());

    // The output side is tagged at its origin so a stage write failure is reported as the output
    // failure it is; anything else (a cancellation, a contract defect) is left untouched for
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
