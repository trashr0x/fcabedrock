using System.Globalization;
using System.Text;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The string escaping of the private publication transaction record, which needs one unambiguous
/// way to write a file name it can later reconstruct exactly. It has its own owner because the
/// record format must stay readable by later builds whatever happens to a presentation format:
/// the CLI's plan report has the same rules today in its own type, and neither may change the
/// other (D-140).
/// <para>
/// <b>Deliberately not Core's <c>CanonicalJson</c>.</b> That one is a frozen hash contract
/// (D-053/D-069/D-077): every fingerprint in the repository is pinned to its exact bytes, so
/// binding another format to it would couple two unrelated byte locks. It is also internal to Core.
/// </para>
/// <para>
/// Rules: <c>\"</c>, <c>\\</c>, the <c>\b \t \n \f \r</c> shorthands, remaining C0
/// controls as lowercase <c>\u00xx</c>, everything else verbatim. Printable Unicode is
/// preserved and never normalized, and <c>/</c> is NOT escaped (JSON permits <c>\/</c>
/// but does not require it, and a path reads better unescaped).
/// </para>
/// </summary>
internal static class RecordStringEscaping
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

    /// <summary>Appends <paramref name="value"/> as a quoted string literal: the record's string form.</summary>
    public static void AppendLiteral(StringBuilder builder, string value)
    {
        builder.Append('"');
        AppendEscaped(builder, value);
        builder.Append('"');
    }
}
