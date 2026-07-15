namespace FcaBedrock.Conversion;

/// <summary>
/// Per-attribute (or per-source) occurrence count plus a bounded first-observed
/// sample, for aggregated data-phase diagnostics (§16.4, D-059). Deterministic
/// given source order and bounded metadata — never the matrix (P-16). Shared by
/// the emitter and the calibrator (D-098 hoist).
/// </summary>
internal sealed class DiagnosticTally
{
    private const int SampleCap = 3;
    private readonly List<string> _sample = [];

    /// <summary>The number of recorded occurrences.</summary>
    public long Count { get; private set; }

    /// <summary>A comma-joined bounded sample of the first observed values.</summary>
    public string Sample => string.Join(", ", _sample);

    /// <summary>Records one occurrence of <paramref name="value"/>.</summary>
    public void Record(string value)
    {
        Count++;
        if (_sample.Count < SampleCap)
        {
            _sample.Add(value);
        }
    }
}
