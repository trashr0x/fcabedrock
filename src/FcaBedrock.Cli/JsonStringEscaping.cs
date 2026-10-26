using System.Globalization;
using System.Text;

namespace FcaBedrock.Cli;

/// <summary>
/// The CLI's own generic JSON string escaping: the quoted strings of the plan report. The private
/// publication transaction record has the same rules in its own type, RecordStringEscaping in
/// FcaBedrock.Orchestration, so neither format can move the other. The §16.4 stderr grammar uses
/// <see cref="DiagnosticEscaping"/> instead, which escapes more controls and leaves a double
/// quote literal in message prose.
/// <para>
/// <b>Deliberately not Core's <c>CanonicalJson</c>.</b> That one is a frozen hash
/// contract (D-053/D-069/D-077): every fingerprint in the repository is pinned to its
/// exact bytes, so binding a presentation surface to it would couple two unrelated byte
/// locks and give a future rendering tweak the power to move fingerprints. It is also
/// internal to Core, and making it public to share ~30 lines of `switch` would be a
/// public-surface change for no benefit (EP-4). The rules below are the same standard
/// JSON rules, restated here where the rendering contract lives.
/// </para>
/// <para>
/// Rules: <c>\"</c>, <c>\\</c>, the <c>\b \t \n \f \r</c> shorthands, remaining C0
/// controls as lowercase <c>\u00xx</c>, everything else verbatim. Printable Unicode is
/// preserved and never normalized, and <c>/</c> is NOT escaped (JSON permits <c>\/</c>
/// but does not require it, and a path reads better unescaped).
/// </para>
/// </summary>
internal static class JsonStringEscaping
{
    /// <summary>Appends <paramref name="value"/> escaped, <b>without</b> surrounding quotes.</summary>
    public static void AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
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
                    if (ch < 0x20)
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

    /// <summary>Appends <paramref name="value"/> as a quoted JSON string literal: the string-location form.</summary>
    public static void AppendLiteral(StringBuilder builder, string value)
    {
        builder.Append('"');
        AppendEscaped(builder, value);
        builder.Append('"');
    }

    /// <summary>The escaped, unquoted form of <paramref name="value"/>.</summary>
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        AppendEscaped(builder, value);
        return builder.ToString();
    }
}
