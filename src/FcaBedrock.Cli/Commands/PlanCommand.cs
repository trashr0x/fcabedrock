using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>plan SPEC DATA [--temp-dir DIR]</c> (D-122 part 10).
/// <para>
/// A dry run in the sense that it <b>publishes no artifact</b> — not in the sense that it
/// avoids the source. DATA is required for every plan, the schema is always acquired, and
/// rows are enumerated exactly when calibration needs them.
/// </para>
/// <para>
/// Stdout is the <see cref="PlanReport"/> byte contract. Stale stored fingerprints are
/// Warnings (§14), so they render on stderr while the report still reaches stdout with exit
/// 0; the stored fields themselves are read from the <b>root</b> document and are never
/// rewritten.
/// </para>
/// </summary>
internal static class PlanCommand
{
    /// <summary>Runs the command; 0 when no Error/Fatal diagnostic was produced, otherwise 1.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var outcome = await RunPipeline.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome is not PipelinePrepared prepared)
        {
            return RunPipeline.Fail(environment, outcome, environment.Signals.Token);
        }

        var run = prepared.Run;
        var diagnostics = new List<BedrockDiagnostic>(prepared.Diagnostics);

        // Verified once, against the root document, after a successful NATIVE plan (§14/D-077).
        diagnostics.AddRange(SpecFingerprints.VerifyStored(run.RootDocument, run.Fingerprints, run.RootKey));

        // Built complete before anything is written, so a failure after this point cannot leave
        // a half-emitted report behind.
        var report = PlanReport.Render(run.Plan, run.Fingerprints);
        return RunPipeline.Complete(environment, diagnostics, report, environment.Signals.Token);
    }
}
