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
/// authored TOML through <see cref="SpecResolver"/> — §19.1 planning to the same
/// schema as the in-code document twin (P-7 parity), §19.3 (unordered) resolving but
/// refused at plan until Slice D with a subject_grouped twin that resolves and plans
/// (D-082), and the D-010 deferred-scale path failing at plan, not before.
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
        // sources + role map + ordering) and plans cleanly — the plan is ordering-independent
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
        // D-082: a subject_grouped triple spec resolves and plans — the blanket transitional
        // triple-conversion refusal retired at Slice C; the unordered gate retired at Slice D.
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
        // plan phase — never earlier, never silently.
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
    public void ReadResolve_WhenValueGroups_ThenReadsAndResolvesToAnExecutableDiscretizer()
    {
        // The mirror of the old deferred-kind test: D-070's last read-reject retired at Slice E
        // (D-104), so this same EMAGE-style spec now flows read → resolve and lands an executable
        // discretizer instead of stopping at read with no document.
        var spec = ResolveOk(TomlFixtures.EmageValueGroups, new SourceSchema(1));

        var attribute = Assert.Single(spec.Attributes);
        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(attribute.Discretizer);
        Assert.Equal(ValueGroupsUnmatched.Skip, discretizer.Unmatched);
        Assert.Equal(["head"], discretizer.Groups.Select(g => g.Label));
    }

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

    // Plans a fully-declared resolved spec + schema the M4 way (D-098).
    private static Diagnosed<ConversionPlan> Plan(BedrockSpec spec, SourceSchema schema) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(
            ResolvedSpec.Create(
                spec, schema,
                SourceReadSettings.Create(
                    spec.Binding.Shape, spec.Binding.Encoding, spec.Binding.Delimiter, spec.Binding.QuoteChar,
                    spec.Binding.HasHeader, spec.Binding.MissingToken, spec.Binding.Ordering),
                [])));
}
