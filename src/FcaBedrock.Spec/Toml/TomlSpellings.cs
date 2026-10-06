using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Single source of truth for the TOML surface's vocabulary: one spelling table
/// per enum, consumed in both directions by the reader and the writer so the two
/// can never drift (EP-5), plus the kind names behind the D-070 dispatch. It holds no
/// deferred-surface set (D-078 and D-120 removed the D-075 sets); the reserved
/// <c>value_type = "date"</c> is a spelling here (<see cref="DateValueType"/>) that the reader
/// carries as a flag on the source and the planner refuses (D-038).
/// </summary>
internal static class TomlSpellings
{
    /// <summary><c>binding.shape</c> (§5.1).</summary>
    internal static readonly (string Text, SourceShape Value)[] Shapes =
        [("wide", SourceShape.Wide), ("triple", SourceShape.Triple)];

    /// <summary><c>binding.ordering</c> (§5.3).</summary>
    internal static readonly (string Text, TripleOrdering Value)[] Orderings =
        [("subject_grouped", TripleOrdering.SubjectGrouped), ("unordered", TripleOrdering.Unordered)];

    /// <summary><c>object_key.mode</c> (§5.4).</summary>
    internal static readonly (string Text, ObjectKeyMode Value)[] ObjectKeyModes =
        [("row_index", ObjectKeyMode.RowIndex), ("column", ObjectKeyMode.Column), ("composite", ObjectKeyMode.Composite)];

    /// <summary><c>object_key.aggregate</c> (§5.4).</summary>
    internal static readonly (string Text, CompositeAggregate Value)[] Aggregates =
        [("union", CompositeAggregate.Union), ("intersection", CompositeAggregate.Intersection)];

    /// <summary><c>missing_policy</c> (§6/§10.5).</summary>
    internal static readonly (string Text, MissingPolicy Value)[] MissingPolicies =
        [("skip", MissingPolicy.Skip), ("as_attribute", MissingPolicy.AsAttribute)];

    /// <summary><c>unknown_value_policy</c> (§6/§10.6).</summary>
    internal static readonly (string Text, UnknownValuePolicy Value)[] UnknownValuePolicies =
    [
        ("skip", UnknownValuePolicy.Skip),
        ("warn", UnknownValuePolicy.Warn),
        ("fail", UnknownValuePolicy.Fail),
        ("include", UnknownValuePolicy.Include),
    ];

    /// <summary><c>duplicate_object_policy</c> (§6/§6.1).</summary>
    internal static readonly (string Text, DuplicateObjectPolicy Value)[] DuplicateObjectPolicies =
    [
        ("fail", DuplicateObjectPolicy.Fail),
        ("keep", DuplicateObjectPolicy.Keep),
        ("dedupe", DuplicateObjectPolicy.Dedupe),
    ];

    /// <summary><c>ordinal_direction</c> / ordinal <c>direction</c> (§6/§12.3).</summary>
    internal static readonly (string Text, OrdinalDirection Value)[] Directions =
        [("ge", OrdinalDirection.Ge), ("le", OrdinalDirection.Le)];

    /// <summary><c>ordinal_boundary</c> / ordinal <c>boundary</c> (§6/§12.3).</summary>
    internal static readonly (string Text, OrdinalBoundary Value)[] Boundaries =
        [("inclusive", OrdinalBoundary.Inclusive), ("strict", OrdinalBoundary.Strict)];

    /// <summary>Cut discretizer <c>ends</c> (§11.2/§11.8).</summary>
    internal static readonly (string Text, BinEnds Value)[] Ends =
        [("open", BinEnds.Open), ("closed", BinEnds.Closed)];

    /// <summary>
    /// <c>equal_width.range</c> (§11.4). The table is the accepted TOML surface and
    /// equals the Core enum, <c>"percentile_p1_p99"</c> included (D-102/D-103).
    /// </summary>
    internal static readonly (string Text, EqualWidthRange Value)[] EqualWidthRanges =
    [
        ("min_max", EqualWidthRange.MinMax),
        ("percentile_p1_p99", EqualWidthRange.PercentileP1P99),
        ("manual", EqualWidthRange.Manual),
    ];

    /// <summary><c>equal_frequency.tie_policy</c> (§11.5); defaults to <c>"left"</c>.</summary>
    internal static readonly (string Text, TiePolicy Value)[] TiePolicies =
        [("left", TiePolicy.Left), ("right", TiePolicy.Right)];

    /// <summary><c>equal_frequency.cut_placement</c> (§11.5); defaults to <c>"right_value"</c>.</summary>
    internal static readonly (string Text, CutPlacement Value)[] CutPlacements =
        [("right_value", CutPlacement.RightValue), ("midpoint", CutPlacement.Midpoint)];

    /// <summary><c>value_groups.unmatched</c> (§11.6); defaults to <c>"skip"</c>.</summary>
    internal static readonly (string Text, ValueGroupsUnmatched Value)[] ValueGroupsUnmatchedKinds =
    [
        ("skip", ValueGroupsUnmatched.Skip),
        ("other", ValueGroupsUnmatched.Other),
        ("passthrough", ValueGroupsUnmatched.Passthrough),
    ];

    /// <summary>Source <c>value_type</c> (§10.2). <c>"date"</c> is reserved (D-038), not a member.</summary>
    internal static readonly (string Text, SourceValueType Value)[] ValueTypes =
        [("string", SourceValueType.String), ("number", SourceValueType.Number)];

    /// <summary>Output <c>line_endings</c> (§8).</summary>
    internal static readonly (string Text, LineEndings Value)[] LineEndingKinds =
        [("lf", LineEndings.Lf), ("crlf", LineEndings.Crlf)];

    // --- Kind vocabularies (§10.2 / §11 / §12) ---

    /// <summary>Wide column source kind (§10.2).</summary>
    internal const string ColumnSourceKind = "column";

    /// <summary>Triple predicate source kind (§10.2).</summary>
    internal const string PredicateSourceKind = "predicate";

    /// <summary>The reserved <c>value_type = "date"</c> spelling (§10.2/§11.7, D-038).</summary>
    internal const string DateValueType = "date";

    /// <summary>Executable discretizer kinds (D-070 tier 1).</summary>
    internal const string IdentityKind = "identity";

    /// <inheritdoc cref="IdentityKind"/>
    internal const string ManualCutsKind = "manual_cuts";

    /// <inheritdoc cref="IdentityKind"/>
    internal const string OrderedCutsKind = "ordered_cuts";

    /// <summary>The <c>free_per_value</c> discretizer kind (§11.3, D-101).</summary>
    internal const string FreePerValueKind = "free_per_value";

    /// <summary>The <c>equal_width</c> discretizer kind (§11.4, D-102).</summary>
    internal const string EqualWidthKind = "equal_width";

    /// <summary>The <c>equal_frequency</c> discretizer kind (§11.5, D-103).</summary>
    internal const string EqualFrequencyKind = "equal_frequency";

    /// <summary>The <c>value_groups</c> discretizer kind (§11.6, D-104).</summary>
    internal const string ValueGroupsKind = "value_groups";

    /// <summary>The <c>equal_width.precision = "exact"</c> spelling (§11.4).</summary>
    internal const string PrecisionExact = "exact";

    /// <summary>The <c>equal_width.precision = { round_to = r }</c> key (§11.4).</summary>
    internal const string RoundToKey = "round_to";

    // There is no D-070 tier-2 deferred-discretizer set: every v1 discretizer kind has a
    // carrier and executes (D-101 to D-104). An empty tier would be dead scaffolding whose
    // dispatch can never fire, and tier 3 (an unknown kind spelling → SpecFieldInvalid)
    // covers everything else.

    /// <summary>Implemented scale kinds (§12.1–§12.3).</summary>
    internal const string NominalKind = "nominal";

    /// <inheritdoc cref="NominalKind"/>
    internal const string DichotomicKind = "dichotomic";

    /// <inheritdoc cref="NominalKind"/>
    internal const string OrdinalKind = "ordinal";

    /// <summary>
    /// Modelled-but-deferred scale kinds (§12.4, D-010): parsed into kind-only
    /// carriers; the planner rejects with <c>ScaleNotImplementedV1</c>.
    /// </summary>
    internal static readonly string[] DeferredScaleKinds =
        ["interordinal", "biordinal", "contranominal"];

    // There is no D-075 key-level deferred-surface set (D-120): display_name and
    // formal_attribute_format have carriers on [defaults], [[attribute]] and [[template]],
    // and an empty set would be dead scaffolding, as with the tier-2 kind set above.

    /// <summary>Parses <paramref name="text"/> against a spelling table; exact (ordinal) match only.</summary>
    internal static bool TryParse<T>((string Text, T Value)[] table, string text, out T value)
        where T : struct
    {
        foreach (var (candidate, mapped) in table)
        {
            if (string.Equals(candidate, text, StringComparison.Ordinal))
            {
                value = mapped;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Renders <paramref name="value"/> via its spelling table. The tables are total, so this cannot miss.</summary>
    internal static string ToToml<T>((string Text, T Value)[] table, T value)
        where T : struct
    {
        foreach (var (text, mapped) in table)
        {
            if (EqualityComparer<T>.Default.Equals(mapped, value))
            {
                return text;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "No TOML spelling is registered for this value.");
    }

    /// <summary>Renders a table's spellings for a diagnostic message, e.g. <c>"wide" or "triple"</c>.</summary>
    internal static string Allowed<T>((string Text, T Value)[] table)
        where T : struct
    {
        var quoted = new string[table.Length];
        for (var i = 0; i < table.Length; i++)
        {
            quoted[i] = $"\"{table[i].Text}\"";
        }

        return quoted.Length == 1 ? quoted[0] : $"{string.Join(", ", quoted[..^1])} or {quoted[^1]}";
    }

    /// <summary>Ordinal membership test for the closed kind/surface sets.</summary>
    internal static bool IsIn(string[] set, string text)
    {
        foreach (var candidate in set)
        {
            if (string.Equals(candidate, text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
