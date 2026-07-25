using System.Text;

namespace FcaBedrock.Cli.Commands;

/// <summary>
/// <c>fingerprint SPEC DATA [--temp-dir DIR]</c> — report mode (D-122 part 10).
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
/// </summary>
internal static class FingerprintCommand
{
    /// <summary>The code-less host error a parse-valid <c>--write</c> invocation produces until S10.</summary>
    internal const string WriteNotImplementedMessage = "the fingerprint --write mode is not implemented yet.";

    /// <summary>Runs the command; 0 when no Error/Fatal diagnostic was produced, otherwise 1.</summary>
    public static async Task<int> RunAsync(CommandInvocation invocation, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(environment);

        if (invocation.Has("--write"))
        {
            // The grammar for write mode is settled and parses, but the behaviour lands with
            // S10. Refusing here — before a single file is opened — is what keeps it
            // non-mutating in the strongest sense: no input is read, no fingerprint computed,
            // no output target opened, created, or replaced, and no report printed.
            environment.Error.Write(DiagnosticRenderer.RenderHostError(WriteNotImplementedMessage));
            return 1;
        }

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

        return RunPipeline.Complete(
            environment, prepared.Diagnostics, report.ToString(), environment.Signals.Token);
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
