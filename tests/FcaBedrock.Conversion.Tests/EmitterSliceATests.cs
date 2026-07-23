using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

public sealed class EmitterSliceATests
{
    private static AttributeSpec Identity(
        string name, int index, IReadOnlyList<string>? domain, UnknownValuePolicy policy = UnknownValuePolicy.Warn) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(),
            new NominalScale(), domain, RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, policy);

    private static async Task<List<BedrockDiagnostic>> EmitAll(ConversionPlan plan, Sources.IRecordSource source)
    {
        var diagnostics = new List<BedrockDiagnostic>();
        await foreach (var _ in Emitter.EmitAsync(plan, source, diagnostics))
        {
        }

        return diagnostics;
    }

    [Fact]
    public async Task EmitAsync_WhenUnknownValueUnderInclude_ThenWarnsInsteadOfCrashing()
    {
        // P-22 regression (D-088 include-crash closure): a between-pass unknown reaching emit under
        // unknown_value_policy = "include" used to throw; it now degrades to an aggregated Warning.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Identity("g", 0, ["a"], UnknownValuePolicy.Include)]);
        var source = ConversionFixtures.SourceOver("a\nb", spec.Binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());
        // Zero-additions include marker → effective domain ["a"]; "b" is unknown at emit.
        var calibrated = CalibratedSpec.Create(resolved, [new IncludeAdditions("g", [])]).Value!;
        var plan = ConversionPlanner.Plan(calibrated).Value!;

        var diagnostics = await EmitAll(plan, source);

        var warning = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task EmitAsync_WhenLiveSchemaDiffersFromResolution_ThenThrows()
    {
        // A reordered same-arity header is caught at enumeration start by the emit pairing guard (D-098).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: true), [Identity("g", 1, ["a"])]);
        var resolved = ConversionFixtures.ResolveFor(spec, new SourceSchema(2, ["id", "g"]));
        var plan = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolved)).Value!;
        // The live source header is reordered relative to the resolution's schema.
        var reordered = ConversionFixtures.SourceOver("g,id\na,1", spec.Binding);

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmitAll(plan, reordered));
    }

    [Fact]
    public async Task Calibrate_ThenPlan_WhenBothPhasesDiagnose_ThenBothSurviveInPhaseOrder()
    {
        // Cross-phase composition (D-098): an absent-domain identity attribute warns at calibrate
        // (ObservedDomainUsed) while a sibling's deferred scale rejects at plan
        // (ScaleNotImplementedV1); appended in phase order, both survive.
        //
        // The plan-side code was RestrictToNotImplementedV1 until Slice F retired it (D-105);
        // restrict_to now executes, so it is no longer a plan diagnostic at all. The deferred
        // scale is a permanent v1 reservation (D-010) and carries the same shape of proof: a
        // plan-phase diagnostic co-firing with a calibrate-phase one.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [
                Identity("g", 0, null),
                Identity("h", 1, ["x"]) with { Scale = new UnimplementedScale("interordinal") },
            ]);
        var source = ConversionFixtures.SourceOver("a,x\nb,x", spec.Binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());

        var calibrated = await Calibrator.CalibrateAsync(resolved, source);
        Assert.True(calibrated.TryGetValue(out var calibratedSpec));
        var planned = ConversionPlanner.Plan(calibratedSpec);

        var combined = calibrated.Diagnostics.Concat(planned.Diagnostics).ToList();
        Assert.Collection(
            combined,
            d => Assert.Equal(DiagnosticCode.ObservedDomainUsed, d.Code),
            d => Assert.Equal(DiagnosticCode.ScaleNotImplementedV1, d.Code));
    }
}
