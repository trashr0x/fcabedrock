namespace FcaBedrock.Core.Spec;

/// <summary>
/// How object names are derived. Spec §5.4. A closed set: <see cref="RowIndexObjectKey"/> and
/// <see cref="ColumnObjectKey"/> execute (wide <c>fail</c>/<c>keep</c> at M3 Slice E; wide
/// <c>dedupe</c> at Slice F; a triple <see cref="ColumnObjectKey"/> is the subject key), while
/// <see cref="CompositeObjectKey"/> is a permanent v1 reject (D-024/D-064).
/// </summary>
public abstract record ObjectKey;

/// <summary>Object names are <c>0</c>, <c>1</c>, … in input order.</summary>
public sealed record RowIndexObjectKey : ObjectKey;

/// <summary>
/// Object names come from a source column, resolved to a 0-based index, with
/// <paramref name="Policy"/> governing duplicate keys (§5.4/§6.1). Wide execution: <c>fail</c>/
/// <c>keep</c> at M3 Slice E, <c>dedupe</c> at Slice F. Under a triple binding this is the resolved
/// subject key (the policy is inapplicable and pinned to <c>Fail</c>, D-082/§6.1).
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
