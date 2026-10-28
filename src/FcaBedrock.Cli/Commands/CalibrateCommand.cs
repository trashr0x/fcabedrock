using FcaBedrock.Orchestration;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>calibrate SPEC DATA --out PATH|- [--force] [--temp-dir DIR]</c> (D-122 part 10; spec §7/§14).
/// <para>
/// <b>Freezing is orchestration, not derivation.</b> The handler prepares the run once, freezes it
/// through <see cref="SpecFreezing.Freeze"/>, which hands the paired resolved/calibrated state to
/// <see cref="SpecFreezer"/>, re-resolves and replans the frozen document to obtain its native
/// fingerprints and stores those three fields, and delivers the document to
/// <see cref="SingleFileOutput"/>. It never recalibrates, never reopens DATA, never serializes,
/// and computes no output byte of its own.
/// </para>
/// <para>
/// <b>Flattening is structural.</b> The composed document the freezer rewrites has already had
/// <c>extends</c> consumed by composition, so the written spec is one standalone frozen file
/// (§13/§14): the deliberate opposite of <c>fingerprint --write</c>, which rewrites the authored
/// root and preserves its <c>extends</c>.
/// </para>
/// <para>
/// <b>Stale stored hashes warn and are corrected.</b> The stored fields are verified once against
/// the root document, exactly as <c>plan</c> does, and the freeze then overwrites all three, so
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

        var outcome = await RunRendering.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome.IsError || outcome.Value.Value is not { } run)
        {
            return RunRendering.Fail(environment, outcome, cancellation);
        }

        // The preparation's own list, unchanged: the freeze verifies the stored fingerprints itself,
        // and keeps the stale warnings out of its multiset baseline.
        var frozen = SpecFreezing.Freeze(run, outcome.Value.Diagnostics);
        if (frozen.Value is not { } stored)
        {
            return RunRendering.Fail(environment, frozen, cancellation);
        }

        return await SingleFileOutput.DeliverAsync(
                environment, invocation, stored, frozen.Diagnostics,
                chain: run.SpecChain, committedReport: null, cancellation)
            .ConfigureAwait(false);
    }
}
