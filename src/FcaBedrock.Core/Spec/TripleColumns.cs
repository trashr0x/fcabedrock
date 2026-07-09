namespace FcaBedrock.Core.Spec;

/// <summary>
/// The resolved triple role→column-index map (spec §5.3): which 0-based physical
/// column holds each of the three logical roles. The document model addresses
/// roles by index or header name; the spec resolver resolves either to indices
/// (D-066), so the Core carrier holds indices only. The three roles are
/// distinct (<c>TripleColumnsNotDistinct</c> otherwise, §5.3). Omitted
/// <c>columns</c> resolves to <c>(0, 1, 2)</c>.
/// </summary>
/// <param name="Subject">0-based subject column (also the object-key column, §5.4).</param>
/// <param name="Predicate">0-based predicate column.</param>
/// <param name="Value">0-based value column.</param>
public sealed record TripleColumns(int Subject, int Predicate, int Value);
