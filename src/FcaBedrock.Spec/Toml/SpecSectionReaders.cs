using FcaBedrock.Diagnostics;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Readers for the non-attribute sections: each takes its known keys through a
/// <see cref="TomlTableCursor"/> and finishes, so every unconsumed key is
/// <c>SpecKeyUnrecognized</c> (the closed D-075 deferred-key sets retired with
/// their carriers at M6 Slice A, D-120). Possibly-invalid values are document
/// territory (D-066) — nothing here validates semantics, with the one exception
/// of authored shape the grammar owns (<c>formal_attribute_format</c>, §10.7).
/// </summary>
internal static class SpecSectionReaders
{
    public static SpecSection ReadSpec(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[spec]", table);
        var section = new SpecSection(
            cursor.TakeLong("version"),
            cursor.TakeString("schema_fingerprint"),
            cursor.TakeString("cxt_output_fingerprint"),
            cursor.TakeString("dat_output_fingerprint"),
            cursor.TakeString("extends"),
            cursor.TakeString("description"));
        cursor.Finish();
        return section;
    }

    /// <summary>
    /// Reads one <c>[[matcher]]</c> (§9.2), including its static shape: a matcher
    /// MUST reference a template and MUST author exactly one selector. These are
    /// authored-shape rules, so they are parse-owned under the ordinary
    /// <c>SpecFieldInvalid</c> — §9.2/D-116 mint no matcher-specific parse code.
    /// They fire even on a matcher that will go on to select nothing, exactly as the
    /// naming-format grammar fires inside an unused template (§10.7).
    /// </summary>
    public static MatcherSection ReadMatcher(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[[matcher]]", table);

        // The table header, for the two conditions whose cause is an ABSENT key and so
        // have no span of their own.
        var anchor = table.Name?.Span ?? table.Span;

        var match = ReadMatch(context, cursor, anchor);

        // Has before Take: TakeString already reports a non-string value, so checking
        // authorship separately is what keeps a malformed template from also reporting
        // as a missing one (one condition, one diagnostic — D-067).
        var templateAuthored = cursor.Has("template");
        var section = new MatcherSection(match, cursor.TakeString("template"));
        if (!templateAuthored)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                "[[matcher]] declares no template; a matcher must reference a [[template]] id (§9.2).",
                anchor);
        }

        cursor.Finish();
        return section;
    }

    public static ProvenanceSection ReadProvenance(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[provenance]", table);
        var section = new ProvenanceSection(
            cursor.TakeString("author"),
            cursor.TakeDateTime("created_at"),
            cursor.TakeString("source_url"),
            cursor.TakeString("source_hash"),
            cursor.TakeString("derived_from"),
            cursor.TakeString("notes"));
        cursor.Finish();
        return section;
    }

    public static BindingSection ReadBinding(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[binding]", table);
        var section = new BindingSection(
            cursor.TakeEnum("shape", TomlSpellings.Shapes),
            cursor.TakeString("encoding"),
            cursor.TakeChar("delimiter"),
            cursor.TakeChar("quote_char"),
            cursor.TakeBool("has_header"),
            cursor.TakeString("locale"),
            cursor.TakeString("missing_token"),
            cursor.TakeEnum("ordering", TomlSpellings.Orderings),
            ReadTripleColumns(context, cursor),
            ObjectKey: null); // [binding.object_key] is its own table; merged by SpecReader
        cursor.Finish();
        return section;
    }

    public static ObjectKeySection ReadObjectKey(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[binding.object_key]", table);
        var section = new ObjectKeySection(
            cursor.TakeEnum("mode", TomlSpellings.ObjectKeyModes),
            ReadColumnRef(context, cursor, "column",
                "[binding.object_key] key 'column' expects a 0-based column index or a header name (§5.4)."),
            cursor.TakeStringArray("columns"),
            cursor.TakeEnum("aggregate", TomlSpellings.Aggregates));
        cursor.Finish();
        return section;
    }

    public static DefaultsSection ReadDefaults(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[defaults]", table);
        var section = new DefaultsSection(
            cursor.TakeBool("include"),
            cursor.TakeEnum("missing_policy", TomlSpellings.MissingPolicies),
            cursor.TakeEnum("unknown_value_policy", TomlSpellings.UnknownValuePolicies),
            cursor.TakeEnum("duplicate_object_policy", TomlSpellings.DuplicateObjectPolicies),
            cursor.TakeEnum("ordinal_direction", TomlSpellings.Directions),
            cursor.TakeEnum("ordinal_boundary", TomlSpellings.Boundaries))
        {
            // §6/§10.7: the spec-wide naming override, validated by the same grammar
            // owner the attribute and template keys use (D-120).
            FormalAttributeFormat = AttributeReader.ReadNameFormat(context, cursor),
        };
        cursor.Finish();
        return section;
    }

    /// <summary>Reads <c>[output]</c>'s one direct field; <c>[output.cxt]</c>/<c>[output.dat]</c> are their own tables.</summary>
    public static bool? ReadOutput(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[output]", table);
        var unicode = cursor.TakeBool("bin_label_unicode");
        cursor.Finish();
        return unicode;
    }

    public static CxtOutputSection ReadOutputCxt(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[output.cxt]", table);
        var section = new CxtOutputSection(
            cursor.TakeEnum("line_endings", TomlSpellings.LineEndingKinds),
            cursor.TakeBool("trailing_newline"),
            cursor.TakeLong("size_advisory_bytes"));
        cursor.Finish();
        return section;
    }

    public static DatOutputSection ReadOutputDat(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[output.dat]", table);
        var section = new DatOutputSection(
            cursor.TakeEnum("line_endings", TomlSpellings.LineEndingKinds),
            cursor.TakeInt("base_index"),
            cursor.TakeBool("nonempty_line_trailing_space"),
            cursor.TakeBool("empty_line_trailing_space"))
        {
            TrailingNewline = cursor.TakeBool("trailing_newline"),
        };
        cursor.Finish();
        return section;
    }

    /// <summary>
    /// Reads a matcher's <c>match</c> table and enforces §9.2's <b>exactly one
    /// selector</b> rule. Both selectors, or neither, is <c>SpecFieldInvalid</c> —
    /// AND/OR semantics for two authored selectors would be ambiguous, so one is the
    /// clear contract (D-115).
    /// </summary>
    private static MatchSection? ReadMatch(TomlReadContext context, TomlTableCursor cursor, SourceSpan anchor)
    {
        var authored = cursor.Has("match");
        if (cursor.TakeInlineTable("match") is not { } table)
        {
            if (!authored)
            {
                context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    "[[matcher]] declares no match table; a matcher authors exactly one selector — name_regex or source_index_range (§9.2).",
                    anchor);
            }

            return null; // an authored-but-malformed `match` was already reported by TakeInlineTable
        }

        var inner = new TomlTableCursor(context, "matcher match", table);

        // Authorship decides the arity rule; validity is checked only for the single
        // authored selector, so a both/neither document reports exactly one condition
        // rather than cascading into per-selector complaints as well.
        var regexAuthored = inner.Has("name_regex");
        var rangeAuthored = inner.Has("source_index_range");

        if (regexAuthored == rangeAuthored)
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                regexAuthored
                    ? "matcher match authors both name_regex and source_index_range; exactly one selector is allowed (§9.2)."
                    : "matcher match authors no selector; a matcher authors exactly one of name_regex or source_index_range (§9.2).",
                table.Span);

            // Consume both so Finish does not ALSO report them as unrecognized keys —
            // the arity is the condition, and the keys themselves are recognized surface.
            _ = inner.Take("name_regex");
            _ = inner.Take("source_index_range");
            inner.Finish();
            return null;
        }

        var section = new MatchSection(
            regexAuthored ? ReadNameRegex(context, inner) : null,
            rangeAuthored ? ReadSourceIndexRange(context, inner) : null);
        inner.Finish();
        return section;
    }

    /// <summary>
    /// Reads and gates a <c>name_regex</c> (§9.2/D-115): non-empty and compilable
    /// <b>in the wrapped whole-name form that actually executes</b>
    /// (<see cref="MatcherSelectors.TryCompileWholeName"/>), so a pattern cannot pass
    /// parse and then fail — or match differently — at evaluation. An uncompilable
    /// pattern is one <c>SpecFieldInvalid</c>, not a regex-error code of its own: the
    /// same stance <c>value_groups.pattern</c> takes (§11.6/D-090).
    /// </summary>
    private static string? ReadNameRegex(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("name_regex") is not { } pair)
        {
            return null; // unreachable: the caller checked authorship
        }

        if (pair.Value is not StringValueSyntax { Value: { } pattern })
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                "matcher match key 'name_regex' expects a string (§9.2).",
                pair.Value?.Span ?? pair.Span);
            return null;
        }

        if (!MatcherSelectors.TryCompileWholeName(pattern, out _, out var error))
        {
            context.Error(
                DiagnosticCode.SpecFieldInvalid,
                $"matcher match name_regex is not a usable pattern (§9.2): {error}.",
                pair.Value.Span);
            return null;
        }

        return pattern;
    }

    /// <summary>
    /// Reads and gates a <c>source_index_range</c> (§9.2/D-115): <b>exactly two</b>
    /// TOML integers satisfying <c>0 ≤ lo ≤ hi</c>. Wrong arity, a non-integer, a
    /// negative endpoint, and reversed endpoints are each <c>SpecFieldInvalid</c>.
    /// <para>
    /// An endpoint beyond the source width is <b>not</b> checked here and never is:
    /// over-coverage is legal (§9.2) — the Internet-Ads idiom writes a generous range
    /// — and it simply has no further attribute to match.
    /// </para>
    /// <para>
    /// Parsed element-wise rather than through <c>TakeLongArray</c> so one malformed
    /// range reports once: the shared helper reports each non-integer element and then
    /// returns a short list, which would report the arity a second time.
    /// </para>
    /// </summary>
    private static IReadOnlyList<long>? ReadSourceIndexRange(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.Take("source_index_range") is not { } pair)
        {
            return null; // unreachable: the caller checked authorship
        }

        if (pair.Value is not ArraySyntax array)
        {
            Invalid(context, pair.Value?.Span ?? pair.Span, "expects an array of exactly two integers [lo, hi]");
            return null;
        }

        var endpoints = new List<long>(2);
        foreach (var item in array.Items)
        {
            if (item.Value is not IntegerValueSyntax integer)
            {
                Invalid(context, item.Value?.Span ?? array.Span, "expects integer endpoints");
                return null;
            }

            endpoints.Add(integer.Value);
        }

        if (endpoints.Count != 2)
        {
            Invalid(context, array.Span, $"expects exactly two endpoints [lo, hi], but {endpoints.Count} were authored");
            return null;
        }

        if (endpoints[0] < 0)
        {
            Invalid(context, array.Span, $"has a negative lower endpoint {endpoints[0]}; source indexes are zero-based");
            return null;
        }

        if (endpoints[0] > endpoints[1])
        {
            Invalid(context, array.Span,
                $"has reversed endpoints (lo = {endpoints[0]}, hi = {endpoints[1]}); the range is inclusive and needs lo <= hi");
            return null;
        }

        return endpoints;
    }

    private static void Invalid(TomlReadContext context, SourceSpan span, string problem) =>
        context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"matcher match key 'source_index_range' {problem} (§9.2).",
            span);

    private static TripleColumnsSection? ReadTripleColumns(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeInlineTable("columns") is not { } table)
        {
            return null;
        }

        // Each role addresses a column by index or header name (§5.3). One-addressing-
        // mode / distinctness / partial-table are semantic checks owned by the resolver
        // (D-066/D-085); the reader only captures the authored refs.
        var inner = new TomlTableCursor(context, "[binding] columns", table);
        var section = new TripleColumnsSection(
            ReadTripleRole(context, inner, "subject"),
            ReadTripleRole(context, inner, "predicate"),
            ReadTripleRole(context, inner, "value"));
        inner.Finish();
        return section;
    }

    private static ColumnRef? ReadTripleRole(TomlReadContext context, TomlTableCursor cursor, string role) =>
        ReadColumnRef(context, cursor, role,
            $"[binding] columns.{role} expects a 0-based column index or a header name (§5.3).");

    private static ColumnRef? ReadColumnRef(
        TomlReadContext context, TomlTableCursor cursor, string key, string expected)
    {
        if (cursor.Take(key) is not { } pair)
        {
            return null;
        }

        switch (pair.Value)
        {
            case IntegerValueSyntax { Value: >= int.MinValue and <= int.MaxValue } index:
                return new IndexColumnRef((int)index.Value);

            case StringValueSyntax { Value: { } name }:
                return new NameColumnRef(name);

            default:
                context.Error(DiagnosticCode.SpecFieldInvalid, expected, pair.Value?.Span ?? pair.Span);
                return null;
        }
    }
}
