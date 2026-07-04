using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The spec-load face of fingerprinting (spec §3/§14, D-051/D-069/D-077):
/// computes a document's <i>native</i> fingerprints — the spec's own resolved
/// output settings, no CLI overrides — and verifies the stored <c>[spec]</c>
/// fields against them, warning per stale field. Verification is defined only
/// over a successful plan (a failed resolve/plan already fails the run). There
/// is no production orchestrator yet; tests call this now and M7's CLI is the
/// real caller (the D-067 pattern).
/// </summary>
public static class SpecFingerprints
{
    /// <summary>
    /// Computes the three native fingerprints for <paramref name="document"/>:
    /// the §8/§21 output defaults apply where <c>[output]</c> is silent
    /// (<c>lf</c>, <c>trailing_newline = true</c>, <c>base_index = 1</c>, no
    /// trailing spaces, <c>bin_label_unicode = false</c>), the label style is
    /// always <see cref="LabelStyle.Native"/> (v2-compat is a CLI override,
    /// D-011), and <c>size_advisory_bytes</c> is ignored — advisory, not
    /// byte-affecting (D-077).
    /// <para><b>Precondition:</b> <paramref name="plan"/> was produced with
    /// <see cref="LabelStyle.Native"/>, so its rendered names pair with the
    /// native label style this method hashes (see
    /// <see cref="FingerprintCalculator.ComputeCxtOutputFingerprint"/>).</para>
    /// </summary>
    public static ComputedFingerprints ComputeNative(SpecDocument document, BedrockSpec spec, ConversionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(plan);

        var output = document.Output;
        var cxt = new CxtFingerprintInputs(
            LabelStyle.Native,
            output?.BinLabelUnicode ?? false,
            ToLineEnding(output?.Cxt?.LineEndings ?? LineEndings.Lf),
            output?.Cxt?.TrailingNewline ?? true);
        var dat = new DatFingerprintInputs(
            output?.Dat?.BaseIndex ?? 1,
            ToLineEnding(output?.Dat?.LineEndings ?? LineEndings.Lf),
            output?.Dat?.NonemptyLineTrailingSpace ?? false,
            output?.Dat?.EmptyLineTrailingSpace ?? false);

        return new ComputedFingerprints(
            FingerprintCalculator.ComputeSchemaFingerprint(plan),
            FingerprintCalculator.ComputeCxtOutputFingerprint(plan, spec, cxt),
            FingerprintCalculator.ComputeDatOutputFingerprint(plan, spec, dat));
    }

    /// <summary>
    /// Verifies the stored <c>[spec]</c> fingerprints against
    /// <paramref name="computed"/> (§14): an absent stored field is silent (the
    /// fields are optional, §3); a matching one is silent (no "verified" noise,
    /// P-3); a differing one warns with its own code. The three checks are
    /// independent — every stale field reports (the D-076 co-fire stance).
    /// Comparison is ordinal string equality of the full stored value, so a
    /// malformed stored string simply reads as stale (D-077).
    /// </summary>
    public static IReadOnlyList<BedrockDiagnostic> VerifyStored(
        SpecDocument document, ComputedFingerprints computed, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(computed);

        if (document.Spec is not { } section)
        {
            return [];
        }

        var diagnostics = new List<BedrockDiagnostic>(3);
        Check(diagnostics, DiagnosticCode.SchemaFingerprintStale, "schema_fingerprint",
            section.SchemaFingerprint, computed.SchemaFingerprint, filePath);
        Check(diagnostics, DiagnosticCode.CxtOutputFingerprintStale, "cxt_output_fingerprint",
            section.CxtOutputFingerprint, computed.CxtOutputFingerprint, filePath);
        Check(diagnostics, DiagnosticCode.DatOutputFingerprintStale, "dat_output_fingerprint",
            section.DatOutputFingerprint, computed.DatOutputFingerprint, filePath);
        return diagnostics;
    }

    private static void Check(
        List<BedrockDiagnostic> diagnostics,
        DiagnosticCode code,
        string field,
        string? stored,
        string computed,
        string? filePath)
    {
        if (stored is null || string.Equals(stored, computed, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(new BedrockDiagnostic(
            code,
            DiagnosticSeverity.Warning,
            $"Stored {field} '{stored}' does not match the computed '{computed}'; the spec changed since it was frozen — recompute or remove it (§14).",
            filePath is null ? null : new DiagnosticLocation(File: filePath)));
    }

    private static LineEnding ToLineEnding(LineEndings lineEndings) => lineEndings switch
    {
        LineEndings.Lf => LineEnding.Lf,
        LineEndings.Crlf => LineEnding.Crlf,
        _ => throw new InvalidOperationException($"Unknown line-ending kind {lineEndings}."),
    };
}
