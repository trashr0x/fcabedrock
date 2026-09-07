using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The independent oracle for the Ads-width declared case.
/// <para>
/// Derived from the corpus's value definition plus the documented semantics of the spec it is
/// converted under: open-ended cut geometry on the three numeric columns, and a dichotomic column
/// per flag crossed only by its true value. It calls no discretizer, no scale, no planner, no
/// emitter, and no writer.
/// </para>
/// <para>
/// A width case's incidence is <em>sparse</em>: a row crosses three numeric bins, at most the local
/// and class flags, and only the handful of term flags that happen to be set. That is the property
/// worth checking — a bug that crossed every declared column would still produce a plausible-looking
/// file, and only an expectation derived from the data can tell the difference.
/// </para>
/// </summary>
internal static class AdsOracle
{
    /// <summary>The crossed formal-attribute ids for one row, ascending.</summary>
    public static List<int> Crosses(long row)
    {
        // Three numeric bins are always crossed; the flags are sparse, so a small initial capacity
        // is the honest one.
        var ids = new List<int>(32)
        {
            AdsSpecs.HeightBase + DatExpectation.BinIndex(AdsCorpus.Height(row), AdsSpecs.SizeCuts),
            AdsSpecs.WidthBase + DatExpectation.BinIndex(AdsCorpus.Width(row), AdsSpecs.SizeCuts),
            AdsSpecs.AspectBase + DatExpectation.BinIndex(
                Determinism.HundredthsValue(AdsCorpus.AspectHundredths(row)), AdsSpecs.AspectCuts),
        };

        if (AdsCorpus.IsLocal(row))
        {
            ids.Add(AdsSpecs.LocalId);
        }

        for (var term = 0; term < AdsCorpus.TermColumns; term++)
        {
            if (AdsCorpus.TermIsSet(row, term))
            {
                ids.Add(AdsSpecs.TermBase + term);
            }
        }

        if (AdsCorpus.IsAd(row))
        {
            ids.Add(AdsSpecs.ClassId);
        }

        ids.Sort();
        return ids;
    }

    /// <summary>The expectation for an Ads-width tier of <paramref name="records"/> rows.</summary>
    public static ContextExpectation Expect(long records, CancellationToken cancellationToken = default) =>
        DatExpectation.Stream(records, Crosses, cancellationToken);
}
