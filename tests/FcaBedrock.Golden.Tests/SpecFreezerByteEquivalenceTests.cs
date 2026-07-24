using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

// SpecFreezer cross-package output equivalence (M7 Slice D / S4, D-122 part 10). For each retained
// outcome kind — and a combined spec exercising all four at once — the calibrated automatic form
// and its frozen re-resolved form must emit byte-identical .cxt and .dat in BOTH native and
// --v2-compat modes over the calibration data (D-088 generalized to every calibration outcome).
// This is the property only a package that can both calibrate (Conversion) and write (Export) can
// prove; the fingerprint self-consistency and idempotence of the frozen document are proven in
// FcaBedrock.Spec.Tests. Auto and frozen output FINGERPRINTS may legitimately differ (their
// authored discretizer configuration differs, D-094), so this suite deliberately does not compare
// them — only the emitted bytes.
public sealed class SpecFreezerByteEquivalenceTests
{
    private const string CutsSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        has_header = false
        [[attribute]]
        name = "score"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "equal_frequency", bins = 4 }
        scale = { kind = "nominal" }
        """;

    private const string CutsCsv = "10\n20\n30\n40\n50\n60\n70\n80\n";

    private const string ObservedSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        has_header = false
        [[attribute]]
        name = "color"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        """;

    private const string ObservedCsv = "red\ngreen\nred\nblue\n";

    private const string IncludeSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        has_header = false
        [[attribute]]
        name = "color"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red"]
        unknown_value_policy = "include"
        """;

    private const string IncludeCsv = "red\nblue\nred\ngreen\n";

    private const string OmittedIncludeSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        has_header = false
        [[attribute]]
        name = "color"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        unknown_value_policy = "include"
        """;

    private const string OmittedIncludeCsv = "red\nblue\nred\ngreen\n";

    private const string PassthroughSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        has_header = false
        [[attribute]]
        name = "cat"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "A", values = ["a1"] }] }
        scale = { kind = "nominal" }
        """;

    private const string PassthroughCsv = "a1\nx\ny\na1\n";

    private const string CombinedSpec = """
        [spec]
        version = 1
        [binding]
        shape = "wide"
        has_header = false
        [[attribute]]
        name = "score"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "equal_frequency", bins = 4 }
        scale = { kind = "nominal" }
        [[attribute]]
        name = "color"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        [[attribute]]
        name = "tag"
        source = { kind = "column", index = 2 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["keep"]
        unknown_value_policy = "include"
        [[attribute]]
        name = "cat"
        source = { kind = "column", index = 3 }
        discretizer = { kind = "value_groups", unmatched = "passthrough", groups = [{ label = "A", values = ["a1"] }] }
        scale = { kind = "nominal" }
        """;

    private const string CombinedCsv =
        "10,red,keep,a1\n20,green,extra,x\n30,red,keep,y\n40,blue,keep,a1\n" +
        "50,red,new,x\n60,green,keep,z\n70,red,keep,a1\n80,blue,extra,y\n";

    [Fact]
    public Task Freeze_WhenCalibratedCuts_ThenAutoAndFrozenEmitByteIdentical() =>
        AssertAutoFrozenByteIdentical(CutsSpec, CutsCsv);

    [Fact]
    public Task Freeze_WhenObservedDomain_ThenAutoAndFrozenEmitByteIdentical() =>
        AssertAutoFrozenByteIdentical(ObservedSpec, ObservedCsv);

    [Fact]
    public Task Freeze_WhenIncludeAdditions_ThenAutoAndFrozenEmitByteIdentical() =>
        AssertAutoFrozenByteIdentical(IncludeSpec, IncludeCsv);

    [Fact]
    public Task Freeze_WhenOmittedDomainWithInclude_ThenAutoAndFrozenEmitByteIdentical() =>
        AssertAutoFrozenByteIdentical(OmittedIncludeSpec, OmittedIncludeCsv);

    [Fact]
    public Task Freeze_WhenPassthroughBins_ThenAutoAndFrozenEmitByteIdentical() =>
        AssertAutoFrozenByteIdentical(PassthroughSpec, PassthroughCsv);

    [Fact]
    public Task Freeze_WhenCombined_ThenAutoAndFrozenEmitByteIdentical() =>
        AssertAutoFrozenByteIdentical(CombinedSpec, CombinedCsv);

    private static async Task AssertAutoFrozenByteIdentical(string autoToml, string csv)
    {
        var autoDocument = Read(autoToml);

        // Produce the frozen twin from a real calibration of the auto document (the paired flow).
        var (resolved, calibrated) = await ResolveAndCalibrateAsync(autoDocument, csv);
        Assert.NotEmpty(calibrated.Calibrations); // the auto spec really was data-dependent
        var frozenDocument = SpecFreezer.Freeze(resolved, calibrated);

        foreach (var (options, style) in Modes)
        {
            var auto = await ConvertBytesAsync(autoDocument, csv, options, style);
            var frozen = await ConvertBytesAsync(frozenDocument, csv, options, style);

            Assert.Equal(auto.Cxt, frozen.Cxt);   // byte equality subsumes object/cross ordering
            Assert.Equal(auto.Dat, frozen.Dat);
            Assert.Equal(
                auto.EmitDiagnostics.Select(d => d.Code),
                frozen.EmitDiagnostics.Select(d => d.Code)); // emit diagnostics unchanged
        }
    }

    private static readonly (WriterOptions Options, LabelStyle Style)[] Modes =
    [
        (WriterOptions.Native, LabelStyle.Native),
        (WriterOptions.V2Compat, LabelStyle.V2Compat),
    ];

    private static async Task<(ResolvedDocument Resolved, CalibratedSpec Calibrated)> ResolveAndCalibrateAsync(
        SpecDocument document, string csv)
    {
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));
        var session = new WideCsvSession(() => Stream(csv), readSettings);
        var schema = await session.GetSchemaAsync();
        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));
        var source = session.Bind(resolvedDocument.Resolved);
        var result = await Calibrator.CalibrateAsync(resolvedDocument.Resolved, source);
        Assert.True(result.TryGetValue(out var calibrated), Describe(result.Diagnostics));
        return (resolvedDocument, calibrated);
    }

    private static async Task<Emitted> ConvertBytesAsync(
        SpecDocument document, string csv, WriterOptions options, LabelStyle style)
    {
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));
        var session = new WideCsvSession(() => Stream(csv), readSettings);
        var schema = await session.GetSchemaAsync();
        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));

        var token = resolvedDocument.Resolved;
        var source = session.Bind(token);

        CalibratedSpec calibrated;
        if (CalibratedSpec.RequiresData(token.Spec))
        {
            var result = await Calibrator.CalibrateAsync(token, source);
            Assert.True(result.TryGetValue(out var calibratedValue), Describe(result.Diagnostics));
            calibrated = calibratedValue;
        }
        else
        {
            calibrated = CalibratedSpec.FromFullyDeclared(token);
        }

        var planned = ConversionPlanner.Plan(calibrated, style);
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));

        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit =
            sink => Emitter.EmitAsync(plan, source, sink);

        using var cxtStream = new MemoryStream();
        var cxtDiagnostics = new List<BedrockDiagnostic>();
        using (var replay = EmitReplay.Begin(emit, cxtDiagnostics))
        {
            await CxtWriter.WriteAsync(plan, replay.Open, options, cxtStream);
        }

        using var datStream = new MemoryStream();
        var datDiagnostics = new List<BedrockDiagnostic>();
        await DatWriter.WriteAsync(emit(datDiagnostics), options, datStream);

        // A valid artifact carries no Error/Fatal (G-12); the two passes see the same data.
        Assert.DoesNotContain(cxtDiagnostics, IsError);
        Assert.DoesNotContain(datDiagnostics, IsError);

        return new Emitted(cxtStream.ToArray(), datStream.ToArray(), datDiagnostics);
    }

    private static bool IsError(BedrockDiagnostic diagnostic) =>
        diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;

    private static SpecDocument Read(string toml)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        return document;
    }

    private static MemoryStream Stream(string csv) => new(Encoding.UTF8.GetBytes(csv));

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        diagnostics.Count == 0 ? "(no diagnostics)" : string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private sealed record Emitted(byte[] Cxt, byte[] Dat, IReadOnlyList<BedrockDiagnostic> EmitDiagnostics);
}
