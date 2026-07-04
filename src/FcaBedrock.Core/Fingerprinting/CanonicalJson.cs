using System.Globalization;
using System.Text;

namespace FcaBedrock.Core.Fingerprinting;

/// <summary>
/// The one place the canonical-JSON byte rules live (D-053/D-069/D-077).
/// Hand-rolled, not a JSON library: the canonical form is a durable hash
/// contract and must not drift with a library upgrade (the D-075 rationale).
/// Rules: compact (no insignificant whitespace); one escaping rule
/// (<c>\"</c>, <c>\\</c>, the <c>\b \t \n \f \r</c> shorthands, remaining C0
/// controls as lowercase <c>\u00xx</c>, raw UTF-8 otherwise); numbers as the
/// parsed value in invariant shortest round-trippable form (P-11). Callers emit
/// object keys pre-sorted (the vocabulary is fixed ASCII); the canonical-bytes
/// golden locks the result.
/// </summary>
internal static class CanonicalJson
{
    /// <summary>Appends <paramref name="value"/> as a quoted, escaped JSON string.</summary>
    public static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
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

        builder.Append('"');
    }

    /// <summary>
    /// Appends the parsed numeric value in invariant, shortest round-trippable
    /// form — <c>30</c>, <c>30.0</c> and <c>3e1</c> all collapse to <c>30</c>
    /// (D-053/P-11). Canonical JSON has no NaN/∞ representation; cut validation
    /// guarantees finiteness, so a non-finite value here is a programmer error.
    /// </summary>
    public static void AppendNumber(StringBuilder builder, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Canonical JSON cannot encode a non-finite number.");
        }

        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Appends an integer in invariant decimal form.</summary>
    public static void AppendNumber(StringBuilder builder, int value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Appends <c>true</c>/<c>false</c>.</summary>
    public static void AppendBool(StringBuilder builder, bool value) =>
        builder.Append(value ? "true" : "false");
}
