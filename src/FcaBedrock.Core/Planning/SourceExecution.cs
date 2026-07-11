using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// How a planned conversion executes its source — the shape-specific execution
/// strategy carried on the <see cref="ConversionPlan"/> (§5 / D-082). A
/// <b>mechanically-closed</b> hierarchy: the <see langword="private protected"/>
/// constructor leaves code outside this assembly no accessible base constructor, so
/// no out-of-assembly type can derive, while Core can add SQL/SPARQL variants later.
/// (A record cannot be closed this way — a non-sealed record needs a public or
/// protected copy constructor, CS8878 — so this is a plain abstract class.)
/// <para>
/// Pure value with explicit equality on the concrete subtypes, so
/// <see cref="ConversionPlan"/> record equality behaves across independently obtained
/// instances. <b>Not</b> a fingerprint input: the execution/streaming strategy never
/// changes output bytes (§17 / D-082).
/// </para>
/// </summary>
public abstract class SourceExecution
{
    private protected SourceExecution()
    {
    }
}

/// <summary>
/// Wide (one row per object) execution. A true singleton — the private constructor
/// leaves <see cref="Instance"/> the only value, so default reference equality is
/// value-correct.
/// </summary>
public sealed class WideExecution : SourceExecution
{
    private WideExecution()
    {
    }

    /// <summary>The single wide-execution value.</summary>
    public static WideExecution Instance { get; } = new();
}

/// <summary>
/// Triple (subject-predicate-value) execution carrying the resolved row
/// <see cref="Ordering"/> (§5.3). The ordering drives row-stream selection at emit
/// (subject-grouped single-pass vs. unordered sort-merge) but never output bytes, so
/// it is not a fingerprint input (D-082). Equality is by <see cref="Ordering"/> so two
/// independently constructed equivalent executions compare equal.
/// </summary>
public sealed class TripleExecution : SourceExecution
{
    /// <summary>Creates a triple execution for the resolved <paramref name="ordering"/> (required).</summary>
    public TripleExecution(TripleOrdering ordering) => Ordering = ordering;

    /// <summary>The resolved triple row ordering (§5.3).</summary>
    public TripleOrdering Ordering { get; }

    /// <summary>Value equality on <see cref="Ordering"/>.</summary>
    public bool Equals(TripleExecution? other) => other is not null && Ordering == other.Ordering;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TripleExecution);

    /// <inheritdoc/>
    public override int GetHashCode() => Ordering.GetHashCode();
}
