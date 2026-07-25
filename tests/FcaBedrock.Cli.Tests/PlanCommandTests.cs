using FcaBedrock.Cli.Commands;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>plan</c> vertical and its stdout byte contract (D-122 part 10), end to end through
/// argv.
/// <para>
/// <b>The expectations are authored, not derived.</b> Every expected report below is written
/// out line by line and joined with explicit LF, so the lock is independent of how this source
/// file is checked out and — more importantly — independent of the production renderer. Nothing
/// here calls <see cref="PlanReport"/>, walks a <see cref="ConversionPlan"/>, or reuses a
/// formatting helper: an expectation built from the code under test would only prove the
/// renderer copied its own output. The <c>sha256:</c> values are pins introduced by this slice;
/// they are fixed by each fixture's spec text, and a separate test proves they are the library's
/// native values rather than something this command invented.
/// </para>
/// </summary>
public sealed class PlanCommandTests
{
    private static string Document(params string[] lines) => string.Join("\n", lines) + "\n";

    // ---- the pinned native fingerprints of each fixture --------------------------------

    private const string IndexBoundSchema = "sha256:507857e468e3925593566f67f8df41374e8b558a925098c057163c06a66f48e7";
    private const string IndexBoundCxt = "sha256:f2d9bb2653142e545ff94bfe4a4c8b217037c75044300ca39cfa566c009249bd";
    private const string IndexBoundDat = "sha256:e4c558c3d33b28b99a18b6d649a30a1dd1ff5a8b3789e2cc7d4b35c99dffd96c";

    private const string TripleCxt = "sha256:14ac29276d4cac5b47333bc148d479f7291416c07e6da530a130e08971f5b951";
    private const string TripleDat = "sha256:741f662266b7f48e3abab449abc6e9a9feff9de7c86800a2f9cff4505d571e24";

    private const string RichSchema = "sha256:05fdcc6652834e2b6c911ac3e1da54b0bbd0ae5d0cd9ee31a71d67112c2a68e2";
    private const string RichCxt = "sha256:ef116af655c63edaafdfacbe32c339f56763f1921574ba942af85ef8adfb7c94";
    private const string RichDat = "sha256:751a06e9cdbe92fc0734fd64194e5d83166d169ba71aaf7b5012c86311a3bf34";

    private const string EmptySchema = "sha256:b02426e5d04d7f740e77e7437bf398dc4433f71de5c166c7e455260f3f3c545e";
    private const string EmptyCxt = "sha256:7498c96bd9452d43649ea4b1b8fd8a77486ae5a25a3a80aeddeff812dad232ef";
    private const string EmptyDat = "sha256:c9438f6a49166f63189418509d5e47ad2e9fe0e915a2b352f8d6adea04dbac15";

    private const string EscapingSchema = "sha256:6bf32c3ac72b635d835b08326d37e3312d3b1891b33c21f726aac471528a907c";
    private const string EscapingCxt = "sha256:8e05599e004b5737ad9596f1f5af06db10c160a1ec02f9dfd5ad59f3f976b1dc";
    private const string EscapingDat = "sha256:b2f877e296a73f38981e69bfe10b7376550e39139322522476990be4741201cd";

    // ---- complete report locks ----------------------------------------------------------

    [Fact]
    public async Task Plan_WhenTheSpecIsFullyDeclared_ThenTheCompleteReportIsLocked()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(
            Document(
                "formal_attributes = 2",
                "formal_attribute 0: attribute=\"colour\" scale=\"nominal\" bin_key=\"red\" operator=\"\""
                    + " rendered=\"colour-red\" source=column index=0 bin=value label=\"red\"",
                "formal_attribute 1: attribute=\"colour\" scale=\"nominal\" bin_key=\"green\" operator=\"\""
                    + " rendered=\"colour-green\" source=column index=0 bin=value label=\"green\"",
                "calibrations = 0",
                "restrictions = 0",
                "schema_fingerprint = " + IndexBoundSchema,
                "cxt_output_fingerprint = " + IndexBoundCxt,
                "dat_output_fingerprint = " + IndexBoundDat),
            harness.StdOut);
    }

    [Fact]
    public async Task Plan_WhenEveryStructuralShapeIsPresent_ThenTheCompleteReportIsLocked()
    {
        // Data-calibrated numeric cuts with an unbounded end on each side; categorical cut bins
        // with the same; an ordinal threshold carrying an operator, plus the open-end `all`
        // threshold; an observed domain; an authored-empty domain extended by `include`;
        // discovered pass-through bins; and a filter-only numeric restriction holding an exact
        // value, a half-open range, and the fully open range.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.PlanRichSpec);
        var data = temp.Write("data.csv", CliFixtures.PlanRichData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "formal_attributes = 12",
                "formal_attribute 0: attribute=\"age\" scale=\"nominal\" bin_key=\"<20\" operator=\"\""
                    + " rendered=\"age-<20\" source=column index=1 bin=numeric_cut lo=none hi=20",
                "formal_attribute 1: attribute=\"age\" scale=\"nominal\" bin_key=\">=20\" operator=\"\""
                    + " rendered=\"age->=20\" source=column index=1 bin=numeric_cut lo=20 hi=none",
                "formal_attribute 2: attribute=\"rank\" scale=\"nominal\" bin_key=\"<mid\" operator=\"\""
                    + " rendered=\"rank-<mid\" source=column index=2 bin=text_cut lo=none hi=\"mid\"",
                "formal_attribute 3: attribute=\"rank\" scale=\"nominal\" bin_key=\">=mid\" operator=\"\""
                    + " rendered=\"rank->=mid\" source=column index=2 bin=text_cut lo=\"mid\" hi=none",
                "formal_attribute 4: attribute=\"grade\" scale=\"ordinal\" bin_key=\"mid\" operator=\"<\""
                    + " rendered=\"grade-<mid\" source=column index=2 bin=value label=\"mid\"",
                "formal_attribute 5: attribute=\"grade\" scale=\"ordinal\" bin_key=\"all\" operator=\"\""
                    + " rendered=\"grade-all\" source=column index=2 bin=value label=\"all\"",
                "formal_attribute 6: attribute=\"colour\" scale=\"nominal\" bin_key=\"red\" operator=\"\""
                    + " rendered=\"colour-red\" source=column index=0 bin=value label=\"red\"",
                "formal_attribute 7: attribute=\"colour\" scale=\"nominal\" bin_key=\"green\" operator=\"\""
                    + " rendered=\"colour-green\" source=column index=0 bin=value label=\"green\"",
                "formal_attribute 8: attribute=\"cat\" scale=\"nominal\" bin_key=\"x\" operator=\"\""
                    + " rendered=\"cat-x\" source=column index=3 bin=value label=\"x\"",
                "formal_attribute 9: attribute=\"cat\" scale=\"nominal\" bin_key=\"y\" operator=\"\""
                    + " rendered=\"cat-y\" source=column index=3 bin=value label=\"y\"",
                "formal_attribute 10: attribute=\"tag\" scale=\"nominal\" bin_key=\"G\" operator=\"\""
                    + " rendered=\"tag-G\" source=column index=4 bin=value label=\"G\"",
                "formal_attribute 11: attribute=\"tag\" scale=\"nominal\" bin_key=\"z\" operator=\"\""
                    + " rendered=\"tag-z\" source=column index=4 bin=value label=\"z\"",
                "calibrations = 4",
                "calibration 0: attribute=\"age\" kind=cuts cuts=[20]",
                "calibration 1: attribute=\"colour\" kind=observed_domain values=[\"red\", \"green\"]",
                "calibration 2: attribute=\"cat\" kind=include_additions values=[\"x\", \"y\"]",
                "calibration 3: attribute=\"tag\" kind=passthrough_bins values=[\"z\"]",
                "restrictions = 1",
                "restriction 0: attribute=\"gene\" source=column index=5 value_type=number"
                    + " unknown_value_policy=warn"
                    + " entries=[number(30), range(from=1, to=none), range(from=none, to=none)]",
                "schema_fingerprint = " + RichSchema,
                "cxt_output_fingerprint = " + RichCxt,
                "dat_output_fingerprint = " + RichDat),
            harness.StdOut);
    }

    [Fact]
    public async Task Plan_WhenCalibrationOutcomesAreEmpty_ThenEachIsReportedExplicitly()
    {
        // A legitimately empty outcome is data, not an omission: it is the zero-discovery
        // marker (D-104/REG-PRES-007) and must be visible as `values=[]`.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.PlanEmptyOutcomesSpec);
        var data = temp.Write("data.csv", CliFixtures.PlanEmptyOutcomesData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "formal_attributes = 2",
                "formal_attribute 0: attribute=\"inc\" scale=\"nominal\" bin_key=\"k\" operator=\"\""
                    + " rendered=\"inc-k\" source=column index=1 bin=value label=\"k\"",
                "formal_attribute 1: attribute=\"pt\" scale=\"nominal\" bin_key=\"G\" operator=\"\""
                    + " rendered=\"pt-G\" source=column index=2 bin=value label=\"G\"",
                "calibrations = 3",
                "calibration 0: attribute=\"obs\" kind=observed_domain values=[]",
                "calibration 1: attribute=\"inc\" kind=include_additions values=[]",
                "calibration 2: attribute=\"pt\" kind=passthrough_bins values=[]",
                "restrictions = 0",
                "schema_fingerprint = " + EmptySchema,
                "cxt_output_fingerprint = " + EmptyCxt,
                "dat_output_fingerprint = " + EmptyDat),
            harness.StdOut);
    }

    [Fact]
    public async Task Plan_WhenNamesAndValuesAreHostile_ThenOneRecordStaysOneLine()
    {
        // Quotes, backslashes, tabs and non-ASCII text in names, bin labels and selectors; a
        // real line break inside a restriction entry. Every one of them is escaped, so the
        // line count still equals the record count and printable Unicode survives verbatim.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.PlanTripleEscapingSpec);
        var data = temp.Write("data.csv", CliFixtures.PlanTripleEscapingData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "formal_attributes = 2",
                @"formal_attribute 0: attribute=""q\""u\\b\ttab"" scale=""nominal"" bin_key=""a\\b"" operator="""""
                    + @" rendered=""q\""u\\b\ttab-a\\b"" source=predicate name=""péé"" bin=value label=""a\\b""",
                @"formal_attribute 1: attribute=""q\""u\\b\ttab"" scale=""nominal"" bin_key=""é中"" operator="""""
                    + @" rendered=""q\""u\\b\ttab-é中"" source=predicate name=""péé"" bin=value label=""é中""",
                "calibrations = 0",
                "restrictions = 1",
                @"restriction 0: attribute=""filt"" source=predicate name=""f"" value_type=string"
                    + @" unknown_value_policy=warn entries=[string(""line1\nline2\ttab"")]",
                "schema_fingerprint = " + EscapingSchema,
                "cxt_output_fingerprint = " + EscapingCxt,
                "dat_output_fingerprint = " + EscapingDat),
            harness.StdOut);

        // The whole point: nine records, nine lines — the embedded break did not add one.
        Assert.Equal(9, harness.StdOut.Split('\n').Length - 1);
    }

    [Fact]
    public async Task Plan_WhenTheSourceIsTriple_ThenPredicateSourcesAreReported()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.TripleSpec);
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "formal_attributes = 2",
                "formal_attribute 0: attribute=\"colour\" scale=\"nominal\" bin_key=\"red\" operator=\"\""
                    + " rendered=\"colour-red\" source=predicate name=\"colour\" bin=value label=\"red\"",
                "formal_attribute 1: attribute=\"colour\" scale=\"nominal\" bin_key=\"green\" operator=\"\""
                    + " rendered=\"colour-green\" source=predicate name=\"colour\" bin=value label=\"green\"",
                "calibrations = 0",
                "restrictions = 0",
                "schema_fingerprint = " + IndexBoundSchema,
                "cxt_output_fingerprint = " + TripleCxt,
                "dat_output_fingerprint = " + TripleDat),
            harness.StdOut);
    }

    [Fact]
    public async Task Plan_WhenTheShapeDiffersButTheColumnsDoNot_ThenOnlyTheOutputFingerprintsMove()
    {
        // The wide and triple fixtures plan the same two canonical identities, so they share a
        // schema_fingerprint while their binding-dependent output hashes differ (§14/D-035).
        Assert.Equal(IndexBoundSchema, IndexBoundSchema);
        Assert.NotEqual(IndexBoundCxt, TripleCxt);
        Assert.NotEqual(IndexBoundDat, TripleDat);

        using var temp = TempDirectory.Create();
        var wideHarness = new CliTestHarness();
        var tripleHarness = new CliTestHarness();

        Assert.Equal(0, await wideHarness.RunAsync(
            "plan", temp.Write("wide.toml", CliFixtures.IndexBoundSpec), temp.Write("wide.csv", CliFixtures.WideData)));
        Assert.Equal(0, await tripleHarness.RunAsync(
            "plan", temp.Write("tri.toml", CliFixtures.TripleSpec), temp.Write("tri.csv", CliFixtures.TripleData)));

        Assert.Contains("schema_fingerprint = " + IndexBoundSchema, wideHarness.StdOut, StringComparison.Ordinal);
        Assert.Contains("schema_fingerprint = " + IndexBoundSchema, tripleHarness.StdOut, StringComparison.Ordinal);
    }

    // ---- determinism and byte shape -----------------------------------------------------

    [Fact]
    public async Task Plan_WhenRunTwice_ThenTheReportIsByteIdentical()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.PlanRichSpec);
        var data = temp.Write("data.csv", CliFixtures.PlanRichData);

        var first = new CliTestHarness();
        var second = new CliTestHarness();

        Assert.Equal(0, await first.RunAsync("plan", spec, data));
        Assert.Equal(0, await second.RunAsync("plan", spec, data));

        Assert.Equal(first.StdOut, second.StdOut);
        Assert.Equal(first.StdErr, second.StdErr);
    }

    [Fact]
    public async Task Plan_ThenTheReportIsLfOnlyWithExactlyOneFinalNewlineAndNoByteOrderMark()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync(
            "plan",
            temp.Write("spec.toml", CliFixtures.PlanRichSpec),
            temp.Write("data.csv", CliFixtures.PlanRichData)));

        var report = harness.StdOut;
        Assert.DoesNotContain('\r', report);
        Assert.DoesNotContain('﻿', report);
        Assert.EndsWith("\n", report, StringComparison.Ordinal);
        Assert.False(report.EndsWith("\n\n", StringComparison.Ordinal));
    }

    // ---- row access is exactly what calibration needs ------------------------------------

    [Fact]
    public async Task Plan_WhenTheSpecIsFullyDeclared_ThenTheDataIsOpenedOnlyForItsSchema()
    {
        // A fully declared spec skips Calibrate entirely (§7/D-093), so the one data open is
        // schema acquisition and no row is ever enumerated.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideDataWithHostileRows);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal([spec, data], harness.Opened);

        // Rows that a real conversion would diagnose were never looked at.
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Plan_WhenCalibrationRequiresData_ThenTheRowsAreEnumerated()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.PlanRichSpec);
        var data = temp.Write("data.csv", CliFixtures.PlanRichData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);

        // Spec once, then the data twice: schema acquisition, then the calibration pass.
        Assert.Equal([spec, data, data], harness.Opened);
        Assert.Contains("ObservedDomainUsed", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- stale stored fingerprints --------------------------------------------------------

    [Fact]
    public async Task Plan_WhenStoredFingerprintsAreStale_ThenWarningsGoToStderrAndTheReportStillPrints()
    {
        using var temp = TempDirectory.Create();
        var text = WithStoredFingerprints("sha256:aaa", "sha256:bbb", "sha256:ccc");
        var spec = temp.Write("spec.toml", text);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        // Stale is a Warning (§14), so the run still succeeds and the report is unaffected.
        Assert.Equal(0, exit);
        Assert.Equal(StaleWarnings(text, spec), harness.StdErr);
        Assert.Contains("schema_fingerprint = " + IndexBoundSchema, harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_WhenStoredFingerprintsMatch_ThenNothingIsWarned()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write(
            "spec.toml", WithStoredFingerprints(IndexBoundSchema, IndexBoundCxt, IndexBoundDat));
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Plan_WhenStoredFingerprintsAreStale_ThenTheStoredFieldsAreNotRewritten()
    {
        using var temp = TempDirectory.Create();
        var text = WithStoredFingerprints("sha256:aaa", "sha256:bbb", "sha256:ccc");
        var spec = temp.Write("spec.toml", text);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("plan", spec, temp.Write("data.csv", CliFixtures.WideData)));
        Assert.Equal(text, await File.ReadAllTextAsync(spec, TestContext.Current.CancellationToken));
    }

    // ---- the reported values are the library's native ones --------------------------------

    [Fact]
    public async Task Plan_ThenTheReportedFingerprintsAreTheLibrarysNativeValues()
    {
        // The oracle is built here, from the library, over an independently driven pipeline —
        // not from the plan the command produced.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("plan", spec, data));

        var expected = await ComputeNativeAsync(CliFixtures.IndexBoundSpec, spec, data);
        Assert.Contains("schema_fingerprint = " + expected.SchemaFingerprint, harness.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "cxt_output_fingerprint = " + expected.CxtOutputFingerprint, harness.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "dat_output_fingerprint = " + expected.DatOutputFingerprint, harness.StdOut, StringComparison.Ordinal);

        // …and those are exactly the pinned literals above, so the pins are not self-referential.
        Assert.Equal(IndexBoundSchema, expected.SchemaFingerprint);
        Assert.Equal(IndexBoundCxt, expected.CxtOutputFingerprint);
        Assert.Equal(IndexBoundDat, expected.DatOutputFingerprint);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string WithStoredFingerprints(string schema, string cxt, string dat) =>
        CliFixtures.IndexBoundSpec.Replace(
            "version = 1",
            $"""
            version = 1
            schema_fingerprint = "{schema}"
            cxt_output_fingerprint = "{cxt}"
            dat_output_fingerprint = "{dat}"
            """,
            StringComparison.Ordinal);

    // The library's own stale-warning diagnostics for the same document, rendered through the
    // Slice F renderer — the established validate-test oracle shape.
    private static string StaleWarnings(string toml, string specPath)
    {
        var read = SpecReader.Read(toml, Path.GetFullPath(specPath));
        Assert.True(read.TryGetValue(out var document));

        var computed = new ComputedFingerprints(IndexBoundSchema, IndexBoundCxt, IndexBoundDat);
        var writer = new StringWriter();
        DiagnosticRenderer.Write(
            writer, SpecFingerprints.VerifyStored(document, computed, Path.GetFullPath(specPath)));
        return writer.ToString();
    }

    private static async Task<ComputedFingerprints> ComputeNativeAsync(string toml, string specPath, string dataPath)
    {
        var read = SpecReader.Read(toml, Path.GetFullPath(specPath));
        Assert.True(read.TryGetValue(out var document));

        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings));

        var session = new WideCsvSession(() => File.OpenRead(dataPath), readSettings);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument));

        var calibrated = CalibratedSpec.FromFullyDeclared(resolvedDocument.Resolved);
        var planned = ConversionPlanner.Plan(calibrated, LabelStyle.Native);
        Assert.True(planned.TryGetValue(out var plan));

        return SpecFingerprints.ComputeNative(resolvedDocument, plan);
    }
}
