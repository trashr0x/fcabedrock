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
    /// The triple face of the same outcome: the pass completed but every row's predicate was
    /// missing, so there is no vocabulary to author. The <b>same code</b> as the wide case
    /// deliberately — one condition ("nothing to author"), one code (D-067/D-111) — differing
    /// only in the message that names why.
    /// </summary>
    public static BedrockDiagnostic NoPredicatesDiscovered() =>
        new(DiagnosticCode.ProbeNoAttributesDiscovered, DiagnosticSeverity.Error,
            "The probe discovered no attributes: the triple source has no present predicates; no draft was produced (§7.1).");

    /// <summary>
    /// A triple subject that cannot name an object (§5.4). Reuses the existing structural code
    /// rather than a probe-specific twin, with the <b>same</b> condition, severity, and
    /// record-index location the calibrate/emit sites use (D-111's phase widening): one
    /// structural condition owned by one code across all three phases.
    /// <para>
    /// Halts the probe with no draft. This must never arrive as
    /// <see cref="SourceReadFailed"/> — invalid data and broken storage are different problems
    /// with different remedies (D-111).
    /// </para>
    /// </summary>
    public static BedrockDiagnostic SubjectUnusable(int recordIndex) =>
        new(DiagnosticCode.ObjectKeyValueInvalid, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The triple subject at record {recordIndex} is empty, whitespace-only, a missing token, or contains a control character; it cannot name an object (§5.4)."),
            new DiagnosticLocation(RecordIndex: recordIndex));

    /// <summary>
    /// A subject recurring after an intervening subject under an <b>explicitly selected</b>
    /// <c>subject_grouped</c> ordering (§5.3) — the probe-phase site of the second widened
    /// structural code, again matching the conversion sites exactly. Never raised under
    /// <c>unordered</c>, where interleaved subjects are legal and no grouping pass exists.
    /// </summary>
    public static BedrockDiagnostic SubjectNotContiguous(string subject, int recordIndex) =>
        new(DiagnosticCode.TripleSubjectNotContiguous, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Triple subject '{subject}' recurs at record {recordIndex} after an intervening subject; ordering = \"subject_grouped\" requires contiguous subjects (§5.3)."),
            new DiagnosticLocation(RecordIndex: recordIndex));

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

    /// <summary>
    /// Guard 1, <b>wide</b> — more schema columns than the probe may discover (D-110). The count
    /// is <em>exact</em>: a wide source's attributes are its schema columns, known in full before
    /// any record is read, so the message can state the real total and a caller can raise the
    /// maximum to it in one step.
    /// </summary>
    public static BedrockDiagnostic AttributeLimitExceeded(int columnCount, int max) =>
        new(DiagnosticCode.ProbeLimitExceeded, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe would discover {columnCount} attributes, above the maximum of {max}; no draft was produced (§7.1)."));

    /// <summary>
    /// Guard 1, <b>triple</b> — more distinct predicates than the probe may discover (D-110).
    /// <para>
    /// <b>Deliberately not the exact-count message above.</b> A triple vocabulary is discovered as
    /// it is read, and the guard stops the pass at the <em>first</em> predicate past the maximum —
    /// so at that moment the probe knows only that more than <c>max</c> distinct predicates exist,
    /// never how many. Reporting <c>max + 1</c> through the wide wording would read as a complete
    /// total: a source with 10,000 predicates would say "would discover 4", inviting a retry at 4
    /// that fails identically. Counting the real total would mean reading on — the very work the
    /// guard exists to prevent — so the honest bound is the only thing probe may claim, exactly as
    /// D-108 lets a truncated domain claim "at least one more distinct value exists" and no more.
    /// </para>
    /// <para>
    /// The comparison is worded against the <em>maximum</em> rather than as "more than {max}
    /// predicates", which keeps one deterministic sentence correct at every positive maximum —
    /// including <c>max = 1</c>, where a count-led phrasing would read "more than 1 distinct
    /// predicates". Pluralization branching would buy nothing but a second string to keep true.
    /// </para>
    /// </summary>
    public static BedrockDiagnostic DynamicAttributeLimitExceeded(int max) =>
        new(DiagnosticCode.ProbeLimitExceeded, DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The probe encountered more distinct predicates than the configured maximum of {max} discovered attributes; observation stopped at the first excess predicate and no draft was produced (§7.1)."));

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
