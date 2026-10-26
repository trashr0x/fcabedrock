using System.Text;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The string escaping of the private publication transaction record. Its rules are the plan
/// report's today, held in their own type so the record format stays readable by later builds
/// whatever happens to a presentation format: it escapes a double quote even without surrounding
/// quotes, and it leaves DEL, C1 and the line and paragraph separators verbatim.
/// </summary>
public sealed class RecordStringEscapingTests
{
    // DEL, the two C1 endpoints, NEL, and the line and paragraph separators.
    private static readonly string Verbatim = new([(char)0x7F, (char)0x80, (char)0x85, (char)0x9F, (char)0x2028, (char)0x2029]);

    [Fact]
    public void AppendLiteral_WhenAValueHoldsQuotesBackslashesAndControls_ThenTheRecordFormIsWritten()
    {
        var builder = new StringBuilder();

        RecordStringEscaping.AppendLiteral(builder, "q\"b\\n\n\u0001\t\r\b\f" + Verbatim);

        Assert.Equal("\"q\\\"b\\\\n\\n\\u0001\\t\\r\\b\\f" + Verbatim + "\"", builder.ToString());
    }

    [Fact]
    public void AppendEscaped_WhenAValueHoldsAQuote_ThenOnlyItIsEscaped()
    {
        var builder = new StringBuilder();

        RecordStringEscaping.AppendEscaped(builder, "a\"b/c");

        Assert.Equal("a\\\"b/c", builder.ToString());
    }
}
