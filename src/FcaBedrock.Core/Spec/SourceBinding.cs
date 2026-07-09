namespace FcaBedrock.Core.Spec;

/// <summary>
/// Binds an attribute to a place in the source. Spec §10.2. A closed set of
/// resolved bindings: column-by-name is a document-model state the spec
/// resolver turns into an index (D-066); a triple <see cref="PredicateSource"/>
/// binds by predicate string (matched against data at emit, M3/D-082).
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

/// <summary>
/// Binds a triple attribute to a source <paramref name="Predicate"/> string
/// (§10.2/§5.3). The predicate is data, not schema: it is matched exactly and
/// ordinally against each row's predicate value at emit (P-12), so a mistyped
/// predicate simply never matches (surfacing at emit as <c>AttributeHasNoCrosses</c>,
/// not a binding error). <paramref name="ValueType"/> has no default for the same
/// reason as <see cref="ColumnSource"/> (§10.2 / D-061).
/// </summary>
/// <param name="Predicate">The predicate selector string (taken verbatim from the spec).</param>
/// <param name="ValueType">How matching raw values are read (§10.2 / D-061).</param>
public sealed record PredicateSource(string Predicate, SourceValueType ValueType) : SourceBinding;
