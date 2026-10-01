using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Spec §5.1 missing values over decoded fields (§5.1.1): decoding and trimming come first; an
/// empty value is missing; a nonempty token matches ordinally; an empty token disables token
/// matching only; deliberate quoted whitespace stays present unless it equals the token.
/// </summary>
public sealed class MissingValueTests
{
    private const string Text = "a,b,c,d,e\n ? ,\"?\",\" ? \",?x,\n";

    [Fact]
    public async Task Read_WhenTheTokenIsSet_ThenDecodedValuesEqualToItAndEmptyValuesAreMissing()
    {
        var records = await DrainAsync(WideSession(Opener(Text), hasHeader: true).ReadAsync());

        Assert.Equal(["0:[null,null,< ? >,<?x>,null]"], records.Select(Render));
    }

    [Fact]
    public async Task Read_WhenTheTokenIsEmpty_ThenOnlyEmptyValuesAreMissing()
    {
        var records = await DrainAsync(WideSession(Opener(Text), hasHeader: true, missingToken: string.Empty).ReadAsync());

        Assert.Equal(["0:[<?>,<?>,< ? >,<?x>,null]"], records.Select(Render));
    }

    [Fact]
    public async Task Read_WhenAQuotedValueIsOnlyWhitespace_ThenItIsPresent()
    {
        var records = await DrainAsync(WideSession(Opener("a\n\"" + U(0xA0) + " " + U(0x3000) + "\"\n"), hasHeader: true).ReadAsync());

        Assert.Equal(["0:[<\\u00A0 \\u3000>]"], records.Select(Render));
    }

    [Fact]
    public async Task Read_WhenUnquotedValuesArePaddedWithUnicodeWhitespace_ThenTheTokenMatchesAfterTrimming()
    {
        var text = "a,b\n" + U(0xA0) + "?" + U(0x2003) + ",\t?\t\n";

        var records = await DrainAsync(WideSession(Opener(text), hasHeader: true).ReadAsync());

        Assert.Equal(["0:[null,null]"], records.Select(Render));
    }
}
