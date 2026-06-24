using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Planning;

/// <summary>
/// Turns a validated spec plus source schema into an immutable
/// <see cref="ConversionPlan"/>. Pure and data-free (spec §7 step 3). Centralizes
/// every ordering rule (decisions.md D-004): attribute order follows the spec,
/// formal-attribute order follows the discretizer's bins then the scale's
/// enumeration, and names are rendered here (not in the writers — P-14).
/// </summary>
public static class ConversionPlanner
{
    /// <summary>Plans the conversion, aggregating all validation/plan diagnostics (P-13).</summary>
    public static Diagnosed<ConversionPlan> Plan(BedrockSpec spec, SourceSchema schema)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(schema);

        var diagnostics = new List<BedrockDiagnostic>();
        ValidateStatic(spec, diagnostics);
        if (HasError(diagnostics))
        {
            return Diagnosed<ConversionPlan>.Failed(diagnostics);
        }

        var formalAttributes = new List<FormalAttribute>();
        var plannedAttributes = new List<PlannedAttribute>();
        var idByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var idByIdentity = new Dictionary<FormalAttributeIdentity, int>();

        foreach (var attribute in spec.Attributes)
        {
            if (!attribute.Include)
            {
                continue; // slice 1: excluded attributes contribute nothing (restrictions land later)
            }

            PlanAttribute(attribute, schema, formalAttributes, plannedAttributes, idByName, idByIdentity, diagnostics);
        }

        if (HasError(diagnostics))
        {
            return Diagnosed<ConversionPlan>.Failed(diagnostics);
        }

        var plan = new ConversionPlan(formalAttributes, plannedAttributes, spec.Binding.ObjectKey);
        return Diagnosed<ConversionPlan>.Ok(plan, diagnostics);
    }

    private static void PlanAttribute(
        AttributeSpec attribute,
        SourceSchema schema,
        List<FormalAttribute> formalAttributes,
        List<PlannedAttribute> plannedAttributes,
        Dictionary<string, int> idByName,
        Dictionary<FormalAttributeIdentity, int> idByIdentity,
        List<BedrockDiagnostic> diagnostics)
    {
        // The .bed reader (the only slice-1 spec producer) guarantees these for an
        // included attribute; a violation is an internal invariant, not user error.
        var discretizer = attribute.Discretizer
            ?? throw new InvalidOperationException($"Included attribute '{attribute.Name}' has no discretizer.");
        var scale = attribute.Scale
            ?? throw new InvalidOperationException($"Included attribute '{attribute.Name}' has no scale.");

        var columnIndex = ResolveColumn(attribute.Name, attribute.Source, schema);
        var binLabels = discretizer.BinLabels(attribute.DeclaredDomain);
        var knownBins = new HashSet<string>(binLabels, StringComparer.Ordinal);

        var crossesByBin = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var shape in scale.BuildShapes(binLabels))
        {
            var id = formalAttributes.Count;
            var name = RenderName(attribute, shape);
            var identity = new FormalAttributeIdentity(attribute.Name, scale.Kind, shape.BinKey, shape.ScaleOp);

            if (!idByName.TryAdd(name, id))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.FormalAttributeNameCollision,
                    DiagnosticSeverity.Error,
                    $"Formal attribute name '{name}' (from attribute '{attribute.Name}') collides with an earlier one.",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }

            if (!idByIdentity.TryAdd(identity, id))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.FormalAttributeCollision,
                    DiagnosticSeverity.Error,
                    $"Formal attribute identity ({identity.AttributeName}/{identity.Scale}/{identity.BinKey}) collides with an earlier one.",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }

            formalAttributes.Add(new FormalAttribute(id, name, identity));

            foreach (var bin in shape.CrossingBins)
            {
                if (!crossesByBin.TryGetValue(bin, out var ids))
                {
                    ids = [];
                    crossesByBin[bin] = ids;
                }

                ids.Add(id);
            }
        }

        plannedAttributes.Add(new PlannedAttribute(
            attribute.Name,
            columnIndex,
            discretizer,
            knownBins,
            Freeze(crossesByBin),
            attribute.MissingPolicy,
            attribute.UnknownValuePolicy));
    }

    private static string RenderName(AttributeSpec attribute, Scaling.FormalAttributeShape shape)
    {
        // Scale-specific default naming (§10.7). An explicit formal_attribute_format
        // override is not modelled until it has a caller (a later slice / M2 TOML).
        if (shape.ValueLabel is null)
        {
            return attribute.Name; // dichotomic: column alone
        }

        var display = attribute.ValueLabels.TryGetValue(shape.ValueLabel, out var label)
            ? label
            : shape.ValueLabel;

        return shape.ScaleOp.Length == 0
            ? $"{attribute.Name}-{display}"               // nominal
            : $"{attribute.Name}-{shape.ScaleOp}{display}"; // ordinal
    }

    private static int ResolveColumn(string attributeName, SourceBinding source, SourceSchema schema)
    {
        var index = source switch
        {
            ColumnSource column => column.Index,
            _ => throw new NotSupportedException(
                $"Source binding {source.GetType().Name} on attribute '{attributeName}' is not supported in this slice."),
        };

        if (index < 0 || index >= schema.ColumnCount)
        {
            throw new InvalidOperationException(
                $"Column index {index} on attribute '{attributeName}' is out of range for a source with {schema.ColumnCount} columns.");
        }

        return index;
    }

    private static void ValidateStatic(BedrockSpec spec, List<BedrockDiagnostic> diagnostics)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in spec.Attributes)
        {
            if (!seenNames.Add(attribute.Name))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.AttributeNameDuplicate,
                    DiagnosticSeverity.Error,
                    $"Attribute name '{attribute.Name}' is declared more than once.",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }

            if (!attribute.Include)
            {
                ValidateExcluded(attribute, diagnostics);
                continue;
            }

            ValidateValueLabels(attribute, diagnostics);
        }
    }

    private static void ValidateExcluded(AttributeSpec attribute, List<BedrockDiagnostic> diagnostics)
    {
        // §10.9: emitted-only fields explicitly set on an excluded attribute are an
        // error. Built-in/[defaults] values are not "present" and are ignored, so
        // the reader leaves these empty for an excluded attribute.
        var hasEmittedFields = attribute.Discretizer is not null
            || attribute.Scale is not null
            || attribute.ValueLabels.Count > 0
            || attribute.DeclaredDomain.Count > 0;

        if (hasEmittedFields)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.EmittedFieldOnExcludedAttribute,
                DiagnosticSeverity.Error,
                $"Excluded attribute '{attribute.Name}' sets emitted-only fields (discretizer/scale/value_labels/declared_domain).",
                new DiagnosticLocation(AttributeName: attribute.Name)));
        }
    }

    private static void ValidateValueLabels(AttributeSpec attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (attribute.ValueLabels.Count == 0)
        {
            return;
        }

        var domain = new HashSet<string>(attribute.DeclaredDomain, StringComparer.Ordinal);
        foreach (var key in attribute.ValueLabels.Keys)
        {
            if (!domain.Contains(key))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ValueLabelKeyNotInDomain,
                    DiagnosticSeverity.Error,
                    $"value_labels key '{key}' on attribute '{attribute.Name}' is not in its declared_domain.",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<int>> Freeze(Dictionary<string, List<int>> map)
    {
        var frozen = new Dictionary<string, IReadOnlyList<int>>(map.Count, StringComparer.Ordinal);
        foreach (var (bin, ids) in map)
        {
            frozen[bin] = ids;
        }

        return frozen;
    }

    private static bool HasError(List<BedrockDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal)
            {
                return true;
            }
        }

        return false;
    }
}
