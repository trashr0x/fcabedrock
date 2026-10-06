namespace FcaBedrock.Diagnostics.Tests;

/// <summary>
/// Governance locks on the diagnostic registry (spec §16.4, D-085 enum timing): a code exists
/// only once it has a real emit site, so the enum grows with its emit sites rather than
/// front-loading codes no path produces (EP-3). These tests make that boundary a runtime assertion
/// rather than something only a diff review would notice.
/// <para>
/// Deliberately targeted rather than a whole-enum snapshot: a snapshot of every name churns on
/// every change and invites blind re-baselining, whereas naming the codes each feature must have,
/// and the retired codes that must stay gone, states the contract directly and fails for the right
/// reason.
/// </para>
/// <para>
/// The count lock and the named presence/absence assertions only work <b>together</b>: the count
/// alone would accept a swap, and the named lists alone would let an unrelated member slip in
/// unnoticed. Both must be updated deliberately, with the decisions.md entry that changes the
/// registry.
/// </para>
/// </summary>
public sealed class DiagnosticCodeRegistryTests
{
    // restrict_to execution (D-105): one spec-validate code for invalid numeric restriction
    // entries, and the three emit-observability codes whose sites arrived with restrict_to
    // execution (registered in §16.4 before they were enum members, the D-085 rule).
    private static readonly string[] RestrictToExecutionCodes =
    [
        nameof(DiagnosticCode.RestrictToRangeInvalid),
        nameof(DiagnosticCode.NoObjectsEmitted),
        nameof(DiagnosticCode.AttributeHasNoCrosses),
        nameof(DiagnosticCode.ObjectHasNoCrosses),
    ];

    // The D-091 rename, made with its check site (D-085). The old spelling is gone, with no alias
    // and no obsolete member: "requires a range" stopped being the whole rule once the exact
    // { value = n } entry existed.
    private const string RenamedTo = nameof(DiagnosticCode.RestrictToNumericEntryRequired);
    private const string RenamedFrom = "RestrictToOnNumericRequiresRange";

    // Codes restrict_to execution reuses rather than duplicating: a malformed { value = … } is a
    // malformed field (SpecFieldInvalid), a numeric entry on a string source is the ordinary
    // value-type mismatch (SourceValueTypeInvalid), and an unparseable value read for a filter-only
    // restriction is an unparseable source value at the policy severity (SourceValueUnparseable,
    // D-097). One condition, one code (D-067).
    private static readonly string[] RestrictToReusedCodes =
    [
        nameof(DiagnosticCode.SpecFieldInvalid),
        nameof(DiagnosticCode.SpecKeyUnrecognized),
        nameof(DiagnosticCode.SourceValueTypeInvalid),
        nameof(DiagnosticCode.SourceValueUnparseable),
        nameof(DiagnosticCode.RestrictToValueNotInDomain),
        nameof(DiagnosticCode.NoFormalAttributes),
        nameof(DiagnosticCode.DuplicateObjectKey),
    ];

    // The calibration and executable-discretizer codes (D-098 to D-104), which restrict_to
    // execution left untouched.
    private static readonly string[] DiscretizerExecutionCodes =
    [
        nameof(DiagnosticCode.ObservedDomainUsed),
        nameof(DiagnosticCode.UnknownValuePolicyInclude),
        nameof(DiagnosticCode.DeclaredDomainInvalid),
        nameof(DiagnosticCode.ValueLabelKeyDuplicate),
        nameof(DiagnosticCode.EqualWidthRangeInvalid),
        nameof(DiagnosticCode.EqualWidthCutsCollapsed),
        nameof(DiagnosticCode.CalibrationDataInsufficient),
        nameof(DiagnosticCode.CalibrationCutsInvalid),
        nameof(DiagnosticCode.CalibrationPopulationTooLarge),
        nameof(DiagnosticCode.ValueGroupsLabelDuplicate),
        nameof(DiagnosticCode.OrdinalNotAllowedWithValueGroupsPassthrough),
        nameof(DiagnosticCode.ValueGroupsPassthroughDataDependent),
    ];

    // The five probe codes (D-111), each registered with a live wide emit site: no half-registered
    // code, and no code waiting for the triple half. The two structural widenings
    // (TripleSubjectNotContiguous and ObjectKeyValueInvalid gaining the probe phase) add no member.
    private static readonly string[] ProbeCodes =
    [
        nameof(DiagnosticCode.ProbeSourceReadFailed),
        nameof(DiagnosticCode.ProbeNoAttributesDiscovered),
        nameof(DiagnosticCode.ProbeAttributeNameAdjusted),
        nameof(DiagnosticCode.ProbeDomainTruncated),
        nameof(DiagnosticCode.ProbeLimitExceeded),
    ];

    // The rendered-name code (D-120): one plan-phase code for an invalid rendered formal-attribute
    // name, emitted by ConversionPlanner. The naming carriers mint no code: every §10.1/§10.7 shape
    // failure reuses SpecFieldInvalid (D-116), which is why this list has one member and not several.
    private static readonly string[] NamingCodes =
    [
        nameof(DiagnosticCode.FormalAttributeNameInvalid),
    ];

    // The six spec-resolve codes template and matcher application needs (D-121), each emitted by
    // SpecResolver. Two are Errors on template identity, one covers every unknown reference
    // (matcher and attribute sites alike), one is the triple/range shape incompatibility, and two
    // are the matcher Warnings.
    //
    // The static authored-shape failures mint nothing: an invalid template id, a missing matcher
    // template, both-or-neither selector, an uncompilable name_regex and a malformed
    // source_index_range are all the ordinary parse-phase SpecFieldInvalid (§16.4/D-116), which is
    // why this list has six members and not eleven.
    private static readonly string[] TemplateAndMatcherCodes =
    [
        nameof(DiagnosticCode.TemplateIdMissing),
        nameof(DiagnosticCode.TemplateIdDuplicate),
        nameof(DiagnosticCode.TemplateReferenceUnknown),
        nameof(DiagnosticCode.MatcherSelectorInvalidForShape),
        nameof(DiagnosticCode.MatcherSelectsNoAttributes),
        nameof(DiagnosticCode.MatcherFullyShadowed),
    ];

    // The one export-phase code, OutputCxtSizeAdvisory, emitted by CxtWriter (D-123). Ordinary host
    // and publication failures stay CLI-owned, code-less errors (D-122 part 2), so the registry does
    // not grow for them.
    private static readonly string[] SizeAdvisoryCodes =
    [
        nameof(DiagnosticCode.OutputCxtSizeAdvisory),
    ];

    // The composed-spec attribute minimum (D-135): one spec-resolve code, AttributesMissing,
    // emitted by SpecResolver. The two [output] value rules the same decision adds (base_index 0 or
    // 1, a size_advisory_bytes that is not negative) reuse the parse-phase SpecFieldInvalid, which
    // is why this list has one member and not three.
    private static readonly string[] AttributeMinimumCodes =
    [
        nameof(DiagnosticCode.AttributesMissing),
    ];

    // Transitional codes that retired, each with the decision under which it retired. None may
    // return.
    private static readonly string[] Retired =
    [
        "ObservedDomainCalibrationNotImplementedV1",     // observed-domain calibration executes (D-098)
        "DiscretizerKindNotYetSupported",                // every discretizer kind executes (D-104)
        "RestrictToNotImplementedV1",                    // restrict_to executes (D-105)
        RenamedFrom,                                     // renamed with no alias (D-091, D-105)
        "TemplateMatcherNotImplementedV1",               // templates and matchers apply (D-121)
        "SpecSurfaceNotYetSupported",                    // value_type = "date" is refused at plan (D-038, D-075)
    ];

    // The registry size, 83. AttributesMissing at its resolve emit site (D-135) made it 83, and
    // DateValueTypeNotImplementedV1 replacing SpecSurfaceNotYetSupported (D-038, D-075) kept it
    // there. Update this number ONLY together with the decisions.md entry that changes the registry:
    // that deliberate edit is the point (D-085: a code exists once it has a real emit site, so the
    // enum grows with its emit sites rather than drifting). Without it the presence/absence
    // assertions below would let an unrelated member in unnoticed, and the change would not be
    // locked.
    private const int RegistrySize = 83;

    private static readonly string[] Defined = Enum.GetNames<DiagnosticCode>();

    [Fact]
    public void DiagnosticCode_WhenRestrictToExecutes_ThenItsFourCodesAreDefined() =>
        Assert.All(RestrictToExecutionCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenProbeRuns_ThenItsFiveCodesAreDefined() =>
        Assert.All(ProbeCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenNamesAreRendered_ThenTheInvalidNameCodeIsDefined() =>
        Assert.All(NamingCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenTemplatesAndMatchersApply_ThenTheirSixResolveCodesAreDefined() =>
        Assert.All(TemplateAndMatcherCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenACxtIsExported_ThenTheSizeAdvisoryCodeIsDefined() =>
        Assert.All(SizeAdvisoryCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenTheComposedSpecMinimumIsEnforced_ThenAttributesMissingIsDefined() =>
        Assert.All(AttributeMinimumCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenCounted_ThenTheRegistryIsExactlyEightyThree() =>
        // The count lock. On its own a count proves little; combined with the presence lists above
        // and the absence lists below it pins BOTH which codes arrived, that earlier retirements
        // really stuck, and that nothing else moved, which the targeted assertions alone cannot do.
        Assert.Equal(RegistrySize, Defined.Length);

    [Fact]
    public void DiagnosticCode_WhenTheDateValueTypeIsRefusedAtPlan_ThenTheRefusalCodeReplacesTheParseReject()
    {
        // value_type = "date" parses, and the planner refuses it with the permanent
        // DateValueTypeNotImplementedV1 (D-038). The parse-phase SpecSurfaceNotYetSupported that
        // refused it before had no other condition left, so it is gone: one condition, one code
        // (D-067), and the swap leaves the count at 83.
        Assert.Contains(nameof(DiagnosticCode.DateValueTypeNotImplementedV1), Defined);
        Assert.DoesNotContain("SpecSurfaceNotYetSupported", Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenRestrictToExecutes_ThenTheReusedCodesRemain() =>
        Assert.All(RestrictToReusedCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenDiscretizersExecute_ThenTheirCalibrationCodesRemain() =>
        Assert.All(DiscretizerExecutionCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenACodeRetires_ThenItStaysRetired() =>
        Assert.All(Retired, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenTheRestrictToCodeWasRenamed_ThenNoAliasRemains()
    {
        // The D-091 rename, in both directions: the new spelling exists and the old one is gone.
        // Asserted together because they are one contract: keeping an alias would let stale call
        // sites compile and leave the registry with two names for one condition (D-067).
        Assert.Contains(RenamedTo, Defined);
        Assert.DoesNotContain(RenamedFrom, Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenTransitionalCodesRetired_ThenOnlyThePermanentReservationsRemain()
    {
        // The transitional ledger, asserted as one contract: every transitional reject retired when
        // its feature executed or was refused at plan, so none remains.
        Assert.DoesNotContain("RestrictToNotImplementedV1", Defined);
        Assert.DoesNotContain("DiscretizerKindNotYetSupported", Defined);
        Assert.DoesNotContain("TemplateMatcherNotImplementedV1", Defined);
        Assert.DoesNotContain("SpecSurfaceNotYetSupported", Defined);

        // The permanent v1 reservations (§20) are not transitional, and each has its plan site.
        Assert.Contains(nameof(DiagnosticCode.ScaleNotImplementedV1), Defined);
        Assert.Contains(nameof(DiagnosticCode.ObjectKeyCompositeNotImplementedV1), Defined);
        Assert.Contains(nameof(DiagnosticCode.DateValueTypeNotImplementedV1), Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenInspected_ThenEveryNameIsUnique() =>
        Assert.Equal(Defined.Length, Defined.Distinct(StringComparer.Ordinal).Count());
}
