using FcaBedrock.Cli.Publication;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>calibrate SPEC DATA --out PATH|- [--force] [--temp-dir DIR]</c> (D-122 part 10; spec §7/§14).
/// <para>
/// <b>Freezing is orchestration, not derivation.</b> The handler prepares the run once, hands the
/// paired resolved/calibrated state to <see cref="SpecFreezer"/>, re-resolves and replans the
/// frozen document to obtain its native fingerprints, stores those three fields, and delivers the
/// document to <see cref="SingleFileOutput"/>. It never recalibrates, never reopens DATA, never
/// serializes, and computes no output byte of its own.
/// </para>
/// <para>
/// <b>Flattening is structural.</b> The composed document the freezer rewrites has already had
/// <c>extends</c> consumed by composition, so the written spec is one standalone frozen file
/// (§13/§14) — the deliberate opposite of <c>fingerprint --write</c>, which rewrites the authored
/// root and preserves its <c>extends</c>.
/// </para>
/// <para>
/// <b>Stale stored hashes warn and are corrected.</b> The stored fields are verified once against
/// the root document, exactly as <c>plan</c> does, and the freeze then overwrites all three — so
/// the warning describes the input and the output no longer deserves it (D-122 part 10).
/// </para>
/// </summary>
internal static class CalibrateCommand
{
    /// <summary>Runs the command; 0 on a delivered frozen spec, 1 on any Error/Fatal or host failure.</summary>
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

        // Verified once, against the root document, after a successful native plan — the same
        // position and the same argument triple `plan` uses (§14/D-077).
        var stale = SpecFingerprints.VerifyStored(run.RootDocument, run.Fingerprints, run.RootKey);

        var (stored, reResolve, replan) = Frozen(run);
        var composed = Compose(prepared.Diagnostics, stale, reResolve, replan);
        if (stored is null)
        {
            return RunPipeline.Fail(environment, new PipelineDiagnosticFailure(composed), cancellation);
        }

        return await SingleFileOutput.DeliverAsync(
                environment, invocation, stored, composed,
                additionalInputs: ChainInputs(run), committedReport: null, cancellation)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Freeze → re-resolve → plan → compute → store, over already-retained state only: it reads no
    /// data source, opens no stream, touches no filesystem, and never serializes — turning a
    /// document into bytes belongs to <see cref="SingleFileOutput"/> alone.
    /// <para>
    /// <c>Stored</c> is null exactly when the re-resolve or the replan failed, in which case the
    /// corresponding list carries the Error or Fatal explaining why and the other is empty. A freeze
    /// whose output does not re-resolve or plan would be a library defect; reporting it through the
    /// established diagnostic-failure path rather than throwing costs one <c>if</c> and keeps the
    /// outcome a clean exit 1.
    /// </para>
    /// </summary>
    private static (SpecDocument? Stored, IReadOnlyList<BedrockDiagnostic> ReResolve,
                    IReadOnlyList<BedrockDiagnostic> Replan) Frozen(PreparedRun run)
    {
        // Paired by construction: PrepareAsync writes one resolution token into both members, so
        // the freezer's ReferenceEquals guard cannot fire here (D-098/D-123 point 9).
        var frozen = SpecFreezer.Freeze(run.Resolved, run.Calibrated);

        // The retained schema, so the frozen document re-resolves against the very schema the run
        // was prepared against; no new PreparedRun member is needed for it.
        var reResolved = SpecResolver.Resolve(frozen, run.Calibrated.Schema);
        if (!reResolved.TryGetValue(out var resolved))
        {
            return (null, reResolved.Diagnostics, []);
        }

        // Fully declared by construction now, so planning takes no data pass at all. Native only:
        // v2-compatible labels are a convert-only override (D-011), and the style is named
        // explicitly rather than defaulted so the stored fingerprints' pairing is visible here. It
        // is qualified because LabelStyle lives in Core.Discretization, which this file does not
        // import.
        var planned = ConversionPlanner.Plan(
            CalibratedSpec.FromFullyDeclared(resolved.Resolved), Core.Discretization.LabelStyle.Native);
        if (!planned.TryGetValue(out var plan))
        {
            return (null, reResolved.Diagnostics, planned.Diagnostics);
        }

        var computed = SpecFingerprints.ComputeNative(resolved, plan);

        // [spec] survives a successful resolve — SpecVersionUnsupported is Fatal when it is absent
        // — so only the three stored fields change; version, description, and every other section
        // are carried by the record `with`.
        var stored = frozen with
        {
            Spec = frozen.Spec! with
            {
                SchemaFingerprint = computed.SchemaFingerprint,
                CxtOutputFingerprint = computed.CxtOutputFingerprint,
                DatOutputFingerprint = computed.DatOutputFingerprint,
            },
        };

        return (stored, reResolved.Diagnostics, planned.Diagnostics);
    }

    /// <summary>
    /// The delivered diagnostic list: <paramref name="initial"/> in order, then
    /// <paramref name="stale"/> in order, then — in re-resolve-then-replan order — every post-freeze
    /// diagnostic whose key is not already accounted for by <paramref name="initial"/>.
    /// <para>
    /// The re-resolve and the replan run the <em>same two phases again</em>, over the frozen
    /// document. Blind appending would duplicate everything both runs produce; blind suppression
    /// would lose what exists only after the freeze — chiefly <c>MatcherFullyShadowed</c>, which
    /// the freeze itself creates by writing explicit fields that shadow a matcher's every
    /// contribution. Order-preserving multiset difference keeps each distinct statement exactly
    /// once, which is the same suppression rule the preparation path already applies to a re-run
    /// phase (D-098) and satisfies P-14: nothing is truncated, only re-stated facts are dropped.
    /// </para>
    /// <para>
    /// The baseline is built from <paramref name="initial"/> only, so a stale-hash warning can
    /// never mask a phase diagnostic, and the <em>same</em> consumed baseline is threaded through
    /// both phases, so one initial occurrence cancels exactly one later occurrence overall.
    /// </para>
    /// </summary>
    private static List<BedrockDiagnostic> Compose(
        IReadOnlyList<BedrockDiagnostic> initial,
        IReadOnlyList<BedrockDiagnostic> stale,
        IReadOnlyList<BedrockDiagnostic> reResolve,
        IReadOnlyList<BedrockDiagnostic> replan)
    {
        var destination = new List<BedrockDiagnostic>(
            initial.Count + stale.Count + reResolve.Count + replan.Count);
        destination.AddRange(initial);
        destination.AddRange(stale);

        var baseline = new Dictionary<(DiagnosticCode, DiagnosticSeverity, string, DiagnosticLocation?), int>();
        foreach (var diagnostic in initial)
        {
            var key = Key(diagnostic);
            baseline.TryGetValue(key, out var seen);
            baseline[key] = seen + 1;
        }

        AddNew(destination, reResolve, baseline);
        AddNew(destination, replan, baseline);
        return destination;
    }

    // Context is excluded deliberately: it is typed object?, so record equality would compare it by
    // reference and two otherwise identical diagnostics would never match. It is also unreachable
    // here — its only producer is the conversion emit/replay path, which neither a resolve nor a
    // plan runs — so excluding it cannot discard information this composition could have seen.
    private static (DiagnosticCode Code, DiagnosticSeverity Severity, string Message,
                    DiagnosticLocation? Location) Key(BedrockDiagnostic diagnostic) =>
        (diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Location);

    // Order-preserving multiset subtraction: a candidate whose key still has an unconsumed baseline
    // occurrence is dropped and decrements it; anything else is appended. Nothing is sorted,
    // grouped, or reordered, and a genuinely repeated post-freeze diagnostic survives beyond the
    // baseline's count.
    private static void AddNew(
        List<BedrockDiagnostic> destination,
        IReadOnlyList<BedrockDiagnostic> candidates,
        Dictionary<(DiagnosticCode, DiagnosticSeverity, string, DiagnosticLocation?), int> baseline)
    {
        foreach (var candidate in candidates)
        {
            var key = Key(candidate);
            if (baseline.TryGetValue(key, out var remaining) && remaining > 0)
            {
                baseline[key] = remaining - 1;
                continue;
            }

            destination.Add(candidate);
        }
    }

    /// <summary>
    /// The composed spec chain as publication inputs, in <see cref="PreparedRun.SpecChain"/>
    /// order — root first, then each base as the chain was walked. Base paths are
    /// referrer-relative and are already resolved, so they are passed through verbatim and
    /// are never re-derived against the process working directory.
    /// </summary>
    private static IReadOnlyList<PublicationInput> ChainInputs(PreparedRun run)
    {
        var inputs = new List<PublicationInput>(run.SpecChain.Count);
        foreach (var file in run.SpecChain)
        {
            inputs.Add(new PublicationInput(file.Spelling, file.FullPath));
        }

        return inputs;
    }
}
