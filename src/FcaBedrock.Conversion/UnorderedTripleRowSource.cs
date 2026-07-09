using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// Presents an interleaved (predicate-major) triple stream as one whose rows are contiguous by
/// cleaned subject, in first-appearance order — the slow path for <c>ordering = "unordered"</c>
/// (D-082). It composes with <see cref="Emitter.EmitTripleAsync"/> unchanged: because the rows it
/// yields are already subject-contiguous, that emitter's contiguity check is a no-op and all its
/// subject-validity / classification / union logic applies verbatim. Grouping buffers <i>rows</i>
/// via <see cref="FirstAppearanceGrouping"/>, never the incidence matrix (P-16).
/// </summary>
internal sealed class UnorderedTripleRowSource : ITripleRowSource
{
    private readonly ITripleRowSource _inner;

    public UnorderedTripleRowSource(ITripleRowSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Schema is a property of the underlying source; grouping does not change it.</summary>
    public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _inner.GetSchemaAsync(cancellationToken);

    /// <summary>
    /// Yields the inner rows regrouped subject-contiguous in first-appearance order, truncated at
    /// the first structurally-invalid subject (see <see cref="TruncateAtFirstUnusableSubjectAsync"/>).
    /// Replayable: each call re-reads and re-derives the same order, which the <c>.cxt</c> two-pass
    /// relies on (P-16).
    /// </summary>
    public IAsyncEnumerable<TripleRow> ReadRowsAsync(CancellationToken cancellationToken = default) =>
        FirstAppearanceGrouping.GroupByFirstAppearanceAsync(
            TruncateAtFirstUnusableSubjectAsync(_inner.ReadRowsAsync(cancellationToken), cancellationToken),
            static row => row.Subject,
            cancellationToken);

    // A structural subject error must halt conversion at that source record; rows *after* it must
    // not influence output (D-085). So the prefix handed to the grouper stops at the first unusable
    // subject (inclusive): later rows are never read, mirroring subject_grouped, whose emitter stops
    // pulling at the invalid row. The offending row is yielded last and — being first-seen last —
    // the grouper ranks it last, so EmitTripleAsync reaches it after the valid prefix and raises
    // ObjectKeyValueInvalid there. This only stops reading; it never raises (ObjectNames.IsUsable is
    // the same boundary the emitter halts on). A no-op when every subject is usable.
    private static async IAsyncEnumerable<TripleRow> TruncateAtFirstUnusableSubjectAsync(
        IAsyncEnumerable<TripleRow> rows,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return row;
            if (!ObjectNames.IsUsable(row.Subject))
            {
                yield break;
            }
        }
    }
}
