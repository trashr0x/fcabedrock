using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Round-trip tests (D-075): the contract is document-model fidelity, not byte
/// fidelity of authored files, so the primary oracle is <em>canonical-text
/// idempotence</em>: after one write canonicalizes the form, read∘write is the
/// identity on the text (and therefore on the document). Targeted structural
/// asserts pin the D-049/D-071 presence guarantees through a full cycle. Text
/// idempotence cannot see a lossy <em>first</em> write, so one shared property also
/// requires a spec's fingerprints, formal names and conversion bytes to survive it.
/// </summary>
public sealed class SpecRoundTripTests
{
    [Theory]
    [InlineData(TomlFixtures.MiniMushroom)]
    [InlineData(TomlFixtures.MiniAdult)]
    [InlineData(TomlFixtures.MiniAdultTriples)]
    [InlineData(TomlFixtures.KitchenSink)]
    public void WriteReadWrite_WhenAuthoredFixture_ThenCanonicalTextIsIdempotent(string fixture)
    {
        var first = SpecWriter.Write(Read(fixture));
        var second = SpecWriter.Write(Read(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void WriteReadWrite_WhenBuilderDocument_ThenCanonicalTextIsIdempotent()
    {
        var first = SpecWriter.Write(DocumentFixtures.MiniMushroom());
        var second = SpecWriter.Write(Read(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void RoundTrip_WhenInlineTablesSpanLines_ThenCanonicalTextEqualsTheSingleLineTwin()
    {
        // A spec written with TOML 1.1.0 spellings (D-133) canonicalizes to the writer's existing
        // form: inline tables on one line, \e as \u001B, \x41 as the character it names, and the
        // omitted seconds written out. That canonical text is also valid TOML 1.0.0, and a second
        // read and write leaves it unchanged.
        const string Authored = """
            [spec]
            version = 1

            [provenance]
            created_at = 2026-05-09T10:15+02:00
            notes = "bold \e[1m, letter \x41"

            [binding]
            shape = "wide"

            [[attribute]]
            name = "tissue"
            source = {
                kind = "column",
                index = 0,   # the first column
            }
            discretizer = {
                kind = "value_groups",
                groups = [
                    { label = "head", values = ["brain", "eye"], },
                ],
                unmatched = "skip",
            }
            scale = { kind = "nominal", }
            """;
        var singleLineTwin = string.Join(
            '\n',
            "[spec]",
            "version = 1",
            "",
            "[provenance]",
            "created_at = 2026-05-09T10:15:00+02:00",
            "notes = \"bold \\u001B[1m, letter A\"",
            "",
            "[binding]",
            "shape = \"wide\"",
            "",
            "[[attribute]]",
            "name = \"tissue\"",
            "source = { kind = \"column\", index = 0 }",
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"head\", values = [\"brain\", \"eye\"] }], unmatched = \"skip\" }",
            "scale = { kind = \"nominal\" }") + "\n";

        var canonical = SpecWriter.Write(Read(Authored));

        Assert.Equal(singleLineTwin, canonical);
        Assert.Equal(canonical, SpecWriter.Write(Read(canonical)));
    }

    [Fact]
    public void RoundTrip_WhenDeclaredDomainAuthoredEmpty_ThenStaysAuthoredEmpty()
    {
        // D-071: [] survives verbatim; omitted stays omitted.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("empty", declaredDomain: []),
            DocumentFixtures.Attribute("omitted"),
        ]);

        var reread = Read(SpecWriter.Write(document));

        Assert.NotNull(reread.Attributes[0].DeclaredDomain);
        Assert.Empty(reread.Attributes[0].DeclaredDomain!);
        Assert.Null(reread.Attributes[1].DeclaredDomain);
    }

    [Fact]
    public void RoundTrip_WhenValueEqualsItsDefault_ThenStaysAuthored()
    {
        // D-049/§6: authored-vs-default presence survives a full cycle.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", missingPolicy: MissingPolicy.Skip, unknownValuePolicy: UnknownValuePolicy.Warn)]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(MissingPolicy.Skip, reread.Attributes[0].MissingPolicy);
        Assert.Equal(UnknownValuePolicy.Warn, reread.Attributes[0].UnknownValuePolicy);
    }

    [Fact]
    public void RoundTrip_WhenRestrictToMixed_ThenEntriesSurviveInOrder()
    {
        // D-057/D-091: every authored form round-trips, including the exact numeric entry.
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
                new RestrictToNumber(30.5),
            ]),
        ]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(
            [
                new RestrictToValue("Bachelors"), new RestrictToRange(10, 20), new RestrictToRange(90, null),
                new RestrictToRange(null, 5), new RestrictToRange(null, null),
                new RestrictToNumber(30), new RestrictToNumber(30.5),
            ],
            reread.Attributes[0].RestrictTo);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("30.0")]
    [InlineData("3e1")]
    public void RoundTrip_WhenExactEntrySpelledVariously_ThenAllCanonicalizeToTheWritersForm(string spelling)
    {
        // §10.4/D-091: 30 / 30.0 / 3e1 are equivalent inputs that all
        // canonicalize to the writer's `{ value = 30 }`, and the canonical text is
        // re-readable, so parse → write → parse is stable. This is the one layer where spelling
        // exists; after parsing they are the same double.
        var parsed = Read(Attribute($"restrict_to = [{{ value = {spelling} }}]"));

        var toml = SpecWriter.Write(parsed);
        Assert.Contains("restrict_to = [{ value = 30 }]", toml, StringComparison.Ordinal);

        // …and writing the canonical text again is idempotent.
        var reread = Read(toml);
        Assert.Equal([new RestrictToNumber(30)], reread.Attributes[0].RestrictTo);
        Assert.Equal(toml, SpecWriter.Write(reread));
    }

    [Fact]
    public void RoundTrip_WhenATemplateCarriesEveryRestrictForm_ThenAllSurviveInOrder()
    {
        // §9/D-057: templates reuse the SAME Core restriction union as attributes, so the exact
        // numeric entry must transport through the template carrier too: templates are merged by
        // `extends` and applied at resolve, so a form that cannot round-trip here would be lost
        // before it could ever be applied.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a")],
            templates:
            [
                new TemplateSection("numeric", true, null, null, null,
                    [new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToRange(null, null), new RestrictToNumber(30)],
                    null, null, null),
                new TemplateSection("categorical", true, null, null, null,
                    [new RestrictToValue("Bmp5")], null, null, null),
            ]);

        var toml = SpecWriter.Write(document);
        Assert.Contains(
            "restrict_to = [{ value = 30 }, { from = 10, to = 20 }, {}, { value = 30 }]",
            toml, StringComparison.Ordinal);

        var reread = Read(toml);
        Assert.Equal(
            [new RestrictToNumber(30), new RestrictToRange(10, 20), new RestrictToRange(null, null), new RestrictToNumber(30)],
            reread.Templates[0].RestrictTo);
        Assert.Equal([new RestrictToValue("Bmp5")], reread.Templates[1].RestrictTo);
    }

    [Fact]
    public void RoundTrip_WhenRestrictToIsOmittedVersusEmpty_ThenThePresenceDistinctionSurvives()
    {
        // D-049 presence tracking: an omitted restrict_to and an authored [] are different
        // documents (one restricts nothing because it says nothing; the other says "no entries"),
        // and the writer must not collapse them.
        var omitted = Read(SpecWriter.Write(DocumentFixtures.Document([DocumentFixtures.Attribute("a")])));
        Assert.Null(omitted.Attributes[0].RestrictTo);

        var empty = Read(SpecWriter.Write(
            DocumentFixtures.Document([DocumentFixtures.Attribute("a", restrictTo: [])])));
        Assert.NotNull(empty.Attributes[0].RestrictTo);
        Assert.Empty(empty.Attributes[0].RestrictTo!);
    }

    [Fact]
    public void RoundTrip_WhenValueLabelsIsOmittedVersusEmpty_ThenThePresenceDistinctionSurvives()
    {
        // The value_labels mirror of the test above (D-049): an omitted map and an authored {}
        // are different documents, and the canonical writer must not collapse them, even though
        // the two converge on identical rendered names and identical fingerprints (D-077).
        var omitted = Read(SpecWriter.Write(DocumentFixtures.Document([DocumentFixtures.Attribute("a")])));
        Assert.Null(omitted.Attributes[0].ValueLabels);

        var empty = Read(SpecWriter.Write(DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", valueLabels: new Dictionary<string, string>())])));
        Assert.NotNull(empty.Attributes[0].ValueLabels);
        Assert.Empty(empty.Attributes[0].ValueLabels!);
    }

    [Fact]
    public void RoundTrip_WhenValueLabelsHaveWeirdKeys_ThenOrderAndKeysSurvive()
    {
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Nominal("a", 0, ["x y", "11th", "café"], new Dictionary<string, string>
            {
                ["x y"] = "spaced",
                ["11th"] = "grade",
                ["café"] = "unicode",
            }),
        ]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(["x y", "11th", "café"], reread.Attributes[0].ValueLabels!.Keys);
        Assert.Equal("unicode", reread.Attributes[0].ValueLabels!["café"]);
    }

    [Fact]
    public void RoundTrip_WhenDeferredScaleCarrier_ThenKindSurvives()
    {
        // D-010: the kind-only carrier round-trips even though planning rejects it.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", discretizer: new IdentityDiscretizerSection(), scale: new DeferredScaleSection("biordinal"))]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal("biordinal", Assert.IsType<DeferredScaleSection>(reread.Attributes[0].Scale).Kind);
    }

    [Fact]
    public void RoundTrip_WhenDerivedSpecAuthored_ThenExtendsSurvivesWriteRead()
    {
        // A derived (uncomposed) document is itself round-trippable authored
        // surface (D-078); extends is consumed only by SpecComposer.
        var document = DocumentFixtures.Document(spec: DocumentFixtures.SpecV1(extends: "../base/emage.toml"));

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal("../base/emage.toml", reread.Spec?.Extends);
    }

    [Fact]
    public void RoundTrip_WhenTemplatesAndMatchersAuthored_ThenCarriersSurviveInOrder()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("a", template: "second")],
            templates:
            [
                new TemplateSection("first", null, new IdentityDiscretizerSection(), new NominalScaleSection(),
                    ["x"], null, null, null, null),
                new TemplateSection("second", true, null, new DeferredScaleSection("contranominal"),
                    null, [new RestrictToValue("Yes")], new Dictionary<string, string> { ["y"] = "yes" },
                    MissingPolicy.Skip, UnknownValuePolicy.Warn),
            ],
            matchers:
            [
                new MatcherSection(new MatchSection("^f_\\d+$", null), "first"),
                new MatcherSection(new MatchSection(null, [10, 1553]), "second"),
            ]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(["first", "second"], reread.Templates.Select(t => t.Id));
        Assert.Equal(["x"], reread.Templates[0].DeclaredDomain);
        Assert.True(reread.Templates[1].Include);
        Assert.Equal("contranominal", Assert.IsType<DeferredScaleSection>(reread.Templates[1].Scale).Kind);
        Assert.Equal([new RestrictToValue("Yes")], reread.Templates[1].RestrictTo);
        Assert.Equal(MissingPolicy.Skip, reread.Templates[1].MissingPolicy);
        Assert.Equal("^f_\\d+$", reread.Matchers[0].Match?.NameRegex);
        Assert.Equal("first", reread.Matchers[0].Template);
        Assert.Equal([10L, 1553L], reread.Matchers[1].Match?.SourceIndexRange);
        Assert.Equal("second", reread.Attributes[0].Template);
    }

    [Fact]
    public void RoundTrip_WhenEscapableStringsEverywhere_ThenVerbatim()
    {
        // A quote, CR or LF is not a valid attribute name (§10.1), so those escapes travel in the
        // domain; the name keeps the escapes a name may carry.
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("tab\tback\\slash", declaredDomain: ["quote\"here", "tab\there", "new\nline", "日本"])]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal("tab\tback\\slash", reread.Attributes[0].Name);
        Assert.Equal(["quote\"here", "tab\there", "new\nline", "日本"], reread.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void RoundTrip_WhenTripleColumnsByName_ThenNameRefsSurvive()
    {
        // Triple columns may bind roles by header name; the ColumnRef form
        // round-trips through read∘write (D-082).
        var columns = new TripleColumnsSection(new NameColumnRef("subj"), new NameColumnRef("pred"), new NameColumnRef("obj"));
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("age", new PredicateSourceSection("age", ValueType: null),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection())],
            binding: DocumentFixtures.TripleBinding(columns, hasHeader: true));

        var first = SpecWriter.Write(document);
        var reread = Read(first);

        Assert.Equal(columns, reread.Binding?.Columns);
        Assert.Equal(first, SpecWriter.Write(reread)); // canonical idempotence
    }

    [Fact]
    public void RoundTrip_WhenTripleColumnsMixIndexAndName_ThenBothFormsSurvive()
    {
        // The document model carries any authored per-role form (D-066); mixed
        // addressing is rejected only at resolve.
        var columns = new TripleColumnsSection(new IndexColumnRef(0), new NameColumnRef("pred"), new IndexColumnRef(2));
        var document = DocumentFixtures.Document(binding: DocumentFixtures.TripleBinding(columns));

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(columns, reread.Binding?.Columns);
    }

    [Fact]
    public void RoundTrip_WhenNumericFreePerValue_ThenAuthoredSpellingsSurviveVerbatim()
    {
        // §11.3/D-096: the document keeps authored numeric spellings; only the resolved Core graph
        // normalizes them. The free_per_value carrier, declared_domain, and value_labels keys all
        // survive read∘write verbatim (a later 90.0 does NOT become 90 in the document).
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("v", DocumentFixtures.Column(0, SourceValueType.Number),
                discretizer: new FreePerValueDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["90.0", "5e0"],
                valueLabels: new Dictionary<string, string> { ["90.0"] = "ninety", ["5e0"] = "five" }),
        ]);

        var reread = Read(SpecWriter.Write(document));

        Assert.IsType<FreePerValueDiscretizerSection>(reread.Attributes[0].Discretizer);
        Assert.Equal(SourceValueType.Number, Assert.IsType<ColumnSourceSection>(reread.Attributes[0].Source).ValueType);
        Assert.Equal(["90.0", "5e0"], reread.Attributes[0].DeclaredDomain);
        Assert.Equal(["90.0", "5e0"], reread.Attributes[0].ValueLabels!.Keys);
    }

    [Fact]
    public void RoundTrip_WhenNumericFreePerValueOrdinalOrder_ThenAuthoredOrderSpellingsSurvive()
    {
        var scale = new OrdinalScaleSection(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, ["90.0", "5e0"], DropTop: null);
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("v", DocumentFixtures.Column(0, SourceValueType.Number),
                discretizer: new FreePerValueDiscretizerSection(), scale: scale, declaredDomain: ["90.0", "5e0"])]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(["90.0", "5e0"], Assert.IsType<OrdinalScaleSection>(reread.Attributes[0].Scale).Order);
    }

    [Fact]
    public void RoundTrip_WhenNamingKeysAuthoredOnAllThreeOwners_ThenEachSurvivesVerbatim()
    {
        // D-119 floor item 7 over the naming carriers: [defaults], [[template]], and
        // [[attribute]] each keep their authored naming values through a full cycle,
        // including the {{…}} escape and the {column} alias: the authored SPELLING
        // round-trips, never a normalized equivalent (D-075).
        var toml =
            "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\n\n"
            + "[defaults]\nformal_attribute_format = \"{value}\"\n\n"
            + "[[template]]\nid = \"t\"\ndisplay_name = \"T\"\nformal_attribute_format = \"{name}\"\n\n"
            + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n"
            + "display_name = \"A\"\nformal_attribute_format = \"{{{column}}}-{value}\"\n";

        var reread = Read(SpecWriter.Write(Read(toml)));

        Assert.Equal("{value}", reread.Defaults!.FormalAttributeFormat);
        Assert.Equal("T", reread.Templates[0].DisplayName);
        Assert.Equal("{name}", reread.Templates[0].FormalAttributeFormat);
        Assert.Equal("A", reread.Attributes[0].DisplayName);
        Assert.Equal("{{{column}}}-{value}", reread.Attributes[0].FormalAttributeFormat);
    }

    [Fact]
    public void RoundTrip_WhenNamingKeysOmitted_ThenTheyStayOmitted()
    {
        // The other half of presence tracking: an omitted key must not acquire a value on
        // the way through, which is what keeps every naming-free spec byte-identical.
        var reread = Read(SpecWriter.Write(DocumentFixtures.Document([DocumentFixtures.Nominal("a", 0)])));

        Assert.Null(reread.Attributes[0].DisplayName);
        Assert.Null(reread.Attributes[0].FormalAttributeFormat);
    }

    [Fact]
    public void RoundTrip_WhenFormatEqualsTheScaleDefaultRendering_ThenItIsStillAuthored()
    {
        // Authored-equals-default presence (D-049) for the naming surface: an explicit
        // "{column}-{value}" on a nominal attribute renders exactly what the default would,
        // yet it is authored state and must survive rather than be optimized away.
        var reread = Read(Attribute("formal_attribute_format = \"{column}-{value}\""));

        Assert.Equal("{column}-{value}", reread.Attributes[0].FormalAttributeFormat);
    }

    // --- the shared semantic round trip (§14, D-075/D-101) --------------------

    // Each case is a valid fully declared or include-calibrated spec over a one- or two-column
    // headerless CSV whose records sit on, just below and just above the boundaries it tests.
    public static TheoryData<string, string, string> SemanticRoundTripCases() => new()
    {
        {
            "negative-zero manual cut",
            Numeric("discretizer = { kind = \"manual_cuts\", cuts = [-0.0, 10] }"),
            "-5\n-0\n0\n5\n10\n15\n"
        },
        {
            "positive-zero manual cut",
            Numeric("discretizer = { kind = \"manual_cuts\", cuts = [0, 10] }"),
            "-5\n-0\n0\n5\n10\n15\n"
        },
        {
            "negative-zero vmin",
            Numeric("discretizer = { kind = \"equal_width\", bins = 2, range = \"manual\", vmin = -0.0, vmax = 10 }"),
            "-1\n-0\n0\n5\n10\n11\n"
        },
        {
            "negative-zero vmax",
            Numeric("discretizer = { kind = \"equal_width\", bins = 2, range = \"manual\", vmin = -10, vmax = -0.0 }"),
            "-11\n-10\n-5\n-0\n0\n1\n"
        },
        {
            "2^53 and its neighbours",
            Numeric("discretizer = { kind = \"manual_cuts\", cuts = [9007199254740991, 9007199254740992, 9007199254740994] }"),
            "9007199254740990\n9007199254740991\n9007199254740992\n9007199254740993\n9007199254740994\n9007199254740996\n"
        },
        {
            "-2^53 and its neighbours",
            Numeric("discretizer = { kind = \"manual_cuts\", cuts = [-9007199254740994, -9007199254740992, -9007199254740991] }"),
            "-9007199254740996\n-9007199254740994\n-9007199254740993\n-9007199254740992\n-9007199254740991\n0\n"
        },
        {
            "1e17 and its neighbours",
            Numeric("discretizer = { kind = \"manual_cuts\", cuts = [99999999999999984, 1e17, 100000000000000016] }"),
            "99999999999999968\n99999999999999984\n1e17\n100000000000000008\n100000000000000016\n1e18\n"
        },
        {
            "ordinary non-integral cuts",
            Numeric("discretizer = { kind = \"manual_cuts\", cuts = [1e-5, 0.1, 2.5, 49.5] }"),
            "0\n0.00001\n0.05\n0.1\n2.5\n49.5\n50\n"
        },
        {
            "computed negative zero",
            Numeric("discretizer = { kind = \"equal_width\", bins = 2, range = \"manual\", vmin = -1, vmax = 0.6, precision = { round_to = 1 } }"),
            "-1\n-0.5\n-0\n0\n0.5\n"
        },
        {
            "extreme range",
            Numeric("discretizer = { kind = \"equal_width\", bins = 2, range = \"manual\", vmin = -1.7e308, vmax = 1.7e308 }"),
            "-1e308\n-0\n0\n1e308\n"
        },
        {
            "restriction and numeric free_per_value zeros",
            RestrictionAndFreePerValue,
            "0,-0\n-0,1\n5,0\n"
        },
        {
            "six value_labels keys",
            Labels(string.Empty),
            "red\ntan\nash\nfig\nelm\noak\n"
        },
        {
            "case-distinct and quoted label keys",
            CaseDistinctLabels,
            "Red\nred\nRED\nx y\na.b\ncafé\n"
        },
        {
            "template-supplied value_labels",
            TemplateLabels,
            "red\ntan\nash\nfig\nelm\noak\n"
        },
        {
            "include-calibrated value_labels",
            Labels("unknown_value_policy = \"include\"\n"),
            "red\ntan\nash\nfig\nelm\noak\nextra\n"
        },
    };

    [Theory]
    [MemberData(nameof(SemanticRoundTripCases))]
    public async Task RoundTrip_WhenTheAuthoredSpecIsWrittenAndReread_ThenItMeansWhatItMeantBefore(
        string name, string toml, string csv)
    {
        _ = name; // labels the case in a test report
        var authored = Read(toml);
        var before = await ConvertAsync(authored, csv);

        var written = SpecWriter.Write(authored);
        var reread = Read(written);
        var after = await ConvertAsync(reread, csv);

        Assert.Equal(before.Fingerprints, after.Fingerprints);
        Assert.Equal(before.Names, after.Names);
        Assert.Equal(before.Cxt, after.Cxt);
        Assert.Equal(before.Dat, after.Dat);
        Assert.Equal(before.V2CompatCxt, after.V2CompatCxt);

        // Canonical-text idempotence, and a resolver snapshot that writes exactly like the document
        // it was taken from (calibrate writes that snapshot, D-075/D-098).
        Assert.Equal(written, SpecWriter.Write(reread));
        Assert.Equal(written, SpecWriter.Write(before.Snapshot));
    }

    private const string Header = "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n";

    private const string DSixDomain = "declared_domain = [\"red\", \"tan\", \"ash\", \"fig\", \"elm\", \"oak\"]\n";

    private const string DSixLabels =
        "value_labels = { red = \"L-red\", tan = \"L-tan\", ash = \"L-ash\", fig = \"L-fig\", elm = \"L-elm\", oak = \"L-oak\" }\n";

    private const string RestrictionAndFreePerValue = Header
        + "[[attribute]]\nname = \"x\"\nsource = { kind = \"column\", index = 0 }\n"
        + "discretizer = { kind = \"manual_cuts\", cuts = [5] }\nscale = { kind = \"nominal\" }\n"
        + "restrict_to = [{ value = -0.0 }, { from = -0.0, to = 1 }]\n\n"
        + "[[attribute]]\nname = \"y\"\nsource = { kind = \"column\", index = 1, value_type = \"number\" }\n"
        + "declared_domain = [\"-0\", \"1\"]\nvalue_labels = { \"-0\" = \"zero\", \"1\" = \"one\" }\n"
        + "discretizer = { kind = \"free_per_value\" }\nscale = { kind = \"nominal\" }\n";

    private const string CaseDistinctLabels = Header
        + "[[attribute]]\nname = \"k\"\nsource = { kind = \"column\", index = 0 }\n"
        + "declared_domain = [\"Red\", \"red\", \"RED\", \"x y\", \"a.b\", \"café\"]\n"
        + "value_labels = { Red = \"L-Red\", red = \"L-red\", RED = \"L-RED\", \"x y\" = \"L-x y\", \"a.b\" = \"L-a.b\", \"café\" = \"L-café\" }\n"
        + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n";

    private const string TemplateLabels = Header
        + "[[template]]\nid = \"t\"\n" + DSixLabels + "\n"
        + "[[attribute]]\nname = \"k\"\ntemplate = \"t\"\nsource = { kind = \"column\", index = 0 }\n" + DSixDomain
        + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n";

    private static string Numeric(string discretizer) =>
        Header + "[[attribute]]\nname = \"x\"\nsource = { kind = \"column\", index = 0, value_type = \"number\" }\n"
        + discretizer + "\nscale = { kind = \"nominal\" }\n";

    private static string Labels(string extraKeys) =>
        Header + "[[attribute]]\nname = \"k\"\nsource = { kind = \"column\", index = 0 }\n" + DSixDomain + extraKeys
        + DSixLabels + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n";

    // Resolve, calibrate when the spec needs data, plan, fingerprint and convert, exactly as a run
    // does, over the given CSV text.
    private static async Task<Converted> ConvertAsync(FcaBedrock.Spec.Toml.SpecDocument document, string csv)
    {
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));
        var session = new WideCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)), readSettings);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);
        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));

        var token = resolvedDocument.Resolved;
        var source = session.Bind(token);
        CalibratedSpec calibrated;
        if (CalibratedSpec.RequiresData(token.Spec))
        {
            var result = await Calibrator.CalibrateAsync(token, source, TestContext.Current.CancellationToken);
            Assert.True(result.TryGetValue(out var value), Describe(result.Diagnostics));
            calibrated = value;
        }
        else
        {
            calibrated = CalibratedSpec.FromFullyDeclared(token);
        }

        var native = Plan(calibrated, LabelStyle.Native);
        return new Converted(
            resolvedDocument.Document,
            SpecFingerprints.ComputeNative(resolvedDocument, native),
            [.. native.FormalAttributes.Select(attribute => attribute.RenderedName)],
            await CxtAsync(native, source, WriterOptions.Native),
            await DatAsync(native, source, WriterOptions.Native),
            await CxtAsync(Plan(calibrated, LabelStyle.V2Compat), source, WriterOptions.V2Compat));
    }

    private static ConversionPlan Plan(CalibratedSpec calibrated, LabelStyle style)
    {
        var planned = ConversionPlanner.Plan(calibrated, style);
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));
        return plan;
    }

    private static async Task<byte[]> CxtAsync(ConversionPlan plan, IRecordSource source, WriterOptions options)
    {
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit =
            sink => Emitter.EmitAsync(plan, source, sink);
        using var output = new MemoryStream();
        var diagnostics = new List<BedrockDiagnostic>();
        using (var replay = EmitReplay.Begin(emit, diagnostics))
        {
            await CxtWriter.WriteAsync(plan, replay.Open, options, output, TestContext.Current.CancellationToken);
        }

        Assert.DoesNotContain(diagnostics, IsError);
        return output.ToArray();
    }

    private static async Task<byte[]> DatAsync(ConversionPlan plan, IRecordSource source, WriterOptions options)
    {
        using var output = new MemoryStream();
        var diagnostics = new List<BedrockDiagnostic>();
        await DatWriter.WriteAsync(Emitter.EmitAsync(plan, source, diagnostics), options, output, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(diagnostics, IsError);
        return output.ToArray();
    }

    private static bool IsError(BedrockDiagnostic diagnostic) =>
        diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private sealed record Converted(
        FcaBedrock.Spec.Toml.SpecDocument Snapshot,
        ComputedFingerprints Fingerprints,
        string[] Names,
        byte[] Cxt,
        byte[] Dat,
        byte[] V2CompatCxt);

    private static FcaBedrock.Spec.Toml.SpecDocument Read(string toml)
    {
        var result = SpecReader.Read(toml);
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return document;
    }

    // A minimal one-attribute document carrying `body`, for the tests that must start from
    // authored TEXT (the only layer where a number's spelling still exists).
    private static string Attribute(string body) =>
        "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\n\n"
        + "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 0 }\n"
        + body + "\n";
}
