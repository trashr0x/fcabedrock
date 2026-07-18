using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

// SourceReadSettings.Create is the P-10 programmer-error backstop (the seam diagnoses authored
// errors first); its exact exception contract is pinned here (D-098/G-1).
public sealed class SourceReadSettingsTests
{
    private static SourceReadSettings Wide(
        string encoding = "utf-8", char delimiter = ',', char quote = '"', bool hasHeader = true, string missingToken = "?") =>
        SourceReadSettings.Create(SourceShape.Wide, encoding, delimiter, quote, hasHeader, missingToken, ordering: null);

    [Fact]
    public void Create_WhenValidWide_ThenExposesTheScalars()
    {
        var settings = Wide(missingToken: "NA");

        Assert.Equal(SourceShape.Wide, settings.Shape);
        Assert.Equal("utf-8", settings.Encoding);
        Assert.Equal(',', settings.Delimiter);
        Assert.Equal("NA", settings.MissingToken);
        Assert.Null(settings.Ordering);
    }

    [Fact]
    public void Create_WhenMissingTokenEmpty_ThenValid() =>
        // §5.1: an empty missing_token disables token-based missing detection — valid, not rejected.
        Assert.Equal("", Wide(missingToken: "").MissingToken);

    [Fact]
    public void Create_WhenEncodingSpelledUtf8_ThenNormalizesToCanonical() =>
        Assert.Equal("utf-8", Wide(encoding: "UTF8").Encoding);

    [Fact]
    public void Create_WhenEncodingUnsupported_ThenThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => Wide(encoding: "latin-1"));

    [Fact]
    public void Create_WhenDelimiterEqualsQuote_ThenThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => Wide(delimiter: '"'));

    [Fact]
    public void Create_WhenQuoteNotStandard_ThenThrowsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => Wide(quote: '\''));

    [Fact]
    public void Create_WhenNullEncoding_ThenThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() =>
            SourceReadSettings.Create(SourceShape.Wide, null!, ',', '"', true, "?", null));

    [Fact]
    public void Create_WhenNullMissingToken_ThenThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() =>
            SourceReadSettings.Create(SourceShape.Wide, "utf-8", ',', '"', true, null!, null));

    [Fact]
    public void Create_WhenWideWithOrdering_ThenThrowsArgumentException() =>
        // ordering must be non-null exactly when shape is Triple.
        Assert.Throws<ArgumentException>(() =>
            SourceReadSettings.Create(SourceShape.Wide, "utf-8", ',', '"', true, "?", TripleOrdering.Unordered));

    [Fact]
    public void Create_WhenTripleWithoutOrdering_ThenThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() =>
            SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", ordering: null));

    [Fact]
    public void Equals_WhenSameScalars_ThenValueEqual()
    {
        var a = Wide(missingToken: "NA");
        var b = Wide(missingToken: "NA");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_WhenMissingTokenDiffers_ThenNotEqual() =>
        Assert.NotEqual(Wide(missingToken: "?"), Wide(missingToken: "NA"));

    // --- CreateWide / CreateTriple: the §5.1/§7.1 delimited-source defaults (M5-IP-004) ---

    [Fact]
    public void CreateWide_WhenNoArguments_ThenExposesTheSection51WideDefaults()
    {
        var settings = SourceReadSettings.CreateWide();

        Assert.Equal(SourceShape.Wide, settings.Shape);
        Assert.Equal("utf-8", settings.Encoding);
        Assert.Equal(',', settings.Delimiter);
        Assert.Equal('"', settings.QuoteChar);
        Assert.True(settings.HasHeader);
        Assert.Equal("?", settings.MissingToken);
        Assert.Null(settings.Ordering);
    }

    [Fact]
    public void CreateTriple_WhenNoArguments_ThenExposesTheSection51TripleDefaults()
    {
        var settings = SourceReadSettings.CreateTriple();

        Assert.Equal(SourceShape.Triple, settings.Shape);
        Assert.Equal("utf-8", settings.Encoding);
        Assert.Equal(',', settings.Delimiter);
        Assert.Equal('"', settings.QuoteChar);
        // The shape-specific default: a triple source has no header unless asked (D-082).
        Assert.False(settings.HasHeader);
        Assert.Equal("?", settings.MissingToken);
        Assert.Equal(TripleOrdering.Unordered, settings.Ordering);
    }

    [Fact]
    public void CreateWide_WhenOverridden_ThenHonorsEveryArgument()
    {
        var settings = SourceReadSettings.CreateWide("utf8", '\t', '"', hasHeader: false, missingToken: "NA");

        Assert.Equal('\t', settings.Delimiter);
        Assert.False(settings.HasHeader);
        Assert.Equal("NA", settings.MissingToken);
        Assert.Equal("utf-8", settings.Encoding);
    }

    [Fact]
    public void CreateTriple_WhenOverridden_ThenHonorsEveryArgument()
    {
        var settings = SourceReadSettings.CreateTriple(
            "UTF-8", '\t', '"', hasHeader: true, missingToken: "", TripleOrdering.SubjectGrouped);

        Assert.Equal('\t', settings.Delimiter);
        Assert.True(settings.HasHeader);
        Assert.Equal("", settings.MissingToken);
        Assert.Equal(TripleOrdering.SubjectGrouped, settings.Ordering);
    }

    [Fact]
    public void CreateWide_WhenDefaulted_ThenValueEqualsTheEquivalentCreate() =>
        // The conveniences are exactly Create with the §5.1 defaults filled in — no second
        // normalization path, so value equality (and the hash) cannot drift between them.
        Assert.Equal(
            SourceReadSettings.Create(SourceShape.Wide, "utf-8", ',', '"', true, "?", ordering: null),
            SourceReadSettings.CreateWide());

    [Fact]
    public void CreateTriple_WhenDefaulted_ThenValueEqualsTheEquivalentCreate() =>
        Assert.Equal(
            SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", TripleOrdering.Unordered),
            SourceReadSettings.CreateTriple());

    [Fact]
    public void CreateWide_WhenEncodingUnsupported_ThenThrowsArgumentException() =>
        // Validation has one owner: the conveniences delegate to Create, so its exact exception
        // contract reaches them unchanged.
        Assert.Throws<ArgumentException>(() => SourceReadSettings.CreateWide(encoding: "latin-1"));

    [Fact]
    public void CreateWide_WhenDelimiterEqualsQuote_ThenThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => SourceReadSettings.CreateWide(delimiter: '"'));

    [Fact]
    public void CreateWide_WhenQuoteNotStandard_ThenThrowsNotSupported() =>
        Assert.Throws<NotSupportedException>(() => SourceReadSettings.CreateWide(quoteChar: '\''));

    [Fact]
    public void CreateWide_WhenNullMissingToken_ThenThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() => SourceReadSettings.CreateWide(missingToken: null!));

    [Fact]
    public void CreateTriple_WhenNullEncoding_ThenThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() => SourceReadSettings.CreateTriple(encoding: null!));

    [Fact]
    public void CreateWide_WhenMissingTokenEmpty_ThenValid() =>
        Assert.Equal("", SourceReadSettings.CreateWide(missingToken: "").MissingToken);
}
