using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>convert SPEC DATA --out BASE --format cxt|dat|both [--v2-compat] [--no-manifest] [--force]
/// [--temp-dir DIR]</c> (D-122 parts 4–7).
/// <para>
/// The shared preparation runs exactly as it does for the report commands (schema, conditional
/// calibration, one native plan, the three native fingerprints, and the single stored-fingerprint
/// verification) and only then does conversion diverge: with <c>--v2-compat</c> the <em>same</em>
/// calibrated state is planned once more for emission, so the native side that verifies the spec
/// and the effective side that produces the bytes stay exactly paired (D-044/D-077).
/// </para>
/// <para>
/// <b>Nothing becomes public until the whole run is known good.</b> Artifacts are staged and
/// hashed, the diagnostics become authoritative when the replay session is disposed, the input
/// digest is re-checked, and only a run with no Error or Fatal reaches the commit, where the
/// manifest publishes last as the run's public marker (<see cref="ConvertPublication"/>).
/// <b>Stdout is exactly empty on every success</b>; diagnostics go to stderr, in library order,
/// once.
/// </para>
/// </summary>
internal static class ConvertCommand
{
    /// <summary>Runs the command; 0 on a committed run, 1 on any Error/Fatal or host failure.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var cancellation = environment.Signals.Token;

        var outcome = await RunRendering.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome.IsError || outcome.Value.Value is not { } run)
        {
            return RunRendering.Fail(environment, outcome, cancellation);
        }

        var diagnostics = new List<BedrockDiagnostic>(outcome.Value.Diagnostics);

        // Once, against the ROOT document and the NATIVE fingerprints, never against an
        // effective override value, which is a manifest fact and never a stored one (§14/D-077).
        diagnostics.AddRange(SpecFingerprints.VerifyStored(run.RootDocument, run.Fingerprints, run.RootKey));

        // A failing --v2-compat plan fails with the native list plus its own diagnostics.
        var planned = ConvertPublication.PlanConversion(run, invocation.Has("--v2-compat"), diagnostics);
        if (planned.Value is not { } conversion)
        {
            return RunRendering.Fail(environment, planned, cancellation);
        }

        var format = invocation.Value("--format")!;
        var output = new ConvertOutput
        {
            Base = invocation.Value("--out")!,
            WritesCxt = format is "cxt" or "both",
            WritesDat = format is "dat" or "both",
            WritesManifest = !invocation.Has("--no-manifest"),
            ReplaceExisting = invocation.Has("--force"),
        };

        // The complete preflight (identity collisions, residue, existing targets) finishes before
        // any record, stage, or backup can exist, and DATA's full path is resolved before it.
        var preflight = ConvertPublication.Preflight(output, run, environment.Orchestration, cancellation);
        if (preflight.IsError)
        {
            return RunRendering.HostFailure(
                environment,
                planned.Diagnostics,
                preflight.Error.Message,
                cancellation);
        }

        var handle = preflight.Value;

        // The handle holds live references to every object whose identity authorizes a mutation,
        // so it is disposed on every exit: success, host failure, cancellation and unexpected fault
        // alike. Releasing them is not cleanup that can be skipped: on Windows a surviving reference
        // keeps a completed deletion pending (D-125). The publish releases them itself; a failure
        // before the commit point is rolled back there, and cancellation and genuine faults
        // propagate for the host to classify (exit 3 or exit 4).
        using (handle)
        {
            var published = await ConvertPublication.PublishAsync(
                    conversion,
                    handle,
                    new ManifestHostFacts
                    {
                        ToolVersion = environment.ToolVersion,
                        CommandLine = environment.AuditArgv,
                        UtcNow = () => environment.Clock.UtcNow,
                    },
                    planned.Diagnostics,
                    cancellation)
                .ConfigureAwait(false);

            if (published.IsError || published.Value.Value is not { } committed)
            {
                return RunRendering.Fail(environment, published, cancellation);
            }

            // Committed. Warnings and Info are rendered and the exit stays 0, and no cancellation
            // check runs here, because a signal arriving after the commit point must not report a
            // published run as cancelled. A private cleanup that did not finish adds one code-less
            // warning after them; the published run stands either way.
            DiagnosticRenderer.Write(environment.Error, published.Value.Diagnostics);
            if (committed.CleanupWarning is { } warning)
            {
                environment.Error.Write(DiagnosticRenderer.RenderHostWarning(warning));
            }

            return 0;
        }
    }
}
