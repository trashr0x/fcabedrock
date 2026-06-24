namespace FcaBedrock.Core.Spec;

/// <summary>
/// How object names are derived. Spec §5.4. A closed set; v1 slice 1 implements
/// <see cref="RowIndexObjectKey"/>. Column and (deferred) composite keys follow.
/// </summary>
public abstract record ObjectKey;

/// <summary>Object names are <c>0</c>, <c>1</c>, … in input order.</summary>
public sealed record RowIndexObjectKey : ObjectKey;
