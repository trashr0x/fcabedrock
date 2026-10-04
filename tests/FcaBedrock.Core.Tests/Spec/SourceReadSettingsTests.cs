using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

// SourceReadSettings.Create is the EP-10 programmer-error backstop (the seam diagnoses authored
// errors first); its exact exception contract is pinned here (D-098).
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
        // §5.1: an empty missing_token disables token-based missing detection: valid, not rejected.
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

    // --- CreateWide / CreateTriple: the §5.1/§7.1 delimited-source defaults ---

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
        // The conveniences are exactly Create with the §5.1 defaults filled in: no second
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

    // --- The v1 delimiter alphabet (§5.1.1): TAB, or U+001F through U+007E except '#' ---

    // The 95 usable delimiters: the alphabet without the double quote, written out literally so the
    // expectation never comes from the rule under test.
    private static readonly string UsableDelimiters =
        "\t" + (char)0x1F + " !$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    [Fact]
    public void IsInDelimiterAlphabet_OverEveryChar_ThenMatchesTheLiteralV1Rule()
    {
        // The alphabet is the 95 usable delimiters plus the double quote: 96 members.
        var alphabet = new HashSet<char>(UsableDelimiters) { '"' };
        Assert.Equal(96, alphabet.Count);

        for (var code = 0; code <= char.MaxValue; code++)
        {
            var c = (char)code;
            Assert.True(alphabet.Contains(c) == SourceReadSettings.IsInDelimiterAlphabet(c), $"U+{code:X4}");
        }

        // Alphabet membership only: the quote is a member although it is not a usable delimiter.
        Assert.True(SourceReadSettings.IsInDelimiterAlphabet('"'));
        foreach (var code in new[] { 0x23, 0x0D, 0x0A, 0x7F, 0x1E, 0xA0 })
        {
            Assert.False(SourceReadSettings.IsInDelimiterAlphabet((char)code), $"U+{code:X4}");
        }

        foreach (var code in new[] { 0x1F, 0x20, 0x3A, 0x09 })
        {
            Assert.True(SourceReadSettings.IsInDelimiterAlphabet((char)code), $"U+{code:X4}");
        }
    }

    [Fact]
    public void Create_WhenEveryUsableDelimiter_ThenEachIsAccepted()
    {
        Assert.Equal(95, UsableDelimiters.Distinct().Count());
        foreach (var delimiter in UsableDelimiters)
        {
            Assert.Equal(delimiter, Wide(delimiter: delimiter).Delimiter);
        }
    }

    [Theory]
    [InlineData(0x23)] // '#'
    [InlineData(0x0D)] // CR
    [InlineData(0x0A)] // LF
    [InlineData(0x7F)]
    [InlineData(0x1E)]
    [InlineData(0xA0)] // no-break space
    [InlineData(0xE9)] // 'é'
    public void Create_WhenDelimiterOutsideTheAlphabet_ThenArgumentException(int code)
    {
        var delimiter = (char)code;

        var thrown = Assert.Throws<ArgumentException>(() => Wide(delimiter: delimiter));

        Assert.Equal("delimiter", thrown.ParamName);
        Assert.StartsWith(
            $"delimiter U+{code:X4} is not in the v1 delimiter alphabet (TAB, or U+001F through U+007E except '#').",
            thrown.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTriple_WhenDelimiterOutsideTheAlphabet_ThenArgumentException() =>
        Assert.Equal("delimiter", Assert.Throws<ArgumentException>(() => SourceReadSettings.CreateTriple(delimiter: '#')).ParamName);

    // The exception precedence is fixed: null arguments, the quote, the alphabet, the
    // delimiter/quote pair, the encoding, then the shape/ordering pair.
    [Fact]
    public void Create_WhenNullEncodingAndDelimiterOutsideTheAlphabet_ThenArgumentNullFirst() =>
        Assert.Throws<ArgumentNullException>(() =>
            SourceReadSettings.Create(SourceShape.Wide, null!, '#', '"', true, "?", null));

    [Fact]
    public void Create_WhenQuoteUnsupportedAndDelimiterOutsideTheAlphabet_ThenNotSupportedExceptionFirst() =>
        Assert.Throws<NotSupportedException>(() => Wide(delimiter: '#', quote: '\''));

    [Fact]
    public void Create_WhenDelimiterOutsideTheAlphabetAndEncodingUnsupported_ThenTheAlphabetIsReported() =>
        Assert.Equal("delimiter", Assert.Throws<ArgumentException>(() => Wide(encoding: "latin-1", delimiter: '#')).ParamName);

    [Fact]
    public void Create_WhenDelimiterOutsideTheAlphabetAndOrderingInconsistent_ThenTheAlphabetIsReported() =>
        Assert.Equal(
            "delimiter",
            Assert.Throws<ArgumentException>(() =>
                SourceReadSettings.Create(SourceShape.Triple, "utf-8", '#', '"', false, "?", ordering: null)).ParamName);

    [Fact]
    public void Create_WhenDelimiterIsTheQuoteAndEncodingUnsupported_ThenTheConflictIsReported()
    {
        // '"' is in the alphabet, so the delimiter/quote check is the one that refuses it.
        var thrown = Assert.Throws<ArgumentException>(() => Wide(encoding: "latin-1", delimiter: '"'));

        Assert.Equal("delimiter", thrown.ParamName);
        Assert.StartsWith("delimiter must differ from quoteChar.", thrown.Message, StringComparison.Ordinal);
    }
}
