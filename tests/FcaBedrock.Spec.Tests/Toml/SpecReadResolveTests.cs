using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Read → resolve integration (D-066/D-067): the §19 worked examples flow from
/// authored TOML through <see cref="SpecResolver"/>: §19.1 planning to the same
/// schema as the in-code document twin (EP-7 parity), §19.3 (unordered) and its
/// subject_grouped twin both resolving and planning (D-082), and the D-010
/// deferred-scale path failing at plan, not before.
/// </summary>
public sealed class SpecReadResolveTests
{
    [Fact]
    public void ReadResolve_WhenMiniMushroomToml_ThenPlansTheSameSchemaAsTheBuilderTwin()
    {
        var fromToml = ResolveOk(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var fromBuilder = SpecResolver.Resolve(DocumentFixtures.MiniMushroom());
        Assert.True(fromBuilder.TryGetValue(out var twinDoc));
        var twin = twinDoc.Resolved.Spec;

        Assert.True(Plan(fromToml, new SourceSchema(5)).TryGetValue(out var tomlPlan));
        Assert.True(Plan(twin, new SourceSchema(5)).TryGetValue(out var twinPlan));

        Assert.Equal(
            twinPlan.FormalAttributes.Select(f => f.RenderedName),
            tomlPlan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void ReadResolve_WhenMiniAdultToml_ThenResolvesAndPlansClean()
    {
        var spec = ResolveOk(TomlFixtures.MiniAdult, new SourceSchema(6));

        Assert.True(Plan(spec, new SourceSchema(6)).TryGetValue(out var plan));
        Assert.Contains(plan.FormalAttributes, f => f.RenderedName == "age-<30");
        Assert.Contains(plan.FormalAttributes, f => f.RenderedName == "US-citizen");
    }

    [Fact]
    public void ReadResolve_WhenTripleToml_ThenResolvesAndPlans()
    {
        // §19.3 uses ordering = "unordered": the triple document resolves fully (predicate
        // sources + role map + ordering) and plans cleanly. The plan is ordering-independent
        // (D-082); ordering is honored at emit. The subject_grouped twin below plans the same way.
        var spec = ResolveOk(TomlFixtures.MiniAdultTriples, schema: null);

        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.NotEmpty(spec.Attributes);

        var plan = Plan(spec, new SourceSchema(3));
        Assert.False(plan.HasErrors);
        Assert.True(plan.TryGetValue(out _));
    }

    [Fact]
    public void ReadResolve_WhenSubjectGroupedTripleToml_ThenResolvesAndPlans()
    {
        // D-082: a subject_grouped triple spec resolves and plans.
        var spec = ResolveOk(TomlFixtures.TripleSubjectGrouped, schema: null);

        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.NotEmpty(spec.Attributes);

        var plan = Plan(spec, new SourceSchema(3));
        Assert.False(plan.HasErrors);
        Assert.True(plan.TryGetValue(out var value));
        Assert.NotEmpty(value.FormalAttributes);
    }

    [Fact]
    public void ReadResolve_WhenDeferredScale_ThenResolvesToMarkerAndFailsAtPlan()
    {
        // D-010 end-to-end: parses, resolves to the Core marker, and fails at the
        // plan phase: never earlier, never silently.
        var toml =
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[[attribute]]\nname = \"g\"\nsource = { kind = \"column\", index = 0 }\n" +
            "declared_domain = [\"x\"]\n" +
            "discretizer = { kind = \"identity\" }\nscale = { kind = \"interordinal\" }\n";

        var spec = ResolveOk(toml, new SourceSchema(1));
        Assert.Equal("interordinal", Assert.IsType<UnimplementedScale>(spec.Attributes[0].Scale).Kind);

        var plan = Plan(spec, new SourceSchema(1));
        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Diagnostics, d =>
            d.Code == DiagnosticCode.ScaleNotImplementedV1 && d.Severity == DiagnosticSeverity.Fatal);
    }

    [Fact]
    public void ReadResolve_WhenBoundaryInheritedFromDefaultsOverCuts_ThenResolvesClean()
    {
        // D-060(c) end-to-end: the reader's presence tracking (Boundary stays null
        // on the scale section) is what lets the seam treat a [defaults]-inherited
        // straddling boundary as defaulted, so it never trips the cut-bin check.
        var toml =
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[defaults]\nordinal_boundary = \"strict\"\n" +
            "[[attribute]]\nname = \"age\"\nsource = { kind = \"column\", index = 0 }\n" +
            "discretizer = { kind = \"manual_cuts\", cuts = [30, 40] }\n" +
            "scale = { kind = \"ordinal\", direction = \"ge\" }\n";

        var spec = ResolveOk(toml, new SourceSchema(1));

        var scale = Assert.IsType<OrdinalScale>(spec.Attributes[0].Scale);
        Assert.Equal(OrdinalDirection.Ge, scale.Direction);
        Assert.Equal(OrdinalBoundary.Strict, scale.Boundary); // filled, but defaulted: inert over cuts
    }

    [Fact]
    public void ReadResolve_WhenBoundaryAuthoredStraddlingOverCuts_ThenFailsAtResolve()
    {
        // D-060(b): the same combination authored per-attribute is rejected at the
        // seam (spec validate, not plan).
        var toml =
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[[attribute]]\nname = \"age\"\nsource = { kind = \"column\", index = 0 }\n" +
            "discretizer = { kind = \"manual_cuts\", cuts = [30, 40] }\n" +
            "scale = { kind = \"ordinal\", direction = \"ge\", boundary = \"strict\" }\n";

        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document));

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));

        Assert.False(resolved.TryGetValue(out _));
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts);
    }

    [Fact]
    public void ReadResolve_WhenValueGroups_ThenReadsAndResolvesToAnExecutableDiscretizer()
    {
        // This EMAGE-style spec flows read → resolve and lands an executable
        // discretizer (D-104).
        var spec = ResolveOk(TomlFixtures.EmageValueGroups, new SourceSchema(1));

        var attribute = Assert.Single(spec.Attributes);
        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(attribute.Discretizer);
        Assert.Equal(ValueGroupsUnmatched.Skip, discretizer.Unmatched);
        Assert.Equal(["head"], discretizer.Groups.Select(g => g.Label));
    }

    // ---- value_type = "date": reserved, refused at plan (§10.2/§11.7, D-038) --------------
    //
    // These name the carrier and the refusal code as text, so they compile against a build that
    // has neither.

    [Theory]
    [InlineData("{ kind = \"identity\" }")]
    [InlineData("{ kind = \"free_per_value\" }")]
    [InlineData("{ kind = \"value_groups\", groups = [{ label = \"g\", values = [\"a\"] }] }")]
    [InlineData("{ kind = \"ordered_cuts\", order = [\"a\", \"b\"], cuts = [\"b\"] }")]
    [InlineData("{ kind = \"manual_cuts\", cuts = [30] }")]
    [InlineData("{ kind = \"equal_width\", bins = 4 }")]
    [InlineData("{ kind = \"equal_frequency\", bins = 4 }")]
    public void ReadResolve_WhenValueTypeIsDate_ThenItResolvesWithNoDiscretizerUnderEveryKind(string discretizer)
    {
        // v1 has no date reading, so no discretizer is kept and no value-type rule fires: identity
        // is string-fixing and manual_cuts number-fixing, yet neither reports SourceValueTypeInvalid
        // for the reserved date (§10.2).
        var spec = ResolveOk(DateSpec(DateColumn + $"discretizer = {discretizer}\nscale = {{ kind = \"nominal\" }}"), new SourceSchema(1));

        var attribute = Assert.Single(spec.Attributes);
        Assert.Equal("UnimplementedDateSource", attribute.Source.GetType().Name);
        Assert.Null(attribute.Discretizer);
        Assert.IsType<NominalScale>(attribute.Scale);
    }

    [Theory]
    [InlineData("source = { kind = \"column\", index = 5, value_type = \"date\" }\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }", DiagnosticCode.SourceBindingInvalid)]
    [InlineData("source = { kind = \"column\", name = \"born\", value_type = \"date\" }\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }", DiagnosticCode.SourceBindingInvalid)]
    [InlineData("source = { kind = \"predicate\", name = \"born\", value_type = \"date\" }\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }", DiagnosticCode.SourceBindingInvalid)]
    [InlineData(DateColumn + "discretizer = { kind = \"identity\" }", DiagnosticCode.AttributeScalingMissing)]
    [InlineData(DateColumn + "discretizer = { kind = \"manual_cuts\", cuts = [40, 30] }\nscale = { kind = \"nominal\" }", DiagnosticCode.DiscretizerCutsNotAscending)]
    [InlineData(DateColumn + "discretizer = { kind = \"manual_cuts\", cuts = [30, 40] }\nscale = { kind = \"ordinal\", order = [\"a\"] }", DiagnosticCode.OrdinalOrderNotAllowedWithCuts)]
    [InlineData(DateColumn + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\nrestrict_to = [{ value = nan }]", DiagnosticCode.RestrictToRangeInvalid)]
    [InlineData(DateColumn + "discretizer = { kind = \"identity\" }\nscale = { kind = \"ordinal\", order = [\"a\", \"a\"] }\ndeclared_domain = [\"a\"]", DiagnosticCode.OrderDomainInvalid)]
    [InlineData(DateColumn + "discretizer = { kind = \"identity\" }\nscale = { kind = \"ordinal\", order = [\"a\", \"\"] }\ndeclared_domain = [\"a\"]", DiagnosticCode.OrderDomainInvalid)]
    [InlineData(DateColumn + "discretizer = { kind = \"value_groups\", groups = [{ label = \"g\", values = [\"a\"] }] }\nscale = { kind = \"ordinal\", order = [\"g\", \"g\"] }", DiagnosticCode.OrderDomainInvalid)]
    public void Resolve_WhenValueTypeIsDate_ThenTheChecksThatDoNotTypeValuesStillReport(string body, DiagnosticCode code)
    {
        // Where the source points (§10.2) and every rule that does not depend on how a value is
        // typed or compared still apply to a date attribute (EP-14), the raw distinct and non-empty
        // scale.order check included (D-081): the date stands down only the rules that type or
        // compare values.
        var read = SpecReader.Read(DateSpec(body));
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));

        Assert.False(resolved.IsOk);
        Assert.Equal(code, Assert.Single(resolved.Diagnostics).Code);
    }

    [Theory]
    [InlineData("discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\nrestrict_to = [{ value = 3 }]")]                         // a string source: SourceValueTypeInvalid
    [InlineData("discretizer = { kind = \"manual_cuts\", cuts = [30] }\nscale = { kind = \"nominal\" }\nrestrict_to = [\"x\"]")]               // a number source: RestrictToNumericEntryRequired
    [InlineData("discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"a\"]\nvalue_labels = { b = \"B\" }")] // ValueLabelKeyNotInDomain
    [InlineData("discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"a\"]\nrestrict_to = [\"b\"]")]      // the RestrictToValueNotInDomain warning
    public void Resolve_WhenValueTypeIsDate_ThenTheChecksThatTypeOrCompareValuesStandDown(string body)
    {
        // v1 defines no date value type and no date identity, so a check that needs either has
        // nothing to judge (§10.2): each input below draws its noted diagnostic for a string or
        // number source, and nothing for a date.
        var read = SpecReader.Read(DateSpec(DateColumn + body));
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));

        Assert.True(resolved.IsOk, Describe(resolved.Diagnostics));
        Assert.Empty(resolved.Diagnostics);
    }

    [Theory]
    [InlineData("discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }")]
    [InlineData("include = false\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }")]
    [InlineData("include = false\nrestrict_to = [\"2026-01-01\"]")]
    [InlineData("discretizer = { kind = \"identity\" }\nscale = { kind = \"ordinal\", order = [\"a\", \"b\"] }")]                         // a valid order
    [InlineData("discretizer = { kind = \"identity\" }\nscale = { kind = \"ordinal\" }")]                                                    // no order
    [InlineData("include = false\ndiscretizer = { kind = \"identity\" }\nscale = { kind = \"ordinal\", order = [\"a\", \"a\"] }")]   // a parked malformed order (D-049)
    public void ReadResolve_WhenValueTypeIsDate_ThenPlanRefusesItWhetherOrNotIncluded(string body)
    {
        // §10.2/§11.7/§20: the planner refuses the reserved date. A source is live configuration on
        // an excluded attribute too (§10.1, D-076), and a filter-only one would read the column, so
        // include does not change the refusal. Nor does an ordinal order that is valid, omitted or
        // parked: the plan's order checks need a discretizer, and a date keeps none (D-049, D-081).
        var spec = ResolveOk(DateSpec(DateColumn + body), new SourceSchema(1));

        var plan = Plan(spec, new SourceSchema(1));

        Assert.False(plan.TryGetValue(out _));
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal("DateValueTypeNotImplementedV1", diagnostic.Code.ToString());
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Equal("d", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void ReadResolve_WhenATemplateSuppliesTheDateAttributesScaling_ThenItStillKeepsNoDiscretizer()
    {
        // Templates cannot carry a source (§9.1), so the date flag survives the effective-section
        // fold (D-121) unchanged: the template's discretizer is resolved and dropped, and its scale
        // is kept for the planner's refusal.
        var spec = ResolveOk(
            DateSpec(DateColumn + "template = \"t\"\n[[template]]\nid = \"t\"\n" +
                "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }"),
            new SourceSchema(1));

        var attribute = Assert.Single(spec.Attributes);
        Assert.Equal("UnimplementedDateSource", attribute.Source.GetType().Name);
        Assert.Null(attribute.Discretizer);
        Assert.IsType<NominalScale>(attribute.Scale);
    }

    private const string DateColumn = "source = { kind = \"column\", index = 0, value_type = \"date\" }\n";

    private static string DateSpec(string attributeBody) =>
        $"[spec]\nversion = 1\n[binding]\nshape = \"wide\"\nhas_header = false\n[[attribute]]\nname = \"d\"\n{attributeBody}\n";

    private static string Describe(IEnumerable<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private static BedrockSpec ResolveOk(string toml, SourceSchema? schema)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var doc),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return doc.Resolved.Spec;
    }

    // Plans a fully-declared resolved spec + schema through the fully-declared calibrated state (D-098).
    private static Diagnosed<ConversionPlan> Plan(BedrockSpec spec, SourceSchema schema) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(
            ResolvedSpec.Create(
                spec, schema,
                SourceReadSettings.Create(
                    spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                    spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
                [])));
}
