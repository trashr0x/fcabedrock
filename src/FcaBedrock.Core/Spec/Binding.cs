namespace FcaBedrock.Core.Spec;

/// <summary>
/// Tells a spec how to apply itself to a concrete data source. Spec §5. The
/// binding is separable from per-attribute scaling (decisions.md D-001): notably,
/// a v2 <c>.bed</c> file carries none of this, so it is supplied by the caller
/// when migrating a v2 spec.
/// </summary>
/// <param name="Shape">Source shape (§5.1).</param>
/// <param name="Encoding">
/// Resolved source text encoding (§5.1). v1 accepts UTF-8 only, canonicalized to
/// <c>"utf-8"</c> at resolve (D-082); a real fingerprint input (was a constant
/// until M3, D-077). UTF-8 specs keep their prior hash.
/// </param>
/// <param name="Delimiter">Field delimiter (§5.1).</param>
/// <param name="QuoteChar">Quote character (§5.1).</param>
/// <param name="HasHeader">Whether the first record is a header (§5.1).</param>
/// <param name="Locale">Locale for numeric/date parsing (§5.1); never string collation (P-12).</param>
/// <param name="MissingToken">Token marking a missing value (§5.1).</param>
/// <param name="ObjectKey">How object names are derived (§5.4).</param>
/// <param name="TripleColumns">
/// Resolved triple role→column-index map (§5.3); <c>null</c> for wide. A role bound
/// by header name and the equivalent index bind resolve to the same map, so they
/// fingerprint identically (D-082).
/// </param>
/// <param name="Ordering">
/// Triple row ordering (§5.3); <c>null</c> for wide. An acceptance/streaming
/// property, <em>not</em> a fingerprint input — both orderings emit identical
/// first-appearance bytes (D-082, Slice A).
/// </param>
public sealed record Binding(
    SourceShape Shape,
    string Encoding,
    char Delimiter,
    char QuoteChar,
    bool HasHeader,
    string Locale,
    string MissingToken,
    ObjectKey ObjectKey,
    TripleColumns? TripleColumns = null,
    TripleOrdering? Ordering = null);
