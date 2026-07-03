using FcaBedrock.Core.Discretization;
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
    /// <summary>
    /// Plans the conversion, aggregating all validation/plan diagnostics (P-13).
    /// <paramref name="labelStyle"/> selects how cut bin labels render in names
    /// (spec §8/§14); it affects rendered names only, never identity (P-14, D-044).
    /// </summary>
    public static Diagnosed<ConversionPlan> Plan(
        BedrockSpec spec, SourceSchema schema, LabelStyle labelStyle = LabelStyle.Native)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(schema);

        // Fail-closed guard (D-072, transitional → M3): a triple spec resolves to a
        // minimal reject-carrier (D-066) that would otherwise plan to an empty plan
        // with zero diagnostics — the only silent path a resolved carrier can take.
        if (spec.Binding.Shape == SourceShape.Triple)
        {
            return Diagnosed<ConversionPlan>.Failed([new BedrockDiagnostic(
                DiagnosticCode.TripleSourceNotImplementedV1, DiagnosticSeverity.Error,
                "Triple source conversion is not implemented in this milestone (planned for M3).")]);
        }

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

            PlanAttribute(attribute, schema, labelStyle, formalAttributes, plannedAttributes, idByName, idByIdentity, diagnostics);
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
        LabelStyle labelStyle,
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
        var scheme = discretizer.DescribeBins(attribute.DeclaredDomain);
        var knownBins = new HashSet<string>(scheme.Labels, StringComparer.Ordinal);

        var crossesByBin = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var shape in scale.BuildShapes(scheme))
        {
            var id = formalAttributes.Count;
            var name = RenderName(attribute, shape, discretizer, labelStyle);
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

    private static string RenderName(
        AttributeSpec attribute, Scaling.FormalAttributeShape shape, Discretizer discretizer, LabelStyle labelStyle)
    {
        // Scale-specific default naming (§10.7). An explicit formal_attribute_format
        // override is not modelled until it has a caller (a later slice / M2 TOML).
        if (shape.ValueLabel is null)
        {
            return attribute.Name; // dichotomic: column alone
        }

        // value_labels (display names) win where set — but only for discretizers
        // that consult them (§10.8 / D-049). Under a cut discretizer the labels are
        // dormant, so the discretizer renders the canonical bin label for the style
        // (cut bins → v2-compat form) and a label keyed to a bin string is ignored.
        var display = discretizer.ConsultsValueLabels
            && attribute.ValueLabels.TryGetValue(shape.ValueLabel, out var label)
            ? label
            : discretizer.RenderBinLabel(shape.ValueLabel, labelStyle);

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
                // §10.9 / D-049: include = false is an authoring toggle. Any emitted
                // config the attribute retains is parked — ignored here, never an
                // error. (restrict_to still applies; that lands in a later slice.)
                continue;
            }

            ValidateValueLabels(attribute, diagnostics);
        }
    }

    private static void ValidateValueLabels(AttributeSpec attribute, List<BedrockDiagnostic> diagnostics)
    {
        if (attribute.ValueLabels.Count == 0)
        {
            return;
        }

        // §10.8 / D-049: value_labels is only consulted by discretizers whose bin
        // label IS the raw value (identity, free_per_value). Under any other
        // discretizer the labels are dormant — ignored here and in RenderName, never
        // an error. Discretizer.ConsultsValueLabels is the single authority.
        if (attribute.Discretizer?.ConsultsValueLabels != true)
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
