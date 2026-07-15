using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// A replayable stream of object records. <see cref="ReadAsync"/> may be called
/// more than once (each call re-reads from the start) — the <c>.cxt</c> writer
/// relies on this for its two-pass layout (spec §18.1) instead of buffering the
/// matrix (P-16).
/// <para>
/// <b>Field-array ownership (the seam contract for custom / SQL / SPARQL sources).</b>
/// An implementation must give each yielded <see cref="ObjectRecord"/> a field array it
/// then relinquishes — construction transfers exclusive, immutable ownership (see
/// <see cref="ObjectRecord(string, string?[])"/>). A source must not mutate or reuse a
/// buffer across rows, because downstream stages may retain a record past the yield
/// (e.g. the wide <c>dedupe</c> grouping buffers/spills it zero-copy, D-082/D-083).
/// </para>
/// </summary>
public interface IRecordSource
{
    /// <summary>
    /// What this source can prove about its preparation (D-098/G-1): a bound source
    /// carries a <see cref="TokenProvenance"/>, a direct-constructed production source
    /// a <see cref="DescriptorProvenance"/>, and a descriptor-less adapter/test fake
    /// the explicit <see cref="SourceProvenance.Unvalidated"/> opt-out. Every
    /// implementor states its provenance explicitly (no default) — the calibrate/emit
    /// guard pairs the source to the resolution against this.
    /// </summary>
    SourceProvenance Provenance { get; }

    /// <summary>Reads source schema metadata (column count, header) without scanning rows.</summary>
    ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>Streams the object records in source order.</summary>
    IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default);
}
