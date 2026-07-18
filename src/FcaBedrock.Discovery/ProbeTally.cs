namespace FcaBedrock.Discovery;

/// <summary>
/// Occurrence count plus a bounded first-observed sample, for probe's two aggregated warnings
/// (§16.4, D-111). One diagnostic carrying "how many, and here are the first few" — never one
/// per attribute, so a 10,000-column source cannot produce 10,000 warnings.
/// <para>
/// The same shape and sample cap as Conversion's <c>DiagnosticTally</c>, re-implemented rather
/// than shared because Discovery must not reference Conversion (D-109) and neither package
/// should grow a public tally type to avoid ~15 lines (P-3/P-4). The duplication is
/// deliberate and local.
/// </para>
/// </summary>
internal sealed class ProbeTally
{
    private const int SampleCap = 3;
    private readonly List<string> _sample = [];

    /// <summary>The number of recorded occurrences.</summary>
    public int Count { get; private set; }

    /// <summary>Whether anything was recorded — the flush condition.</summary>
    public bool Any => Count > 0;

    /// <summary>A comma-joined sample of the first <see cref="SampleCap"/> recorded values.</summary>
    public string Sample => string.Join(", ", _sample);

    /// <summary>
    /// Records one occurrence. Called in physical attribute order, so both the count and the
    /// sample are functions of the schema alone (D-112).
    /// </summary>
    public void Record(string value)
    {
        Count++;
        if (_sample.Count < SampleCap)
        {
            _sample.Add(value);
        }
    }
}
