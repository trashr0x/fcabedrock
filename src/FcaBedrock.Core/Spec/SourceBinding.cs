namespace FcaBedrock.Core.Spec;

/// <summary>
/// Binds an attribute to a place in the source. Spec §10.2. A closed set; v1
/// slice 1 implements wide-CSV column-by-index. Column-by-name and triple
/// predicate bindings join as their milestones land.
/// </summary>
public abstract record SourceBinding;

/// <summary>Binds to a wide-CSV column by 0-based index.</summary>
public sealed record ColumnSource(int Index) : SourceBinding;
