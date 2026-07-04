using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Read → resolve integration (D-066/D-067): the §19 worked examples flow from
/// authored TOML through <see cref="SpecResolver"/> — §19.1 planning to the same
/// schema as the in-code document twin (P-7 parity), §19.3 resolving to the
/// D-072 triple reject-carrier, and the D-010 deferred-scale path failing at
/// plan, not before.
/// </summary>
public sealed class SpecReadResolveTests
{
    [Fact]
    public void ReadResolve_WhenMiniMushroomToml_ThenPlansTheSameSchemaAsTheBuilderTwin()
    {
        var fromToml = ResolveOk(TomlFixtures.MiniMushroom, new SourceSchema(5));
        var fromBuilder = SpecResolver.Resolve(DocumentFixtures.MiniMushroom());
        Assert.True(fromBuilder.TryGetValue(out var twin));

        Assert.True(ConversionPlanner.Plan(fromToml, new SourceSchema(5)).TryGetValue(out var tomlPlan));
        Assert.True(ConversionPlanner.Plan(twin, new SourceSchema(5)).TryGetValue(out var twinPlan));

        Assert.Equal(
            twinPlan.FormalAttributes.Select(f => f.RenderedName),
            tomlPlan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void ReadResolve_WhenMiniAdultToml_ThenResolvesAndPlansClean()
    {
        var spec = ResolveOk(TomlFixtures.MiniAdult, new SourceSchema(6));

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(6)).TryGetValue(out var plan));
        Assert.Contains(plan.FormalAttributes, f => f.RenderedName == "age-<30");
        Assert.Contains(plan.FormalAttributes, f => f.RenderedName == "US-citizen");
    }

    [Fact]
    public void ReadResolve_WhenTripleToml_ThenRejectCarrierAndPlanRefuses()
    {
        // D-072: the triple document resolves to the minimal carrier; the planner
        // owns the transitional refusal.
        var spec = ResolveOk(TomlFixtures.MiniAdultTriples, schema: null);

        Assert.Equal(SourceShape.Triple, spec.Binding.Shape);
        Assert.Empty(spec.Attributes);

        var plan = ConversionPlanner.Plan(spec, new SourceSchema(3));
        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Diagnostics, d => d.Code == DiagnosticCode.TripleSourceNotImplementedV1);
    }

    [Fact]
    public void ReadResolve_WhenDeferredScale_ThenResolvesToMarkerAndFailsAtPlan()
    {
        // D-010 end-to-end: parses, resolves to the Core marker, and fails at the
        // plan phase — never earlier, never silently.
        var toml =
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[[attribute]]\nname = \"g\"\nsource = { kind = \"column\", index = 0 }\n" +
            "declared_domain = [\"x\"]\n" +
            "discretizer = { kind = \"identity\" }\nscale = { kind = \"interordinal\" }\n";

        var spec = ResolveOk(toml, new SourceSchema(1));
        Assert.Equal("interordinal", Assert.IsType<UnimplementedScale>(spec.Attributes[0].Scale).Kind);

        var plan = ConversionPlanner.Plan(spec, new SourceSchema(1));
        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Diagnostics, d =>
            d.Code == DiagnosticCode.ScaleNotImplementedV1 && d.Severity == DiagnosticSeverity.Fatal);
    }

    [Fact]
    public void ReadResolve_WhenBoundaryInheritedFromDefaultsOverCuts_ThenResolvesClean()
    {
        // D-060(c) end-to-end: the reader's presence tracking (Boundary stays null
        // on the scale section) is what lets the seam treat a [defaults]-inherited
        // straddling boundary as defaulted — it never trips the cut-bin check.
        var toml =
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[defaults]\nordinal_boundary = \"strict\"\n" +
            "[[attribute]]\nname = \"age\"\nsource = { kind = \"column\", index = 0 }\n" +
            "discretizer = { kind = \"manual_cuts\", cuts = [30, 40] }\n" +
            "scale = { kind = \"ordinal\", direction = \"ge\" }\n";

        var spec = ResolveOk(toml, new SourceSchema(1));

        var scale = Assert.IsType<OrdinalScale>(spec.Attributes[0].Scale);
        Assert.Equal(OrdinalDirection.Ge, scale.Direction);
        Assert.Equal(OrdinalBoundary.Strict, scale.Boundary); // filled, but defaulted — inert over cuts
    }

    [Fact]
    public void ReadResolve_WhenBoundaryAuthoredStraddlingOverCuts_ThenFailsAtResolve()
    {
        // D-060(b): the same combination authored per-attribute is rejected at the
        // seam — spec validate, not plan.
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
    public void ReadResolve_WhenDeferredDiscretizerKind_ThenReadFailsBeforeResolve()
    {
        // D-070: no carrier exists, so the pipeline stops at read — there is no
        // document to resolve.
        var result = SpecReader.Read(TomlFixtures.EmageDeferredKind);

        Assert.False(result.IsOk);
        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DiscretizerKindNotYetSupported);
    }

    private static BedrockSpec ResolveOk(string toml, SourceSchema? schema)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document),
            string.Join("; ", read.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var spec),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return spec;
    }
}
