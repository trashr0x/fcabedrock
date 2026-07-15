using System.Globalization;

namespace FcaBedrock.Core.Fingerprinting;

/// <summary>
/// The one §14 number-identity rule, made public for the M4 numeric surface
/// (D-096/G-6). <see cref="Format"/> reproduces the existing canonical encoder
/// (<see cref="CanonicalJson.AppendNumber(System.Text.StringBuilder,double)"/>)
/// byte-for-byte — invariant, shortest round-trippable, so <c>90</c>, <c>90.0</c>,
/// and <c>9e1</c> all render <c>90</c> — and, like that encoder, formats
/// <c>-0.0</c> as <c>"-0"</c> so the <c>fp_format = 1</c> bytes never move.
/// <see cref="CanonicalizeZero"/> is applied at the <b>new</b> M4 numeric identity
/// sites only (numeric <c>free_per_value</c> keys/values, and — at slice F —
/// numeric <c>restrict_to</c> entries and computed cuts), so a signed zero never
/// leaks into a bin identity, label, or hash. The two type-correct chains
/// (D-096/G-6):
/// <list type="bullet">
/// <item><b>text-sourced:</b> <c>TryParse(text, culture)</c> →
/// <c>CanonicalizeZero(value)</c> → <c>Format(value)</c>;</item>
/// <item><b>already-numeric:</b> <c>CanonicalizeZero(value)</c> →
/// <c>Format(value)</c>.</item>
/// </list>
/// </summary>
public static class CanonicalNumber
{
    /// <summary>
    /// The parsed value in invariant, shortest round-trippable form — identical
    /// bytes to the canonical JSON number encoder, <c>-0</c> included. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for a non-finite value (canonical
    /// JSON has no NaN/∞ representation), so callers zero-canonicalize but never
    /// pass NaN/±∞.
    /// </summary>
    public static string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A canonical number must be finite.");
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Maps both signed zeros to positive zero and leaves every other value
    /// unchanged (D-096): <c>-0</c> and <c>+0</c> collapse to one identity so a
    /// signed zero never reaches a key, label, or hash. Applied at the M4 numeric
    /// production sites; <see cref="Format"/> itself never canonicalizes (it must
    /// reproduce the existing encoder, G-6).
    /// </summary>
    public static double CanonicalizeZero(double value) => value == 0.0 ? 0.0 : value;

    /// <summary>
    /// Parses <paramref name="text"/> under <paramref name="culture"/> using
    /// <see cref="NumberStyles.Float"/> (P-11: never ambient), accepting only a
    /// finite result. Returns <see langword="false"/> — with <paramref name="value"/>
    /// set to <c>0</c> — on a parse failure or a non-finite result. Does <b>not</b>
    /// canonicalize zero itself; the caller applies <see cref="CanonicalizeZero"/>,
    /// keeping the parse reusable (D-096/G-6).
    /// </summary>
    public static bool TryParse(string text, CultureInfo culture, out double value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (double.TryParse(text, NumberStyles.Float, culture, out value) && double.IsFinite(value))
        {
            return true;
        }

        value = 0.0;
        return false;
    }
}
