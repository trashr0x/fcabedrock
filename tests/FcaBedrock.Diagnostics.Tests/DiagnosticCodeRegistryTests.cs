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
/// </para>
/// </summary>
public sealed class DiagnosticCodeRegistryTests
{
    // The exact M4 Slice C delta (D-102): four members, no more.
    private static readonly string[] SliceCAdditions =
    [
        nameof(DiagnosticCode.EqualWidthRangeInvalid),
        nameof(DiagnosticCode.EqualWidthCutsCollapsed),
        nameof(DiagnosticCode.CalibrationDataInsufficient),
        nameof(DiagnosticCode.CalibrationCutsInvalid),
    ];

    // Codes the approved M4 plan assigns to LATER slices. Each must stay absent until the slice
    // that owns its emit site lands, so an early or accidental addition fails here.
    private static readonly string[] NotYetOwned =
    [
        "CalibrationPopulationTooLarge",                 // Slice D (G-13)
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

    // The registry size after M4 Slice C: 60 members at the Slice B baseline plus this slice's
    // four (D-102). Update this number ONLY together with the slice's decisions.md entry — that
    // deliberate edit is the point (D-085: a code exists once it has a real emit site, so the enum
    // grows per slice rather than drifting). Without it the presence/absence assertions below would
    // let an unrelated member in unnoticed, and the "exactly four" delta would not be locked.
    private const int MembersAfterSliceC = 64;

    private static readonly string[] Defined = Enum.GetNames<DiagnosticCode>();

    [Fact]
    public void DiagnosticCode_WhenSliceCLanded_ThenItsFourCodesAreDefined() =>
        Assert.All(SliceCAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceCLanded_ThenTheRegistryGrewByExactlyItsFourCodes() =>
        // The delta lock. On its own a count proves little; combined with the presence list above
        // and the absence list below it pins BOTH which codes arrived and that nothing else did —
        // which the targeted assertions alone cannot do (an unrelated addition would pass them).
        Assert.Equal(MembersAfterSliceC, Defined.Length);

    [Fact]
    public void DiagnosticCode_WhenSliceCLanded_ThenNoLaterSliceCodeIsDefinedYet() =>
        // The D-085 rule made mechanical: a code with no emit site in this milestone must not exist.
        Assert.All(NotYetOwned, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceCLanded_ThenTheTransitionalRejectsStillExist()
    {
        // The transitional codes retire with their features, not before: equal_width left the
        // deferred-kind set at Slice C, but equal_frequency/value_groups still need the member
        // (D-070), and restrict_to execution is Slice F (D-057).
        Assert.Contains(nameof(DiagnosticCode.DiscretizerKindNotYetSupported), Defined);
        Assert.Contains(nameof(DiagnosticCode.RestrictToNotImplementedV1), Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenSliceCLanded_ThenTheRetiredSliceACodeStaysRetired() =>
        // ObservedDomainCalibrationNotImplementedV1 retired when observed-domain calibration landed
        // (D-098); a later slice must not resurrect it.
        Assert.DoesNotContain("ObservedDomainCalibrationNotImplementedV1", Defined);

    [Fact]
    public void DiagnosticCode_WhenInspected_ThenEveryNameIsUnique() =>
        Assert.Equal(Defined.Length, Defined.Distinct(StringComparer.Ordinal).Count());
}
