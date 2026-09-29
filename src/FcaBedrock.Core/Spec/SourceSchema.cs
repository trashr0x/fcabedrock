namespace FcaBedrock.Core.Spec;

/// <summary>
/// Source schema metadata the planner may inspect without reading object rows
/// (spec §7): the column count and, when present, the header names.
/// Produced by a source adapter; consumed by spec resolution (D-098) and the planner
/// to validate bindings.
/// </summary>
public sealed record SourceSchema(int ColumnCount, IReadOnlyList<string>? Header = null);
