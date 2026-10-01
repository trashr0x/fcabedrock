namespace FcaBedrock.Sources;

/// <summary>
/// One raw triple row as produced by a source: its 0-based data record index (spec §5.1.1;
/// used for first-appearance ordering) and the three role fields after spec §5.1.1 decoding
/// and missing normalization. A role is <see langword="null"/> when its cell is empty, equal to
/// the binding's <c>missing_token</c>, or absent because the row is too short, uniformly.
/// The Conversion layer assigns per-role meaning (null subject → invalid; null predicate →
/// no observation; null value → present-missing); the source owns no role semantics (D-082).
/// </summary>
public readonly record struct TripleRow(int RecordIndex, string? Subject, string? Predicate, string? Value);
