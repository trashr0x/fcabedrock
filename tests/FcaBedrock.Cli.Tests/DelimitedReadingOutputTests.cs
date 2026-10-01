using System.Security.Cryptography;
using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// What the delimited-text reading rules (spec §5.1.1) do to the committed outputs and the run
/// manifest, end to end through <c>convert</c>.
/// <para>
/// <b>Every expected context below is derived by hand</b> from the spec text and the data rows
/// beside it: blank and whitespace-only lines are not records, Unicode whitespace around an
/// unquoted field and outside a quoted one is removed, and quoted content is kept as written. The
/// manifest checks recompute every hash from the bytes on disk with <see cref="SHA256"/>.
/// </para>
/// </summary>
public sealed class DelimitedReadingOutputTests
{
    private static readonly string Nbsp = ((char)0x00A0).ToString();

    // colour has a declared domain; age's two equal-width bins are calibrated from the data.
    private const string WideSpec = """
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
        declared_domain = ["red", "green"]

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "equal_width", bins = 2, range = "min_max" }
        scale = { kind = "nominal" }
        """;

    // A blank CRLF line before the header; a blank line and a whitespace-only line between records;
    // TAB and no-break-space padding around unquoted fields; an outer TAB beside a quoted field; a
    // quoted " 30 " under equal_width; the missing token; and blank lines after the last record.
    private static readonly string WideData =
        "\r\n"
        + "colour,age\n"
        + "\n"
        + "\tred" + Nbsp + "," + Nbsp + "10\n"
        + " \t \n"
        + "\t\"green\"\t,\" 30 \"\r\n"
        + "?,20\n"
        + "\n"
        + "  ";

    // Three records 0, 1, 2. age 10, 30 and 20 give min 10, max 30 and one cut at 20, so the bins
    // are age-<20 and age->=20. Record 2's colour is the missing token, which `skip` leaves
    // uncrossed. Formal attributes in spec order: colour-red 1, colour-green 2, age-<20 3, age->=20 4.
    private const string ExpectedWideCxt =
        "B\n\n3\n4\n\n0\n1\n2\ncolour-red\ncolour-green\nage-<20\nage->=20\nX.X.\n.X.X\n...X\n";

    private const string ExpectedWideDat = "1 3\n2 4\n4\n";

    // Blank lines before, between and after the triples; TAB and no-break-space padding; outer TABs
    // beside quoted subject and value fields; and o3's value is the missing token.
    private static readonly string TripleData =
        "\n"
        + "\to1" + Nbsp + ",colour," + Nbsp + "red\n"
        + " \t\n"
        + "\"o2\"\t,\tcolour\t,\t\"green\"\r\n"
        + "\n\n"
        + "o3,colour,?\n"
        + "   ";

    // Objects in first-appearance order. o3 has a matching row whose value is missing, so it is an
    // object with no cross, which the object-level aggregate reports (spec §16.4).
    private const string ExpectedTripleCxt =
        "B\n\n3\n2\n\no1\no2\no3\ncolour-red\ncolour-green\nX.\n.X\n..\n";

    private const string ExpectedTripleDat = "1\n2\n\n";

    // ---- literal outputs, verified in two executions --------------------------------------------

    [Fact]
    public async Task Convert_WhenAWideInputHasBlankLinesPaddingAndQuotes_ThenTheContextsAreTheLiteralBytes()
    {
        using var first = ConvertRun.Wide(WideSpec, WideData);
        using var second = ConvertRun.Wide(WideSpec, WideData);

        Assert.Equal(0, await first.ConvertAsync("--format", "both"));
        Assert.Equal(0, await second.ConvertAsync("--format", "both"));

        Assert.Equal(string.Empty, first.Harness.StdErr);
        Assert.Equal(ExpectedWideCxt, first.Text(".cxt"));
        Assert.Equal(ExpectedWideDat, first.Text(".dat"));
        Assert.Equal(first.Bytes(".cxt"), second.Bytes(".cxt"));
        Assert.Equal(first.Bytes(".dat"), second.Bytes(".dat"));
    }

    [Fact]
    public async Task Convert_WhenATripleInputHasBlankLinesPaddingAndQuotes_ThenTheContextsAreTheLiteralBytes()
    {
        using var first = TripleRun(TripleData);
        using var second = TripleRun(TripleData);

        Assert.Equal(0, await first.ConvertAsync("--format", "both"));
        Assert.Equal(0, await second.ConvertAsync("--format", "both"));

        Assert.Equal(
            "warning ObjectHasNoCrosses: 1 emitted object(s) cross no formal attribute (e.g. o3);"
                + " their rows are empty (§16.4).\n",
            first.Harness.StdErr);
        Assert.Equal(ExpectedTripleCxt, first.Text(".cxt"));
        Assert.Equal(ExpectedTripleDat, first.Text(".dat"));
        Assert.Equal(first.Bytes(".cxt"), second.Bytes(".cxt"));
        Assert.Equal(first.Bytes(".dat"), second.Bytes(".dat"));
    }

    // ---- the manifest ---------------------------------------------------------------------------

    [Fact]
    public async Task Manifest_WhenTheSameInputIsConvertedTwice_ThenItsSemanticFieldsAreEqual()
    {
        using var first = ConvertRun.Wide(WideSpec, WideData);
        using var second = ConvertRun.Wide(WideSpec, WideData);
        second.Harness.ToolVersion = "fcabedrock-vnext 0.0.0-other";
        second.Harness.Clock.UtcNow = new DateTimeOffset(2031, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.Equal(0, await first.ConvertAsync("--format", "both"));
        Assert.Equal(0, await second.ConvertAsync("--format", "both"));

        var manifest = first.Text(".manifest.toml");
        Assert.Equal(Semantic(manifest), Semantic(second.Text(".manifest.toml")));

        // The hash entries are over the bytes on disk, and the cut is the hand-derived one.
        Assert.Equal(Hash(File.ReadAllBytes(first.Spec)), Field(manifest, "spec_file_hash"));
        Assert.Equal(Hash(File.ReadAllBytes(first.Data)), Field(manifest, "input_hash"));
        Assert.Contains($"hash = \"{Hash(first.Bytes(".cxt"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{Hash(first.Bytes(".dat"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains(
            """
            [[run.calibrations]]
            attribute = "age"
            kind = "cuts"
            discretizer = "equal_width"
            cuts = [20]
            """.ReplaceLineEndings("\n"),
            manifest,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_WhenOnlyBlankLinesAreAdded_ThenOutputsAndFingerprintsAreKeptAndTheInputHashChanges()
    {
        var withMoreBlanks = "\n \n" + WideData.Replace("\n?,20", "\n\t\n\n?,20", StringComparison.Ordinal) + "\r\n\n";
        using var plain = ConvertRun.Wide(WideSpec, WideData);
        using var blank = ConvertRun.Wide(WideSpec, withMoreBlanks);

        Assert.Equal(0, await plain.ConvertAsync("--format", "both"));
        Assert.Equal(0, await blank.ConvertAsync("--format", "both"));

        Assert.Equal(plain.Bytes(".cxt"), blank.Bytes(".cxt"));
        Assert.Equal(plain.Bytes(".dat"), blank.Bytes(".dat"));
        var before = plain.Text(".manifest.toml");
        var after = blank.Text(".manifest.toml");
        foreach (var key in new[] { "schema_fingerprint", "cxt_output_fingerprint", "dat_output_fingerprint", "spec_file_hash" })
        {
            Assert.Equal(Field(before, key), Field(after, key));
        }

        Assert.Equal(Hash(File.ReadAllBytes(plain.Data)), Field(before, "input_hash"));
        Assert.Equal(Hash(File.ReadAllBytes(blank.Data)), Field(after, "input_hash"));
        Assert.NotEqual(Field(before, "input_hash"), Field(after, "input_hash"));
    }

    public static TheoryData<string, byte[]> RawInputs() => new()
    {
        { "a byte order mark", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("colour,size\nred,1\ngreen,2\n")] },
        { "an invalid byte in an unused column", [.. Encoding.UTF8.GetBytes("colour,size\nred,1"), 0xFF, .. Encoding.UTF8.GetBytes("\ngreen,2\n")] },
        { "quoted CR CR in an unused column", Encoding.UTF8.GetBytes("colour,size\nred,\"1\r\r\"\n\ngreen,2\n") },
    };

    [Theory]
    [MemberData(nameof(RawInputs))]
    public async Task Manifest_WhenTheInputHasBytesTheDecoderAlters_ThenTheInputHashIsOverTheOriginalBytes(string what, byte[] bytes)
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        await File.WriteAllBytesAsync(data, bytes, TestContext.Current.CancellationToken);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("convert", spec, data, "--out", temp.Resolve("out"), "--format", "cxt");

        Assert.True(exit == 0, $"{what}: {harness.StdErr}");
        Assert.Equal("B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", File.ReadAllText(temp.Resolve("out.cxt")));
        Assert.Equal(Hash(bytes), Field(File.ReadAllText(temp.Resolve("out.manifest.toml")), "input_hash"));
    }

    private static ConvertRun TripleRun(string data)
    {
        var run = ConvertRun.Triple();
        File.WriteAllText(run.Data, data, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return run;
    }

    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    // The value of the first `key = "..."` line.
    private static string Field(string manifest, string key)
    {
        var prefix = key + " = \"";
        var line = manifest.Split('\n').First(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..^1];
    }

    // The manifest without the fields that describe the invocation rather than the result: the
    // tool version, the timestamp, the command line and every path.
    private static string[] Semantic(string manifest) =>
        [.. manifest.Split('\n').Where(line =>
            !line.StartsWith("tool_version = ", StringComparison.Ordinal)
            && !line.StartsWith("timestamp = ", StringComparison.Ordinal)
            && !line.StartsWith("command_line = ", StringComparison.Ordinal)
            && !line.StartsWith("spec_path = ", StringComparison.Ordinal)
            && !line.StartsWith("input_path = ", StringComparison.Ordinal)
            && !line.StartsWith("path = ", StringComparison.Ordinal))];
}
