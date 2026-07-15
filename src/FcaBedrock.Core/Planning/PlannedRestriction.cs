using System.Collections.Immutable;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// One attribute's executable restriction (§10.4/D-091), planned for every
/// attribute with a non-empty <c>restrict_to</c> — included or filter-only. Order
/// is spec-attribute order (deterministic, P-7). The <c>restrict_to</c> execution
/// engine lands at M4 slice F; slice A only carries the (currently always empty)
/// list on the plan.
/// </summary>
public sealed record PlannedRestriction
{
    /// <summary>Creates a planned restriction, snapshotting <paramref name="entries"/> into immutable storage.</summary>
    public PlannedRestriction(
        string attributeName,
        AttributeSource source,
        SourceValueType valueType,
        IReadOnlyList<RestrictToEntry> entries,
        UnknownValuePolicy unknownValuePolicy)
    {
        ArgumentNullException.ThrowIfNull(attributeName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        AttributeName = attributeName;
        Source = source;
        ValueType = valueType;
        Entries = entries.ToImmutableArray();
        UnknownValuePolicy = unknownValuePolicy;
    }

    /// <summary>The logical attribute name (diagnostics only).</summary>
    public string AttributeName { get; }

    /// <summary>Where the restricted raw value is read (resolved column index / predicate selector).</summary>
    public AttributeSource Source { get; }

    /// <summary>The source value type: string ⇒ ordinal equality; number ⇒ parsed identity.</summary>
    public SourceValueType ValueType { get; }

    /// <summary>The resolved restriction entries.</summary>
    public IReadOnlyList<RestrictToEntry> Entries { get; }

    /// <summary>The attribute's unknown-value policy (D-097 severity source).</summary>
    public UnknownValuePolicy UnknownValuePolicy { get; }
}
