using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// A replayable stream of object records. <see cref="ReadAsync"/> may be called
/// more than once (each call re-reads from the start) — the <c>.cxt</c> writer
/// relies on this for its two-pass layout (spec §18.1) instead of buffering the
/// matrix (P-16).
/// </summary>
public interface IRecordSource
{
    /// <summary>Reads source schema metadata (column count, header) without scanning rows.</summary>
    ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Streams the object records in source order.</summary>
    IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default);
}
