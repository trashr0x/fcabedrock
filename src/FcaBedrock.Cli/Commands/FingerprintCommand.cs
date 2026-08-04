using System.Text;
using FcaBedrock.Cli.Publication;
using FcaBedrock.Core.Calibration;

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
/// spec that is not <b>fully frozen</b> — <see cref="CalibratedSpec.RequiresData"/> is the §14
/// gate — with a code-less host error and exit 1, having opened no output. Otherwise it rewrites
/// the <b>authored root</b> document's three stored fingerprint fields and nothing else, so the
/// root's <c>extends</c> and every other section survive verbatim (§13 rule 8): deliberately the
/// opposite of <c>calibrate</c>, which writes the flattened composed document. It freezes,
/// re-resolves, and replans nothing, and — like report mode — never calls <c>VerifyStored</c>.
/// </para>
/// <para>
/// The report is built <b>before</b> the rewrite, from the input root's stored values, so its
/// <c>stored=</c> states describe what the user's file said on entry. A <b>file</b> target
/// commits the corrected spec and then writes that report to stdout; <c>--out -</c> writes only
/// the corrected spec and suppresses the report entirely, because the stdout path is never handed
/// a committed payload (D-123 point 14 / FBL-M7P-002).
/// </para>
/// </summary>
internal static class FingerprintCommand
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
        var stored = run.RootDocument.Spec;

        var report = new StringBuilder();
        Append(report, "schema_fingerprint", run.Fingerprints.SchemaFingerprint, stored?.SchemaFingerprint);
        Append(report, "cxt_output_fingerprint", run.Fingerprints.CxtOutputFingerprint, stored?.CxtOutputFingerprint);
        Append(report, "dat_output_fingerprint", run.Fingerprints.DatOutputFingerprint, stored?.DatOutputFingerprint);

        if (!invocation.Has("--write"))
        {
            return RunPipeline.Complete(
                environment, prepared.Diagnostics, report.ToString(), environment.Signals.Token);
        }

        // Confined to the write block, introduced only after report mode has already returned, so
        // the two report-mode `environment.Signals.Token` argument expressions above stay exactly
        // as they were and report mode's bytes cannot move.
        var cancellation = environment.Signals.Token;

        // The §14 gate, after preparation because RequiresData needs the schema-aware resolution.
        // A data-dependent spec therefore reads DATA before being refused — deliberate, and still
        // exit 1 with nothing opened for output, no target created, and no transaction begun.
        if (CalibratedSpec.RequiresData(run.Resolved.Resolved.Spec))
        {
            return RunPipeline.HostFailure(
                environment,
                prepared.Diagnostics,
                WriteRequiresFullyFrozenMessage(run.SpecPath),
                cancellation);
        }

        // The ROOT document, as authored — never the composed one — with only the three stored
        // fields replaced. Everything else the record carries, `extends` included, is untouched.
        var written = run.RootDocument with
        {
            Spec = run.RootDocument.Spec! with
            {
                SchemaFingerprint = run.Fingerprints.SchemaFingerprint,
                CxtOutputFingerprint = run.Fingerprints.CxtOutputFingerprint,
                DatOutputFingerprint = run.Fingerprints.DatOutputFingerprint,
            },
        };

        return await SingleFileOutput.DeliverAsync(
                environment, invocation, written, prepared.Diagnostics,
                additionalInputs: ChainInputs(run), committedReport: report.ToString(), cancellation)
            .ConfigureAwait(false);
    }

    /// <summary>The code-less host error a data-dependent spec produces under <c>--write</c>.</summary>
    internal static string WriteRequiresFullyFrozenMessage(string specPath) =>
        $"the spec '{specPath}' is not fully frozen, so its fingerprints cannot be written.";

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

    private static void Append(StringBuilder builder, string field, string computed, string? stored)
    {
        builder.Append(field);
        builder.Append(" computed=");
        builder.Append(computed);
        builder.Append(" stored=");
        builder.Append(State(stored, computed));
        builder.Append('\n');
    }

    // absent = the optional field was not authored (§3); match = ordinal equality with the
    // COMPLETE computed string; stale = anything else, malformed values included.
    private static string State(string? stored, string computed) =>
        stored is null
            ? "absent"
            : string.Equals(stored, computed, StringComparison.Ordinal) ? "match" : "stale";
}
