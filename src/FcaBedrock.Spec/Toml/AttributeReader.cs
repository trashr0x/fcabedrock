using System.Text.RegularExpressions;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Reads one <c>[[attribute]]</c> (§10) — and one <c>[[template]]</c> (§9.1),
/// whose body is the attribute config surface minus
/// <c>name</c>/<c>source</c>/<c>description</c> — into its presence-tracked
/// section, including the nested inline-table groups. Kind dispatch follows the
/// D-070 three tiers for discretizers (full carrier / recognized-deferred
/// reject / unknown-kind field error) and D-010 for the deferred scales
/// (kind-only carrier; the planner rejects). Diagnostics raised inside an
/// attribute table carry the attribute's name as scope (§16.3).
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
                cursor.TakeString("template"),
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

    /// <summary>
    /// Reads one <c>[[template]]</c> (§9.1). The identity fields
    /// <c>name</c>/<c>source</c>/<c>description</c> are always per-attribute, so
    /// here they are simply not taken and fall to <c>SpecKeyUnrecognized</c>
    /// (the D-075 listed-name-in-the-wrong-table stance); the naming-deferred
    /// keys stay <c>SpecSurfaceNotYetSupported</c> exactly as on attributes.
    /// </summary>
    public static TemplateSection ReadTemplate(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[[template]]", table);
        var section = new TemplateSection(
            cursor.TakeString("id"),
            cursor.TakeBool("include"),
            ReadDiscretizer(context, cursor, owner: "template"),
            ReadScale(context, cursor, owner: "template"),
            cursor.TakeStringArray("declared_domain"),
            ReadRestrictTo(context, cursor),
            ReadValueLabels(context, cursor),
            cursor.TakeEnum("missing_policy", TomlSpellings.MissingPolicies),
            cursor.TakeEnum("unknown_value_policy", TomlSpellings.UnknownValuePolicies));
        cursor.Finish(TomlSpellings.AttributeDeferredKeys);
        return section;
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

    private static DiscretizerSection? ReadDiscretizer(TomlReadContext context, TomlTableCursor cursor, string owner = "attribute")
    {
        if (cursor.TakeInlineTable("discretizer") is not { } table)
        {
            return null;
        }

        var inner = new TomlTableCursor(context, $"{owner} discretizer", table);
        var kind = TakeKind(context, inner, $"{owner} discretizer", table.Span, "a §11 discretizer kind");
        switch (kind)
        {
            case TomlSpellings.IdentityKind:
                var identity = new IdentityDiscretizerSection();
                inner.Finish();
                return identity;

            case TomlSpellings.FreePerValueKind:
                // §11.3 / D-061: no authored parameters; the value_type (source) decides
                // string-vs-numeric identity. Numeric domain/label/order keys stay verbatim
                // in the document and normalize at the seam (D-096).
                var freePerValue = new FreePerValueDiscretizerSection();
                inner.Finish();
                return freePerValue;

            case TomlSpellings.ManualCutsKind:
                var manual = new ManualCutsDiscretizerSection(
                    inner.TakeDoubleArray("cuts"),
                    inner.TakeEnum("ends", TomlSpellings.Ends));
                inner.Finish();
                return manual;

            case TomlSpellings.EqualWidthKind:
                return ReadEqualWidth(context, inner, table.Span);

            case TomlSpellings.EqualFrequencyKind:
                return ReadEqualFrequency(context, inner, table.Span);

            case TomlSpellings.ValueGroupsKind:
                return ReadValueGroups(context, inner, table.Span);

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
                // D-070 tier 3, now the only tier: every v1 discretizer kind has a carrier as of
                // M4 Slice E (D-104), so an unrecognized spelling is an ordinary field error.
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"discretizer kind '{kind}' is not recognized (§11).",
                    table.Span);
                return null;
        }
    }

    // §11.6 (D-090/D-104): value_groups' authored fields. Parse owns every field shape — the
    // groups array and each group's label/values/pattern validity (including the regex compile
    // check and the G-11 matcher predicate) and the unmatched spelling — all as SpecFieldInvalid,
    // the one code §11.6 assigns; there is deliberately no dedicated regex-error code. The seam
    // owns what the values IMPLY across groups (ValueGroupsLabelDuplicate,
    // OrdinalNotAllowedWithValueGroupsPassthrough).
    //
    // Every field is read before the gates run, so independent failures report together (P-14):
    // a spec with a bad group AND a bad unmatched reports both.
    private static DiscretizerSection ReadValueGroups(TomlReadContext context, TomlTableCursor inner, SourceSpan tableSpan)
    {
        var groups = ReadGroups(context, inner, tableSpan);
        var unmatched = TakeValueGroupsUnmatched(context, inner);
        inner.Finish();

        // The carrier holds only what resolved: an authored-but-unrecognized unmatched carries
        // null (its Error already fails the read), exactly like every other malformed field.
        return new ValueGroupsDiscretizerSection(groups, unmatched);
    }

    private static IReadOnlyList<ValueGroupSection>? ReadGroups(
        TomlReadContext context, TomlTableCursor cursor, SourceSpan tableSpan)
    {
        if (cursor.Take("groups") is not { } pair)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                "value_groups discretizer declares no groups; expected an array of { label, values/pattern } tables (§11.6).",
                tableSpan);
            return null;
        }

        if (pair.Value is not ArraySyntax array)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                "value_groups discretizer key 'groups' expects an array of { label, values/pattern } tables (§11.6).",
                pair.Value?.Span ?? pair.Span);
            return null;
        }

        // Declaration order is preserved because it is semantic — first match wins (§11.6).
        var groups = new List<ValueGroupSection>(array.Items.ChildrenCount);
        foreach (var item in array.Items)
        {
            if (item.Value is not InlineTableSyntax table)
            {
                if (item.Value is { } node)
                {
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        "value_groups groups entries are { label, values/pattern } tables (§11.6).",
                        node.Span);
                }

                continue; // a malformed item Tomlyn already reported
            }

            if (ReadGroup(context, table) is { } group)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    // One group: label present and non-empty; every authored explicit value non-empty; an
    // authored pattern non-empty and compilable; and at least one usable matcher.
    //
    // Each field is read PRESENCE-AWARE (authored-vs-absent, not merely valid-vs-null), the same
    // discipline equal_width's vmin/vmax follow: the cursor's typed accessors collapse absent and
    // malformed to null, and the gates below must tell them apart so a malformed field reports its
    // own type error once instead of also being called missing or matcher-less (D-067, one
    // condition → one code). A field that failed its own gate returns null for the whole group —
    // its Error already fails the read, and continuing would only pile on.
    private static ValueGroupSection? ReadGroup(TomlReadContext context, InlineTableSyntax table)
    {
        var inner = new TomlTableCursor(context, "value_groups group", table);
        var (label, labelAuthored) = TakeGroupString(context, inner, "label");
        var (values, valuesAuthored) = TakeGroupValues(context, inner);
        var (pattern, patternAuthored) = TakeGroupString(context, inner, "pattern");
        inner.Finish();

        // A field authored-but-malformed already reported; its shape is unknown, so the
        // label/matcher rules below cannot be judged and would only add noise.
        if ((labelAuthored && label is null) || (valuesAuthored && values is null) || (patternAuthored && pattern is null))
        {
            return null;
        }

        if (!labelAuthored || label!.Length == 0)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                "value_groups group declares no label; every group needs a non-empty label (§11.6).",
                table.Span);
            return null;
        }

        var usableValues = false;
        if (values is { } authored)
        {
            foreach (var value in authored)
            {
                if (value.Length == 0)
                {
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        $"value_groups group '{label}' declares an empty explicit value; every authored value must be non-empty (§11.6).",
                        table.Span);
                    return null;
                }
            }

            usableValues = authored.Count > 0;
        }

        var usablePattern = false;
        if (pattern is { } authoredPattern)
        {
            if (authoredPattern.Length == 0)
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"value_groups group '{label}' declares an empty pattern; an authored pattern must be non-empty (§11.6).",
                    table.Span);
                return null;
            }

            // The compile check at parse (D-090): an uncompilable pattern is ONE SpecFieldInvalid
            // condition, not a code of its own. Compiled with the same options AND the same
            // explicit InfiniteMatchTimeout ValueGroup.Create uses — that type is the semantic
            // authority; this is the parse gate that keeps the strict factory behind a clean
            // success gate, the same reader-gates/factory-backstops split `bins` and `precision`
            // already follow. (The timeout cannot change which patterns COMPILE, but matching the
            // construction exactly is what stops the two sites drifting.)
            try
            {
                _ = new Regex(authoredPattern, RegexOptions.CultureInvariant, Regex.InfiniteMatchTimeout);
                usablePattern = true;
            }
            catch (ArgumentException ex)
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"value_groups group '{label}' declares an invalid regex pattern (§11.6): {ex.Message}",
                    table.Span);
                return null;
            }
        }

        // G-11: at least one NON-EMPTY explicit value or a non-empty pattern, so `values = []`
        // alone is invalid while `values = []` alongside a pattern is valid.
        if (!usableValues && !usablePattern)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                $"value_groups group '{label}' carries no usable matcher; a group needs at least one explicit value or a pattern (§11.6).",
                table.Span);
            return null;
        }

        return new ValueGroupSection(label, values, pattern);
    }

    private static (string? Value, bool Authored) TakeGroupString(
        TomlReadContext context, TomlTableCursor cursor, string key)
    {
        if (cursor.Take(key) is not { } pair)
        {
            return (null, false);
        }

        if (pair.Value is StringValueSyntax { Value: { } text })
        {
            return (text, true);
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"value_groups group key '{key}' expects a string (§11.6).",
            pair.Value?.Span ?? pair.Span);
        return (null, true);
    }

    // An omitted `values` and an authored `values = []` are DIFFERENT authored states the
    // document must keep apart — the §14 encoding writes `values` only when authored, so the two
    // are byte-distinct (G-11/D-094) — hence the explicit authored flag rather than "null means
    // absent". Authored order and duplicates are preserved verbatim.
    private static (IReadOnlyList<string>? Value, bool Authored) TakeGroupValues(
        TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("values") is not { } pair)
        {
            return (null, false);
        }

        if (pair.Value is not ArraySyntax array)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                "value_groups group key 'values' expects an array of strings (§11.6).",
                pair.Value?.Span ?? pair.Span);
            return (null, true);
        }

        var values = new List<string>(array.Items.ChildrenCount);
        foreach (var item in array.Items)
        {
            if (item.Value is StringValueSyntax { Value: { } value })
            {
                values.Add(value);
                continue;
            }

            if (item.Value is { } node)
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    "value_groups group 'values' entries must be strings (§11.6).",
                    node.Span);
                return (null, true);
            }
        }

        return (values, true);
    }

    private static ValueGroupsUnmatched? TakeValueGroupsUnmatched(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("unmatched") is not { } pair)
        {
            return null; // §11.6 default ("skip") resolves at the seam, never in the document
        }

        if (pair.Value is StringValueSyntax { Value: { } text }
            && TomlSpellings.TryParse(TomlSpellings.ValueGroupsUnmatchedKinds, text, out var value))
        {
            return value;
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"value_groups discretizer key 'unmatched' expects {TomlSpellings.Allowed(TomlSpellings.ValueGroupsUnmatchedKinds)} (§11.6).",
            pair.Value?.Span ?? pair.Span);
        return null;
    }

    // §11.4 (D-089/D-102): equal_width's authored fields. Parse owns the field shapes —
    // bins presence and its 2..int.MaxValue range, the range spelling, the vmin/vmax
    // presence rules, and the precision form (SpecFieldInvalid). The seam owns the
    // semantics the values imply (EqualWidthRangeInvalid / EqualWidthCutsCollapsed).
    private static DiscretizerSection ReadEqualWidth(TomlReadContext context, TomlTableCursor inner, SourceSpan tableSpan)
    {
        // Every field is read presence-aware (authored-vs-absent, not just valid-vs-null): the
        // semantic gates below must fire on what the author actually wrote, so a malformed field
        // reports its own type error once instead of also being called missing (D-067, one
        // condition → one code).
        var (range, rangeAuthored, rangeValid) = TakeEqualWidthRange(context, inner);
        var (bins, binsAuthored) = TakeBins(context, inner, TomlSpellings.EqualWidthKind, "§11.4");
        var (vmin, vminAuthored) = TakeBound(context, inner, "vmin");
        var (vmax, vmaxAuthored) = TakeBound(context, inner, "vmax");
        var precision = ReadPrecision(context, inner);
        inner.Finish();

        ValidateBins(context, TomlSpellings.EqualWidthKind, "§11.4", bins, binsAuthored, tableSpan);

        // An unrecognized range spelling already reported; its mode is unknown, so the vmin/vmax
        // rules below cannot be judged and would only add noise.
        if (rangeValid)
        {
            // §11.4: range defaults to min_max, so an omitted range forbids vmin/vmax too.
            if ((rangeAuthored ? range : EqualWidthRange.MinMax) == EqualWidthRange.Manual)
            {
                if (!vminAuthored || !vmaxAuthored)
                {
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        "equal_width discretizer with range = \"manual\" requires both vmin and vmax (§11.4).",
                        tableSpan);
                }
            }
            else if (vminAuthored || vmaxAuthored)
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    "equal_width discretizer declares vmin/vmax, which apply only to range = \"manual\"; a data-derived range draws its span from the data (§11.4).",
                    tableSpan);
            }
        }

        // The carrier holds only what resolved: an authored-but-unrecognized range carries null
        // (its Error already fails the read), exactly like any other malformed field.
        return new EqualWidthDiscretizerSection(
            bins, rangeAuthored && rangeValid ? range : null, vmin, vmax, precision);
    }

    // §11.5 (D-103): equal_frequency's authored fields. Parse owns the field shapes — bins
    // presence and its 2..int.MaxValue range, and the tie_policy/cut_placement spellings
    // (SpecFieldInvalid). There is no range/span surface: equal_frequency draws its cuts from
    // the population under every configuration (§7), so unlike equal_width it has no
    // spec-determined mode and no vmin/vmax cross-checks. Independent failures are reported
    // together — every field is read before the gates run, so a spec with a bad bins AND a bad
    // tie_policy reports both rather than stopping at the first (P-14).
    private static DiscretizerSection ReadEqualFrequency(TomlReadContext context, TomlTableCursor inner, SourceSpan tableSpan)
    {
        var (bins, binsAuthored) = TakeBins(context, inner, TomlSpellings.EqualFrequencyKind, "§11.5");
        var tiePolicy = TakeSpelling(context, inner, "tie_policy", TomlSpellings.TiePolicies, "§11.5");
        var cutPlacement = TakeSpelling(context, inner, "cut_placement", TomlSpellings.CutPlacements, "§11.5");
        inner.Finish();

        ValidateBins(context, TomlSpellings.EqualFrequencyKind, "§11.5", bins, binsAuthored, tableSpan);

        // The carrier holds only what resolved: an authored-but-unrecognized spelling carries
        // null (its Error already fails the read), so an omitted field and a rejected one are
        // indistinguishable downstream — which is correct, since neither can be written back.
        return new EqualFrequencyDiscretizerSection(bins, tiePolicy, cutPlacement);
    }

    // The shared bins contract (§11.4/§11.5): authored, an integer, and within 2..int.MaxValue.
    // The document carrier keeps it `long?` (that is what TOML integers are); this gate is what
    // makes the seam's narrowing to `int` total.
    private static void ValidateBins(
        TomlReadContext context, string kind, string section, long? bins, bool authored, SourceSpan tableSpan)
    {
        if (!authored)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                $"{kind} discretizer declares no bins; expected an integer of at least 2 ({section}).",
                tableSpan);
        }
        else if (bins is { } authoredBins && authoredBins is < 2 or > int.MaxValue)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                $"{kind} discretizer bins {authoredBins} is out of range; expected an integer from 2 to {int.MaxValue} ({section}).",
                tableSpan);
        }
    }

    // The presence-aware reads. The cursor's typed accessors collapse absent and malformed to
    // null, which the semantic gates above must tell apart; each reports its own type error, so a
    // malformed field is never also reported as missing.
    private static (long? Value, bool Authored) TakeBins(
        TomlReadContext context, TomlTableCursor cursor, string kind, string section)
    {
        if (cursor.Take("bins") is not { } pair)
        {
            return (null, false);
        }

        if (pair.Value is IntegerValueSyntax integer)
        {
            return (integer.Value, true);
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"{kind} discretizer key 'bins' expects an integer ({section}).",
            pair.Value?.Span ?? pair.Span);
        return (null, true);
    }

    // An optional enum-spelled field: absent → null (the §11.5 default applies at the seam);
    // authored-and-recognized → its value; authored-and-unrecognized → null after its own Error.
    private static T? TakeSpelling<T>(
        TomlReadContext context, TomlTableCursor cursor, string key, (string Text, T Value)[] table, string section)
        where T : struct
    {
        if (cursor.Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is StringValueSyntax { Value: { } text } && TomlSpellings.TryParse(table, text, out var value))
        {
            return value;
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"{TomlSpellings.EqualFrequencyKind} discretizer key '{key}' expects {TomlSpellings.Allowed(table)} ({section}).",
            pair.Value?.Span ?? pair.Span);
        return null;
    }

    private static (double? Value, bool Authored) TakeBound(TomlReadContext context, TomlTableCursor cursor, string key)
    {
        if (cursor.Take(key) is not { } pair)
        {
            return (null, false);
        }

        if (TomlSyntaxHelpers.AsDouble(pair.Value) is { } value)
        {
            return (value, true);
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"equal_width discretizer key '{key}' expects a number (§11.4).",
            pair.Value?.Span ?? pair.Span);
        return (null, true);
    }

    // Reads `range`, distinguishing absent (default min_max) from an unrecognized spelling — the
    // vmin/vmax cross-checks need to tell them apart. The accepted spellings are the TOML surface,
    // not the Core enum: "percentile_p1_p99" is modelled in Core but not accepted until its
    // calibration lands (D-102).
    private static (EqualWidthRange Value, bool Authored, bool Valid) TakeEqualWidthRange(
        TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("range") is not { } pair)
        {
            return (default, false, true);
        }

        if (pair.Value is StringValueSyntax { Value: { } text }
            && TomlSpellings.TryParse(TomlSpellings.EqualWidthRanges, text, out var value))
        {
            return (value, true, true);
        }

        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"equal_width discretizer key 'range' expects {TomlSpellings.Allowed(TomlSpellings.EqualWidthRanges)} (§11.4).",
            pair.Value?.Span ?? pair.Span);
        return (default, true, false);
    }

    // §11.4: precision is the string "exact" or the inline table { round_to = <number> },
    // whose step must be finite and greater than zero. Unknown keys inside the object fall
    // to the cursor's SpecKeyUnrecognized, exactly like any other table (D-075).
    private static CutPrecision? ReadPrecision(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("precision") is not { } pair)
        {
            return null;
        }

        switch (pair.Value)
        {
            case StringValueSyntax { Value: TomlSpellings.PrecisionExact }:
                return CutPrecision.Exact;

            case InlineTableSyntax table:
            {
                var inner = new TomlTableCursor(context, "discretizer precision", table);
                var roundTo = inner.Take(TomlSpellings.RoundToKey);
                inner.Finish();

                if (roundTo is null)
                {
                    context.Error(
                        DiagnosticCode.SpecFieldInvalid,
                        "discretizer precision object declares no round_to; expected { round_to = <number greater than 0> } (§11.4).",
                        table.Span);
                    return null;
                }

                if (TomlSyntaxHelpers.AsDouble(roundTo.Value) is { } step && double.IsFinite(step) && step > 0)
                {
                    return RoundToPrecision.Create(step);
                }

                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    "discretizer precision round_to expects a finite number greater than zero (§11.4).",
                    roundTo.Value?.Span ?? roundTo.Span);
                return null;
            }

            default:
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    "discretizer precision expects \"exact\" or { round_to = <number> } (§11.4).",
                    pair.Value?.Span ?? pair.Span);
                return null;
        }
    }

    private static ScaleSection? ReadScale(TomlReadContext context, TomlTableCursor cursor, string owner = "attribute")
    {
        if (cursor.TakeInlineTable("scale") is not { } table)
        {
            return null;
        }

        var inner = new TomlTableCursor(context, $"{owner} scale", table);
        var kind = TakeKind(context, inner, $"{owner} scale", table.Span, "a §12 scale kind");
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
                    $"{owner} scale kind '{kind}' is not recognized (§12).",
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
