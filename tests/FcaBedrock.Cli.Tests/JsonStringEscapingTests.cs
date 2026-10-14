using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The generic JSON string escaping behind the plan report and the private publication control
/// record. Its contract is narrower than the diagnostic transport's on purpose: it escapes a
/// double quote even without surrounding quotes, and it leaves DEL, C1 and the line and paragraph
/// separators verbatim, so neither consumer's bytes move when the diagnostic policy does.
/// </summary>
public sealed class JsonStringEscapingTests
{
    // DEL, the two C1 endpoints, NEL, and the line and paragraph separators.
    private static readonly string Verbatim = new([(char)0x7F, (char)0x80, (char)0x85, (char)0x9F, (char)0x2028, (char)0x2029]);

    [Fact]
    public void AppendLiteral_WhenTheValueCarriesCharactersOutsideTheJsonEscapeSet_ThenTheyStayVerbatim()
    {
        var builder = new StringBuilder();

        JsonStringEscaping.AppendLiteral(builder, "q\"b\\n\n\u0001" + Verbatim);

        Assert.Equal("\"q\\\"b\\\\n\\n\\u0001" + Verbatim + "\"", builder.ToString());
    }

    [Fact]
    public void AppendEscaped_WhenTheValueCarriesADoubleQuote_ThenItIsEscapedWithoutSurroundingQuotes()
    {
        var builder = new StringBuilder();

        JsonStringEscaping.AppendEscaped(builder, "a\"b");

        Assert.Equal("a\\\"b", builder.ToString());
    }
}
