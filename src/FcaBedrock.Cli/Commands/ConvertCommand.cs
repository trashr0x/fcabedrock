using System.Text;
using FcaBedrock.Cli.Publication;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Spec.Manifest;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>convert SPEC DATA --out BASE --format cxt|dat|both [--v2-compat] [--no-manifest] [--force]
/// [--temp-dir DIR]</c> (D-122 parts 4–7).
/// <para>
/// The shared preparation runs exactly as it does for the report commands — schema, conditional
/// calibration, one native plan, the three native fingerprints, and the single stored-fingerprint
/// verification — and only then does conversion diverge: with <c>--v2-compat</c> the <em>same</em>
/// calibrated state is planned once more for emission, so the native side that verifies the spec
/// and the effective side that produces the bytes stay exactly paired (D-044/D-077).
/// </para>
/// <para>
/// <b>Nothing becomes public until the whole run is known good.</b> Artifacts are staged and
/// hashed, the diagnostics become authoritative when the replay session is disposed, the input
/// digest is re-checked, and only a run with no Error or Fatal reaches the commit — where the
/// manifest publishes last as the run's public marker. <b>Stdout is exactly empty on every
/// success</b>; diagnostics go to stderr, in library order, once.
/// </para>
/// </summary>
internal static class ConvertCommand
{
    /// <summary>
    /// The code-less host error for an authored <c>[output.cxt] size_advisory_bytes</c> below zero.
    /// <para>
    /// §8 gives the field exactly two readings — a positive threshold, or <c>0</c> to disable —
    /// and the writer rejects a negative one outright. Mapping it to "disabled" would invent a
    /// third reading and hide invalid configuration, so the run refuses it before it creates
    /// anything, and the refusal never becomes an unexpected-fault exit.
    /// </para>
    /// </summary>
    internal const string NegativeSizeAdvisoryMessage =
        "The [output.cxt] size_advisory_bytes value cannot be negative.";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Runs the command; 0 on a committed run, 1 on any Error/Fatal or host failure.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var cancellation = environment.Signals.Token;

        var outcome = await RunPipeline.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome is not PipelinePrepared prepared)
        {
            return RunPipeline.Fail(environment, outcome, cancellation);
        }

        var run = prepared.Run;
        var diagnostics = new List<BedrockDiagnostic>(prepared.Diagnostics);

        // Once, against the ROOT document and the NATIVE fingerprints — never against an
        // effective override value, which is a manifest fact and never a stored one (§14/D-077).
        diagnostics.AddRange(SpecFingerprints.VerifyStored(run.RootDocument, run.Fingerprints, run.RootKey));

        if (run.Output.CxtSizeAdvisoryBytes < 0)
        {
            return RunPipeline.HostFailure(environment, diagnostics, NegativeSizeAdvisoryMessage, cancellation);
        }

        var v2Compat = invocation.Has("--v2-compat");
        var settings = v2Compat ? run.Output.ToV2Compat(run.Shape) : run.Output;

        var plan = run.Plan;
        if (v2Compat)
        {
            // One extra plan from the same calibrated state. A successful one carries exactly the
            // native plan's diagnostics — the label style reaches only rendered names (D-044), and
            // every rendered-name defect is an Error that would have failed the plan — so nothing
            // is added here and no diagnostic is reported twice. A FAILING one is different: it
            // found a defect the native labels do not have, and that is the run's failure.
            var planned = run.PlanEffective(LabelStyle.V2Compat);
            if (!planned.TryGetValue(out var effective))
            {
                diagnostics.AddRange(planned.Diagnostics);
                return RunPipeline.Fail(environment, new PipelineDiagnosticFailure(diagnostics), cancellation);
            }

            plan = effective;
        }

        var format = invocation.Value("--format")!;
        var writesCxt = format is "cxt" or "both";
        var writesDat = format is "dat" or "both";
        var writesManifest = !invocation.Has("--no-manifest");

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
        catch (Exception exception) when (PublicationTransaction.IsPublicationFailure(exception))
        {
            return RunPipeline.HostFailure(
                environment, diagnostics, RunPipeline.DataReadMessage(run.DataPath), cancellation);
        }

        foreach (var file in run.SpecChain)
        {
            inputs.Add(new PublicationInput(file.Spelling, file.FullPath));
        }

        // The complete preflight — identity collisions, residue, existing targets — finishes
        // before any record, stage, or backup can exist.
        var preparation = PublicationTransaction.Preflight(
            environment.PublicationFiles,

            // A factory, not an instance: identity is memoized per path, so the collision check
            // must run on a service acquired after any recovery has finished moving files.
            FileIdentity.CreateDefault,
            invocation.Value("--out")!,
            kinds,
            inputs,
            invocation.Has("--force"),

            // The exact host token reaches residue classification, validated recovery, and every
            // mutation boundary inside it: a signal must stop the run before it begins a new
            // transaction, not merely before the first write (CX-M7H-022).
            cancellation);

        if (preparation is not PublicationReady(var transaction))
        {
            return RunPipeline.HostFailure(
                environment, diagnostics, ((PublicationRefused)preparation).Message, cancellation);
        }

        try
        {
            return await PublishAsync(
                environment, run, transaction, plan, settings, diagnostics, writesCxt, writesDat, writesManifest)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (RunPipeline.IsDataReadFailure(exception) && !transaction.Committed)
        {
            transaction.Rollback();
            return RunPipeline.HostFailure(
                environment, diagnostics, RunPipeline.DataReadMessage(run.DataPath), cancellation);
        }
        catch
        {
            // Cancellation and genuine faults alike: undo everything this run staged or committed
            // before the commit point, then let the host classify (exit 3 or exit 4). Past the
            // commit point the run is public and is never unwound.
            if (!transaction.Committed)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (PublicationFaultException)
                {
                    // A contract defect met while undoing does not get to REPLACE the outcome that
                    // sent the run here. An exact-host-token cancellation is exit 3 by contract
                    // (CX-M7H-004/022), and letting a rollback-time fault escape from inside this
                    // handler would silently promote it to exit 4 (CX-M7H-041). The original
                    // exception is rethrown below; where it is itself a fault, the classification
                    // is the same either way.
                }
            }

            throw;
        }
    }

    private static async Task<int> PublishAsync(
        CliEnvironment environment,
        PreparedRun run,
        PublicationTransaction transaction,
        ConversionPlan plan,
        OutputSettings settings,
        List<BedrockDiagnostic> diagnostics,
        bool writesCxt,
        bool writesDat,
        bool writesManifest)
    {
        var cancellation = environment.Signals.Token;

        // A signal that arrived during preparation stops the run here: after it, no new
        // transaction begins at all (CX-M7H-022).
        cancellation.ThrowIfCancellationRequested();

        if (transaction.Begin() is { } started)
        {
            return RunPipeline.HostFailure(environment, diagnostics, started.Message, cancellation);
        }

        PublicationFailure? failure;

        // ONE session brackets the whole conversion attempt, whatever --format selected: data
        // diagnostics are collected by its first pass only — so `both` never double-counts the
        // conversion the two formats share — while grouping storage failures are intercepted on
        // every real pass and flushed, aggregated, at disposal (D-082/D-105).
        using (var replay = EmitReplay.Begin(run.EmitWith(plan), diagnostics))
        {
            failure = writesCxt
                ? await transaction.StageAsync(
                    PublicationTargetKind.Cxt,
                    stage => CxtWriter.WriteAsync(
                        plan,
                        replay.Open,
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
                    stage => DatWriter.WriteAsync(replay.Open(), settings.Dat, stage, cancellation))
                    .ConfigureAwait(false);
            }
        }

        // Only here are the emit diagnostics authoritative (D-105): the session's final
        // cross-pass aggregates land at disposal, so artifact validity is decided after it.
        if (failure is not null)
        {
            transaction.Rollback();
            return RunPipeline.HostFailure(environment, diagnostics, failure.Message, cancellation);
        }

        if (run.Input.HasMismatch)
        {
            transaction.Rollback();
            return RunPipeline.HostFailure(
                environment, diagnostics, RunPipeline.InputChangedMessage(run.DataPath), cancellation);
        }

        if (run.Input.Digest is null)
        {
            // Every emit pass reads the source to its end, so a run with no completed pass never
            // read the data at all — the same condition, and the same honest message, as any
            // other unreadable input.
            transaction.Rollback();
            return RunPipeline.HostFailure(
                environment, diagnostics, RunPipeline.DataReadMessage(run.DataPath), cancellation);
        }

        if (DiagnosticRenderer.HasErrors(diagnostics))
        {
            // Every staged artifact is invalid; nothing is committed and nothing becomes public
            // (D-105's caller-discard rule, realized as a discarded stage).
            transaction.Rollback();
            cancellation.ThrowIfCancellationRequested();
            DiagnosticRenderer.Write(environment.Error, diagnostics);
            return 1;
        }

        if (writesManifest)
        {
            var manifest = ConvertManifest.Compose(
                run,
                environment,
                Outputs(transaction, writesCxt, writesDat),
                writesCxt ? FingerprintCalculator.ComputeCxtOutputFingerprint(plan, settings.CxtFingerprint(plan.LabelStyle)) : null,
                writesDat ? FingerprintCalculator.ComputeDatOutputFingerprint(plan, settings.DatFingerprint()) : null);

            var bytes = Utf8NoBom.GetBytes(RunManifestWriter.Write(manifest));
            failure = await transaction.StageAsync(
                PublicationTargetKind.Manifest,
                stage => stage.WriteAsync(bytes, 0, bytes.Length, cancellation)).ConfigureAwait(false);

            if (failure is not null)
            {
                transaction.Rollback();
                return RunPipeline.HostFailure(environment, diagnostics, failure.Message, cancellation);
            }
        }

        // Every stage is now written and flushed. Sealing publishes each target's durable identity
        // evidence and then records that fact — which is what lets a later run tell "a commit
        // rename consumed this stage" from "this stage was never created", and what proves which
        // exact object each target holds before rollback may remove one.
        if (transaction.Seal(cancellation) is { } marked)
        {
            transaction.Rollback();
            return RunPipeline.HostFailure(environment, diagnostics, marked.Message, cancellation);
        }

        // Commit observes the host token before every pre-commit transition of its own; this is
        // simply the last check before the first one.
        cancellation.ThrowIfCancellationRequested();

        if (transaction.Commit(cancellation) is { } committed)
        {
            transaction.Rollback();
            return RunPipeline.HostFailure(environment, diagnostics, committed.Message, cancellation);
        }

        // Committed. Warnings and Info are rendered and the exit stays 0 — and no cancellation
        // check runs here, because a signal arriving after the commit point must not report a
        // published run as cancelled.
        DiagnosticRenderer.Write(environment.Error, diagnostics);
        return 0;
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
