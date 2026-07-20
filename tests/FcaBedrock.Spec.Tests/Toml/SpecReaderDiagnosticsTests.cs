using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Reader diagnostics tests (D-075): every parse-phase code with positions,
/// the D-070 three-tier discretizer dispatch, the retired deferred-surface set
/// (a clean read per naming key per owning table, plus the near-miss negatives
/// that must stay <c>SpecKeyUnrecognized</c>), and whole-read aggregation.
/// </summary>
public sealed class SpecReaderDiagnosticsTests
{
    [Fact]
    public void Read_WhenTomlSyntaxBroken_ThenSpecTomlInvalidFatalWithPosition()
    {
        var result = SpecReader.Read("[spec\nversion = 1\n", "broken.toml");

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == DiagnosticCode.SpecTomlInvalid &&
            d.Severity == DiagnosticSeverity.Fatal &&
            d.Location is { File: "broken.toml", Line: 1 });
    }

    [Fact]
    public void Read_WhenKeyDuplicated_ThenSpecTomlInvalid()
    {
        var result = SpecReader.Read("[spec]\nversion = 1\nversion = 2\n");

        AssertFailsWith(result, DiagnosticCode.SpecTomlInvalid);
    }

    [Fact]
    public void Read_WhenTableDuplicated_ThenSpecTomlInvalid()
    {
        var result = SpecReader.Read("[spec]\nversion = 1\n[spec]\ndescription = \"x\"\n");

        AssertFailsWith(result, DiagnosticCode.SpecTomlInvalid);
    }

    [Fact]
    public void Read_WhenKeyUnknown_ThenSpecKeyUnrecognizedWithPosition()
    {
        var result = SpecReader.Read("[binding]\nshape = \"wide\"\nmissing_polcy = \"skip\"\n", "spec.toml");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
        Assert.Contains("missing_polcy", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("spec.toml", diagnostic.Location?.File);
        Assert.Equal(3, diagnostic.Location?.Line);
        Assert.Equal(1, diagnostic.Location?.Column);
    }

    [Fact]
    public void Read_WhenTableUnknown_ThenSpecKeyUnrecognized()
    {
        AssertFailsWith(SpecReader.Read("[bindings]\nshape = \"wide\"\n"), DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenRootLevelKey_ThenSpecKeyUnrecognized()
    {
        AssertFailsWith(SpecReader.Read("version = 1\n"), DiagnosticCode.SpecKeyUnrecognized);
    }

    [Theory]
    [InlineData("[spec]\nversion = 1.0\n")]
    [InlineData("[binding]\nhas_header = \"true\"\n")]
    [InlineData("[binding]\ndelimiter = \", \"\n")]
    [InlineData("[binding]\ndelimiter = \"\"\n")]
    [InlineData("[provenance]\ncreated_at = \"2026-05-09\"\n")]
    public void Read_WhenKnownKeyHasWrongShape_ThenSpecFieldInvalid(string toml)
    {
        AssertFailsWith(SpecReader.Read(toml), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenEnumSpellingUnknown_ThenSpecFieldInvalidNamesAllowedSpellings()
    {
        var result = SpecReader.Read("[binding]\nshape = \"wibble\"\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("\"wide\" or \"triple\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("identity", "")]
    [InlineData("free_per_value", "")]
    [InlineData("manual_cuts", ", cuts = [30]")]
    [InlineData("ordered_cuts", ", order = [\"a\", \"b\"], cuts = [\"b\"]")]
    [InlineData("equal_width", ", bins = 4")]
    [InlineData("equal_frequency", ", bins = 4")]
    [InlineData("value_groups", ", groups = [{ label = \"g\", values = [\"a\"] }]")]
    public void Read_WhenAnyV1DiscretizerKindIsWellFormed_ThenItReadsCleanToItsCarrier(string kind, string parameters)
    {
        // D-070's tier-2 deferred-kind reject retired entirely at M4 Slice E (D-104): EVERY v1
        // discretizer kind now has a carrier, so a well-formed one of each must read clean.
        // Asserting a clean read of each kind — rather than the absence of a code that no longer
        // exists — is what keeps this test able to fail: re-deferring any kind would break it.
        var result = SpecReader.Read(Attribute($"discretizer = {{ kind = \"{kind}\"{parameters} }}"));

        Assert.True(result.IsOk, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Value!.Attributes[0].Discretizer);
    }

    [Fact]
    public void Read_WhenDiscretizerKindUnknown_ThenGenericFieldInvalid()
    {
        // D-070 tier 3: a typo gets the generic code, not the transitional one.
        AssertFailsWith(
            SpecReader.Read(Attribute("discretizer = { kind = \"identty\" }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenScaleKindUnknown_ThenGenericFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute("scale = { kind = \"ordnal\" }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenDefaultsAuthorsNameFormat_ThenItIsCarriedNotRejected()
    {
        // D-120: the [defaults] half of the retired deferred set. Asserting a clean read
        // AND the carried value — rather than the absence of a code that no longer has
        // this owner — is what keeps the retirement lock able to fail (the substitution
        // Slice E made when the deferred-discretizer set retired, D-104).
        var result = SpecReader.Read("[defaults]\nformal_attribute_format = \"{value}\"\n");

        Assert.True(result.IsOk, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(result.Diagnostics);
        Assert.Equal("{value}", result.Value!.Defaults!.FormalAttributeFormat);
    }

    [Fact]
    public void Read_WhenAttributeAuthorsNamingKeys_ThenBothAreCarriedNotRejected()
    {
        var result = SpecReader.Read(
            Attribute("display_name = \"Education\"\nformal_attribute_format = \"{display_name}::{value}\""));

        Assert.True(result.IsOk, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(result.Diagnostics);
        var attribute = result.Value!.Attributes[0];
        Assert.Equal("Education", attribute.DisplayName);
        Assert.Equal("{display_name}::{value}", attribute.FormalAttributeFormat);
    }

    [Fact]
    public void Read_WhenTemplateAuthorsNamingKeys_ThenBothAreCarriedNotRejected()
    {
        // §9.1: a template body is the attribute config surface, so the naming keys carry
        // and validate there on exactly the same terms as on an attribute — including
        // inside a template nothing references, since shape is parse's concern while
        // semantic dormancy is the resolver's (§10.7/D-049).
        var result = SpecReader.Read(
            "[[template]]\nid = \"t\"\ndisplay_name = \"Boolean\"\nformal_attribute_format = \"{column}-{value}\"\n");

        Assert.True(result.IsOk, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(result.Diagnostics);
        var template = result.Value!.Templates[0];
        Assert.Equal("Boolean", template.DisplayName);
        Assert.Equal("{column}-{value}", template.FormalAttributeFormat);
    }

    [Theory]
    [InlineData("display_name = \"\"")]                              // §10.1: an authored one must be non-empty
    [InlineData("display_name = \"one\\ntwo\"")]                     // §10.1: no CR/LF — .cxt is line-oriented
    [InlineData("display_name = 7")]                                 // wrong type
    [InlineData("formal_attribute_format = \"\"")]                   // §10.7: the whole format must be non-empty
    [InlineData("formal_attribute_format = \"{Value}\"")]            // case variant of a closed-set placeholder
    [InlineData("formal_attribute_format = \"{scale}\"")]            // there is no {scale}
    [InlineData("formal_attribute_format = \"{}\"")]                 // empty placeholder
    [InlineData("formal_attribute_format = \"{name\"")]              // unmatched {
    [InlineData("formal_attribute_format = \"name}\"")]              // unmatched }
    [InlineData("formal_attribute_format = \"a\\nb{name}\"")]        // CR/LF in literal text
    [InlineData("formal_attribute_format = 7")]                      // wrong type
    public void Read_WhenNamingKeyMalformed_ThenSpecFieldInvalid(string line)
    {
        // §10.7/§16.4: every naming-shape failure is the ordinary SpecFieldInvalid —
        // D-116 deliberately mints no format-specific or display-name-specific code.
        var result = SpecReader.Read(Attribute(line));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Read_WhenExcludedAttributeAuthorsBadFormat_ThenStillSpecFieldInvalid()
    {
        // §10.7: checked wherever authored, INCLUDING on an excluded attribute — authored
        // shape is the parser's concern while dormancy is semantic (the D-049 split).
        var result = SpecReader.Read(Attribute("include = false\nformal_attribute_format = \"{nope}\""));

        Assert.Equal(DiagnosticCode.SpecFieldInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Read_WhenUnusedTemplateAuthorsBadFormat_ThenStillSpecFieldInvalid()
    {
        // §9.2/§10.7: an unused template is semantically dormant, but parse-level shape
        // checks and the naming-format grammar still apply to its authored body.
        var result = SpecReader.Read("[[template]]\nid = \"t\"\nformal_attribute_format = \"{nope}\"\n");

        Assert.Equal(DiagnosticCode.SpecFieldInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("\"\"")]                      // empty
    [InlineData("\"1st\"")]                   // leading digit
    [InlineData("\"-lead\"")]                 // leading hyphen
    [InlineData("\"has space\"")]
    [InlineData("\"has.dot\"")]
    [InlineData("\"café\"")]                  // non-ASCII
    [InlineData("7")]                         // not a string at all
    public void Read_WhenTemplateIdIsMalformed_ThenSpecFieldInvalidAtParse(string id)
    {
        // §9.1: unlike an attribute `name` (free text, §10.1), a template `id` is
        // referenced BY NAME from matchers and attributes, so it takes the stricter
        // [A-Za-z_][A-Za-z0-9_-]* form. A malformed id is static authored shape and so is
        // parse-owned under the ordinary SpecFieldInvalid — no id-specific parse code.
        var result = SpecReader.Read($"[[template]]\nid = {id}\n");

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("t")]
    [InlineData("_leading_underscore")]
    [InlineData("boolean_yes_no")]
    [InlineData("term-flag")]
    [InlineData("A1")]
    public void Read_WhenTemplateIdIsWellFormed_ThenItIsAccepted(string id)
    {
        // The permissive half of the same grammar, so the rule is pinned in both
        // directions rather than only rejecting.
        var result = SpecReader.Read($"[[template]]\nid = \"{id}\"\n");

        Assert.True(result.TryGetValue(out var document));
        Assert.Equal(id, Assert.Single(document.Templates).Id);
    }

    [Fact]
    public void Read_WhenTemplateIdIsOmitted_ThenParseIsSilentAndItBecomesAResolveCondition()
    {
        // §9.1 splits the two deliberately: a malformed id is a field SHAPE failure
        // (parse), while an omitted one is "this template is unreachable" — a
        // document-level condition the resolver owns as TemplateIdMissing (§16.4).
        var result = SpecReader.Read("[[template]]\ninclude = true\n");

        Assert.True(result.TryGetValue(out var document));
        Assert.Null(Assert.Single(document.Templates).Id);
    }

    [Theory]
    [InlineData("name = \"a\"")]
    [InlineData("source = { kind = \"column\", index = 0 }")]
    [InlineData("description = \"per-attribute only\"")]
    public void Read_WhenTemplateDeclaresPerAttributeField_ThenSpecKeyUnrecognized(string line)
    {
        // §9.1 forbids name/source/description in a template — the D-075
        // listed-name-in-the-wrong-table stance, not the transitional code.
        var result = SpecReader.Read($"[[template]]\nid = \"t\"\n{line}\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
    }

    [Fact]
    public void Read_WhenTemplateDeclaresValueGroups_ThenItCarriesLikeAnyAttributeDiscretizer()
    {
        // A template body is the attribute config surface (§9.1/D-078), so value_groups gains its
        // carrier there too at Slice E (D-104) — it is no longer rejected for its kind. An unknown
        // key inside it still gets the ordinary SpecKeyUnrecognized, which is exactly the noise the
        // old deferred-kind reject suppressed by not walking the parameters at all.
        var result = SpecReader.Read(
            "[[template]]\nid = \"t\"\ndiscretizer = { kind = \"value_groups\", " +
            "groups = [{ label = \"g\", values = [\"a\"] }], n = 4 }\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
        Assert.Contains("n", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenMatcherHasUnknownKey_ThenSpecKeyUnrecognized()
    {
        // The unknown key is still its own condition — asserted here alongside the two
        // shape failures this matcher genuinely also has, since `pattern` is not a
        // selector: it authors no `match` table and no `template` (§9.2, M6 Slice B).
        // Distinct conditions aggregate rather than masking one another (P-14).
        var result = SpecReader.Read("[[matcher]]\npattern = \"x\"\n");

        var unrecognized = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.SpecKeyUnrecognized);
        Assert.Contains("pattern", unrecognized.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenMatcherDeclaresNoSelectorOrTemplate_ThenBothShapeFailuresReport()
    {
        // §9.2's two static requirements, checked independently so a matcher missing both
        // tells its author about both in one read (P-14). Anchored at the table header,
        // since an ABSENT key has no span of its own.
        var result = SpecReader.Read("[[matcher]]\n");

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticCode.SpecFieldInvalid, d.Code));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("no match table", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("no template", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[template]\nid = \"t\"\n")]
    [InlineData("[matcher]\ntemplate = \"t\"\n")]
    public void Read_WhenTemplateOrMatcherWrittenAsSingleTable_ThenSpecFieldInvalid(string toml)
    {
        AssertFailsWith(SpecReader.Read(toml), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenValueTypeIsDate_ThenInterimSurfaceReject()
    {
        // D-038/D-075: reserved surface with no carrier; the v1 end-state is a
        // plan-phase DateValueTypeNotImplementedV1 once the carrier lands.
        AssertFailsWith(
            SpecReader.Read(Attribute(string.Empty, source: "{ kind = \"column\", index = 0, value_type = \"date\" }")),
            DiagnosticCode.SpecSurfaceNotYetSupported);
    }

    [Theory]
    [InlineData("display_nam = \"x\"")] // typo of a naming key
    [InlineData("formal_attribute_formatt = \"{value}\"")]
    [InlineData("extends = \"base.toml\"")] // real key, wrong table
    public void Read_WhenNearMissOfANamingKey_ThenSpecKeyUnrecognized(string line)
    {
        // D-075: the allow-list is exactly what a reader consumes, so a near-miss stays an
        // unknown key rather than being absorbed by the naming surface it resembles.
        var result = SpecReader.Read(Attribute(line));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
    }

    [Fact]
    public void Read_WhenDisplayNameUnderBinding_ThenSpecKeyUnrecognized()
    {
        var result = SpecReader.Read("[binding]\nshape = \"wide\"\ndisplay_name = \"x\"\n");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
    }

    [Fact]
    public void Read_WhenAttributeWrittenAsSingleTable_ThenSpecFieldInvalid()
    {
        AssertFailsWith(SpecReader.Read("[attribute]\nname = \"a\"\n"), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenSingleTableWrittenAsArray_ThenSpecFieldInvalid()
    {
        AssertFailsWith(SpecReader.Read("[[binding]]\nshape = \"wide\"\n"), DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenSeveralProblems_ThenAllAggregateInOnePass()
    {
        // P-14: one read reports everything — a bad shape spelling, an unknown key, and an
        // unrecognized discretizer kind together, each on its own condition.
        var result = SpecReader.Read(
            "[binding]\nshape = \"wibble\"\nmissing_polcy = \"skip\"\n" +
            "[[attribute]]\nname = \"a\"\ndiscretizer = { kind = \"wibble_bins\" }\n");

        Assert.False(result.IsOk);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenProblemsAreAuthoredAgainstTraversalOrder_ThenTheyReportInSourcePositionOrder()
    {
        // D-116/D-120: the semantic pass reports in SOURCE-POSITION order, applied once at
        // the SpecReader boundary rather than left as an artifact of traversal. Here the
        // bad keys are authored in reverse READER order — the reader takes name/source/
        // display_name/description before it ever reaches missing_policy — so a
        // traversal-ordered result would report line 8 before line 6. Position wins.
        var result = SpecReader.Read(
            "[spec]\nversion = 1\n\n[[attribute]]\nname = \"a\"\n"
            + "missing_policy = \"nope\"\n"                       // line 6
            + "source = { kind = \"column\", index = 0 }\n"
            + "display_name = \"\"\n");                           // line 8

        Assert.Equal([6, 8], result.Diagnostics.Select(d => d.Location?.Line ?? 0).ToArray());
    }

    [Fact]
    public void Read_WhenTwoProblemsShareOneSpan_ThenBothReportInEmissionOrder()
    {
        // The equal-position case reached through the PUBLIC surface: both of these
        // anchor on the same inline-table span, so only the emission-ordinal tie-break
        // decides their order. Asserted on the exact message sequence — asserting that
        // the positions are sorted would be a tautology, since swapping two diagnostics
        // at one position leaves the position list identical.
        var result = SpecReader.Read(Attribute("discretizer = { kind = \"equal_width\", vmin = 1, vmax = 2 }"));

        var spans = result.Diagnostics.Select(d => (d.Location?.Line, d.Location?.Column)).Distinct().ToArray();
        Assert.Single(spans); // precondition: every diagnostic really is at one position
        Assert.Equal(
            ["declares no bins", "declares vmin/vmax"],
            result.Diagnostics.Select(d => d.Message.Contains("declares no bins", StringComparison.Ordinal)
                ? "declares no bins"
                : "declares vmin/vmax").ToArray());
    }

    // --- The ordering policy itself (SpecReader.SortSemantic) ---
    //
    // Exercised directly with constructed diagnostics, because two of its guarantees
    // cannot be reached or observed through Read: TomlReadContext always attaches a span,
    // so the span-less branch has no authored input at all, and a two-element equal-position
    // case would survive even an unstable sort. A test that cannot fail is not a lock.

    [Fact]
    public void SortSemantic_WhenADiagnosticHasNoLocation_ThenItSortsFirst()
    {
        // Span-less (document-level) diagnostics compare as line 0, column 0 and therefore
        // lead, whatever their emission position.
        var diagnostics = new List<BedrockDiagnostic>
        {
            At("located-late", 5, 2),
            Unlocated("document-level"),
            At("located-early", 1, 1),
        };

        SpecReader.SortSemantic(diagnostics, 0);

        Assert.Null(diagnostics[0].Location); // precondition made explicit: it really has none
        Assert.Equal(["document-level", "located-early", "located-late"], Messages(diagnostics));
    }

    [Fact]
    public void SortSemantic_WhenSeveralDiagnosticsHaveNoLocation_ThenTheyLeadInEmissionOrder()
    {
        var diagnostics = new List<BedrockDiagnostic>
        {
            Unlocated("second"),
            At("located", 1, 1),
            Unlocated("first"),
        };

        SpecReader.SortSemantic(diagnostics, 0);

        // "second"/"first" name their EMISSION order, not their sorted order — so a
        // content-based reordering would be visible here.
        Assert.Equal(["second", "first", "located"], Messages(diagnostics));
    }

    [Fact]
    public void SortSemantic_WhenManyDiagnosticsShareOnePosition_ThenEmissionOrderSurvivesExactly()
    {
        // The emission ordinal is part of the comparison, not a convention — which makes
        // the order total, so it does not depend on the sort's stability. Twenty elements
        // is past the threshold where .NET's introsort stops being an insertion sort, so
        // dropping the tie-break would visibly reorder these rather than happening to work.
        // The messages descend, so any content-driven reordering is also visible.
        var diagnostics = new List<BedrockDiagnostic>();
        for (var i = 0; i < 20; i++)
        {
            diagnostics.Add(At($"m{19 - i:D2}", 7, 3));
        }

        var emitted = Messages(diagnostics);
        SpecReader.SortSemantic(diagnostics, 0);

        Assert.Equal(emitted, Messages(diagnostics));
        Assert.Equal("m19", diagnostics[0].Message);
        Assert.Equal("m00", diagnostics[^1].Message);
    }

    [Fact]
    public void SortSemantic_WhenPositionsDiffer_ThenLineThenColumnAscending()
    {
        var diagnostics = new List<BedrockDiagnostic>
        {
            At("l9c1", 9, 1),
            At("l2c9", 2, 9),
            At("l2c1", 2, 1),
            At("l9c0", 9, 0),
        };

        SpecReader.SortSemantic(diagnostics, 0);

        Assert.Equal(["l2c1", "l2c9", "l9c0", "l9c1"], Messages(diagnostics));
    }

    [Fact]
    public void SortSemantic_WhenAppliedFromAnOffset_ThenEarlierDiagnosticsKeepTheirPlace()
    {
        // The phase boundary: the sort is scoped to the semantic range, so phase-1 parser
        // warnings stay ahead of every semantic diagnostic no matter where they sit.
        var diagnostics = new List<BedrockDiagnostic>
        {
            At("parser-warning-late", 99, 1),
            At("semantic-late", 5, 1),
            At("semantic-early", 2, 1),
        };

        SpecReader.SortSemantic(diagnostics, from: 1);

        Assert.Equal(["parser-warning-late", "semantic-early", "semantic-late"], Messages(diagnostics));
    }

    private static BedrockDiagnostic At(string message, int line, int column) =>
        new(DiagnosticCode.SpecFieldInvalid, DiagnosticSeverity.Error, message, new DiagnosticLocation(Line: line, Column: column));

    private static BedrockDiagnostic Unlocated(string message) =>
        new(DiagnosticCode.SpecFieldInvalid, DiagnosticSeverity.Error, message);

    private static string[] Messages(List<BedrockDiagnostic> diagnostics) =>
        diagnostics.Select(d => d.Message).ToArray();

    [Fact]
    public void Read_WhenReadTwice_ThenTheOrderedDiagnosticListIsIdentical()
    {
        // P-7 at the parse boundary: same document ⇒ same ordered diagnostics, so tooling
        // that prints or hashes them cannot see run-to-run drift.
        const string Toml =
            "[binding]\nshape = \"wibble\"\nmissing_polcy = \"skip\"\n"
            + "[[attribute]]\nname = \"a\"\ndisplay_name = \"\"\nformal_attribute_format = \"{nope}\"\n";

        Assert.Equal(
            SpecReader.Read(Toml).Diagnostics.Select(d => (d.Code, d.Location?.Line, d.Location?.Column)).ToArray(),
            SpecReader.Read(Toml).Diagnostics.Select(d => (d.Code, d.Location?.Line, d.Location?.Column)).ToArray());
    }

    [Fact]
    public void Read_WhenTomlSyntaxIsBroken_ThenTheSemanticPassNeverRuns()
    {
        // The phase boundary the ordering policy must not disturb (D-075): syntax errors
        // are TERMINAL, so no semantic diagnostic is produced to be sorted alongside them.
        var result = SpecReader.Read("[spec\nversion = 1\ndisplay_name = \"\"\n");

        Assert.False(result.IsOk);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticCode.SpecTomlInvalid, d.Code));
    }

    [Fact]
    public void Read_WhenProblemInsideAttribute_ThenDiagnosticCarriesAttributeScope()
    {
        var result = SpecReader.Read(Attribute("wibble = 1"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
        Assert.NotNull(diagnostic.Location?.Line);
    }

    [Fact]
    public void Read_WhenSourceDeclaresNoKind_ThenSpecFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute(string.Empty, source: "{ index = 0 }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenValueLabelValueNotString_ThenSpecFieldInvalid()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute("value_labels = { b = 1 }")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenRestrictToEntryHasWrongShape_ThenSpecFieldInvalid()
    {
        // A BARE number is not an approved entry form: numeric exactness is spelled
        // { value = n } (§10.4), so `[10]` is an invalid entry rather than an exact 10.
        AssertFailsWith(
            SpecReader.Read(Attribute("restrict_to = [10]")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Theory]
    [InlineData("\"thirty\"")]        // string
    [InlineData("true")]              // boolean
    [InlineData("[30]")]              // array
    [InlineData("{ n = 30 }")]        // nested table
    public void Read_WhenExactRestrictValueIsNotNumeric_ThenSpecFieldInvalid(string value)
    {
        // The reported condition must be "this exact entry's value is not a number", raised by
        // the ordinary numeric accessor — never a leaked factory/parser exception (P-14).
        AssertFailsWith(
            SpecReader.Read(Attribute($"restrict_to = [{{ value = {value} }}]")),
            DiagnosticCode.SpecFieldInvalid);
    }

    [Fact]
    public void Read_WhenExactRestrictValueIsMalformed_ThenItDoesNotDegradeIntoAnUnrestrictedRange()
    {
        // The silent-widening trap this shape-first reader exists to avoid. Reading
        // { value = "x" } as a RANGE would take neither `from` nor `to` and yield
        // RestrictToRange(null, null) — the {} entry, which matches EVERY usable numeric value.
        // A typo'd filter would then keep every object instead of failing loudly.
        var result = SpecReader.Read(Attribute("restrict_to = [{ value = \"thirty\" }]"));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecFieldInvalid);

        // The entry is dropped, not silently turned into a match-everything range.
        if (result.TryGetValue(out var document))
        {
            Assert.DoesNotContain(
                document.Attributes[0].RestrictTo ?? [],
                e => e is RestrictToRange { From: null, To: null });
        }
    }

    [Fact]
    public void Read_WhenExactEntryAlsoCarriesRangeKeys_ThenTheStrayKeysAreUnrecognized()
    {
        // Shape is chosen by the presence of `value`; `from`/`to` alongside it are then
        // unconsumed keys, which the cursor's Finish classifies as SpecKeyUnrecognized — the
        // established owner for a key no reader takes (D-075).
        AssertFailsWith(
            SpecReader.Read(Attribute("restrict_to = [{ value = 30, from = 10 }]")),
            DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenRestrictToRangeHasUnknownKey_ThenSpecKeyUnrecognized()
    {
        AssertFailsWith(
            SpecReader.Read(Attribute("restrict_to = [{ from = 10, until = 20 }]")),
            DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenSeveralRestrictEntriesAreMalformed_ThenAllAggregateDeterministically()
    {
        // P-14: parse failures aggregate rather than short-circuiting, and never leak an
        // exception from a strict factory.
        var result = SpecReader.Read(Attribute("restrict_to = [{ value = \"x\" }, 10, { value = true }]"));

        Assert.False(result.IsOk);
        Assert.Equal(3, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
    }

    private static void AssertFailsWith(Diagnosed<FcaBedrock.Spec.Toml.SpecDocument> result, DiagnosticCode code)
    {
        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }

    private static string Attribute(string body, string source = "{ kind = \"column\", index = 0 }") =>
        $"[spec]\nversion = 1\n[[attribute]]\nname = \"a\"\nsource = {source}\n{body}\n";
}
