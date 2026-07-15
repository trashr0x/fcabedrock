using System.Collections.Immutable;
using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;

namespace FcaBedrock.Sources;

/// <summary>
/// Shared schema-reading and schema-equality helpers for the source sessions
/// (D-098 stage 2). Reads the same header/first-row metadata a
/// <see cref="WideCsvSource"/> / <see cref="TripleCsvSource"/> would, using
/// identical Sep tokenization, so a session's cached schema matches its bound
/// source's.
/// </summary>
internal static class CsvSourceSchema
{
    public static async ValueTask<SourceSchema> ReadAsync(
        Func<Stream> openStream, char delimiter, bool hasHeader, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = Sep.New(delimiter)
            .Reader(o => o with { HasHeader = hasHeader, Unescape = true, Trim = SepTrim.Outer, DisableColCountCheck = true })
            .From(openStream());
        if (reader.HasHeader)
        {
            // The cached header is immutable storage (D-098): the session hands the same snapshot to
            // every caller, so no downstream cast can mutate it.
            return new SourceSchema(reader.Header.ColNames.Count, reader.Header.ColNames.ToImmutableArray());
        }

        foreach (var row in reader)
        {
            return new SourceSchema(row.ColCount);
        }

        return new SourceSchema(0);
    }

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
