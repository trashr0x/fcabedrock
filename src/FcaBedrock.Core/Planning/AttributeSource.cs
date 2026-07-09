namespace FcaBedrock.Core.Planning;

/// <summary>
/// Where a <see cref="PlannedAttribute"/> reads its raw value at emit — a closed set
/// resolved by the planner from the spec's <c>SourceBinding</c>. Wide attributes read a
/// resolved column index; triple attributes match a predicate string against each row
/// (§5.3/§10.2). Keeping this a discriminated locator (rather than an index plus a
/// nullable predicate) lets each emit path pattern-match its own case with no sentinel.
/// </summary>
public abstract record AttributeSource;

/// <summary>Reads the raw value from a resolved 0-based wide column (§10.2).</summary>
/// <param name="Index">Resolved 0-based source column.</param>
public sealed record ColumnAttributeSource(int Index) : AttributeSource;

/// <summary>
/// Reads the raw value from every triple row whose predicate matches <paramref name="Predicate"/>
/// exactly and ordinally (§5.3/§10.2/P-12); a mistyped predicate simply never matches. The
/// selector is data, not schema — so it is not range-checked at plan.
/// </summary>
/// <param name="Predicate">The predicate selector string (verbatim from the spec).</param>
public sealed record PredicateAttributeSource(string Predicate) : AttributeSource;
