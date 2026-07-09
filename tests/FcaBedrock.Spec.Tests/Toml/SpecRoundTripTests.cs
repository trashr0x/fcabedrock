using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Round-trip tests (D-075): the contract is document-model fidelity, not byte
/// fidelity of authored files, so the primary oracle is <em>canonical-text
/// idempotence</em> — after one write canonicalizes the form, read∘write is the
/// identity on the text (and therefore on the document). Targeted structural
/// asserts pin the D-049/D-071 presence guarantees through a full cycle.
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
        // D-057: every authored form round-trips; execution stays M4.
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("a", restrictTo:
            [
                new RestrictToValue("Bachelors"),
                new RestrictToRange(10, 20),
                new RestrictToRange(90, null),
                new RestrictToRange(null, 5),
            ]),
        ]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal(
            [new RestrictToValue("Bachelors"), new RestrictToRange(10, 20), new RestrictToRange(90, null), new RestrictToRange(null, 5)],
            reread.Attributes[0].RestrictTo);
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
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("quote\"back\\slash", declaredDomain: ["tab\there", "new\nline", "日本"])]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal("quote\"back\\slash", reread.Attributes[0].Name);
        Assert.Equal(["tab\there", "new\nline", "日本"], reread.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void RoundTrip_WhenTripleColumnsByName_ThenNameRefsSurvive()
    {
        // Slice B: triple columns may bind roles by header name; the ColumnRef form
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

    private static FcaBedrock.Spec.Toml.SpecDocument Read(string toml)
    {
        var result = SpecReader.Read(toml);
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return document;
    }
}
