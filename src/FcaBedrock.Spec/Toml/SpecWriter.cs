using System.Text;
using FcaBedrock.Core.Discretization;
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
        foreach (var template in document.Templates)
        {
            WriteTemplate(builder, template);
        }

        foreach (var matcher in document.Matchers)
        {
            WriteMatcher(builder, matcher);
        }

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

        if (spec.Extends is { } extends)
        {
            builder.Key("extends", TomlLiteral.FormatString(extends));
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

            if (dat.TrailingNewline is { } trailingNewline)
            {
                builder.Key("trailing_newline", TomlLiteral.FormatBool(trailingNewline));
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

    private static void WriteTemplate(TomlBuilder builder, TemplateSection template)
    {
        // Mirrors WriteAttribute's key order minus the per-attribute identity
        // fields (name/source/description), with id leading (§9.1).
        builder.BeginSection("[[template]]");
        if (template.Id is { } id)
        {
            builder.Key("id", TomlLiteral.FormatString(id));
        }

        if (template.Include is { } include)
        {
            builder.Key("include", TomlLiteral.FormatBool(include));
        }

        if (template.DeclaredDomain is { } domain)
        {
            builder.Key(DeclaredDomainKey, FormatDeclaredDomain(domain));
        }

        if (template.RestrictTo is { } restrictTo)
        {
            builder.Key("restrict_to", FormatRestrictTo(restrictTo));
        }

        if (template.MissingPolicy is { } missing)
        {
            builder.Key("missing_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.MissingPolicies, missing)));
        }

        if (template.UnknownValuePolicy is { } unknown)
        {
            builder.Key("unknown_value_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.UnknownValuePolicies, unknown)));
        }

        if (template.ValueLabels is { } labels)
        {
            builder.Key("value_labels", FormatValueLabels(labels));
        }

        if (template.Discretizer is { } discretizer)
        {
            builder.Key("discretizer", FormatDiscretizer(discretizer));
        }

        if (template.Scale is { } scale)
        {
            builder.Key("scale", FormatScale(scale));
        }
    }

    private static void WriteMatcher(TomlBuilder builder, MatcherSection matcher)
    {
        builder.BeginSection("[[matcher]]");
        if (matcher.Match is { } match)
        {
            var items = new List<string>(2);
            if (match.NameRegex is { } nameRegex)
            {
                items.Add(Item("name_regex", TomlLiteral.FormatString(nameRegex)));
            }

            if (match.SourceIndexRange is { } range)
            {
                items.Add(Item("source_index_range", FormatLongArray(range)));
            }

            builder.Key("match", InlineTable(items));
        }

        if (matcher.Template is { } template)
        {
            builder.Key("template", TomlLiteral.FormatString(template));
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

        if (attribute.Template is { } templateRef)
        {
            builder.Key("template", TomlLiteral.FormatString(templateRef));
        }

        if (attribute.DeclaredDomain is { } domain)
        {
            builder.Key(DeclaredDomainKey, FormatDeclaredDomain(domain));
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

            case FreePerValueDiscretizerSection:
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.FreePerValueKind)));
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

            case EqualWidthDiscretizerSection equalWidth:
                // §11.4 presentation order: kind, bins, range, vmin, vmax, precision. Every
                // field is written only when authored (D-049 presence tracking), so an omitted
                // range/precision stays omitted and parse→write→parse is idempotent; vmin/vmax
                // are authored only under range = "manual" (the reader enforces it).
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.EqualWidthKind)));
                if (equalWidth.Bins is { } bins)
                {
                    items.Add(Item("bins", TomlLiteral.FormatLong(bins)));
                }

                if (equalWidth.Range is { } range)
                {
                    items.Add(Item("range", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.EqualWidthRanges, range))));
                }

                if (equalWidth.VMin is { } vmin)
                {
                    items.Add(Item("vmin", TomlLiteral.FormatDouble(vmin)));
                }

                if (equalWidth.VMax is { } vmax)
                {
                    items.Add(Item("vmax", TomlLiteral.FormatDouble(vmax)));
                }

                if (equalWidth.Precision is { } precision)
                {
                    items.Add(Item("precision", FormatPrecision(precision)));
                }

                break;

            case EqualFrequencyDiscretizerSection equalFrequency:
                // §11.5 presentation order: kind, bins, tie_policy, cut_placement. As everywhere
                // else, a field is written only when authored (D-049 presence tracking), so an
                // omitted tie_policy/cut_placement stays omitted and parse→write→parse is
                // idempotent; the resolved defaults are spelled where they are semantically
                // load-bearing — the §14 fingerprint (D-094) — not injected into the author's text.
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.EqualFrequencyKind)));
                if (equalFrequency.Bins is { } frequencyBins)
                {
                    items.Add(Item("bins", TomlLiteral.FormatLong(frequencyBins)));
                }

                if (equalFrequency.TiePolicy is { } tiePolicy)
                {
                    items.Add(Item("tie_policy", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.TiePolicies, tiePolicy))));
                }

                if (equalFrequency.CutPlacement is { } cutPlacement)
                {
                    items.Add(Item("cut_placement", TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.CutPlacements, cutPlacement))));
                }

                break;

            case ValueGroupsDiscretizerSection valueGroups:
                // §11.6 presentation order: kind, groups, unmatched. The groups array keeps
                // DECLARATION order and each group's values keep AUTHORED order with duplicates —
                // never canonical-sorted, because first-match order is semantic (§11.6) and the
                // values are authored config, not a set. As everywhere else a field is written only
                // when authored (D-049 presence tracking), so an omitted unmatched stays omitted —
                // injecting the "skip" default into the author's text would change the document,
                // and the §14 fingerprint (D-094) is where the resolved default is spelled.
                items.Add(Item("kind", TomlLiteral.FormatString(TomlSpellings.ValueGroupsKind)));
                if (valueGroups.Groups is { } groups)
                {
                    var formatted = new List<string>(groups.Count);
                    foreach (var group in groups)
                    {
                        formatted.Add(FormatValueGroup(group));
                    }

                    items.Add(Item("groups", Array(formatted)));
                }

                if (valueGroups.Unmatched is { } unmatched)
                {
                    items.Add(Item(
                        "unmatched",
                        TomlLiteral.FormatString(TomlSpellings.ToToml(TomlSpellings.ValueGroupsUnmatchedKinds, unmatched))));
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

    // §11.6: one group — kind-free inline table in presentation order label, values, pattern.
    // Presence, not emptiness, decides whether values/pattern are written: an omitted `values`
    // stays omitted and an authored `values = []` writes `values = []`, which is what keeps
    // parse→write→parse idempotent and the two states byte-distinct downstream (G-11/D-094).
    private static string FormatValueGroup(ValueGroupSection group)
    {
        var items = new List<string>(3);
        if (group.Label is { } label)
        {
            items.Add(Item("label", TomlLiteral.FormatString(label)));
        }

        if (group.Values is { } values)
        {
            items.Add(Item("values", FormatStringArray(values)));
        }

        if (group.Pattern is { } pattern)
        {
            items.Add(Item("pattern", TomlLiteral.FormatString(pattern)));
        }

        return InlineTable(items);
    }

    // §11.4: the two canonical precision forms — the bare string "exact", or the inline
    // table { round_to = <number> }.
    private static string FormatPrecision(CutPrecision precision) => precision switch
    {
        ExactPrecision => TomlLiteral.FormatString(TomlSpellings.PrecisionExact),
        RoundToPrecision roundTo => InlineTable([Item(TomlSpellings.RoundToKey, TomlLiteral.FormatDouble(roundTo.RoundTo))]),
        _ => throw new ArgumentOutOfRangeException(nameof(precision), precision, "Unknown cut precision type."),
    };

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

    // §10.4/D-091: the canonical presentation of each authored entry form. List order and
    // duplicates are preserved — they are authoring state, and the writer never sorts (D-075);
    // only the fingerprint projects a sorted, deduplicated view (§14).
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

                case RestrictToNumber number:
                    // The canonical invariant shortest form, via the same TomlLiteral encoder the
                    // range bounds and cut lists use — so 30, 30.0, and 3e1 all round-trip to
                    // { value = 30 }, and a resolved -0 writes as 0 (the seam canonicalized it,
                    // G-6). The canonical text must be re-readable: the reader's exact-entry shape
                    // accepts exactly this.
                    items.Add(InlineTable([Item("value", TomlLiteral.FormatDouble(number.Value))]));
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
            items.Add(Item("subject", FormatColumnRef(subject)));
        }

        if (columns.Predicate is { } predicate)
        {
            items.Add(Item("predicate", FormatColumnRef(predicate)));
        }

        if (columns.Value is { } value)
        {
            items.Add(Item("value", FormatColumnRef(value)));
        }

        return InlineTable(items);
    }

    private static string FormatColumnRef(ColumnRef column) => column switch
    {
        IndexColumnRef byIndex => TomlLiteral.FormatLong(byIndex.Index),
        NameColumnRef byName => TomlLiteral.FormatString(byName.Name),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Unknown column ref type."),
    };

    /// <summary>
    /// Renders a top-level <c>declared_domain</c> array (D-113): inline while the
    /// complete line fits <see cref="DeclaredDomainInlineLineLimit"/>, otherwise
    /// deterministically multiline — one escaped value per line at a two-space
    /// indent, a trailing comma on every value line, and an unindented closing
    /// bracket. A single over-long value wraps but is never split; an authored
    /// empty array stays inline as <c>[]</c>. No other array wraps: cut lists,
    /// <c>scale.order</c>, <c>value_groups</c>, <c>restrict_to</c>, and every
    /// nested or inline array keep <see cref="Array"/>'s inline rendering.
    /// Formatting only — semantics and fingerprints are untouched (§14).
    /// </summary>
    private static string FormatDeclaredDomain(IReadOnlyList<string> values)
    {
        var items = new List<string>(values.Count);
        foreach (var value in values)
        {
            items.Add(TomlLiteral.FormatString(value));
        }

        // Measured over the line the writer would actually emit — Item is the same
        // helper that renders `key = value`, so the cutoff cannot drift from the
        // rendering it governs. The transient inline string is a cold-path cost.
        var inline = Array(items);
        if (Item(DeclaredDomainKey, inline).Length <= DeclaredDomainInlineLineLimit)
        {
            return inline;
        }

        var wrapped = new StringBuilder("[\n");
        foreach (var item in items)
        {
            wrapped.Append("  ").Append(item).Append(",\n");
        }

        return wrapped.Append(']').ToString();
    }

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

    private static string FormatLongArray(IReadOnlyList<long> values)
    {
        var items = new List<string>(values.Count);
        foreach (var value in values)
        {
            items.Add(TomlLiteral.FormatLong(value));
        }

        return Array(items);
    }

    private const string DeclaredDomainKey = "declared_domain";

    /// <summary>
    /// The length, in UTF-16 code units, of the longest complete
    /// <c>declared_domain</c> line the writer emits inline (D-113) — key, spaces,
    /// equals sign, brackets, quotes, commas, separators, and escape sequences,
    /// excluding the terminating LF. A line of this length or shorter stays
    /// inline; a longer one wraps. A private canonical-writer formatting
    /// constant, byte-pinned by test: never a spec field, probe option, CLI/UI
    /// setting, or fingerprint input. UTF-16 code units (not display cells,
    /// graphemes, or UTF-8 bytes) keep the measurement machine-independent (P-7).
    /// </summary>
    private const int DeclaredDomainInlineLineLimit = 100;

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
