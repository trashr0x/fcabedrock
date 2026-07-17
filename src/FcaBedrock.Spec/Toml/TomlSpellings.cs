using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Single source of truth for the TOML surface's vocabulary: one spelling table
/// per enum, consumed in both directions by the reader and the writer so the two
/// can never drift (P-5); the kind names behind the D-070 three-tier dispatch;
/// and the closed, per-table deferred-surface sets behind the transitional
/// <c>SpecSurfaceNotYetSupported</c> reject (D-075) — never a fallback for
/// unknown keys.
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
    /// <c>equal_width.range</c> (§11.4). The table is the accepted TOML surface; it now
    /// equals the Core enum — <c>"percentile_p1_p99"</c> joined at M4 Slice D together
    /// with its exact bounded-memory calibration (D-103), closing the Slice C
    /// transitional gap in which the spelling was modelled but unreachable (D-102/G-8).
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

    /// <summary>M2-executable discretizer kinds (D-070 tier 1).</summary>
    internal const string IdentityKind = "identity";

    /// <inheritdoc cref="IdentityKind"/>
    internal const string ManualCutsKind = "manual_cuts";

    /// <inheritdoc cref="IdentityKind"/>
    internal const string OrderedCutsKind = "ordered_cuts";

    /// <summary>The <c>free_per_value</c> discretizer kind (§11.3, M4 Slice B / D-101).</summary>
    internal const string FreePerValueKind = "free_per_value";

    /// <summary>The <c>equal_width</c> discretizer kind (§11.4, M4 Slice C / D-102).</summary>
    internal const string EqualWidthKind = "equal_width";

    /// <summary>The <c>equal_frequency</c> discretizer kind (§11.5, M4 Slice D / D-103).</summary>
    internal const string EqualFrequencyKind = "equal_frequency";

    /// <summary>The <c>value_groups</c> discretizer kind (§11.6, M4 Slice E / D-104).</summary>
    internal const string ValueGroupsKind = "value_groups";

    /// <summary>The <c>equal_width.precision = "exact"</c> spelling (§11.4).</summary>
    internal const string PrecisionExact = "exact";

    /// <summary>The <c>equal_width.precision = { round_to = r }</c> key (§11.4).</summary>
    internal const string RoundToKey = "round_to";

    // The D-070 tier-2 deferred-discretizer set (and its DiscretizerKindNotYetSupported
    // reject) retired at M4 Slice E (D-104): every v1 discretizer kind now has a carrier and
    // executes — free_per_value at Slice B (D-101), equal_width at Slice C (D-102),
    // equal_frequency at Slice D (D-103), value_groups here. The set is removed rather than
    // kept empty: an empty tier is dead scaffolding whose dispatch can never fire, and tier 3
    // (an unknown kind spelling → SpecFieldInvalid) already covers everything else.

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

    // --- Deferred v1 surface (D-075): closed, per-owning-table sets ---
    //
    // Each entry is a recognized v1 concept the document model does not yet
    // carry; the reader rejects it with the transitional
    // SpecSurfaceNotYetSupported so nothing known is silently dropped. The sets
    // are exact: near-miss keys and a listed name in the wrong table fall to
    // SpecKeyUnrecognized. Entries retire as slices land their carriers —
    // Slice F retired extends/template/matcher (D-078); the remaining entries
    // belong to the naming-fidelity slice.

    /// <summary><c>[defaults]</c> keys deferred to the naming-fidelity slice.</summary>
    internal static readonly string[] DefaultsDeferredKeys = ["formal_attribute_format"];

    /// <summary>
    /// <c>[[attribute]]</c> keys deferred to the naming-fidelity slice; also the
    /// deferred set for <c>[[template]]</c> bodies, which carry the same config
    /// surface (§9.1, D-078).
    /// </summary>
    internal static readonly string[] AttributeDeferredKeys = ["display_name", "formal_attribute_format"];

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
