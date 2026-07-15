using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Planning;

public sealed class ConversionPlannerTests
{
    // Plans a hand-built spec + schema the way production does (D-098): resolve the token
    // (the trust boundary), take the fully-declared calibrated state, then plan. The
    // fixtures here are all fully-declared, so no data pass is needed.
    private static Diagnosed<ConversionPlan> Plan(BedrockSpec spec, SourceSchema schema, LabelStyle style = LabelStyle.Native) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(Resolve(spec, schema)), style);

    private static ResolvedSpec Resolve(BedrockSpec spec, SourceSchema schema) =>
        ResolvedSpec.Create(
            spec,
            schema,
            SourceReadSettings.Create(
                spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
            []);

    private static readonly string[] ExpectedMushroomNames =
    [
        "bruises?", "gill-size-broad", "gill-size-narrow", "veil-type-partial",
        "veil-type-universal", "ring-number-none", "ring-number-one", "ring-number-two",
    ];

    [Fact]
    public void Plan_WhenMiniMushroom_ThenFormalAttributesMatchV2OrderAndNames()
    {
        var result = Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5));

        Assert.True(result.TryGetValue(out var plan));
        Assert.Equal(ExpectedMushroomNames, plan.FormalAttributes.Select(f => f.RenderedName).ToArray());
    }

    [Fact]
    public void Plan_WhenDichotomic_ThenSingleColumnNamedAttributeOnlyAndCrossesTrueValue()
    {
        Assert.True(Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        var bruises = plan.Attributes.Single(a => a.Name == "bruises?");
        Assert.True(bruises.KnownBins.SetEquals(["t", "f"]));
        Assert.Equal([0], bruises.CrossesByBin["t"]);
        Assert.False(bruises.CrossesByBin.ContainsKey("f")); // recognized bin that crosses nothing
    }

    [Fact]
    public void Plan_WhenNominal_ThenEachBinCrossesItsOwnFormalAttribute()
    {
        Assert.True(Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        var gill = plan.Attributes.Single(a => a.Name == "gill-size");
        Assert.Equal([1], gill.CrossesByBin["b"]);
        Assert.Equal([2], gill.CrossesByBin["n"]);
    }

    [Fact]
    public void Plan_WhenNumericCutsNominal_ThenStyleChangesInteriorNamesButNotIdentityOrCrossings()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30, 40, 50], new NominalScale())]);

        Assert.True(Plan(spec, new SourceSchema(1), LabelStyle.Native).TryGetValue(out var native));
        Assert.True(Plan(spec, new SourceSchema(1), LabelStyle.V2Compat).TryGetValue(out var v2));

        Assert.Equal(
            ["age-<30", "age-[30, 40)", "age-[40, 50)", "age->=50"],
            native.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(
            ["age-<30", "age-30to<40", "age-40to<50", "age->=50"],
            v2.FormalAttributes.Select(f => f.RenderedName));

        // D-035: rendering differs by style; identity and crossings do not.
        Assert.Equal(native.FormalAttributes.Select(f => f.Identity), v2.FormalAttributes.Select(f => f.Identity));
        var ageNative = native.Attributes.Single(a => a.Name == "age");
        var ageV2 = v2.Attributes.Single(a => a.Name == "age");
        Assert.True(ageNative.KnownBins.SetEquals(["<30", "[30, 40)", "[40, 50)", ">=50"]));
        Assert.True(ageNative.KnownBins.SetEquals(ageV2.KnownBins));
        Assert.Equal([1], ageNative.CrossesByBin["[30, 40)"]); // canonical key, identical under both styles
        Assert.Equal([1], ageV2.CrossesByBin["[30, 40)"]);
    }

    [Fact]
    public void Plan_WhenNumericCutsOrdinalLe_ThenCumulativeCrossingsAndStyleIndependentThresholdNames()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30, 40, 50], new OrdinalScale(OrdinalDirection.Le))]);

        Assert.True(Plan(spec, new SourceSchema(1), LabelStyle.Native).TryGetValue(out var native));
        Assert.True(Plan(spec, new SourceSchema(1), LabelStyle.V2Compat).TryGetValue(out var v2));

        // Threshold labels come from the cuts (no interval), so they are style-independent.
        string[] expected = ["age-<30", "age-<40", "age-<50", "age-all"];
        Assert.Equal(expected, native.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(expected, v2.FormalAttributes.Select(f => f.RenderedName));

        var age = native.Attributes.Single(a => a.Name == "age");
        Assert.Equal([0, 1, 2, 3], age.CrossesByBin["<30"]);       // crossed by <30, <40, <50, all
        Assert.Equal([1, 2, 3], age.CrossesByBin["[30, 40)"]);     // crossed by <40, <50, all
        Assert.Equal([2, 3], age.CrossesByBin["[40, 50)"]);        // crossed by <50, all
        Assert.Equal([3], age.CrossesByBin[">=50"]);               // crossed by all only
    }

    [Fact]
    public void Plan_WhenAttributeExcluded_ThenItProducesNoFormalAttributes()
    {
        Assert.True(Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        Assert.DoesNotContain(plan.Attributes, a => a.Name == "class");
        Assert.All(plan.FormalAttributes, f => Assert.NotEqual("class", f.Identity.AttributeName));
    }

    [Fact]
    public void Plan_WhenEveryAttributeExcluded_ThenWarnsNoFormalAttributes()
    {
        // §16.4 (D-098): a plan with zero columns is degenerate but structurally valid — a Warning,
        // not an Error, so the plan still succeeds. Already reachable via an all-excluded spec.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Excluded("x", 0)]);

        var result = Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out var plan));
        Assert.Empty(plan.FormalAttributes);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.NoFormalAttributes);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void Plan_WhenCalledTwice_ThenProducesIdenticalFormalAttributeOrder()
    {
        var schema = new SourceSchema(5);

        Assert.True(Plan(SpecFixtures.MiniMushroom(), schema).TryGetValue(out var first));
        Assert.True(Plan(SpecFixtures.MiniMushroom(), schema).TryGetValue(out var second));

        Assert.Equal(
            first.FormalAttributes.Select(f => f.RenderedName),
            second.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenExcludedAttributeRetainsEmittedConfig_ThenIgnoredWithoutError()
    {
        // include = false is an authoring toggle (D-049): retained discretizer/scale/
        // domain/labels are parked, not rejected, and contribute no formal attributes.
        var parked = new AttributeSpec("x", new ColumnSource(0, SourceValueType.String), Include: false,
            new IdentityDiscretizer(), new NominalScale(), ["a"], RestrictTo: [],
            new Dictionary<string, string> { ["a"] = "Alpha" },
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [parked]);

        var result = Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out var plan));
        Assert.False(result.HasErrors);
        Assert.DoesNotContain(plan.Attributes, a => a.Name == "x");
        Assert.All(plan.FormalAttributes, f => Assert.NotEqual("x", f.Identity.AttributeName));
    }

    [Fact]
    public void Plan_WhenCutBasedAttributeHasValueLabelsMatchingBinLabel_ThenLabelsAreIgnoredInNames()
    {
        // §10.8 / D-049: value_labels is dormant under a cut discretizer — it must not
        // change rendered names, even when a key happens to match a cut-bin label.
        var age = new AttributeSpec("age", new ColumnSource(0, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create([30.0], BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            new NominalScale(), DeclaredDomain: [], RestrictTo: [],
            new Dictionary<string, string> { ["<30"] = "Young" },
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [age]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["age-<30", "age->=30"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenValueLabelsPartial_ThenLabeledValueUsesLabelAndUnlabeledFallsThroughToRaw()
    {
        // §10.8 coverage: a value in declared_domain but absent from value_labels falls through
        // to the raw value as its label; nominal default naming is {column}-{value} (§10.7).
        var attr = SpecFixtures.Nominal("g", 0, ["b", "n"], new Dictionary<string, string> { ["b"] = "broad" });
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));
        Assert.Equal(["g-broad", "g-n"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenRenderedNamesCollide_ThenReportsNameCollisionButNotIdentityCollision()
    {
        // "a"+"b-x" and "a-b"+"x" both render "a-b-x" but have distinct identities.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
        [
            SpecFixtures.Nominal("a", 0, ["b-x"]),
            SpecFixtures.Nominal("a-b", 1, ["x"]),
        ]);

        var result = Plan(spec, new SourceSchema(2));

        AssertFailsWith(result, DiagnosticCode.FormalAttributeNameCollision);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenDeclaredDomainHasDuplicate_ThenReportsIdentityCollision()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("a", 0, ["x", "x"])]);

        AssertFailsWith(Plan(spec, new SourceSchema(1)), DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenNominalAsAttribute_ThenMissingAttributeAppendedAfterValueBins()
    {
        // §10.5 / D-068: nominal + as_attribute → one extra column after the value
        // bins, canonical identity (name, scale, "missing", "") (§14).
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.Nominal("a", 0, ["x", "y"], missing: MissingPolicy.AsAttribute)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["a-x", "a-y", "a-missing"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(new FormalAttributeIdentity("a", "nominal", "missing", ""), plan.FormalAttributes[2].Identity);
        var planned = Assert.Single(plan.Attributes);
        Assert.Equal(2, planned.MissingFormalAttributeId);
        Assert.All(planned.CrossesByBin.Values, ids => Assert.DoesNotContain(2, ids)); // no bin crosses it
    }

    [Fact]
    public void Plan_WhenDichotomicAsAttribute_ThenMissingIsSecondColumn()
    {
        // §10.5 / §12.2: true_value crosses when present, missing crosses when absent.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.Dichotomic("bruises?", 0, "t", ["t", "f"], missing: MissingPolicy.AsAttribute)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["bruises?", "bruises?-missing"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(1, Assert.Single(plan.Attributes).MissingFormalAttributeId);
    }

    [Fact]
    public void Plan_WhenOrdinalAsAttribute_ThenMissingAttributeAppendedAfterThresholds()
    {
        // D-074: the position rule is uniform across scale kinds — the missing
        // column follows the ordinal threshold columns.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30, 40, 50], new OrdinalScale(OrdinalDirection.Le),
                missing: MissingPolicy.AsAttribute)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(
            ["age-<30", "age-<40", "age-<50", "age-all", "age-missing"],
            plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(4, Assert.Single(plan.Attributes).MissingFormalAttributeId);
    }

    [Fact]
    public void Plan_WhenAsAttributeOnEarlierAttribute_ThenLaterAttributeIdsFollowMissingColumn()
    {
        // §14: the missing column occupies a real slot in the planned list — the
        // next attribute's formal ids start after it (fingerprint-input ordering).
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
        [
            SpecFixtures.Nominal("a", 0, ["x"], missing: MissingPolicy.AsAttribute),
            SpecFixtures.Nominal("b", 1, ["z"]),
        ]);

        Assert.True(Plan(spec, new SourceSchema(2)).TryGetValue(out var plan));

        Assert.Equal(["a-x", "a-missing", "b-z"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal([2], plan.Attributes.Single(a => a.Name == "b").CrossesByBin["z"]);
    }

    [Fact]
    public void Plan_WhenDomainContainsLiteralMissingUnderAsAttribute_ThenCollisionDiagnostics()
    {
        // §10.7: a real category value "missing" collides with the missing column
        // in both rendered name and canonical identity; existing machinery reports it.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.Nominal("a", 0, ["missing"], missing: MissingPolicy.AsAttribute)]);

        var result = Plan(spec, new SourceSchema(1));

        AssertFailsWith(result, DiagnosticCode.FormalAttributeNameCollision);
        AssertFailsWith(result, DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenSkip_ThenMissingFormalAttributeIdIsNull()
    {
        Assert.True(Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        Assert.All(plan.Attributes, a => Assert.Null(a.MissingFormalAttributeId));
    }

    [Fact]
    public void Plan_WhenTripleSubjectGrouped_ThenBuildsFormalAttributesFromPredicates()
    {
        // D-082: a triple subject_grouped spec now plans (the transitional refusal retired at
        // Slice C). Formal attributes come from the predicate sources; each planned attribute
        // reads by predicate, not column; the subject-derived ColumnObjectKey is accepted.
        var spec = new BedrockSpec(SpecFixtures.TripleSubjectGrouped(), [
            SpecFixtures.PredicateNominal("color", "hasColor", ["red", "green"]),
            SpecFixtures.PredicateNominal("size", "hasSize", ["big", "small"]),
        ]);

        var result = Plan(spec, new SourceSchema(3));

        Assert.True(result.TryGetValue(out var plan));
        Assert.Equal(
            ["color-red", "color-green", "size-big", "size-small"],
            plan.FormalAttributes.Select(f => f.RenderedName).ToArray());
        Assert.All(plan.Attributes, a => Assert.IsType<PredicateAttributeSource>(a.Source));
    }

    [Fact]
    public void Plan_WhenTripleUnordered_ThenPlansIdenticallyToSubjectGrouped()
    {
        // §5.3 / §17 rule 4 / D-082: the formal-attribute schema is ordering-independent — unordered
        // plans the same schema as subject_grouped (no plan-phase reject). The resolved ordering rides
        // on the plan's SourceExecution; EmitTripleAsync honors it at emit, not here.
        AttributeSpec[] attributes = [SpecFixtures.PredicateNominal("color", "hasColor", ["red", "green"])];
        var unordered = new BedrockSpec(SpecFixtures.TripleSubjectGrouped(TripleOrdering.Unordered), attributes);
        var grouped = new BedrockSpec(SpecFixtures.TripleSubjectGrouped(TripleOrdering.SubjectGrouped), attributes);

        var unorderedResult = Plan(unordered, new SourceSchema(3));
        var groupedResult = Plan(grouped, new SourceSchema(3));

        Assert.False(unorderedResult.HasErrors);
        Assert.True(unorderedResult.TryGetValue(out var unorderedPlan));
        Assert.True(groupedResult.TryGetValue(out var groupedPlan));
        Assert.Equal(
            groupedPlan.FormalAttributes.Select(a => a.RenderedName),
            unorderedPlan.FormalAttributes.Select(a => a.RenderedName));
    }

    [Fact]
    public void Plan_WhenWide_ThenExecutionIsWide()
    {
        // D-082: the plan carries a shape-specific SourceExecution; a wide source plans the singleton.
        Assert.True(Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));
        Assert.Same(WideExecution.Instance, plan.Execution);
    }

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped)]
    [InlineData(TripleOrdering.Unordered)]
    public void Plan_WhenTriple_ThenExecutionIsTripleWithResolvedOrdering(TripleOrdering ordering)
    {
        // D-082: the plan carries the resolved ordering on its SourceExecution (no fallback).
        var spec = new BedrockSpec(SpecFixtures.TripleSubjectGrouped(ordering),
            [SpecFixtures.PredicateNominal("color", "hasColor", ["red", "green"])]);

        Assert.True(Plan(spec, new SourceSchema(3)).TryGetValue(out var plan));
        var triple = Assert.IsType<TripleExecution>(plan.Execution);
        Assert.Equal(ordering, triple.Ordering);
    }

    [Fact]
    public void Resolve_WhenTripleBindingHasNoResolvedOrdering_ThenThrowsAtTrustBoundary()
    {
        // D-082/D-098: triple ordering is required; a null ordering on a triple binding is a corrupt
        // Core state. Under the schema-aware pipeline the ResolvedSpec trust boundary rejects it
        // (ArgumentException) before Plan is reachable — the planner's residual invariant is now
        // unreachable-by-construction (never a silent SubjectGrouped default that would mask the
        // invalid binding and could wrongly reject interleaved data).
        var binding = new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail), new TripleColumns(0, 1, 2), Ordering: null);
        var spec = new BedrockSpec(binding, [SpecFixtures.PredicateNominal("color", "hasColor", ["red", "green"])]);

        Assert.Throws<ArgumentException>(() => Resolve(spec, new SourceSchema(3)));
    }

    [Theory]
    [InlineData("interordinal")]
    [InlineData("biordinal")]
    [InlineData("contranominal")]
    public void Plan_WhenScaleIsDeferred_ThenReportsScaleNotImplementedV1(string kind)
    {
        // §12.4 / D-010: deferred scales are parsable carriers the v1 planner
        // refuses — parse-but-fail-to-plan, at the plan phase (§16.4).
        var attr = new AttributeSpec("a", new ColumnSource(0, SourceValueType.String), Include: true,
            new IdentityDiscretizer(), new UnimplementedScale(kind),
            ["x"], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        var result = Plan(spec, new SourceSchema(1));

        AssertFailsWith(result, DiagnosticCode.ScaleNotImplementedV1);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.ScaleNotImplementedV1);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Contains(kind, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenDeferredScaleOnExcludedAttribute_ThenParkedAndNeverAnError()
    {
        // §10.9 / D-049: parked config never blocks — the deferred-scale guard
        // applies to included attributes only.
        var parked = new AttributeSpec("x", new ColumnSource(0, SourceValueType.String), Include: false,
            new IdentityDiscretizer(), new UnimplementedScale("biordinal"),
            ["a"], RestrictTo: [], SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [parked, SpecFixtures.Nominal("g", 1, ["b"])]);

        Assert.True(Plan(spec, new SourceSchema(2)).TryGetValue(out var plan));
        Assert.Equal(["g-b"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    // --- Slice D plan guards (D-057/D-063/D-064/D-071/D-076) ---

    [Fact]
    public void Plan_WhenObjectKeyComposite_ThenReportsObjectKeyCompositeNotImplementedV1Fatal()
    {
        // §5.4 / D-024/D-064: a permanent v1 reservation, rejected at plan.
        var spec = new BedrockSpec(WideWithKey(new CompositeObjectKey()), [SpecFixtures.Nominal("g", 0, ["b"])]);

        var result = Plan(spec, new SourceSchema(1));

        AssertFailsWith(result, DiagnosticCode.ObjectKeyCompositeNotImplementedV1);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
    }

    [Theory]
    [InlineData(DuplicateObjectPolicy.Fail)]
    [InlineData(DuplicateObjectPolicy.Keep)]
    [InlineData(DuplicateObjectPolicy.Dedupe)]
    public void Plan_WhenObjectKeyColumnUnderWide_ThenAccepted(DuplicateObjectPolicy policy)
    {
        // §5.4/§6.1 (D-083): wide column keys execute at M3 for every duplicate_object_policy —
        // fail/keep single-pass, dedupe on the shared spool backend — with no transitional reject and no
        // silent row_index fallback. The key column may sit anywhere in range.
        var spec = new BedrockSpec(
            WideWithKey(new ColumnObjectKey(0, policy)), [SpecFixtures.Nominal("g", 1, ["b"])]);

        var result = Plan(spec, new SourceSchema(2));

        Assert.False(result.HasErrors);
        Assert.True(result.TryGetValue(out _));
    }

    [Fact]
    public void Resolve_WhenObjectKeyColumnIndexOutOfRange_ThenThrowsAtTrustBoundary()
    {
        // D-085/D-098: an out-of-range key INDEX is a binding error. The conversion pipeline now
        // resolves schema-aware (G-1), so the range check is seam-owned — the resolver emits
        // ObjectKeyBindingInvalid over the document, and the ResolvedSpec trust boundary rejects a
        // hand-built spec with ArgumentException before Plan (the planner's residual is unreachable).
        var spec = new BedrockSpec(
            WideWithKey(new ColumnObjectKey(5, DuplicateObjectPolicy.Fail)), [SpecFixtures.Nominal("g", 1, ["b"])]);

        Assert.Throws<ArgumentException>(() => Resolve(spec, new SourceSchema(2)));
    }

    [Fact]
    public void Plan_WhenObjectKeyCompositeAndRestrictToPresent_ThenBothDiagnosticsReport()
    {
        // P-14: the object-key guard aggregates with the attribute checks rather
        // than short-circuiting the static pass. Duplicate names moved to the resolve
        // seam (D-080), so a still-plan-phase code — RestrictToNotImplementedV1 —
        // pairs with the object-key reject here.
        var attr = SpecFixtures.Nominal("g", 0, ["b"]) with { RestrictTo = [new RestrictToValue("b")] };
        var spec = new BedrockSpec(WideWithKey(new CompositeObjectKey()), [attr]);

        var result = Plan(spec, new SourceSchema(1));

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ObjectKeyCompositeNotImplementedV1);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.RestrictToNotImplementedV1);
    }

    [Fact]
    public void Plan_WhenRestrictToPresent_ThenReportsRestrictToNotImplementedV1()
    {
        // §10.4 / D-057: never silently ignored — unfiltered output would mismatch
        // the spec's intent. Transitional until execution lands at M4.
        var attr = SpecFixtures.Nominal("g", 0, ["b"]) with { RestrictTo = [new RestrictToValue("b")] };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        var result = Plan(spec, new SourceSchema(1));

        AssertFailsWith(result, DiagnosticCode.RestrictToNotImplementedV1);
    }

    [Fact]
    public void Plan_WhenRestrictToOnExcludedAttribute_ThenStillReportsRestrictToNotImplementedV1()
    {
        // §10.4 / D-057/D-076: restrict_to filters even on a filter-only attribute;
        // the guard sits before the include-skip.
        var attr = SpecFixtures.Excluded("Gene", 0) with { RestrictTo = [new RestrictToValue("Bmp5")] };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr, SpecFixtures.Nominal("g", 1, ["b"])]);

        var result = Plan(spec, new SourceSchema(2));

        AssertFailsWith(result, DiagnosticCode.RestrictToNotImplementedV1);
    }

    // Observed-domain calibration of an absent-domain identity attribute (formerly the
    // transitional ObservedDomainCalibrationNotImplementedV1 plan reject, D-071) now happens in
    // the Calibrate phase — its coverage lives in CalibratorTests (D-036/D-098). A
    // FromFullyDeclared plan of such a spec throws (it requires data), so it is not tested here.

    [Fact]
    public void Plan_WhenExcludedIdentityHasNoDomain_ThenNoCalibrationDiagnostic()
    {
        // D-049: parked config never blocks — the guard applies to included
        // attributes only.
        var parked = new AttributeSpec("x", new ColumnSource(0, SourceValueType.String), Include: false,
            new IdentityDiscretizer(), new NominalScale(), DeclaredDomain: [], RestrictTo: [],
            SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [parked, SpecFixtures.Nominal("g", 1, ["b"])]);

        var result = Plan(spec, new SourceSchema(2));

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Plan_WhenCutDiscretizerHasNoDomain_ThenNoCalibrationDiagnostic()
    {
        // §10.3: cut discretizers ignore declared_domain — the D-071 guard is
        // value-bin only.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale())]);

        var result = Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(result.Diagnostics);
    }

    // --- Value-bin ordinal (identity + explicit order, D-081) ---

    [Fact]
    public void Plan_WhenValueBinOrdinalLe_ThenThresholdNamesValueLabelsAndCumulativeCrossings()
    {
        // §12.3 / D-081: identity + explicit order → cumulative threshold columns
        // named {attr}-{op}{label}; value_labels supply the display label, the raw
        // order value is the style-independent identity key.
        var scale = new OrdinalScale(OrdinalDirection.Le, DropTop: false, OrdinalBoundary.Inclusive,
            ["Pre-Uni", "Undergrad", "Postgrad"]);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.OrdinalValueBins("edu", 0, ["Pre-Uni", "Undergrad", "Postgrad"], scale,
                new Dictionary<string, string> { ["Pre-Uni"] = "PU", ["Undergrad"] = "UG", ["Postgrad"] = "PG" })]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["edu-<=PU", "edu-<=UG", "edu-<=PG"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(["Pre-Uni", "Undergrad", "Postgrad"], plan.FormalAttributes.Select(f => f.Identity.BinKey));
        Assert.All(plan.FormalAttributes, f => Assert.Equal("<=", f.Identity.Operator));

        var edu = Assert.Single(plan.Attributes);
        Assert.True(edu.KnownBins.SetEquals(["Pre-Uni", "Undergrad", "Postgrad"]));
        Assert.Equal([0, 1, 2], edu.CrossesByBin["Pre-Uni"]);   // <=Pre-Uni, <=Undergrad, <=Postgrad
        Assert.Equal([1, 2], edu.CrossesByBin["Undergrad"]);    // <=Undergrad, <=Postgrad
        Assert.Equal([2], edu.CrossesByBin["Postgrad"]);        // <=Postgrad (tautological) only
    }

    [Fact]
    public void Plan_WhenValueBinOrdinalOmitsOrder_ThenReportsOrdinalOrderMissing()
    {
        // The F1 silent path, closed: identity + ordinal with no order used to plan
        // and emit while ignoring the authored order/boundary — now rejected (§12.3).
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, Order: null);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.OrdinalValueBins("edu", 0, ["a", "b"], scale)]);

        AssertFailsWith(Plan(spec, new SourceSchema(1)), DiagnosticCode.OrdinalOrderMissing);
    }

    [Fact]
    public void Plan_WhenOrderOmitsADomainValue_ThenReportsOrdinalOrderMissing()
    {
        // A full permutation is required — a domain value with no order entry has no
        // threshold (§12.3). Same code, message variant naming the missing value.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["a", "b"]);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.OrdinalValueBins("edu", 0, ["a", "b", "c"], scale)]);

        var result = Plan(spec, new SourceSchema(1));

        AssertFailsWith(result, DiagnosticCode.OrdinalOrderMissing);
        Assert.Contains("c",
            Assert.Single(result.Diagnostics, d => d.Code == DiagnosticCode.OrdinalOrderMissing).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_WhenOrderHasTwoStrayEntries_ThenTwoOrdinalOrderHasUnknownValue()
    {
        // Each order entry outside the declared_domain gets its own diagnostic (§12.3).
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["a", "x", "y"]);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.OrdinalValueBins("edu", 0, ["a"], scale)]);

        var result = Plan(spec, new SourceSchema(1));

        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.OrdinalOrderHasUnknownValue));
    }

    [Fact]
    public void Plan_WhenValueBinOrdinalAsAttribute_ThenMissingColumnFollowsThresholds()
    {
        // D-068/D-074: the missing column follows the ordinal threshold columns,
        // uniformly across scale kinds.
        var scale = new OrdinalScale(OrdinalDirection.Le, DropTop: false, OrdinalBoundary.Inclusive, ["a", "b"]);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.OrdinalValueBins("edu", 0, ["a", "b"], scale, missing: MissingPolicy.AsAttribute)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["edu-<=a", "edu-<=b", "edu-missing"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(2, Assert.Single(plan.Attributes).MissingFormalAttributeId);
    }

    // --- free_per_value value-bin planning (§11.3 / §12.3 / D-096, M4 Slice B) ---

    [Fact]
    public void Plan_WhenNumericFreePerValueNominal_ThenOneColumnPerCanonicalDomainKeyInDeclarationOrder()
    {
        // §17 r3: the canonical numeric domain keys drive column order and identity; the rendered
        // label is the canonical number (§10.7/§11.3/D-092).
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, ["90", "0", "5"], new NominalScale())]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["v-90", "v-0", "v-5"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(["90", "0", "5"], plan.FormalAttributes.Select(f => f.Identity.BinKey));
    }

    [Fact]
    public void Plan_WhenNumericFreePerValueWithValueLabels_ThenRendersLabelKeyedByCanonicalIdentity()
    {
        // value_labels keys are canonical numeric identities (D-096); the planner looks them up by
        // the canonical bin key (§10.8).
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, ["90", "5"], new NominalScale(),
                new Dictionary<string, string> { ["90"] = "ninety", ["5"] = "five" })]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["v-ninety", "v-five"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenNumericFreePerValueOrdinalOmitsOrder_ThenNaturalAscendingOrderNoDiagnostic()
    {
        // §12.3/D-096: numeric free_per_value with no scale.order is EXEMPT from OrdinalOrderMissing;
        // the bin order is the natural numeric ascending order of the (canonical) domain — regardless
        // of declaration order.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, Order: null);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, ["90", "0", "5"], scale)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        // Natural ascending order 0, 5, 90 — not the declaration order 90, 0, 5.
        Assert.Equal(["v->=0", "v->=5", "v->=90"], plan.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(["0", "5", "90"], plan.FormalAttributes.Select(f => f.Identity.BinKey));
    }

    [Theory]
    // §12.3: all four direction × boundary combinations are well-defined over value bins.
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, new[] { "v->=0", "v->=5", "v->=90" })]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict, new[] { "v->0", "v->5", "v->90" })]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive, new[] { "v-<=0", "v-<=5", "v-<=90" })]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict, new[] { "v-<0", "v-<5", "v-<90" })]
    public void Plan_WhenNumericFreePerValueOrdinalNaturalOrder_ThenAllFourDirectionBoundaryCombos(
        OrdinalDirection direction, OrdinalBoundary boundary, string[] expected)
    {
        var scale = new OrdinalScale(direction, DropTop: false, boundary, Order: null);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, ["90", "0", "5"], scale)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(expected, plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenNumericFreePerValueOrdinalCombos_ThenCrossesByBinMatchGeometry()
    {
        // Names alone can't catch a direction/boundary regression in incidence — assert CrossesByBin
        // (raw bin → crossed formal-attribute ids) for every combination over natural order 0,5,90.
        var domain = new[] { "90", "0", "5" }; // natural ascending → ids 0(>=/<0), 1(5), 2(90)
        AssertCrosses(OrdinalDirection.Ge, OrdinalBoundary.Inclusive, domain, zero: [0], five: [0, 1], ninety: [0, 1, 2]);
        AssertCrosses(OrdinalDirection.Ge, OrdinalBoundary.Strict, domain, zero: [], five: [0], ninety: [0, 1]);
        AssertCrosses(OrdinalDirection.Le, OrdinalBoundary.Inclusive, domain, zero: [0, 1, 2], five: [1, 2], ninety: [2]);
        AssertCrosses(OrdinalDirection.Le, OrdinalBoundary.Strict, domain, zero: [1, 2], five: [2], ninety: []);
    }

    private static void AssertCrosses(
        OrdinalDirection direction, OrdinalBoundary boundary, IReadOnlyList<string> domain,
        int[] zero, int[] five, int[] ninety)
    {
        var scale = new OrdinalScale(direction, DropTop: false, boundary, Order: null);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, domain, scale)]);
        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));
        var v = Assert.Single(plan.Attributes);

        Assert.Equal(zero, v.CrossesByBin.TryGetValue("0", out var z) ? z : []);
        Assert.Equal(five, v.CrossesByBin.TryGetValue("5", out var f) ? f : []);
        Assert.Equal(ninety, v.CrossesByBin.TryGetValue("90", out var n) ? n : []);
    }

    [Fact]
    public void Plan_WhenNumericFreePerValueNaturalOrderDiverseValues_ThenSortedByNumericValue()
    {
        // Natural ordering handles negative, zero, subnormal, fractional, and extreme/exponent values.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, Order: null);
        var domain = new[] { "1E+300", "-5", "0.5", "0", "90", "5E-324" };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, domain, scale)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        // Ascending: -5 < 0 < 5E-324 (smallest subnormal) < 0.5 < 90 < 1E+300.
        Assert.Equal(
            ["v->=-5", "v->=0", "v->=5E-324", "v->=0.5", "v->=90", "v->=1E+300"],
            plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenNumericFreePerValueOrdinalAuthorsOrder_ThenAuthoredOrderIsThePermutation()
    {
        // An authored (canonical) order is validated as a full permutation and used verbatim (§12.3).
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["90", "5", "0"]);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("v", 0, SourceValueType.Number, ["0", "5", "90"], scale)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["v->=90", "v->=5", "v->=0"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenStringFreePerValueOrdinalOmitsOrder_ThenReportsOrdinalOrderMissing()
    {
        // §12.3: string free_per_value still REQUIRES an explicit order — the natural-order exemption
        // is numeric-only (D-096). The value-bin ordinal path applies exactly as for identity.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, Order: null);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("g", 0, SourceValueType.String, ["a", "b"], scale)]);

        AssertFailsWith(Plan(spec, new SourceSchema(1)), DiagnosticCode.OrdinalOrderMissing);
    }

    [Fact]
    public void Plan_WhenStringFreePerValueOrdinalAuthorsOrder_ThenValueBinThresholds()
    {
        // String free_per_value with an explicit order behaves exactly like identity value bins.
        var scale = new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["a", "b", "c"]);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.FreePerValue("g", 0, SourceValueType.String, ["a", "b", "c"], scale)]);

        Assert.True(Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["g->=a", "g->=b", "g->=c"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    private static Binding WideWithKey(ObjectKey key) =>
        new(SourceShape.Wide, "utf-8", ',', '"', HasHeader: true, "invariant", "?", key);

    private static void AssertFailsWith(Diagnosed<ConversionPlan> result, DiagnosticCode code)
    {
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }
}
