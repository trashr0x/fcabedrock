using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Core.Tests.Planning;

public sealed class ConversionPlannerTests
{
    private static readonly string[] ExpectedMushroomNames =
    [
        "bruises?", "gill-size-broad", "gill-size-narrow", "veil-type-partial",
        "veil-type-universal", "ring-number-none", "ring-number-one", "ring-number-two",
    ];

    [Fact]
    public void Plan_WhenMiniMushroom_ThenFormalAttributesMatchV2OrderAndNames()
    {
        var result = ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5));

        Assert.True(result.TryGetValue(out var plan));
        Assert.Equal(ExpectedMushroomNames, plan.FormalAttributes.Select(f => f.RenderedName).ToArray());
    }

    [Fact]
    public void Plan_WhenDichotomic_ThenSingleColumnNamedAttributeOnlyAndCrossesTrueValue()
    {
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        var bruises = plan.Attributes.Single(a => a.Name == "bruises?");
        Assert.True(bruises.KnownBins.SetEquals(["t", "f"]));
        Assert.Equal([0], bruises.CrossesByBin["t"]);
        Assert.False(bruises.CrossesByBin.ContainsKey("f")); // recognized bin that crosses nothing
    }

    [Fact]
    public void Plan_WhenNominal_ThenEachBinCrossesItsOwnFormalAttribute()
    {
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        var gill = plan.Attributes.Single(a => a.Name == "gill-size");
        Assert.Equal([1], gill.CrossesByBin["b"]);
        Assert.Equal([2], gill.CrossesByBin["n"]);
    }

    [Fact]
    public void Plan_WhenNumericCutsNominal_ThenStyleChangesInteriorNamesButNotIdentityOrCrossings()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.NumericCuts("age", 0, [30, 40, 50], new NominalScale())]);

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.Native).TryGetValue(out var native));
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.V2Compat).TryGetValue(out var v2));

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

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.Native).TryGetValue(out var native));
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1), LabelStyle.V2Compat).TryGetValue(out var v2));

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
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        Assert.DoesNotContain(plan.Attributes, a => a.Name == "class");
        Assert.All(plan.FormalAttributes, f => Assert.NotEqual("class", f.Identity.AttributeName));
    }

    [Fact]
    public void Plan_WhenCalledTwice_ThenProducesIdenticalFormalAttributeOrder()
    {
        var schema = new SourceSchema(5);

        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), schema).TryGetValue(out var first));
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), schema).TryGetValue(out var second));

        Assert.Equal(
            first.FormalAttributes.Select(f => f.RenderedName),
            second.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenDuplicateAttributeName_ThenReportsAttributeNameDuplicate()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
        [
            SpecFixtures.Nominal("dup", 0, ["a"]),
            SpecFixtures.Nominal("dup", 1, ["b"]),
        ]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(2)), DiagnosticCode.AttributeNameDuplicate);
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

        var result = ConversionPlanner.Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out var plan));
        Assert.False(result.HasErrors);
        Assert.DoesNotContain(plan.Attributes, a => a.Name == "x");
        Assert.All(plan.FormalAttributes, f => Assert.NotEqual("x", f.Identity.AttributeName));
    }

    [Fact]
    public void Plan_WhenActiveCutBasedAttributeHasDormantValueLabels_ThenIgnoredWithoutError()
    {
        // §10.8 / D-049: value_labels under a cut-based discretizer is dormant — it is
        // ignored, so a key absent from declared_domain is NOT ValueLabelKeyNotInDomain.
        var age = new AttributeSpec("age", new ColumnSource(0, SourceValueType.Number), Include: true,
            ManualCutsDiscretizer.Create([30.0, 40.0], BinEnds.Open, CultureInfo.InvariantCulture).Value!,
            new NominalScale(), DeclaredDomain: [], RestrictTo: [],
            new Dictionary<string, string> { ["old"] = "Old retained label" },
            MissingPolicy.Skip, UnknownValuePolicy.Warn);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [age]);

        var result = ConversionPlanner.Plan(spec, new SourceSchema(1));

        Assert.True(result.TryGetValue(out _));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ValueLabelKeyNotInDomain);
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

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

        Assert.Equal(["age-<30", "age->=30"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Plan_WhenValueLabelKeyNotInDomain_ThenReportsValueLabelKeyNotInDomain()
    {
        var attr = SpecFixtures.Nominal("g", 0, ["b", "n"], new Dictionary<string, string> { ["x"] = "broad" });
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(1)), DiagnosticCode.ValueLabelKeyNotInDomain);
    }

    [Fact]
    public void Plan_WhenValueLabelsPartial_ThenLabeledValueUsesLabelAndUnlabeledFallsThroughToRaw()
    {
        // §10.8 coverage: a value in declared_domain but absent from value_labels falls through
        // to the raw value as its label; nominal default naming is {column}-{value} (§10.7).
        var attr = SpecFixtures.Nominal("g", 0, ["b", "n"], new Dictionary<string, string> { ["b"] = "broad" });
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));
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

        var result = ConversionPlanner.Plan(spec, new SourceSchema(2));

        AssertFailsWith(result, DiagnosticCode.FormalAttributeNameCollision);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenDeclaredDomainHasDuplicate_ThenReportsIdentityCollision()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("a", 0, ["x", "x"])]);

        AssertFailsWith(ConversionPlanner.Plan(spec, new SourceSchema(1)), DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenNominalAsAttribute_ThenMissingAttributeAppendedAfterValueBins()
    {
        // §10.5 / D-068: nominal + as_attribute → one extra column after the value
        // bins, canonical identity (name, scale, "missing", "") (§14).
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [SpecFixtures.Nominal("a", 0, ["x", "y"], missing: MissingPolicy.AsAttribute)]);

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

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

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

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

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(1)).TryGetValue(out var plan));

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

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(2)).TryGetValue(out var plan));

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

        var result = ConversionPlanner.Plan(spec, new SourceSchema(1));

        AssertFailsWith(result, DiagnosticCode.FormalAttributeNameCollision);
        AssertFailsWith(result, DiagnosticCode.FormalAttributeCollision);
    }

    [Fact]
    public void Plan_WhenSkip_ThenMissingFormalAttributeIdIsNull()
    {
        Assert.True(ConversionPlanner.Plan(SpecFixtures.MiniMushroom(), new SourceSchema(5)).TryGetValue(out var plan));

        Assert.All(plan.Attributes, a => Assert.Null(a.MissingFormalAttributeId));
    }

    [Fact]
    public void Plan_WhenBindingShapeTriple_ThenReportsTripleSourceNotImplementedV1()
    {
        // D-072: a triple spec is a minimal reject-carrier (D-066); the guard
        // short-circuits before static validation, so this is the sole diagnostic.
        var binding = new Binding(SourceShape.Triple, ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail));
        var spec = new BedrockSpec(binding, []);

        var result = ConversionPlanner.Plan(spec, new SourceSchema(3));

        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.TripleSourceNotImplementedV1, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
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

        var result = ConversionPlanner.Plan(spec, new SourceSchema(1));

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

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(2)).TryGetValue(out var plan));
        Assert.Equal(["g-b"], plan.FormalAttributes.Select(f => f.RenderedName));
    }

    private static void AssertFailsWith(Diagnosed<ConversionPlan> result, DiagnosticCode code)
    {
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }
}
