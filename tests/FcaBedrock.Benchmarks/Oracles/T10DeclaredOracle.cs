using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// The independent oracle for the T10 declared case.
/// <para>
/// Derived from the corpus definition plus the documented triple semantics — an object per subject
/// in first-appearance order, observations unioned onto it, an exact duplicate contributing nothing
/// new, an absent or unmatched predicate contributing no cross, and open-ended cut geometry for the
/// numeric predicate. It calls no part of the reader, planner, emitter, or writer.
/// </para>
/// <para>
/// Because the declared spec's column set does not depend on the data, this single expectation is
/// the oracle for <b>both</b> physical layouts: proving each layout against it proves them equal to
/// each other, which is the property the two layouts exist to test.
/// </para>
/// </summary>
internal static class T10DeclaredOracle
{
    /// <summary>The crossed formal-attribute ids for one subject, ascending.</summary>
    public static List<int> Crosses(long subject)
    {
        var ids = new List<int>(5);

        foreach (var tissue in T10Corpus.DistinctTissues(subject))
        {
            ids.Add(T10Specs.TissueBase + IndexIn(T10Corpus.TissueDomain, tissue));
        }

        foreach (var signal in T10Corpus.DistinctSignals(subject))
        {
            ids.Add(T10Specs.SignalBase + IndexIn(T10Corpus.SignalDomain, signal));
        }

        // The subject's three Stage rows are two raw spellings of ONE numeric value, so they all
        // land in the same bin and contribute a single cross.
        ids.Add(T10Specs.StageBase + DatExpectation.BinIndex(T10Corpus.Stage(subject), T10Specs.StageCuts));

        // The unmatched predicate contributes nothing at all; the subject still exists as an object.
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// The expectation for a T10 tier of <paramref name="records"/> input rows: one object per
    /// subject, in first-appearance order.
    /// </summary>
    public static ContextExpectation Expect(long records, CancellationToken cancellationToken = default) =>
        DatExpectation.Stream(T10Corpus.Subjects(records), Crosses, cancellationToken);

    private static int IndexIn(IReadOnlyList<string> domain, string value)
    {
        for (var i = 0; i < domain.Count; i++)
        {
            if (string.Equals(domain[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"'{value}' is not in the declared domain.");
    }
}
