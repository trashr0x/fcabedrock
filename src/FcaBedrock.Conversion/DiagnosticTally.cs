using System.Text;

namespace FcaBedrock.Conversion;

/// <summary>
/// Per-attribute (or per-source) occurrence count plus a bounded sample of distinct
/// examples, for aggregated data-phase diagnostics (§16.4, D-059). Deterministic
/// given source order and bounded metadata, never the matrix (EP-16). Shared by
/// the emitter and the calibrator (D-098 hoist).
/// <para>
/// The count is every recorded occurrence. The sample keeps at most three distinct examples, in
/// the order they were first recorded, and membership is checked only against those three
/// (ordinal, EP-12): no set of every distinct value is kept, so a tally stays this size whatever
/// the input.
/// </para>
/// </summary>
internal sealed class DiagnosticTally
{
    private const int SampleCap = 3;

    // A renamed key keeps its assigned name beside it; every other example has Renamed = null.
    private readonly List<(string Value, string? Renamed)> _sample = new(SampleCap);

    /// <summary>The number of recorded occurrences.</summary>
    public long Count { get; private set; }

    /// <summary>
    /// The retained examples, comma-joined. Each value is wrapped in double quotes with exactly the
    /// four escapes D-120 pins for invalid-name samples (backslash, double quote, CR, LF), so a
    /// comma, a quote or a line break inside a value cannot be mistaken for the list's own
    /// punctuation and an actual line break stays distinct from a typed backslash-n. A renamed key
    /// reads <c>"key"→"name"</c>. Nothing else is escaped: a host that escapes the whole message
    /// for its own output escapes these backslashes again (§16.4). Values are never shortened.
    /// </summary>
    public string Sample
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var (value, renamed) in _sample)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                AppendQuoted(builder, value);
                if (renamed is not null)
                {
                    builder.Append('→');
                    AppendQuoted(builder, renamed);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Records one occurrence of <paramref name="value"/>.</summary>
    public void Record(string value) => Record(value, renamed: null);

    /// <summary>
    /// Records one occurrence of <paramref name="key"/> being given the object name
    /// <paramref name="name"/>; repeats of the same pair share one example.
    /// </summary>
    public void RecordRename(string key, string name) => Record(key, name);

    // Runs per occurrence on the emit and calibration hot paths (EP-18): it compares against at
    // most three retained entries and allocates nothing, since the list is sized for them up front.
    private void Record(string value, string? renamed)
    {
        Count++;
        if (_sample.Count == SampleCap)
        {
            return;
        }

        foreach (var (kept, keptRenamed) in _sample)
        {
            if (string.Equals(kept, value, StringComparison.Ordinal)
                && string.Equals(keptRenamed, renamed, StringComparison.Ordinal))
            {
                return;
            }
        }

        _sample.Add((value, renamed));
    }

    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append(@"\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append(@"\r"); break;
                case '\n': builder.Append(@"\n"); break;
                default: builder.Append(character); break;
            }
        }

        builder.Append('"');
    }
}
