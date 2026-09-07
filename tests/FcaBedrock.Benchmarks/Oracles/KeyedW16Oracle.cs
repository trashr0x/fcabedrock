using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The independent oracle for the keyed-wide dedupe case.
/// <para>
/// It is deliberately built <em>on top of</em> the plain W16 oracle rather than beside it. The keyed
/// spec declares the same attributes in the same order over the same values, so the formal-attribute
/// layout is identical and a deduped object's crosses are exactly the <b>union</b> of its member
/// rows' plain W16 crosses (§6.1: later rows' crosses union onto the first). Stating the expectation
/// that way makes the dedupe semantics the only thing this oracle asserts — everything else it
/// inherits from an expectation already proved against a different conversion.
/// </para>
/// <para>
/// Object order is first-occurrence order, and the first occurrence of key <c>k{i}</c> is row
/// <c>i</c>, so objects come out in ascending key index — the same order the ids below are indexed
/// by.
/// </para>
/// </summary>
internal static class KeyedW16Oracle
{
    /// <summary>
    /// The crossed formal-attribute ids for object <paramref name="objectIndex"/>, ascending and
    /// distinct: the union over the rows that share its key.
    /// </summary>
    public static List<int> Crosses(long objectIndex, long records)
    {
        var union = new HashSet<int>();
        foreach (var row in KeyedW16Corpus.RowsOf(objectIndex, records))
        {
            foreach (var id in W16DeclaredOracle.Crosses(row))
            {
                union.Add(id);
            }
        }

        var ids = union.ToList();
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// The expectation for a keyed tier of <paramref name="records"/> rows — one object per distinct
    /// key, so <c>records / RowsPerObject</c> objects.
    /// </summary>
    public static ContextExpectation Expect(long records, CancellationToken cancellationToken = default) =>
        DatExpectation.Stream(
            KeyedW16Corpus.Objects(records),
            objectIndex => Crosses(objectIndex, records),
            cancellationToken);
}
