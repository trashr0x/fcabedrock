using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// Schema-value equality for the source sessions (D-098 stage 2). Schema <em>reading</em>
/// belongs to <see cref="CsvReadPipeline"/>, which every schema and record path now shares,
/// so a session's cached schema and its bound source's records cannot disagree about the
/// header or where the data starts.
/// </summary>
internal static class CsvSourceSchema
{
    // Ordinal schema-value equality (P-12): same column count and the same header
    // (both absent, or the same ordinal name sequence).
    public static bool Equal(SourceSchema a, SourceSchema b)
    {
        if (a.ColumnCount != b.ColumnCount)
        {
            return false;
        }

        if (a.Header is null || b.Header is null)
        {
            return a.Header is null && b.Header is null;
        }

        if (a.Header.Count != b.Header.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Header.Count; i++)
        {
            if (!string.Equals(a.Header[i], b.Header[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
