using System.Collections.Immutable;
using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Orchestration.Publication;
using FcaBedrock.Spec.Manifest;

namespace FcaBedrock.Orchestration;

/// <summary>Where a conversion publishes and what it writes.</summary>
public sealed record ConvertOutput
{
    /// <summary>The output base as the host spelled it; each artifact is the base plus its extension.</summary>
    public required string Base { get; init; }

    /// <summary>Whether the <c>.cxt</c> context is written.</summary>
    public required bool WritesCxt { get; init; }

    /// <summary>Whether the <c>.dat</c> context is written. At least one of the two contexts must be.</summary>
    public required bool WritesDat { get; init; }

    /// <summary>Whether the run manifest is written, last, as the run's public marker.</summary>
    public required bool WritesManifest { get; init; }

    /// <summary>Whether existing distinct targets may be replaced, and an earlier manifest removed.</summary>
    public required bool ReplaceExisting { get; init; }
}

/// <summary>The host facts a run manifest records (§15).</summary>
public sealed record ManifestHostFacts
{
    /// <summary>The <c>tool_version</c> string.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>The audit command line, recorded verbatim; copied when the publish starts.</summary>
    public required IReadOnlyList<string> CommandLine { get; init; }

    /// <summary>The clock, read once, when the manifest is composed; the instant is truncated to whole UTC seconds.</summary>
    public required Func<DateTimeOffset> UtcNow { get; init; }
}

/// <summary>
/// The plan and output settings the writers use, the native ones or the <c>--v2-compat</c> ones
/// planned from the same calibrated state (D-011, D-044, D-077), bound to the run they were planned
/// from.
/// </summary>
public sealed class EffectiveConversion
{
    internal EffectiveConversion(PreparedFileRun run, ConversionPlan plan, OutputSettings settings)
    {
        Run = run;
        Plan = plan;
        Settings = settings;
    }

    /// <summary>The run this conversion was planned from.</summary>
    public PreparedFileRun Run { get; }

    /// <summary>The plan the writers emit.</summary>
    internal ConversionPlan Plan { get; }

    /// <summary>The output settings the writers and the effective fingerprints use.</summary>
    internal OutputSettings Settings { get; }
}

/// <summary>
/// The convert operation in two steps, preflight then publish (D-122 parts 4 to 7, D-140).
/// <para>
/// The native side that verifies the spec and the effective side that produces the bytes stay
/// exactly paired: <c>--v2-compat</c> plans the <em>same</em> calibrated state once more
/// (D-044/D-077). <b>Nothing becomes public until the whole run is known good.</b> Artifacts are
/// staged and hashed, the diagnostics become authoritative when the replay session is disposed,
/// the input digest is re-checked, and only a run with no Error or Fatal reaches the commit, where
/// the manifest publishes last as the run's public marker.
/// </para>
/// </summary>
public static class ConvertPublication
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The plan and settings the writers use. Native: the run's own plan. Compatible: one extra plan
    /// from the same calibrated state, which on success adds no diagnostic, because the label style
    /// and the operator presentation reach only rendered names (D-044, §8) and every rendered-name
    /// defect is an Error that would have failed the plan; a failing one found a defect the native
    /// labels do not have, and that is the run's diagnostic failure. <paramref name="diagnostics"/>
    /// is the run's complete prior list, and the returned list continues it.
    /// </summary>
    public static OperationOutcome<EffectiveConversion> PlanConversion(
        PreparedFileRun run, bool v2Compatible, IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var collected = new List<BedrockDiagnostic>(diagnostics);
        var settings = v2Compatible ? run.Output.ToV2Compat(run.Shape) : run.Output;

        var plan = run.Plan;
        if (v2Compatible)
        {
            // The v2 settings carry bin_label_unicode = false, so the plan and the manifest's .cxt
            // fingerprint both describe the ASCII names written.
            var planned = run.PlanEffective(LabelStyle.V2Compat, settings);
            if (!planned.TryGetValue(out var effective))
            {
                collected.AddRange(planned.Diagnostics);
                return new OperationDiagnosticFailure<EffectiveConversion>(collected);
            }

            plan = effective;
        }

        return new OperationCompleted<EffectiveConversion>(new EffectiveConversion(run, plan, settings), collected);
    }

    /// <summary>
    /// The complete artifacts preflight: identity collisions, residue, existing targets. The
    /// protected inputs are the files <paramref name="run"/> read: DATA, then every spec chain file.
    /// The handle is bound to <paramref name="run"/>. Produces no diagnostics; DATA's full path is
    /// resolved first, and a failure there is <see cref="HostErrorKind.DataUnreadable"/>.
    /// <para>
    /// A refusal or a thrown exception leaves no reference held. A completed preflight returns a
    /// handle that holds live references; they belong to the caller until the handle is published or
    /// disposed. Cancellation propagates as <paramref name="cancellation"/>'s own exception.
    /// </para>
    /// <para>
    /// When <paramref name="run"/> carries an <see cref="OperationProgress"/>, the preflight reports
    /// <see cref="WorkingStep.PreparingOutput"/> while it works and clears it when it returns or throws,
    /// and the returned handle carries the progress into its publish (D-141).
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="output"/> selects neither context, or the progress <paramref name="run"/>
    /// carries is completed; thrown before any path is resolved.
    /// </exception>
    public static OperationOutcome<PreflightedConvertOutput> Preflight(
        ConvertOutput output, PreparedFileRun run, OrchestrationHost host, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(host);

        var baseSpelling = output.Base;
        var writesCxt = output.WritesCxt;
        var writesDat = output.WritesDat;
        var writesManifest = output.WritesManifest;
        var replaceExisting = output.ReplaceExisting;
        if (!writesCxt && !writesDat)
        {
            throw new ArgumentException("A conversion writes at least one of the two contexts.", nameof(output));
        }

        var progress = run.Input.Progress;
        OperationProgress.ThrowIfCompleted(progress, nameof(run));
        progress?.Step(WorkingStep.PreparingOutput);
        try
        {
            return PreflightCore(baseSpelling, writesCxt, writesDat, writesManifest, replaceExisting, run, host, cancellation);
        }
        finally
        {
            progress?.Step(0);
        }
    }

    private static OperationOutcome<PreflightedConvertOutput> PreflightCore(
        string baseSpelling,
        bool writesCxt,
        bool writesDat,
        bool writesManifest,
        bool replaceExisting,
        PreparedFileRun run,
        OrchestrationHost host,
        CancellationToken cancellation)
    {
        var kinds = new List<PublicationTargetKind>();
        if (writesCxt)
        {
            kinds.Add(PublicationTargetKind.Cxt);
        }

        if (writesDat)
        {
            kinds.Add(PublicationTargetKind.Dat);
        }

        if (writesManifest)
        {
            kinds.Add(PublicationTargetKind.Manifest);
        }

        List<PublicationInput> inputs;
        try
        {
            inputs = [new PublicationInput(run.DataPath, Path.GetFullPath(run.DataPath))];
        }
        catch (Exception exception) when (FailureFamily.IsPublicationFailure(exception))
        {
            return new OperationHostFailure<PreflightedConvertOutput>(
                RunPipeline.DataUnreadable(run.DataPath, RunPipeline.DataReadMessage(run.DataPath)),
                OutcomeLists.NoDiagnostics);
        }

        foreach (var file in run.SpecChain)
        {
            inputs.Add(new PublicationInput(file.Spelling, file.FullPath));
        }

        // The complete preflight (identity collisions, residue, existing targets) finishes
        // before any record, stage, or backup can exist.
        var preparation = PublicationTransaction.Preflight(
            host.PublicationFiles,

            // A factory, not an instance: identity is memoized per path, so the collision check
            // must run on a service acquired after any recovery has finished moving files.
            FileIdentity.CreateDefault,
            baseSpelling,
            kinds,
            inputs,
            replaceExisting,

            // The exact host token reaches residue classification, validated recovery, and every
            // mutation boundary inside it: a signal must stop the run before it begins a new
            // transaction, not merely before the first write.
            cancellation);

        if (preparation is not PublicationReady(var transaction))
        {
            return new OperationHostFailure<PreflightedConvertOutput>(
                ((PublicationRefused)preparation).Error.ToHostError(), OutcomeLists.NoDiagnostics);
        }

        // The transfer: the handle's constructor assigns fields only, and nothing fallible sits
        // between the transaction's return and the handle's. The progress is one more assignment.
        transaction.Progress = run.Input.Progress;
        return new OperationCompleted<PreflightedConvertOutput>(
            new PreflightedConvertOutput(transaction, run, writesCxt, writesDat, writesManifest),
            OutcomeLists.NoDiagnostics);
    }

    /// <summary>
    /// Begins, stages each selected artifact inside one replay session, checks stage failures,
    /// input stability, Errors and the digest, composes and stages the manifest, seals and commits;
    /// rolls back on every failure before the commit point. Spends the handle and releases its
    /// references before it returns or throws. <paramref name="diagnostics"/> is the run's complete
    /// prior list, and the returned list continues it with the emit diagnostics.
    /// <para>
    /// Completed: the committed files, the same instance as <see cref="PreflightedPublication.Attempt"/>'s
    /// committed record. Diagnostic failure: an Error or Fatal anywhere in the final list, the replay
    /// session's disposal aggregates included, after the rollback. Host failure: the step's classified
    /// error, after its rollback. Cancellation and unexpected faults propagate unchanged after the
    /// same rollback; a fault met while rolling back on that path never replaces the exception that
    /// caused it. Past the commit point no cancellation is observed: a committed run stands.
    /// </para>
    /// <para>
    /// When the run carries an <see cref="OperationProgress"/>, the publish reports
    /// <see cref="WorkingStep.Staging"/> after its cancellation check before beginning, each writer pass
    /// (<see cref="PassKind.CxtNames"/>, <see cref="PassKind.CxtRows"/>, <see cref="PassKind.Dat"/>) with
    /// its objects and with <see cref="WorkingStep.Grouping"/> once its input closed when the plan
    /// groups, then <see cref="WorkingStep.Sealing"/>, <see cref="WorkingStep.Committing"/> and
    /// <see cref="WorkingStep.Cleaning"/>, and clears the step on every exit after the record is built
    /// and the references are released (D-141). A cancellation observed before beginning reports no
    /// step. The attempt record, never the progress, holds the facts.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="handle"/> was not preflighted for <paramref name="conversion"/>'s run (the
    /// parameter is <paramref name="handle"/>); or the progress <paramref name="conversion"/>'s run
    /// carries is completed (the parameter is <paramref name="conversion"/>). Both are thrown before any
    /// mutation and before the handle is spent: it stays unspent and disposable, its record stays
    /// <see cref="PublicationAttemptState.NotBegun"/>, and a completed progress refuses every later
    /// publish of it again.
    /// </exception>
    public static async Task<OperationOutcome<Published>> PublishAsync(
        EffectiveConversion conversion,
        PreflightedConvertOutput handle,
        ManifestHostFacts manifest,
        IReadOnlyList<BedrockDiagnostic> diagnostics,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // A run is one operation's state (D-098), so reference identity is the whole association.
        if (!ReferenceEquals(handle.Run, conversion.Run))
        {
            throw new ArgumentException("The handle was preflighted for another run.", nameof(handle));
        }

        // The conversion's run carries the progress, and the association above proves it is the
        // handle's run, so the refusal names the conversion.
        var progress = conversion.Run.Input.Progress;
        OperationProgress.ThrowIfCompleted(progress, nameof(conversion));

        var facts = new ManifestHostFacts
        {
            ToolVersion = manifest.ToolVersion,
            CommandLine = manifest.CommandLine.ToImmutableArray(),
            UtcNow = manifest.UtcNow,
        };
        var collected = new List<BedrockDiagnostic>(diagnostics);
        var run = conversion.Run;
        var transaction = handle.Spend();
        try
        {
            try
            {
                return await PublishCoreAsync(conversion, handle, transaction, facts, collected, cancellation)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (RunPipeline.IsDataReadFailure(exception) && !transaction.Committed)
            {
                // The data could not be read during a pass. This rollback is deliberately not
                // guarded: a fault met here propagates in place of the read failure, as it always has.
                transaction.Rollback();
                return new OperationHostFailure<Published>(
                    RunPipeline.DataUnreadable(
                        run.DataPath, DataReadExplanation.Message(run.DataPath, exception, run.Input.CurrentPassMark)),
                    collected);
            }
            catch
            {
                // Cancellation and genuine faults alike: undo everything this run staged or committed
                // before the commit point, then let the host classify. Past the commit point the run
                // is public and is never unwound.
                if (!transaction.Committed)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (PublicationFaultException)
                    {
                        // A contract defect met while undoing does not get to REPLACE the outcome that
                        // sent the run here. An exact-host-token cancellation stays a cancellation, and
                        // letting a rollback-time fault escape from inside this handler would silently
                        // turn it into an unexpected fault. The original exception is rethrown below;
                        // where it is itself a fault, the classification is the same either way.
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

    private static async Task<OperationOutcome<Published>> PublishCoreAsync(
        EffectiveConversion conversion,
        PreflightedConvertOutput handle,
        PublicationTransaction transaction,
        ManifestHostFacts facts,
        List<BedrockDiagnostic> diagnostics,
        CancellationToken cancellation)
    {
        var run = conversion.Run;
        var plan = conversion.Plan;
        var settings = conversion.Settings;
        var writesCxt = handle.WritesCxt;
        var writesDat = handle.WritesDat;
        var progress = run.Input.Progress;

        // A signal that arrived during preparation stops the run here: after it, no new
        // transaction begins at all.
        cancellation.ThrowIfCancellationRequested();

        progress?.Step(WorkingStep.Staging);
        if (transaction.Begin() is { } started)
        {
            return Failed(started, diagnostics);
        }

        PublicationFailure? failure;

        // ONE session brackets the whole conversion attempt, whatever was selected: data
        // diagnostics are collected by its first pass only (so writing both contexts never
        // double-counts the conversion the two formats share) while grouping storage failures are
        // intercepted on every real pass and flushed, aggregated, at disposal (D-082/D-105).
        using (var replay = EmitReplay.Begin(sink => run.EmitWith(plan, sink, cancellation), diagnostics))
        {
            // Observed, each replay pass is handed to its writer inside an enumeration scope: the
            // .cxt writer's first request is its names pass and its second its rows pass. Unobserved,
            // the writers receive the replay passes themselves.
            Func<IAsyncEnumerable<EmittedObject>> openCxt = replay.Open;
            if (progress is not null)
            {
                var cxtPasses = 0;
                openCxt = () => ObservedEmission.Over(
                    replay.Open(), run.Input, plan, cxtPasses++ == 0 ? PassKind.CxtNames : PassKind.CxtRows, cancellation);
            }

            failure = writesCxt
                ? await transaction.StageAsync(
                    PublicationTargetKind.Cxt,
                    stage => CxtWriter.WriteAsync(
                        plan,
                        openCxt,
                        settings.Cxt,
                        stage,
                        diagnostics,
                        settings.CxtSizeAdvisoryBytes,
                        cancellation)).ConfigureAwait(false)
                : null;

            if (failure is null && writesDat)
            {
                failure = await transaction.StageAsync(
                    PublicationTargetKind.Dat,
                    stage => DatWriter.WriteAsync(
                        ObservedEmission.Over(replay.Open(), run.Input, plan, PassKind.Dat, cancellation),
                        settings.Dat,
                        stage,
                        cancellation))
                    .ConfigureAwait(false);
            }
        }

        // Only here are the emit diagnostics authoritative (D-105): the session's final
        // cross-pass aggregates land at disposal, so artifact validity is decided after it.
        if (failure is not null)
        {
            transaction.Rollback();
            return Failed(failure, diagnostics);
        }

        if (run.Input.HasMismatch)
        {
            transaction.Rollback();
            return new OperationHostFailure<Published>(RunPipeline.DataChanged(run.DataPath), diagnostics);
        }

        if (HasErrors(diagnostics))
        {
            // Every staged artifact is invalid; nothing is committed and nothing becomes public
            // (D-105's caller-discard rule, realized as a discarded stage). This holds for every
            // collected Error or Fatal, including a storage failure that the session aggregated
            // across passes and added at disposal, and whether or not a pass completed a digest.
            // It precedes the digest check because a halt (an unusable or duplicate object key, a
            // non-contiguous subject) can stop a pass before the end of DATA. A missing digest
            // alone therefore does not show that DATA could not be read. The code-less DATA error
            // is only for an input that cannot be read (D-122 part 2).
            transaction.Rollback();
            return new OperationDiagnosticFailure<Published>(diagnostics);
        }

        if (run.Input.Digest is null)
        {
            // A guard, not an expected path: with no Error or Fatal there was no halt, so every emit
            // pass read to the end of the input and completed a digest. A run without one has no
            // evidence that its input was stable, so it is refused with the code-less DATA error
            // rather than published.
            transaction.Rollback();
            return new OperationHostFailure<Published>(
                RunPipeline.DataUnreadable(run.DataPath, RunPipeline.DataReadMessage(run.DataPath)), diagnostics);
        }

        if (handle.WritesManifest)
        {
            var composed = ConvertManifest.Compose(
                run,
                facts,
                Outputs(transaction, writesCxt, writesDat),
                writesCxt ? FingerprintCalculator.ComputeCxtOutputFingerprint(plan, settings.CxtFingerprint(plan.LabelStyle)) : null,
                writesDat ? FingerprintCalculator.ComputeDatOutputFingerprint(plan, settings.DatFingerprint()) : null);

            var bytes = Utf8NoBom.GetBytes(RunManifestWriter.Write(composed));
            failure = await transaction.StageAsync(
                PublicationTargetKind.Manifest,
                stage => stage.WriteAsync(bytes, 0, bytes.Length, cancellation)).ConfigureAwait(false);

            if (failure is not null)
            {
                transaction.Rollback();
                return Failed(failure, diagnostics);
            }
        }

        // Every stage is now written and flushed. Sealing publishes each target's durable identity
        // evidence and then records that fact, which is what lets a later run tell "a commit
        // rename consumed this stage" from "this stage was never created", and what proves which
        // exact object each target holds before rollback may remove one.
        progress?.Step(WorkingStep.Sealing);
        if (transaction.Seal(cancellation) is { } marked)
        {
            transaction.Rollback();
            return Failed(marked, diagnostics);
        }

        // Commit observes the host token before every pre-commit transition of its own; this is
        // simply the last check before the first one.
        cancellation.ThrowIfCancellationRequested();

        progress?.Step(WorkingStep.Committing);
        if (transaction.Commit(cancellation) is { } committed)
        {
            transaction.Rollback();
            return Failed(committed, diagnostics);
        }

        // Committed: the record is built right here, so the outcome's value is its committed record.
        // No cancellation check runs after this point, because a signal arriving after the commit
        // point must not report a published run as cancelled.
        return new OperationCompleted<Published>(handle.Record().Committed!, diagnostics);
    }

    private static OperationHostFailure<Published> Failed(
        PublicationFailure failure, IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        new(failure.Error.ToHostError(), diagnostics);

    // The validity rule: an Error or Fatal anywhere in the run's list makes every staged artifact invalid.
    private static bool HasErrors(IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return true;
            }
        }

        return false;
    }

    // Canonical format order, cxt before dat (§15), with the verbatim invoked spelling and the
    // hash taken inline while the stage was written.
    private static List<RunOutput> Outputs(PublicationTransaction transaction, bool writesCxt, bool writesDat)
    {
        var outputs = new List<RunOutput>();
        if (writesCxt)
        {
            outputs.Add(new RunOutput(
                RunOutputFormat.Cxt,
                transaction.Spelling(PublicationTargetKind.Cxt),
                transaction.HashOf(PublicationTargetKind.Cxt)));
        }

        if (writesDat)
        {
            outputs.Add(new RunOutput(
                RunOutputFormat.Dat,
                transaction.Spelling(PublicationTargetKind.Dat),
                transaction.HashOf(PublicationTargetKind.Dat)));
        }

        return outputs;
    }
}
