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
/// right reason.
/// <para>
/// The count lock and the named presence/absence assertions only work <b>together</b>: the count
/// alone would accept a swap, and the named lists alone would let an unrelated member slip in
/// unnoticed. Both must be updated deliberately, with the slice's decisions.md entry.
/// </para>
/// </para>
/// </summary>
public sealed class DiagnosticCodeRegistryTests
{
    // The exact M4 Slice E delta (D-104): three codes for value_groups' three genuinely new
    // conditions — a duplicate authored group label, an ordinal scale over data-discovered
    // passthrough bins, and the data-dependence of a passthrough calibration.
    private static readonly string[] SliceEAdditions =
    [
        nameof(DiagnosticCode.ValueGroupsLabelDuplicate),
        nameof(DiagnosticCode.OrdinalNotAllowedWithValueGroupsPassthrough),
        nameof(DiagnosticCode.ValueGroupsPassthroughDataDependent),
    ];

    // Codes Slice E reuses rather than duplicating: a malformed group/regex/unmatched field is a
    // malformed field (SpecFieldInvalid), an observed passthrough bin colliding with an authored
    // label is a formal-attribute collision like any other (FormalAttributeCollision), and an
    // unmatched value under `skip` is an unknown value (UnknownValueObserved). One condition →
    // one code (D-067).
    private static readonly string[] ReusedCodes =
    [
        nameof(DiagnosticCode.SpecFieldInvalid),
        nameof(DiagnosticCode.FormalAttributeCollision),
        nameof(DiagnosticCode.UnknownValueObserved),
        nameof(DiagnosticCode.OrdinalOrderMissing),
        nameof(DiagnosticCode.OrdinalOrderHasUnknownValue),
        nameof(DiagnosticCode.OrderDomainInvalid),
        nameof(DiagnosticCode.SourceValueTypeInvalid),
    ];

    // Slice D's calibration codes, which must survive Slice E untouched.
    private static readonly string[] SliceDCalibrationCodes =
    [
        nameof(DiagnosticCode.CalibrationDataInsufficient),
        nameof(DiagnosticCode.CalibrationCutsInvalid),
        nameof(DiagnosticCode.CalibrationPopulationTooLarge),
        nameof(DiagnosticCode.ObservedDomainUsed),
        nameof(DiagnosticCode.UnknownValuePolicyInclude),
    ];

    // Codes the approved M4 plan assigns to LATER slices. Each must stay absent until the slice
    // that owns its emit site lands, so an early or accidental addition fails here.
    private static readonly string[] NotYetOwned =
    [
        "RestrictToRangeInvalid",                        // Slice F
        "RestrictToNumericEntryRequired",                // Slice F (the D-091 rename)
        "NoObjectsEmitted",                              // Slice F
        "AttributeHasNoCrosses",                         // Slice F
        "ObjectHasNoCrosses",                            // Slice F
        "OutputCxtSizeAdvisory",                         // M7
        "DateValueTypeNotImplementedV1",                 // deferred (D-038)
    ];

    // Transitional codes that RETIRED, each with the slice that retired it. A later slice must not
    // resurrect one.
    private static readonly string[] Retired =
    [
        "ObservedDomainCalibrationNotImplementedV1",     // retired at Slice A (D-098)
        "DiscretizerKindNotYetSupported",                // retired at Slice E (D-104)
    ];

    // The registry size after M4 Slice E: 65 members at the Slice D baseline, plus this slice's
    // three, minus the one transitional code it retires (D-104) — 65 + 3 - 1. Update this number
    // ONLY together with the slice's decisions.md entry — that deliberate edit is the point
    // (D-085: a code exists once it has a real emit site, so the enum grows per slice rather than
    // drifting). Without it the presence/absence assertions below would let an unrelated member in
    // unnoticed, and the delta would not be locked.
    private const int MembersAfterSliceE = 67;

    private static readonly string[] Defined = Enum.GetNames<DiagnosticCode>();

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenItsThreeCodesAreDefined() =>
        Assert.All(SliceEAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenTheRegistryIsExactlyPlusThreeMinusOne() =>
        // The delta lock. On its own a count proves little; combined with the presence list above
        // and the absence lists below it pins BOTH which codes arrived, that the retirement really
        // happened, and that nothing else moved — which the targeted assertions alone cannot do.
        Assert.Equal(MembersAfterSliceE, Defined.Length);

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenTheReusedCodesRemain() =>
        Assert.All(ReusedCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenSliceDCalibrationCodesRemain() =>
        Assert.All(SliceDCalibrationCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenNoLaterSliceCodeIsDefinedYet() =>
        // The D-085 rule made mechanical: a code with no emit site in this milestone must not exist.
        Assert.All(NotYetOwned, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenRetiredTransitionalCodesStayRetired() =>
        Assert.All(Retired, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceELanded_ThenTheLastDeferredKindCodeIsGoneButRestrictToRemains()
    {
        // The two halves of the Slice E transitional boundary, asserted together because they are
        // one contract: every discretizer kind is now executable, so DiscretizerKindNotYetSupported
        // has no owner left and retires (D-070 complete) — while restrict_to execution is still
        // Slice F, so its transitional reject must stay live (D-057).
        Assert.DoesNotContain(nameof(DiagnosticCode.RestrictToNotImplementedV1), Retired);
        Assert.Contains(nameof(DiagnosticCode.RestrictToNotImplementedV1), Defined);
        Assert.DoesNotContain("DiscretizerKindNotYetSupported", Defined);

        // SpecSurfaceNotYetSupported keeps its own owners (naming carriers → M6, date → D-038),
        // so it is unaffected by the deferred-KIND retirement.
        Assert.Contains(nameof(DiagnosticCode.SpecSurfaceNotYetSupported), Defined);
        Assert.Contains(nameof(DiagnosticCode.TemplateMatcherNotImplementedV1), Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenInspected_ThenEveryNameIsUnique() =>
        Assert.Equal(Defined.Length, Defined.Distinct(StringComparer.Ordinal).Count());
}
