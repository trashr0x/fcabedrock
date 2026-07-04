using System.Text;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Writes a <see cref="SpecDocument"/> as canonical TOML (D-075): only authored
/// (non-null) sections and fields are emitted — presence tracking survives
/// verbatim (D-049/D-071), including an authored value that equals its default
/// and an authored empty <c>declared_domain = []</c> — in a fixed section and
/// key order (spec presentation order), with inline tables for the nested
/// groups, LF line endings, and invariant shortest number formatting
/// (<see cref="TomlLiteral"/>). The canonical form is a deliberate
/// normalization: the round-trip contract is document-model fidelity, not byte
/// fidelity of the authored file — §2 makes formatting informative and
/// fingerprints hash the plan, never TOML text (D-053).
/// </summary>
public static class SpecWriter
{
    /// <summary>Renders <paramref name="document"/> as canonical TOML text (LF, no BOM).</summary>
    public static string Write(SpecDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new TomlBuilder();
        WriteSpec(builder, document.Spec);
        WriteProvenance(builder, document.Provenance);
        WriteBinding(builder, document.Binding);
        WriteDefaults(builder, document.Defaults);
        WriteOutput(builder, document.Output);
        foreach (var attribute in document.Attributes)
        {
            WriteAttribute(builder, attribute);
        }

        return builder.ToString();
    }

    private static void WriteSpec(TomlBuilder builder, SpecSection? spec)
    {
        if (spec is null)
        {
            return;
        }

        builder.BeginSection("[spec]");
        if (spec.Version is { } version)
        {
            builder.Key("version", TomlLiteral.FormatLong(version));
        }

        if (spec.SchemaFingerprint is { } schema)
        {
            builder.Key("schema_fingerprint", TomlLiteral.FormatString(schema));
        }

        if (spec.CxtOutputFingerprint is { } cxt)
        {
            builder.Key("cxt_output_fingerprint", TomlLiteral.FormatString(cxt));
        }

        if (spec.DatOutputFingerprint is { } dat)
        {
            builder.Key("dat_output_fingerprint", TomlLiteral.FormatString(dat));
        }

        if (spec.Description is { } description)
        {
            builder.Key("description", TomlLiteral.FormatString(description));
        }
    }

    private static void WriteProvenance(TomlBuilder builder, ProvenanceSection? provenance)
    {
        if (provenance is null)
        {
            return;
        }

        builder.BeginSection("[provenance]");
        if (provenance.Author is { } author)
        {
            builder.Key("author", TomlLiteral.FormatString(author));
        }

        if (provenance.CreatedAt is { } createdAt)
        {
            builder.Key("created_at", TomlLiteral.FormatDateTime(createdAt));
        }

        if (provenance.SourceUrl is { } url)
        {
            builder.Key("source_url", TomlLiteral.FormatString(url));
        }

        if (provenance.SourceHash is { } hash)
        {
            builder.Key("source_hash", TomlLiteral.FormatString(hash));
        }

        if (provenance.DerivedFrom is { } derivedFrom)
        {
            builder.Key("derived_from", TomlLiteral.FormatString(derivedFrom));
        }

        if (provenance.Notes is { } notes)
        {
            builder.Key("notes", TomlLiteral.FormatString(notes));
        }
    }

    private static void WriteBinding(TomlBuilder builder, BindingSection? binding)
    {
        if (binding is null)
        {
            return;
        }

        builder.BeginSection("[binding]");
        if (binding.Shape is { } shape)
        {
            builder.Key("shape", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Shapes, shape)));
        }

        if (binding.Encoding is { } encoding)
        {
            builder.Key("encoding", TomlLiteral.FormatString(encoding));
        }

        if (binding.Delimiter is { } delimiter)
        {
            builder.Key("delimiter", TomlLiteral.FormatChar(delimiter));
        }

        if (binding.QuoteChar is { } quoteChar)
        {
            builder.Key("quote_char", TomlLiteral.FormatChar(quoteChar));
        }

        if (binding.HasHeader is { } hasHeader)
        {
            builder.Key("has_header", TomlLiteral.FormatBool(hasHeader));
        }

        if (binding.Locale is { } locale)
        {
            builder.Key("locale", TomlLiteral.FormatString(locale));
        }

        if (binding.MissingToken is { } missingToken)
        {
            builder.Key("missing_token", TomlLiteral.FormatString(missingToken));
        }

        if (binding.Ordering is { } ordering)
        {
            builder.Key("ordering", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Orderings, ordering)));
        }

        if (binding.Columns is { } columns)
        {
            builder.Key("columns", FormatTripleColumns(columns));
        }

        WriteObjectKey(builder, binding.ObjectKey);
    }

    private static void WriteObjectKey(TomlBuilder builder, ObjectKeySection? objectKey)
    {
        if (objectKey is null)
        {
            return;
        }

        builder.BeginSection("[binding.object_key]");
        if (objectKey.Mode is { } mode)
        {
            builder.Key("mode", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.ObjectKeyModes, mode)));
        }

        if (objectKey.Column is { } column)
        {
            builder.Key("column", FormatColumnRef(column));
        }

        if (objectKey.Columns is { } columns)
        {
            builder.Key("columns", FormatStringArray(columns));
        }

        if (objectKey.Aggregate is { } aggregate)
        {
            builder.Key("aggregate", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Aggregates, aggregate)));
        }
    }

    private static void WriteDefaults(TomlBuilder builder, DefaultsSection? defaults)
    {
        if (defaults is null)
        {
            return;
        }

        builder.BeginSection("[defaults]");
        if (defaults.Include is { } include)
        {
            builder.Key("include", TomlLiteral.FormatBool(include));
        }

        if (defaults.MissingPolicy is { } missing)
        {
            builder.Key("missing_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.MissingPolicies, missing)));
        }

        if (defaults.UnknownValuePolicy is { } unknown)
        {
            builder.Key("unknown_value_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.UnknownValuePolicies, unknown)));
        }

        if (defaults.DuplicateObjectPolicy is { } duplicate)
        {
            builder.Key("duplicate_object_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.DuplicateObjectPolicies, duplicate)));
        }

        if (defaults.OrdinalDirection is { } direction)
        {
            builder.Key("ordinal_direction", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Directions, direction)));
        }

        if (defaults.OrdinalBoundary is { } boundary)
        {
            builder.Key("ordinal_boundary", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Boundaries, boundary)));
        }
    }

    private static void WriteOutput(TomlBuilder builder, OutputSection? output)
    {
        if (output is null)
        {
            return;
        }

        builder.BeginSection("[output]");
        if (output.BinLabelUnicode is { } unicode)
        {
            builder.Key("bin_label_unicode", TomlLiteral.FormatBool(unicode));
        }

        if (output.Cxt is { } cxt)
        {
            builder.BeginSection("[output.cxt]");
            if (cxt.LineEndings is { } lineEndings)
            {
                builder.Key("line_endings", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.LineEndingKinds, lineEndings)));
            }

            if (cxt.TrailingNewline is { } trailingNewline)
            {
                builder.Key("trailing_newline", TomlLiteral.FormatBool(trailingNewline));
            }

            if (cxt.SizeAdvisoryBytes is { } sizeAdvisory)
            {
                builder.Key("size_advisory_bytes", TomlLiteral.FormatLong(sizeAdvisory));
            }
        }

        if (output.Dat is { } dat)
        {
            builder.BeginSection("[output.dat]");
            if (dat.LineEndings is { } lineEndings)
            {
                builder.Key("line_endings", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.LineEndingKinds, lineEndings)));
            }

            if (dat.BaseIndex is { } baseIndex)
            {
                builder.Key("base_index", TomlLiteral.FormatLong(baseIndex));
            }

            if (dat.NonemptyLineTrailingSpace is { } nonempty)
            {
                builder.Key("nonempty_line_trailing_space", TomlLiteral.FormatBool(nonempty));
            }

            if (dat.EmptyLineTrailingSpace is { } empty)
            {
                builder.Key("empty_line_trailing_space", TomlLiteral.FormatBool(empty));
            }
        }
    }

    private static void WriteAttribute(TomlBuilder builder, AttributeSection attribute)
    {
        builder.BeginSection("[[attribute]]");
        if (attribute.Name is { } name)
        {
            builder.Key("name", TomlLiteral.FormatString(name));
        }

        if (attribute.Source is { } source)
        {
            builder.Key("source", FormatSource(source));
        }

        if (attribute.Description is { } description)
        {
            builder.Key("description", TomlLiteral.FormatString(description));
        }

        if (attribute.Include is { } include)
        {
            builder.Key("include", TomlLiteral.FormatBool(include));
        }

        if (attribute.DeclaredDomain is { } domain)
        {
            builder.Key("declared_domain", FormatStringArray(domain));
        }

        if (attribute.RestrictTo is { } restrictTo)
        {
            builder.Key("restrict_to", FormatRestrictTo(restrictTo));
        }

        if (attribute.MissingPolicy is { } missing)
        {
            builder.Key("missing_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.MissingPolicies, missing)));
        }

        if (attribute.UnknownValuePolicy is { } unknown)
        {
            builder.Key("unknown_value_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.UnknownValuePolicies, unknown)));
        }

        if (attribute.ValueLabels is { } labels)
        {
            builder.Key("value_labels", FormatValueLabels(labels));
        }

        if (attribute.Discretizer is { } discretizer)
        {
            builder.Key("discretizer", FormatDiscretizer(discretizer));
        }

        if (attribute.Scale is { } scale)
        {
            builder.Key("scale", FormatScale(scale));
        }
    }

    private static string FormatSource(SourceSection source)
    {
        var items = new List<string>(4);
        switch (source)
        {
            case ColumnSourceSection column:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.ColumnSourceKind)));
                if (column.Index is { } index)
                {
                    items.Add(Item("index", TomlLiteral.FormatLong(index)));
                }

                // An authored index *and* name is an invalid state the document
                // must keep representable (D-066); both are written faithfully.
                if (column.Name is { } name)
                {
                    items.Add(Item("name", TomlLiteral.FormatString(name)));
                }

                AddValueType(items, column.ValueType);
                break;

            case PredicateSourceSection predicate:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.PredicateSourceKind)));
                if (predicate.Name is { } predicateName)
                {
                    items.Add(Item("name", TomlLiteral.FormatString(predicateName)));
                }

                AddValueType(items, predicate.ValueType);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown source section type.");
        }

        return InlineTable(items);
    }

    private static void AddValueType(List<string> items, SourceValueType? valueType)
    {
        if (valueType is { } authored)
        {
            items.Add(Item("value_type", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.ValueTypes, authored))));
        }
    }

    private static string FormatDiscretizer(DiscretizerSection discretizer)
    {
        var items = new List<string>(4);
        switch (discretizer)
        {
            case IdentityDiscretizerSection:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.IdentityKind)));
                break;

            case ManualCutsDiscretizerSection manual:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.ManualCutsKind)));
                if (manual.Cuts is { } cuts)
                {
                    items.Add(Item("cuts", FormatDoubleArray(cuts)));
                }

                if (manual.Ends is { } ends)
                {
                    items.Add(Item("ends", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Ends, ends))));
                }

                break;

            case OrderedCutsDiscretizerSection ordered:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.OrderedCutsKind)));
                if (ordered.Order is { } order)
                {
                    items.Add(Item("order", FormatStringArray(order)));
                }

                if (ordered.Cuts is { } orderedCuts)
                {
                    items.Add(Item("cuts", FormatStringArray(orderedCuts)));
                }

                if (ordered.Ends is { } orderedEnds)
                {
                    items.Add(Item("ends", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Ends, orderedEnds))));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(discretizer), discretizer, "Unknown discretizer section type.");
        }

        return InlineTable(items);
    }

    private static string FormatScale(ScaleSection scale)
    {
        var items = new List<string>(5);
        switch (scale)
        {
            case NominalScaleSection:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.NominalKind)));
                break;

            case DichotomicScaleSection dichotomic:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.DichotomicKind)));
                if (dichotomic.TrueValue is { } trueValue)
                {
                    items.Add(Item("true_value", TomlLiteral.FormatString(trueValue)));
                }

                break;

            case OrdinalScaleSection ordinal:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.OrdinalKind)));
                if (ordinal.Direction is { } direction)
                {
                    items.Add(Item("direction", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Directions, direction))));
                }

                if (ordinal.Boundary is { } boundary)
                {
                    items.Add(Item("boundary", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.Boundaries, boundary))));
                }

                if (ordinal.Order is { } order)
                {
                    items.Add(Item("order", FormatStringArray(order)));
                }

                if (ordinal.DropTop is { } dropTop)
                {
                    items.Add(Item("drop_top", TomlLiteral.FormatBool(dropTop)));
                }

                break;

            case DeferredScaleSection deferred:
                items.Add(Item("kind", TomlLiteral.FormatString(deferred.Kind)));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown scale section type.");
        }

        return InlineTable(items);
    }

    private static string FormatRestrictTo(IReadOnlyList<RestrictToEntry> entries)
    {
        var items = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case RestrictToValue value:
                    items.Add(TomlLiteral.FormatString(value.Value));
                    break;

                case RestrictToRange range:
                    var bounds = new List<string>(2);
                    if (range.From is { } from)
                    {
                        bounds.Add(Item("from", TomlLiteral.FormatDouble(from)));
                    }

                    if (range.To is { } to)
                    {
                        bounds.Add(Item("to", TomlLiteral.FormatDouble(to)));
                    }

                    items.Add(InlineTable(bounds));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(entries), entry, "Unknown restrict_to entry type.");
            }
        }

        return Array(items);
    }

    private static string FormatValueLabels(IReadOnlyDictionary<string, string> labels)
    {
        // Enumeration order is the document's stored (authored) order; the writer
        // never sorts (D-075) — label order is semantically inert (§10.8).
        var items = new List<string>(labels.Count);
        foreach (var (key, value) in labels)
        {
            items.Add(Item(TomlLiteral.FormatKey(key), TomlLiteral.FormatString(value)));
        }

        return InlineTable(items);
    }

    private static string FormatTripleColumns(TripleColumnsSection columns)
    {
        var items = new List<string>(3);
        if (columns.Subject is { } subject)
        {
            items.Add(Item("subject", TomlLiteral.FormatLong(subject)));
        }

        if (columns.Predicate is { } predicate)
        {
            items.Add(Item("predicate", TomlLiteral.FormatLong(predicate)));
        }

        if (columns.Value is { } value)
        {
            items.Add(Item("value", TomlLiteral.FormatLong(value)));
        }

        return InlineTable(items);
    }

    private static string FormatColumnRef(ColumnRef column) => column switch
    {
        IndexColumnRef byIndex => TomlLiteral.FormatLong(byIndex.Index),
        NameColumnRef byName => TomlLiteral.FormatString(byName.Name),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown column ref type."),
    };

    private static string FormatStringArray(IReadOnlyList<string> values)
    {
        var items = new List<string>(values.Count);
        foreach (var value in values)
        {
            items.Add(TomlLiteral.FormatString(value));
        }

        return Array(items);
    }

    private static string FormatDoubleArray(IReadOnlyList<double> values)
    {
        var items = new List<string>(values.Count);
        foreach (var value in values)
        {
            items.Add(TomlLiteral.FormatDouble(value));
        }

        return Array(items);
    }

    private static string Item(string key, string value) => $"{key} = {value}";

    private static string Array(List<string> items) => items.Count == 0 ? "[]" : $"[{string.Join(", ", items)}]";

    private static string InlineTable(List<string> items) => items.Count == 0 ? "{}" : $"{{ {string.Join(", ", items)} }}";

    /// <summary>
    /// Accumulates canonical TOML: sections separated by one blank line, LF only.
    /// </summary>
    private sealed class TomlBuilder
    {
        private readonly StringBuilder _text = new();

        public void BeginSection(string header)
        {
            if (_text.Length > 0)
            {
                _text.Append('\n');
            }

            _text.Append(header).Append('\n');
        }

        public void Key(string key, string value) =>
            _text.Append(key).Append(" = ").Append(value).Append('\n');

        public override string ToString() => _text.ToString();
    }
}
