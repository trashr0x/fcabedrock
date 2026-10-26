using FcaBedrock.Cli.Commands;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>stats</c> vertical (D-122 part 10): the six-field report, the counting contract, and
/// the density rule. The expected documents are authored line by line and joined with explicit
/// LF, never produced by the code under test.
/// </summary>
public sealed class StatsCommandTests
{
    private static string Document(params string[] lines) => string.Join("\n", lines) + "\n";

    // ---- the six-field report --------------------------------------------------------

    [Fact]
    public async Task Stats_WhenTheContextIsOrdinary_ThenTheSixFieldsAreLocked()
    {
        // Two rows, two declared values, one cross each: 2 of 4 cells.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(
            Document(
                "objects = 2",
                "formal_attributes = 2",
                "crosses = 2",
                "density = 0.500000",
                "crossless_objects = 0",
                "empty_attributes = 0"),
            harness.StdOut);
    }

    [Fact]
    public async Task Stats_WhenSomeRowsAndColumnsAreEmpty_ThenBothDegenerateCountsAreReported()
    {
        // Declared red/blue over rows red/green: `green` crosses nothing (policy skip), so one
        // object is crossless, and `colour-blue` is never crossed, so one column is empty.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.StatsSparseSpec),
            temp.Write("data.csv", CliFixtures.StatsSparseData));

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "objects = 2",
                "formal_attributes = 2",
                "crosses = 1",
                "density = 0.250000",
                "crossless_objects = 1",
                "empty_attributes = 1"),
            harness.StdOut);

        // Warnings do not move the exit code off 0 and do not suppress the report.
        Assert.Contains("ObjectHasNoCrosses", harness.StdErr, StringComparison.Ordinal);
        Assert.Contains("AttributeHasNoCrosses", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_WhenThereAreNoObjects_ThenDensityIsTheZeroCellForm()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.StatsSparseSpec),
            temp.Write("data.csv", CliFixtures.HeaderOnlyWideData));

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "objects = 0",
                "formal_attributes = 2",
                "crosses = 0",
                "density = n/a (0 cells)",
                "crossless_objects = 0",
                "empty_attributes = 2"),
            harness.StdOut);
    }

    [Fact]
    public async Task Stats_WhenThereAreNoFormalAttributes_ThenDensityIsTheZeroCellForm()
    {
        // The other degenerate direction: rows exist, columns do not. The other five fields
        // stay present, exactly as §10's contract requires.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.StatsNoColumnsSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "objects = 2",
                "formal_attributes = 0",
                "crosses = 0",
                "density = n/a (0 cells)",
                "crossless_objects = 2",
                "empty_attributes = 0"),
            harness.StdOut);
        Assert.Contains("NoFormalAttributes", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_WhenTheSourceIsTriple_ThenTheSameSixFieldsAreCounted()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.TripleSpec),
            temp.Write("data.csv", CliFixtures.TripleData));

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "objects = 2",
                "formal_attributes = 2",
                "crosses = 2",
                "density = 0.500000",
                "crossless_objects = 0",
                "empty_attributes = 0"),
            harness.StdOut);
    }

    [Fact]
    public async Task Stats_ThenTheReportIsLfOnlyWithExactlyOneFinalNewlineAndNoByteOrderMark()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData)));

        var report = harness.StdOut;
        Assert.DoesNotContain('\r', report);
        Assert.DoesNotContain('﻿', report);
        Assert.EndsWith("\n", report, StringComparison.Ordinal);
        Assert.Equal(6, report.Split('\n').Length - 1);
    }

    // ---- the counts are right, checked against an independent enumeration ---------------

    [Fact]
    public async Task Stats_ThenEveryCountAgreesWithAnIndependentLibraryEnumeration()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.StatsSparseSpec);
        var data = temp.Write("data.csv", CliFixtures.StatsSparseData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("stats", spec, data));

        // Counted here, from the library, over a pipeline this test drives itself.
        var (objects, columns, crosses, crossless, empty) =
            await CountWithLibraryAsync(CliFixtures.StatsSparseSpec, spec, data);

        Assert.Contains($"objects = {objects}", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains($"formal_attributes = {columns}", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains($"crosses = {crosses}", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains($"crossless_objects = {crossless}", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains($"empty_attributes = {empty}", harness.StdOut, StringComparison.Ordinal);
    }

    // ---- exactly one counting enumeration -----------------------------------------------

    [Fact]
    public async Task Stats_WhenTheSpecIsFullyDeclared_ThenTheDataIsOpenedForSchemaAndOneEmitPass()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("stats", spec, data));

        // No calibration pass, and exactly one counting pass, never a replay for a second one.
        Assert.Equal([spec, data, data], harness.Opened);
    }

    [Fact]
    public async Task Stats_WhenCalibrationIsRequired_ThenSchemaCalibrationAndOneEmitPassAreOpened()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.PlanEmptyOutcomesSpec);
        var data = temp.Write("data.csv", CliFixtures.PlanEmptyOutcomesData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("stats", spec, data));

        Assert.Equal([spec, data, data, data], harness.Opened);
    }

    // ---- diagnostics and exits ------------------------------------------------------------

    [Fact]
    public async Task Stats_WhenAnEmitDiagnosticIsAnError_ThenNoReportIsWrittenAndExitIsOne()
    {
        // unknown_value_policy = "fail" turns the out-of-domain `green` into an Error at emit,
        // which invalidates the run, so the counts are never reported (§16.2).
        using var temp = TempDirectory.Create();
        var failing = CliFixtures.StatsSparseSpec.Replace(
            "unknown_value_policy = \"skip\"", "unknown_value_policy = \"fail\"", StringComparison.Ordinal);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", failing),
            temp.Write("data.csv", CliFixtures.StatsSparseData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("error UnknownValueObserved", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_WhenStoredFingerprintsAreStale_ThenWarningsGoToStderrAndTheReportStillPrints()
    {
        using var temp = TempDirectory.Create();
        var stale = CliFixtures.IndexBoundSpec.Replace(
            "version = 1",
            """
            version = 1
            schema_fingerprint = "sha256:aaa"
            """,
            StringComparison.Ordinal);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats", temp.Write("spec.toml", stale), temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Contains("warning SchemaFingerprintStale", harness.StdErr, StringComparison.Ordinal);
        Assert.Contains("objects = 2", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_WhenAnInfoDiagnosticIsEmitted_ThenTheReportAndExitZeroAreUnaffected()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.DedupeSpec),
            temp.Write("data.csv", CliFixtures.DedupeData));

        Assert.Equal(0, exit);
        Assert.Contains("info DuplicateObjectKey", harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(
            Document(
                "objects = 2",
                "formal_attributes = 2",
                "crosses = 3",
                "density = 0.750000",
                "crossless_objects = 0",
                "empty_attributes = 0"),
            harness.StdOut);
    }

    [Fact]
    public async Task Stats_WhenUnknownValuesNeedEscaping_ThenEachQuotedSampleCrossesTheTransportOnce()
    {
        // The library quotes each sample and escapes its backslash, quote, CR and LF; the renderer then
        // escapes the whole line once (§16.4). So a quote shows as \\", a backslash as four, an actual
        // line break as \\n beside a typed backslash-n as \\\\n, and the controls and separators the
        // library left raw as their own escapes. Each attribute repeats a value and has more than
        // three distinct ones, so the counts and the three-example bound are pinned here too.
        const string Spec = """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "a"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["ok"]

            [[attribute]]
            name = "b"
            source = { kind = "column", index = 1 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["ok"]

            [[attribute]]
            name = "c"
            source = { kind = "column", index = 2 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["ok"]
            """;
        var data =
            "a,b,c\n"
            + "\"x\"\"y\",\"l1\nl2\",l" + (char)0x2028 + "s\n"
            + "\"p,q\",l1\\nl2,p" + (char)0x2029 + "s\n"
            + "C:\\d,y" + (char)0x85 + "z,t\tt\n"
            + "\"x\"\"y\",\"l1\nl2\",l" + (char)0x2028 + "s\n"
            + "e,ok,ok\n";
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("stats", temp.Write("spec.toml", Spec), temp.Write("data.csv", data));

        Assert.Equal(0, exit);
        Assert.Equal(
            "attribute=\"a\": warning UnknownValueObserved: Attribute 'a' had 5 occurrence(s) of values that are not in "
            + "its declared domain (e.g. \"x\\\\\"y\", \"p,q\", \"C:\\\\\\\\d\").\n"
            + "attribute=\"b\": warning UnknownValueObserved: Attribute 'b' had 4 occurrence(s) of values that are not in "
            + "its declared domain (e.g. \"l1\\\\nl2\", \"l1\\\\\\\\nl2\", \"y" + Unicode("0085") + "z\").\n"
            + "attribute=\"c\": warning UnknownValueObserved: Attribute 'c' had 4 occurrence(s) of values that are not in "
            + "its declared domain (e.g. \"l" + Unicode("2028") + "s\", \"p" + Unicode("2029") + "s\", \"t\\tt\").\n"
            + "warning ObjectHasNoCrosses: 4 emitted object(s) cross no formal attribute (e.g. \"0\", \"1\", \"2\"); "
            + "their rows are empty (§16.4).\n"
            + "warning AttributeHasNoCrosses: 1 formal attribute(s) were never crossed by an emitted object (e.g. a-ok); "
            + "their columns are empty (§7/§16.4).\n",
            harness.StdErr);
    }

    [Fact]
    public async Task Stats_WhenNumericSpellingsShareAnUnknownBin_ThenTheSampleKeepsEachRawSpelling()
    {
        // 2.50, 2.5 and 2.500 are one canonical number outside the domain and 1 is in it (D-096), so the
        // report counts one column and one cross; the warning still quotes the first three spellings read.
        const string Spec = """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "v"
            source = { kind = "column", index = 0, value_type = "number" }
            discretizer = { kind = "free_per_value" }
            scale = { kind = "nominal" }
            declared_domain = ["1"]
            """;
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats", temp.Write("spec.toml", Spec), temp.Write("data.csv", "v\n2.50\n2.5\n2.500\n3.0\n2.50\n1"));

        Assert.Equal(0, exit);
        Assert.Equal(
            Document(
                "objects = 6",
                "formal_attributes = 1",
                "crosses = 1",
                "density = 0.166667",
                "crossless_objects = 5",
                "empty_attributes = 0"),
            harness.StdOut);
        Assert.Equal(
            "attribute=\"v\": warning UnknownValueObserved: Attribute 'v' had 5 occurrence(s) of values that are not in "
            + "its declared domain (e.g. \"2.50\", \"2.5\", \"2.500\").\n"
            + "warning ObjectHasNoCrosses: 5 emitted object(s) cross no formal attribute (e.g. \"0\", \"1\", \"2\"); "
            + "their rows are empty (§16.4).\n",
            harness.StdErr);
    }

    // ---- --temp-dir ------------------------------------------------------------------------

    [Fact]
    public async Task Stats_WhenATempDirectoryIsSuppliedForSpillCapableWork_ThenTheReportIsUnchangedAndNoResidueRemains()
    {
        // `dedupe` runs on the shared grouping backend (the machinery --temp-dir configures),
        // and the Info diagnostic proves the merge happened. The supplied root is
        // byte-neutral by construction (D-082); this run stays inside the production memory
        // budget, so it does not itself spill, and the forced-spill placement proof lives in
        // the Conversion suite over the same public mapping.
        using var temp = TempDirectory.Create();
        using var root = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.DedupeSpec);
        var data = temp.Write("data.csv", CliFixtures.DedupeData);

        var plain = new CliTestHarness();
        var custom = new CliTestHarness();

        Assert.Equal(0, await plain.RunAsync("stats", spec, data));
        Assert.Equal(0, await custom.RunAsync("stats", spec, data, "--temp-dir", root.Path));

        Assert.Equal(plain.StdOut, custom.StdOut);
        Assert.Equal(plain.StdErr, custom.StdErr);
        Assert.Contains("info DuplicateObjectKey", custom.StdErr, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(root.Path, "fcabedrock-spool-*"));
    }

    [Fact]
    public async Task Stats_WhenATempDirectoryIsSuppliedForTripleGrouping_ThenTheReportIsUnchanged()
    {
        using var temp = TempDirectory.Create();
        using var root = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.TripleSpec);
        var data = temp.Write("data.csv", CliFixtures.TripleData);

        var plain = new CliTestHarness();
        var custom = new CliTestHarness();

        Assert.Equal(0, await plain.RunAsync("stats", spec, data));
        Assert.Equal(0, await custom.RunAsync("stats", spec, data, "--temp-dir", root.Path));

        Assert.Equal(plain.StdOut, custom.StdOut);
        Assert.Empty(Directory.GetDirectories(root.Path, "fcabedrock-spool-*"));
    }

    [Fact]
    public async Task Plan_WhenATempDirectoryIsSuppliedForGroupingCalibration_ThenTheReportIsUnchanged()
    {
        // `equal_frequency` over interleaved triple input is count-sensitive, so the calibrator
        // takes its grouped second pass: the calibration half of what --temp-dir configures,
        // which no emit-only test would reach. `plan` never emits, so this run exercises the
        // option on the calibration path alone.
        using var temp = TempDirectory.Create();
        using var root = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.TripleCountSensitiveSpec);
        var data = temp.Write("data.csv", CliFixtures.TripleCountSensitiveData);

        var plain = new CliTestHarness();
        var custom = new CliTestHarness();

        Assert.Equal(0, await plain.RunAsync("plan", spec, data));
        Assert.Equal(0, await custom.RunAsync("plan", spec, data, "--temp-dir", root.Path));

        Assert.Equal(plain.StdOut, custom.StdOut);
        Assert.Contains("kind=cuts cuts=[30]", custom.StdOut, StringComparison.Ordinal);

        // The grouped pass really ran: schema, the raw pass, and the grouped pass.
        Assert.Equal([spec, data, data, data], custom.Opened);
        Assert.Empty(Directory.GetDirectories(root.Path, "fcabedrock-spool-*"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Stats_WhenTheTempDirectoryValueIsBlank_ThenItIsAUsageFailure(string value)
    {
        // A blank value is a mistake in the command line, so the parser refuses it as usage
        // (exit 2) rather than letting it reach ConversionRuntimeOptions as a host failure.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "stats",
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData),
            "--temp-dir",
            value);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(RunPipeline.TempDirectoryMessage) + UsageText.For("stats"),
            harness.StdErr);

        // Failing on the option alone means nothing was opened at all.
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Stats_WhenTheTempDirectoryIsUnusableButNoSpillHappens_ThenItIsNeverTouched()
    {
        // A non-blank value is used only when a spill needs storage (D-082), so a run that does
        // not spill succeeds even when the value names an ordinary file, which leaves it alone.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.DedupeSpec);
        var data = temp.Write("data.csv", CliFixtures.DedupeData);
        var blocker = temp.Write("blocked", "not a directory");

        var plain = new CliTestHarness();
        var custom = new CliTestHarness();

        Assert.Equal(0, await plain.RunAsync("stats", spec, data));
        Assert.Equal(0, await custom.RunAsync("stats", spec, data, "--temp-dir", blocker));

        Assert.Equal(plain.StdOut, custom.StdOut);
        Assert.Equal(plain.StdErr, custom.StdErr);
        Assert.Equal("not a directory", await File.ReadAllTextAsync(blocker));
    }

    // ---- the density rule, at its own seam --------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 5)]
    [InlineData(0, 5, 0)]
    public void FormatDensity_WhenThereAreNoCells_ThenItIsNotANumber(long crosses, long objects, long columns) =>
        Assert.Equal("n/a (0 cells)", StatsCommand.FormatDensity(crosses, objects, columns));

    [Theory]
    [InlineData(1, 1, 2, "0.500000")]
    [InlineData(1, 1, 4, "0.250000")]
    [InlineData(3, 2, 2, "0.750000")]
    [InlineData(4, 2, 2, "1.000000")]
    [InlineData(0, 2, 2, "0.000000")]
    public void FormatDensity_WhenTheRatioIsExact_ThenSixDigitsAreRendered(
        long crosses, long objects, long columns, string expected) =>
        Assert.Equal(expected, StatsCommand.FormatDensity(crosses, objects, columns));

    [Fact]
    public void FormatDensity_WhenTheSeventhDigitIsBelowHalf_ThenItTruncates() =>
        Assert.Equal("0.333333", StatsCommand.FormatDensity(1, 1, 3));

    [Fact]
    public void FormatDensity_WhenTheSeventhDigitIsAboveHalf_ThenItRoundsUp() =>
        Assert.Equal("0.666667", StatsCommand.FormatDensity(2, 1, 3));

    [Fact]
    public void FormatDensity_WhenTheRemainderIsExactlyHalfAndTheDigitIsEven_ThenItStays()
    {
        // 1 / 2,000,000 is exactly 0.0000005: a true midpoint, decided without any binary
        // approximation. The sixth digit is 0, which is even, so it stays.
        Assert.Equal("0.000000", StatsCommand.FormatDensity(1, 2_000, 1_000));
    }

    [Fact]
    public void FormatDensity_WhenTheRemainderIsExactlyHalfAndTheDigitIsOdd_ThenItRoundsUp()
    {
        // 3 / 2,000,000 is exactly 0.0000015; the sixth digit would be 1, which is odd, so
        // half-to-even carries it to 2.
        Assert.Equal("0.000002", StatsCommand.FormatDensity(3, 2_000, 1_000));
    }

    [Fact]
    public void FormatDensity_WhenTheCellCountExceedsSixtyFourBits_ThenItIsStillExact()
    {
        // objects × formal_attributes = 10^19, past long.MaxValue, and crosses × 10^6 = 5×10^24
        // is far past it: the whole point of the Int128 arithmetic.
        Assert.Equal("0.500000", StatsCommand.FormatDensity(5_000_000_000_000_000_000, 2_500_000_000, 4_000_000_000));
    }

    [Fact]
    public void FormatDensity_WhenTheScaledNumeratorExceedsSixtyFourBits_ThenItIsStillExact() =>
        Assert.Equal("0.500000", StatsCommand.FormatDensity(1_500_000_000_000_000_000, 3_000_000_000, 1_000_000_000));

    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, -1)]
    public void FormatDensity_WhenACountIsNegative_ThenItIsRejected(long crosses, long objects, long columns) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StatsCommand.FormatDensity(crosses, objects, columns));

    // ---- helpers -----------------------------------------------------------------------------

    private static async Task<(long Objects, int Columns, long Crosses, long Crossless, long Empty)>
        CountWithLibraryAsync(string toml, string specPath, string dataPath)
    {
        var read = SpecReader.Read(toml, Path.GetFullPath(specPath));
        Assert.True(read.TryGetValue(out var document));

        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings));

        var session = new WideCsvSession(() => File.OpenRead(dataPath), readSettings);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);

        var resolved = SpecResolver.Resolve(document, schema);
        Assert.True(resolved.TryGetValue(out var resolvedDocument));

        var source = session.Bind(resolvedDocument.Resolved);
        var calibrated = CalibratedSpec.FromFullyDeclared(resolvedDocument.Resolved);
        var planned = ConversionPlanner.Plan(calibrated, LabelStyle.Native);
        Assert.True(planned.TryGetValue(out var plan));

        var crossed = new bool[plan.FormalAttributes.Count];
        long objects = 0;
        long crosses = 0;
        long crossless = 0;

        await foreach (var emitted in Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>())
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            objects++;
            if (emitted.CrossedFormalAttributeIds.Count == 0)
            {
                crossless++;
            }

            foreach (var id in emitted.CrossedFormalAttributeIds)
            {
                crosses++;
                crossed[id] = true;
            }
        }

        return (objects, plan.FormalAttributes.Count, crosses, crossless, crossed.Count(seen => !seen));
    }

    // A backslash, u and four hex digits, built rather than typed so the expectation stays text.
    private static string Unicode(string hex) => "\\" + "u" + hex;
}
