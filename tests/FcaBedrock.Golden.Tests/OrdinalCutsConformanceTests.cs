using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

/// <summary>
/// Native conformance of <c>ordinal</c> over the two authored cut discretizers
/// (§11.2/§11.8/§12.3): inline TOML and CSV through the complete read → resolve → plan → emit →
/// write chain, asserting the exact <c>.cxt</c> and <c>.dat</c> bytes, the diagnostics and the
/// fingerprints.
/// <para>
/// No v2 fixture authors closed ends, so each expectation is §12.3's rule written out for the
/// case: one threshold per bin at that bin's far edge, an open end's threshold labelled
/// <c>all</c>, and <c>drop_top</c> suppressing the one threshold that every object with a bin
/// crosses. A value outside a closed range falls in no bin and crosses nothing (§11.2).
/// </para>
/// </summary>
public sealed class OrdinalCutsConformanceTests
{
    private const string Manual = "manual";
    private const string Ordered = "ordered";

    // One value per row, no header: object names are the row indexes 0..n-1.
    private const string ManualCsv = "25\n35\n45\n55\n";
    private const string OrderedCsv = "a\nb\nc\nd\n";

    // Cuts 30/40 with closed ends: the single bin [30, 40).
    private const string SingleClosedBin = "{ kind = \"manual_cuts\", cuts = [30, 40], ends = \"closed\" }";

    // Closed cuts 30/40/50 (or b/c/d over a..e) give the two bins [30, 40) and [40, 50). Under
    // `le`, 35 crosses <40 and <50 and 45 only <50; under `ge`, 35 crosses >=30 and 45 crosses
    // >=30 and >=40. 25 and 55 (a and d) fall outside the closed range.
    private const string ManualClosedLeCxt = "B\n\n4\n2\n\n0\n1\n2\n3\nage-<40\nage-<50\n..\nXX\n.X\n..\n";
    private const string OrderedClosedLeCxt = "B\n\n4\n2\n\n0\n1\n2\n3\nage-<c\nage-<d\n..\nXX\n.X\n..\n";
    private const string ClosedLeDat = "\n1 2\n2\n\n";
    private const string ManualClosedGeCxt = "B\n\n4\n2\n\n0\n1\n2\n3\nage->=30\nage->=40\n..\nX.\nXX\n..\n";
    private const string OrderedClosedGeCxt = "B\n\n4\n2\n\n0\n1\n2\n3\nage->=b\nage->=c\n..\nX.\nXX\n..\n";
    private const string ClosedGeDat = "\n1\n1 2\n\n";

    // The schema fingerprints of the four closed specs (hashes of the canonical schema JSON,
    // computed independently), and the hash of the one-column-per-cut schema <30, <40, <50.
    private const string ManualClosedLeSchema = "sha256:0351328917b749acabce413b79d817c1e96141803b11876d1caf73f72bbd0aa0";
    private const string ManualClosedGeSchema = "sha256:5d67b7d41a0508ff8b43040732faeca0bca6b830e111998752c93cc8104ffc7d";
    private const string OrderedClosedLeSchema = "sha256:149de3cd891aa0a08f58978708f2e823ec382e5f2e4ba598181a018df0ef6bc4";
    private const string OrderedClosedGeSchema = "sha256:6432ac3214a253efd7ee937e5599b88606fde7f9d1ec73a1bf2f59527d0c83a4";
    private const string OneColumnPerCutSchema = "sha256:e0bb6c890c4f733cacad49d0dc0e47a20401b549c8d0ad54cd65a08130c76988";

    [Theory]
    [InlineData(Manual)]
    [InlineData(Ordered)]
    public async Task Convert_WhenClosedEndsAndBelow_ThenEachBinCrossesAtItsUpperEdge(string kind)
    {
        var result = await ConvertAsync(Spec(Cuts(kind, "closed"), Ordinal("le")), Csv(kind));

        Assert.Equal(kind == Manual ? ManualClosedLeCxt : OrderedClosedLeCxt, result.Cxt);
        Assert.Equal(ClosedLeDat, result.Dat);
        Assert.Empty(result.PlanDiagnostics);
        AssertOnlyObjectsWithoutCrosses(result, 2);
        Assert.Equal(kind == Manual ? ManualClosedLeSchema : OrderedClosedLeSchema, result.Fingerprints.SchemaFingerprint);
    }

    [Theory]
    [InlineData(Manual)]
    [InlineData(Ordered)]
    public async Task Convert_WhenClosedEndsAndAtOrAbove_ThenEachBinCrossesAtItsLowerEdge(string kind)
    {
        var result = await ConvertAsync(Spec(Cuts(kind, "closed"), Ordinal("ge")), Csv(kind));

        // The last cut is no bin's lower edge, so no threshold sits there and no column is
        // statically empty.
        Assert.Equal(kind == Manual ? ManualClosedGeCxt : OrderedClosedGeCxt, result.Cxt);
        Assert.Equal(ClosedGeDat, result.Dat);
        Assert.Empty(result.PlanDiagnostics);
        Assert.DoesNotContain(result.EmitDiagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        AssertOnlyObjectsWithoutCrosses(result, 2);
        Assert.Equal(kind == Manual ? ManualClosedGeSchema : OrderedClosedGeSchema, result.Fingerprints.SchemaFingerprint);
    }

    [Theory]
    [InlineData(Manual, "le")]
    [InlineData(Manual, "ge")]
    [InlineData(Ordered, "le")]
    [InlineData(Ordered, "ge")]
    public async Task Convert_WhenOpenEnds_ThenTheCumulativeColumnsAndAllAreUnchanged(string kind, string direction)
    {
        var result = await ConvertAsync(Spec(Cuts(kind, "open"), Ordinal(direction)), Csv(kind));

        // Four open bins, four columns, `all` at the open end: the output these specs already had.
        var (cxt, dat) = (kind, direction) switch
        {
            (Manual, "le") => (
                "B\n\n4\n4\n\n0\n1\n2\n3\nage-<30\nage-<40\nage-<50\nage-all\nXXXX\n.XXX\n..XX\n...X\n",
                "1 2 3 4\n2 3 4\n3 4\n4\n"),
            (Manual, "ge") => (
                "B\n\n4\n4\n\n0\n1\n2\n3\nage-all\nage->=30\nage->=40\nage->=50\nX...\nXX..\nXXX.\nXXXX\n",
                "1\n1 2\n1 2 3\n1 2 3 4\n"),
            (Ordered, "le") => (
                "B\n\n4\n4\n\n0\n1\n2\n3\nage-<b\nage-<c\nage-<d\nage-all\nXXXX\n.XXX\n..XX\n...X\n",
                "1 2 3 4\n2 3 4\n3 4\n4\n"),
            (Ordered, "ge") => (
                "B\n\n4\n4\n\n0\n1\n2\n3\nage-all\nage->=b\nage->=c\nage->=d\nX...\nXX..\nXXX.\nXXXX\n",
                "1\n1 2\n1 2 3\n1 2 3 4\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Assert.Equal(cxt, result.Cxt);
        Assert.Equal(dat, result.Dat);
        Assert.Empty(result.PlanDiagnostics);
        Assert.Empty(result.EmitDiagnostics);
    }

    [Theory]
    [InlineData(Manual, "le")]
    [InlineData(Manual, "ge")]
    [InlineData(Ordered, "le")]
    [InlineData(Ordered, "ge")]
    public async Task Convert_WhenClosedValuesSitOnTheCuts_ThenEachCutOpensTheBinAboveAndTheLastCutIsOutside(
        string kind, string direction)
    {
        // 29 and 51 (a and e) lie outside the cuts; 30 and 40 (b and c) open the bin above them;
        // 50 (d), the last cut, is outside the half-open closed range.
        var csv = kind == Manual ? "29\n30\n40\n50\n51\n" : "a\nb\nc\nd\ne\n";
        var result = await ConvertAsync(Spec(Cuts(kind, "closed"), Ordinal(direction)), csv);

        var (cxt, dat) = (kind, direction) switch
        {
            (Manual, "le") => ("B\n\n5\n2\n\n0\n1\n2\n3\n4\nage-<40\nage-<50\n..\nXX\n.X\n..\n..\n", "\n1 2\n2\n\n\n"),
            (Manual, "ge") => ("B\n\n5\n2\n\n0\n1\n2\n3\n4\nage->=30\nage->=40\n..\nX.\nXX\n..\n..\n", "\n1\n1 2\n\n\n"),
            (Ordered, "le") => ("B\n\n5\n2\n\n0\n1\n2\n3\n4\nage-<c\nage-<d\n..\nXX\n.X\n..\n..\n", "\n1 2\n2\n\n\n"),
            (Ordered, "ge") => ("B\n\n5\n2\n\n0\n1\n2\n3\n4\nage->=b\nage->=c\n..\nX.\nXX\n..\n..\n", "\n1\n1 2\n\n\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Assert.Equal(cxt, result.Cxt);
        Assert.Equal(dat, result.Dat);
        AssertOnlyObjectsWithoutCrosses(result, 3);
    }

    [Theory]
    [InlineData(Manual, "le")]
    [InlineData(Manual, "ge")]
    [InlineData(Ordered, "le")]
    [InlineData(Ordered, "ge")]
    public async Task Convert_WhenClosedEndsAndDropTop_ThenTheOutermostCutColumnIsSuppressed(string kind, string direction)
    {
        var result = await ConvertAsync(Spec(Cuts(kind, "closed"), Ordinal(direction, ", drop_top = true")), Csv(kind));

        // <50 (or <d) and >=30 (or >=b) are crossed by every object with a bin, so drop_top
        // suppresses them; the objects that crossed only that threshold now cross nothing.
        var (cxt, dat) = (kind, direction) switch
        {
            (Manual, "le") => ("B\n\n4\n1\n\n0\n1\n2\n3\nage-<40\n.\nX\n.\n.\n", "\n1\n\n\n"),
            (Manual, "ge") => ("B\n\n4\n1\n\n0\n1\n2\n3\nage->=40\n.\n.\nX\n.\n", "\n\n1\n\n"),
            (Ordered, "le") => ("B\n\n4\n1\n\n0\n1\n2\n3\nage-<c\n.\nX\n.\n.\n", "\n1\n\n\n"),
            (Ordered, "ge") => ("B\n\n4\n1\n\n0\n1\n2\n3\nage->=c\n.\n.\nX\n.\n", "\n\n1\n\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Assert.Equal(cxt, result.Cxt);
        Assert.Equal(dat, result.Dat);
        AssertOnlyObjectsWithoutCrosses(result, 3);
    }

    [Theory]
    [InlineData("le")]
    [InlineData("ge")]
    public async Task Convert_WhenASingleClosedBinDropsItsTop_ThenThereIsNoThresholdColumn(string direction)
    {
        // Cuts 30/40 with closed ends give the one bin [30, 40); its only threshold is crossed by
        // every object with a bin, so drop_top leaves no column (§16.4: a degenerate plan warns).
        var result = await ConvertAsync(
            Spec(SingleClosedBin, Ordinal(direction, ", drop_top = true")), ManualCsv);

        Assert.Equal("B\n\n4\n0\n\n0\n1\n2\n3\n\n\n\n\n", result.Cxt);
        Assert.Equal("\n\n\n\n", result.Dat);
        var noColumns = Assert.Single(result.PlanDiagnostics);
        Assert.Equal(DiagnosticCode.NoFormalAttributes, noColumns.Code);
        Assert.Equal(
            "The plan produced no formal attributes; every attribute is excluded or filter-only, or yields no column (§16.4).",
            noColumns.Message);
        AssertOnlyObjectsWithoutCrosses(result, 4);
    }

    [Fact]
    public async Task Convert_WhenASingleClosedBinDropsItsTopAndMissingIsAnAttribute_ThenOnlyTheMissingColumnRemains()
    {
        var result = await ConvertAsync(
            Spec(SingleClosedBin, Ordinal("le", ", drop_top = true"), attributeKeys: "missing_policy = \"as_attribute\"\n"),
            "25\n?\n35\n");

        Assert.Equal("B\n\n3\n1\n\n0\n1\n2\nage-missing\n.\nX\n.\n", result.Cxt);
        Assert.Equal("\n1\n\n", result.Dat);
        Assert.DoesNotContain(result.PlanDiagnostics, d => d.Code == DiagnosticCode.NoFormalAttributes);
    }

    [Fact]
    public async Task Convert_WhenAClosedSpecStoresTheHashOfOneColumnPerCut_ThenTheStoredSchemaFingerprintIsStale()
    {
        // A stored schema_fingerprint that hashes one column per cut (<30, <40, <50) is not this
        // spec's schema: the spec plans <40 and <50 (§12.3), so verification reports it stale
        // (§14), and the hash of the planned columns verifies silently.
        var stale = await ConvertAsync(
            Spec(Cuts(Manual, "closed"), Ordinal("le"), storedKeys: $"schema_fingerprint = \"{OneColumnPerCutSchema}\"\n"),
            ManualCsv);
        var current = await ConvertAsync(
            Spec(Cuts(Manual, "closed"), Ordinal("le"), storedKeys: $"schema_fingerprint = \"{ManualClosedLeSchema}\"\n"),
            ManualCsv);

        var diagnostic = Assert.Single(stale.StoredDiagnostics);
        Assert.Equal(DiagnosticCode.SchemaFingerprintStale, diagnostic.Code);
        Assert.Contains(ManualClosedLeSchema, diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(current.StoredDiagnostics);
    }

    [Fact]
    public async Task Convert_WhenAClosedSpecRunsTwice_ThenBytesAndFingerprintsAreIdentical()
    {
        // EP-7: same spec + same input give the same bytes and hashes.
        var spec = Spec(Cuts(Ordered, "closed"), Ordinal("le"));

        var first = await ConvertAsync(spec, OrderedCsv);
        var second = await ConvertAsync(spec, OrderedCsv);

        Assert.Equal(first.Cxt, second.Cxt);
        Assert.Equal(first.Dat, second.Dat);
        Assert.Equal(first.Fingerprints.SchemaFingerprint, second.Fingerprints.SchemaFingerprint);
        Assert.Equal(first.Fingerprints.CxtOutputFingerprint, second.Fingerprints.CxtOutputFingerprint);
        Assert.Equal(first.Fingerprints.DatOutputFingerprint, second.Fingerprints.DatOutputFingerprint);
    }

    [Fact]
    public async Task Convert_WhenADefaultedStrictBoundaryMeetsClosedAtOrAbove_ThenTheGeometryStillRendersAtOrAbove()
    {
        // A boundary inherited from [defaults] is defaulted, not authored, so over cut bins the
        // geometry still decides the operator (D-060): `ge` thresholds with >= at lower edges.
        var result = await ConvertAsync(
            Spec(Cuts(Manual, "closed"), Ordinal("ge"), block: "[defaults]\nordinal_boundary = \"strict\"\n\n"),
            ManualCsv);

        Assert.Equal(ManualClosedGeCxt, result.Cxt);
        Assert.Equal(ClosedGeDat, result.Dat);
    }

    [Fact]
    public async Task Convert_WhenAnAlignedBoundaryIsAuthoredOverClosedCuts_ThenItMatchesTheGeometry()
    {
        // `le` with an authored strict boundary is the pairing the half-open geometry already gives.
        var result = await ConvertAsync(
            Spec(Cuts(Ordered, "closed"), Ordinal("le", ", boundary = \"strict\"")), OrderedCsv);

        Assert.Equal(OrderedClosedLeCxt, result.Cxt);
        Assert.Equal(ClosedLeDat, result.Dat);
    }

    [Theory]
    [InlineData("le", "inclusive")]
    [InlineData("ge", "strict")]
    public async Task Resolve_WhenAnAuthoredBoundaryStraddlesClosedCuts_ThenOrdinalBoundaryIncompatibleWithCuts(
        string direction, string boundary)
    {
        var resolved = await ResolveAsync(
            Spec(Cuts(Manual, "closed"), Ordinal(direction, $", boundary = \"{boundary}\"")), ManualCsv);

        Assert.False(resolved.IsOk);
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts);
    }

    [Fact]
    public async Task Resolve_WhenATemplateSuppliesAStraddlingBoundaryOverClosedCuts_ThenOrdinalBoundaryIncompatibleWithCuts()
    {
        // A boundary a template supplies counts as authored (D-114), so the straddling `ge` +
        // strict is refused over the attribute's closed cuts just as if the attribute authored it.
        var template =
            "[[template]]\nid = \"geometry\"\nscale = { kind = \"ordinal\", direction = \"ge\", boundary = \"strict\" }\n\n"
            + "[[matcher]]\nmatch = { name_regex = \"age\" }\ntemplate = \"geometry\"\n\n";
        var resolved = await ResolveAsync(Spec(Cuts(Manual, "closed"), scale: "", block: template), ManualCsv);

        Assert.False(resolved.IsOk);
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts);
    }

    [Fact]
    public async Task Resolve_WhenAnOrderIsAuthoredOverClosedCuts_ThenOrdinalOrderNotAllowedWithCuts()
    {
        var resolved = await ResolveAsync(
            Spec(Cuts(Ordered, "closed"), Ordinal("le", ", order = [\"b\", \"c\"]")), OrderedCsv);

        Assert.False(resolved.IsOk);
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.OrdinalOrderNotAllowedWithCuts);
    }

    // ---- Spec builder ----

    // One `age` attribute over column 0. storedKeys go in [spec]; block (for [defaults],
    // [[template]] and [[matcher]]) precedes the attribute; an empty scale leaves it unauthored.
    private static string Spec(
        string discretizer, string scale, string storedKeys = "", string block = "", string attributeKeys = "") =>
        "[spec]\nversion = 1\n" + storedKeys + "\n"
        + "[binding]\nshape = \"wide\"\nhas_header = false\n\n"
        + block
        + "[[attribute]]\nname = \"age\"\nsource = { kind = \"column\", index = 0 }\n"
        + "discretizer = " + discretizer + "\n"
        + scale
        + attributeKeys;

    private static string Cuts(string kind, string ends) => kind switch
    {
        Manual => $"{{ kind = \"manual_cuts\", cuts = [30, 40, 50], ends = \"{ends}\" }}",
        Ordered => $"{{ kind = \"ordered_cuts\", order = [\"a\", \"b\", \"c\", \"d\", \"e\"], cuts = [\"b\", \"c\", \"d\"], ends = \"{ends}\" }}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string Ordinal(string direction, string extraKeys = "") =>
        $"scale = {{ kind = \"ordinal\", direction = \"{direction}\"{extraKeys} }}\n";

    private static string Csv(string kind) => kind == Manual ? ManualCsv : OrderedCsv;

    // ---- Harness ----

    // The emit diagnostics are exactly one ObjectHasNoCrosses, for the given number of objects.
    private static void AssertOnlyObjectsWithoutCrosses(Converted result, int objects)
    {
        var diagnostic = Assert.Single(result.EmitDiagnostics);
        Assert.Equal(DiagnosticCode.ObjectHasNoCrosses, diagnostic.Code);
        Assert.Contains($"{objects} emitted object(s)", diagnostic.Message, StringComparison.Ordinal);
    }

    private sealed record Converted(
        string Cxt,
        string Dat,
        IReadOnlyList<BedrockDiagnostic> PlanDiagnostics,
        IReadOnlyList<BedrockDiagnostic> EmitDiagnostics,
        ComputedFingerprints Fingerprints,
        IReadOnlyList<BedrockDiagnostic> StoredDiagnostics);

    // The real production chain, as GoldenConversion drives it for the fixtures: bootstrap read
    // settings → session → schema-aware resolve → bind → fully-declared calibrated state → plan →
    // emit → write, plus the native fingerprints over that same resolved document and plan and
    // their verification against any stored values. The .cxt two-pass runs through EmitReplay,
    // which collects the emit diagnostics once; .dat streams single-pass.
    private static async Task<Converted> ConvertAsync(string toml, string csv)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        var session = Session(document, csv);
        var resolved = SpecResolver.Resolve(document, await session.GetSchemaAsync());
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));

        var token = resolvedDocument.Resolved;
        Assert.False(CalibratedSpec.RequiresData(token.Spec)); // both cut discretizers are fully declared
        var planned = ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(token));
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));

        var source = session.Bind(token);
        var emit = (ICollection<BedrockDiagnostic> sink) => Emitter.EmitAsync(plan, source, sink);

        using var cxtStream = new MemoryStream();
        var emitDiagnostics = new List<BedrockDiagnostic>();
        using (var replay = EmitReplay.Begin(emit, emitDiagnostics))
        {
            await CxtWriter.WriteAsync(plan, replay.Open, WriterOptions.Native, cxtStream);
        }

        using var datStream = new MemoryStream();
        await DatWriter.WriteAsync(emit(new List<BedrockDiagnostic>()), WriterOptions.Native, datStream);

        var fingerprints = SpecFingerprints.ComputeNative(resolvedDocument, plan);
        return new Converted(
            Encoding.UTF8.GetString(cxtStream.ToArray()),
            Encoding.UTF8.GetString(datStream.ToArray()),
            planned.Diagnostics,
            emitDiagnostics,
            fingerprints,
            SpecFingerprints.VerifyStored(document, fingerprints));
    }

    private static async Task<Diagnosed<ResolvedDocument>> ResolveAsync(string toml, string csv)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));

        return SpecResolver.Resolve(document, await Session(document, csv).GetSchemaAsync());
    }

    private static WideCsvSession Session(SpecDocument document, string csv)
    {
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));
        return new WideCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)), readSettings);
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));
}
