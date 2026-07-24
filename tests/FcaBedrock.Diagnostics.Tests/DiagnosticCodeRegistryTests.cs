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
    // The exact M4 Slice F delta (D-105): one spec-validate code for invalid numeric restriction
    // entries, and the three emit-observability codes whose sites land with restrict_to execution
    // (registered in §16.4 since M2, enum members only now — the D-085 rule).
    private static readonly string[] SliceFAdditions =
    [
        nameof(DiagnosticCode.RestrictToRangeInvalid),
        nameof(DiagnosticCode.NoObjectsEmitted),
        nameof(DiagnosticCode.AttributeHasNoCrosses),
        nameof(DiagnosticCode.ObjectHasNoCrosses),
    ];

    // The D-091 rename, landing with its check site (D-085). The old spelling is GONE — no alias,
    // no obsolete member: "requires a range" stopped being the whole rule once the exact
    // { value = n } entry existed.
    private const string RenamedTo = nameof(DiagnosticCode.RestrictToNumericEntryRequired);
    private const string RenamedFrom = "RestrictToOnNumericRequiresRange";

    // Codes Slice F reuses rather than duplicating: a malformed { value = … } is a malformed field
    // (SpecFieldInvalid), a numeric entry on a string source is the ordinary value-type mismatch
    // (SourceValueTypeInvalid), and an unparseable value read for a filter-only restriction is an
    // unparseable source value at the policy severity (SourceValueUnparseable, D-097). One
    // condition → one code (D-067).
    private static readonly string[] ReusedCodes =
    [
        nameof(DiagnosticCode.SpecFieldInvalid),
        nameof(DiagnosticCode.SpecKeyUnrecognized),
        nameof(DiagnosticCode.SourceValueTypeInvalid),
        nameof(DiagnosticCode.SourceValueUnparseable),
        nameof(DiagnosticCode.RestrictToValueNotInDomain),
        nameof(DiagnosticCode.NoFormalAttributes),
        nameof(DiagnosticCode.DuplicateObjectKey),
    ];

    // Slices A–E codes, which must survive Slice F untouched.
    private static readonly string[] EarlierSliceCodes =
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

    // The exact M5 Slice C delta (D-111): the five `probe` codes, all five with a live WIDE emit
    // site in the same slice that registers them — no half-registered code, and no code waiting
    // for the triple half. The two structural widenings (TripleSubjectNotContiguous,
    // ObjectKeyValueInvalid gaining the `probe` phase) add no member and belong to Slice D.
    private static readonly string[] M5SliceCAdditions =
    [
        nameof(DiagnosticCode.ProbeSourceReadFailed),
        nameof(DiagnosticCode.ProbeNoAttributesDiscovered),
        nameof(DiagnosticCode.ProbeAttributeNameAdjusted),
        nameof(DiagnosticCode.ProbeDomainTruncated),
        nameof(DiagnosticCode.ProbeLimitExceeded),
    ];

    // Codes still owned by LATER milestones. Each must stay absent until the milestone that owns
    // its emit site lands, so an early or accidental addition fails here. Completing M4 retires
    // M4's transitions only — it does not license M6/M7 surface.
    // The exact M6 Slice A delta (D-120): ONE plan-phase code for an invalid rendered
    // formal-attribute name, landing with its emit site in ConversionPlanner. The naming
    // carriers themselves mint no code — every §10.1/§10.7 shape failure reuses
    // SpecFieldInvalid (D-116) — which is why this delta is one member and not several.
    private static readonly string[] M6SliceAAdditions =
    [
        nameof(DiagnosticCode.FormalAttributeNameInvalid),
    ];

    // The exact M6 Slice B delta (D-121): the SIX spec-resolve codes template/matcher application
    // needs, each landing with a live emit site in SpecResolver. Two are Errors on template
    // identity, one covers every unknown reference (matcher and attribute sites alike), one is the
    // triple/range shape incompatibility, and two are the matcher Warnings.
    //
    // The static authored-shape failures deliberately mint NOTHING: an invalid template id, a
    // missing matcher template, both-or-neither selector, an uncompilable name_regex, and a
    // malformed source_index_range are all the ordinary parse-phase SpecFieldInvalid
    // (§16.4/D-116) — which is why this delta is six members and not eleven.
    private static readonly string[] M6SliceBAdditions =
    [
        nameof(DiagnosticCode.TemplateIdMissing),
        nameof(DiagnosticCode.TemplateIdDuplicate),
        nameof(DiagnosticCode.TemplateReferenceUnknown),
        nameof(DiagnosticCode.MatcherSelectorInvalidForShape),
        nameof(DiagnosticCode.MatcherSelectsNoAttributes),
        nameof(DiagnosticCode.MatcherFullyShadowed),
    ];

    // The exact M7 Slice B delta (D-123): ONE export-phase code, OutputCxtSizeAdvisory, landing with
    // its real .cxt emit site in CxtWriter (81 → 82). It is M7's ONLY new diagnostic member —
    // ordinary host/publication failures stay CLI-owned, code-less errors (D-122 part 2), so the
    // registry does not grow for them.
    private static readonly string[] M7SliceBAdditions =
    [
        nameof(DiagnosticCode.OutputCxtSizeAdvisory),
    ];

    // Codes still owned by a LATER, not-yet-implemented surface. Each must stay absent until the
    // milestone that owns its emit site lands, so an early or accidental addition fails here. M7
    // Slice B landed OutputCxtSizeAdvisory at its real export emit site (above; D-123), so the only
    // remaining not-yet-owned code is the deferred D-038 date carrier, which joins the registry when
    // its plan-phase DateValueTypeNotImplementedV1 site lands.
    private static readonly string[] NotYetOwned =
    [
        "DateValueTypeNotImplementedV1",                 // deferred (D-038)
    ];

    // Transitional codes that RETIRED, each with the slice that retired it. A later slice must not
    // resurrect one.
    private static readonly string[] Retired =
    [
        "ObservedDomainCalibrationNotImplementedV1",     // retired at Slice A (D-098)
        "DiscretizerKindNotYetSupported",                // retired at Slice E (D-104)
        "RestrictToNotImplementedV1",                    // retired at Slice F (D-105) — M4's last
        RenamedFrom,                                     // renamed at Slice F (D-105); no alias
        "TemplateMatcherNotImplementedV1",               // retired at M6 Slice B (D-121) — M6's last
    ];

    // The registry size after M7 Slice B: 81 at the M6 exit (76 after M6 Slice A, plus the six
    // resolve codes, minus the retired TemplateMatcherNotImplementedV1 = 81), plus
    // OutputCxtSizeAdvisory at its real export emit site (D-123) — 81 + 1 = 82.
    // (SpecSurfaceNotYetSupported stays live throughout: M6 Slice A narrowed it to value_type =
    // "date" rather than retiring it, so it never moved the count.)
    // Update this number ONLY together with the slice's decisions.md entry — that deliberate edit
    // is the point (D-085: a code exists once it has a real emit site, so the enum grows per
    // slice rather than drifting). Without it the presence/absence assertions below would let an
    // unrelated member in unnoticed, and the delta would not be locked.
    private const int MembersAfterM7SliceB = 82;

    private static readonly string[] Defined = Enum.GetNames<DiagnosticCode>();

    [Fact]
    public void DiagnosticCode_WhenSliceFLanded_ThenItsFourCodesAreDefined() =>
        Assert.All(SliceFAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenM5SliceCLanded_ThenItsFiveProbeCodesAreDefined() =>
        Assert.All(M5SliceCAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenM6SliceALanded_ThenItsOneCodeIsDefined() =>
        Assert.All(M6SliceAAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenM6SliceBLanded_ThenItsSixResolveCodesAreDefined() =>
        Assert.All(M6SliceBAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenM7SliceBLanded_ThenItsOneExportCodeIsDefined() =>
        Assert.All(M7SliceBAdditions, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenM7SliceBLanded_ThenTheRegistryIsExactlyEightyTwo() =>
        // The delta lock. On its own a count proves little; combined with the presence lists above
        // and the absence lists below it pins BOTH which codes arrived, that earlier retirements
        // really stuck, and that nothing else moved — which the targeted assertions alone cannot do.
        Assert.Equal(MembersAfterM7SliceB, Defined.Length);

    [Fact]
    public void DiagnosticCode_WhenM6SliceBLanded_ThenNoM6TransitionalRemainsButDateStillDoes()
    {
        // The M6 transition boundary, asserted as one contract. Template/matcher application
        // EXECUTES now, so its transitional reject is gone and no M6 transitional remains. But
        // SpecSurfaceNotYetSupported is NOT an M6 transitional any more: Slice A narrowed it to
        // its one surviving owner, the value-level value_type = "date" reject, which hands over
        // to DateValueTypeNotImplementedV1 only when the D-038 carrier lands. Removing it here
        // would run ahead of that decision.
        Assert.DoesNotContain("TemplateMatcherNotImplementedV1", Defined);
        Assert.Contains(nameof(DiagnosticCode.SpecSurfaceNotYetSupported), Defined);
        Assert.DoesNotContain("DateValueTypeNotImplementedV1", Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenSliceFLanded_ThenTheReusedCodesRemain() =>
        Assert.All(ReusedCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceFLanded_ThenEarlierSliceCodesRemain() =>
        Assert.All(EarlierSliceCodes, name => Assert.Contains(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenM7SliceBLanded_ThenOnlyTheDeferredDateCodeRemainsUnowned() =>
        // The D-085 rule made mechanical: a code with no emit site yet must not exist. M7 Slice B
        // landed OutputCxtSizeAdvisory, so the sole remaining not-yet-owned code is the deferred
        // D-038 date carrier (DateValueTypeNotImplementedV1).
        Assert.All(NotYetOwned, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceFLanded_ThenRetiredTransitionalCodesStayRetired() =>
        Assert.All(Retired, name => Assert.DoesNotContain(name, Defined));

    [Fact]
    public void DiagnosticCode_WhenSliceFLanded_ThenTheRestrictToRenameIsCompleteWithNoAlias()
    {
        // The D-091 rename, in both directions: the new spelling exists and the old one is gone.
        // Asserted together because they are one contract — keeping an alias would let stale call
        // sites compile and leave the registry with two names for one condition (D-067).
        Assert.Contains(RenamedTo, Defined);
        Assert.DoesNotContain(RenamedFrom, Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenM6SliceBLanded_ThenEveryMilestoneTransitionalIsRetired()
    {
        // The cumulative transitional ledger, asserted as one contract. M4's three went at their
        // own slices (DiscretizerKind… at E, RestrictTo… at F), and M6's last one goes here — so
        // no milestone transitional survives.
        Assert.DoesNotContain("RestrictToNotImplementedV1", Defined);
        Assert.DoesNotContain("DiscretizerKindNotYetSupported", Defined);
        Assert.DoesNotContain("TemplateMatcherNotImplementedV1", Defined);

        // SpecSurfaceNotYetSupported is the one survivor and is NOT a milestone transitional:
        // it now carries a single value-level reject (value_type = "date") that hands over to
        // DateValueTypeNotImplementedV1 with the deferred D-038 carrier.
        Assert.Contains(nameof(DiagnosticCode.SpecSurfaceNotYetSupported), Defined);

        // Permanent v1 reservations are not transitional and are unaffected by any of this.
        Assert.Contains(nameof(DiagnosticCode.ScaleNotImplementedV1), Defined);
        Assert.Contains(nameof(DiagnosticCode.ObjectKeyCompositeNotImplementedV1), Defined);
    }

    [Fact]
    public void DiagnosticCode_WhenInspected_ThenEveryNameIsUnique() =>
        Assert.Equal(Defined.Length, Defined.Distinct(StringComparer.Ordinal).Count());
}
