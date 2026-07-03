namespace FcaBedrock.Core.Spec;

/// <summary>
/// How object names are derived. Spec §5.4. A closed set; v1 executes
/// <see cref="RowIndexObjectKey"/> only. <see cref="ColumnObjectKey"/> executes
/// at M3 and <see cref="CompositeObjectKey"/> is a permanent v1 reject (D-064);
/// both are resolved carriers in this slice — conversion currently fails loudly
/// in <c>WideCsvSource</c>, and the plan-phase diagnostics (D-064) land with
/// the M2 validation slice.
/// </summary>
public abstract record ObjectKey;

/// <summary>Object names are <c>0</c>, <c>1</c>, … in input order.</summary>
public sealed record RowIndexObjectKey : ObjectKey;

/// <summary>
/// Object names come from a source column, resolved to a 0-based index, with
/// <paramref name="Policy"/> governing duplicate keys (§5.4/§6.1). Execution is
/// M3 (D-064); until its plan-phase guard lands, conversion fails loudly in
/// <c>WideCsvSource</c>.
/// </summary>
/// <param name="Index">0-based index of the key column.</param>
/// <param name="Policy">How duplicate keys are handled (§6.1; Fail is the spec default).</param>
public sealed record ColumnObjectKey(int Index, DuplicateObjectPolicy Policy) : ObjectKey;

/// <summary>
/// Marker for the deferred composite key (§5.4), a permanent v1 plan-phase
/// reject (D-064). Columns/aggregate stay document-only; parameterizing later
/// is additive (P-3).
/// </summary>
public sealed record CompositeObjectKey : ObjectKey;
