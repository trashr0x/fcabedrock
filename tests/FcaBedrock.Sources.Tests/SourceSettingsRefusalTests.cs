using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The <see cref="Binding"/>-based source constructors refuse settings no read could honour before
/// any stream is opened, in a fixed order: null arguments, the shape, the quote, the v1 delimiter
/// alphabet (§5.1.1), the delimiter/quote pair, then (triple) the role map. The sessions take
/// <see cref="SourceReadSettings"/>, which are valid by construction.
/// </summary>
public sealed class SourceSettingsRefusalTests
{
    private int _opens;

    private Stream CountingOpen()
    {
        _opens++;
        return ScriptedStream.Utf8("a,b\n");
    }

    [Theory]
    [InlineData(0x23)] // '#'
    [InlineData(0x0D)] // CR
    [InlineData(0x0A)] // LF
    [InlineData(0x7F)]
    [InlineData(0x1E)]
    [InlineData(0xA0)] // no-break space
    [InlineData(0xE9)] // 'é'
    public void Construct_WhenTheDelimiterIsOutsideTheAlphabet_ThenArgumentExceptionBeforeAnyStreamIsOpened(int code)
    {
        var expected = $"binding.Delimiter U+{code:X4} is not in the v1 delimiter alphabet (TAB, or U+001F through U+007E except '#').";

        var wide = Assert.Throws<ArgumentException>(() => new WideCsvSource(CountingOpen, WideBinding((char)code)));
        var triple = Assert.Throws<ArgumentException>(() => new TripleCsvSource(CountingOpen, TripleBinding((char)code)));

        Assert.Equal("binding", wide.ParamName);
        Assert.StartsWith(expected, wide.Message, StringComparison.Ordinal);
        Assert.Equal("binding", triple.ParamName);
        Assert.StartsWith(expected, triple.Message, StringComparison.Ordinal);
        Assert.Equal(0, _opens);
    }

    [Fact]
    public void Construct_WhenTheDelimiterIsTheQuote_ThenArgumentExceptionBeforeAnyStreamIsOpened()
    {
        var wide = Assert.Throws<ArgumentException>(() => new WideCsvSource(CountingOpen, WideBinding('"')));
        var triple = Assert.Throws<ArgumentException>(() => new TripleCsvSource(CountingOpen, TripleBinding('"')));

        Assert.StartsWith("binding.Delimiter must differ from binding.QuoteChar.", wide.Message, StringComparison.Ordinal);
        Assert.StartsWith("binding.Delimiter must differ from binding.QuoteChar.", triple.Message, StringComparison.Ordinal);
        Assert.Equal(0, _opens);
    }

    [Fact]
    public void Construct_WhenSeveralSettingsAreWrong_ThenTheFixedOrderDecidesWhichIsReported()
    {
        // Null arguments first.
        Assert.Throws<ArgumentNullException>(() => new WideCsvSource(null!, WideBinding('#')));
        Assert.Throws<ArgumentNullException>(() => new TripleCsvSource(CountingOpen, null!));

        // The shape before the alphabet.
        var shape = Assert.Throws<ArgumentException>(() => new WideCsvSource(CountingOpen, TripleBinding('#')));
        Assert.StartsWith("WideCsvSource requires a wide binding.", shape.Message, StringComparison.Ordinal);

        // The quote before the alphabet.
        Assert.Throws<NotSupportedException>(() => new WideCsvSource(CountingOpen, WideBinding('#') with { QuoteChar = '\'' }));
        Assert.Throws<NotSupportedException>(() => new TripleCsvSource(CountingOpen, TripleBinding('#') with { QuoteChar = '\'' }));

        // The alphabet and the delimiter/quote pair before the triple role map.
        var alphabet = Assert.Throws<ArgumentException>(() => new TripleCsvSource(CountingOpen, TripleBinding('#') with { TripleColumns = null }));
        Assert.StartsWith("binding.Delimiter U+0023", alphabet.Message, StringComparison.Ordinal);
        var pair = Assert.Throws<ArgumentException>(() => new TripleCsvSource(CountingOpen, TripleBinding('"') with { TripleColumns = null }));
        Assert.StartsWith("binding.Delimiter must differ", pair.Message, StringComparison.Ordinal);

        Assert.Equal(0, _opens);
    }

    [Fact]
    public async Task Read_WhenEachUsableDelimiterSeparatesTwoValues_ThenEveryRouteSplitsThem()
    {
        for (var code = 0; code <= 0x7E; code++)
        {
            if (code != 0x09 && (code < 0x1F || code == 0x22 || code == 0x23))
            {
                continue;
            }

            var delimiter = (char)code;
            var (left, right) = delimiter is 'x' or 'y' ? ("p", "q") : ("x", "y");
            var text = left + delimiter + right + "\n";

            var wide = await DrainAsync(new WideCsvSource(Opener(text), WideBinding(delimiter)).ReadAsync());
            var triple = await DrainAsync(new TripleCsvSource(Opener(text), TripleBinding(delimiter)).ReadRowsAsync());

            Assert.Equal([$"0:[<{left}>,<{right}>]"], wide.Select(Render));
            Assert.Equal([$"0:[<{left}>,<{right}>,null]"], triple.Select(Render));
        }
    }
}
