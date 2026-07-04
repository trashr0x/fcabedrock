using System.Globalization;
using System.Text;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Pure TOML text primitives for the canonical writer (D-075): basic-string
/// escaping, bare-key detection, and invariant value formatting. Deliberately
/// hand-rolled and Tomlyn-free so the canonical output form is owned here and
/// cannot drift with a library upgrade; spec §2 makes formatting informative and
/// fingerprints never hash TOML text (D-053), so one fixed form is safe.
/// </summary>
internal static class TomlLiteral
{
    /// <summary>
    /// Renders a TOML basic string, quotes included: <c>\</c>, <c>"</c>, and
    /// control characters escape (<c>\b \t \n \f \r</c>, else <c>\uXXXX</c>);
    /// printable non-ASCII passes through raw with no normalization.
    /// </summary>
    internal static string FormatString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append(@"\b");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\f':
                    builder.Append(@"\f");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                default:
                    if (ch < ' ' || ch == '\u007F')
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)ch:X4}");
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>
    /// Renders a TOML key: bare when it matches <c>[A-Za-z0-9_-]+</c>, otherwise
    /// a quoted basic string.
    /// </summary>
    internal static string FormatKey(string key) => IsBareKey(key) ? key : FormatString(key);

    /// <summary>
    /// Renders a double invariantly and shortest-round-trippably: finite integral
    /// values in the exact-integer range emit as bare TOML integers (matching the
    /// spec's own <c>cuts = [30, 40, 50]</c>, §11.2); the reader accepts both
    /// integer and float nodes for double fields, so the form is round-trip
    /// stable. Non-finite values use the TOML spellings.
    /// </summary>
    internal static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        // 2^53: beyond this doubles cannot represent every integer, so the float
        // form is the honest rendering.
        if (double.IsInteger(value) && Math.Abs(value) <= 9007199254740992d)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        var text = value.ToString(CultureInfo.InvariantCulture);

        // TOML floats need a '.' or exponent; integral values were handled above,
        // so this only guards huge integral magnitudes like 1e17 → "1E+17".
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal)
            ? text
            : text + ".0";
    }

    /// <summary>Renders a TOML integer.</summary>
    internal static string FormatLong(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Renders a TOML boolean.</summary>
    internal static string FormatBool(bool value) => value ? "true" : "false";

    /// <summary>Renders a single-character field (<c>delimiter</c>/<c>quote_char</c>) as a basic string.</summary>
    internal static string FormatChar(char value) => FormatString(value.ToString());

    /// <summary>
    /// Renders an RFC 3339 offset date-time: seconds always, fractional seconds
    /// only when non-zero (trailing zeros trimmed), zero offset as <c>Z</c>.
    /// </summary>
    internal static string FormatDateTime(DateTimeOffset value)
    {
        var builder = new StringBuilder(35);
        builder.Append(value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            builder.Append('.').Append(value.ToString("fffffff", CultureInfo.InvariantCulture).TrimEnd('0'));
        }

        if (value.Offset == TimeSpan.Zero)
        {
            builder.Append('Z');
        }
        else
        {
            builder.Append(value.Offset < TimeSpan.Zero ? '-' : '+');
            builder.Append(value.Offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool IsBareKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        foreach (var ch in key)
        {
            if (ch is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
