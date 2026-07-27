using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The §15 run manifest, end to end through argv (D-122 part 6).
/// <para>
/// <b>The expected documents below are authored by hand.</b> Nothing here calls
/// <c>RunManifestWriter</c>, the CLI's manifest composer, the CLI's hash helper, or the
/// transaction record writer to build an expectation: the text is written out literally, and
/// every hash is recomputed in this file straight from the bytes on disk with
/// <see cref="SHA256"/>. The three fingerprints are pinned constants — byte locks over the
/// fixture spec, which is exactly what makes an encoder change fail here.
/// </para>
/// </summary>
public sealed class ConvertManifestTests
{
    // Byte locks for CliFixtures.IndexBoundSpec resolved against CliFixtures.WideData. A change
    // to the fixture, the plan, or the canonical fingerprint encoding must move these.
    private const string IndexBoundSchemaFingerprint =
        "sha256:507857e468e3925593566f67f8df41374e8b558a925098c057163c06a66f48e7";
    private const string IndexBoundCxtFingerprint =
        "sha256:f2d9bb2653142e545ff94bfe4a4c8b217037c75044300ca39cfa566c009249bd";
    private const string IndexBoundDatFingerprint =
        "sha256:e4c558c3d33b28b99a18b6d649a30a1dd1ff5a8b3789e2cc7d4b35c99dffd96c";

    // ---- the complete document ----------------------------------------------------------------

    [Fact]
    public async Task Manifest_WhenBothFormatsAreWritten_ThenTheDocumentIsExactlyTheSpecifiedBytes()
    {
        using var run = ConvertRun.Wide();

        // A non-`fcabedrock` argv[0], a control character, a non-ASCII member, and an empty one:
        // command_line is an audit record of what the process received, verbatim (CX-M7P-012).
        run.Harness.AuditArgv = ["C:\\tools\\fcabedrock.exe", "convert", "a\tb", "é中", string.Empty];
        run.Harness.ToolVersion = "fcabedrock-vnext 9.9.9-test";
        run.Harness.Clock.UtcNow = new DateTimeOffset(2026, 7, 25, 12, 34, 56, TimeSpan.Zero);

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var expected = $"""
            [run]
            tool_version = "fcabedrock-vnext 9.9.9-test"
            timestamp = 2026-07-25T12:34:56Z
            command_line = ["C:\\tools\\fcabedrock.exe", "convert", "a\tb", "é中", ""]
            spec_path = "{Escape(run.Spec)}"
            spec_file_hash = "{Hash(run.Spec)}"
            schema_fingerprint = "{IndexBoundSchemaFingerprint}"
            cxt_output_fingerprint = "{IndexBoundCxtFingerprint}"
            dat_output_fingerprint = "{IndexBoundDatFingerprint}"
            input_path = "{Escape(run.Data)}"
            input_hash = "{Hash(run.Data)}"

            [[run.outputs]]
            format = "cxt"
            path = "{Escape(run.Target(".cxt"))}"
            hash = "{Hash(run.Target(".cxt"))}"

            [[run.outputs]]
            format = "dat"
            path = "{Escape(run.Target(".dat"))}"
            hash = "{Hash(run.Target(".dat"))}"

            """.ReplaceLineEndings("\n");

        Assert.Equal(expected, run.Text(".manifest.toml"));
    }

    [Fact]
    public async Task Manifest_WhenItIsCommitted_ThenItIsUtf8WithoutABomAndLfOnly()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var bytes = run.Bytes(".manifest.toml");
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    [Theory]
    [InlineData("cxt", true, false)]
    [InlineData("dat", false, true)]
    [InlineData("both", true, true)]
    public async Task Manifest_WhenAFormatIsNotWritten_ThenItsFingerprintAndOutputAreAbsent(
        string format, bool cxt, bool dat)
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", format));

        var manifest = run.Text(".manifest.toml");
        Assert.Equal(cxt, manifest.Contains("cxt_output_fingerprint", StringComparison.Ordinal));
        Assert.Equal(dat, manifest.Contains("dat_output_fingerprint", StringComparison.Ordinal));
        Assert.Equal(cxt, manifest.Contains("format = \"cxt\"", StringComparison.Ordinal));
        Assert.Equal(dat, manifest.Contains("format = \"dat\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifest_WhenNoManifestIsRequested_ThenNoSidecarIsCommitted()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both", "--no-manifest"));

        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
    }

    [Fact]
    public async Task Manifest_WhenTheOutputHashesAreRecorded_ThenTheyMatchTheCommittedBytes()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var manifest = run.Text(".manifest.toml");
        Assert.Contains($"hash = \"{Hash(run.Target(".cxt"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{Hash(run.Target(".dat"))}\"", manifest, StringComparison.Ordinal);
    }

    // ---- calibrations ---------------------------------------------------------------------------

    [Fact]
    public async Task Manifest_WhenEveryCalibrationKindIsRetained_ThenEachIsRecordedInAttributeOrder()
    {
        // PlanRichSpec retains all four kinds: data-derived equal_width cuts (age), an observed
        // domain (colour), include additions over an authored-empty domain (cat), and discovered
        // pass-through bins (tag). The cuts entry's `discretizer` is the AUTHORED kind, which no
        // amount of staring at the cut values could recover.
        using var run = ConvertRun.Wide(CliFixtures.PlanRichSpec, CliFixtures.PlanRichData);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Contains(
            """
            [[run.calibrations]]
            attribute = "age"
            kind = "cuts"
            discretizer = "equal_width"
            cuts = [20]

            [[run.calibrations]]
            attribute = "colour"
            kind = "observed_domain"
            values = ["red", "green"]

            [[run.calibrations]]
            attribute = "cat"
            kind = "include_additions"
            values = ["x", "y"]

            [[run.calibrations]]
            attribute = "tag"
            kind = "passthrough_bins"
            values = ["z"]
            """.ReplaceLineEndings("\n"),
            run.Text(".manifest.toml"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenAnOutcomeDiscoveredNothing_ThenItIsAnExplicitEmptyArray()
    {
        // REG-PRES-007: a legitimate zero-discovery outcome is recorded, not dropped — the
        // difference between "calibrated and found nothing" and "never calibrated".
        using var run = ConvertRun.Wide(CliFixtures.PlanEmptyOutcomesSpec, CliFixtures.PlanEmptyOutcomesData);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Contains(
            """
            [[run.calibrations]]
            attribute = "obs"
            kind = "observed_domain"
            values = []

            [[run.calibrations]]
            attribute = "inc"
            kind = "include_additions"
            values = []

            [[run.calibrations]]
            attribute = "pt"
            kind = "passthrough_bins"
            values = []
            """.ReplaceLineEndings("\n"),
            run.Text(".manifest.toml"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenNothingWasCalibrated_ThenThereIsNoCalibrationsFamily()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.DoesNotContain("[[run.calibrations]]", run.Text(".manifest.toml"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenAnObservedDomainIsLong_ThenOnlyItsValuesArrayWraps()
    {
        // D-113: long non-cut `values` arrays wrap; `command_line` and `cuts` never do.
        var values = new StringBuilder();
        var data = new StringBuilder("colour\n");
        for (var i = 0; i < 40; i++)
        {
            values.Append("value-").Append(i).Append('\n');
            data.Append("value-").Append(i).Append('\n');
        }

        using var run = ConvertRun.Wide(ObservedDomainSpec, data.ToString());

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var manifest = run.Text(".manifest.toml");
        Assert.Contains("values = [\n  \"value-0\",\n", manifest, StringComparison.Ordinal);
        Assert.Contains("\n]\n", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("command_line = [\n", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenValuesCarryAwkwardText_ThenTheyAreEscapedNotRewritten()
    {
        using var run = ConvertRun.Wide(ObservedDomainSpec, "colour\na\\b\né中\n");

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Contains(
            "values = [\"a\\\\b\", \"é中\"]", run.Text(".manifest.toml"), StringComparison.Ordinal);
    }

    // ---- the extends chain ------------------------------------------------------------------------

    [Fact]
    public async Task Manifest_WhenTheSpecIsASingleFile_ThenThereIsNoSpecFilesFamily()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.DoesNotContain("[[run.spec_files]]", run.Text(".manifest.toml"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenTheSpecIsAMultilevelChain_ThenEveryFileIsRecordedRootFirstAsAuthored()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var grandparent = temp.Write("base/grand.toml", CliFixtures.IndexBoundSpec);
        var parent = temp.Write("base/mid.toml", "[spec]\nversion = 1\nextends = \"grand.toml\"\n");
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"base/mid.toml\"\n");
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out");

        var exit = await harness.RunAsync("convert", root, data, "--out", basePath, "--format", "cxt");

        Assert.Equal(0, exit);

        // Root first, then bases, each with the spelling that named it — the root operand and
        // the authored referrer-relative references — never a normalized or absolutized path.
        Assert.Contains(
            $"""
            [[run.spec_files]]
            path = "{Escape(root)}"
            hash = "{Hash(root)}"

            [[run.spec_files]]
            path = "base/mid.toml"
            hash = "{Hash(parent)}"

            [[run.spec_files]]
            path = "grand.toml"
            hash = "{Hash(grandparent)}"
            """.ReplaceLineEndings("\n"),
            File.ReadAllText(basePath + ".manifest.toml"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenTheSpecCarriesAByteOrderMarkAndCrlf_ThenTheHashCoversTheRawBytes()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var specPath = Path.Combine(temp.Path, "spec.toml");
        var raw = new List<byte> { 0xEF, 0xBB, 0xBF };
        raw.AddRange(Encoding.UTF8.GetBytes(CliFixtures.IndexBoundSpec.ReplaceLineEndings("\r\n")));
        await File.WriteAllBytesAsync(specPath, raw.ToArray());

        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out");

        var exit = await harness.RunAsync("convert", specPath, data, "--out", basePath, "--format", "cxt");

        Assert.Equal(0, exit);
        Assert.Contains(
            $"spec_file_hash = \"{Hash(specPath)}\"",
            File.ReadAllText(basePath + ".manifest.toml"),
            StringComparison.Ordinal);
    }

    // ---- native versus effective ---------------------------------------------------------------

    [Fact]
    public async Task Manifest_WhenTheRunIsNative_ThenItsFingerprintsAreTheOnesTheReportComputes()
    {
        using var run = ConvertRun.Wide();
        var report = new CliTestHarness();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        Assert.Equal(0, await report.RunAsync("fingerprint", run.Spec, run.Data));

        var manifest = run.Text(".manifest.toml");
        foreach (var field in Reported(report.StdOut))
        {
            Assert.Contains($"{field.Key} = \"{field.Value}\"", manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Manifest_WhenTheRunIsV2Compatible_ThenOnlyTheOutputFingerprintsBecomeEffective()
    {
        // §14/§15: the schema fingerprint is style-independent and stays the native one, while the
        // two output fingerprints are the effective values the override produced — which is why
        // they are a manifest fact and never a stored one.
        using var run = ConvertRun.Wide();
        var report = new CliTestHarness();

        Assert.Equal(0, await run.ConvertAsync("--format", "both", "--v2-compat"));
        Assert.Equal(0, await report.RunAsync("fingerprint", run.Spec, run.Data));

        var manifest = run.Text(".manifest.toml");
        var reported = Reported(report.StdOut);

        Assert.Contains(
            $"schema_fingerprint = \"{reported["schema_fingerprint"]}\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"cxt_output_fingerprint = \"{reported["cxt_output_fingerprint"]}\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"dat_output_fingerprint = \"{reported["dat_output_fingerprint"]}\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenTheSpecStoresStaleFingerprints_ThenTheyAreWarnedAboutAndNotRewritten()
    {
        var stale = CliFixtures.IndexBoundSpec.Replace(
            "version = 1",
            "version = 1\nschema_fingerprint = \"sha256:0000000000000000000000000000000000000000000000000000000000000000\"",
            StringComparison.Ordinal);
        using var run = ConvertRun.Wide(stale);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Contains("SchemaFingerprintStale", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Contains("sha256:0000", File.ReadAllText(run.Spec), StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private const string ObservedDomainSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        """;

    // Recomputed here from the bytes on disk — never the CLI's own helper.
    private static string Hash(string path) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static Dictionary<string, string> Reported(string stdout)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ');
            fields[parts[0]] = parts[1]["computed=".Length..];
        }

        return fields;
    }
}
