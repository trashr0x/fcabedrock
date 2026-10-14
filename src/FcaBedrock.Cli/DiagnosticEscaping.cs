using System.Globalization;
using System.Text;

namespace FcaBedrock.Cli;

/// <summary>
/// The escaping of the stderr diagnostic grammar (spec §16.4, D-122 part 3): one transport pass
/// over message prose, and the quoted form of a string location field.
/// <para>
/// Both forms write a backslash as <c>\\</c>, keep the short escapes <c>\b \t \n \f \r</c> for
/// those five controls, and write every other C0 control (U+0000 to U+001F), DEL (U+007F), every
/// C1 control (U+0080 to U+009F) and the line and paragraph separators (U+2028, U+2029) as a
/// lowercase four-digit <c>\u</c> escape. So no control code and no line or paragraph separator
/// reaches stderr raw, and each diagnostic stays one line. Every other character, printable
/// non-BMP text included, is written verbatim and never normalized.
/// </para>
/// <para>
/// The two forms differ only in the double quote. Prose has no surrounding quotes, so a quote is
/// written as itself. A location field is a quoted JSON string literal, so a quote is escaped and
/// the field parses back to exactly the original string.
/// </para>
/// <para>
/// This is a transport, not a reader of the message. A producer that already quotes and escapes
/// a sample (the D-120 invalid-name samples) owns that representation, and its backslashes are
/// escaped once more like any other. Nothing here recognizes or undoes an escape it did not write.
/// </para>
/// <para>
/// Deliberately separate from <see cref="JsonStringEscaping"/>, whose narrower contract the plan
/// report and the private publication record keep, and from Core's <c>CanonicalJson</c>, which is
/// a fingerprint contract.
/// </para>
/// </summary>
internal static class DiagnosticEscaping
{
    /// <summary>Appends <paramref name="value"/> as unquoted message prose.</summary>
    public static void AppendProse(StringBuilder builder, string value) => Append(builder, value, escapeQuote: false);

    /// <summary>Appends <paramref name="value"/> as a quoted JSON string literal: the string-location form.</summary>
    public static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        Append(builder, value, escapeQuote: true);
        builder.Append('"');
    }

    private static void Append(StringBuilder builder, string value, bool escapeQuote)
    {
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"' when escapeQuote:
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (IsEscapedAsUnicode(ch))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }
    }

    // C0, DEL and C1 (the short-escape controls never get here), plus the two separators that
    // some readers split lines on.
    private static bool IsEscapedAsUnicode(char ch) =>
        ch < 0x20 || ch is >= (char)0x7F and <= (char)0x9F || ch is (char)0x2028 or (char)0x2029;
}
