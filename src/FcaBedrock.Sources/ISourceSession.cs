using FcaBedrock.Core.Spec;

namespace FcaBedrock.Sources;

/// <summary>
/// The general <b>unbound</b> streaming source session (D-109): a source that can be read
/// <em>before</em> any spec exists to bind it. Its stable boundary is deliberately narrow —
/// source shape, an ordered schema, streamed cleaned records, cancellation, and the typed
/// <see cref="SourceReadException"/> failure channel — and nothing more.
/// <para>
/// <b>What this seam deliberately does not expose.</b> No read settings, stream, file, spec,
/// provenance, or conversion source; no CSV-specific concept. A future SQL/SPARQL adapter has
/// no delimiter, quote, or header to describe, so those live on the concrete CSV sessions
/// (<see cref="WideCsvSession.ReadSettings"/> / <see cref="TripleCsvSession.ReadSettings"/>)
/// and reach a caller through the caller, never through this interface. Consumers
/// therefore depend on <em>records</em>, which is what makes discovery determinism a property
/// of the record sequence rather than of any file's bytes (D-112).
/// </para>
/// <para>
/// <b>Complements, not replaces, the bound seam.</b> <see cref="IRecordSource"/> /
/// <see cref="ITripleRowSource"/> remain the conversion-side interfaces: they carry
/// <see cref="SourceProvenance"/> pairing, which is a <em>bound</em> concept an unbound reader
/// must not be forced to have.
/// </para>
/// </summary>
public interface ISourceSession
{
    /// <summary>
    /// The shape of the records this session streams (§5.1) — the one piece of structural
    /// information the seam exposes, since a caller must know which of the two read methods
    /// applies. Probe never <em>infers</em> shape; the caller selects it (D-106).
    /// </summary>
    SourceShape Shape { get; }

    /// <summary>
    /// Reads the source schema — column count and, when present, the ordered header names.
    /// Column order is the source's own and is the addressing space every index-based
    /// selector and triple role map resolves into.
    /// </summary>
    ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An unbound session over a <b>wide</b> source: one record per object, one column per
/// potential attribute (§5.2).
/// </summary>
public interface IWideSourceSession : ISourceSession
{
    /// <summary>
    /// Streams the cleaned object records in input order. <b>Repeatable</b> — each call
    /// re-reads from the start and yields the same sequence — and <b>cleaned</b>: fields
    /// carry the §5.1 quote-aware trim and missing normalization already applied, exactly
    /// as the bound <see cref="IRecordSource"/> path applies them, so a probe and the
    /// conversion of the spec it drafts observe identical values.
    /// <para>
    /// A header record, when the source has one, is consumed as schema and never yielded as
    /// data; the first data record is index 0. Expected provider/read failures surface as
    /// <see cref="SourceReadException"/>; cancellation surfaces as
    /// <see cref="OperationCanceledException"/> and is never wrapped.
    /// </para>
    /// </summary>
    IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An unbound session over a <b>triple</b> (subject–predicate–value) source (§5.3).
/// </summary>
public interface ITripleSourceSession : ISourceSession
{
    /// <summary>
    /// Streams the cleaned triple rows in input order, reading the three roles through
    /// <paramref name="columns"/>. Same repeatability, cleaning, header, failure, and
    /// cancellation contract as <see cref="IWideSourceSession.ReadAsync"/>; an absent mapped
    /// role on a ragged short row is <see langword="null"/>, never an error (§5.4, D-085).
    /// <para>
    /// <b>Roles are a per-read argument, not session identity.</b> They index the ordered
    /// <see cref="ISourceSession.GetSchemaAsync"/> schema — schema-relative, hence portable to
    /// a non-delimited adapter — so the same session may be read under different role maps,
    /// and doing so constrains no later binding. This seam neither infers nor
    /// resolves roles from header names: a caller supplies an already-resolved map.
    /// </para>
    /// </summary>
    IAsyncEnumerable<TripleRow> ReadRowsAsync(
        TripleColumns columns,
        CancellationToken cancellationToken = default);
}
