using System.Collections.Frozen;
using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
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
/// enumeration, and names are rendered here (not in the writers — P-15).
/// </summary>
public static class ConversionPlanner
{
    /// <summary>
    /// Plans the conversion from resolved <b>calibrated state</b> (D-093/D-098): the
    /// effective spec and its schema come from <paramref name="calibrated"/>, so Plan
    /// cannot be handed an unrelated schema and "plan an uncalibrated spec" is a
    /// compile error. Aggregates all plan diagnostics (P-14). <paramref name="labelStyle"/>
    /// selects how cut bin labels render in names (spec §8/§14); it affects rendered
    /// names only, never identity (P-15, D-044), and is carried on the plan so the cxt
    /// output fingerprint pairs with it.
    /// </summary>
    public static Diagnosed<ConversionPlan> Plan(
        CalibratedSpec calibrated, LabelStyle labelStyle = LabelStyle.Native)
    {
        ArgumentNullException.ThrowIfNull(calibrated);

        var spec = calibrated.Spec;
        var schema = calibrated.Schema;

        var diagnostics = new List<BedrockDiagnostic>();
        ValidateStatic(spec, schema, diagnostics);
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

        // §16.4: a plan with zero columns (every attribute excluded, or — at M4 —
        // filter-only) is degenerate but structurally valid; warn, do not fail.
        if (formalAttributes.Count == 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.NoFormalAttributes, DiagnosticSeverity.Warning,
                "The plan produced no formal attributes; every attribute is excluded (§16.4)."));
        }

        var plan = new ConversionPlan(
            [.. formalAttributes],
            [.. plannedAttributes],
            ImmutableArray<PlannedRestriction>.Empty,
            spec.Binding.ObjectKey,
            ResolveExecution(spec.Binding),
            calibrated,
            labelStyle);
        return Diagnosed<ConversionPlan>.Ok(plan, diagnostics);
    }

    // §5 / D-082: the plan carries a shape-specific SourceExecution. Wide → the singleton;
    // triple → the resolved ordering verbatim (no fallback — ordering is required by the spec and the
    // resolver produces a non-null value or fails with a diagnostic; a null ordering on a triple
    // binding is a corrupt/internally-inconsistent Core state, so it throws via the invariant path,
    // like ResolveColumnIndex, rather than silently defaulting and masking the invalid binding). Not a
    // fingerprint input.
    private static SourceExecution ResolveExecution(Binding binding) =>
        binding.Shape switch
        {
            SourceShape.Wide => WideExecution.Instance,
            SourceShape.Triple => new TripleExecution(binding.Ordering
                ?? throw new InvalidOperationException(
                    "Triple binding has no resolved ordering; the resolver must supply one (corrupt Core state).")),
            _ => throw new InvalidOperationException($"Unknown source shape {binding.Shape}."),
        };

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

        var source = ResolveAttributeSource(attribute.Name, attribute.Source, schema);
        var scheme = discretizer.DescribeBins(attribute.DeclaredDomain);
        var knownBins = scheme.Labels.ToFrozenSet(StringComparer.Ordinal);

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
            source,
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

    // §10.2 / D-082: a wide attribute resolves to a range-checked column index; a triple
    // attribute carries its predicate selector verbatim (matched against data at emit, not
    // a column — so no schema range-check; a mistyped predicate surfaces at emit, D-082).
    // The resolver guarantees ColumnSource↔wide / PredicateSource↔triple (SourceBindingInvalid).
    private static AttributeSource ResolveAttributeSource(string attributeName, SourceBinding source, SourceSchema schema) =>
        source switch
        {
            ColumnSource column => new ColumnAttributeSource(ResolveColumnIndex(attributeName, column.Index, schema)),
            PredicateSource predicate => new PredicateAttributeSource(predicate.Predicate),
            _ => throw new NotSupportedException(
                $"Source binding {source.GetType().Name} on attribute '{attributeName}' is not supported."),
        };

    private static int ResolveColumnIndex(string attributeName, int index, SourceSchema schema)
    {
        if (index < 0 || index >= schema.ColumnCount)
        {
            throw new InvalidOperationException(
                $"Column index {index} on attribute '{attributeName}' is out of range for a source with {schema.ColumnCount} columns.");
        }

        return index;
    }

    private static void ValidateStatic(BedrockSpec spec, SourceSchema schema, List<BedrockDiagnostic> diagnostics)
    {
        // Static validation is ordering-agnostic: subject_grouped and unordered validate identically.
        // The resolved ordering is carried on the plan's SourceExecution (ResolveExecution, below) and
        // selects the row stream at emit (§5.3 / §17 rule 4 / D-082); no ordering-specific check here.
        ValidateObjectKey(spec.Binding.ObjectKey, spec.Binding.Shape, schema, diagnostics);

        // Duplicate authored names (AttributeNameDuplicate) and value_labels keys
        // (ValueLabelKeyNotInDomain) are rejected at the resolve seam over the
        // document model (D-080); the planner keeps only its plan-phase checks.
        foreach (var attribute in spec.Attributes)
        {
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

            // §10.3 / D-036: an absent domain (omitted or authored []) on a consuming
            // discretizer is now filled by the Calibrate phase (ObservedDomainUsed),
            // so the effective spec Plan receives already carries a resolved domain —
            // the D-071 transitional plan reject retired at M4. Cut discretizers ignore
            // the domain (§10.3) and are unaffected.

            // §12.3 / D-081: the value-bin ordinal path (identity — the only M2
            // value-bin discretizer, D-070) needs an explicit scale.order that is a
            // full permutation of the declared_domain, or it would silently ignore the
            // authored order/boundary. Membership is checked only for a non-empty
            // domain — an absent domain is already rejected by D-071 above, so
            // re-reporting here would double up (cut discretizers ignore the domain and
            // never take this path — their order is OrdinalOrderNotAllowedWithCuts).
            if (attribute.Discretizer is IdentityDiscretizer
                && attribute.Scale is OrdinalScale ordinal
                && attribute.DeclaredDomain.Count > 0)
            {
                ValidateValueBinOrder(attribute, ordinal, diagnostics);
            }
        }
    }

    // §12.3 / D-081: an identity value-bin ordinal must author a scale.order that is
    // a full permutation of the declared_domain — every domain value gets a threshold
    // (a value with no order entry is OrdinalOrderMissing; an order entry outside the
    // domain is OrdinalOrderHasUnknownValue, one per stray entry). The order lists raw
    // domain values, never display labels. Duplicate/empty order entries are caught
    // earlier at the resolve seam (OrderDomainInvalid, D-081). Runs on an included,
    // non-empty-domain identity attribute only (the caller's gate).
    private static void ValidateValueBinOrder(
        AttributeSpec attribute, OrdinalScale ordinal, List<BedrockDiagnostic> diagnostics)
    {
        if (ordinal.Order is not { } order)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrdinalOrderMissing, DiagnosticSeverity.Error,
                $"Attribute '{attribute.Name}' uses an ordinal scale over identity value bins but declares no scale.order; the bin order must be explicit (§12.3).",
                new DiagnosticLocation(AttributeName: attribute.Name)));
            return;
        }

        var domain = new HashSet<string>(attribute.DeclaredDomain, StringComparer.Ordinal);
        foreach (var value in order)
        {
            if (!domain.Contains(value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrdinalOrderHasUnknownValue, DiagnosticSeverity.Error,
                    $"scale.order entry '{value}' on attribute '{attribute.Name}' is not in its declared_domain (§12.3).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }
        }

        var ordered = new HashSet<string>(order, StringComparer.Ordinal);
        foreach (var value in attribute.DeclaredDomain)
        {
            if (!ordered.Contains(value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrdinalOrderMissing, DiagnosticSeverity.Error,
                    $"declared_domain value '{value}' on attribute '{attribute.Name}' has no scale.order entry; every value bin needs a threshold (§12.3).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }
        }
    }

    // §5.4 / D-064 / D-082 / D-083: object-key modes the v1 planner cannot execute are refused rather
    // than silently falling back to row index. Shape-aware: a triple ColumnObjectKey is the
    // subject-derived key, executed by the triple emit (D-082); a wide ColumnObjectKey executes at M3
    // (row_index/fail/keep single-pass, dedupe on the shared spool backend). Only composite stays a v1
    // reject.
    private static void ValidateObjectKey(
        ObjectKey objectKey, SourceShape shape, SourceSchema schema, List<BedrockDiagnostic> diagnostics)
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

            case ColumnObjectKey column when shape == SourceShape.Wide:
                // The wide column-key index range check now runs at spec-validate against
                // the schema the two-stage bootstrap resolves against (G-1/D-098), and the
                // ResolvedSpec trust boundary re-checks it, so an out-of-range index cannot
                // reach here from the conversion path. A residual violation is a corrupt
                // Core state (an unvalidated hand-built spec), not user input — throw.
                if (column.Index >= schema.ColumnCount)
                {
                    throw new InvalidOperationException(
                        $"object_key column index {column.Index} is out of range for a source with {schema.ColumnCount} columns; " +
                        "the resolve seam and ResolvedSpec.Create validate this (§5.4/D-098).");
                }

                break;
        }
    }

    // Freezes the per-bin crossing map into recursively-immutable storage (D-098): a
    // FrozenDictionary whose values are ImmutableArray, so no castable mutable
    // collection survives on the plan graph.
    private static IReadOnlyDictionary<string, IReadOnlyList<int>> Freeze(Dictionary<string, List<int>> map)
    {
        var frozen = new Dictionary<string, IReadOnlyList<int>>(map.Count, StringComparer.Ordinal);
        foreach (var (bin, ids) in map)
        {
            frozen[bin] = ids.ToImmutableArray();
        }

        return frozen.ToFrozenDictionary(StringComparer.Ordinal);
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
