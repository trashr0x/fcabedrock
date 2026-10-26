using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Orchestration;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>fingerprint SPEC DATA [--write --out NEW_SPEC|-] [--force] [--temp-dir DIR]</c>
/// (D-122 part 10).
/// <para>
/// Reports the three computed <b>native</b> fingerprints and the state of each corresponding
/// stored field on the <b>root</b> document:
/// </para>
/// <code>
/// schema_fingerprint computed=sha256:… stored=match
/// cxt_output_fingerprint computed=sha256:… stored=stale
/// dat_output_fingerprint computed=sha256:… stored=absent
/// </code>
/// <para>
/// <b>The states are computed here, not warned about.</b> This report <em>is</em> the owner of
/// the stored-versus-computed comparison, so <c>SpecFingerprints.VerifyStored</c> is
/// deliberately not called: doing both would report the same fact twice, once as a field and
/// once as a stale Warning. Every other pipeline diagnostic still renders normally, in library
/// order. Comparison is ordinal over the complete stored string, so a malformed or
/// differently-cased value simply reads <c>stale</c> (D-077).
/// </para>
/// <para>
/// Report mode never freezes, rewrites a stored field, serializes a spec, opens an output, or
/// publishes anything. <c>--v2-compat</c> does not exist here (D-011 keeps it convert-only) and
/// remains a parser usage error.
/// </para>
/// <para>
/// <b><c>--write</c> shares that one preparation path and adds only a rewrite.</b> It refuses a
/// spec that is not <b>fully frozen</b> (<see cref="CalibratedSpec.RequiresData"/> applies the §14
/// definition) with a code-less host error and exit 1, having opened no output. Otherwise it rewrites
/// the <b>authored root</b> document's three stored fingerprint fields, so every other section
/// survives verbatim and the chain is not flattened (§13 rule 8): deliberately the opposite of
/// <c>calibrate</c>, which writes the flattened composed document. It freezes, re-resolves, and
/// replans nothing, and (like report mode) never calls <c>VerifyStored</c>.
/// </para>
/// <para>
/// <b>The root's <c>extends</c> keeps its meaning, not necessarily its text.</b> A file target
/// writes the authored reference where it still reaches, from the target's folder, the base the
/// chain was loaded through, and otherwise a relative reference rebuilt to reach it;
/// <see cref="SpecRelocation"/> checks either before anything is opened for output and again
/// just before publication writes, and refuses when neither can be written and verified. The
/// spec's raw bytes can change with that text; its composition, native fingerprints and
/// conversions do not. <c>--out -</c> writes the authored reference.
/// </para>
/// <para>
/// The report is built <b>before</b> the rewrite, from the input root's stored values, so its
/// <c>stored=</c> states describe what the user's file said on entry. A <b>file</b> target
/// commits the corrected spec and then writes that report to stdout; <c>--out -</c> writes only
/// the corrected spec and suppresses the report entirely, because the stdout path is never handed
/// a committed payload (D-123 point 14).
/// </para>
/// </summary>
internal static class FingerprintCommand
{
    /// <summary>Runs the command; 0 when no Error/Fatal diagnostic was produced, otherwise 1.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        var outcome = await RunRendering.PrepareAsync(invocation, environment).ConfigureAwait(false);
        if (outcome is not OperationCompleted<PreparedFileRun> prepared)
        {
            return RunRendering.Fail(environment, outcome, environment.Signals.Token);
        }

        var run = prepared.Value;
        var fingerprints = FingerprintReport.Of(run);

        var report = new StringBuilder();
        Append(report, "schema_fingerprint", fingerprints.Schema);
        Append(report, "cxt_output_fingerprint", fingerprints.CxtOutput);
        Append(report, "dat_output_fingerprint", fingerprints.DatOutput);

        if (!invocation.Has("--write"))
        {
            return RunRendering.Complete(
                environment, prepared.Diagnostics, report.ToString(), environment.Signals.Token);
        }

        var cancellation = environment.Signals.Token;

        // The §14 check, after preparation because RequiresData needs the schema-aware resolution.
        // A data-dependent spec therefore reads DATA before being refused: deliberate, and still
        // exit 1 with nothing opened for output, no target created, and no transaction begun.
        if (!run.IsFullyFrozen)
        {
            return RunRendering.HostFailure(
                environment,
                prepared.Diagnostics,
                WriteRequiresFullyFrozenMessage(invocation.Operand(0)!),
                cancellation);
        }

        // The ROOT document, as authored, never the composed one, with the three stored fields
        // replaced. Everything else the record carries is untouched, `extends` included unless a
        // file target needs it rewritten below.
        var written = FingerprintWriting.Corrected(run);

        // A file target keeps the root's inheritance from wherever it is written: its extends must
        // still reach the base the chain was loaded through (§13). Stdout has no folder of its own,
        // so the authored reference goes out as it is.
        var outPath = invocation.Value("--out")!;
        Func<string?>? verifyBeforeWrite = null;
        if (written.Spec!.Extends is { } authored && !string.Equals(outPath, "-", StringComparison.Ordinal))
        {
            // Load order is root first, so the base the root's own extends loaded is the second file.
            var immediateBase = run.SpecChain.Count > 1
                ? run.SpecChain[1]
                : throw new InvalidOperationException("A composed root that extends a base has that base in its chain.");

            var relocated = SpecRelocation.Relocate(authored, outPath, immediateBase, environment.Orchestration);
            if (relocated is not OperationCompleted<VerifiedRelocation> { Value: var decision })
            {
                return RunRendering.HostFailure(
                    environment,
                    prepared.Diagnostics,
                    ((OperationHostFailure<VerifiedRelocation>)relocated).Error.Message,
                    cancellation);
            }

            written = written with { Spec = written.Spec with { Extends = decision.Reference } };
            verifyBeforeWrite = () =>
                SpecRelocation.Recheck(decision, environment.Orchestration) is OperationHostFailure<RelocationDecision> refused
                    ? refused.Error.Message
                    : null;
        }

        return await SingleFileOutput.DeliverAsync(
                environment, invocation, written, prepared.Diagnostics,
                chain: run.SpecChain, committedReport: report.ToString(), verifyBeforeWrite, cancellation)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The code-less host error a data-dependent spec produces under <c>--write</c>. It names the
    /// <c>calibrate</c> step that writes a fully frozen copy, with placeholders rather than the
    /// user's paths, so no shell's quoting rules are implied, and it promises nothing: calibration
    /// can still fail on the data.
    /// </summary>
    internal static string WriteRequiresFullyFrozenMessage(string specPath) =>
        $"the spec '{specPath}' is not fully frozen, so its fingerprints cannot be written. If calibration succeeds, "
        + "fcabedrock calibrate SPEC DATA --out NEW_SPEC writes a fully frozen copy that stores them; SPEC is this "
        + "spec, DATA its data and NEW_SPEC a new file.";

    private static void Append(StringBuilder builder, string field, FingerprintComparison comparison)
    {
        builder.Append(field);
        builder.Append(" computed=");
        builder.Append(comparison.Computed);
        builder.Append(" stored=");
        builder.Append(State(comparison.Stored));
        builder.Append('\n');
    }

    // The report's spelling of each state FingerprintReport decides (D-077).
    private static string State(StoredFingerprintState stored) => stored switch
    {
        StoredFingerprintState.Absent => "absent",
        StoredFingerprintState.Match => "match",
        StoredFingerprintState.Stale => "stale",
        _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown stored fingerprint state."),
    };
}
