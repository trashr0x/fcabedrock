using System.Globalization;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The pinned arithmetic every synthetic corpus is generated from.
/// <para>
/// A benchmark corpus must regenerate byte-for-byte on any machine, runtime, and OS, forever —
/// otherwise a later comparison silently measures different work. So the value stream is defined by
/// <b>explicit integer arithmetic</b> rather than by any runtime pseudo-random source:
/// <see cref="System.Random"/> is documented as free to change its algorithm between .NET versions
/// and is unseeded-per-thread by default, and even a seeded instance is a promise .NET does not
/// make. The mixing function below is the SplitMix64 finalizer, written out here so the corpus's
/// definition lives in this repository rather than in a dependency (P-7/P-11).
/// </para>
/// </summary>
internal static class Determinism
{
    /// <summary>
    /// The SplitMix64 finalizer: a fixed, reversible avalanche over 64 bits. Deterministic on every
    /// platform because it uses only wrapping unsigned arithmetic and shifts.
    /// </summary>
    public static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    /// <summary>
    /// A stream value for row <paramref name="row"/> and column <paramref name="salt"/>. The salt
    /// keeps the columns independent without a per-column generator, and multiplying the row by a
    /// prime before adding the salt keeps <c>(row, salt)</c> pairs from colliding.
    /// </summary>
    public static ulong Draw(long row, int salt) => Mix((unchecked((ulong)row) * 0x100000001B3UL) + (ulong)(uint)salt);

    /// <summary>
    /// Renders <paramref name="hundredths"/> as a fixed two-decimal invariant decimal, by integer
    /// composition rather than by floating-point formatting. Composing the text keeps the corpus
    /// free of any binary64 rounding question: the file says exactly what the generator meant, and
    /// the oracle can re-derive the same value from the same integer.
    /// </summary>
    public static string FormatHundredths(long hundredths)
    {
        var negative = hundredths < 0;
        var magnitude = negative ? unchecked((ulong)(-hundredths)) : (ulong)hundredths;
        var whole = magnitude / 100;
        var fraction = magnitude % 100;
        var sign = negative ? "-" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{whole}.{fraction:D2}");
    }

    /// <summary>The numeric value <see cref="FormatHundredths"/> renders, for oracle-side comparison.</summary>
    public static double HundredthsValue(long hundredths) => hundredths / 100.0;
}
