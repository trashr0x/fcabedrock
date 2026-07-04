using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Reads one <c>[[attribute]]</c> (§10) into its presence-tracked section,
/// including the nested inline-table groups. Kind dispatch follows the D-070
/// three tiers for discretizers (full carrier / recognized-deferred reject /
/// unknown-kind field error) and D-010 for the deferred scales (kind-only
/// carrier; the planner rejects). Diagnostics raised inside the table carry the
/// attribute's name as scope (§16.3).
/// </summary>
internal static class AttributeReader
{
    public static AttributeSection Read(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[[attribute]]", table);
        var name = cursor.TakeString("name");
        context.AttributeName = name;
        try
        {
            var section = new AttributeSection(
                name,
                ReadSource(context, cursor),
                cursor.TakeString("description"),
                cursor.TakeBool("include"),
                ReadDiscretizer(context, cursor),
                ReadScale(context, cursor),
                cursor.TakeStringArray("declared_domain"),
                ReadRestrictTo(context, cursor),
                ReadValueLabels(context, cursor),
                cursor.TakeEnum("missing_policy", TomlSpellings.MissingPolicies),
                cursor.TakeEnum("unknown_value_policy", TomlSpellings.UnknownValuePolicies));
            cursor.Finish(TomlSpellings.AttributeDeferredKeys);
            return section;
        }
        finally
        {
            context.AttributeName = null;
        }
    }

    private static SourceSection? ReadSource(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeInlineTable("source") is not { } table)
        {
            return null;
        }

        var inner = new TomlTableCursor(context, "attribute source", table);
        var kind = TakeKind(context, inner, "attribute source", table.Span, "\"column\" or \"predicate\"");
        switch (kind)
        {
            case TomlSpellings.ColumnSourceKind:
                var column = new ColumnSourceSection(
                    inner.TakeInt("index"),
                    inner.TakeString("name"),
                    ReadValueType(context, inner));
                inner.Finish();
                return column;

            case TomlSpellings.PredicateSourceKind:
                var predicate = new PredicateSourceSection(
                    inner.TakeString("name"),
                    ReadValueType(context, inner));
                inner.Finish();
                return predicate;

            case null:
                return null; // TakeKind reported

            default:
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"attribute source kind '{kind}' is not recognized; expected \"column\" or \"predicate\" (§10.2).",
                    table.Span);
                return null;
        }
    }

    private static SourceValueType? ReadValueType(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("value_type") is not { } pair)
        {
            return null;
        }

        if (pair.Value is StringValueSyntax { Value: { } text })
        {
            if (TomlSpellings.TryParse(TomlSpellings.ValueTypes, text, out var valueType))
            {
                return valueType;
            }

            if (string.Equals(text, TomlSpellings.DateValueType, StringComparison.Ordinal))
            {
                // Interim D-075 reject: the Core enum deliberately lacks Date
                // until its carrier lands (D-038); the v1 end-state is a
                // plan-phase DateValueTypeNotImplementedV1 over a real carrier.
                context.Error(
                    DiagnosticCode.SpecSurfaceNotYetSupported,
                    "value_type = \"date\" is reserved v1 surface with no carrier in this build (D-038/D-075).",
                    pair.Value.Span);
                return null;
            }
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"source value_type expects {TomlSpellings.Allowed(TomlSpellings.ValueTypes)} (§10.2).",
            pair.Value?.Span ?? pair.Span);
        return null;
    }

    private static DiscretizerSection? ReadDiscretizer(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeInlineTable("discretizer") is not { } table)
        {
            return null;
        }

        var inner = new TomlTableCursor(context, "attribute discretizer", table);
        var kind = TakeKind(context, inner, "attribute discretizer", table.Span, "a §11 discretizer kind");
        switch (kind)
        {
            case TomlSpellings.IdentityKind:
                var identity = new IdentityDiscretizerSection();
                inner.Finish();
                return identity;

            case TomlSpellings.ManualCutsKind:
                var manual = new ManualCutsDiscretizerSection(
                    inner.TakeDoubleArray("cuts"),
                    inner.TakeEnum("ends", TomlSpellings.Ends));
                inner.Finish();
                return manual;

            case TomlSpellings.OrderedCutsKind:
                var ordered = new OrderedCutsDiscretizerSection(
                    inner.TakeStringArray("order"),
                    inner.TakeStringArray("cuts"),
                    inner.TakeEnum("ends", TomlSpellings.Ends));
                inner.Finish();
                return ordered;

            case null:
                return null; // TakeKind reported

            default:
                if (TomlSpellings.IsIn(TomlSpellings.DeferredDiscretizerKinds, kind))
                {
                    // D-070 tier 2: recognized by kind name only — no carrier is
                    // built, no round-trip is promised, and the parameter keys
                    // are deliberately not walked (no unknown-key noise).
                    context.Error(
                        DiagnosticCode.DiscretizerKindNotYetSupported,
                        $"discretizer kind '{kind}' is a valid v1 discretizer this build does not support yet; it lands at M4 (D-070).",
                        table.Span);
                    return null;
                }

                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"discretizer kind '{kind}' is not recognized (§11).",
                    table.Span);
                return null;
        }
    }

    private static ScaleSection? ReadScale(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeInlineTable("scale") is not { } table)
        {
            return null;
        }

        var inner = new TomlTableCursor(context, "attribute scale", table);
        var kind = TakeKind(context, inner, "attribute scale", table.Span, "a §12 scale kind");
        switch (kind)
        {
            case TomlSpellings.NominalKind:
                var nominal = new NominalScaleSection();
                inner.Finish();
                return nominal;

            case TomlSpellings.DichotomicKind:
                var dichotomic = new DichotomicScaleSection(inner.TakeString("true_value"));
                inner.Finish();
                return dichotomic;

            case TomlSpellings.OrdinalKind:
                var ordinal = new OrdinalScaleSection(
                    inner.TakeEnum("direction", TomlSpellings.Directions),
                    inner.TakeEnum("boundary", TomlSpellings.Boundaries),
                    inner.TakeStringArray("order"),
                    inner.TakeBool("drop_top"));
                inner.Finish();
                return ordinal;

            case null:
                return null; // TakeKind reported

            default:
                if (TomlSpellings.IsIn(TomlSpellings.DeferredScaleKinds, kind))
                {
                    // D-010: parsable kind-only carrier; the spec defines no
                    // parameters for these, so any extra key is unknown. The
                    // planner rejects the resolved marker (ScaleNotImplementedV1).
                    var deferred = new DeferredScaleSection(kind);
                    inner.Finish();
                    return deferred;
                }

                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"attribute scale kind '{kind}' is not recognized (§12).",
                    table.Span);
                return null;
        }
    }

    private static IReadOnlyList<RestrictToEntry>? ReadRestrictTo(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeArray("restrict_to") is not { } array)
        {
            return null;
        }

        var entries = new List<RestrictToEntry>();
        foreach (var item in array.Items)
        {
            switch (item.Value)
            {
                case StringValueSyntax { Value: { } value }:
                    entries.Add(new RestrictToValue(value));
                    break;

                case InlineTableSyntax range:
                    var inner = new TomlTableCursor(context, "restrict_to range", range);
                    entries.Add(new RestrictToRange(inner.TakeDouble("from"), inner.TakeDouble("to")));
                    inner.Finish();
                    break;

                case { } node:
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        "restrict_to entries are raw-value strings or { from, to } ranges (§10.4).",
                        node.Span);
                    break;

                default:
                    break; // malformed item; Tomlyn reported
            }
        }

        return entries;
    }

    private static IReadOnlyDictionary<string, string>? ReadValueLabels(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeInlineTable("value_labels") is not { } table)
        {
            return null;
        }

        // Insertion order is the authored order; the writer preserves it (D-075).
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in table.Items)
        {
            if (item.KeyValue is not { } pair || pair.Key is not { } key)
            {
                continue; // malformed; Tomlyn reported
            }

            if (key.DotKeys is { ChildrenCount: > 0 })
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    "value_labels keys are raw values and must be simple keys (quote them if needed, §10.8).",
                    key.Span);
                continue;
            }

            if (TomlSyntaxHelpers.KeyText(key.Key) is not { } raw)
            {
                continue;
            }

            if (pair.Value is StringValueSyntax { Value: { } label })
            {
                labels[raw] = label;
            }
            else
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"value_labels entry '{raw}' expects a string label (§10.8).",
                    pair.Value?.Span ?? pair.Span);
            }
        }

        return labels;
    }

    private static string? TakeKind(
        TomlReadContext context,
        TomlTableCursor cursor,
        string label,
        SourceSpan tableSpan,
        string expected)
    {
        if (cursor.Take("kind") is not { } pair)
        {
            context.Error(DiagnosticCode.SpecFieldInvalid, $"{label} declares no kind; expected {expected}.", tableSpan);
            return null;
        }

        if (pair.Value is StringValueSyntax { Value: { } kind })
        {
            return kind;
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"{label} kind expects a string ({expected}).",
            pair.Value?.Span ?? pair.Span);
        return null;
    }
}
