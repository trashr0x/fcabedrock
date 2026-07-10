using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Resolves a presence-tracked <see cref="SpecDocument"/> into a Core
/// <see cref="Core.Spec.BedrockSpec"/>, validating in the same pass and
/// aggregating all diagnostics (D-066/D-067). Defaults merge here (§5.1/§6,
/// D-060(c)) and by-name column bindings resolve to indices against the
/// supplied schema. Never throws for valid inputs under its contract — every
/// cannot-resolve state maps to a seam-owned diagnostic (D-067) — but the
/// contract takes a <em>composed or extends-free</em> document: one that still
/// carries an authored <c>[spec].extends</c> is invalid input (the caller
/// skipped <see cref="SpecComposer.Compose"/>, §13/D-078) and throws
/// <see cref="ArgumentException"/> rather than silently ignoring composition.
/// A triple document resolves fully — its predicate sources, role→index map,
/// ordering, and encoding become Core (D-082). Conversion runs for both orderings:
/// <c>ordering = "subject_grouped"</c> single-pass (M3, Slice C) and
/// <c>"unordered"</c> via the first-appearance grouping of interleaved input
/// (M3, Slice D). The plan is ordering-independent; ordering is honored at emit.
/// </summary>
public static class SpecResolver
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    /// <summary>
    /// Resolves <paramref name="document"/>; <paramref name="schema"/> is needed
    /// only when something binds a column by header name (§10.2/§5.4). When it
    /// is supplied, direct column indexes are also range-checked against it.
    /// </summary>
    public static Diagnosed<BedrockSpec> Resolve(SpecDocument document, SourceSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Spec?.Extends is { } extends)
        {
            // Call-contract violation, not document content: the document is
            // fine, the caller skipped composition (§13, D-078). Throwing here
            // guarantees extends is never silently ignored.
            throw new ArgumentException(
                $"The document declares extends = \"{extends}\" and must be composed before resolving; " +
                "apply SpecComposer.Compose first (§13, D-078).",
                nameof(document));
        }

        var diagnostics = new List<BedrockDiagnostic>();

        // §2/§3: unknown versions are refused outright — nothing below is
        // meaningful under unknown semantics.
        if (document.Spec?.Version is not { } version)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                "The document declares no [spec] version; a Bedrock spec must declare version = 1 (§2/§3)."));
            return Diagnosed<BedrockSpec>.Failed(diagnostics);
        }

        if (version != 1)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                $"Spec version {version} is not supported; this implementation supports version 1 (§2/§3)."));
            return Diagnosed<BedrockSpec>.Failed(diagnostics);
        }

        // §9/D-078: templates/matchers are carried and composed but not applied
        // before M6; a document that *uses* them must fail here — they never
        // resolve into Core, so a silent pass would drop schema-changing config.
        // Checked before the shape gate so they aggregate on shape-less and
        // triple documents too. Unreferenced [[template]] blocks are inert.
        if (document.Matchers.Count > 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.TemplateMatcherNotImplementedV1, DiagnosticSeverity.Error,
                $"The document declares {document.Matchers.Count} [[matcher]] entr{(document.Matchers.Count == 1 ? "y" : "ies")}; " +
                "matcher resolution lands at M6 (§9, D-078)."));
        }

        foreach (var attribute in document.Attributes)
        {
            if (attribute.Template is { } templateRef)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.TemplateMatcherNotImplementedV1, DiagnosticSeverity.Error,
                    $"The attribute references template = \"{templateRef}\"; template resolution lands at M6 (§9, D-078).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }
        }

        // §5.1: shape is the one binding field with no default; without it nothing
        // downstream is buildable.
        if (document.Binding?.Shape is not { } shape)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.BindingShapeMissing, DiagnosticSeverity.Error,
                document.Binding is null
                    ? "The document has no [binding] section (§5.1)."
                    : "[binding] declares no shape (§5.1)."));
            return Diagnosed<BedrockSpec>.Failed(diagnostics);
        }

        var bindingSection = document.Binding;
        ValidateBinding(bindingSection, diagnostics);
        // §5.1: has_header defaults are shape-specific — wide true, triple false
        // (triple data is typically headerless; a true default would eat row 1).
        var hasHeader = bindingSection.HasHeader ?? (shape == SourceShape.Wide);
        var locale = bindingSection.Locale ?? "invariant";
        var culture = ResolveCulture(locale, diagnostics);
        var encoding = ResolveEncoding(bindingSection.Encoding, diagnostics);

        // §5.3/§5.4 sequencing: the triple role→index map (and ordering) resolve
        // before the object key, because the triple object key is the resolved
        // subject column — which may be bound by header name (D-082).
        var tripleColumns = shape == SourceShape.Triple
            ? ResolveTripleColumns(bindingSection, hasHeader, schema, diagnostics)
            : null;
        var ordering = shape == SourceShape.Triple
            ? ResolveOrdering(bindingSection, diagnostics)
            : (TripleOrdering?)null;

        var binding = new Binding(
            shape,
            encoding,
            bindingSection.Delimiter ?? ',',
            bindingSection.QuoteChar ?? '"',
            hasHeader,
            locale,
            bindingSection.MissingToken ?? "?",
            ResolveObjectKey(bindingSection, shape, tripleColumns?.Subject ?? 0, document.Defaults, schema, diagnostics),
            tripleColumns,
            ordering);

        var attributes = new List<AttributeSpec>(document.Attributes.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in document.Attributes)
        {
            // §10.2 (D-080): duplicate authored names reject at the seam, over the
            // document model — a duplicate whose sibling field fails to resolve still
            // surfaces (ResolveAttribute would drop the broken one and hide the clash).
            // Empty names are owned by AttributeNameMissing, so they are skipped here;
            // one diagnostic per extra occurrence. Applies to both shapes.
            if (!string.IsNullOrEmpty(section.Name) && !seenNames.Add(section.Name))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.AttributeNameDuplicate, DiagnosticSeverity.Error,
                    $"Attribute name '{section.Name}' is declared more than once.",
                    new DiagnosticLocation(AttributeName: section.Name)));
            }

            if (ResolveAttribute(section, shape, document.Defaults, schema, hasHeader, culture, diagnostics) is { } attribute)
            {
                attributes.Add(attribute);
            }
        }

        return Finish(new BedrockSpec(binding, attributes), diagnostics);
    }

    // §5.1 (D-054/D-076): the quote check fires on the authored char only (the
    // default is the supported quote); the conflict check compares the resolved
    // pair. Distinct conditions — both report when both hold.
    private static void ValidateBinding(BindingSection binding, List<BedrockDiagnostic> diagnostics)
    {
        if (binding.QuoteChar is { } quote && quote != '"')
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.QuoteCharNotSupportedV1, DiagnosticSeverity.Error,
                $"binding.quote_char '{quote}' is not supported; v1 supports only the standard double quote '\"' (§5.1)."));
        }

        if ((binding.Delimiter ?? ',') == (binding.QuoteChar ?? '"'))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.BindingDelimiterQuoteConflict, DiagnosticSeverity.Error,
                "binding.delimiter equals binding.quote_char; the delimiter must differ from the quote character (§5.1)."));
        }
    }

    private static CultureInfo ResolveCulture(string locale, List<BedrockDiagnostic> diagnostics)
    {
        if (string.Equals(locale, "invariant", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            // predefinedOnly: under ICU, GetCultureInfo synthesizes a culture for
            // almost any well-formed tag, which would make locale acceptance
            // OS-dependent (P-7); only predefined cultures resolve.
            return CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.BindingLocaleInvalid, DiagnosticSeverity.Error,
                $"binding.locale '{locale}' is neither \"invariant\" nor a known culture name (§5.1)."));
            return CultureInfo.InvariantCulture; // placeholder; the Error fails the result
        }
    }

    private static ObjectKey ResolveObjectKey(
        BindingSection binding,
        SourceShape shape,
        int subjectColumn,
        DefaultsSection? defaults,
        SourceSchema? schema,
        List<BedrockDiagnostic> diagnostics)
    {
        var policy = defaults?.DuplicateObjectPolicy ?? DuplicateObjectPolicy.Fail;
        var section = binding.ObjectKey;

        // §5.4/D-082: triple object identity is always the resolved subject and is
        // not repointable, so ANY authored [binding.object_key] under triple is
        // rejected (not just row_index). The default is the subject column.
        if (shape == SourceShape.Triple)
        {
            if (section is not null)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyModeInvalidForShape, DiagnosticSeverity.Error,
                    "[binding.object_key] is not allowed under shape = \"triple\"; the object key is always the subject (§5.4)."));
            }

            // §6.1: duplicate_object_policy does not apply to triple (the subject is never a
            // duplicate-object condition). Carry the inert default so defaults.duplicate_object_policy
            // never reaches the triple key — otherwise it would perturb the output fingerprint
            // while triple emit ignores it, breaking "fingerprint = output bytes" (§14/D-077).
            return new ColumnObjectKey(subjectColumn, DuplicateObjectPolicy.Fail);
        }

        // §5.4 defaults: wide → row_index.
        if (section?.Mode is not { } mode)
        {
            return new RowIndexObjectKey();
        }

        return mode switch
        {
            ObjectKeyMode.RowIndex => new RowIndexObjectKey(),
            // Columns/aggregate stay document-only (D-064); the planner owns the
            // permanent composite reject (ObjectKeyCompositeNotImplementedV1, Fatal).
            ObjectKeyMode.Composite => new CompositeObjectKey(),
            _ => ResolveObjectKeyColumn(section.Column, schema, diagnostics) is { } index
                ? new ColumnObjectKey(index, policy)
                : new RowIndexObjectKey(), // placeholder; the Error fails the result
        };
    }

    private static int? ResolveObjectKeyColumn(
        ColumnRef? column, SourceSchema? schema, List<BedrockDiagnostic> diagnostics)
    {
        switch (column)
        {
            case IndexColumnRef { Index: < 0 } byIndex:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    $"object_key column index {byIndex.Index} is negative (§5.4)."));
                return null;

            // The resolve-time upper-bound check when a schema IS supplied. When resolve runs
            // schema-less (the conversion pipeline), the planner range-checks the resolved wide key
            // index against the schema it has instead (ObjectKeyBindingInvalid, D-083 interim).
            case IndexColumnRef byIndex when schema is not null && byIndex.Index >= schema.ColumnCount:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    $"object_key column index {byIndex.Index} is out of range for a source with {schema.ColumnCount} columns (§5.4)."));
                return null;

            case IndexColumnRef byIndex:
                return byIndex.Index;

            case NameColumnRef byName when schema?.Header is { } header:
                // §5.4/§10.2: the key column name must resolve to exactly one column.
                var index = ResolveUniqueHeader(header, byName.Name);
                if (index >= 0)
                {
                    return index;
                }

                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    index == -1
                        ? $"object_key column '{byName.Name}' is not in the source header (§5.4)."
                        : $"object_key column '{byName.Name}' matches multiple source header columns; it must resolve to exactly one (§5.4)."));
                return null;

            case NameColumnRef byName:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    $"object_key column '{byName.Name}' is bound by name but no header schema was supplied (§5.4)."));
                return null;

            default:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    "object_key mode \"column\" declares no column (§5.4)."));
                return null;
        }
    }

    // §5.1/D-082: v1 accepts UTF-8 only. Recognized spellings canonicalize to
    // "utf-8" so casing/spelling never perturbs the hash (UTF-8 specs keep their
    // bytes); any other encoding fails at resolve — no non-UTF-8 decoding in v1.
    private static string ResolveEncoding(string? authored, List<BedrockDiagnostic> diagnostics)
    {
        if (authored is null)
        {
            return "utf-8";
        }

        var normalized = authored.Trim();
        if (string.Equals(normalized, "utf-8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            return "utf-8";
        }

        AddBindingInvalid(diagnostics, $"binding.encoding '{authored}' is not supported; v1 accepts only UTF-8 (§5.1).");
        return "utf-8"; // placeholder; the Error fails the result
    }

    // §5.3/§5.4: resolve the triple role→index map. Omitted columns default to
    // (subject 0, predicate 1, value 2). One addressing mode across the three
    // roles; a partial table, mixed addressing, an unresolvable name, or
    // non-distinct roles are diagnosed (D-085). Placeholders keep resolution
    // going so sibling problems still surface; any Error fails the result.
    private static TripleColumns ResolveTripleColumns(
        BindingSection binding, bool hasHeader, SourceSchema? schema, List<BedrockDiagnostic> diagnostics)
    {
        var section = binding.Columns;
        if (section is null)
        {
            return new TripleColumns(0, 1, 2);
        }

        if (section.Subject is null || section.Predicate is null || section.Value is null)
        {
            AddBindingInvalid(diagnostics,
                "triple binding.columns must map all three roles (subject, predicate, value) or omit the table entirely (§5.3).");
            return new TripleColumns(0, 1, 2);
        }

        var names = (section.Subject is NameColumnRef ? 1 : 0)
            + (section.Predicate is NameColumnRef ? 1 : 0)
            + (section.Value is NameColumnRef ? 1 : 0);
        if (names is not (0 or 3))
        {
            AddBindingInvalid(diagnostics,
                "triple binding.columns must use one addressing mode — all indices or all names, not a mix (§5.3).");
            return new TripleColumns(0, 1, 2);
        }

        var subject = ResolveRole(section.Subject, "subject", hasHeader, schema, diagnostics);
        var predicate = ResolveRole(section.Predicate, "predicate", hasHeader, schema, diagnostics);
        var value = ResolveRole(section.Value, "value", hasHeader, schema, diagnostics);
        if (subject is null || predicate is null || value is null)
        {
            return new TripleColumns(subject ?? 0, predicate ?? 1, value ?? 2);
        }

        if (subject == predicate || subject == value || predicate == value)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.TripleColumnsNotDistinct, DiagnosticSeverity.Error,
                $"triple binding.columns roles must be distinct physical columns, but resolved to " +
                $"subject={subject}, predicate={predicate}, value={value} (§5.3)."));
        }

        return new TripleColumns(subject.Value, predicate.Value, value.Value);
    }

    private static int? ResolveRole(
        ColumnRef role, string name, bool hasHeader, SourceSchema? schema, List<BedrockDiagnostic> diagnostics)
    {
        switch (role)
        {
            case IndexColumnRef { Index: < 0 } byIndex:
                AddBindingInvalid(diagnostics, $"triple binding.columns.{name} index {byIndex.Index} is negative (§5.3).");
                return null;

            case IndexColumnRef byIndex when schema is not null && byIndex.Index >= schema.ColumnCount:
                AddBindingInvalid(diagnostics,
                    $"triple binding.columns.{name} index {byIndex.Index} is out of range for a source with {schema.ColumnCount} columns (§5.3).");
                return null;

            case IndexColumnRef byIndex:
                return byIndex.Index;

            case NameColumnRef byName when !hasHeader:
                AddBindingInvalid(diagnostics,
                    $"triple binding.columns.{name} binds by name '{byName.Name}' but the binding declares has_header = false (§5.3).");
                return null;

            case NameColumnRef byName when schema?.Header is { } header:
                // §5.3/§10.2: a name must resolve to exactly one column — no match
                // and a duplicate match are both invalid.
                var resolved = ResolveUniqueHeader(header, byName.Name);
                if (resolved >= 0)
                {
                    return resolved;
                }

                AddBindingInvalid(diagnostics, resolved == -1
                    ? $"triple binding.columns.{name} binds by name '{byName.Name}', which is not in the source header (§5.3)."
                    : $"triple binding.columns.{name} binds by name '{byName.Name}', which matches multiple header columns; it must resolve to exactly one (§5.3).");
                return null;

            case NameColumnRef byName:
                AddBindingInvalid(diagnostics,
                    $"triple binding.columns.{name} binds by name '{byName.Name}' but no header schema was supplied (§5.3).");
                return null;

            default:
                // ColumnRef is a closed Index/Name set; unreachable in practice.
                AddBindingInvalid(diagnostics, $"triple binding.columns.{name} has an unrecognized reference (§5.3).");
                return null;
        }
    }

    // §5.3: ordering is required for triple. Not a fingerprint input (D-082) — an
    // acceptance/streaming property; the document carries the Core enum directly.
    private static TripleOrdering ResolveOrdering(BindingSection binding, List<BedrockDiagnostic> diagnostics)
    {
        if (binding.Ordering is { } ordering)
        {
            return ordering;
        }

        AddBindingInvalid(diagnostics,
            "shape = \"triple\" requires binding.ordering (\"subject_grouped\" or \"unordered\") (§5.3).");
        return TripleOrdering.Unordered; // placeholder; the Error fails the result
    }

    private static AttributeSpec? ResolveAttribute(
        AttributeSection section,
        SourceShape shape,
        DefaultsSection? defaults,
        SourceSchema? schema,
        bool hasHeader,
        CultureInfo culture,
        List<BedrockDiagnostic> diagnostics)
    {
        var name = section.Name;
        if (string.IsNullOrEmpty(name))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.AttributeNameMissing, DiagnosticSeverity.Error,
                "An [[attribute]] has a missing or empty name (§10.1)."));
        }

        var label = string.IsNullOrEmpty(name) ? "<unnamed>" : name;
        var include = section.Include ?? defaults?.Include ?? true;
        var source = ResolveSource(section, label, shape, schema, hasHeader, diagnostics);

        Discretizer? discretizer = null;
        Scale? scale = null;
        if (include)
        {
            discretizer = ResolveDiscretizer(section.Discretizer, label, culture, diagnostics);
            scale = ResolveScale(section.Scale, label, defaults, diagnostics);
        }

        // else: parked with nulls, never an error (§10.9 / D-049) — the authored
        // config stays in the document model for round-trip.

        ValidateAttributeConstraints(section, label, include, defaults, diagnostics);

        if (string.IsNullOrEmpty(name) || source is null || (include && (discretizer is null || scale is null)))
        {
            return null; // the diagnostics above explain why; the Error fails the result
        }

        return new AttributeSpec(
            name,
            source,
            include,
            discretizer,
            scale,
            section.DeclaredDomain ?? [], // omitted and authored-[] both resolve absent; provenance stays in the document (D-049/D-071)
            section.RestrictTo ?? [],
            section.ValueLabels ?? NoLabels,
            section.MissingPolicy ?? defaults?.MissingPolicy ?? MissingPolicy.Skip,
            section.UnknownValuePolicy ?? defaults?.UnknownValuePolicy ?? UnknownValuePolicy.Warn);
    }

    private static SourceBinding? ResolveSource(
        AttributeSection section,
        string attribute,
        SourceShape shape,
        SourceSchema? schema,
        bool hasHeader,
        List<BedrockDiagnostic> diagnostics)
    {
        switch (section.Source)
        {
            // §10.2: source kind must match the binding shape.
            case ColumnSourceSection when shape == SourceShape.Triple:
                AddSourceInvalid(diagnostics, attribute, "has a column source, which requires a wide binding");
                return null;

            case ColumnSourceSection column:
                if (ResolveColumnIndex(column, attribute, schema, hasHeader, diagnostics) is not { } index)
                {
                    return null;
                }

                return new ColumnSource(index, ResolveValueType(column.ValueType, section.Discretizer));

            case PredicateSourceSection when shape != SourceShape.Triple:
                AddSourceInvalid(diagnostics, attribute, "has a predicate source, which requires a triple binding");
                return null;

            case PredicateSourceSection predicate:
                // The predicate is a data selector, not a header name — no schema
                // resolution; it only must be a non-empty string (§5.3/§10.2).
                if (predicate.Name is not { Length: > 0 } predicateName)
                {
                    AddSourceInvalid(diagnostics, attribute, "has a predicate source with no name");
                    return null;
                }

                return new PredicateSource(predicateName, ResolveValueType(predicate.ValueType, section.Discretizer));

            default:
                AddSourceInvalid(diagnostics, attribute, "declares no source");
                return null;
        }
    }

    private static int? ResolveColumnIndex(
        ColumnSourceSection column,
        string attribute,
        SourceSchema? schema,
        bool hasHeader,
        List<BedrockDiagnostic> diagnostics)
    {
        // §10.2: exactly one of index/name.
        if (column is { Index: { } index, Name: null })
        {
            if (index < 0)
            {
                AddSourceInvalid(diagnostics, attribute, $"declares negative source index {index}");
                return null;
            }

            // Without a schema the width is unknown; the planner's range check
            // remains the backstop for that window.
            if (schema is not null && index >= schema.ColumnCount)
            {
                AddSourceInvalid(diagnostics, attribute,
                    $"declares source index {index}, which is out of range for a source with {schema.ColumnCount} columns");
                return null;
            }

            return index;
        }

        string problem;
        if (column.Index is not null)
        {
            problem = "declares both a source index and a source name; exactly one is allowed";
        }
        else if (column.Name is not { } byName)
        {
            problem = "declares neither a source index nor a source name";
        }
        else if (!hasHeader)
        {
            problem = $"binds source name '{byName}' but the binding declares has_header = false";
        }
        else if (schema?.Header is not { } header)
        {
            problem = $"binds source name '{byName}' but no header schema was supplied";
        }
        else
        {
            // §10.2: a source name must resolve to exactly one column.
            var found = ResolveUniqueHeader(header, byName);
            if (found >= 0)
            {
                return found;
            }

            problem = found == -1
                ? $"binds source name '{byName}', which is not in the source header"
                : $"binds source name '{byName}', which matches multiple source header columns; it must resolve to exactly one";
        }

        AddSourceInvalid(diagnostics, attribute, problem);
        return null;
    }

    // Authored value_type wins; otherwise the discretizer kind decides —
    // manual_cuts is number-fixing, identity/ordered_cuts/none string (D-061).
    // Include-independent: value_type is a source-level property, so a parked
    // cut discretizer still types the source — and its live restrict_to (D-076).
    // Shape-agnostic: both column and predicate sources carry a value_type.
    private static SourceValueType ResolveValueType(SourceValueType? authored, DiscretizerSection? discretizer) =>
        authored
            ?? (discretizer is ManualCutsDiscretizerSection
                ? SourceValueType.Number
                : SourceValueType.String);

    // The authored value_type of any source kind (§10.2 — both column and predicate
    // sources carry one), or null when none is authored / no source.
    private static SourceValueType? AuthoredValueType(SourceSection? source) => source switch
    {
        ColumnSourceSection column => column.ValueType,
        PredicateSourceSection predicate => predicate.ValueType,
        _ => null,
    };

    // The Slice D static attribute checks (D-067). They read the document
    // sections directly — authored-vs-default provenance exists only there
    // (D-060) — and run whether or not the source/discretizer/scale resolved,
    // so one bad field does not mask another (P-14).
    private static void ValidateAttributeConstraints(
        AttributeSection section,
        string attribute,
        bool include,
        DefaultsSection? defaults,
        List<BedrockDiagnostic> diagnostics)
    {
        if (include)
        {
            ValidateValueType(section, attribute, diagnostics);
            ValidateOrdinalOverCuts(section, attribute, defaults, diagnostics);
            ValidateValueLabels(section, attribute, diagnostics);
            ValidateOrdinalOrderShape(section, attribute, diagnostics);
        }

        // restrict_to is live config even when the attribute is excluded (the
        // filter-only pattern, §10.1/§10.4) — its shape checks are
        // include-independent; only the parked-domain typo-catcher is gated (D-076).
        ValidateRestrictTo(section, attribute, include, diagnostics);
    }

    // §10.2 (D-061): a type-fixing discretizer disallows the other authored
    // value_type; only an authored type can conflict — the derived default is
    // the fixed type by construction. Parked (excluded) config never blocks
    // (D-049), and the flexible deferred kinds are read-rejected before this
    // seam (D-070). Source-kind agnostic: value_type is a source-level property of
    // both column and predicate sources (§10.2).
    private static void ValidateValueType(
        AttributeSection section, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        var authored = AuthoredValueType(section.Source);
        if (authored is not { } value)
        {
            return;
        }

        var problem = section.Discretizer switch
        {
            IdentityDiscretizerSection when value == SourceValueType.Number =>
                "declares value_type = \"number\", but identity is string-fixing — numeric distinct-value binning uses free_per_value",
            OrderedCutsDiscretizerSection when value == SourceValueType.Number =>
                "declares value_type = \"number\", but ordered_cuts is string-fixing (categories are used verbatim)",
            ManualCutsDiscretizerSection when value == SourceValueType.String =>
                "declares value_type = \"string\", but manual_cuts is number-fixing (cuts are numeric)",
            _ => null,
        };

        if (problem is not null)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SourceValueTypeInvalid, DiagnosticSeverity.Error,
                $"Attribute '{attribute}' {problem} (§10.2).",
                new DiagnosticLocation(AttributeName: attribute)));
        }
    }

    // §10.8 (D-080): value_labels keys must name a declared_domain value. Re-homed
    // from the planner to the seam, over the document model (D-067 phase ownership):
    // the code is §16.4 spec-validate, and reading the section directly catches a
    // stale key even when a sibling field fails to resolve (ResolveAttribute would
    // return null). In M2 `section.Discretizer is IdentityDiscretizerSection` is
    // exactly Discretizer.ConsultsValueLabels — free_per_value is the only other
    // consulting kind and it is read-rejected before this seam (D-070); it joins
    // this gate when it lands at M4. Under any other discretizer value_labels is
    // dormant (§10.8/D-049) — ignored here and in name rendering, never an error.
    // Include-gated by the caller, so a parked label list never blocks (D-049).
    private static void ValidateValueLabels(
        AttributeSection section, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (section.ValueLabels is not { Count: > 0 } labels
            || section.Discretizer is not IdentityDiscretizerSection)
        {
            return;
        }

        var domain = new HashSet<string>(section.DeclaredDomain ?? [], StringComparer.Ordinal);
        foreach (var key in labels.Keys)
        {
            if (!domain.Contains(key))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ValueLabelKeyNotInDomain, DiagnosticSeverity.Error,
                    $"value_labels key '{key}' on attribute '{attribute}' is not in its declared_domain.",
                    new DiagnosticLocation(AttributeName: attribute)));
            }
        }
    }

    // §10.4 (D-063): shape checks for the still-deferred restrict_to. Entry-type
    // checks run regardless of include — restrict_to filters even when the
    // attribute is excluded (filter-only), and a parked numeric-cut discretizer
    // legitimately types it ("a numeric source … or a numeric-cut discretizer",
    // §10.4/D-076). The domain typo-catcher fires only against a live domain:
    // included, string-typed identity with an explicit non-empty declared_domain
    // (declared_domain is parked when excluded, D-049; the numeric mismatch is
    // owned by RestrictToOnNumericRequiresRange). Source-kind agnostic: the
    // value-type shape checks apply to any source carrying a value_type (§10.2).
    private static void ValidateRestrictTo(
        AttributeSection section,
        string attribute,
        bool include,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.RestrictTo is not { Count: > 0 } entries || section.Source is not { } source)
        {
            return;
        }

        var valueType = ResolveValueType(AuthoredValueType(source), section.Discretizer);
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case RestrictToValue value when valueType == SourceValueType.Number:
                    diagnostics.Add(new BedrockDiagnostic(
                        DiagnosticCode.RestrictToOnNumericRequiresRange, DiagnosticSeverity.Error,
                        $"Attribute '{attribute}' is number-typed, but restrict_to entry \"{value.Value}\" is a bare string; numeric restriction uses range entries (§10.4).",
                        new DiagnosticLocation(AttributeName: attribute)));
                    break;

                case RestrictToRange when valueType == SourceValueType.String:
                    diagnostics.Add(new BedrockDiagnostic(
                        DiagnosticCode.SourceValueTypeInvalid, DiagnosticSeverity.Error,
                        $"Attribute '{attribute}' is string-typed, but restrict_to contains a numeric-range entry (§10.4/§10.2).",
                        new DiagnosticLocation(AttributeName: attribute)));
                    break;
            }
        }

        if (!include
            || valueType != SourceValueType.String
            || section.Discretizer is not IdentityDiscretizerSection
            || section.DeclaredDomain is not { Count: > 0 } domain)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry is RestrictToValue { Value: var value } && IndexOf(domain, value) < 0)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.RestrictToValueNotInDomain, DiagnosticSeverity.Warning,
                    $"restrict_to value \"{value}\" on attribute '{attribute}' is not in its declared_domain (§10.4).",
                    new DiagnosticLocation(AttributeName: attribute)));
            }
        }
    }

    // §12.3 (D-060): over cut bins the discretizer geometry is the single source
    // of order and operator. Both checks read the document sections — the
    // authored-vs-default boundary provenance exists only there (D-060(c)) —
    // and fire only on active attributes (parked scale config never blocks,
    // D-049). The deferred cut kinds (equal_width/equal_frequency) are
    // read-rejected before this seam (D-070).
    private static void ValidateOrdinalOverCuts(
        AttributeSection section,
        string attribute,
        DefaultsSection? defaults,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.Scale is not OrdinalScaleSection ordinal
            || section.Discretizer is not (ManualCutsDiscretizerSection or OrderedCutsDiscretizerSection))
        {
            return;
        }

        // Presence is the violation (§12.3 "MUST NOT be present") — an authored
        // empty order is still an order declaration over cut bins.
        if (ordinal.Order is not null)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrdinalOrderNotAllowedWithCuts, DiagnosticSeverity.Error,
                $"Attribute '{attribute}' declares scale.order over a cut discretizer; the cut geometry is the single source of bin order (§12.3).",
                new DiagnosticLocation(AttributeName: attribute)));
        }

        // Only a per-attribute authored boundary can straddle; an omitted or
        // [defaults]-inherited boundary is defaulted and never selects the
        // operator over cut bins (D-060(c)). The direction may itself be
        // defaulted — the geometry is judged on the resolved direction.
        var direction = ordinal.Direction ?? defaults?.OrdinalDirection ?? OrdinalDirection.Ge;
        var straddles = ordinal.Boundary is { } boundary
            && ((direction == OrdinalDirection.Le && boundary == OrdinalBoundary.Inclusive)
                || (direction == OrdinalDirection.Ge && boundary == OrdinalBoundary.Strict));
        if (straddles)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts, DiagnosticSeverity.Error,
                $"Attribute '{attribute}' authors an ordinal boundary that straddles the cut geometry ('le' pairs with strict '<', 'ge' with inclusive '>='); over cut bins the geometry fixes the operator (§12.3).",
                new DiagnosticLocation(AttributeName: attribute)));
        }
    }

    // §12.3 (D-081): over a non-cut discretizer an authored scale.order is the
    // value-bin ordering; its entries must be distinct and non-empty — the same
    // structural rule ordered_cuts.order already carries via OrderDomainInvalid,
    // broadened here to any authored order. The order-vs-domain permutation
    // (missing / unknown values) is a plan-phase check (OrdinalOrderMissing /
    // OrdinalOrderHasUnknownValue); this seam owns only the list's internal
    // validity. A cut discretizer's order is OrdinalOrderNotAllowedWithCuts
    // (ValidateOrdinalOverCuts), so those kinds are skipped here — no double
    // report. Include-gated by the caller, so a parked order never blocks (D-049),
    // exactly like the ordinal-over-cuts checks.
    private static void ValidateOrdinalOrderShape(
        AttributeSection section, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (section.Scale is not OrdinalScaleSection { Order: { } order }
            || section.Discretizer is ManualCutsDiscretizerSection or OrderedCutsDiscretizerSection)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in order)
        {
            if (value.Length == 0 || !seen.Add(value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrderDomainInvalid, DiagnosticSeverity.Error,
                    $"Attribute '{attribute}' scale.order entries must be distinct and non-empty (§12.3).",
                    new DiagnosticLocation(AttributeName: attribute)));
                return;
            }
        }
    }

    private static Discretizer? ResolveDiscretizer(
        DiscretizerSection? section,
        string attribute,
        CultureInfo culture,
        List<BedrockDiagnostic> diagnostics)
    {
        switch (section)
        {
            case IdentityDiscretizerSection:
                return new IdentityDiscretizer();

            case ManualCutsDiscretizerSection manual:
                // §11.2 defaults; the D-056 factory owns cut validation and its
                // diagnostics merge into this pass.
                return Merge(
                    ManualCutsDiscretizer.Create(manual.Cuts ?? [], manual.Ends ?? BinEnds.Open, culture),
                    attribute, diagnostics);

            case OrderedCutsDiscretizerSection ordered:
                return Merge(
                    OrderedCutsDiscretizer.Create(ordered.Order ?? [], ordered.Cuts ?? [], ordered.Ends ?? BinEnds.Open),
                    attribute, diagnostics);

            default:
                AddScalingMissing(diagnostics, attribute, "has no discretizer (§10.9)");
                return null;
        }
    }

    private static Scale? ResolveScale(
        ScaleSection? section,
        string attribute,
        DefaultsSection? defaults,
        List<BedrockDiagnostic> diagnostics)
    {
        switch (section)
        {
            case NominalScaleSection:
                return new NominalScale();

            case DichotomicScaleSection { TrueValue: { Length: > 0 } trueValue }:
                return new DichotomicScale(trueValue);

            case DichotomicScaleSection:
                AddScalingMissing(diagnostics, attribute, "has a dichotomic scale with no true_value (§12.2)");
                return null;

            case OrdinalScaleSection ordinal:
                // Omitted fields fill from [defaults] then the hard defaults (D-060(c)).
                return new OrdinalScale(
                    ordinal.Direction ?? defaults?.OrdinalDirection ?? OrdinalDirection.Ge,
                    ordinal.DropTop ?? false,
                    ordinal.Boundary ?? defaults?.OrdinalBoundary ?? OrdinalBoundary.Inclusive,
                    ordinal.Order);

            case DeferredScaleSection deferred:
                // §12.4 / D-010: resolves into the Core reject-carrier; the planner
                // owns the refusal (ScaleNotImplementedV1, Fatal) — not this seam.
                return new UnimplementedScale(deferred.Kind);

            default:
                AddScalingMissing(diagnostics, attribute, "has no scale (§10.9)");
                return null;
        }
    }

    // Merges a D-056 factory result into the resolve pass, attribute-scoping any
    // factory diagnostic that lacks a location.
    private static T? Merge<T>(Diagnosed<T> result, string attribute, List<BedrockDiagnostic> diagnostics)
        where T : class
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            diagnostics.Add(diagnostic.Location is null
                ? diagnostic with { Location = new DiagnosticLocation(AttributeName: attribute) }
                : diagnostic);
        }

        return result.Value;
    }

    private static void AddSourceInvalid(List<BedrockDiagnostic> diagnostics, string attribute, string problem) =>
        diagnostics.Add(new BedrockDiagnostic(
            DiagnosticCode.SourceBindingInvalid, DiagnosticSeverity.Error,
            $"Attribute '{attribute}' {problem} (§10.2).",
            new DiagnosticLocation(AttributeName: attribute)));

    // §10.2/§5.3 (D-085): SourceBindingInvalid also owns binding-level problems
    // (the triple columns table, ordering, encoding) that belong to no attribute;
    // the caller-supplied message names the binding concern and its § reference,
    // disambiguating it from an attribute source. No attribute Location.
    private static void AddBindingInvalid(List<BedrockDiagnostic> diagnostics, string message) =>
        diagnostics.Add(new BedrockDiagnostic(
            DiagnosticCode.SourceBindingInvalid, DiagnosticSeverity.Error, message));

    private static void AddScalingMissing(List<BedrockDiagnostic> diagnostics, string attribute, string problem) =>
        diagnostics.Add(new BedrockDiagnostic(
            DiagnosticCode.AttributeScalingMissing, DiagnosticSeverity.Error,
            $"Included attribute '{attribute}' {problem}.",
            new DiagnosticLocation(AttributeName: attribute)));

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // §10.2/§5.3: a header name must resolve to exactly one column. Returns the sole
    // index, -1 when no header matches, or -2 when several do — the -2 case is the
    // duplicate-matching-header reject shared by wide sources, wide column object
    // keys, and triple roles (ordinal compare, P-12).
    private static int ResolveUniqueHeader(IReadOnlyList<string> header, string name)
    {
        var matches = 0;
        var index = -1;
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.Ordinal))
            {
                matches++;
                if (index < 0)
                {
                    index = i;
                }
            }
        }

        return matches switch { 1 => index, 0 => -1, _ => -2 };
    }

    private static Diagnosed<BedrockSpec> Finish(BedrockSpec spec, List<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return Diagnosed<BedrockSpec>.Failed(diagnostics);
            }
        }

        return Diagnosed<BedrockSpec>.Ok(spec, diagnostics);
    }
}
