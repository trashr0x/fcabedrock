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
    public void RoundTrip_WhenEscapableStringsEverywhere_ThenVerbatim()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Attribute("quote\"back\\slash", declaredDomain: ["tab\there", "new\nline", "日本"])]);

        var reread = Read(SpecWriter.Write(document));

        Assert.Equal("quote\"back\\slash", reread.Attributes[0].Name);
        Assert.Equal(["tab\there", "new\nline", "日本"], reread.Attributes[0].DeclaredDomain);
    }

    private static FcaBedrock.Spec.Toml.SpecDocument Read(string toml)
    {
        var result = SpecReader.Read(toml);
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return document;
    }
}
