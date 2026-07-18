using FcaBedrock.Conversion;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The two structural checks that make a triple draft convertible (D-106/D-107/D-111): subject
/// usability under <b>both</b> orderings, and contiguity under an <b>explicitly selected</b>
/// <c>subject_grouped</c>.
/// <para>
/// <b>Why probe validates at all.</b> Probe authors no object-key configuration, so it would be
/// easy to argue subjects are none of its business. They are: the draft's fourth guarantee is
/// that the same source converts cleanly, and conversion halts on exactly these two conditions.
/// A probe that skipped them would hand back a draft whose own convert leg fails — the failure
/// mode D-107 exists to prevent.
/// </para>
/// <para>
/// Both reuse the existing structural codes rather than probe-specific twins: one condition, one
/// code, three phases (D-067/D-111). The last two tests here pin that reuse literally, by
/// comparing probe's diagnostic with the calibrator's over the same bad source.
/// </para>
/// </summary>
public sealed class ProbeTripleStructureTests
{
    private static SourceReadSettings Settings(TripleOrdering ordering) =>
        TripleProbeFixtures.TripleSettings(ordering: ordering);

    public static TheoryData<TripleOrdering> BothOrderings() =>
        new() { TripleOrdering.Unordered, TripleOrdering.SubjectGrouped };

    private static void AssertHalted(
        Diagnosed<SpecDocument> result, DiagnosticCode code, int recordIndex)
    {
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(recordIndex, diagnostic.Location?.RecordIndex);

        // No draft — not even a partial one built from the rows already observed.
        Assert.True(result.HasErrors);
        Assert.False(result.TryGetValue(out _));
        Assert.Null(result.Value);
    }

    // --- Subject usability, under both orderings ----------------------------------------------

    public static TheoryData<string, string?> UnusableSubjects() => new()
    {
        // Every shape the shared Core predicate rejects. Missing (null) covers the `?` token, an
        // empty cell, and a ragged row that never reached the subject column — the source
        // normalizes all three to null before probe sees them.
        { "missing", null },
        { "empty", "" },
        { "whitespace only", "   " },
        { "tab only", "\t" },
        { "carries a newline", "a\nb" },
        { "carries a control character", "ab" },
    };

    [Theory]
    [MemberData(nameof(UnusableSubjects))]
    public async Task ProbeTriple_WhenASubjectIsUnusableUnderUnordered_ThenHaltsWithObjectKeyValueInvalid(
        string because, string? subject)
    {
        Assert.NotEmpty(because);
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), (subject, "p", "b"), ("s3", "p", "c"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.Unordered));

        AssertHalted(result, DiagnosticCode.ObjectKeyValueInvalid, recordIndex: 1);
    }

    [Theory]
    [MemberData(nameof(UnusableSubjects))]
    public async Task ProbeTriple_WhenASubjectIsUnusableUnderSubjectGrouped_ThenHaltsTheSameWay(
        string because, string? subject)
    {
        // "Under both orderings" is normative (§7.1): grouping changes which SECOND check runs,
        // never whether subjects are validated at all.
        Assert.NotEmpty(because);
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), (subject, "p", "b"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.SubjectGrouped));

        AssertHalted(result, DiagnosticCode.ObjectKeyValueInvalid, recordIndex: 1);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSubjectIsUnusableOnTheVeryFirstRow_ThenHaltsImmediately()
    {
        var session = TripleProbeFixtures.Fake((null, "p", "a"), ("s2", "p", "b"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.Unordered));

        AssertHalted(result, DiagnosticCode.ObjectKeyValueInvalid, recordIndex: 0);
        Assert.Equal(1, session.RowsYielded);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSubjectIsUnusableOnARowWhosePredicateIsIgnored_ThenStillHalts()
    {
        // The check runs BEFORE predicate filtering, deliberately: conversion would halt on this
        // row regardless of whether its predicate names an attribute, so probe must too.
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), (null, null, null));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.Unordered));

        AssertHalted(result, DiagnosticCode.ObjectKeyValueInvalid, recordIndex: 1);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheSubjectIsUnusableThroughARealRead_ThenStillHalts()
    {
        // The same failure through the production adapter: `?` is the missing token, so the
        // subject cell normalizes to null and the halt is reachable from the shipping path.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,p,a\n?,p,b\n");

        AssertHalted(result, DiagnosticCode.ObjectKeyValueInvalid, recordIndex: 1);
    }

    [Fact]
    public async Task ProbeTriple_WhenSubjectsAreUsable_ThenNoStructuralDiagnosticIsRaised()
    {
        // The control for the whole suite.
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync("s1,p,a\ns2,p,b\n");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsOk);
    }

    // --- Contiguity, under subject_grouped only -----------------------------------------------

    [Fact]
    public async Task ProbeTriple_WhenSubjectGroupedAndSubjectsAreContiguous_ThenSucceeds()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,a\ns1,q,b\ns2,p,c\ns2,q,d\n", ordering: TripleOrdering.SubjectGrouped);

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
        Assert.Equal(["p", "q"], ProbeFixtures.Draft(result).Attributes.Select(a => a.Name));
    }

    [Fact]
    public async Task ProbeTriple_WhenSubjectGroupedAndASubjectRecurs_ThenHaltsWithNotContiguous()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,a\ns2,p,b\ns1,p,c\n", ordering: TripleOrdering.SubjectGrouped);

        AssertHalted(result, DiagnosticCode.TripleSubjectNotContiguous, recordIndex: 2);
    }

    [Fact]
    public async Task ProbeTriple_WhenAnInterveningRowsPredicateIsIgnored_ThenContiguityStillBreaks()
    {
        // Contiguity is judged over every VALID-SUBJECT row, including rows this probe otherwise
        // ignores. Judging it over observed rows only would accept input that conversion rejects.
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), ("s2", null, "b"), ("s1", "p", "c"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.SubjectGrouped));

        AssertHalted(result, DiagnosticCode.TripleSubjectNotContiguous, recordIndex: 2);
    }

    [Fact]
    public async Task ProbeTriple_WhenAnInterveningRowsValueIsMissing_ThenContiguityStillBreaks()
    {
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), ("s2", "p", null), ("s1", "p", "c"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.SubjectGrouped));

        AssertHalted(result, DiagnosticCode.TripleSubjectNotContiguous, recordIndex: 2);
    }

    [Fact]
    public async Task ProbeTriple_WhenUnordered_ThenTheSameInterleavingIsAccepted()
    {
        // The contrast that gives the check its meaning: interleaved subjects are LEGAL under
        // `unordered` (§5.3), and probe runs no grouping pass to make them contiguous — one read,
        // set-based observation, no complaint.
        var session = TripleProbeFixtures.Fake(("s1", "p", "a"), ("s2", "p", "b"), ("s1", "q", "c"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.Unordered));

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
        Assert.Equal(1, session.RowEnumerations);
        Assert.Equal(["p", "q"], ProbeFixtures.Draft(result).Attributes.Select(a => a.Name));
    }

    [Fact]
    public async Task ProbeTriple_WhenSubjectGroupedAndOnlyOneSubjectExists_ThenSucceeds()
    {
        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,a\ns1,p,b\ns1,q,c\n", ordering: TripleOrdering.SubjectGrouped);

        Assert.True(result.IsOk, ProbeFixtures.Describe(result.Diagnostics));
    }

    [Fact]
    public async Task ProbeTriple_WhenContiguityBreaksAfterPartialObservation_ThenNoDraftLeaks()
    {
        // Real state has already been accumulated when the halt lands, which is exactly when
        // returning "what we have" would be tempting and wrong (D-112).
        var session = TripleProbeFixtures.Fake(
            ("s1", "p", "a"), ("s1", "q", "b"), ("s2", "p", "c"), ("s1", "r", "d"), ("s3", "p", "e"));

        var result = await Prober.ProbeTripleAsync(session, Settings(TripleOrdering.SubjectGrouped));

        AssertHalted(result, DiagnosticCode.TripleSubjectNotContiguous, recordIndex: 3);
        Assert.Equal(4, session.RowsYielded);
    }

    [Fact]
    public async Task ProbeTriple_WhenTheOrderingIsUnordered_ThenTheDraftNeverClaimsSubjectGrouped()
    {
        // `subject_grouped` is explicit-only (§7.1): observing contiguous subjects must NOT
        // promote an unordered probe, or a redraft would silently acquire a constraint the caller
        // never asked for.
        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            "s1,p,a\ns1,q,b\ns2,p,c\n"));

        Assert.Equal(TripleOrdering.Unordered, draft.Binding!.Ordering);
    }

    // --- Symmetry with conversion, literally --------------------------------------------------

    [Fact]
    public async Task ProbeTriple_WhenASubjectIsUnusable_ThenTheDiagnosticMatchesTheCalibratorsExactly()
    {
        // The reuse claim, checked rather than described: same code, same severity, same message,
        // same record-index location as the calibrate site over the same source. Two phases
        // reporting one condition differently would be a taxonomy nobody asked for (D-067).
        const string csv = "s1,p,a\n?,p,b\n";

        var probed = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);
        var calibrated = await CalibrateAsync(csv, TripleOrdering.Unordered);

        Assert.Equal(
            Assert.Single(calibrated, d => d.Code == DiagnosticCode.ObjectKeyValueInvalid),
            Assert.Single(probed.Diagnostics));
    }

    [Fact]
    public async Task ProbeTriple_WhenContiguityBreaks_ThenTheDiagnosticMatchesTheCalibratorsExactly()
    {
        const string csv = "s1,p,a\ns2,p,b\ns1,p,c\n";

        var probed = await TripleProbeFixtures.ProbeTripleCsvAsync(
            csv, ordering: TripleOrdering.SubjectGrouped);
        var calibrated = await CalibrateAsync(csv, TripleOrdering.SubjectGrouped);

        Assert.Equal(
            Assert.Single(calibrated, d => d.Code == DiagnosticCode.TripleSubjectNotContiguous),
            Assert.Single(probed.Diagnostics));
    }

    // Calibrates a minimal triple spec over the same bytes, so the two phases' structural
    // diagnostics can be compared directly. Test-only Conversion reference (D-109).
    private static async Task<IReadOnlyList<BedrockDiagnostic>> CalibrateAsync(
        string csv, TripleOrdering ordering)
    {
        var document = new SpecDocument(
            new SpecSection(1, null, null, null, null, null),
            null,
            new BindingSection(SourceShape.Triple, null, null, null, null, null, null, ordering, null, null),
            null, null, [], [],
            [
                new AttributeSection(
                    "p", new PredicateSourceSection("p", SourceValueType.String), null, null, null,
                    new IdentityDiscretizerSection(), new NominalScaleSection(),
                    null, null, null, null, null),
            ]);

        var settings = SpecResolver.ResolveReadSettings(document).Value!;
        var session = new TripleCsvSession(TripleProbeFixtures.Bytes(csv), settings);
        var resolved = SpecResolver.Resolve(document, await session.GetSchemaAsync()).Value!;
        var calibrated = await Calibrator.CalibrateTripleAsync(resolved.Resolved, session.Bind(resolved.Resolved));

        return calibrated.Diagnostics;
    }
}
