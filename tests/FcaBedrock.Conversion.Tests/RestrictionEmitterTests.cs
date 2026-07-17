using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// <c>restrict_to</c> execution at emit (§10.4/D-091/D-097/D-105): existential matching over
/// formed objects, the G-2 sequencing against the object-key policies, and the D-097 diagnostic
/// ownership split.
/// <para>
/// Expected survivors are worked out <b>by hand</b> from the spec's rules in each test, never by
/// invoking the production matcher to build an expectation table — a test that asked the code
/// under test what the answer is would pass for any implementation.
/// </para>
/// </summary>
public sealed class RestrictionEmitterTests
{
    // --- string matching (§10.4/P-12) ---------------------------------------

    [Fact]
    public async Task Emit_WhenStringRestriction_ThenMatchesOrdinallyAndCaseSensitively()
    {
        // §10.4/P-12: ordinal, case-sensitive equality — no case folding, no locale, no regex.
        // Rows: "Bmp5" (match), "bmp5" (case differs → no), "BMP5" (no), "Wnt1" (other → no).
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, "Bmp5,x\nbmp5,x\nBMP5,x\nWnt1,x");

        Assert.Equal(["0"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Emit_WhenUnquotedFieldHasSurroundingWhitespace_ThenItIsTrimmedBeforeMatching()
    {
        // §5.1, which names restrict_to explicitly: leading/trailing whitespace around an
        // UNQUOTED data field is trimmed before any interpretation — including restriction
        // matching — so a value never fails to match purely because of spaces in the source.
        // Inside a QUOTED field the whitespace is preserved (deliberate spaces survive) and the
        // value genuinely differs, so it does not match.
        //
        // The spec-side entry is never trimmed either way: "Bmp5" is verbatim.
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, " Bmp5 ,x\n\" Bmp5\",x");

        Assert.Equal(["0"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Emit_WhenSeveralStringEntries_ThenTheyOrTogether()
    {
        // OR within an attribute (§10.4): a row matching ANY entry survives.
        var spec = Wide(
            Filter("Gene", 0, new RestrictToValue("Bmp5"), new RestrictToValue("Wnt1")),
            ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, "Bmp5,x\nShh,x\nWnt1,x");

        Assert.Equal(["0", "2"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Emit_WhenValueIsMissing_ThenItMatchesNothingAndTheObjectIsExcluded()
    {
        // "?" is the missing token → the cell is null → matches nothing (§10.4), silently.
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, "Bmp5,x\n?,x");

        Assert.Equal(["0"], objects.Select(o => o.Name));
    }

    // --- numeric matching (§10.4/D-091/G-6) ---------------------------------

    [Theory]
    [InlineData("30", true)]      // exact
    [InlineData("30.0", true)]    // same parsed identity, different spelling
    [InlineData("3e1", true)]     // ditto
    [InlineData("+30", true)]     // ditto
    [InlineData("30.5", false)]
    [InlineData("29.999999999", false)]  // no epsilon: near is not equal
    [InlineData("300", false)]
    [InlineData("abc", false)]    // unparseable → non-match
    [InlineData("NaN", false)]    // non-finite → non-match
    [InlineData("Infinity", false)]
    public async Task Emit_WhenExactNumericRestriction_ThenMatchesByParsedIdentityWithNoTolerance(string raw, bool survives)
    {
        // §10.4/D-091: the observation is parsed under the binding locale and compared by exact
        // numeric identity. Spelling collapses; nearness does not — 29.999999999 is NOT 30.
        var spec = Wide(FilterNumeric("age", 0, new RestrictToNumber(30)), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, $"{raw},x");

        Assert.Equal(survives ? 1 : 0, objects.Count);
    }

    [Theory]
    [InlineData("-0", true)]
    [InlineData("0", true)]
    [InlineData("0.0", true)]
    public async Task Emit_WhenExactZeroRestriction_ThenEveryZeroSpellingMatches(string raw, bool survives)
    {
        // G-6: the observation is zero-canonicalized after parsing, and the entry was
        // zero-canonicalized at the seam — so both signed zeros are one identity.
        var spec = Wide(FilterNumeric("age", 0, new RestrictToNumber(0)), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, $"{raw},x");

        Assert.Equal(survives ? 1 : 0, objects.Count);
    }

    [Theory]
    // Half-open [10, 20): low-inclusive, high-exclusive.
    [InlineData("9.999", false)]
    [InlineData("10", true)]      // low bound included
    [InlineData("15", true)]
    [InlineData("19.999", true)]
    [InlineData("20", false)]     // high bound excluded
    public async Task Emit_WhenBoundedRange_ThenLowIsInclusiveAndHighIsExclusive(string raw, bool survives)
    {
        var spec = Wide(FilterNumeric("age", 0, new RestrictToRange(10, 20)), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, $"{raw},x");

        Assert.Equal(survives ? 1 : 0, objects.Count);
    }

    [Theory]
    [InlineData("89.9", false)]
    [InlineData("90", true)]
    [InlineData("1e300", true)]
    public async Task Emit_WhenRangeIsOpenAbove_ThenOnlyTheLowBoundConstrains(string raw, bool survives)
    {
        var spec = Wide(FilterNumeric("age", 0, new RestrictToRange(90, null)), ConversionFixtures.Nominal("t", 1, "x"));

        Assert.Equal(survives ? 1 : 0, (await EmitAsync(spec, $"{raw},x")).Count);
    }

    [Theory]
    [InlineData("4.999", true)]
    [InlineData("5", false)]
    [InlineData("-1e300", true)]
    public async Task Emit_WhenRangeIsOpenBelow_ThenOnlyTheHighBoundConstrains(string raw, bool survives)
    {
        var spec = Wide(FilterNumeric("age", 0, new RestrictToRange(null, 5)), ConversionFixtures.Nominal("t", 1, "x"));

        Assert.Equal(survives ? 1 : 0, (await EmitAsync(spec, $"{raw},x")).Count);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("-1e300", true)]
    [InlineData("1e300", true)]
    [InlineData("abc", false)]     // unparseable is not USABLE
    [InlineData("NaN", false)]     // non-finite is not usable
    [InlineData("?", false)]       // missing matches nothing
    public async Task Emit_WhenEmptyRange_ThenItMatchesAnyUsableNumericValueOnly(string raw, bool survives)
    {
        // §10.4: {} matches any usable numeric value — equivalently, it excludes only
        // missing/unparseable values. It is NOT "match everything".
        var spec = Wide(FilterNumeric("age", 0, new RestrictToRange(null, null)), ConversionFixtures.Nominal("t", 1, "x"));

        Assert.Equal(survives ? 1 : 0, (await EmitAsync(spec, $"{raw},x")).Count);
    }

    [Fact]
    public async Task Emit_WhenExactAndRangeEntriesMix_ThenTheyOrTogether()
    {
        // §10.4: exact and range entries coexist freely on one numeric attribute.
        var spec = Wide(
            FilterNumeric("age", 0, new RestrictToNumber(30), new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 1, "x"));

        // 30 → exact; 15 → range; 25 → neither; 20 → range's high bound is exclusive.
        var objects = await EmitAsync(spec, "30,x\n15,x\n25,x\n20,x");

        Assert.Equal(["0", "1"], objects.Select(o => o.Name));
    }

    [Theory]
    // Under de-DE ',' is the decimal separator, so "1,5" is 1.5 — but the field would then split
    // on the comma, so the discriminating vector is the DECIMAL POINT: NumberStyles.Float allows
    // no group separators, so "1.5" does not parse under de-DE at all and cannot match.
    [InlineData("invariant", "1.5", true)]
    [InlineData("de-DE", "1.5", false)]
    // …while an integer parses identically under both, proving the locale change is not simply
    // breaking everything.
    [InlineData("invariant", "2", true)]
    [InlineData("de-DE", "2", true)]
    public async Task Emit_WhenNumericRestrictionUnderALocale_ThenObservationsParseUnderThatLocale(
        string locale, string raw, bool survives)
    {
        // §5.1/§10.4/P-11: the observation parses under binding.locale — derived once per emit,
        // never ambient. A discriminating vector, so an invariant-hardcoded matcher fails here.
        var binding = ConversionFixtures.Wide(hasHeader: false) with { Locale = locale };
        var spec = new BedrockSpec(binding,
        [
            new AttributeSpec("age", new ColumnSource(0, SourceValueType.Number), Include: false, null, null, [],
                [new RestrictToRange(1, 3)], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Skip),
            ConversionFixtures.Nominal("t", 1, "x"),
        ]);

        var objects = await EmitAsync(spec, $"{raw},x");

        Assert.Equal(survives ? 1 : 0, objects.Count);
    }

    // --- AND across attributes ----------------------------------------------

    [Fact]
    public async Task Emit_WhenSeveralAttributesRestrict_ThenTheyAndTogether()
    {
        // §10.1/§10.4: an object failing ANY attribute's restriction is excluded.
        var spec = Wide(
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            FilterNumeric("age", 1, new RestrictToRange(10, 20)),
            ConversionFixtures.Nominal("t", 2, "x"));

        // both / gene-only / age-only / neither
        var objects = await EmitAsync(spec, "Bmp5,15,x\nBmp5,50,x\nWnt1,15,x\nWnt1,50,x");

        Assert.Equal(["0"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Emit_WhenSurvivingObject_ThenItKeepsEveryCrossNotJustMatchingObservations()
    {
        // §10.4/D-097, the headline rule: restrictions filter OBJECTS, not observations. The
        // surviving object keeps all its crosses — including the ones from attributes that had
        // nothing to do with the filter.
        var spec = Wide(
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("tissue", 1, "endoderm", "mesoderm"));

        var objects = await EmitAsync(spec, "Bmp5,mesoderm");

        // tissue plans two columns (endoderm=0, mesoderm=1); the survivor crosses mesoderm.
        Assert.Equal([1], Assert.Single(objects).CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task Emit_WhenIncludedAttributeIsAlsoRestricted_ThenItStillContributesItsColumns()
    {
        // An included-AND-restricted attribute is not filter-only: it emits its column AND filters.
        var spec = Wide(
            ConversionFixtures.Nominal("tissue", 0, "endoderm", "mesoderm") with
            {
                RestrictTo = [new RestrictToValue("endoderm")],
            });

        var objects = await EmitAsync(spec, "endoderm\nmesoderm");

        var survivor = Assert.Single(objects);
        Assert.Equal("0", survivor.Name);
        Assert.Equal([0], survivor.CrossedFormalAttributeIds); // still crosses its own column
    }

    // --- G-2: sequencing vs the object-key policies --------------------------

    [Fact]
    public async Task Emit_WhenRowIndexKeyAndRowsAreFiltered_ThenSurvivorsKeepTheirInputPositions()
    {
        // §5.4/G-2 (round-5 Medium-4): row_index names are SOURCE positions and filtering never
        // renumbers them. Row 0 is filtered and row 1 survives → the survivor is still "1", not
        // "0". A survivor-rank implementation would name it "0" and pass a weaker test.
        var spec = Wide(Filter("Gene", 0, new RestrictToValue("Bmp5")), ConversionFixtures.Nominal("t", 1, "x"));

        var objects = await EmitAsync(spec, "Wnt1,x\nBmp5,x\nShh,x\nBmp5,x");

        // Input positions 1 and 3 survive; positions 0 and 2 are filtered.
        Assert.Equal(["1", "3"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task Emit_WhenFilteredRowDuplicatesAKeyUnderFail_ThenItDoesNotTripTheDuplicateCheck()
    {
        // G-2: a non-surviving row is not an object, so it cannot duplicate one. The filtered row
        // repeats key "p1" — under `fail` that would halt if the check ran before the filter.
        var spec = WideKeyed(
            DuplicateObjectPolicy.Fail,
            Filter("Gene", 1, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 2, "x"));

        var (objects, diagnostics) = await EmitWithDiagnosticsAsync(spec, "p1,Bmp5,x\np1,Wnt1,x\np2,Bmp5,x");

        Assert.Equal(["p1", "p2"], objects.Select(o => o.Name));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey);
    }

    [Fact]
    public async Task Emit_WhenFilteredRowSharesAKeyUnderKeep_ThenItConsumesNoAssignedName()
    {
        // §6.1/G-2: keep names are assigned in EMISSION order, so a filtered row consumes no
        // assigned name and produces no suffix and no diagnostic. Here the FILTERED row is the
        // first occurrence of "p1" — if it had consumed the name, the survivor would be renamed
        // "p1#2" instead of taking the bare key.
        var spec = WideKeyed(
            DuplicateObjectPolicy.Keep,
            Filter("Gene", 1, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 2, "x"));

        var (objects, diagnostics) = await EmitWithDiagnosticsAsync(spec, "p1,Wnt1,x\np1,Bmp5,x");

        Assert.Equal(["p1"], objects.Select(o => o.Name));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectKeyNameDisambiguated);
    }

    [Fact]
    public async Task Emit_WhenSurvivingRowsShareAKeyUnderKeep_ThenTheSuffixUsesTheSourceRecordIndex()
    {
        // The complement: two SURVIVORS sharing a key do disambiguate, and the suffix is the
        // 0-based SOURCE record index (§6.1) — record 2, not survivor rank 1.
        var spec = WideKeyed(
            DuplicateObjectPolicy.Keep,
            Filter("Gene", 1, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 2, "x"));

        var (objects, diagnostics) = await EmitWithDiagnosticsAsync(spec, "p1,Bmp5,x\np9,Wnt1,x\np1,Bmp5,x");

        Assert.Equal(["p1", "p1#2"], objects.Select(o => o.Name));
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey && d.Severity == DiagnosticSeverity.Warning);
    }

    // --- G-2: wide dedupe ----------------------------------------------------

    [Fact]
    public async Task EmitDedupe_WhenALaterMergedRowSuppliesTheMatch_ThenTheWholeObjectSurvives()
    {
        // §6.1/§10.4/G-2: grouping strictly PRECEDES filtering, and the restriction is evaluated
        // existentially over the MERGED observations. The match arrives on the group's SECOND row
        // — an implementation that filtered rows before grouping would drop the first row, and one
        // that only checked the first row would drop the group entirely.
        var spec = WideKeyed(
            DuplicateObjectPolicy.Dedupe,
            Filter("Gene", 1, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 2, "x", "y"));

        var (objects, _) = await EmitWithDiagnosticsAsync(spec, "p1,Wnt1,x\np1,Bmp5,y\np2,Shh,x");

        // p1 survives on its second row's Gene and keeps BOTH rows' crosses (x=0 and y=1);
        // p2 never matches and is dropped.
        var survivor = Assert.Single(objects);
        Assert.Equal("p1", survivor.Name);
        Assert.Equal([0, 1], survivor.CrossedFormalAttributeIds);
    }

    [Fact]
    public async Task EmitDedupe_WhenAMergedObjectIsFiltered_ThenItsDuplicateInfoIsStillCountedPreFilter()
    {
        // G-2: the intake hook observes the RAW stream as it is grouped, so the aggregated
        // DuplicateObjectKey (Info) count is pre-filter — it reports what the INPUT contained,
        // which is what a duplicate-key report is for. p2's two rows merge and are then dropped
        // by the filter, and the merge is still counted.
        var spec = WideKeyed(
            DuplicateObjectPolicy.Dedupe,
            Filter("Gene", 1, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("t", 2, "x"));

        var (objects, diagnostics) = await EmitWithDiagnosticsAsync(spec, "p1,Bmp5,x\np2,Wnt1,x\np2,Wnt1,x");

        Assert.Equal(["p1"], objects.Select(o => o.Name)); // p2 filtered out entirely

        var info = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey);
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Contains("1 record(s)", info.Message, StringComparison.Ordinal); // p2's second row
    }

    // --- G-2: triple, both orderings ----------------------------------------

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenSubjectGroupCloses_ThenTheRestrictionDecidesEmission(TripleOrdering ordering)
    {
        // §10.4/G-2: the subject's COMPLETE group is the formed object, so emission is decided at
        // group close — with every observation seen. Both orderings behave identically.
        var spec = Triple(ordering,
            TripleFilter("Gene", "Gene", new RestrictToValue("Bmp5")),
            ConversionFixtures.PredicateNominal("Tissue", "Tissue", ["endoderm"]));

        var data = ordering == TripleOrdering.SubjectGrouped
            ? "s1,Gene,Bmp5\ns1,Tissue,endoderm\ns2,Gene,Wnt1\ns2,Tissue,endoderm"
            : "s1,Gene,Bmp5\ns2,Gene,Wnt1\ns1,Tissue,endoderm\ns2,Tissue,endoderm";

        var objects = await EmitTripleAsync(spec, data, ordering);

        Assert.Equal(["s1"], objects.Select(o => o.Name));
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenTheRestrictedPredicateIsAbsent_ThenTheSubjectFails(TripleOrdering ordering)
    {
        // §10.4: an ABSENT predicate supplies no observation at all — distinct from a present
        // missing value — so its restriction never matches and the object is excluded. s2 has a
        // Tissue but no Gene.
        var spec = Triple(ordering,
            TripleFilter("Gene", "Gene", new RestrictToValue("Bmp5")),
            ConversionFixtures.PredicateNominal("Tissue", "Tissue", ["endoderm"]));

        var data = ordering == TripleOrdering.SubjectGrouped
            ? "s1,Gene,Bmp5\ns1,Tissue,endoderm\ns2,Tissue,endoderm"
            : "s1,Gene,Bmp5\ns2,Tissue,endoderm\ns1,Tissue,endoderm";

        var objects = await EmitTripleAsync(spec, data, ordering);

        Assert.Equal(["s1"], objects.Select(o => o.Name));
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenAPredicateIsMultiValued_ThenMatchingIsExistentialAndCrossesUnion(TripleOrdering ordering)
    {
        // §5.3.1/§10.4: a subject may carry SEVERAL values for one predicate. The restriction ORs
        // across them (one match is enough), and the object keeps the full union of crosses — a
        // single-valued fixture could not tell existential matching from first-value matching.
        var spec = Triple(ordering,
            TripleFilter("Gene", "Gene", new RestrictToValue("Bmp5")),
            ConversionFixtures.PredicateNominal("Tissue", "Tissue", ["endoderm", "mesoderm"]));

        // s1 has Gene = {Wnt1, Bmp5} — the MATCH is the second value — and two Tissues.
        // s2 has Gene = {Wnt1, Shh} — neither matches.
        var data = ordering == TripleOrdering.SubjectGrouped
            ? "s1,Gene,Wnt1\ns1,Gene,Bmp5\ns1,Tissue,endoderm\ns1,Tissue,mesoderm\ns2,Gene,Wnt1\ns2,Gene,Shh\ns2,Tissue,endoderm"
            : "s1,Gene,Wnt1\ns2,Gene,Wnt1\ns1,Gene,Bmp5\ns2,Gene,Shh\ns1,Tissue,endoderm\ns2,Tissue,endoderm\ns1,Tissue,mesoderm";

        var objects = await EmitTripleAsync(spec, data, ordering);

        var survivor = Assert.Single(objects);
        Assert.Equal("s1", survivor.Name);
        Assert.Equal([0, 1], survivor.CrossedFormalAttributeIds); // union of both tissues
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenNumericRestrictions_ThenExactRangeAndEmptyRangeAllMatchAsOnWide(TripleOrdering ordering)
    {
        // The shared matcher must behave identically on the triple paths — exact identity,
        // half-open ranges, {} as any-usable-numeric, and unparseable/missing as non-matches. The
        // wide vectors above cannot prove that: they never route through ObserveTriple.
        //
        // Truth worked out by hand, per subject:
        //   s1: exact 30 ✓, range [10,20) via 15 ✓, {} via 7 ✓          → survives
        //   s2: exact 31 ✗                                              → fails on `exact`
        //   s3: exact 30 ✓, range 20 ✗ (high bound exclusive)           → fails on `bounded`
        //   s4: exact 30 ✓, range 15 ✓, {} "abc" unparseable ✗          → fails on `usable`
        var spec = Triple(ordering,
            TripleFilterNumeric("exact", "exact", new RestrictToNumber(30)),
            TripleFilterNumeric("bounded", "bounded", new RestrictToRange(10, 20)),
            TripleFilterNumeric("usable", "usable", new RestrictToRange(null, null)),
            ConversionFixtures.PredicateNominal("t", "t", ["x"]));

        const string grouped =
            "s1,exact,30.0\ns1,bounded,15\ns1,usable,7\ns1,t,x\n" +
            "s2,exact,31\ns2,bounded,15\ns2,usable,7\ns2,t,x\n" +
            "s3,exact,3e1\ns3,bounded,20\ns3,usable,7\ns3,t,x\n" +
            "s4,exact,30\ns4,bounded,15\ns4,usable,abc\ns4,t,x";
        const string interleaved =
            "s1,exact,30.0\ns2,exact,31\ns3,exact,3e1\ns4,exact,30\n" +
            "s1,bounded,15\ns2,bounded,15\ns3,bounded,20\ns4,bounded,15\n" +
            "s1,usable,7\ns2,usable,7\ns3,usable,7\ns4,usable,abc\n" +
            "s1,t,x\ns2,t,x\ns3,t,x\ns4,t,x";

        var objects = await EmitTripleAsync(spec, ordering == TripleOrdering.SubjectGrouped ? grouped : interleaved, ordering);

        Assert.Equal(["s1"], objects.Select(o => o.Name));
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenAMultiValuedNumericPredicateHasOneMatch_ThenTheSubjectSurvives(TripleOrdering ordering)
    {
        // Existential numeric matching over a multi-valued predicate: s1's SECOND stage is the
        // one in range. A first-value-only matcher would drop it.
        var spec = Triple(ordering,
            TripleFilterNumeric("stage", "stage", new RestrictToRange(3, 9)),
            ConversionFixtures.PredicateNominal("t", "t", ["x"]));

        var data = ordering == TripleOrdering.SubjectGrouped
            ? "s1,stage,12\ns1,stage,5\ns1,t,x\ns2,stage,12\ns2,stage,20\ns2,t,x"
            : "s1,stage,12\ns2,stage,12\ns1,stage,5\ns2,stage,20\ns1,t,x\ns2,t,x";

        var objects = await EmitTripleAsync(spec, data, ordering);

        Assert.Equal(["s1"], objects.Select(o => o.Name));
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public async Task EmitTriple_WhenSubjectsAreFiltered_ThenSurvivorsKeepFirstAppearanceOrder(TripleOrdering ordering)
    {
        // §17 r4: object order is first-appearance of each cleaned subject; filtering removes
        // objects but never reorders the survivors.
        var spec = Triple(ordering,
            TripleFilter("Gene", "Gene", new RestrictToValue("Bmp5")),
            ConversionFixtures.PredicateNominal("Tissue", "Tissue", ["endoderm"]));

        var data = ordering == TripleOrdering.SubjectGrouped
            ? "s1,Gene,Wnt1\ns2,Gene,Bmp5\ns3,Gene,Wnt1\ns4,Gene,Bmp5"
            : "s1,Gene,Wnt1\ns2,Gene,Bmp5\ns3,Gene,Wnt1\ns4,Gene,Bmp5";

        var objects = await EmitTripleAsync(spec, data, ordering);

        Assert.Equal(["s2", "s4"], objects.Select(o => o.Name));
    }

    // --- helpers ------------------------------------------------------------
    //
    // A filter-only attribute: include = false + restrict_to (§10.4). Its discretizer/scale are
    // parked (D-049), which is exactly why the plan carries the value type and policy on the
    // restriction itself.

    private static AttributeSpec Filter(
        string name, int index, params RestrictToEntry[] entries) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec FilterNumeric(
        string name, int index, params RestrictToEntry[] entries) =>
        new(name, new ColumnSource(index, SourceValueType.Number), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec TripleFilter(
        string name, string predicate, params RestrictToEntry[] entries) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    // Skip policy: these vectors are about MATCHING, so an unparseable observation must be a
    // silent non-match rather than dragging a diagnostic into the assertion.
    private static AttributeSpec TripleFilterNumeric(
        string name, string predicate, params RestrictToEntry[] entries) =>
        new(name, new PredicateSource(predicate, SourceValueType.Number), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Skip);

    private static BedrockSpec Wide(params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Wide(hasHeader: false), attributes);

    private static BedrockSpec WideKeyed(DuplicateObjectPolicy policy, params AttributeSpec[] attributes) =>
        new(ConversionFixtures.WideWithKey(0, policy, hasHeader: false), attributes);

    private static BedrockSpec Triple(TripleOrdering ordering, params AttributeSpec[] attributes) =>
        new(ConversionFixtures.Triple(ordering), attributes);

    private static async Task<List<EmittedObject>> EmitAsync(BedrockSpec spec, string csv) =>
        (await EmitWithDiagnosticsAsync(spec, csv)).Objects;

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)>
        EmitWithDiagnosticsAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan!, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }

    private static async Task<List<EmittedObject>> EmitTripleAsync(BedrockSpec spec, string data, TripleOrdering ordering)
    {
        _ = ordering; // the plan carries it; the source is shape-identical either way
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan!, source, new List<BedrockDiagnostic>()))
        {
            objects.Add(emitted);
        }

        return objects;
    }
}
