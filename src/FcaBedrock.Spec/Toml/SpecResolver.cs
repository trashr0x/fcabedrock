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

        // Mode-vs-shape checks are the M2 validation slice (D-064).
        return mode switch
        {
            ObjectKeyMode.RowIndex => new RowIndexObjectKey(),
            // Columns/aggregate stay document-only (D-064); the marker becomes a
            // permanent plan-phase reject when the Slice D guard lands.
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

            // Without a schema the width is unknown; conversion fails loudly in
            // WideCsvSource for that window until the Slice D guards land.
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

                // Authored value_type wins; otherwise the discretizer kind decides —
                // manual_cuts is number-fixing, identity/ordered_cuts/none string (D-061).
                var valueType = column.ValueType
                    ?? (section.Discretizer is ManualCutsDiscretizerSection
                        ? SourceValueType.Number
                        : SourceValueType.String);
                return new ColumnSource(index, valueType);

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
