namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[binding.object_key]</c> block (§5.4). The resolver builds
/// the Core <c>ObjectKey</c> from it; mode-vs-shape validation is the M2
/// validation slice (D-064).
/// </summary>
/// <param name="Mode">Key mode (§5.4); null means the per-shape default (wide: row_index; triple: subject column).</param>
/// <param name="Column">The key column for <c>column</c> mode, by index or name.</param>
/// <param name="Columns">The key columns for <c>composite</c> mode; document-only (the Core carrier is a marker, D-064).</param>
/// <param name="Aggregate">How composite rows combine (§5.4); document-only.</param>
public sealed record ObjectKeySection(
    ObjectKeyMode? Mode,
    ColumnRef? Column,
    IReadOnlyList<string>? Columns,
    CompositeAggregate? Aggregate);

/// <summary>Object key modes (§5.4).</summary>
public enum ObjectKeyMode
{
    /// <summary>Object names are <c>0</c>, <c>1</c>, … in input order (wide default).</summary>
    RowIndex,

    /// <summary>Object names come from a source column.</summary>
    Column,

    /// <summary>Object names combine several columns (deferred — permanent v1 reject, D-064).</summary>
    Composite,
}

/// <summary>How composite-key rows combine (§5.4); document-only carrier.</summary>
public enum CompositeAggregate
{
    /// <summary>Crosses union across rows sharing a composite key.</summary>
    Union,

    /// <summary>Crosses intersect across rows sharing a composite key.</summary>
    Intersection,
}

/// <summary>
/// An authored column reference (§5.4/§10.2): by 0-based index or by header
/// name. Name refs are resolved to indices by <see cref="SpecResolver"/> —
/// resolved Core types carry indices only (D-066).
/// </summary>
public abstract record ColumnRef;

/// <summary>Refers to a column by 0-based index.</summary>
/// <param name="Index">0-based column index.</param>
public sealed record IndexColumnRef(int Index) : ColumnRef;

/// <summary>Refers to a column by header name; requires a header schema to resolve.</summary>
/// <param name="Name">The header name.</param>
public sealed record NameColumnRef(string Name) : ColumnRef;
