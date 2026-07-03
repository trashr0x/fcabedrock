namespace FcaBedrock.Core.Spec;

/// <summary>
/// Binds an attribute to a place in the source. Spec §10.2. A closed set of
/// resolved bindings: column-by-name is a document-model state the spec
/// resolver turns into an index (D-066), and triple predicate bindings stay
/// document-only until M3 (D-072).
/// </summary>
public abstract record SourceBinding;

/// <summary>
/// Binds to a wide-CSV column by 0-based index. <paramref name="ValueType"/>
/// deliberately has no default: the spec default is per-discretizer (§10.2 /
/// D-061), so every construction site must decide it explicitly.
/// </summary>
/// <param name="Index">0-based column index.</param>
/// <param name="ValueType">How raw values in the column are read (§10.2 / D-061).</param>
public sealed record ColumnSource(int Index, SourceValueType ValueType) : SourceBinding;
