using System.Globalization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Discovery;

/// <summary>
/// One discovered triple attribute: the predicate it observes (which is also its
/// <c>source</c> selector, verbatim), the logical name it authors, and whether reaching that
/// name required an adjustment worth warning about.
/// </summary>
/// <param name="Ordinal">The 0-based first-appearance position, which is also the fallback name's suffix.</param>
/// <param name="Predicate">The exact cleaned predicate text: the <c>source</c> selector, never rewritten.</param>
/// <param name="Name">The logical attribute name, §10.1-valid and ordinal-unique across the draft.</param>
/// <param name="NameAdjusted">Whether the name departs from the predicate text (an unusable predicate, or a fallback that had to escalate).</param>
internal sealed record DiscoveredPredicate(int Ordinal, string Predicate, string Name, bool NameAdjusted);

/// <summary>
/// The §7.1 / D-107 <b>triple</b> naming matrix, computed over the discovered predicates in
/// first-appearance order once the pass has completed. It is the reference result:
/// <see cref="IncrementalPredicateNaming"/> keeps the same names current during the pass, and
/// the probe requires the two to agree at the end.
/// <para>
/// <b>The same invariant as wide: no source selector is ever silently changed.</b> Here it bites
/// harder, because a triple attribute's selector <em>is</em> its predicate, the very string the
/// data spells. So an unusable predicate keeps its exact selector and gets a synthesized
/// <em>name</em>; renaming the selector instead would silently point the attribute at a
/// predicate the source does not contain.
/// </para>
/// <para>
/// <b>Why the whole list is planned at once.</b> A synthesized <c>predicate_3</c> can collide
/// with a real, perfectly usable predicate literally spelled <c>predicate_3</c>, including one
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
        // collide with each other; only a fallback can collide with one of them.
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (AttributeNameValidity.IsValid(predicates[i]))
            {
                used.Add(predicates[i]);
            }
        }

        var planned = new DiscoveredPredicate[count];
        for (var i = 0; i < count; i++)
        {
            var predicate = predicates[i];
            if (AttributeNameValidity.IsValid(predicate))
            {
                // A usable predicate is both selector and name, the ordinary case, and not an
                // adjustment: nothing departed from what the data spells.
                planned[i] = new DiscoveredPredicate(i, predicate, predicate, NameAdjusted: false);
                continue;
            }

            // Unusable as a §10.1 name (a quote or newline in the predicate text). The fallback is
            // a real departure from the authored string, so it is warned, unlike wide's routine
            // headerless `column_N` synthesis, where there was no name to depart from (D-111).
            var name = AttributeNaming.FirstUnused(FallbackName(i), i, used);
            used.Add(name);
            planned[i] = new DiscoveredPredicate(i, predicate, name, NameAdjusted: true);
        }

        return planned;
    }

    internal static string FallbackName(int ordinal) =>
        string.Create(CultureInfo.InvariantCulture, $"predicate_{ordinal}");
}

/// <summary>
/// Keeps <see cref="PredicateNaming.Plan"/>'s result for the predicates discovered so far
/// current while the pass runs, so a name is known while its attribute's values arrive (§7.1).
/// <para>
/// A usable predicate's name is its own text and never changes. An unusable predicate at
/// ordinal <c>i</c> takes the first string of its ladder (<c>predicate_i</c>, then
/// <c>predicate_i#i</c>, then <c>predicate_i#1</c>, <c>#2</c>, …) that no usable predicate
/// spells, which is what <see cref="AttributeNaming.FirstUnused"/> gives the batch plan, because
/// the ladders of different ordinals share no string. A later usable predicate that spells a
/// fallback's current name takes it, and that one fallback moves on along its own ladder. Usable
/// names only accumulate, so a blocked string never frees up again: a fallback moves forward
/// only, and no move displaces another fallback.
/// </para>
/// </summary>
internal sealed class IncrementalPredicateNaming
{
    private const int Usable = -1;

    private readonly HashSet<string> _usable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _fallbackOwner = new(StringComparer.Ordinal);

    // Per ordinal: its position on its own ladder, or Usable.
    private readonly List<int> _position = [];

    private readonly CancellationToken _cancellationToken;

    /// <summary>Starts with no predicates.</summary>
    /// <param name="cancellationToken">The probe's token, checked at every ladder step and at every character of a name.</param>
    public IncrementalPredicateNaming(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        Names = new NameRelations(cancellationToken);
    }

    /// <summary>The current names, by predicate first-appearance ordinal.</summary>
    public NameRelations Names { get; }

    /// <summary>
    /// Names a newly discovered predicate, moving at most one fallback first, and returns its
    /// ordinal. Every name change is recorded in <see cref="NameRelations"/> before the row's
    /// value is observed.
    /// </summary>
    public int Discover(string predicate)
    {
        var ordinal = _position.Count;
        if (AttributeNameValidity.IsValid(predicate))
        {
            _usable.Add(predicate);
            if (_fallbackOwner.Remove(predicate, out var displaced))
            {
                var position = FirstFree(displaced, _position[displaced] + 1);
                _position[displaced] = position;
                var moved = Candidate(displaced, position);
                _fallbackOwner.Add(moved, displaced);
                Names.Rename(displaced, moved);
            }

            _position.Add(Usable);
            Names.Add(predicate);
            return ordinal;
        }

        var first = FirstFree(ordinal, 0);
        _position.Add(first);
        var name = Candidate(ordinal, first);
        _fallbackOwner.Add(name, ordinal);
        Names.Add(name);
        return ordinal;
    }

    private int FirstFree(int ordinal, int position)
    {
        while (_usable.Contains(Candidate(ordinal, position)))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            position = checked(position + 1);
        }

        return position;
    }

    // The ladder AttributeNaming.FirstUnused walks: the fallback, then `#<ordinal>`, then `#1`,
    // `#2`, … The indexed step reappears later as `#<ordinal>` itself, and is blocked there too.
    private static string Candidate(int ordinal, int position) =>
        position switch
        {
            0 => PredicateNaming.FallbackName(ordinal),
            1 => string.Create(CultureInfo.InvariantCulture, $"predicate_{ordinal}#{ordinal}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"predicate_{ordinal}#{position - 1}"),
        };
}
