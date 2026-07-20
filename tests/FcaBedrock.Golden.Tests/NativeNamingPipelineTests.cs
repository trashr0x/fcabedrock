using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Golden.Tests;

/// <summary>
/// The naming surface end-to-end on the <b>native</b> path (M6 Slice A, D-120):
/// small in-memory specs and data through the complete production chain — read →
/// resolve → calibrate → plan → emit → write — asserting all three fingerprints
/// <b>and</b> both <c>.cxt</c>/<c>.dat</c> byte outputs for the D-119
/// neutrality-and-change matrix rows that naming owns.
/// <para>
/// Deliberately separate from the v2 golden comparisons: those are immutable
/// evidence of what v2 produced (P-9), while <c>formal_attribute_format</c> is
/// native surface no v2 fixture uses. Asserting through the real pipeline rather
/// than the Spec-side fingerprint API is what proves the claim that matters — a
/// naming edit moves <c>.cxt</c> bytes and the cxt fingerprint <em>together</em>,
/// and leaves <c>.dat</c> alone.
/// </para>
/// </summary>
public sealed class NativeNamingPipelineTests
{
    private const string Header = "[spec]\nversion = 1\n\n[binding]\nshape = \"wide\"\nhas_header = false\n\n";
    private const string Csv = "b\nn\n";

    /// <summary>One nominal attribute over a two-value domain, plus whatever naming keys the case authors.</summary>
    private static string Spec(string namingKeys = "") =>
        Header
        + "[[attribute]]\nname = \"gill-size\"\nsource = { kind = \"column\", index = 0 }\n"
        + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
        + "declared_domain = [\"b\", \"n\"]\nvalue_labels = { b = \"broad\", n = \"narrow\" }\n"
        + namingKeys;

    // ---- The change row: an effect-changing format moves cxt only ----

    [Fact]
    public async Task Convert_WhenFormatChangesRenderedNames_ThenOnlyCxtBytesAndTheCxtFingerprintMove()
    {
        // D-117/D-119's headline row, proven on real bytes: naming reaches the .cxt names,
        // the .cxt fingerprint, and nothing else. The resolved plan is otherwise identical,
        // so schema and .dat identity must be untouched.
        var baseline = await ConvertAsync(Spec());
        var formatted = await ConvertAsync(Spec("formal_attribute_format = \"{value}\"\n"));

        Assert.NotEqual(baseline.Cxt, formatted.Cxt);
        Assert.NotEqual(baseline.Fingerprints.CxtOutputFingerprint, formatted.Fingerprints.CxtOutputFingerprint);

        Assert.Equal(baseline.Dat, formatted.Dat);
        Assert.Equal(baseline.Fingerprints.DatOutputFingerprint, formatted.Fingerprints.DatOutputFingerprint);
        Assert.Equal(baseline.Fingerprints.SchemaFingerprint, formatted.Fingerprints.SchemaFingerprint);

        // The names really did change — and through value_labels, since identity consults them.
        Assert.Contains("\nbroad\nnarrow\n", formatted.Cxt, StringComparison.Ordinal);
        Assert.Contains("\ngill-size-broad\ngill-size-narrow\n", baseline.Cxt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenFormatIsAuthoredOnDefaults_ThenItHasTheSameEffect()
    {
        // §9.2 tier 2 reaching real bytes: a spec-wide [defaults] format renders exactly
        // what the same format on the attribute would.
        var viaDefaults = await ConvertAsync(
            Header.Replace("[binding]", "[defaults]\nformal_attribute_format = \"{value}\"\n\n[binding]", StringComparison.Ordinal)
            + "[[attribute]]\nname = \"gill-size\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"b\", \"n\"]\nvalue_labels = { b = \"broad\", n = \"narrow\" }\n");
        var viaAttribute = await ConvertAsync(Spec("formal_attribute_format = \"{value}\"\n"));

        AssertIdentical(viaDefaults, viaAttribute);
    }

    [Fact]
    public async Task Convert_WhenDefaultsFormatIsInheritedThroughExtends_ThenItEqualsTheFlatEquivalent()
    {
        // D-117's composition row (the MergeDefaults extension, D-120): a base-supplied
        // [defaults] format inherited across an `extends` whose derived [defaults] authors
        // something else must change .cxt bytes exactly as the flat spec does. This is the
        // case a whole-section defaults merge would silently drop.
        var flat = await ConvertAsync(
            Header.Replace(
                "[binding]",
                "[defaults]\nunknown_value_policy = \"warn\"\nformal_attribute_format = \"{value}\"\n\n[binding]",
                StringComparison.Ordinal)
            + "[[attribute]]\nname = \"gill-size\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"b\", \"n\"]\nvalue_labels = { b = \"broad\", n = \"narrow\" }\n");

        var baseToml =
            Header.Replace("[binding]", "[defaults]\nformal_attribute_format = \"{value}\"\n\n[binding]", StringComparison.Ordinal)
            + "[[attribute]]\nname = \"gill-size\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"b\", \"n\"]\nvalue_labels = { b = \"broad\", n = \"narrow\" }\n";
        var composed = await ConvertAsync(
            "[spec]\nversion = 1\nextends = \"base.toml\"\n\n[defaults]\nunknown_value_policy = \"warn\"\n",
            baseToml);

        AssertIdentical(flat, composed);
    }

    // ---- The neutrality rows: naming that changes no rendered name is inert ----

    [Fact]
    public async Task Convert_WhenFormatRendersIdentically_ThenEveryByteAndHashIsUnchanged()
    {
        // An explicit "{column}-{value}" on a nominal attribute reproduces the scale
        // default exactly — different document, identical output (§10.7).
        var baseline = await ConvertAsync(Spec());
        var explicitDefault = await ConvertAsync(Spec("formal_attribute_format = \"{column}-{value}\"\n"));

        AssertIdentical(baseline, explicitDefault);
    }

    [Fact]
    public async Task Convert_WhenDisplayNameIsNotReferenced_ThenEveryByteAndHashIsUnchanged()
    {
        var baseline = await ConvertAsync(Spec());
        var withDisplay = await ConvertAsync(Spec("display_name = \"Gill Size\"\n"));

        AssertIdentical(baseline, withDisplay);
    }

    [Fact]
    public async Task Convert_WhenAnUnusedTemplateAuthorsNaming_ThenEveryByteAndHashIsUnchanged()
    {
        // §9.2: an unused template is dormant — it converts without touching a byte, even
        // while carrying naming keys that Slice B will make live.
        var baseline = await ConvertAsync(Spec());
        var withTemplate = await ConvertAsync(
            Spec() + "\n[[template]]\nid = \"t\"\ndisplay_name = \"T\"\nformal_attribute_format = \"{value}\"\n");

        AssertIdentical(baseline, withTemplate);
    }

    // ---- The total override and the labelled dichotomic, on real bytes ----

    [Fact]
    public async Task Convert_WhenFormatAndMissingColumn_ThenTheMissingColumnRendersThroughTheFormat()
    {
        // §10.7's total-override rule, end to end: with a format set, the missing column
        // renders through it ({value} = "missing") instead of the default "{column}-missing".
        var result = await ConvertAsync(
            Header
            + "[[attribute]]\nname = \"gill-size\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"b\", \"n\"]\nmissing_policy = \"as_attribute\"\n"
            + "formal_attribute_format = \"{column}::{value}\"\n",
            csv: "b\n?\n");

        Assert.Contains("gill-size::b\ngill-size::n\ngill-size::missing\n", result.Cxt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenLabelledDichotomic_ThenValueIsTheLabelledTrueValue()
    {
        // §10.7's worked example, converted for real: bruises? with true_value = "t"
        // labelled "bruised" renders "bruises?-bruised".
        var result = await ConvertAsync(
            Header
            + "[[attribute]]\nname = \"bruises?\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"dichotomic\", true_value = \"t\" }\n"
            + "declared_domain = [\"t\", \"f\"]\nvalue_labels = { t = \"bruised\" }\n"
            + "formal_attribute_format = \"{column}-{value}\"\n",
            csv: "t\nf\n");

        Assert.Contains("\nbruises?-bruised\n", result.Cxt, StringComparison.Ordinal);
    }

    // ---- Determinism and the failure path ----

    [Fact]
    public async Task Convert_WhenRunTwice_ThenTheArtifactsAreByteIdentical()
    {
        // P-7 through the whole naming path: same spec + same input ⇒ same bytes and hashes.
        var first = await ConvertAsync(Spec("display_name = \"Gill\"\nformal_attribute_format = \"{display_name}-{value}\"\n"));
        var second = await ConvertAsync(Spec("display_name = \"Gill\"\nformal_attribute_format = \"{display_name}-{value}\"\n"));

        AssertIdentical(first, second);
    }

    [Fact]
    public async Task Convert_WhenARenderedNameIsInvalid_ThenThePlanFailsAndNeitherOutputIsProduced()
    {
        // §10.7: the plan is shared, so an invalid rendered name blocks .dat as well as
        // .cxt. Asserted at the pipeline level — the writers are never reached, which is
        // what "exporters never sanitize" (P-15) means operationally.
        var plan = await PlanOnlyAsync(
            Header
            + "[[attribute]]\nname = \"gill-size\"\nsource = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\n"
            + "declared_domain = [\"b\"]\nvalue_labels = { b = \"\" }\n"
            + "formal_attribute_format = \"{value}\"\n");

        Assert.False(plan.IsOk);
        Assert.Null(plan.Value);
        Assert.Contains(plan.Diagnostics, d => d.Code == DiagnosticCode.FormalAttributeNameInvalid);
    }

    // ---- Harness ----

    private static void AssertIdentical(Converted expected, Converted actual)
    {
        Assert.Equal(expected.Cxt, actual.Cxt);
        Assert.Equal(expected.Dat, actual.Dat);
        Assert.Equal(expected.Fingerprints.SchemaFingerprint, actual.Fingerprints.SchemaFingerprint);
        Assert.Equal(expected.Fingerprints.CxtOutputFingerprint, actual.Fingerprints.CxtOutputFingerprint);
        Assert.Equal(expected.Fingerprints.DatOutputFingerprint, actual.Fingerprints.DatOutputFingerprint);
    }

    private sealed record Converted(string Cxt, string Dat, ComputedFingerprints Fingerprints);

    // The real production chain, exactly as GoldenConversion drives it for the fixtures:
    // bootstrap read settings → session → schema-aware resolve → bind → calibrate → plan →
    // emit → write, plus the native fingerprints over that same resolved document and plan.
    // The .cxt two-pass runs through EmitReplay (a fresh enumeration per pass) and .dat
    // streams single-pass, so both byte formats come off one prepared conversion.
    private static async Task<Converted> ConvertAsync(string toml, string? baseToml = null, string csv = Csv)
    {
        var (resolvedDocument, plan, emit) = await PrepareAsync(toml, baseToml, csv);

        using var cxtStream = new MemoryStream();
        var cxtDiagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(emit, cxtDiagnostics))
        {
            await CxtWriter.WriteAsync(plan, session.Open, WriterOptions.Native, cxtStream);
        }

        using var datStream = new MemoryStream();
        await DatWriter.WriteAsync(emit(new List<BedrockDiagnostic>()), WriterOptions.Native, datStream);

        return new Converted(
            Encoding.UTF8.GetString(cxtStream.ToArray()),
            Encoding.UTF8.GetString(datStream.ToArray()),
            SpecFingerprints.ComputeNative(resolvedDocument, plan));
    }

    private static async Task<Diagnosed<ConversionPlan>> PlanOnlyAsync(string toml, string? baseToml = null, string csv = Csv)
    {
        var (_, _, planned) = await ResolveAndPlanAsync(toml, baseToml, csv);
        return planned;
    }

    private static async Task<(ResolvedDocument Document, ConversionPlan Plan,
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> Emit)> PrepareAsync(
        string toml, string? baseToml, string csv)
    {
        var (resolvedDocument, source, planned) = await ResolveAndPlanAsync(toml, baseToml, csv);
        Assert.True(planned.TryGetValue(out var plan), Describe(planned.Diagnostics));

        return (resolvedDocument, plan, sink => Emitter.EmitAsync(plan, source, sink));
    }

    private static async Task<(ResolvedDocument Document, WideCsvSource Source, Diagnosed<ConversionPlan> Planned)>
        ResolveAndPlanAsync(string toml, string? baseToml, string csv)
    {
        var document = Compose(toml, baseToml);
        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings), Describe(settings.Diagnostics));

        var session = new WideCsvSession(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)), readSettings);
        var schema = await session.GetSchemaAsync();

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), Describe(resolved.Diagnostics));

        var token = resolvedDocument.Resolved;
        var source = session.Bind(token);
        var calibrated = CalibratedSpec.RequiresData(token.Spec)
            ? await CalibrateAsync(token, source)
            : CalibratedSpec.FromFullyDeclared(token);

        return (resolvedDocument, source, ConversionPlanner.Plan(calibrated));
    }

    private static async Task<CalibratedSpec> CalibrateAsync(ResolvedSpec token, WideCsvSource source)
    {
        var result = await Calibrator.CalibrateAsync(token, source);
        Assert.True(result.TryGetValue(out var calibrated), Describe(result.Diagnostics));
        return calibrated;
    }

    private static SpecDocument Compose(string toml, string? baseToml)
    {
        var read = SpecReader.Read(toml);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        if (baseToml is null)
        {
            return document;
        }

        var composed = SpecComposer.Compose(document, "derived.toml", new SingleBaseSource(baseToml));
        Assert.True(composed.TryGetValue(out var result), Describe(composed.Diagnostics));
        return result;
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private sealed class SingleBaseSource(string toml) : ISpecTextSource
    {
        public SpecSourceText? Load(string reference, string referrerKey) =>
            reference == "base.toml" ? new SpecSourceText(reference, toml) : null;
    }
}
