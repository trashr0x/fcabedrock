using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
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
                continue; // excluded attributes contribute nothing; their restrict_to
                          // is guarded in ValidateStatic until execution lands at M4
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
            var name = RenderName(attribute, shape, discretizer, labelStyle);
            var identity = new FormalAttributeIdentity(attribute.Name, scale.Kind, shape.BinKey, shape.ScaleOp);
            var id = AddFormalAttribute(name, identity, shape.Bin, formalAttributes, idByName, idByIdentity, diagnostics);

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

        // §10.5 / D-068 / D-074: the missing column appends after the scale's columns
        // (uniformly across scale kinds); its bin key is the literal "missing" with an
        // empty operator (§14). The rendered name bypasses value_labels and label
        // style — "missing" is not a raw value.
        int? missingId = null;
        if (attribute.MissingPolicy == MissingPolicy.AsAttribute)
        {
            var identity = new FormalAttributeIdentity(attribute.Name, scale.Kind, "missing", "");
            missingId = AddFormalAttribute(
                $"{attribute.Name}-missing", identity, new ValueBin("missing"), formalAttributes, idByName, idByIdentity, diagnostics);
        }

        plannedAttributes.Add(new PlannedAttribute(
            attribute.Name,
            columnIndex,
            discretizer,
            knownBins,
            Freeze(crossesByBin),
            missingId,
            attribute.UnknownValuePolicy));
    }

    private static int AddFormalAttribute(
        string name,
        FormalAttributeIdentity identity,
        CanonicalBin bin,
        List<FormalAttribute> formalAttributes,
        Dictionary<string, int> idByName,
        Dictionary<FormalAttributeIdentity, int> idByIdentity,
        List<BedrockDiagnostic> diagnostics)
    {
        var id = formalAttributes.Count;

        if (!idByName.TryAdd(name, id))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.FormalAttributeNameCollision,
                DiagnosticSeverity.Error,
                $"Formal attribute name '{name}' (from attribute '{identity.AttributeName}') collides with an earlier one.",
                new DiagnosticLocation(AttributeName: identity.AttributeName)));
        }

        if (!idByIdentity.TryAdd(identity, id))
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.FormalAttributeCollision,
                DiagnosticSeverity.Error,
                $"Formal attribute identity ({identity.AttributeName}/{identity.Scale}/{identity.BinKey}) collides with an earlier one.",
                new DiagnosticLocation(AttributeName: identity.AttributeName)));
        }

        formalAttributes.Add(new FormalAttribute(id, name, identity, bin));
        return id;
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
        ValidateObjectKey(spec.Binding.ObjectKey, diagnostics);

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

            // §10.4 / D-057: restrict_to filters whether or not the attribute is
            // included (filter-only pattern), so the transitional reject sits
            // before the include-skip — silently ignoring it would emit
            // unfiltered output. Removed when execution lands at M4.
            if (attribute.RestrictTo.Count > 0)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.RestrictToNotImplementedV1,
                    DiagnosticSeverity.Error,
                    $"Attribute '{attribute.Name}' carries restrict_to, whose execution is not implemented in this milestone (planned for M4, §10.4).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }

            if (!attribute.Include)
            {
                // §10.9 / D-049: include = false is an authoring toggle. Any emitted
                // config the attribute retains is parked — ignored here, never an
                // error. (restrict_to stays live and is guarded above.)
                continue;
            }

            // §12.4 / D-010: deferred scales are parsable carriers the v1 planner
            // refuses — a permanent reservation, unlike the transitional rejects.
            if (attribute.Scale is Scaling.UnimplementedScale unimplemented)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ScaleNotImplementedV1,
                    DiagnosticSeverity.Fatal,
                    $"Attribute '{attribute.Name}' uses scale '{unimplemented.Kind}', which v1 does not implement (§12.4/§20).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }

            // §10.3 / D-071: an absent domain (omitted or authored []) on the
            // value-bin discretizer needs the observed-domain calibration the
            // pipeline does not build yet; planning it would emit an empty or
            // data-order-dependent schema. Blanket across scales — dichotomic
            // included, since every observed value would be "unknown" and the
            // column would never cross (D-076). Cut discretizers ignore the
            // domain (§10.3) and are unaffected. Removed when calibration lands.
            if (attribute.Discretizer is IdentityDiscretizer && attribute.DeclaredDomain.Count == 0)
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObservedDomainCalibrationNotImplementedV1,
                    DiagnosticSeverity.Error,
                    $"Attribute '{attribute.Name}' has no declared_domain; observed-domain calibration is not implemented in this milestone — declare the domain explicitly (§10.3).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }

            ValidateValueLabels(attribute, diagnostics);
        }
    }

    // §5.4 / D-064: object-key modes the v1 planner cannot execute are refused
    // rather than silently falling back to row index. The shape is Wide by
    // construction — the triple guard at the top of Plan precedes this check.
    private static void ValidateObjectKey(ObjectKey objectKey, List<BedrockDiagnostic> diagnostics)
    {
        switch (objectKey)
        {
            case CompositeObjectKey:
                // Permanent v1 reservation (D-024): composite keys are v1.1.
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyCompositeNotImplementedV1,
                    DiagnosticSeverity.Fatal,
                    "Composite object keys are not implemented in v1 (§5.4/§20)."));
                break;

            case ColumnObjectKey:
                // Transitional (D-064): wide column keys execute at M3, with the
                // triple subject-derived key machinery.
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.ObjectKeyColumnNotImplementedV1,
                    DiagnosticSeverity.Error,
                    "Wide column object keys are not implemented in this milestone (planned for M3, §5.4)."));
                break;
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
