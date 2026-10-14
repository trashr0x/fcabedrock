namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Native stored verification versus effective emission, locked causally.
/// <para>
/// The two are deliberately separate authorities: the <b>stored</b> <c>[spec]</c> fields are
/// always verified against the <b>native</b> fingerprints (a CLI byte override is not a property
/// of the spec), while <c>[[run.outputs]]</c>'s per-format fingerprints are the <b>effective</b>
/// values the override produced (§14/§15, D-077/D-011).
/// </para>
/// <para>
/// <b>Why pinned literals rather than "they differ".</b> Asserting only that the v2 values are
/// unequal to the native ones is satisfied by any wrong value. Pinning both sets makes a run that
/// verified against the effective plan, or manifested the native one, fail here, which is the
/// whole point of keeping the two authorities apart.
/// </para>
/// </summary>
public sealed class ConvertNativeEffectiveTests
{
    // Byte locks over CliFixtures.IndexBoundSpec against CliFixtures.WideData. The schema
    // fingerprint is style-independent and so is shared; the two output fingerprints are not.
    private const string SchemaFingerprint =
        "sha256:507857e468e3925593566f67f8df41374e8b558a925098c057163c06a66f48e7";

    private const string NativeCxtFingerprint =
        "sha256:f2d9bb2653142e545ff94bfe4a4c8b217037c75044300ca39cfa566c009249bd";

    private const string NativeDatFingerprint =
        "sha256:e4c558c3d33b28b99a18b6d649a30a1dd1ff5a8b3789e2cc7d4b35c99dffd96c";

    private const string EffectiveCxtFingerprint =
        "sha256:3c724e5aedbb4d8f40e9418e30e463ce36f2b0e142331786e964d16981ed78f3";

    private const string EffectiveDatFingerprint =
        "sha256:aea1fd3a6d401c1aab157eb5311cbf61443058fd63bde5013eced5945eaf4ba9";

    [Fact]
    public void Fingerprints_WhenTheStyleChanges_ThenOnlyTheOutputValuesMove()
    {
        // The premise the rest of the file depends on: N != V per format, and the schema value is
        // the same on both sides.
        Assert.NotEqual(NativeCxtFingerprint, EffectiveCxtFingerprint);
        Assert.NotEqual(NativeDatFingerprint, EffectiveDatFingerprint);
    }

    [Fact]
    public async Task Convert_WhenTheRootStoresCorrectNativeFields_ThenAV2RunIsStillCurrent()
    {
        // The stored fields are native and correct. A v2-compatible conversion emits different
        // bytes and records different output fingerprints, and must NOT report the spec as stale,
        // because nothing about the spec changed.
        using var run = ConvertRun.Wide(Stored(
            $"schema_fingerprint = \"{SchemaFingerprint}\"",
            $"cxt_output_fingerprint = \"{NativeCxtFingerprint}\"",
            $"dat_output_fingerprint = \"{NativeDatFingerprint}\""));

        var exit = await run.ConvertAsync("--format", "both", "--v2-compat");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, run.Harness.StdErr);

        var manifest = run.Text(".manifest.toml");
        Assert.Contains($"schema_fingerprint = \"{SchemaFingerprint}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"cxt_output_fingerprint = \"{EffectiveCxtFingerprint}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"dat_output_fingerprint = \"{EffectiveDatFingerprint}\"", manifest, StringComparison.Ordinal);

        // And the bytes really are the v2 ones, so the effective values describe what was written.
        Assert.EndsWith("X.\r\n.X\r\n", run.Text(".cxt"), StringComparison.Ordinal);
        Assert.Equal("1 \r\n2 \r\n", run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenTheRootStoresCorrectNativeFields_ThenANativeRunIsAlsoCurrent()
    {
        using var run = ConvertRun.Wide(Stored(
            $"schema_fingerprint = \"{SchemaFingerprint}\"",
            $"cxt_output_fingerprint = \"{NativeCxtFingerprint}\"",
            $"dat_output_fingerprint = \"{NativeDatFingerprint}\""));

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Contains(
            $"cxt_output_fingerprint = \"{NativeCxtFingerprint}\"",
            run.Text(".manifest.toml"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Convert_WhenAnEffectiveValueIsStoredAsIfNative_ThenItIsReportedStale(bool v2Compat)
    {
        // Storing the v2-effective value where the native one belongs is exactly the mistake the
        // separation exists to catch: the stored field is compared against the computed NATIVE
        // value, so it is stale, and stays stale under --v2-compat, where that same effective
        // value is the one being written.
        using var run = ConvertRun.Wide(Stored(
            $"cxt_output_fingerprint = \"{EffectiveCxtFingerprint}\"",
            $"dat_output_fingerprint = \"{EffectiveDatFingerprint}\""));

        var options = v2Compat ? new[] { "--format", "both", "--v2-compat" } : ["--format", "both"];
        var exit = await run.ConvertAsync(options);

        Assert.Equal(0, exit);
        Assert.Contains("CxtOutputFingerprintStale", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Contains("DatOutputFingerprintStale", run.Harness.StdErr, StringComparison.Ordinal);

        // The warning quotes the computed NATIVE value, which is what proves the comparison did
        // not quietly move to the effective plan.
        Assert.Contains(NativeCxtFingerprint, run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Contains(NativeDatFingerprint, run.Harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("SchemaFingerprintStale", run.Harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenTheSpecIsAChain_ThenTheRootRemainsTheVerifiedDocument()
    {
        // §13 rule 8: a base file's stored fingerprints are never the root's. The root here stores
        // correct native values, so the run is current even though the base stores nonsense.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        temp.Write(
            "base.toml",
            CliFixtures.IndexBoundSpec.Replace(
                "version = 1",
                "version = 1\nschema_fingerprint = \"sha256:"
                + "1111111111111111111111111111111111111111111111111111111111111111\"",
                StringComparison.Ordinal));

        var root = temp.Write(
            "root.toml",
            "[spec]\nversion = 1\nextends = \"base.toml\"\n"
            + $"schema_fingerprint = \"{SchemaFingerprint}\"\n"
            + $"cxt_output_fingerprint = \"{NativeCxtFingerprint}\"\n"
            + $"dat_output_fingerprint = \"{NativeDatFingerprint}\"\n");

        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out");

        var exit = await harness.RunAsync("convert", root, data, "--out", basePath, "--format", "both", "--v2-compat");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Contains(
            $"cxt_output_fingerprint = \"{EffectiveCxtFingerprint}\"",
            await File.ReadAllTextAsync(basePath + ".manifest.toml", TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    // ---- bin_label_unicode: native Unicode, v2-compatible ASCII (§8/§14) ------------------------

    [Fact]
    public async Task Convert_WhenBinLabelUnicodeIsTrue_ThenNativeNamesUseUnicodeForGeneratedOperatorsOnly()
    {
        // Written out by hand: ≥ and ≤ replace only the operators the planner generates (an open
        // high cut bin, ordinal thresholds, {scale_op}). The ordered cut "=x" makes the open low
        // bin "<" + "=x", the authored value "<=50K" and the authored label ">=50K" stay as written.
        using var run = ConvertRun.Wide(CliFixtures.UnicodeLabelSpec, CliFixtures.UnicodeLabelData);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Equal(
            string.Join(
                "\n",
                "B",
                "",
                "3",
                "13",
                "",
                "0",
                "1",
                "2",
                "age-<30",
                "age-[30, 50)",
                "age-≥50",
                "age_ge-all",
                "age_ge-≥30",
                "age_ge-≥50",
                "grade-<=x",
                "grade-[=x, b)",
                "grade-≥b",
                "level ≤ low",
                "level ≤ high",
                "class-<=50K",
                "class->=50K",
                "X..X..X..XXX.",
                ".X.XX..X..X.X",
                "..XXXX..XXXX.") + "\n",
            run.Text(".cxt"));
    }

    [Fact]
    public async Task Convert_WhenV2CompatOverridesAUnicodeSpec_ThenItsBytesAndEffectiveFingerprintsAreTheAsciiSpecs()
    {
        // --v2-compat writes ASCII operators whatever the spec authors (§8), so the effective .cxt
        // fingerprint the manifest records must describe ASCII names: exactly the value of the same
        // spec with the flag false. The stored native fields are not involved (§14).
        using var unicode = ConvertRun.Wide(CliFixtures.UnicodeLabelSpec, CliFixtures.UnicodeLabelData);
        using var ascii = ConvertRun.Wide(CliFixtures.UnicodeLabelAsciiSpec, CliFixtures.UnicodeLabelData);

        Assert.Equal(0, await unicode.ConvertAsync("--format", "both", "--v2-compat"));
        Assert.Equal(0, await ascii.ConvertAsync("--format", "both", "--v2-compat"));
        Assert.Equal(string.Empty, unicode.Harness.StdErr);

        var cxt = unicode.Text(".cxt");
        Assert.Contains("\r\nage->=50\r\n", cxt, StringComparison.Ordinal);
        Assert.Contains("\r\nage-30to<50\r\n", cxt, StringComparison.Ordinal);
        Assert.Contains("\r\nlevel <= low\r\n", cxt, StringComparison.Ordinal);
        Assert.DoesNotContain('≥', cxt);
        Assert.DoesNotContain('≤', cxt);

        Assert.Equal(ascii.Bytes(".cxt"), unicode.Bytes(".cxt"));
        Assert.Equal(ascii.Bytes(".dat"), unicode.Bytes(".dat"));
        foreach (var field in new[] { "schema_fingerprint", "cxt_output_fingerprint", "dat_output_fingerprint" })
        {
            Assert.Equal(ManifestField(ascii, field), ManifestField(unicode, field));
        }
    }

    [Fact]
    public async Task Convert_WhenOnlyBinLabelUnicodeDiffers_ThenTheNamesMoveAndTheIdentitiesDoNot()
    {
        // Natively the flag changes the .cxt names and their fingerprint, and nothing else: the
        // incidence, the .dat bytes, and the schema and .dat fingerprints stay equal (§14).
        using var unicode = ConvertRun.Wide(CliFixtures.UnicodeLabelSpec, CliFixtures.UnicodeLabelData);
        using var ascii = ConvertRun.Wide(CliFixtures.UnicodeLabelAsciiSpec, CliFixtures.UnicodeLabelData);

        Assert.Equal(0, await unicode.ConvertAsync("--format", "both"));
        Assert.Equal(0, await ascii.ConvertAsync("--format", "both"));

        Assert.Equal(ascii.Bytes(".dat"), unicode.Bytes(".dat"));
        Assert.Equal(Incidence(ascii.Text(".cxt")), Incidence(unicode.Text(".cxt")));
        Assert.NotEqual(ascii.Bytes(".cxt"), unicode.Bytes(".cxt"));
        Assert.Equal(ManifestField(ascii, "schema_fingerprint"), ManifestField(unicode, "schema_fingerprint"));
        Assert.Equal(ManifestField(ascii, "dat_output_fingerprint"), ManifestField(unicode, "dat_output_fingerprint"));
        Assert.NotEqual(ManifestField(ascii, "cxt_output_fingerprint"), ManifestField(unicode, "cxt_output_fingerprint"));
    }

    private static string Stored(params string[] fields) =>
        CliFixtures.IndexBoundSpec.Replace(
            "version = 1", "version = 1\n" + string.Join('\n', fields), StringComparison.Ordinal);

    // The complete line of one [run] field of the committed manifest.
    private static string ManifestField(ConvertRun run, string field) =>
        run.Text(".manifest.toml").Split('\n').Single(line => line.StartsWith(field + " = ", StringComparison.Ordinal));

    // The incidence rows of a .cxt: the lines after the header, the object names and the
    // attribute names (the header gives both counts).
    private static string[] Incidence(string cxt)
    {
        var lines = cxt.Split('\n');
        var objects = int.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture);
        var attributes = int.Parse(lines[3], System.Globalization.CultureInfo.InvariantCulture);
        return lines[(5 + objects + attributes)..];
    }
}
