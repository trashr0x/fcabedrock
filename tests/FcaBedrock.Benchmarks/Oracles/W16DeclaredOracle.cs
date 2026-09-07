using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The independent oracle for the W16 declared case.
/// <para>
/// It re-derives the expected incidence from two things only: the corpus's own value definition
/// (<see cref="W16Corpus"/>) and the <em>documented</em> semantics of the spec that case is
/// converted under — open-ended cut geometry, nominal value bins, a dichotomic column crossed by
/// its true value, and an <c>as_attribute</c> missing column appended after its attribute's value
/// bins. It calls no discretizer, no scale, no planner, no emitter, and no writer, which is what
/// makes it evidence rather than a restatement of the code it validates.
/// </para>
/// </summary>
internal static class W16DeclaredOracle
{
    /// <summary>The crossed formal-attribute ids for one row, ascending.</summary>
    public static List<int> Crosses(long row)
    {
        var ids = new List<int>(6);

        // n_ties: skipped entirely when missing — the default missing_policy emits no cross.
        if (!W16Corpus.TiesIsMissing(row))
        {
            ids.Add(W16Specs.TiesBase + DatExpectation.BinIndex(W16Corpus.Ties(row), W16Specs.TiesCuts));
        }

        ids.Add(W16Specs.C0Base + W16Corpus.CategoryIndex(row, 0));

        // Dichotomic: exactly one column, crossed only by the true value.
        if (W16Corpus.BinaryIsYes(row, 0))
        {
            ids.Add(W16Specs.B0Id);
        }

        // c3 carries missing_policy = "as_attribute", so a missing cell crosses the missing column
        // instead of producing no cross at all.
        ids.Add(W16Corpus.C3IsMissing(row)
            ? W16Specs.C3MissingId
            : W16Specs.C3Base + W16Corpus.CategoryIndex(row, 3));

        ids.Add(W16Specs.SkewBase + DatExpectation.BinIndex(W16Corpus.Skew(row), W16Specs.SkewCuts));
        ids.Add(W16Specs.C6Base + W16Corpus.CategoryIndex(row, 6));

        ids.Sort();
        return ids;
    }

    /// <summary>The expectation for a W16 tier of <paramref name="records"/> rows: one object per row.</summary>
    public static ContextExpectation Expect(long records, CancellationToken cancellationToken = default) =>
        DatExpectation.Stream(records, Crosses, cancellationToken);

    /// <summary>
    /// The zero-based open-ended cut bin index. Retained as a named alias of the shared helper
    /// because the W16 layout constants and this indexing rule are read together.
    /// </summary>
    public static int BinIndex(int value, IReadOnlyList<int> cuts) => DatExpectation.BinIndex(value, cuts);
}
