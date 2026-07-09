using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// Selects the triple row stream to emit over, from a resolved binding's <c>ordering</c>. This is
/// the production seam that honors <c>binding.ordering</c> — the decision the (now retired) planner
/// guard used to own. Callers plan once (the plan is ordering-independent) and emit over
/// <c>ForOrdering(source, ordering)</c>, so <c>subject_grouped</c> and <c>unordered</c> share one
/// <see cref="Emitter.EmitTripleAsync"/> path (§17 rule 4; D-082).
/// </summary>
public static class TripleRowSources
{
    /// <summary>
    /// Returns <paramref name="source"/> unchanged for <see cref="TripleOrdering.SubjectGrouped"/>
    /// (single-pass, contiguity-checked), or wrapped in a subject-regrouping decorator for
    /// <see cref="TripleOrdering.Unordered"/> (interleaved input, first-appearance order).
    /// </summary>
    public static ITripleRowSource ForOrdering(ITripleRowSource source, TripleOrdering ordering)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ordering switch
        {
            TripleOrdering.SubjectGrouped => source,
            TripleOrdering.Unordered => new UnorderedTripleRowSource(source),
            _ => throw new ArgumentOutOfRangeException(nameof(ordering), ordering, "Unknown triple ordering."),
        };
    }
}
