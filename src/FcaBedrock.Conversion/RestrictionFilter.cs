using System.Globalization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// Evaluates a plan's <c>restrict_to</c> filters over formed objects (§10.4/D-091). One instance
/// per emit attempt, shared by every emit path — wide streaming, wide <c>dedupe</c>, and both
/// triple orderings — so the four paths cannot drift about what a restriction means.
/// <para>
/// <b>Matching is existential.</b> A restriction passes an object when at least one of that
/// object's observations for its source matches at least one entry (OR across observations, OR
/// across entries); the object is emitted only when <b>every</b> restriction passes (AND across
/// attributes). Missing (<see langword="null"/>) matches nothing, and an absent triple predicate
/// supplies no observation at all — either way that restriction fails.
/// </para>
/// <para>
/// <b>Restrictions filter objects, not observations</b> (§10.4/D-097): a surviving object keeps
/// <b>all</b> its crosses, not only the ones that matched. The caller therefore accumulates
/// crosses and match flags independently and decides emission when the object closes.
/// </para>
/// <para>
/// Restriction reads raw values <b>before</b> discretization, so it is a diagnostic owner —
/// but only where nothing else is looking. For a <b>filter-only</b> attribute this is the sole
/// pass that sees the value, so an unparseable/non-finite numeric input is tallied here at the
/// severity <c>unknown_value_policy</c> selects. For an <b>included</b> attribute the ordinary
/// classification pass already owns that diagnostic (it runs for every formed object, filtered
/// or not), so this path stays silent rather than counting the same cell twice (D-097).
/// </para>
/// </summary>
internal sealed class RestrictionFilter
{
    private readonly PlannedRestriction[] _restrictions;
    private readonly int[] _columns;                        // wide: resolved column index; -1 under triple
    private readonly Dictionary<string, List<int>> _byPredicate; // triple: predicate selector → restriction indices
    private readonly DiagnosticTally?[] _unparseable;       // null where the restriction path owns no diagnostic
    private readonly CultureInfo _culture;

    private RestrictionFilter(
        PlannedRestriction[] restrictions,
        int[] columns,
        Dictionary<string, List<int>> byPredicate,
        DiagnosticTally?[] unparseable,
        CultureInfo culture)
    {
        _restrictions = restrictions;
        _columns = columns;
        _byPredicate = byPredicate;
        _unparseable = unparseable;
        _culture = culture;
    }

    /// <summary>The number of planned restrictions; <c>0</c> when the spec restricts nothing.</summary>
    public int Count => _restrictions.Length;

    /// <summary>
    /// Builds the filter for one emit attempt. The numeric parsing culture is derived <b>once</b>
    /// here from <c>binding.locale</c> — never ambient (P-11) and never per row.
    /// </summary>
    public static RestrictionFilter Create(ConversionPlan plan)
    {
        var restrictions = plan.Restrictions.ToArray();
        var columns = new int[restrictions.Length];
        var unparseable = new DiagnosticTally?[restrictions.Length];
        var byPredicate = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        // A restriction owns the unparseable diagnostic only when its attribute plans no column —
        // i.e. it is filter-only. An included-and-restricted attribute is classified for every
        // formed object, and that pass is the owner (D-097). Ordinal, like every identity
        // comparison here (P-12).
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in plan.Attributes)
        {
            included.Add(attribute.Name);
        }

        for (var i = 0; i < restrictions.Length; i++)
        {
            var restriction = restrictions[i];
            ValidateEntries(restriction);

            switch (restriction.Source)
            {
                case ColumnAttributeSource column:
                    columns[i] = column.Index;
                    break;

                case PredicateAttributeSource predicate:
                    columns[i] = -1;
                    if (!byPredicate.TryGetValue(predicate.Predicate, out var list))
                    {
                        list = [];
                        byPredicate[predicate.Predicate] = list;
                    }

                    // One predicate may bind several restrictions (source repeat, D-033).
                    list.Add(i);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Restriction on attribute '{restriction.AttributeName}' has an unrecognized source binding " +
                        $"'{restriction.Source.GetType().Name}'.");
            }

            // A string restriction never parses, so it can never report unparseable.
            unparseable[i] = restriction.ValueType == SourceValueType.Number && !included.Contains(restriction.AttributeName)
                ? new DiagnosticTally()
                : null;
        }

        return new RestrictionFilter(restrictions, columns, byPredicate, unparseable, ResolveCulture(plan));
    }

    /// <summary>A fresh per-object match buffer: one flag per restriction, all false.</summary>
    public bool[] NewMatchBuffer() => new bool[_restrictions.Length];

    /// <summary>Clears <paramref name="matched"/> for the next object (reused, so no per-object allocation).</summary>
    public static void Reset(bool[] matched) => Array.Clear(matched);

    /// <summary>
    /// Whether the object passes every restriction: the AND across attributes (§10.4). Trivially
    /// true when the spec restricts nothing.
    /// </summary>
    public static bool Passes(bool[] matched)
    {
        foreach (var hit in matched)
        {
            if (!hit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Observes one wide source row against every restriction (streaming path).</summary>
    public void ObserveWide(ObjectRecord record, bool[] matched)
    {
        for (var i = 0; i < _restrictions.Length; i++)
        {
            Observe(i, record.Field(_columns[i]), matched);
        }
    }

    /// <summary>Observes one merged wide row against every restriction (<c>dedupe</c> path).</summary>
    public void ObserveWide(DedupeRow row, bool[] matched)
    {
        for (var i = 0; i < _restrictions.Length; i++)
        {
            Observe(i, row.Field(_columns[i]), matched);
        }
    }

    /// <summary>
    /// Observes one triple <c>(predicate, value)</c> observation against the restrictions that bind
    /// that predicate. A predicate no restriction binds is simply not an observation for any of
    /// them — and a predicate <b>absent</b> from a subject's group never reaches here at all, so
    /// its restriction stays unmatched and the object fails (§10.4).
    /// </summary>
    public void ObserveTriple(string predicate, string? value, bool[] matched)
    {
        if (!_byPredicate.TryGetValue(predicate, out var indices))
        {
            return;
        }

        foreach (var i in indices)
        {
            Observe(i, value, matched);
        }
    }

    /// <summary>
    /// Flushes the filter-only unparseable aggregates — one per restricting attribute, in
    /// restriction (spec-attribute) order, at the severity its <c>unknown_value_policy</c> selects
    /// (D-097: <c>skip</c> silent, <c>warn</c> Warning, <c>fail</c> Error, <c>include</c> Warning —
    /// an unparseable token cannot join a numeric domain, so <c>include</c> behaves as warn).
    /// Called on normal completion only, like every other emit aggregate.
    /// <para>
    /// Returns <see langword="true"/> when any aggregate flushed at Error — the <c>fail</c>-policy
    /// abort (§10.4/§10.6). The caller suppresses the whole-stream observability aggregates on it:
    /// the run is invalid, so describing its shape is noise the caller must discard anyway (G-12).
    /// </para>
    /// </summary>
    public bool Flush(ICollection<BedrockDiagnostic> diagnostics)
    {
        var aborted = false;
        for (var i = 0; i < _restrictions.Length; i++)
        {
            if (_unparseable[i] is not { Count: > 0 } tally)
            {
                continue;
            }

            var restriction = _restrictions[i];
            if (Severity(restriction.UnknownValuePolicy) is not { } severity)
            {
                continue; // skip: silent
            }

            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SourceValueUnparseable,
                severity,
                $"{tally.Count} value(s) read for the restrict_to filter on attribute '{restriction.AttributeName}' " +
                $"could not be parsed as a number (e.g. {tally.Sample}); they match no numeric entry (§10.4).",
                new DiagnosticLocation(AttributeName: restriction.AttributeName)));
            aborted |= severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;
        }

        return aborted;
    }

    // Records one observation's outcome for restriction i. Existential: once matched, a later
    // non-match cannot un-match it — but every observation is still READ, because the unparseable
    // tally must be complete (a filter-only attribute has no other diagnostic owner).
    private void Observe(int i, string? raw, bool[] matched)
    {
        if (Matches(_restrictions[i], raw, out var unparseable))
        {
            matched[i] = true;
        }
        else if (unparseable)
        {
            _unparseable[i]?.Record(raw!);
        }
    }

    // One observation against one restriction's entries: the OR across entries.
    private bool Matches(PlannedRestriction restriction, string? raw, out bool unparseable)
    {
        unparseable = false;
        if (raw is null)
        {
            return false; // missing matches nothing, and is silent (D-097)
        }

        if (restriction.ValueType == SourceValueType.String)
        {
            // §10.4/P-12: ordinal, case-sensitive equality on the raw value. No trimming, no case
            // folding, no locale, no regex — restriction matching is identity, not search.
            foreach (var entry in restriction.Entries)
            {
                if (entry is RestrictToValue value && string.Equals(value.Value, raw, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // §10.4/§11.5/G-6: the numeric observation is parsed under the binding locale and
        // zero-canonicalized — the text-sourced arm of the pinned chain. An unparseable or
        // non-finite input can match no numeric entry (not even {}), so it is a non-match; the
        // caller decides whether this path owns its diagnostic.
        if (!CanonicalNumber.TryParse(raw, _culture, out var parsed))
        {
            unparseable = true;
            return false;
        }

        var observed = CanonicalNumber.CanonicalizeZero(parsed);
        foreach (var entry in restriction.Entries)
        {
            var hit = entry switch
            {
                // Exact numeric identity — no epsilon. Both sides are canonical, so 30/30.0/3e1
                // and -0/0 already collapse before this compare (§10.4/D-091).
                RestrictToNumber number => observed == number.Value,

                // Half-open [from, to), either end open when null; {} (both null) matches any
                // usable numeric value.
                RestrictToRange range =>
                    (range.From is not { } from || observed >= from)
                    && (range.To is not { } to || observed < to),

                // Unreachable: Create validates entry-vs-value_type once per emit.
                _ => throw new InvalidOperationException(
                    $"restrict_to entry '{entry.GetType().Name}' is not a numeric entry on attribute " +
                    $"'{restriction.AttributeName}' (corrupt Core state)."),
            };

            if (hit)
            {
                return true;
            }
        }

        return false;
    }

    // The resolve seam fixes one value_type per attribute and rejects every entry form that
    // contradicts it (RestrictToNumericEntryRequired / SourceValueTypeInvalid), so a mismatch here
    // is corrupt Core state from a hand-built spec. Validated ONCE per emit rather than per row,
    // and thrown rather than silently treated as a non-match — a filter that quietly stops
    // matching would drop every object with no diagnostic at all (P-10, the D-098 posture).
    private static void ValidateEntries(PlannedRestriction restriction)
    {
        foreach (var entry in restriction.Entries)
        {
            var valid = restriction.ValueType == SourceValueType.String
                ? entry is RestrictToValue
                : entry is RestrictToNumber or RestrictToRange;
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"restrict_to entry '{entry.GetType().Name}' on attribute '{restriction.AttributeName}' " +
                    $"is invalid for value_type '{restriction.ValueType}'; the resolve seam validates this (§10.2/§10.4).");
            }
        }
    }

    // §5.1/§10.4/P-11: the numeric parsing culture, derived once per emit from the seam-validated
    // binding locale — never ambient, never per row, never mutable state. ResolvedSpec.Create
    // already proved the locale resolves under the same predefined-only rule, so a failure here is
    // corrupt Core state, not user input.
    private static CultureInfo ResolveCulture(ConversionPlan plan)
    {
        var locale = plan.Calibrated.Spec.Binding.Locale;
        if (string.Equals(locale, "invariant", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
        }
        catch (CultureNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"binding.locale '{locale}' does not resolve to a predefined culture; " +
                "the resolve seam and ResolvedSpec.Create validate this (corrupt Core state).",
                exception);
        }
    }

    // D-097/§10.6: the filter-only restriction path reports at the policy severity. "include"
    // behaves as warn — an unparseable token cannot join a numeric domain.
    private static DiagnosticSeverity? Severity(UnknownValuePolicy policy) => policy switch
    {
        UnknownValuePolicy.Skip => null,
        UnknownValuePolicy.Warn => DiagnosticSeverity.Warning,
        UnknownValuePolicy.Fail => DiagnosticSeverity.Error,
        UnknownValuePolicy.Include => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Warning,
    };
}
