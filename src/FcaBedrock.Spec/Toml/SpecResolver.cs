using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
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
    /// Resolves <paramref name="document"/> into a paired
    /// <see cref="ResolvedDocument"/> (D-098/G-1): the resolved
    /// <see cref="ResolvedSpec"/> token plus an immutable snapshot of the document.
    /// <paramref name="schema"/> is needed only when something binds a column by
    /// header name (§10.2/§5.4); when it is supplied, direct column indexes are also
    /// range-checked against it — the conversion pipeline resolves schema-aware via
    /// the two-stage source bootstrap, so all binding range checks are seam-owned
    /// (G-1). Strict factories run only behind the success gate: on any Error/Fatal
    /// the result is <see cref="Diagnosed{T}.Failed"/> and no strict factory is
    /// called (round-7 High-1).
    /// </summary>
    public static Diagnosed<ResolvedDocument> Resolve(SpecDocument document, SourceSchema? schema = null)
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
        var nameBindings = new List<ResolvedNameBinding>();

        // §2/§3: unknown versions are refused outright — nothing below is
        // meaningful under unknown semantics.
        if (document.Spec?.Version is not { } version)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                "The document declares no [spec] version; a Bedrock spec must declare version = 1 (§2/§3)."));
            return Diagnosed<ResolvedDocument>.Failed(diagnostics);
        }

        if (version != 1)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                $"Spec version {version} is not supported; this implementation supports version 1 (§2/§3)."));
            return Diagnosed<ResolvedDocument>.Failed(diagnostics);
        }

        // §9/D-078: templates/matchers are carried and composed but not applied
        // before M6 Slice B; a document that *uses* them must fail here — they never
        // resolve into Core, so a silent pass would drop schema-changing config.
        // Checked before the shape gate so they aggregate on shape-less and
        // triple documents too. Unreferenced [[template]] blocks are inert (their
        // naming keys are parse-validated from M6 Slice A but stay inert, D-120).
        if (document.Matchers.Count > 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.TemplateMatcherNotImplementedV1, DiagnosticSeverity.Error,
                $"The document declares {document.Matchers.Count} [[matcher]] entr{(document.Matchers.Count == 1 ? "y" : "ies")}; " +
                "matcher resolution lands at M6 Slice B (§9, D-078)."));
        }

        foreach (var attribute in document.Attributes)
        {
            if (attribute.Template is { } templateRef)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.TemplateMatcherNotImplementedV1, DiagnosticSeverity.Error,
                    $"The attribute references template = \"{templateRef}\"; template resolution lands at M6 Slice B (§9, D-078).",
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
            return Diagnosed<ResolvedDocument>.Failed(diagnostics);
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
            ? ResolveTripleColumns(bindingSection, hasHeader, schema, nameBindings, diagnostics)
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
            ResolveObjectKey(bindingSection, shape, tripleColumns?.Subject ?? 0, document.Defaults, schema, nameBindings, diagnostics),
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

            if (ResolveAttribute(section, shape, document.Defaults, schema, hasHeader, culture, nameBindings, diagnostics) is { } attribute)
            {
                attributes.Add(attribute);
            }
        }

        return Finish(new BedrockSpec(binding, attributes), document, schema, nameBindings, diagnostics);
    }

    /// <summary>
    /// Stage-1 bootstrap resolution (D-098/G-1): resolves only the §5.1
    /// schema-independent read settings a source session needs before the schema is
    /// known, via the same private helpers as full resolution (so no condition gains
    /// a second owner). Enforces the same prefix gates as <see cref="Resolve"/> — an
    /// authored <c>extends</c> throws <see cref="ArgumentException"/> (uncomposed), and
    /// a missing/unsupported version returns <c>SpecVersionUnsupported</c> (Fatal) with
    /// no settings — so the bootstrap never opens a source for a document whose
    /// semantics are unknown. Strict factory (<see cref="SourceReadSettings.Create"/>)
    /// runs only behind the success gate.
    /// </summary>
    public static Diagnosed<SourceReadSettings> ResolveReadSettings(SpecDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Spec?.Extends is { } extends)
        {
            throw new ArgumentException(
                $"The document declares extends = \"{extends}\" and must be composed before resolving; " +
                "apply SpecComposer.Compose first (§13, D-078).",
                nameof(document));
        }

        var diagnostics = new List<BedrockDiagnostic>();

        if (document.Spec?.Version is not { } version)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                "The document declares no [spec] version; a Bedrock spec must declare version = 1 (§2/§3)."));
            return Diagnosed<SourceReadSettings>.Failed(diagnostics);
        }

        if (version != 1)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.SpecVersionUnsupported, DiagnosticSeverity.Fatal,
                $"Spec version {version} is not supported; this implementation supports version 1 (§2/§3)."));
            return Diagnosed<SourceReadSettings>.Failed(diagnostics);
        }

        if (document.Binding?.Shape is not { } shape)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.BindingShapeMissing, DiagnosticSeverity.Error,
                document.Binding is null
                    ? "The document has no [binding] section (§5.1)."
                    : "[binding] declares no shape (§5.1)."));
            return Diagnosed<SourceReadSettings>.Failed(diagnostics);
        }

        var bindingSection = document.Binding;
        ValidateBinding(bindingSection, diagnostics);
        var hasHeader = bindingSection.HasHeader ?? (shape == SourceShape.Wide);
        var encoding = ResolveEncoding(bindingSection.Encoding, diagnostics);
        var ordering = shape == SourceShape.Triple
            ? ResolveOrdering(bindingSection, diagnostics)
            : (TripleOrdering?)null;

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return Diagnosed<SourceReadSettings>.Failed(diagnostics);
            }
        }

        var settings = SourceReadSettings.Create(
            shape,
            encoding,
            bindingSection.Delimiter ?? ',',
            bindingSection.QuoteChar ?? '"',
            hasHeader,
            bindingSection.MissingToken ?? "?",
            ordering);
        return Diagnosed<SourceReadSettings>.Ok(settings, diagnostics);
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
        List<ResolvedNameBinding> nameBindings,
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
            _ => ResolveObjectKeyColumn(section.Column, schema, nameBindings, diagnostics) is { } index
                ? new ColumnObjectKey(index, policy)
                : new RowIndexObjectKey(), // placeholder; the Error fails the result
        };
    }

    private static int? ResolveObjectKeyColumn(
        ColumnRef? column, SourceSchema? schema, List<ResolvedNameBinding> nameBindings, List<BedrockDiagnostic> diagnostics)
    {
        switch (column)
        {
            case IndexColumnRef { Index: < 0 } byIndex:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    $"object_key column index {byIndex.Index} is negative (§5.4)."));
                return null;

            // The upper-bound check is seam-owned (G-1/D-098): the conversion pipeline now
            // resolves schema-aware via the two-stage bootstrap, so this is the single home
            // for the wide key-index range check. A schema-less resolve (spec tooling) leaves
            // the upper bound unchecked; ResolvedSpec.Create is the trust-boundary backstop.
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
                    nameBindings.Add(new ObjectKeyNameBinding(byName.Name, index));
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
        BindingSection binding, bool hasHeader, SourceSchema? schema,
        List<ResolvedNameBinding> nameBindings, List<BedrockDiagnostic> diagnostics)
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

        var subject = ResolveRole(section.Subject, TripleRole.Subject, "subject", hasHeader, schema, nameBindings, diagnostics);
        var predicate = ResolveRole(section.Predicate, TripleRole.Predicate, "predicate", hasHeader, schema, nameBindings, diagnostics);
        var value = ResolveRole(section.Value, TripleRole.Value, "value", hasHeader, schema, nameBindings, diagnostics);
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
        ColumnRef role, TripleRole roleKind, string name, bool hasHeader, SourceSchema? schema,
        List<ResolvedNameBinding> nameBindings, List<BedrockDiagnostic> diagnostics)
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
                    nameBindings.Add(new TripleRoleNameBinding(roleKind, byName.Name, resolved));
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
        List<ResolvedNameBinding> nameBindings,
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
        var source = ResolveSource(section, label, name, shape, schema, hasHeader, nameBindings, diagnostics);

        // §10.2/D-061: the resolved source value type (authored, else discretizer-implied),
        // computed once here and shared with discretizer construction and the D-096 numeric
        // free_per_value normalization — ResolveSource derives it identically for the source.
        var valueType = ResolveValueType(AuthoredValueType(section.Source), section.Discretizer);
        var numericFreePerValue = section.Discretizer is FreePerValueDiscretizerSection
            && valueType == SourceValueType.Number;

        Discretizer? discretizer = null;
        Scale? scale = null;
        IReadOnlyList<string> declaredDomain = section.DeclaredDomain ?? []; // omitted and authored-[] both resolve absent (D-049/D-071)
        IReadOnlyDictionary<string, string> valueLabels = section.ValueLabels ?? NoLabels;
        if (include)
        {
            discretizer = ResolveDiscretizer(section.Discretizer, label, culture, valueType, diagnostics);

            // §10.3/§10.8/D-096: a numeric free_per_value's declared_domain and value_labels keys
            // are the §5.1 exception to verbatim strings — parsed under binding.locale to their
            // canonical numeric identity, with the invalid/duplicate cases diagnosed here. The
            // resolved Core graph carries canonical keys; the document keeps the authored spellings
            // for round-trip (D-096). scale.order is normalized inside ResolveScale.
            if (numericFreePerValue)
            {
                declaredDomain = NormalizeNumericDomain(section.DeclaredDomain, culture, label, diagnostics);
                valueLabels = NormalizeNumericValueLabels(section.ValueLabels, declaredDomain, culture, label, diagnostics);
            }

            scale = ResolveScale(section.Scale, label, defaults, numericFreePerValue ? culture : null, diagnostics);
        }

        // else: parked with nulls, never an error (§10.9 / D-049) — the authored
        // config stays in the document model for round-trip.

        ValidateAttributeConstraints(section, label, include, valueType, defaults, diagnostics);

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
            declaredDomain,
            NormalizeRestrictTo(section.RestrictTo),
            valueLabels,
            section.MissingPolicy ?? defaults?.MissingPolicy ?? MissingPolicy.Skip,
            section.UnknownValuePolicy ?? defaults?.UnknownValuePolicy ?? UnknownValuePolicy.Warn)
        {
            // §10.1/§10.7: the two naming inputs Core consumes. Explicit attribute field,
            // else [defaults] for the format (§9.2 tiers 5 and 2); the template tiers are
            // inert until application lands. An absent display_name defaults to the name,
            // and an absent format leaves the scale-specific defaults in charge — which is
            // what every pre-M6 spec resolves to, byte-for-byte unchanged.
            DisplayName = section.DisplayName ?? name,
            NameFormat = ParseEffectiveFormat(section.FormalAttributeFormat ?? defaults?.FormalAttributeFormat),
        };
    }

    // The effective format, reparsed for Core. Every authored format was validated at
    // parse against this same grammar owner (AttributeReader.ReadNameFormat), so a
    // failure here means the document did not come through the reader — a programmer
    // error on a hand-built document, not authored input, and therefore the exception
    // channel rather than a diagnostic (P-14; there is no resolve-phase condition for it,
    // and giving SpecFieldInvalid a second phase would break D-067's one-code-one-phase
    // rule). Same reader-gate/factory-backstop split the discretizer factories follow.
    private static NameFormat? ParseEffectiveFormat(string? format)
    {
        if (format is null)
        {
            return null;
        }

        if (!NameFormat.TryCreate(format, out var parsed, out var error))
        {
            throw new InvalidOperationException(
                $"formal_attribute_format \"{format}\" is invalid ({error}); " +
                "SpecReader validates every authored format at parse (§10.7, corrupt document state).");
        }

        return parsed;
    }

    private static SourceBinding? ResolveSource(
        AttributeSection section,
        string attribute,
        string? attributeName,
        SourceShape shape,
        SourceSchema? schema,
        bool hasHeader,
        List<ResolvedNameBinding> nameBindings,
        List<BedrockDiagnostic> diagnostics)
    {
        switch (section.Source)
        {
            // §10.2: source kind must match the binding shape.
            case ColumnSourceSection when shape == SourceShape.Triple:
                AddSourceInvalid(diagnostics, attribute, "has a column source, which requires a wide binding");
                return null;

            case ColumnSourceSection column:
                if (ResolveColumnIndex(column, attribute, attributeName, schema, hasHeader, nameBindings, diagnostics) is not { } index)
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
        string? attributeName,
        SourceSchema? schema,
        bool hasHeader,
        List<ResolvedNameBinding> nameBindings,
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

            // The conversion pipeline resolves schema-aware (G-1/D-098), so this seam
            // owns the source-index range check. A schema-less resolve (spec tooling)
            // leaves the width unknown; ResolvedSpec.Create is the trust-boundary backstop.
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
                // Record the site-typed name binding for the ResolvedSpec trust boundary
                // (D-098); an empty attribute name is already an AttributeNameMissing error
                // that fails the success gate, so the binding is never consumed there.
                if (!string.IsNullOrEmpty(attributeName))
                {
                    nameBindings.Add(new AttributeSourceNameBinding(attributeName, byName, found));
                }

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
    // manual_cuts, equal_width, and equal_frequency are number-fixing (their cuts are numeric),
    // identity/ordered_cuts/none string (D-061). free_per_value is type-flexible, so it
    // takes the authored type or the string default. Include-independent: value_type is a
    // source-level property, so a parked cut discretizer still types the source — and its
    // live restrict_to (D-076). Shape-agnostic: both column and predicate sources carry
    // a value_type.
    private static SourceValueType ResolveValueType(SourceValueType? authored, DiscretizerSection? discretizer) =>
        authored
            ?? (discretizer is ManualCutsDiscretizerSection or EqualWidthDiscretizerSection or EqualFrequencyDiscretizerSection
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

    // §10.3/§5.1 (D-096): normalize a numeric free_per_value declared_domain to canonical
    // numeric identities under binding.locale, preserving declaration order (§17 rule 3, over
    // the first occurrence of each identity). An unparseable, non-finite, or normalization-duplicate
    // entry is DeclaredDomainInvalid (one per bad entry — spec-validate diagnostics aggregate). An
    // absent or empty authored domain resolves absent (calibrated later); the canonical list is what
    // the resolved Core graph and fingerprint carry (the document keeps the authored spellings).
    private static IReadOnlyList<string> NormalizeNumericDomain(
        IReadOnlyList<string>? authored, CultureInfo culture, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (authored is not { Count: > 0 })
        {
            return [];
        }

        var canonical = new List<string>(authored.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in authored)
        {
            if (!CanonicalNumber.TryParse(entry, culture, out var value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.DeclaredDomainInvalid, DiagnosticSeverity.Error,
                    $"declared_domain entry '{entry}' on numeric free_per_value attribute '{attribute}' is not a finite number under binding.locale (§10.3).",
                    new DiagnosticLocation(AttributeName: attribute)));
                continue;
            }

            var key = CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(value));
            if (!seen.Add(key))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.DeclaredDomainInvalid, DiagnosticSeverity.Error,
                    $"declared_domain entry '{entry}' on numeric free_per_value attribute '{attribute}' duplicates the numeric identity '{key}' of an earlier entry (§10.3, D-096).",
                    new DiagnosticLocation(AttributeName: attribute)));
                continue;
            }

            canonical.Add(key);
        }

        return canonical;
    }

    // §10.8 (D-096): normalize a numeric free_per_value value_labels map to canonical numeric
    // key identities under binding.locale. Two keys collapsing to one identity are
    // ValueLabelKeyDuplicate; a key whose (canonical) identity is not in the normalized domain —
    // including an unparseable key, which names no numeric identity — stays ValueLabelKeyNotInDomain
    // (the typo-catcher). The resolved dictionary is keyed by canonical identity so the planner
    // renders bins by the same identity (§11.3/D-092).
    private static IReadOnlyDictionary<string, string> NormalizeNumericValueLabels(
        IReadOnlyDictionary<string, string>? authored,
        IReadOnlyList<string> canonicalDomain,
        CultureInfo culture,
        string attribute,
        List<BedrockDiagnostic> diagnostics)
    {
        if (authored is not { Count: > 0 })
        {
            return NoLabels;
        }

        var domain = new HashSet<string>(canonicalDomain, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rawKey, labelValue) in authored)
        {
            if (!CanonicalNumber.TryParse(rawKey, culture, out var value))
            {
                // Unparseable numeric label key names no domain identity — the typo-catcher.
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ValueLabelKeyNotInDomain, DiagnosticSeverity.Error,
                    $"value_labels key '{rawKey}' on numeric free_per_value attribute '{attribute}' is not a finite number in its declared_domain (§10.8).",
                    new DiagnosticLocation(AttributeName: attribute)));
                continue;
            }

            var key = CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(value));
            if (!seen.Add(key))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ValueLabelKeyDuplicate, DiagnosticSeverity.Error,
                    $"value_labels keys on attribute '{attribute}' collapse to one numeric identity '{key}' (e.g. '{rawKey}', §10.8, D-096).",
                    new DiagnosticLocation(AttributeName: attribute)));
                continue;
            }

            if (!domain.Contains(key))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ValueLabelKeyNotInDomain, DiagnosticSeverity.Error,
                    $"value_labels key '{rawKey}' (numeric identity '{key}') on attribute '{attribute}' is not in its declared_domain (§10.8).",
                    new DiagnosticLocation(AttributeName: attribute)));
                continue;
            }

            result[key] = labelValue;
        }

        return result;
    }

    // §12.3 (D-096): normalize a numeric free_per_value scale.order to canonical numeric identities.
    // An invalid (unparseable/non-finite) or normalization-duplicate entry reuses OrderDomainInvalid
    // (fired once, matching the string ValidateOrdinalOrderShape structural check); the resulting
    // canonical order is used only when resolution succeeds. The order/domain permutation check is a
    // plan-phase concern (OrdinalOrderMissing / OrdinalOrderHasUnknownValue) over these normalized keys.
    private static IReadOnlyList<string> NormalizeNumericOrder(
        IReadOnlyList<string> authored, CultureInfo culture, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        var canonical = new List<string>(authored.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in authored)
        {
            if (!CanonicalNumber.TryParse(entry, culture, out var value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrderDomainInvalid, DiagnosticSeverity.Error,
                    $"scale.order entry '{entry}' on numeric free_per_value attribute '{attribute}' is not a finite number under binding.locale (§12.3, D-096).",
                    new DiagnosticLocation(AttributeName: attribute)));
                return canonical;
            }

            var key = CanonicalNumber.Format(CanonicalNumber.CanonicalizeZero(value));
            if (!seen.Add(key))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrderDomainInvalid, DiagnosticSeverity.Error,
                    $"scale.order entries on attribute '{attribute}' collapse to one numeric identity '{key}' (§12.3, D-096).",
                    new DiagnosticLocation(AttributeName: attribute)));
                return canonical;
            }

            canonical.Add(key);
        }

        return canonical;
    }

    // The Slice D static attribute checks (D-067). They read the document
    // sections directly — authored-vs-default provenance exists only there
    // (D-060) — and run whether or not the source/discretizer/scale resolved,
    // so one bad field does not mask another (P-14).
    private static void ValidateAttributeConstraints(
        AttributeSection section,
        string attribute,
        bool include,
        SourceValueType valueType,
        DefaultsSection? defaults,
        List<BedrockDiagnostic> diagnostics)
    {
        if (include)
        {
            ValidateValueType(section, attribute, diagnostics);
            ValidateOrdinalOverCuts(section, attribute, defaults, diagnostics);
            ValidateValueLabels(section, attribute, valueType, diagnostics);
            ValidateOrdinalOrderShape(section, attribute, valueType, diagnostics);
            ValidateValueGroups(section, attribute, diagnostics);
        }

        // restrict_to is live config even when the attribute is excluded (the
        // filter-only pattern, §10.1/§10.4) — its shape checks are
        // include-independent; only the parked-domain typo-catcher is gated (D-076).
        ValidateRestrictTo(section, attribute, include, diagnostics);
    }

    // §10.2 (D-061): a type-fixing discretizer disallows the other authored
    // value_type; only an authored type can conflict — the derived default is
    // the fixed type by construction. Parked (excluded) config never blocks
    // (D-049). Every §11 kind reaches this seam as of M4 Slice E (D-104), so the
    // switch below is the complete matrix: identity/ordered_cuts/value_groups are
    // string-fixing, manual_cuts/equal_width/equal_frequency number-fixing, and
    // free_per_value alone is type-FLEXIBLE — it has no arm because neither
    // authored type conflicts with it. Source-kind agnostic: value_type is a
    // source-level property of both column and predicate sources (§10.2).
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
            EqualWidthDiscretizerSection when value == SourceValueType.String =>
                "declares value_type = \"string\", but equal_width is number-fixing (its cuts are numeric)",
            EqualFrequencyDiscretizerSection when value == SourceValueType.String =>
                "declares value_type = \"string\", but equal_frequency is number-fixing (its cuts are numeric)",
            ValueGroupsDiscretizerSection when value == SourceValueType.Number =>
                "declares value_type = \"number\", but value_groups is string-fixing (groups match raw value spellings)",
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

    // §11.6 (D-090): the two value_groups rules the seam owns — the reader owns each group's own
    // validity, and these are the CROSS-group / cross-field ones it cannot see.
    //
    // (a) Authored labels must be distinct, and — under unmatched = "other" — none may collide
    //     with the synthetic Other bin. Ordinal comparison (P-12): "Other" collides, "other"
    //     does not. Duplicates own ValueGroupsLabelDuplicate and never surface as
    //     SpecFieldInvalid (D-090); one diagnostic per duplicate occurrence. A pass-through value
    //     merely OBSERVED to equal a label is data-dependent and belongs to plan
    //     (FormalAttributeCollision), not here.
    // (b) ordinal + passthrough is impossible, not merely unusual: ordinal over groups requires an
    //     authored scale.order that is a full permutation of the group labels (§12.3), and a
    //     data-discovered bin set can never be one.
    //
    // Include-gated by the caller, so parked config never blocks (D-049).
    private static void ValidateValueGroups(
        AttributeSection section, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (section.Discretizer is not ValueGroupsDiscretizerSection valueGroups)
        {
            return;
        }

        var unmatched = valueGroups.Unmatched ?? ValueGroupsUnmatched.Skip; // §11.6 default

        foreach (var (label, otherCollision) in LabelConflicts(valueGroups))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.ValueGroupsLabelDuplicate, DiagnosticSeverity.Error,
                otherCollision
                    ? $"Attribute '{attribute}' declares a value_groups group labelled 'Other', which collides with the synthetic bin unmatched = \"other\" adds (§11.6)."
                    : $"Attribute '{attribute}' declares the value_groups label '{label}' more than once; group labels must be distinct (§11.6).",
                new DiagnosticLocation(AttributeName: attribute)));
        }

        if (unmatched == ValueGroupsUnmatched.Passthrough && section.Scale is OrdinalScaleSection)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrdinalNotAllowedWithValueGroupsPassthrough, DiagnosticSeverity.Error,
                $"Attribute '{attribute}' uses an ordinal scale over value_groups with unmatched = \"passthrough\"; its bins are discovered from the data, so no authored scale.order can be a full permutation of them (§11.6/§12.3).",
                new DiagnosticLocation(AttributeName: attribute)));
        }
    }

    // The single decision point for the §11.6 label rules, in authored order: one entry per
    // conflict, flagged as a synthetic-Other collision or a plain duplicate. Two callers with
    // different jobs share it so they cannot drift (P-5) — ValidateValueGroups turns each entry
    // into the user-facing ValueGroupsLabelDuplicate, and ResolveValueGroups uses "any conflict"
    // to decline building the discretizer WITHOUT reporting the same condition a second time
    // (D-067, one condition → one code). Groups with no usable label are skipped: the reader owns
    // those (SpecFieldInvalid). Ordinal throughout (P-12).
    private static List<(string Label, bool OtherCollision)> LabelConflicts(ValueGroupsDiscretizerSection section)
    {
        var conflicts = new List<(string, bool)>();
        if (section.Groups is not { } groups)
        {
            return conflicts;
        }

        var unmatched = section.Unmatched ?? ValueGroupsUnmatched.Skip; // §11.6 default
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group?.Label is not { Length: > 0 } label)
            {
                continue;
            }

            if (!seen.Add(label))
            {
                conflicts.Add((label, false));
            }
            else if (unmatched == ValueGroupsUnmatched.Other && string.Equals(label, "Other", StringComparison.Ordinal))
            {
                conflicts.Add((label, true));
            }
        }

        return conflicts;
    }

    // §10.8 (D-080): value_labels keys must name a declared_domain value. Re-homed
    // from the planner to the seam, over the document model (D-067 phase ownership):
    // the code is §16.4 spec-validate, and reading the section directly catches a
    // stale key even when a sibling field fails to resolve (ResolveAttribute would
    // return null). The consulting kinds are exactly Discretizer.ConsultsValueLabels:
    // identity and free_per_value (§10.8). A numeric free_per_value is handled during
    // resolution (its keys normalize to canonical identities, D-096) and is skipped here
    // to avoid a double report; identity and string free_per_value compare verbatim.
    // Under any other discretizer value_labels is dormant (§10.8/D-049) — ignored here
    // and in name rendering, never an error. Include-gated by the caller (D-049).
    private static void ValidateValueLabels(
        AttributeSection section, string attribute, SourceValueType valueType, List<BedrockDiagnostic> diagnostics)
    {
        if (section.Discretizer is FreePerValueDiscretizerSection && valueType == SourceValueType.Number)
        {
            return;
        }

        if (section.ValueLabels is not { Count: > 0 } labels
            || section.Discretizer is not (IdentityDiscretizerSection or FreePerValueDiscretizerSection))
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

    // §10.4 (D-063/D-091): restrict_to validation. Entry-type checks run regardless of
    // include — restrict_to filters even when the attribute is excluded (filter-only), and a
    // parked numeric-cut discretizer legitimately types it ("a numeric source … or a
    // numeric-cut discretizer", §10.4/D-076). The domain typo-catcher fires only against a live
    // domain: included, string-typed identity/free_per_value with an explicit non-empty
    // declared_domain (declared_domain is parked when excluded, D-049; the numeric mismatch is
    // owned by RestrictToNumericEntryRequired). Source-kind agnostic: the value-type shape
    // checks apply to any source carrying a value_type (§10.2).
    //
    // The attribute's single effective value_type is resolved ONCE here under the D-061 matrix
    // and decides which entry forms are legal: string-fixing accepts only bare strings,
    // number-fixing only numeric entries (exact or range). No value_type admits both, so a
    // genuinely mixed list always reports.
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
            // Two INDEPENDENT questions, checked independently so both report when both hold
            // (P-14 aggregation; the D-076 precedent where the quote check and the
            // delimiter/quote conflict co-fire). Compatibility with the source's value_type is
            // one condition; the entry's own validity is another, and an entry can be wrong on
            // both counts at once — e.g. `{ value = nan }` on a string source.
            ValidateRestrictEntryCompatibility(entry, valueType, attribute, diagnostics);
            ValidateRestrictEntryValidity(entry, attribute, diagnostics);
        }

        // §10.4/§10.8 (D-063/D-101): the typo-catcher fires against a live string domain — the
        // domain-consulting string discretizers are identity and (string) free_per_value; a numeric
        // free_per_value is number-typed, so the value-type gate above already excludes it. The domain
        // is verbatim strings here (numeric normalization applies only to numeric free_per_value).
        if (!include
            || valueType != SourceValueType.String
            || section.Discretizer is not (IdentityDiscretizerSection or FreePerValueDiscretizerSection)
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

    // §10.2/§10.4: does this entry FORM suit the attribute's single value_type? A string-fixing
    // source accepts only bare strings; a number-fixing source only numeric entries (exact or
    // range). The two mismatches have different owners — D-063 gives the numeric-source /
    // bare-string case its own code rather than folding it into SourceValueTypeInvalid.
    private static void ValidateRestrictEntryCompatibility(
        RestrictToEntry entry, SourceValueType valueType, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        switch (entry)
        {
            case RestrictToValue value when valueType == SourceValueType.Number:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.RestrictToNumericEntryRequired, DiagnosticSeverity.Error,
                    $"Attribute '{attribute}' is number-typed, but restrict_to entry \"{value.Value}\" is a bare string; numeric restriction uses a numeric entry — exact {{ value = n }} or a range (§10.4).",
                    new DiagnosticLocation(AttributeName: attribute)));
                break;

            case RestrictToNumber or RestrictToRange when valueType == SourceValueType.String:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.SourceValueTypeInvalid, DiagnosticSeverity.Error,
                    $"Attribute '{attribute}' is string-typed, but restrict_to contains a numeric entry (§10.4/§10.2).",
                    new DiagnosticLocation(AttributeName: attribute)));
                break;
        }
    }

    // §10.4/D-091: is this entry well-formed in itself, whatever source it sits on? Independent
    // of the value_type check above — an entry on the wrong source can also be internally
    // invalid, and silently dropping the second finding would hide a second edit the author has
    // to make.
    private static void ValidateRestrictEntryValidity(
        RestrictToEntry entry, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        switch (entry)
        {
            // An exact value must be finite: a non-finite one can match no usable observation, so
            // it is authored nonsense rather than a filter that happens to keep nothing.
            case RestrictToNumber { Value: var number } when !double.IsFinite(number):
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.RestrictToRangeInvalid, DiagnosticSeverity.Error,
                    $"Attribute '{attribute}' has restrict_to entry {{ value = {TomlLiteral.FormatDouble(number)} }}, which is not finite; an exact numeric entry must be a finite number (§10.4).",
                    new DiagnosticLocation(AttributeName: attribute)));
                break;

            case RestrictToRange range:
                ValidateRestrictRange(range, attribute, diagnostics);
                break;
        }
    }

    // §10.4/D-091: a range's PROVIDED bounds must be finite and strictly ordered from < to. An
    // omitted bound is open (null), not infinity, so {} — neither bound authored — is valid and
    // means "any usable numeric value"; one-sided ranges are equally valid. from == to is
    // rejected rather than treated as empty: under the half-open [from, to) convention it can
    // match nothing, so it is authored nonsense, and from > to likewise. One diagnostic per
    // offending entry (independent conditions aggregate, P-14).
    private static void ValidateRestrictRange(
        RestrictToRange range, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        var from = range.From;
        var to = range.To;

        if (from is { } low && !double.IsFinite(low))
        {
            AddRangeInvalid(diagnostics, attribute, $"its 'from' bound {TomlLiteral.FormatDouble(low)} is not finite");
            return;
        }

        if (to is { } high && !double.IsFinite(high))
        {
            AddRangeInvalid(diagnostics, attribute, $"its 'to' bound {TomlLiteral.FormatDouble(high)} is not finite");
            return;
        }

        if (from is { } f && to is { } t && f >= t)
        {
            AddRangeInvalid(
                diagnostics,
                attribute,
                f == t
                    ? $"its bounds are equal ({TomlLiteral.FormatDouble(f)}); a half-open [from, to) range with from == to matches nothing"
                    : $"its bounds are reversed (from = {TomlLiteral.FormatDouble(f)}, to = {TomlLiteral.FormatDouble(t)}); a range needs from < to");
        }
    }

    private static void AddRangeInvalid(List<BedrockDiagnostic> diagnostics, string attribute, string reason) =>
        diagnostics.Add(new BedrockDiagnostic(
            DiagnosticCode.RestrictToRangeInvalid, DiagnosticSeverity.Error,
            $"Attribute '{attribute}' has an invalid restrict_to range: {reason} (§10.4).",
            new DiagnosticLocation(AttributeName: attribute)));

    // §10.4/G-6/D-096: valid exact values and provided bounds are zero-canonicalized at
    // resolution, so an authored -0 resolves — and therefore matches, plans, and hashes —
    // identically to 0. This is the "already-numeric" arm of the pinned chain (the values arrive
    // as TOML doubles; there is no text to parse), and it is scoped to the new M4 numeric
    // identities: CanonicalJson.AppendNumber and every authored manual-cut byte are untouched.
    //
    // Authored ORDER and DUPLICATES survive verbatim — resolved Core state mirrors the document
    // (D-057). Canonical sorting/deduplication is a fingerprint projection only (§14) and must
    // not rewrite what the author wrote. A non-finite value is left as-is: it is already
    // diagnosed (RestrictToRangeInvalid) and the Error fails the result before any strict
    // factory sees it.
    private static IReadOnlyList<RestrictToEntry> NormalizeRestrictTo(IReadOnlyList<RestrictToEntry>? entries)
    {
        if (entries is not { Count: > 0 })
        {
            return [];
        }

        var normalized = new List<RestrictToEntry>(entries.Count);
        foreach (var entry in entries)
        {
            normalized.Add(entry switch
            {
                RestrictToNumber number => new RestrictToNumber(CanonicalNumber.CanonicalizeZero(number.Value)),
                RestrictToRange range => new RestrictToRange(CanonicalizeBound(range.From), CanonicalizeBound(range.To)),
                _ => entry,
            });
        }

        return normalized;
    }

    private static double? CanonicalizeBound(double? bound) =>
        bound is { } value ? CanonicalNumber.CanonicalizeZero(value) : null;

    // §12.3 (D-060): over cut bins the discretizer geometry is the single source
    // of order and operator. Both checks read the document sections — the
    // authored-vs-default boundary provenance exists only there (D-060(c)) —
    // and fire only on active attributes (parked scale config never blocks,
    // D-049). equal_width is a cut kind on exactly the same terms (its computed cuts
    // fix the bin order, §12.3/D-102); the last deferred cut kind (equal_frequency)
    // is read-rejected before this seam (D-070).
    private static void ValidateOrdinalOverCuts(
        AttributeSection section,
        string attribute,
        DefaultsSection? defaults,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.Scale is not OrdinalScaleSection ordinal || !IsCutDiscretizer(section.Discretizer))
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
        AttributeSection section, string attribute, SourceValueType valueType, List<BedrockDiagnostic> diagnostics)
    {
        // A numeric free_per_value order is normalized + validated during resolution
        // (OrderDomainInvalid over canonical identities, D-096); skip here to avoid a double
        // report. String free_per_value orders compare verbatim, exactly like identity.
        if (section.Discretizer is FreePerValueDiscretizerSection && valueType == SourceValueType.Number)
        {
            return;
        }

        if (section.Scale is not OrdinalScaleSection { Order: { } order } || IsCutDiscretizer(section.Discretizer))
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

    // §12.3/§17 r3: the discretizers whose bins are cut intervals, so the cut geometry —
    // not scale.order — fixes the bin order. The one place the seam's cut-kind set lives, so
    // the ordinal-over-cuts checks and the value-bin order check stay exact complements and
    // never double-report. equal_frequency joined at Slice D (D-103): its bins are cut
    // intervals like any other, so it needs no ordinal implementation of its own — the
    // existing cut geometry is the ordering authority.
    private static bool IsCutDiscretizer(DiscretizerSection? section) =>
        section is ManualCutsDiscretizerSection or OrderedCutsDiscretizerSection
            or EqualWidthDiscretizerSection or EqualFrequencyDiscretizerSection;

    private static Discretizer? ResolveDiscretizer(
        DiscretizerSection? section,
        string attribute,
        CultureInfo culture,
        SourceValueType valueType,
        List<BedrockDiagnostic> diagnostics)
    {
        switch (section)
        {
            case IdentityDiscretizerSection:
                return new IdentityDiscretizer();

            case FreePerValueDiscretizerSection:
                // §11.3/D-061: type-flexible — the resolved value_type decides string-vs-numeric
                // identity; the culture parses numeric values (unused in string mode).
                return new FreePerValueDiscretizer(valueType, culture);

            case ManualCutsDiscretizerSection manual:
                // §11.2 defaults; the D-056 factory owns cut validation and its
                // diagnostics merge into this pass.
                return Merge(
                    ManualCutsDiscretizer.Create(manual.Cuts ?? [], manual.Ends ?? BinEnds.Open, culture),
                    attribute, diagnostics);

            case EqualWidthDiscretizerSection equalWidth:
                return ResolveEqualWidth(equalWidth, attribute, culture, diagnostics);

            case EqualFrequencyDiscretizerSection equalFrequency:
                return ResolveEqualFrequency(equalFrequency, attribute, culture, diagnostics);

            case ValueGroupsDiscretizerSection valueGroups:
                return ResolveValueGroups(valueGroups, attribute, culture, diagnostics);

            case OrderedCutsDiscretizerSection ordered:
                return Merge(
                    OrderedCutsDiscretizer.Create(ordered.Order ?? [], ordered.Cuts ?? [], ordered.Ends ?? BinEnds.Open),
                    attribute, diagnostics);

            default:
                AddScalingMissing(diagnostics, attribute, "has no discretizer (§10.9)");
                return null;
        }
    }

    // §11.4 (D-089/D-102): the range mode decides the phase. "manual" is spec-determined —
    // the D-056-style factory derives and validates the cuts here, and its diagnostics
    // (EqualWidthRangeInvalid / EqualWidthCutsCollapsed) merge into this pass. A data-derived
    // range cannot resolve without data, so it resolves to the CalibrationPending carrier the
    // Calibrate phase replaces (D-093) — not a second unresolved-discretizer shape.
    // <para>
    // The reader owns the field shapes (bins presence/range, the range spelling, the vmin/vmax
    // presence rules — SpecFieldInvalid, §11.4), so a document that reached this seam carries
    // them. The guards below are the backstop for a hand-built section that bypassed the
    // reader: they resolve to the same AttributeScalingMissing the other unbuildable
    // discretizer carriers use (§10.9) rather than throwing or — worse — dropping the
    // attribute silently. The strict factories then only ever see valid arguments.
    // </para>
    private static Discretizer? ResolveEqualWidth(
        EqualWidthDiscretizerSection section,
        string attribute,
        CultureInfo culture,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.Bins is not { } authoredBins || authoredBins is < 2 or > int.MaxValue)
        {
            AddScalingMissing(diagnostics, attribute, "has an equal_width discretizer with no usable bins count (§11.4)");
            return null;
        }

        var bins = (int)authoredBins;
        var range = section.Range ?? EqualWidthRange.MinMax; // §11.4 default
        var precision = section.Precision ?? CutPrecision.Exact; // §11.4 default

        if (range == EqualWidthRange.Manual)
        {
            if (section.VMin is not { } vmin || section.VMax is not { } vmax)
            {
                AddScalingMissing(diagnostics, attribute, "has an equal_width discretizer with range = \"manual\" but no vmin/vmax (§11.4)");
                return null;
            }

            return Merge(EqualWidthDiscretizer.CreateManual(bins, vmin, vmax, precision, culture), attribute, diagnostics);
        }

        return new CalibrationPending(new PendingEqualWidth(bins, range, precision), culture);
    }

    // §11.5 (D-088/D-103): equal_frequency is always data-calibrated — its cuts come from the
    // population under every configuration — so it has no spec-determined mode and always
    // resolves to the CalibrationPending carrier the Calibrate phase replaces (D-093). The
    // §11.5 defaults resolve here (tie_policy = "left", cut_placement = "right_value") so the
    // carrier — and therefore the calibrator and the §14 fingerprint — see one concrete
    // configuration; the document keeps the authored/omitted distinction (D-049).
    //
    // The reader owns the field shapes (bins presence/range, the two spellings —
    // SpecFieldInvalid, §11.5), so a document that reached this seam carries them. The guard
    // below is the backstop for a hand-built section that bypassed the reader: it resolves to
    // the same AttributeScalingMissing the other unbuildable discretizer carriers use (§10.9)
    // rather than throwing, so the strict carrier constructor only ever sees valid arguments.
    private static Discretizer? ResolveEqualFrequency(
        EqualFrequencyDiscretizerSection section,
        string attribute,
        CultureInfo culture,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.Bins is not { } authoredBins || authoredBins is < 2 or > int.MaxValue)
        {
            AddScalingMissing(diagnostics, attribute, "has an equal_frequency discretizer with no usable bins count (§11.5)");
            return null;
        }

        var config = new PendingEqualFrequency(
            (int)authoredBins,
            section.TiePolicy ?? Core.Discretization.TiePolicy.Left,           // §11.5 default
            section.CutPlacement ?? Core.Discretization.CutPlacement.RightValue); // §11.5 default

        return new CalibrationPending(config, culture);
    }

    // §11.6 (D-090/D-104): the unmatched policy decides the phase. skip/other are
    // spec-determined and resolve straight to the executable discretizer; passthrough discovers
    // its bins from the data, so — like a data-derived equal_width range — it resolves to the
    // CalibrationPending carrier the Calibrate phase replaces (D-093).
    //
    // The reader owns every field shape (groups presence, each group's label/values/pattern
    // validity including the regex compile, the unmatched spelling — SpecFieldInvalid, §11.6) and
    // ValidateValueGroups owns the cross-group rules (ValueGroupsLabelDuplicate), so a document
    // that reached a CLEAN resolve carries them all and the strict factories below only ever see
    // valid arguments (the success-gate sequence). The guards here are the backstop for a
    // hand-built section that bypassed the reader: they resolve to the same
    // AttributeScalingMissing the other unbuildable discretizer carriers use (§10.9) rather than
    // throwing — an authored error must never leave on the exception channel (P-14).
    private static Discretizer? ResolveValueGroups(
        ValueGroupsDiscretizerSection section,
        string attribute,
        CultureInfo culture,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.Groups is not { } authored)
        {
            AddScalingMissing(diagnostics, attribute, "has a value_groups discretizer with no groups (§11.6)");
            return null;
        }

        var unmatched = section.Unmatched ?? ValueGroupsUnmatched.Skip; // §11.6 default

        var groups = new List<ValueGroup>(authored.Count);
        foreach (var group in authored)
        {
            // The reader guarantees a label and a usable matcher on every carried group; a
            // section that bypassed it is unbuildable, not diagnosable per-field here.
            if (group.Label is not { Length: > 0 })
            {
                AddScalingMissing(diagnostics, attribute, "has a value_groups group with no label (§11.6)");
                return null;
            }

            try
            {
                // Authored presence flows straight through: a null Values stays null (omitted) and
                // an authored empty list stays empty (G-11) — Create never normalizes one to the
                // other, and the pattern text is retained verbatim.
                groups.Add(ValueGroup.Create(group.Label, group.Values, group.Pattern));
            }
            catch (ArgumentException)
            {
                // Only reachable from a hand-built section (the reader's gate is equivalent);
                // report on the diagnostic channel rather than letting the backstop escape.
                AddScalingMissing(diagnostics, attribute, $"has an invalid value_groups group '{group.Label}' (§11.6)");
                return null;
            }
        }

        // ValidateValueGroups owns the user-facing ValueGroupsLabelDuplicate for exactly this
        // condition, so decline silently rather than letting the strict factory throw and adding a
        // SECOND diagnostic for one condition (D-067). The attribute is dropped either way — that
        // Error fails the resolve — and this is what keeps the strict factories below behind a
        // clean gate.
        if (LabelConflicts(section).Count > 0)
        {
            return null;
        }

        if (unmatched == ValueGroupsUnmatched.Passthrough)
        {
            // The carrier requires a culture; value_groups never parses a value (matching is
            // ordinal + culture-invariant), so this one is carried for uniformity, not read.
            return new CalibrationPending(new PendingValueGroupsPassthrough(groups), culture);
        }

        return ValueGroupsDiscretizer.Create(groups, unmatched);
    }

    private static Scale? ResolveScale(
        ScaleSection? section,
        string attribute,
        DefaultsSection? defaults,
        CultureInfo? numericFreePerValueCulture,
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
                // §12.3/D-096: a numeric free_per_value order is normalized to canonical numeric
                // identities (OrderDomainInvalid on invalid/duplicate entries); every other order
                // is used verbatim. An absent order stays null (natural numeric ascending order is
                // derived at plan for numeric free_per_value; a plan-phase OrdinalOrderMissing
                // otherwise). Omitted direction/boundary fill from [defaults] then hard defaults
                // (D-060(c)).
                var order = numericFreePerValueCulture is { } orderCulture && ordinal.Order is { } authoredOrder
                    ? NormalizeNumericOrder(authoredOrder, orderCulture, attribute, diagnostics)
                    : ordinal.Order;
                return new OrdinalScale(
                    ordinal.Direction ?? defaults?.OrdinalDirection ?? OrdinalDirection.Ge,
                    ordinal.DropTop ?? false,
                    ordinal.Boundary ?? defaults?.OrdinalBoundary ?? OrdinalBoundary.Inclusive,
                    order);

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

    // The success gate (round-7 High-1): on any Error/Fatal, fail without calling any
    // strict factory; only on a clean pass build the read settings, the ResolvedSpec
    // token (the trust boundary), and the document-paired wrapper. An exception beyond
    // this gate is an implementation invariant, never a user-input channel.
    private static Diagnosed<ResolvedDocument> Finish(
        BedrockSpec spec,
        SpecDocument document,
        SourceSchema? schema,
        List<ResolvedNameBinding> nameBindings,
        List<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return Diagnosed<ResolvedDocument>.Failed(diagnostics);
            }
        }

        var settings = SourceReadSettings.Create(
            spec.Binding.Shape,
            spec.Binding.Encoding,
            spec.Binding.Delimiter,
            spec.Binding.QuoteChar,
            spec.Binding.HasHeader,
            spec.Binding.MissingToken,
            spec.Binding.Ordering);
        var resolvedSpec = ResolvedSpec.Create(spec, schema, settings, nameBindings);
        var resolvedDocument = new ResolvedDocument(DocumentSnapshot.Take(document), resolvedSpec);
        return Diagnosed<ResolvedDocument>.Ok(resolvedDocument, diagnostics);
    }
}
