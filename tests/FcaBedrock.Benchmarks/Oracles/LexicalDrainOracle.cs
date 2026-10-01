using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The expected <see cref="DrainSummary"/> of a lexical variant, derived from the variant's own
/// decoded-value definition without opening the file or invoking any reader. The header line and
/// the blank lines are not records, so they contribute nothing; every decoded value is present,
/// because no lexical value is empty or equal to the missing token.
/// </summary>
internal static class LexicalDrainOracle
{
    /// <summary>The summary a correct drain of <paramref name="records"/> records of <paramref name="variant"/> must produce.</summary>
    public static DrainSummary Expected(LexicalVariant variant, long records)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return DrainOracle.Expected(records, variant.Columns, variant.DecodedValue);
    }
}
