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
/// supplied schema. Never throws for document content — every cannot-resolve
/// state maps to a seam-owned diagnostic (D-067). A triple document resolves
/// to a minimal reject-carrier (binding, no attributes) that the planner
/// refuses with <c>TripleSourceNotImplementedV1</c> (D-072).
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
        var hasHeader = bindingSection.HasHeader ?? true;
        var locale = bindingSection.Locale ?? "invariant";
        var culture = ResolveCulture(locale, diagnostics);

        // [binding].encoding stays document-only: Core carries no encoding field
        // and Sources is UTF-8-only until M3 (recorded for Slice C).
        var binding = new Binding(
            shape,
            bindingSection.Delimiter ?? ',',
            bindingSection.QuoteChar ?? '"',
            hasHeader,
            locale,
            bindingSection.MissingToken ?? "?",
            ResolveObjectKey(bindingSection, shape, document.Defaults, schema, diagnostics));

        if (shape == SourceShape.Triple)
        {
            // Reject-carrier only (D-066/D-072): predicate sources are
            // document-only, so no attributes resolve; the planner guard refuses
            // the spec before any planning.
            return Finish(new BedrockSpec(binding, []), diagnostics);
        }

        var attributes = new List<AttributeSpec>(document.Attributes.Count);
        foreach (var section in document.Attributes)
        {
            if (ResolveAttribute(section, document.Defaults, schema, hasHeader, culture, diagnostics) is { } attribute)
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
        DefaultsSection? defaults,
        SourceSchema? schema,
        List<BedrockDiagnostic> diagnostics)
    {
        var policy = defaults?.DuplicateObjectPolicy ?? DuplicateObjectPolicy.Fail;
        var section = binding.ObjectKey;
        if (section?.Mode is not { } mode)
        {
            // §5.4 defaults: wide → row_index; triple → the subject column (inert
            // behind the planner guard, D-072).
            return shape == SourceShape.Wide
                ? new RowIndexObjectKey()
                : new ColumnObjectKey(binding.Columns?.Subject ?? 0, policy);
        }

        // §5.4 (D-064): row_index keys are positional over wide rows only; under
        // triple the subject keys objects, so the mode contradicts the shape.
        if (mode == ObjectKeyMode.RowIndex && shape == SourceShape.Triple)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.ObjectKeyModeInvalidForShape, DiagnosticSeverity.Error,
                "object_key mode \"row_index\" is not allowed under shape = \"triple\" (§5.4)."));
            return new RowIndexObjectKey(); // placeholder; the Error fails the result
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

            // Without a schema the width is unknown; the plan-phase column-key
            // reject (ObjectKeyColumnNotImplementedV1, D-064) closes that window
            // until wide column keys execute at M3.
            case IndexColumnRef byIndex when schema is not null && byIndex.Index >= schema.ColumnCount:
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    $"object_key column index {byIndex.Index} is out of range for a source with {schema.ColumnCount} columns (§5.4)."));
                return null;

            case IndexColumnRef byIndex:
                return byIndex.Index;

            case NameColumnRef byName when schema?.Header is { } header:
                var index = IndexOf(header, byName.Name);
                if (index >= 0)
                {
                    return index;
                }

                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyBindingInvalid, DiagnosticSeverity.Error,
                    $"object_key column '{byName.Name}' is not in the source header (§5.4)."));
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

    private static AttributeSpec? ResolveAttribute(
        AttributeSection section,
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
        var source = ResolveSource(section, label, schema, hasHeader, diagnostics);

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

    private static ColumnSource? ResolveSource(
        AttributeSection section,
        string attribute,
        SourceSchema? schema,
        bool hasHeader,
        List<BedrockDiagnostic> diagnostics)
    {
        switch (section.Source)
        {
            case ColumnSourceSection column:
                if (ResolveColumnIndex(column, attribute, schema, hasHeader, diagnostics) is not { } index)
                {
                    return null;
                }

                return new ColumnSource(index, ResolveValueType(column, section.Discretizer));

            case PredicateSourceSection:
                AddSourceInvalid(diagnostics, attribute, "has a predicate source, which requires a triple binding");
                return null;

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
            var found = IndexOf(header, byName);
            if (found >= 0)
            {
                return found;
            }

            problem = $"binds source name '{byName}', which is not in the source header";
        }

        AddSourceInvalid(diagnostics, attribute, problem);
        return null;
    }

    // Authored value_type wins; otherwise the discretizer kind decides —
    // manual_cuts is number-fixing, identity/ordered_cuts/none string (D-061).
    // Include-independent: value_type is a source-level property, so a parked
    // cut discretizer still types the source — and its live restrict_to (D-076).
    private static SourceValueType ResolveValueType(ColumnSourceSection column, DiscretizerSection? discretizer) =>
        column.ValueType
            ?? (discretizer is ManualCutsDiscretizerSection
                ? SourceValueType.Number
                : SourceValueType.String);

    // The Slice D static attribute checks (D-067). They read the document
    // sections directly — authored-vs-default provenance exists only there
    // (D-060) — and run whether or not the source/discretizer/scale resolved,
    // so one bad field does not mask another (P-13).
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
    // seam (D-070).
    private static void ValidateValueType(
        AttributeSection section, string attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (section.Source is not ColumnSourceSection { ValueType: { } authored })
        {
            return;
        }

        var problem = section.Discretizer switch
        {
            IdentityDiscretizerSection when authored == SourceValueType.Number =>
                "declares value_type = \"number\", but identity is string-fixing — numeric distinct-value binning uses free_per_value",
            OrderedCutsDiscretizerSection when authored == SourceValueType.Number =>
                "declares value_type = \"number\", but ordered_cuts is string-fixing (categories are used verbatim)",
            ManualCutsDiscretizerSection when authored == SourceValueType.String =>
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

    // §10.4 (D-063): shape checks for the still-deferred restrict_to. Entry-type
    // checks run regardless of include — restrict_to filters even when the
    // attribute is excluded (filter-only), and a parked numeric-cut discretizer
    // legitimately types it ("a numeric source … or a numeric-cut discretizer",
    // §10.4/D-076). The domain typo-catcher fires only against a live domain:
    // included, string-typed identity with an explicit non-empty declared_domain
    // (declared_domain is parked when excluded, D-049; the numeric mismatch is
    // owned by RestrictToOnNumericRequiresRange).
    private static void ValidateRestrictTo(
        AttributeSection section,
        string attribute,
        bool include,
        List<BedrockDiagnostic> diagnostics)
    {
        if (section.RestrictTo is not { Count: > 0 } entries
            || section.Source is not ColumnSourceSection column)
        {
            return;
        }

        var valueType = ResolveValueType(column, section.Discretizer);
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
