using FcaBedrock.Diagnostics;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Readers for the non-attribute sections: each takes its known keys through a
/// <see cref="TomlTableCursor"/> and finishes with the section's closed
/// deferred-surface set (D-075). Possibly-invalid values are document territory
/// (D-066) — nothing here validates semantics.
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

    public static MatcherSection ReadMatcher(TomlReadContext context, TableSyntaxBase table)
    {
        var cursor = new TomlTableCursor(context, "[[matcher]]", table);
        var section = new MatcherSection(
            ReadMatch(context, cursor),
            cursor.TakeString("template"));
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
            cursor.TakeEnum("ordinal_boundary", TomlSpellings.Boundaries));
        cursor.Finish(TomlSpellings.DefaultsDeferredKeys);
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
            cursor.TakeBool("empty_line_trailing_space"));
        cursor.Finish();
        return section;
    }

    private static MatchSection? ReadMatch(TomlReadContext context, TomlTableCursor cursor)
    {
        if (cursor.TakeInlineTable("match") is not { } table)
        {
            return null;
        }

        // Pattern semantics (arity, regex syntax) are M6 territory — the match
        // is carried verbatim at authored shape (D-078).
        var inner = new TomlTableCursor(context, "matcher match", table);
        var section = new MatchSection(
            inner.TakeString("name_regex"),
            inner.TakeLongArray("source_index_range"));
        inner.Finish();
        return section;
    }

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
