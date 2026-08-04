namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>fingerprint</c> report vertical (D-122 part 10). The expected documents are authored
/// here and joined with explicit LF; the <c>sha256:</c> values are this slice's pins, fixed by
/// the fixture's spec text and cross-checked against the library in
/// <see cref="PlanCommandTests"/>.
/// </summary>
public sealed class FingerprintCommandTests
{
    private const string Schema = "sha256:507857e468e3925593566f67f8df41374e8b558a925098c057163c06a66f48e7";
    private const string Cxt = "sha256:f2d9bb2653142e545ff94bfe4a4c8b217037c75044300ca39cfa566c009249bd";
    private const string Dat = "sha256:e4c558c3d33b28b99a18b6d649a30a1dd1ff5a8b3789e2cc7d4b35c99dffd96c";

    private const string Absent = "absent";
    private const string Match = "match";
    private const string Stale = "stale";

    private static string Document(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Report(string schemaState, string cxtState, string datState) =>
        Document(
            $"schema_fingerprint computed={Schema} stored={schemaState}",
            $"cxt_output_fingerprint computed={Cxt} stored={cxtState}",
            $"dat_output_fingerprint computed={Dat} stored={datState}");

    // ---- the complete stored-state cross-product -----------------------------------------

    public static TheoryData<string, string, string> StateCombinations()
    {
        string[] states = [Absent, Match, Stale];
        var data = new TheoryData<string, string, string>();
        foreach (var schema in states)
        {
            foreach (var cxt in states)
            {
                foreach (var dat in states)
                {
                    data.Add(schema, cxt, dat);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StateCombinations))]
    public async Task Fingerprint_ThenEachFieldReportsItsOwnStoredStateIndependently(
        string schemaState, string cxtState, string datState)
    {
        // All 27 combinations: each field's state is decided by that field's stored value
        // alone, so no combination can leak into another.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "fingerprint",
            temp.Write("spec.toml", SpecWithStored(
                Stored(schemaState, Schema), Stored(cxtState, Cxt), Stored(datState, Dat))),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(Report(schemaState, cxtState, datState), harness.StdOut);
    }

    [Fact]
    public async Task Fingerprint_WhenNothingIsStored_ThenEveryFieldIsAbsent()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "fingerprint",
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(Report(Absent, Absent, Absent), harness.StdOut);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("sha256:")]
    [InlineData("sha256:507857e4")]
    [InlineData("SHA256:507857E468E3925593566F67F8DF41374E8B558A925098C057163C06A66F48E7")]
    [InlineData("")]
    public async Task Fingerprint_WhenAStoredValueIsMalformed_ThenItIsSimplyStale(string stored)
    {
        // Comparison is ordinal over the complete stored string, so a truncated, differently
        // cased, or nonsensical value is stale — never a parse failure (D-077).
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "fingerprint",
            temp.Write("spec.toml", SpecWithStored(stored, null, null)),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(Report(Stale, Absent, Absent), harness.StdOut);
    }

    // ---- no duplicate reporting -------------------------------------------------------------

    [Fact]
    public async Task Fingerprint_WhenStoredValuesAreStale_ThenNoStaleWarningIsAlsoEmitted()
    {
        // This report OWNS the stored-versus-computed comparison, so it must not also arrive as
        // a *FingerprintStale Warning: one fact, one place.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "fingerprint",
            temp.Write("spec.toml", SpecWithStored("sha256:aaa", "sha256:bbb", "sha256:ccc")),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(Report(Stale, Stale, Stale), harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Plan_WhenTheSameSpecIsStale_ThenTheWarningIsReported()
    {
        // The counterpart: `plan` deliberately DOES verify, so the two commands' different
        // treatments are both deliberate rather than one of them being an oversight.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "plan",
            temp.Write("spec.toml", SpecWithStored("sha256:aaa", null, null)),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Contains("warning SchemaFingerprintStale", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- root stored fields, composed computed values -----------------------------------------

    [Fact]
    public async Task Fingerprint_WhenTheSpecExtendsABase_ThenStoredFieldsComeFromTheRootAndHashesFromTheComposedPlan()
    {
        // The base holds the only attribute — so a hash can only be computed from the COMPOSED
        // plan — and a bogus stored fingerprint, which must never merge into the root (§13 rule
        // 8 / D-078). The root holds the stored fields that are reported.
        using var temp = TempDirectory.Create();
        temp.Write("base.toml", BaseWithBogusStoredFingerprint);
        var root = temp.Write("root.toml", RootExtendingBase(schema: null));
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", root, data);

        Assert.Equal(0, exit);
        Assert.Equal(Report(Absent, Absent, Absent), harness.StdOut);
    }

    [Fact]
    public async Task Fingerprint_WhenTheRootStoresTheComposedValue_ThenItMatches()
    {
        using var temp = TempDirectory.Create();
        temp.Write("base.toml", BaseWithBogusStoredFingerprint);
        var root = temp.Write("root.toml", RootExtendingBase(Schema));
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", root, data);

        Assert.Equal(0, exit);
        Assert.Equal(Report(Match, Absent, Absent), harness.StdOut);
    }

    // ---- report mode changes nothing ------------------------------------------------------------

    [Fact]
    public async Task Fingerprint_WhenReporting_ThenNothingIsWrittenAndTheSpecIsUnchanged()
    {
        using var temp = TempDirectory.Create();
        var text = SpecWithStored("sha256:aaa", null, null);
        var spec = temp.Write("spec.toml", text);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("fingerprint", spec, data));

        Assert.Equal(text, await File.ReadAllTextAsync(spec, TestContext.Current.CancellationToken));
        Assert.Equal(
            [Path.GetFileName(data), Path.GetFileName(spec)],
            Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Fingerprint_ThenTheReportIsLfOnlyWithExactlyOneFinalNewlineAndNoByteOrderMark()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync(
            "fingerprint",
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData)));

        var report = harness.StdOut;
        Assert.DoesNotContain('\r', report);
        Assert.DoesNotContain('﻿', report);
        Assert.EndsWith("\n", report, StringComparison.Ordinal);
        Assert.Equal(3, report.Split('\n').Length - 1);
    }

    // ---- the excluded override ------------------------------------------------------------------------

    [Fact]
    public async Task Fingerprint_WhenV2CompatIsSupplied_ThenItIsAUsageError()
    {
        // D-011 keeps v2 byte compatibility convert-only, so the flag does not exist here and
        // the parser — not this handler — refuses it.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "fingerprint",
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData),
            "--v2-compat");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Empty(harness.Opened);
        Assert.Contains("unknown option '--v2-compat'", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static string? Stored(string state, string value) => state switch
    {
        Absent => null,
        Match => value,
        Stale => "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown stored state."),
    };

    /// <summary>The fixture spec with each supplied stored value authored; null means the field is omitted.</summary>
    private static string SpecWithStored(string? schema, string? cxt, string? dat)
    {
        var fields = new[]
        {
            Field("schema_fingerprint", schema),
            Field("cxt_output_fingerprint", cxt),
            Field("dat_output_fingerprint", dat),
        }.Where(field => field is not null);

        return CliFixtures.IndexBoundSpec.Replace(
            "version = 1", string.Join("\n", ["version = 1", .. fields]), StringComparison.Ordinal);
    }

    private static string? Field(string key, string? value) => value is null ? null : $"{key} = \"{value}\"";

    private const string BaseWithBogusStoredFingerprint = """
        [spec]
        version = 1
        schema_fingerprint = "sha256:1111111111111111111111111111111111111111111111111111111111111111"

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]
        """;

    /// <summary>A root that inherits its only attribute from the base and stores <paramref name="schema"/>.</summary>
    private static string RootExtendingBase(string? schema) =>
        string.Join(
            "\n",
            new[]
            {
                "[spec]",
                "version = 1",
                "extends = \"base.toml\"",
                Field("schema_fingerprint", schema),
                "",
                "[binding]",
                "shape = \"wide\"",
                "has_header = true",
                "",
            }.Where(line => line is not null));
}
