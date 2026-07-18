using System.Globalization;

namespace FcaBedrock.Discovery;

/// <summary>
/// One discovered triple attribute: the predicate it observes (which is also its
/// <c>source</c> selector, verbatim), the logical name it authors, and whether reaching that
/// name required an adjustment worth warning about.
/// </summary>
/// <param name="Ordinal">The 0-based first-appearance position, which is also the fallback name's suffix.</param>
/// <param name="Predicate">The exact cleaned predicate text — the <c>source</c> selector, never rewritten.</param>
/// <param name="Name">The logical attribute name — §10.1-valid and ordinal-unique across the draft.</param>
/// <param name="NameAdjusted">Whether the name departs from the predicate text (an unusable predicate, or a fallback that had to escalate).</param>
internal sealed record DiscoveredPredicate(int Ordinal, string Predicate, string Name, bool NameAdjusted);

/// <summary>
/// The §7.1 / D-107 <b>triple</b> naming matrix, computed over the discovered predicates in
/// first-appearance order once the pass has completed.
/// <para>
/// <b>The same invariant as wide: no source selector is ever silently changed.</b> Here it bites
/// harder, because a triple attribute's selector <em>is</em> its predicate — the very string the
/// data spells. So an unusable predicate keeps its exact selector and gets a synthesized
/// <em>name</em>; renaming the selector instead would silently point the attribute at a
/// predicate the source does not contain.
/// </para>
/// <para>
/// <b>Why the whole list is planned at once.</b> A synthesized <c>predicate_3</c> can collide
/// with a real, perfectly usable predicate literally spelled <c>predicate_3</c> — including one
/// discovered later in the pass. Reserving every usable predicate's own text before assigning
/// any fallback is what keeps the real name with the real predicate and pushes the synthesized
/// one up the ladder, rather than letting arrival order decide which attribute gets renamed.
/// </para>
/// </summary>
internal static class PredicateNaming
{
    /// <summary>
    /// Plans one attribute per discovered predicate, in first-appearance order.
    /// </summary>
    public static IReadOnlyList<DiscoveredPredicate> Plan(IReadOnlyList<string> predicates)
    {
        var count = predicates.Count;

        // Predicates are already distinct under ordinal comparison, so the usable ones cannot
        // collide with each other — only a fallback can collide with one of them.
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (AttributeNaming.IsUsableName(predicates[i]))
            {
                used.Add(predicates[i]);
            }
        }

        var planned = new DiscoveredPredicate[count];
        for (var i = 0; i < count; i++)
        {
            var predicate = predicates[i];
            if (AttributeNaming.IsUsableName(predicate))
            {
                // A usable predicate is both selector and name — the ordinary case, and not an
                // adjustment: nothing departed from what the data spells.
                planned[i] = new DiscoveredPredicate(i, predicate, predicate, NameAdjusted: false);
                continue;
            }

            // Unusable as a §10.1 name (a quote or newline in the predicate text). The fallback is
            // a real departure from the authored string, so it is warned — unlike wide's routine
            // headerless `column_N` synthesis, where there was no name to depart from (D-111).
            var name = AttributeNaming.FirstUnused(FallbackName(i), i, used);
            used.Add(name);
            planned[i] = new DiscoveredPredicate(i, predicate, name, NameAdjusted: true);
        }

        return planned;
    }

    private static string FallbackName(int ordinal) =>
        string.Create(CultureInfo.InvariantCulture, $"predicate_{ordinal}");
}
