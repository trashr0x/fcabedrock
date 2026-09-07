using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The independent oracle for the long-text declared case.
/// <para>
/// Two nominal attributes over eight-value domains, so every row crosses exactly two columns
/// whatever the size of the strings behind them. That is the point of the case: the incidence is
/// trivial and the <em>text</em> is not, so a difference in cost is attributable to the strings
/// rather than to the shape of the context.
/// </para>
/// </summary>
internal static class LongTextOracle
{
    /// <summary>The crossed formal-attribute ids for one row, ascending.</summary>
    public static List<int> Crosses(long row)
    {
        var ids = new List<int>(2)
        {
            LongTextSpecs.TagBase + LongTextCorpus.TagIndex(row),
            LongTextSpecs.BlobBase + LongTextCorpus.BlobIndex(row),
        };

        ids.Sort();
        return ids;
    }

    /// <summary>The expectation for a long-text tier of <paramref name="records"/> rows.</summary>
    public static ContextExpectation Expect(long records, CancellationToken cancellationToken = default) =>
        DatExpectation.Stream(records, Crosses, cancellationToken);

    /// <summary>
    /// The exact number of UTF-16 code units the <b>distinct</b> retained values of <c>note</c>
    /// would occupy over the first <paramref name="records"/> rows, which is what probe's third
    /// guard accounts for (D-110). Derived by enumerating the generator, never by probing.
    /// </summary>
    public static long ExpectedNoteTextUnits(long records)
    {
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var units = 0L;
        for (var row = 0L; row < records; row++)
        {
            var note = LongTextCorpus.Note(row);
            if (distinct.Add(note))
            {
                units += note.Length;
            }
        }

        return units;
    }
}
