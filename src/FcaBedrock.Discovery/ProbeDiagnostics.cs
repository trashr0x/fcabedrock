using System.Globalization;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Discovery;

/// <summary>
/// The five probe diagnostics (§16.4, D-111), constructed in one place so their severities,
/// wording, and number formatting cannot drift apart.
/// <para>
/// <b>Every message is a function of the input alone</b> (D-112): counts and limits render as
/// ungrouped invariant decimal digits, and nothing carries a stack trace, a path, an ambient
/// culture's formatting, or a provider's own message text — any of which would make the same
/// record sequence produce different diagnostics on a different machine. A read failure names
/// its exception <em>type</em>, which is stable and says which channel failed, but never the
/// exception's message.
/// </para>
/// <para>
/// <b>No <c>Context</c> payload.</b> <see cref="BedrockDiagnostic"/> is a record struct, so a
/// live exception in <c>Context</c> would make two diagnostics from identical input compare
/// unequal — breaking the repeatability the determinism suite asserts.
/// </para>
/// </summary>
internal static class ProbeDiagnostics
{
    /// <summary>
    /// A stream or read failure crossing the source-session seam. Error, no draft — even when
    /// records were already observed, because a partially-read source yields a draft that
    /// silently understates the data (D-112).
    /// </summary>
    public static BedrockDiagnostic SourceReadFailed(Exception cause) =>
        new(DiagnosticCode.ProbeSourceReadFailed, DiagnosticSeverity.Error,
            $"The probe could not read the source ({cause.GetType().Name}); no draft was produced (§7.1).");

    /// <summary>
    /// The D-107 no-valid-draft outcome for a wide source with no columns. Error: a draft must
    /// contain at least one attribute, and a zero-column source can produce none.
    /// </summary>
    public static BedrockDiagnostic NoAttributesDiscovered() =>
        new(DiagnosticCode.ProbeNoAttributesDiscovered, DiagnosticSeverity.Error,
            "The probe discovered no attributes: the wide source has zero columns; no draft was produced (§7.1).");

    /// <summary>
    /// The aggregated naming warning (D-107): names that had to be synthesized from an unusable
    /// header or disambiguated against an already-taken name. Routine headerless
    /// <c>column_N</c> synthesis is <b>not</b> counted here (D-111).
    /// </summary>
    public static BedrockDiagnostic AttributeNameAdjusted(ProbeTally tally) =>
        new(DiagnosticCode.ProbeAttributeNameAdjusted, DiagnosticSeverity.Warning,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe adjusted {tally.Count} attribute name(s) (e.g. {tally.Sample}); every source selector still reads its original column (§7.1)."));

    /// <summary>
    /// The aggregated truncation warning (D-108). Fires only on the strictly-greater boundary:
    /// a domain that fits the limit exactly is complete and is not reported.
    /// </summary>
    public static BedrockDiagnostic DomainTruncated(ProbeTally tally, int limit) =>
        new(DiagnosticCode.ProbeDomainTruncated, DiagnosticSeverity.Warning,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe truncated {tally.Count} attribute domain(s) after {limit} distinct values (e.g. {tally.Sample}); each authors its retained prefix with unknown_value_policy = \"include\" (§7.1)."));

    /// <summary>Guard 1 — more schema columns than the probe may discover (D-110).</summary>
    public static BedrockDiagnostic AttributeLimitExceeded(int columnCount, int max) =>
        new(DiagnosticCode.ProbeLimitExceeded, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe would discover {columnCount} attributes, above the maximum of {max}; no draft was produced (§7.1)."));

    /// <summary>Guard 2 — total retained distinct values across attributes (D-110).</summary>
    public static BedrockDiagnostic ValueLimitExceeded(long max) =>
        new(DiagnosticCode.ProbeLimitExceeded, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe exceeded its maximum of {max} total retained distinct values; no draft was produced (§7.1)."));

    /// <summary>Guard 3 — total retained value text across attributes (D-110).</summary>
    public static BedrockDiagnostic TextLimitExceeded(long max) =>
        new(DiagnosticCode.ProbeLimitExceeded, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe exceeded its maximum of {max} total retained value text (UTF-16 code units); no draft was produced (§7.1)."));
}
