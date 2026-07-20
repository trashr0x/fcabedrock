using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Tests for the canonical writer (D-075): authored-only emission (presence
/// tracking survives, D-049/D-071), fixed section/key order, inline-table
/// shapes, escaping, and the LF-only output invariant.
/// </summary>
public sealed class SpecWriterTests
{
    [Fact]
    public void Write_WhenMinimalDocument_ThenOnlyAuthoredFieldsEmit()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("odor", 0)]);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[binding]",
                "shape = \"wide\"",
                "",
                "[[attribute]]",
                "name = \"odor\"",
                "source = { kind = \"column\", index = 0 }",
                "discretizer = { kind = \"identity\" }",
                "scale = { kind = \"nominal\" }"),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenValueEqualsItsDefault_ThenStillEmitted()
    {
        // D-049/§6: authored-vs-default presence is provenance; an authored
        // missing_policy = "skip" must not be dropped just because skip is the default.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", missingPolicy: MissingPolicy.Skip)]);

        Assert.Contains("missing_policy = \"skip\"", SpecWriter.Write(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenDeclaredDomainAuthoredEmpty_ThenEmptyArrayEmitted()
    {
        // D-071: omitted and authored-[] both resolve absent, but the authored
        // form round-trips verbatim.
        var omitted = DocumentFixtures.Document([DocumentFixtures.Attribute("a")]);
        var authoredEmpty = DocumentFixtures.Document([DocumentFixtures.Attribute("a", declaredDomain: [])]);

        Assert.DoesNotContain("declared_domain", SpecWriter.Write(omitted), StringComparison.Ordinal);
        Assert.Contains("declared_domain = []", SpecWriter.Write(authoredEmpty), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenNameNeedsQuoting_ThenEscapedBasicString()
    {
        var document = DocumentFixtures.Document([DocumentFixtures.Attribute("bruises?")]);

        Assert.Contains("name = \"bruises?\"", SpecWriter.Write(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenValueLabelKeyIsNotBare_ThenKeyQuoted()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("a", 0, ["x y", "HS-grad"], new Dictionary<string, string>
            {
                ["x y"] = "spaced",
                ["HS-grad"] = "grad",
            }),
        ]);

        Assert.Contains(
            "value_labels = { \"x y\" = \"spaced\", HS-grad = \"grad\" }",
            SpecWriter.Write(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenManualCuts_ThenIntegralCutsEmitAsBareIntegers()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("age",
                discretizer: new ManualCutsDiscretizerSection([30d, 40.5d], Core.Discretization.BinEnds.Open),
                scale: new NominalScaleSection()),
        ]);

        Assert.Contains(
            "discretizer = { kind = \"manual_cuts\", cuts = [30, 40.5], ends = \"open\" }",
            SpecWriter.Write(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenFreePerValue_ThenKindOnly()
    {
        // §11.3/D-101: free_per_value writes as a kind-only inline table (no parameters).
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("v", DocumentFixtures.Column(0, SourceValueType.Number),
                discretizer: new FreePerValueDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["90.0", "5e0"]),
        ]);

        var toml = SpecWriter.Write(document);

        Assert.Contains("discretizer = { kind = \"free_per_value\" }", toml, StringComparison.Ordinal);
        // Authored numeric spellings are written verbatim (normalized only at the resolve seam, D-096).
        Assert.Contains("declared_domain = [\"90.0\", \"5e0\"]", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenRestrictToMixesEveryForm_ThenAllFormsRenderCanonically()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("a", restrictTo:
            [
                new RestrictToValue("Bachelors"),
                new RestrictToRange(10, 20),
                new RestrictToRange(90, null),
                new RestrictToRange(null, 5),
                new RestrictToRange(null, null),
                new RestrictToNumber(30),
            ]),
        ]);

        Assert.Contains(
            "restrict_to = [\"Bachelors\", { from = 10, to = 20 }, { from = 90 }, { to = 5 }, {}, { value = 30 }]",
            SpecWriter.Write(document),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(30.0, "{ value = 30 }")]     // integral → bare, matching the spec's own cuts = [30] style
    [InlineData(30.5, "{ value = 30.5 }")]
    [InlineData(-0.0, "{ value = 0 }")]      // a resolved -0 writes as 0 (G-6)
    [InlineData(-12.25, "{ value = -12.25 }")]
    public void Write_WhenExactRestrictEntry_ThenRendersTheCanonicalInvariantShortestNumber(double value, string expected)
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", restrictTo: [new RestrictToNumber(value)])]);

        Assert.Contains($"restrict_to = [{expected}]", SpecWriter.Write(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenRestrictToRepeatsEntriesOutOfCanonicalOrder_ThenTheAuthoredListIsPreserved()
    {
        // The writer never sorts or deduplicates (D-075): order and duplicates are authoring
        // state. Only the fingerprint projects a canonical view (§14) — and it must not leak back
        // into the document.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("a", restrictTo:
                [new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToNumber(30)]),
        ]);

        Assert.Contains(
            "restrict_to = [{ value = 30 }, { from = 10, to = 20 }, { value = 30 }]",
            SpecWriter.Write(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenTripleBindingWithObjectKeyDefaultsAndOutput_ThenCanonicalSectionOrder()
    {
        var document = new SpecDocument(
            DocumentFixtures.SpecV1(),
            new ProvenanceSection(
                "Constantinos Orphanides",
                new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.Zero),
                SourceUrl: null, SourceHash: null, DerivedFrom: null, Notes: null),
            new BindingSection(
                SourceShape.Triple, Encoding: null, Delimiter: null, QuoteChar: null, HasHeader: null,
                Locale: null, MissingToken: null, TripleOrdering.Unordered,
                new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2)),
                new ObjectKeySection(ObjectKeyMode.Column, new NameColumnRef("id"), Columns: null, Aggregate: null)),
            new DefaultsSection(
                Include: true, MissingPolicy: null, UnknownValuePolicy: Core.Spec.UnknownValuePolicy.Warn,
                DuplicateObjectPolicy: null, OrdinalDirection: null, OrdinalBoundary: null),
            new OutputSection(
                BinLabelUnicode: false,
                new CxtOutputSection(LineEndings.Lf, TrailingNewline: true, SizeAdvisoryBytes: 1_073_741_824),
                new DatOutputSection(LineEndings.Crlf, BaseIndex: 0, NonemptyLineTrailingSpace: null, EmptyLineTrailingSpace: null)
                {
                    TrailingNewline = false,
                }),
            [],
            [],
            [
                new AttributeSection(
                    "age", new PredicateSourceSection("age", SourceValueType.Number), Description: "years",
                    Include: null, Template: null, Discretizer: null, Scale: null, DeclaredDomain: null,
                    RestrictTo: null, ValueLabels: null, MissingPolicy: null, UnknownValuePolicy: null),
            ]);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[provenance]",
                "author = \"Constantinos Orphanides\"",
                "created_at = 2026-05-09T10:00:00Z",
                "",
                "[binding]",
                "shape = \"triple\"",
                "ordering = \"unordered\"",
                "columns = { subject = 0, predicate = 1, value = 2 }",
                "",
                "[binding.object_key]",
                "mode = \"column\"",
                "column = \"id\"",
                "",
                "[defaults]",
                "include = true",
                "unknown_value_policy = \"warn\"",
                "",
                "[output]",
                "bin_label_unicode = false",
                "",
                "[output.cxt]",
                "line_endings = \"lf\"",
                "trailing_newline = true",
                "size_advisory_bytes = 1073741824",
                "",
                "[output.dat]",
                "line_endings = \"crlf\"",
                "trailing_newline = false",
                "base_index = 0",
                "",
                "[[attribute]]",
                "name = \"age\"",
                "source = { kind = \"predicate\", name = \"age\", value_type = \"number\" }",
                "description = \"years\""),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenOrdinalScaleFullyAuthored_ThenAllFieldsInOrder()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("education",
                discretizer: new IdentityDiscretizerSection(),
                scale: new OrdinalScaleSection(
                    Core.Scaling.OrdinalDirection.Le,
                    Core.Scaling.OrdinalBoundary.Strict,
                    ["Pre-Uni", "Undergrad", "Postgrad"],
                    DropTop: true)),
        ]);

        Assert.Contains(
            "scale = { kind = \"ordinal\", direction = \"le\", boundary = \"strict\", order = [\"Pre-Uni\", \"Undergrad\", \"Postgrad\"], drop_top = true }",
            SpecWriter.Write(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenExtendsAuthored_ThenEmittedBetweenFingerprintsAndDescription()
    {
        var document = new SpecDocument(
            new SpecSection(1, null, null, "sha256:7890ab", "../base.toml", "derived"),
            null, null, null, null, [], [], []);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "dat_output_fingerprint = \"sha256:7890ab\"",
                "extends = \"../base.toml\"",
                "description = \"derived\""),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenTemplatesAndMatchersAuthored_ThenEmittedBetweenOutputAndAttributes()
    {
        // §2 section order: [output] → [[template]] → [[matcher]] → [[attribute]];
        // only authored keys emit, in the canonical per-section key order.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", template: "boolean_yes_no")],
            output: new OutputSection(BinLabelUnicode: true, Cxt: null, Dat: null),
            templates:
            [
                new TemplateSection(
                    "boolean_yes_no", Include: null,
                    new IdentityDiscretizerSection(), new DichotomicScaleSection("Yes"),
                    ["Yes", "No"], RestrictTo: null, ValueLabels: null,
                    MissingPolicy: null, UnknownValuePolicy: null),
            ],
            matchers:
            [
                new MatcherSection(new MatchSection("^feature_\\d+$", null), "boolean_yes_no"),
                new MatcherSection(new MatchSection(null, [10, 1553]), "boolean_yes_no"),
            ]);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[binding]",
                "shape = \"wide\"",
                "",
                "[output]",
                "bin_label_unicode = true",
                "",
                "[[template]]",
                "id = \"boolean_yes_no\"",
                "declared_domain = [\"Yes\", \"No\"]",
                "discretizer = { kind = \"identity\" }",
                "scale = { kind = \"dichotomic\", true_value = \"Yes\" }",
                "",
                "[[matcher]]",
                "match = { name_regex = \"^feature_\\\\d+$\" }",
                "template = \"boolean_yes_no\"",
                "",
                "[[matcher]]",
                "match = { source_index_range = [10, 1553] }",
                "template = \"boolean_yes_no\"",
                "",
                "[[attribute]]",
                "name = \"a\"",
                "source = { kind = \"column\", index = 0 }",
                "template = \"boolean_yes_no\""),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenAttributeAuthorsNamingKeys_ThenTheyEmitInSpecPresentationOrder()
    {
        // §10.1/§10.7 presentation order (D-075/D-120): display_name takes the §10.1
        // example position between source and description; formal_attribute_format sits
        // in §-order between unknown_value_policy (§10.6) and value_labels (§10.8). The
        // canonical key order is the writer's, never the record's field order — which is
        // exactly why the new carriers could be added non-positionally.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("odor", 0, domain: ["a"], valueLabels: new Dictionary<string, string> { ["a"] = "almond" }) with
            {
                Description = "smells",
                Include = true,
                MissingPolicy = MissingPolicy.AsAttribute,
                UnknownValuePolicy = UnknownValuePolicy.Warn,
                DisplayName = "Odor",
                FormalAttributeFormat = "{display_name}-{value}",
            },
        ]);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[binding]",
                "shape = \"wide\"",
                "",
                "[[attribute]]",
                "name = \"odor\"",
                "source = { kind = \"column\", index = 0 }",
                "display_name = \"Odor\"",
                "description = \"smells\"",
                "include = true",
                "declared_domain = [\"a\"]",
                "missing_policy = \"as_attribute\"",
                "unknown_value_policy = \"warn\"",
                "formal_attribute_format = \"{display_name}-{value}\"",
                "value_labels = { a = \"almond\" }",
                "discretizer = { kind = \"identity\" }",
                "scale = { kind = \"nominal\" }"),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenTemplateAuthorsNamingKeys_ThenTheyMirrorTheAttributeOrder()
    {
        // §9.1: a template body mirrors the attribute key order minus the identity fields,
        // with id leading — so display_name follows id, and the format keeps its place
        // before value_labels.
        var document = DocumentFixtures.Document(
            templates:
            [
                new TemplateSection("t", Include: true, Discretizer: null, Scale: null, DeclaredDomain: null,
                    RestrictTo: null, ValueLabels: null, MissingPolicy: null, UnknownValuePolicy: null)
                {
                    DisplayName = "Boolean",
                    FormalAttributeFormat = "{column}-{value}",
                },
            ]);

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[binding]",
                "shape = \"wide\"",
                "",
                "[[template]]",
                "id = \"t\"",
                "display_name = \"Boolean\"",
                "include = true",
                "formal_attribute_format = \"{column}-{value}\""),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenDefaultsAuthorsNameFormat_ThenItSitsBetweenDuplicatePolicyAndOrdinalDefaults()
    {
        // §6 presentation order: after duplicate_object_policy, before ordinal_direction.
        var document = DocumentFixtures.Document(
            defaults: new DefaultsSection(null, null, null, DuplicateObjectPolicy.Fail, OrdinalDirection.Le, null)
            {
                FormalAttributeFormat = "{value}",
            });

        Assert.Equal(
            Lines(
                "[spec]",
                "version = 1",
                "",
                "[binding]",
                "shape = \"wide\"",
                "",
                "[defaults]",
                "duplicate_object_policy = \"fail\"",
                "formal_attribute_format = \"{value}\"",
                "ordinal_direction = \"le\""),
            SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenNamingKeysOmitted_ThenNeitherEmits()
    {
        // Presence tracking (D-049): omitted stays omitted, so no existing spec gains a
        // key it never authored — which is why every golden and pinned vector is
        // byte-unchanged across this slice.
        var text = SpecWriter.Write(DocumentFixtures.Document([DocumentFixtures.Nominal("odor", 0)]));

        Assert.DoesNotContain("display_name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("formal_attribute_format", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenDocumentEmpty_ThenEmptyText()
    {
        var document = new SpecDocument(null, null, null, null, null, [], [], []);

        Assert.Equal(string.Empty, SpecWriter.Write(document));
    }

    [Fact]
    public void Write_WhenAnyDocument_ThenLfOnlyAndFinalNewline()
    {
        var text = SpecWriter.Write(DocumentFixtures.MiniMushroom());

        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
    }

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";
}
