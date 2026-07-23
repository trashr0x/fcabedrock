using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
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
        var restrictions = new List<PlannedRestriction>();
        var idByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var idByIdentity = new Dictionary<FormalAttributeIdentity, int>();

        foreach (var attribute in spec.Attributes)
        {
            // §10.4/D-091: restrict_to is include-INDEPENDENT, so this runs before the
            // include-skip below. A filter-only attribute (include = false + restrict_to)
            // contributes only here — no PlannedAttribute, no formal column — while an
            // included-and-restricted attribute contributes both. Restrictions are built in
            // spec-attribute order (P-7); that order never reorders columns or objects.
            if (attribute.RestrictTo.Count > 0)
            {
                restrictions.Add(new PlannedRestriction(
                    attribute.Name,
                    ResolveAttributeSource(attribute.Name, attribute.Source, schema),
                    ValueTypeOf(attribute.Source),
                    attribute.RestrictTo,
                    attribute.UnknownValuePolicy));
            }

            if (!attribute.Include)
            {
                continue; // §10.9/D-049: excluded attributes contribute no column; their
                          // parked discretizer/scale/domain stay unread. Their live
                          // restrict_to was planned above.
            }

            PlanAttribute(attribute, schema, labelStyle, formalAttributes, plannedAttributes, idByName, idByIdentity, diagnostics);
        }

        if (HasError(diagnostics))
        {
            return Diagnosed<ConversionPlan>.Failed(diagnostics);
        }

        // §16.4: a plan with zero columns (every attribute excluded or filter-only) is
        // degenerate but structurally valid; warn, do not fail. An all-filter-only spec is
        // the M4 case — it still filters objects, it just emits no columns.
        if (formalAttributes.Count == 0)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.NoFormalAttributes, DiagnosticSeverity.Warning,
                "The plan produced no formal attributes; every attribute is excluded or filter-only (§16.4)."));
        }

        var plan = new ConversionPlan(
            [.. formalAttributes],
            [.. plannedAttributes],
            [.. restrictions],
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

        // A consuming discretizer's effective domain is non-null here (calibration filled an
        // omitted one before plan); a cut discretizer ignores it. Coalescing is byte-neutral.
        var declaredDomain = attribute.DeclaredDomain ?? [];
        var source = ResolveAttributeSource(attribute.Name, attribute.Source, schema);
        var scheme = discretizer.DescribeBins(declaredDomain);
        var knownBins = scheme.Labels.ToFrozenSet(StringComparer.Ordinal);

        // §12.3/§17-r2/D-096: a numeric free_per_value value-bin ordinal with no authored
        // scale.order uses the natural numeric ascending order of its (canonical) domain — the
        // one value-bin case exempt from the explicit-order requirement (ValidateValueBinOrder
        // enforces it everywhere else). An authored order is a validated permutation and used
        // verbatim.
        scale = DeriveNaturalNumericOrder(discretizer, scale, declaredDomain);

        // §10.7/D-117: the rendered-name backstop collects offenders in RENDER order
        // (the order this attribute's formal attributes are planned) so the sample list
        // is deterministic, and reports once for the whole logical attribute.
        var invalidNames = new List<string>();

        var crossesByBin = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var shape in scale.BuildShapes(scheme))
        {
            var name = RenderName(attribute, shape, discretizer, scale, labelStyle);
            RecordIfInvalid(name, invalidNames);
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
        // style — "missing" is not a raw value. §10.7/D-117: an explicit format is a
        // TOTAL override, so it renders this column too ({value} = the literal
        // "missing", {scale_op} empty) instead of the default "{column}-missing"; the
        // column's position and canonical identity are unaffected.
        int? missingId = null;
        if (attribute.MissingPolicy == MissingPolicy.AsAttribute)
        {
            var missingName = attribute.NameFormat is { } missingFormat
                ? missingFormat.Render(attribute.Name, attribute.DisplayName, "missing", "")
                : $"{attribute.Name}-missing";
            RecordIfInvalid(missingName, invalidNames);
            var identity = new FormalAttributeIdentity(attribute.Name, scale.Kind, "missing", "");
            missingId = AddFormalAttribute(
                missingName, identity, new ValueBin("missing"), formalAttributes, idByName, idByIdentity, diagnostics);
        }

        // Reported after the attribute's columns are registered, so ids and any
        // collision diagnostics stay exactly what a valid run would produce (P-7); the
        // Error fails the shared plan either way, blocking .dat as well as .cxt (§10.7).
        if (invalidNames.Count > 0)
        {
            diagnostics.Add(InvalidRenderedNames(attribute.Name, invalidNames));
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
        AttributeSpec attribute,
        Scaling.FormalAttributeShape shape,
        Discretizer discretizer,
        Scale scale,
        LabelStyle labelStyle)
    {
        if (attribute.NameFormat is { } format)
        {
            // §10.7/D-117: an explicit format overrides the scale default ENTIRELY, for
            // every formal attribute the logical attribute emits. {scale_op} is the
            // planned operator (empty for non-ordinal shapes).
            return format.Render(attribute.Name, attribute.DisplayName, RenderValue(attribute, shape, discretizer, scale, labelStyle), shape.ScaleOp);
        }

        // Scale-specific default naming (§10.7) — byte-identical to the pre-M6 path,
        // which every existing spec and golden still takes (no fixture authors a format).
        if (shape.ValueLabel is null)
        {
            return attribute.Name; // dichotomic: column alone
        }

        var display = RenderValue(attribute, shape, discretizer, scale, labelStyle);
        return shape.ScaleOp.Length == 0
            ? $"{attribute.Name}-{display}"               // nominal
            : $"{attribute.Name}-{shape.ScaleOp}{display}"; // ordinal
    }

    // The value side of a name (§10.7's {value} table), shared by the default and
    // explicit paths so the two cannot disagree about what a value renders as.
    //
    // value_labels (display names) win where set — but only for discretizers that
    // consult them (§10.8 / D-049). Under a cut discretizer the labels are dormant, so
    // the discretizer renders the canonical bin label for the style (cut bins →
    // v2-compat form; numeric free_per_value → its D-092 identity) and a label keyed to
    // a bin string is ignored.
    //
    // A dichotomic shape carries no value label of its own (the default name is the
    // column alone), so {value} resolves to the scale's true_value — through
    // value_labels when they are live, which is the labelled-dichotomic case §10.7
    // spells out: true_value = "t" labelled "bruised" renders "bruises?-bruised".
    private static string RenderValue(
        AttributeSpec attribute,
        Scaling.FormalAttributeShape shape,
        Discretizer discretizer,
        Scale scale,
        LabelStyle labelStyle)
    {
        var raw = shape.ValueLabel ?? (scale as DichotomicScale)?.TrueValue ?? "";
        return discretizer.ConsultsValueLabels && attribute.ValueLabels.TryGetValue(raw, out var label)
            ? label
            : discretizer.RenderBinLabel(raw, labelStyle);
    }

    // §10.7/D-117: after final substitution — on the default path too, because raw
    // values, calibrated domains, and value_labels can inject CR/LF independently of
    // any authored format.
    private static void RecordIfInvalid(string name, List<string> invalidNames)
    {
        if (name.Length == 0 || name.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            invalidNames.Add(name);
        }
    }

    // One aggregated Error per affected logical attribute (D-116 granularity), with a
    // deterministic representation: the count, then at most three offenders in render
    // order, each quoted and escaped, then the truncation tail. Pinned so two runs on
    // two machines produce byte-identical messages (P-7).
    private static BedrockDiagnostic InvalidRenderedNames(string attributeName, List<string> invalidNames)
    {
        const int sampleLimit = 3;
        var shown = Math.Min(sampleLimit, invalidNames.Count);
        var samples = new string[shown];
        for (var i = 0; i < shown; i++)
        {
            samples[i] = QuoteSample(invalidNames[i]);
        }

        var truncated = invalidNames.Count > sampleLimit
            ? $" (+{invalidNames.Count - sampleLimit} more)"
            : "";
        return new BedrockDiagnostic(
            DiagnosticCode.FormalAttributeNameInvalid,
            DiagnosticSeverity.Error,
            $"Attribute '{attributeName}' renders {invalidNames.Count} invalid formal-attribute name(s) — " +
            $"empty or containing CR/LF: {string.Join(", ", samples)}{truncated} (§10.7).",
            new DiagnosticLocation(AttributeName: attributeName));
    }

    // Exactly four escapes — backslash, double quote, CR, LF — and no other transform,
    // so an empty name is visible as "" and a newline is legible rather than breaking
    // the diagnostic across lines. Built char by char so the escapes cannot be applied
    // in the wrong order (a naive replace chain would double-escape backslashes).
    private static string QuoteSample(string name)
    {
        var quoted = new StringBuilder(name.Length + 2).Append('"');
        foreach (var character in name)
        {
            switch (character)
            {
                case '\\': quoted.Append(@"\\"); break;
                case '"': quoted.Append("\\\""); break;
                case '\r': quoted.Append(@"\r"); break;
                case '\n': quoted.Append(@"\n"); break;
                default: quoted.Append(character); break;
            }
        }

        return quoted.Append('"').ToString();
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

    // §10.2/D-061: the attribute's single effective value_type, fixed at the resolve seam and
    // carried on the resolved source. It selects the restriction's matching mode — string ⇒
    // ordinal equality, number ⇒ parsed numeric identity (§10.4) — and is read straight off the
    // source rather than re-derived from the discretizer, because a FILTER-ONLY attribute's
    // discretizer is parked (D-049) and may be absent entirely.
    private static SourceValueType ValueTypeOf(SourceBinding source) => source switch
    {
        ColumnSource column => column.ValueType,
        PredicateSource predicate => predicate.ValueType,
        _ => throw new NotSupportedException(
            $"Source binding {source.GetType().Name} carries no value type."),
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
            if (!attribute.Include)
            {
                // §10.9 / D-049: include = false is an authoring toggle. Any emitted
                // config the attribute retains is parked — ignored here, never an
                // error. Its restrict_to stays live: shape-validated at the resolve
                // seam and planned as a PlannedRestriction above (D-091/D-105).
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

            // §10.3 / D-036 / D-122 §15: an OMITTED domain on a consuming discretizer is
            // filled by the Calibrate phase (ObservedDomainUsed), so the effective spec Plan
            // receives already carries a resolved domain — the D-071 transitional plan reject
            // retired at M4. An authored [] is a complete fixed empty domain that calibration
            // leaves untouched. Cut discretizers ignore the domain (§10.3) and are unaffected.

            // The effective bin universe. A consuming discretizer's domain is non-null here
            // (calibration filled an omitted one before plan); a cut discretizer's is ignored.
            // An authored [] is a genuine empty universe — not coalesced away — so the ordinal
            // checks below still apply to it (D-122 §15 / REG-PRES-002).
            var declaredDomain = attribute.DeclaredDomain ?? [];

            // §12.3 / D-081 / D-122 §15: identity value bins need an explicit scale.order that is
            // a full permutation of the declared_domain, or they would silently ignore the
            // authored order/boundary. A complete empty universe (an authored []) is NOT exempt:
            // an omitted order there is OrdinalOrderMissing and order = [] is the valid empty
            // permutation. Cut discretizers ignore the domain and never take this path — their
            // order is OrdinalOrderNotAllowedWithCuts.
            if (attribute.Discretizer is IdentityDiscretizer
                && attribute.Scale is OrdinalScale ordinal)
            {
                ValidateValueBinOrder(
                    attribute, ordinal, declaredDomain, "identity value bins", "declared_domain value", diagnostics);
            }

            // §12.3 / D-096: free_per_value value bins take the same ordinal path. A NUMERIC
            // free_per_value with an absent scale.order is exempt from OrdinalOrderMissing — it
            // derives natural numeric ascending order at plan (DeriveNaturalNumericOrder); every
            // other case (string free_per_value, or a numeric one with an authored order) still
            // requires an explicit full-permutation order. Domain/order keys are canonical numeric
            // identities from the seam, so the permutation check compares them ordinally.
            if (attribute.Discretizer is FreePerValueDiscretizer freePerValue
                && attribute.Scale is OrdinalScale freeOrdinal
                && !(freePerValue.ValueType == SourceValueType.Number && freeOrdinal.Order is null))
            {
                ValidateValueBinOrder(
                    attribute, freeOrdinal, declaredDomain, "free_per_value value bins", "declared_domain value", diagnostics);
            }

            // §12.3 / §11.6 / D-090: value groups take the same value-bin ordinal path, but their
            // universe is the GROUP LABELS — not declared_domain, which value_groups ignores
            // entirely (D-055). The labels come from the discretizer's own BinLabels, so `Other`
            // is included exactly when unmatched = "other" and the permutation rule needs no
            // second copy of the bin-order logic. ordinal + passthrough is rejected at the seam
            // (OrdinalNotAllowedWithValueGroupsPassthrough) and so never reaches plan; a hand-built
            // spec that bypassed the seam degrades to requiring a permutation of the discovered
            // bins rather than crashing.
            if (attribute.Discretizer is ValueGroupsDiscretizer valueGroups
                && attribute.Scale is OrdinalScale groupOrdinal)
            {
                ValidateValueBinOrder(
                    attribute, groupOrdinal, valueGroups.BinLabels(declaredDomain),
                    "value groups", "group label", diagnostics);
            }
        }
    }

    // §12.3 / §17-r2 / D-096: the one value-bin-ordinal exemption. When a numeric free_per_value
    // ordinal authors no scale.order, its bin order is the natural NUMERIC ascending order of its
    // canonical domain keys — parsed back to their numeric value and sorted (distinct identities,
    // so the sort is total and deterministic, P-7/P-11). Every other discretizer/scale/order state
    // is returned unchanged.
    private static Scale DeriveNaturalNumericOrder(Discretizer discretizer, Scale scale, IReadOnlyList<string> domain)
    {
        if (discretizer is not FreePerValueDiscretizer { ValueType: SourceValueType.Number }
            || scale is not OrdinalScale { Order: null } ordinal)
        {
            return scale;
        }

        var keyed = new (double Value, string Key)[domain.Count];
        for (var i = 0; i < domain.Count; i++)
        {
            // ResolvedSpec.Create validates that every numeric free_per_value domain key is a canonical
            // numeric identity (D-096/D-098), so parsing them back to sort always succeeds; a failure
            // here is corrupt Core state (an unvalidated hand-built spec), not user input — throw rather
            // than silently sort a bad key as 0.
            if (!CanonicalNumber.TryParse(domain[i], CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException(
                    $"numeric free_per_value domain key '{domain[i]}' is not a finite number; " +
                    "ResolvedSpec.Create validates canonical numeric keys (corrupt Core state).");
            }

            keyed[i] = (value, domain[i]);
        }

        Array.Sort(keyed, static (a, b) => a.Value.CompareTo(b.Value));
        return ordinal with { Order = Array.ConvertAll(keyed, static k => k.Key) };
    }

    // §12.3 / D-081 / D-090: a value-bin or value-group ordinal must author a scale.order that
    // is a full permutation of its bin universe — every bin gets a threshold (a bin with no
    // order entry is OrdinalOrderMissing; an order entry outside the universe is
    // OrdinalOrderHasUnknownValue, one per stray entry). The order lists raw bin values or
    // group labels, never display labels. Duplicate/empty order entries are caught earlier at
    // the resolve seam (OrderDomainInvalid, D-081).
    //
    // The universe is the caller's, because it differs by kind: identity/free_per_value bin the
    // declared_domain, while value_groups ignores the domain entirely (D-055) and bins its group
    // labels plus a synthetic Other. Passing it in keeps ONE permutation algorithm over the
    // effective bin labels rather than a second ordinal implementation per kind (P-5).
    private static void ValidateValueBinOrder(
        AttributeSpec attribute,
        OrdinalScale ordinal,
        IReadOnlyList<string> universe,
        string what,
        string member,
        List<BedrockDiagnostic> diagnostics)
    {
        if (ordinal.Order is not { } order)
        {
            diagnostics.Add(new BedrockDiagnostic(
                DiagnosticCode.OrdinalOrderMissing, DiagnosticSeverity.Error,
                $"Attribute '{attribute.Name}' uses an ordinal scale over {what} but declares no scale.order; the bin order must be explicit (§12.3).",
                new DiagnosticLocation(AttributeName: attribute.Name)));
            return;
        }

        var bins = new HashSet<string>(universe, StringComparer.Ordinal);
        foreach (var value in order)
        {
            if (!bins.Contains(value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrdinalOrderHasUnknownValue, DiagnosticSeverity.Error,
                    $"scale.order entry '{value}' on attribute '{attribute.Name}' is not among its {what} (§12.3).",
                    new DiagnosticLocation(AttributeName: attribute.Name)));
            }
        }

        var ordered = new HashSet<string>(order, StringComparer.Ordinal);
        foreach (var value in universe)
        {
            if (!ordered.Contains(value))
            {
                diagnostics.Add(new BedrockDiagnostic(
                    DiagnosticCode.OrdinalOrderMissing, DiagnosticSeverity.Error,
                    $"{member} '{value}' on attribute '{attribute.Name}' has no scale.order entry; every bin needs a threshold (§12.3).",
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
