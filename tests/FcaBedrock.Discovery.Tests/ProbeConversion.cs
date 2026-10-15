using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>What converting a draft over its probed source produced.</summary>
/// <param name="Columns">The planned formal-attribute names, in column order.</param>
/// <param name="Objects">Each emitted object with the names of the columns it crosses.</param>
/// <param name="Diagnostics">Every calibrate, plan and emit diagnostic, in order.</param>
/// <param name="Fingerprints">The three native fingerprints of the converted plan.</param>
internal sealed record ConvertedDraft(
    IReadOnlyList<string> Columns,
    IReadOnlyList<(string Name, IReadOnlyList<string> Crosses)> Objects,
    IReadOnlyList<BedrockDiagnostic> Diagnostics,
    ComputedFingerprints Fingerprints);

/// <summary>
/// Converts a probe draft over the source it was probed from, through the strict reread and the
/// ordinary conversion pipeline: the draft's guarantee legs 2 to 4 (D-107). A test-only route,
/// because Discovery may not reference Conversion (D-109). Every leg must finish with no Error or
/// Fatal; anything else fails the calling test here.
/// </summary>
internal static class ProbeConversion
{
    public static async Task<ConvertedDraft> ConvertAsync(SpecDocument draft, Func<Stream> open)
    {
        var toml = SpecWriter.Write(draft);
        var reread = SpecReader.Read(toml);
        Assert.True(reread.TryGetValue(out var document), ProbeFixtures.Describe(reread.Diagnostics));
        Assert.Equal(toml, SpecWriter.Write(document));

        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), ProbeFixtures.Describe(settings.Diagnostics));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<(string, IReadOnlyList<string>)>();
        ConversionPlan plan;
        ResolvedDocument resolvedDocument;
        if (readSettings.Shape == SourceShape.Triple)
        {
            var session = new TripleCsvSession(open, readSettings);
            var resolved = SpecResolver.Resolve(document, await session.GetSchemaAsync());
            Assert.True(resolved.TryGetValue(out resolvedDocument!), ProbeFixtures.Describe(resolved.Diagnostics));
            var source = session.Bind(resolvedDocument.Resolved);
            var calibrated = await Calibrator.CalibrateTripleAsync(resolvedDocument.Resolved, source);
            plan = Plan(calibrated, diagnostics);
            var emit = new List<BedrockDiagnostic>();
            await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, emit))
            {
                objects.Add((emitted.Name, Names(plan, emitted)));
            }

            diagnostics.AddRange(emit);
        }
        else
        {
            var session = new WideCsvSession(open, readSettings);
            var resolved = SpecResolver.Resolve(document, await session.GetSchemaAsync());
            Assert.True(resolved.TryGetValue(out resolvedDocument!), ProbeFixtures.Describe(resolved.Diagnostics));
            var source = session.Bind(resolvedDocument.Resolved);
            var calibrated = await Calibrator.CalibrateAsync(resolvedDocument.Resolved, source);
            plan = Plan(calibrated, diagnostics);
            var emit = new List<BedrockDiagnostic>();
            await foreach (var emitted in Emitter.EmitAsync(plan, source, emit))
            {
                objects.Add((emitted.Name, Names(plan, emitted)));
            }

            diagnostics.AddRange(emit);
        }

        Assert.DoesNotContain(diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal);
        return new ConvertedDraft(
            [.. plan.FormalAttributes.Select(a => a.RenderedName)],
            objects,
            diagnostics,
            SpecFingerprints.ComputeNative(resolvedDocument, plan));
    }

    private static ConversionPlan Plan(Diagnosed<CalibratedSpec> calibrated, List<BedrockDiagnostic> diagnostics)
    {
        diagnostics.AddRange(calibrated.Diagnostics);
        Assert.True(calibrated.TryGetValue(out var spec), ProbeFixtures.Describe(calibrated.Diagnostics));
        var planned = ConversionPlanner.Plan(spec, LabelStyle.Native);
        diagnostics.AddRange(planned.Diagnostics);
        Assert.True(planned.TryGetValue(out var plan), ProbeFixtures.Describe(planned.Diagnostics));
        return plan;
    }

    private static string[] Names(ConversionPlan plan, EmittedObject emitted) =>
        [.. emitted.CrossedFormalAttributeIds.Select(id => plan.FormalAttributes[id].RenderedName)];
}
