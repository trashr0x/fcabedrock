using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

// §5 / D-082: the wide/triple emit entrypoints require the matching plan.Execution. A mismatch is a
// caller error surfaced (as InvalidOperationException) before any object is emitted.
public sealed class EmitterExecutionGuardTests
{
    [Fact]
    public async Task EmitAsync_WhenPlanIsTriple_ThenThrowsBeforeEmitting()
    {
        var plan = TriplePlan();
        var source = ConversionFixtures.SourceOver("a\n", ConversionFixtures.Wide());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>()))
            {
            }
        });
    }

    [Fact]
    public async Task EmitTripleAsync_WhenPlanIsWide_ThenThrowsBeforeEmitting()
    {
        var plan = WidePlan();
        var source = ConversionFixtures.TripleSourceOver("m0,color,red\n", ConversionFixtures.Triple());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in Emitter.EmitTripleAsync(plan, source, new List<BedrockDiagnostic>()))
            {
            }
        });
    }

    private static ConversionPlan WidePlan()
    {
        Assert.True(ConversionFixtures.PlanFor(
            new BedrockSpec(ConversionFixtures.Wide(), [ConversionFixtures.Nominal("g", 0, "b")]),
            new SourceSchema(1)).TryGetValue(out var plan));
        return plan;
    }

    private static ConversionPlan TriplePlan()
    {
        Assert.True(ConversionFixtures.PlanFor(ConversionFixtures.MushroomTripleSpec(), new SourceSchema(3))
            .TryGetValue(out var plan));
        return plan;
    }
}
