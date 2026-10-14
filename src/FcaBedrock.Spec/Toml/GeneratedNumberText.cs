using System.Collections.Immutable;
using System.Globalization;
using FcaBedrock.Core.Fingerprinting;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Writes the generated entries of a frozen numeric <c>free_per_value</c> domain for the binding
/// locale (spec §7, D-122 part 10). An entry is the shortest round-trippable text of its canonical
/// number, with the culture's decimal mark, ASCII <c>-</c>, <c>+</c> and <c>E</c>, and no group
/// separator. The product parser must read that text back under the same culture as the same
/// canonical identity. A number whose ASCII-sign text does not read back is written with the
/// culture's own symbols instead, checked the same way. The check is made for every number,
/// because a culture can accept the ASCII signs for one value and refuse them for another
/// (an exponent sign, for example).
/// <para>
/// The text depends on the culture data the runtime supplies, so another configuration can write a
/// different decimal mark or sign for the same number. Its identity, and so every fingerprint, is
/// the same. Authored entries never pass through here: <see cref="SpecFreezer"/> writes them back
/// as authored.
/// </para>
/// </summary>
internal static class GeneratedNumberText
{
    /// <summary>
    /// Formats each retained canonical identity in <paramref name="identities"/>, in order, for
    /// <paramref name="culture"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An identity is not a finite number, or neither spelling reads back as it under
    /// <paramref name="culture"/>: a defect, never authored input, because a calibration retains
    /// only finite numbers it parsed and the culture's own spelling of a number is meant to read back.
    /// </exception>
    public static ImmutableArray<string> Format(IReadOnlyList<string> identities, CultureInfo culture)
    {
        // A private writable copy (Clone resets read-only): the binding culture's own format stays
        // untouched, and no ambient or shared culture state changes.
        var asciiSigns = (NumberFormatInfo)culture.NumberFormat.Clone();
        asciiSigns.NegativeSign = "-";
        asciiSigns.PositiveSign = "+";

        var texts = ImmutableArray.CreateBuilder<string>(identities.Count);
        foreach (var identity in identities)
        {
            texts.Add(FormatOne(identity, culture, asciiSigns));
        }

        return texts.MoveToImmutable();
    }

    private static string FormatOne(string identity, CultureInfo culture, NumberFormatInfo asciiSigns)
    {
        // Retained identities are invariant canonical text (D-096/D-101).
        if (!CanonicalNumber.TryParse(identity, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException(
                $"The retained numeric domain entry '{identity}' is not a finite number; a calibration retains only canonical numeric identities.");
        }

        var value = CanonicalNumber.CanonicalizeZero(parsed);
        var canonical = CanonicalNumber.Format(value);

        // The default format of a double is the shortest round-trippable text: uppercase E, the
        // format's decimal separator and signs, and never a group separator.
        var candidate = value.ToString(asciiSigns);
        if (ReadsBackAs(candidate, culture, canonical))
        {
            return candidate;
        }

        var own = value.ToString(culture);
        if (ReadsBackAs(own, culture, canonical))
        {
            return own;
        }

        throw new InvalidOperationException(
            $"Neither '{candidate}' nor '{own}' reads back as the number {canonical} under culture '{culture.Name}', " +
            "so a frozen spec could not reread it.");
    }

    // The resolver's own reading of a numeric domain entry (§10.3): parse under the binding culture,
    // then canonicalize zero, then compare canonical identities.
    private static bool ReadsBackAs(string text, CultureInfo culture, string canonical) =>
        CanonicalNumber.TryParse(text, culture, out var parsed)
        && string.Equals(CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(parsed)), canonical, StringComparison.Ordinal);
}
