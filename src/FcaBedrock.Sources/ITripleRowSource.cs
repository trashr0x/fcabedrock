using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// A replayable stream of raw triple rows — the dumb reader half of triple conversion
/// (§5.3). Grouping rows into FCA objects, subject validation, and contiguity are the
/// Conversion layer's job (D-082); this only tokenizes and normalizes.
/// <see cref="ReadRowsAsync"/> may be called more than once (each call re-reads from the
/// start) — the <c>.cxt</c> two-pass and the <c>unordered</c> first-appearance grouping rely
/// on it instead of buffering the matrix (P-16).
/// </summary>
public interface ITripleRowSource
{
    /// <summary>
    /// What this source can prove about its preparation (D-098/G-1). See
    /// <see cref="IRecordSource.Provenance"/> — every implementor states it explicitly.
    /// </summary>
    SourceProvenance Provenance { get; }

    /// <summary>Reads source schema metadata (column count, header) without scanning rows.</summary>
    ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Streams the raw triple rows in source order.</summary>
    IAsyncEnumerable<TripleRow> ReadRowsAsync(CancellationToken cancellationToken = default);
}
