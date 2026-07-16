namespace FcaBedrock.Diagnostics.Tests;

/// <summary>
/// Governance locks on the diagnostic registry (spec §16.4, D-085 enum timing): a code exists
/// only once it has a real emit site, so the enum grows per slice rather than front-loading codes
/// no path produces (P-3). These tests make the current slice's boundary a runtime assertion
/// rather than something only a diff review would notice.
/// <para>
/// Deliberately targeted rather than a whole-enum snapshot: a snapshot of every name churns on
/// every slice and invites blind re-baselining, whereas naming the codes this milestone must have —
/// and the ones it must NOT yet have — states the transitional contract directly and fails for the
/// right reason. It is the same posture as <c>TomlSpellingsTests</c>' deferred-kind set lock.
/// <para>
/// The count lock and the named presence/absence assertions only work <b>together</b>: the count
/// alone would accept a swap, and the named lists alone would let an unrelated member slip in
/// unnoticed. Both must be updated deliberately, with the slice's decisions.md entry.
/// </para>
/// </para>
/// </summary>
public sealed class DiagnosticCodeRegistryTests
{
    // The exact M4 Slice D delta (D-103): one member, no more. Slice D is deliberately frugal —
    // equal_frequency and percentile reuse Slice C's calibration codes, so the only genuinely new
    // condition is a population too large to count exactly (G-13).
    private static readonly string[] SliceDAdditions =
    [
        nameof(DiagnosticCode.CalibrationPopulationTooLarge),
    ];

    // Slice C's calibration codes, which Slice D reuses rather than duplicating: equal_frequency's
    // distinct-value guard and percentile's empty/no-spread span are both "the data cannot bound
    // these cuts" (CalibrationDataInsufficient), and invalid computed cuts are invalid computed
    // cuts whichever formula produced them (CalibrationCutsInvalid). One condition → one code
    // (D-067).
    private static readonly string[] ReusedCalibrationCodes =
    [
        nameof(DiagnosticCode.CalibrationDataInsufficient),
        nameof(DiagnosticCode.CalibrationCutsInvalid),
    ];

    // Codes the approved M4 plan assigns to LATER slices. Each must stay absent until the slice
    // that owns its emit site lands, so an early or accidental addition fails here.
    private static readonly string[] NotYetOwned =
    [
        "ValueGroupsLabelDuplicate",                     // Slice E
        "OrdinalNotAllowedWithValueGroupsPassthrough",   // Slice E
        "ValueGroupsPassthroughDataDependent",           // Slice E
        "RestrictToRangeInvalid",                        // Slice F
        "RestrictToNumericEntryRequired",                // Slice F (the D-091 rename)
        "NoObjectsEmitted",                              // Slice F
        "AttributeHasNoCrosses",                         // Slice F
        "ObjectHasNoCrosses",                            // Slice F
        "OutputCxtSizeAdvisory",                         // M7
        "DateValueTypeNotImplementedV1",                 // deferred (D-038)
    ];

    // The registry size after M4 Slice D: 64 members at the Slice C baseline plus this slice's
    // one (D-103). Update this number ONLY together with the slice's decisions.md entry — that
    // deliberate edit is the point (D-085: a code exists once it has a real emit site, so the enum
    // grows per slice rather than drifting). Without it the presence/absence assertions below would
    // let an unrelated member in unnoticed, and the "exactly one" delta would not be locked.
    private const int MembersAfterSliceD = 65;

    private static readonly string[] Defined = Enum.GetNames<DiagnosticCode>();

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenItsOneCodeIsDefined() =>
        Assert.All(SliceDAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenTheRegistryGrewByExactlyItsOneCode() =>
        // The delta lock. On its own a count proves little; combined with the presence list above
        // and the absence list below it pins BOTH which codes arrived and that nothing else did —
        // which the targeted assertions alone cannot do (an unrelated addition would pass them).
        Assert.Equal(MembersAfterSliceD, Defined.Length);

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenTheReusedCalibrationCodesRemain() =>
        Assert.All(ReusedCalibrationCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenNoLaterSliceCodeIsDefinedYet() =>
        // The D-085 rule made mechanical: a code with no emit site in this milestone must not exist.
        Assert.All(NotYetOwned, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenTheTransitionalRejectsStillExist()
    {
        // The transitional codes retire with their features, not before: equal_frequency left the
        // deferred-kind set at Slice D, but value_groups still needs the member (D-070) — it is
        // now the code's only owner — and restrict_to execution is Slice F (D-057).
        Assert.Contains(nameof(DiagnosticCode.DiscretizerKindNotYetSupported), Defined);
        Assert.Contains(nameof(DiagnosticCode.RestrictToNotImplementedV1), Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenSliceDLanded_ThenTheRetiredSliceACodeStaysRetired() =>
        // ObservedDomainCalibrationNotImplementedV1 retired when observed-domain calibration landed
        // (D-098); a later slice must not resurrect it.
        Assert.DoesNotContain("ObservedDomainCalibrationNotImplementedV1", Defined);

    [Fact]
    public void DiagnosticCode_WhenInspected_ThenEveryNameIsUnique() =>
        Assert.Equal(Defined.Length, Defined.Distinct(StringComparer.Ordinal).Count());
}
