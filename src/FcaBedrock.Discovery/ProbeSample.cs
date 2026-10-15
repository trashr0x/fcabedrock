using System.Globalization;
using System.Text;

namespace FcaBedrock.Discovery;

/// <summary>
/// The bounded, unambiguous form in which probe quotes an attribute name or a category in its
/// omission messages and saved notices (§7.1).
/// <para>
/// A sample is double-quoted. Inside the quotes a backslash is written <c>\\</c> and a double
/// quote <c>\"</c>; backspace, tab, LF, form feed and CR keep the short escapes <c>\b</c>,
/// <c>\t</c>, <c>\n</c>, <c>\f</c> and <c>\r</c>; every other C0 control, DEL, every C1 control and
/// U+2028 and U+2029 are written as a lowercase four-digit <c>\u</c> escape. Everything else is
/// written as itself. A sample keeps at most 128 UTF-16 code units of its value, one fewer when
/// the cut would split a surrogate pair, and a cut sample reads
/// <c>prefix "…" (length &lt;n&gt; UTF-16 code units)</c>, with the full length. The escaping is
/// this text's own: a host that escapes the message again for its own output escapes these
/// backslashes once more.
/// </para>
/// </summary>
internal static class ProbeSample
{
    /// <summary>The most UTF-16 code units of a value a sample keeps.</summary>
    public const int MaxUnits = 128;

    /// <summary>The most samples one message or one description carries.</summary>
    public const int Cap = 3;

    /// <summary>Returns <paramref name="value"/> in sample form.</summary>
    public static string Format(string value)
    {
        var kept = value.Length <= MaxUnits ? value.Length : ClippedLength(value);
        var builder = new StringBuilder(kept + 48);
        if (kept < value.Length)
        {
            builder.Append("prefix ");
        }

        builder.Append('"');
        foreach (var character in value.AsSpan(0, kept))
        {
            Append(builder, character);
        }

        builder.Append('"');
        if (kept < value.Length)
        {
            builder.Append(CultureInfo.InvariantCulture, $" (length {value.Length} UTF-16 code units)");
        }

        return builder.ToString();
    }

    private static int ClippedLength(string value) =>
        char.IsHighSurrogate(value[MaxUnits - 1]) && char.IsLowSurrogate(value[MaxUnits]) ? MaxUnits - 1 : MaxUnits;

    private static void Append(StringBuilder builder, char character)
    {
        switch (character)
        {
            case '\\': builder.Append(@"\\"); break;
            case '"': builder.Append("\\\""); break;
            case '\b': builder.Append(@"\b"); break;
            case '\t': builder.Append(@"\t"); break;
            case '\n': builder.Append(@"\n"); break;
            case '\f': builder.Append(@"\f"); break;
            case '\r': builder.Append(@"\r"); break;
            default:
                if (character < 0x20 || character is >= (char)0x7F and <= (char)0x9F or (char)0x2028 or (char)0x2029)
                {
                    builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
                }
                else
                {
                    builder.Append(character);
                }

                break;
        }
    }
}
