using System.Runtime.CompilerServices;

namespace FcaBedrock.Conversion;

/// <summary>
/// Reorders a row stream so rows sharing a key are contiguous and the groups appear in
/// <b>first-appearance order</b> of the key — the shared core of the slow (non-single-pass) path
/// for triple <c>ordering = "unordered"</c> (D-082) and, later, wide <c>duplicate_object_policy =
/// "dedupe"</c> (D-083). It buffers input rows in memory and stable-sorts them by each distinct
/// key's first-appearance rank; the buffer is fully encapsulated behind the
/// <see cref="IAsyncEnumerable{T}"/>→<see cref="IAsyncEnumerable{T}"/> transform, so a future
/// temp-file/spool backend can replace the storage strategy without changing semantics or any
/// caller. It buffers <i>rows</i>, never the incidence matrix (P-16); crosses are still accumulated
/// one object at a time downstream in the emitter.
/// <para>
/// The component is deliberately <b>validity-agnostic</b>: it never inspects a key for "usability"
/// and never throws (a <c>null</c> key is ranked like any other, by its own first appearance). Any
/// structural-error boundary — e.g. an unusable triple subject — is the caller's concern: the
/// caller truncates its input at that row before grouping, so no later row is reordered ahead of an
/// earlier structural error, and the diagnostic stays owned by the emitter.
/// </para>
/// </summary>
internal static class FirstAppearanceGrouping
{
    /// <summary>
    /// Yields <paramref name="rows"/> reordered so that all rows with the same
    /// <paramref name="keyOf"/> value are contiguous, groups ordered by first appearance of the key,
    /// with input order preserved within each group. Ordinal keying (P-12) keeps ranks
    /// machine-stable.
    /// </summary>
    public static async IAsyncEnumerable<TRow> GroupByFirstAppearanceAsync<TRow>(
        IAsyncEnumerable<TRow> rows,
        Func<TRow, string?> keyOf,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(keyOf);

        // Buffer the input, assigning each distinct key a first-appearance rank over a single
        // counter that also ranks the null key — so null and non-null keys interleave by true first
        // appearance. Ordinal comparison (P-12) is the string-identity rule for grouping.
        var buffer = new List<(TRow Row, int Rank)>();
        var rankByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var nullRank = -1;
        var next = 0;

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var key = keyOf(row);
            int rank;
            if (key is null)
            {
                if (nullRank < 0)
                {
                    nullRank = next++;
                }

                rank = nullRank;
            }
            else if (!rankByKey.TryGetValue(key, out rank))
            {
                rank = next++;
                rankByKey[key] = rank;
            }

            buffer.Add((row, rank));
        }

        // Enumerable.OrderBy is a documented stable sort: equal ranks keep their insertion (source)
        // order, so within a group the input order is preserved.
        foreach (var (row, _) in buffer.OrderBy(entry => entry.Rank))
        {
            yield return row;
        }
    }
}
