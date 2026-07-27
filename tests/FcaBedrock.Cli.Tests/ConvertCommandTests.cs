using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>convert</c> vertical at the argv boundary (D-122 parts 4–7): the committed bytes, the
/// target names, the stdout/stderr/exit contract, the <c>--v2-compat</c> override, the size
/// advisory, and the rule that an invalid run publishes nothing.
/// <para>
/// Every expected artifact below is derived by hand from the spec text and the two data rows
/// beside it — two declared values, two objects, one cross each — so the byte locks are an
/// independent statement of what the format is, not a recording of what the writer did.
/// </para>
/// </summary>
public sealed class ConvertCommandTests
{
    // colour ∈ {red, green} over two rows, one of each: formal attributes `colour-red` and
    // `colour-green`, objects named by row index, and one cross per object.
    private const string ExpectedWideCxt = "B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n";
    private const string ExpectedTripleCxt = "B\n\n2\n2\n\no1\no2\ncolour-red\ncolour-green\nX.\n.X\n";
    private const string ExpectedDat = "1\n2\n";

    // ---- happy paths -------------------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenTheSourceIsWideAndTheFormatIsCxt_ThenTheContextIsCommittedAndStdoutIsEmpty()
    {
        using var run = ConvertRun.Wide();

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Equal(ExpectedWideCxt, run.Text(".cxt"));
        Assert.False(File.Exists(run.Target(".dat")));
    }

    [Fact]
    public async Task Convert_WhenTheSourceIsWideAndTheFormatIsDat_ThenTheContextIsCommittedAndNoCxtExists()
    {
        using var run = ConvertRun.Wide();

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(0, exit);
        Assert.Equal(ExpectedDat, run.Text(".dat"));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Convert_WhenTheFormatIsBoth_ThenBothContextsAndTheManifestAreCommitted()
    {
        using var run = ConvertRun.Wide();

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);
        Assert.Equal(ExpectedWideCxt, run.Text(".cxt"));
        Assert.Equal(ExpectedDat, run.Text(".dat"));
        Assert.True(File.Exists(run.Target(".manifest.toml")));
    }

    [Fact]
    public async Task Convert_WhenTheSourceIsTriple_ThenTheSubjectsNameTheObjects()
    {
        using var run = ConvertRun.Triple();

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);
        Assert.Equal(ExpectedTripleCxt, run.Text(".cxt"));
        Assert.Equal(ExpectedDat, run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenTheBaseAlreadyCarriesAnExtension_ThenTheRuledOneIsAppendedToItVerbatim()
    {
        // D-122 part 5: the extension is appended to BASE, never inferred from it and never
        // stripped — so `--out out.cxt` publishes `out.cxt.cxt`.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out.cxt");

        var exit = await harness.RunAsync("convert", spec, data, "--out", basePath, "--format", "cxt");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(basePath + ".cxt"));
        Assert.False(File.Exists(basePath));
    }

    [Fact]
    public async Task Convert_WhenTheSameRunIsRepeated_ThenTheCommittedBytesAreIdentical()
    {
        using var first = ConvertRun.Wide();
        using var second = ConvertRun.Wide();

        Assert.Equal(0, await first.ConvertAsync("--format", "both"));
        Assert.Equal(0, await second.ConvertAsync("--format", "both"));

        Assert.Equal(first.Bytes(".cxt"), second.Bytes(".cxt"));
        Assert.Equal(first.Bytes(".dat"), second.Bytes(".dat"));
    }

    [Theory]
    [InlineData("cxt", 3)]
    [InlineData("dat", 2)]
    [InlineData("both", 4)]
    public async Task Convert_WhenTheSpecIsFullyDeclared_ThenOnlyTheWriterRequiredPassesReadTheData(
        string format, int expectedOpens)
    {
        // No calibration pass at all — the spec declares its domain — so the data is opened once
        // for the schema and then once per pass the selected writers actually need: two for the
        // .cxt replay, one for .dat.
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", format));

        Assert.Equal(
            expectedOpens,
            run.Harness.Opened.Count(path => string.Equals(path, run.Data, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Convert_WhenTheOutBaseIsADash_ThenItIsAFileNameAndStdoutStaysEmpty()
    {
        // The settled parser rejects `-` as a SPEC or DATA operand (there is no stdin source) but
        // does not special-case it as an `--out` VALUE for convert, whose --out is a base rather
        // than a sink. The behaviour that matters is D-122 part 4's: convert publishes no artifact
        // to stdout. It does not — `-` names an ordinary file, and stdout is still exactly empty.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var exit = await harness.RunAsync(
            "convert", spec, data, "--out", temp.Resolve("-"), "--format", "cxt");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(ExpectedWideCxt, await File.ReadAllTextAsync(temp.Resolve("-.cxt")));
    }

    [Fact]
    public async Task Convert_WhenTheRunFinishes_ThenNoHandleIsLeftOnAnyCommittedArtifact()
    {
        // Stage streams, hashers, the replay session, and any spool are all released: a file
        // still held open could not be renamed on Windows, let alone deleted here.
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        File.Delete(run.Target(".cxt"));
        File.Delete(run.Target(".dat"));
        File.Delete(run.Target(".manifest.toml"));

        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    // ---- native [output] settings ------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenOutputSettingsAreAuthored_ThenTheWritersHonourEveryOne()
    {
        // CRLF on both formats, no trailing newline on either, zero-based .dat ids, and a
        // trailing space on non-empty .dat lines — the full §8 native surface at once.
        using var run = ConvertRun.Wide(CliFixtures.IndexBoundSpec + """

            [output.cxt]
            line_endings = "crlf"
            trailing_newline = false

            [output.dat]
            line_endings = "crlf"
            trailing_newline = false
            base_index = 0
            nonempty_line_trailing_space = true
            """);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);
        Assert.Equal("B\r\n\r\n2\r\n2\r\n\r\n0\r\n1\r\ncolour-red\r\ncolour-green\r\nX.\r\n.X", run.Text(".cxt"));
        Assert.Equal("0 \r\n1 ", run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenAnEmptyLineTrailingSpaceIsAuthored_ThenACrosslessObjectCarriesIt()
    {
        // The second row's value is outside the declared domain, so its object crosses nothing.
        using var run = ConvertRun.Wide(
            SkipUnknownSpec + "\n[output.dat]\nempty_line_trailing_space = true\n",
            "colour,size\nred,1\nblue,2\n");

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(0, exit);
        Assert.Equal("1\n \n", run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenTempDirIsSupplied_ThenTheSpillingRunStillCommitsIdenticalBytes()
    {
        using var spill = TempDirectory.Create();
        using var plain = ConvertRun.Wide(CliFixtures.DedupeSpec, CliFixtures.DedupeData);
        using var directed = ConvertRun.Wide(CliFixtures.DedupeSpec, CliFixtures.DedupeData);

        Assert.Equal(0, await plain.ConvertAsync("--format", "both"));
        Assert.Equal(0, await directed.ConvertAsync("--format", "both", "--temp-dir", spill.Path));

        Assert.Equal(plain.Bytes(".cxt"), directed.Bytes(".cxt"));
        Assert.Equal(plain.Bytes(".dat"), directed.Bytes(".dat"));
    }

    // ---- --v2-compat -------------------------------------------------------------------------

    [Fact]
    public async Task Convert_WhenV2CompatIsRequestedOnAWideSource_ThenEveryV2ByteConventionApplies()
    {
        using var run = ConvertRun.Wide();

        var exit = await run.ConvertAsync("--format", "both", "--v2-compat");

        Assert.Equal(0, exit);
        Assert.Equal(
            "B\r\n\r\n2\r\n2\r\n\r\n0\r\n1\r\ncolour-red\r\ncolour-green\r\nX.\r\n.X\r\n", run.Text(".cxt"));
        Assert.Equal("1 \r\n2 \r\n", run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenV2CompatIsRequestedOnATripleSource_ThenTheDatFinalNewlineIsAbsent()
    {
        // D-087: v2's triple converter wrote no final .dat line terminator, while its wide
        // converter did. The .cxt keeps its trailing CRLF in both shapes.
        using var run = ConvertRun.Triple();

        var exit = await run.ConvertAsync("--format", "both", "--v2-compat");

        Assert.Equal(0, exit);
        Assert.EndsWith("X.\r\n.X\r\n", run.Text(".cxt"), StringComparison.Ordinal);
        Assert.Equal("1 \r\n2 ", run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenV2CompatSupersedesAuthoredOutputSettings_ThenTheAuthoredOnesDoNotShow()
    {
        using var run = ConvertRun.Wide(CliFixtures.IndexBoundSpec + """

            [output.cxt]
            line_endings = "lf"
            trailing_newline = false

            [output.dat]
            base_index = 0
            trailing_newline = false
            """);

        var exit = await run.ConvertAsync("--format", "both", "--v2-compat");

        Assert.Equal(0, exit);
        Assert.EndsWith("X.\r\n.X\r\n", run.Text(".cxt"), StringComparison.Ordinal);
        Assert.Equal("1 \r\n2 \r\n", run.Text(".dat"));
    }

    [Fact]
    public async Task Convert_WhenV2LabelsCollideButNativeOnesDoNot_ThenOnlyTheV2RunFails()
    {
        // Both attributes render through `{value}` alone. Natively the cut bin is `[30, 40)` and
        // the declared value is the literal `30to<40`, so the two names differ; under v2 labels
        // the cut bin renders `30to<40` too, and the plan collides. The effective plan's own
        // diagnostics are the run's failure — and they only appear for the run that has them.
        using var native = ConvertRun.Wide(V2LabelCollisionSpec, V2LabelCollisionData);
        using var compat = ConvertRun.Wide(V2LabelCollisionSpec, V2LabelCollisionData);

        Assert.Equal(0, await native.ConvertAsync("--format", "cxt"));
        Assert.Equal(1, await compat.ConvertAsync("--format", "cxt", "--v2-compat"));

        Assert.Contains(
            DiagnosticCode.FormalAttributeNameCollision.ToString(), compat.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, compat.Harness.StdOut);
        Assert.False(File.Exists(compat.Target(".cxt")));
        Assert.False(File.Exists(compat.Target(".manifest.toml")));
    }

    // ---- diagnostics, validity, and exits -----------------------------------------------------

    [Fact]
    public async Task Convert_WhenOnlyWarningsAreProduced_ThenTheRunCommitsAndExitsZero()
    {
        // An observed domain warns (ObservedDomainUsed) and an unknown value warns; neither
        // moves the exit off 0, and both still reach stderr.
        using var run = ConvertRun.Wide(SkipUnknownSpec, "colour,size\nred,1\nblue,2\n");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Contains(
            DiagnosticCode.UnknownValueObserved.ToString(), run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Convert_WhenTheSpecFailsToResolve_ThenNothingIsPublished()
    {
        using var run = ConvertRun.Wide(CliFixtures.ErrorSpec);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenAnEmitErrorOccurs_ThenEveryStagedArtifactIsDiscarded()
    {
        // unknown_value_policy = "fail" turns the out-of-domain value into an Error at emit. The
        // enumeration still completes (the aggregate needs the whole population), so bytes were
        // staged — and every one of them is discarded rather than committed.
        using var run = ConvertRun.Wide(FailUnknownSpec, "colour,size\nred,1\nblue,2\n");

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Contains(
            DiagnosticCode.UnknownValueObserved.ToString(), run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenTheFormatIsBoth_ThenTheSharedConversionIsNotDiagnosedTwice()
    {
        // .cxt replays the emit and .dat enumerates it once more, so three real passes run over
        // one conversion. The data diagnostics belong to the conversion, not to a pass, and must
        // appear exactly once.
        using var both = ConvertRun.Wide(SkipUnknownSpec, "colour,size\nred,1\nblue,2\n");
        using var single = ConvertRun.Wide(SkipUnknownSpec, "colour,size\nred,1\nblue,2\n");

        Assert.Equal(0, await both.ConvertAsync("--format", "both"));
        Assert.Equal(0, await single.ConvertAsync("--format", "dat"));

        Assert.Equal(single.Harness.StdErr, both.Harness.StdErr);
        Assert.Equal(1, Occurrences(both.Harness.StdErr, DiagnosticCode.UnknownValueObserved.ToString()));
    }

    // ---- the .cxt size advisory ---------------------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(42, true)]
    [InlineData(43, false)]
    public async Task Convert_WhenTheSizeAdvisoryThresholdIsSet_ThenItFiresAtOrAboveTheProjectedSize(
        long threshold, bool expected)
    {
        // The committed .cxt is exactly 42 ASCII bytes (the hand-derived layout above), so the
        // at-threshold case, the just-below case, and the explicit `0` disable are all exact.
        using var run = ConvertRun.Wide(
            CliFixtures.IndexBoundSpec + $"\n[output.cxt]\nsize_advisory_bytes = {threshold}\n");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(0, exit);
        Assert.Equal(42, ExpectedWideCxt.Length);
        Assert.Equal(
            expected,
            run.Harness.StdErr.Contains(DiagnosticCode.OutputCxtSizeAdvisory.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Convert_WhenTheAdvisoryThresholdChangesOnly_ThenTheCommittedBytesAreUnchanged()
    {
        using var warned = ConvertRun.Wide(CliFixtures.IndexBoundSpec + "\n[output.cxt]\nsize_advisory_bytes = 1\n");
        using var silent = ConvertRun.Wide(CliFixtures.IndexBoundSpec + "\n[output.cxt]\nsize_advisory_bytes = 0\n");

        Assert.Equal(0, await warned.ConvertAsync("--format", "cxt"));
        Assert.Equal(0, await silent.ConvertAsync("--format", "cxt"));

        Assert.Contains(
            DiagnosticCode.OutputCxtSizeAdvisory.ToString(), warned.Harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DiagnosticCode.OutputCxtSizeAdvisory.ToString(), silent.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(warned.Bytes(".cxt"), silent.Bytes(".cxt"));
    }

    [Fact]
    public async Task Convert_WhenOnlyDatIsRequested_ThenNoSizeAdvisoryIsEmitted()
    {
        using var run = ConvertRun.Wide(CliFixtures.IndexBoundSpec + "\n[output.cxt]\nsize_advisory_bytes = 1\n");

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(
            DiagnosticCode.OutputCxtSizeAdvisory.ToString(), run.Harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenTheSizeAdvisoryThresholdIsNegative_ThenTheRunIsRefusedBeforeAnythingIsCreated()
    {
        // §8 defines exactly two readings — a positive threshold, or 0 to disable — and the
        // writer rejects a negative one. Reading it as "disabled" would invent a third; the run
        // refuses it instead, as an ordinary code-less failure rather than an internal fault.
        using var run = ConvertRun.Wide(CliFixtures.IndexBoundSpec + "\n[output.cxt]\nsize_advisory_bytes = -1\n");

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            "error: The [output.cxt] size_advisory_bytes value cannot be negative.\n", run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    // ---- fixtures ------------------------------------------------------------------------------

    private const string SkipUnknownSpec = """
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
        unknown_value_policy = "warn"
        """;

    private const string FailUnknownSpec = """
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
        unknown_value_policy = "fail"
        """;

    // `{value}` alone on both attributes, so the rendered names are exactly the bin labels.
    private const string V2LabelCollisionSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "age"
        source = { kind = "column", index = 0, value_type = "number" }
        discretizer = { kind = "manual_cuts", cuts = [30.0, 40.0], ends = "open" }
        scale = { kind = "nominal" }
        formal_attribute_format = "{value}"

        [[attribute]]
        name = "label"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["30to<40"]
        formal_attribute_format = "{value}"
        """;

    private const string V2LabelCollisionData = "age,label\n35,30to<40\n";

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}

/// <summary>
/// One temporary convert run: a spec, a data source, an output base, and the harness that drives
/// it. The base is absolute so no test depends on — or changes — the process working directory.
/// </summary>
internal sealed class ConvertRun : IDisposable
{
    private readonly TempDirectory _temp;

    private ConvertRun(TempDirectory temp, string spec, string data, string basePath)
    {
        _temp = temp;
        Spec = spec;
        Data = data;
        Base = basePath;
    }

    /// <summary>The harness the run is driven through.</summary>
    public CliTestHarness Harness { get; } = new();

    /// <summary>The spec file's path.</summary>
    public string Spec { get; }

    /// <summary>The data file's path.</summary>
    public string Data { get; }

    /// <summary>The <c>--out</c> operand.</summary>
    public string Base { get; }

    /// <summary>The directory every target and any residue lives in.</summary>
    public string Directory => _temp.Path;

    /// <summary>A wide run over the given spec and data (the fully declared defaults).</summary>
    public static ConvertRun Wide(string? spec = null, string? data = null)
    {
        var temp = TempDirectory.Create();
        return new ConvertRun(
            temp,
            temp.Write("spec.toml", spec ?? CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", data ?? CliFixtures.WideData),
            temp.Resolve("out"));
    }

    /// <summary>A triple run over the triple fixtures.</summary>
    public static ConvertRun Triple()
    {
        var temp = TempDirectory.Create();
        return new ConvertRun(
            temp,
            temp.Write("spec.toml", CliFixtures.TripleSpec),
            temp.Write("data.csv", CliFixtures.TripleData),
            temp.Resolve("out"));
    }

    /// <summary>Runs <c>convert SPEC DATA --out BASE</c> plus <paramref name="options"/>.</summary>
    public Task<int> ConvertAsync(params string[] options) =>
        Harness.RunAsync(["convert", Spec, Data, "--out", Base, .. options]);

    /// <summary>The absolute path of the target with <paramref name="extension"/>.</summary>
    public string Target(string extension) => Base + extension;

    /// <summary>The committed bytes of the target with <paramref name="extension"/>.</summary>
    public byte[] Bytes(string extension) => File.ReadAllBytes(Target(extension));

    /// <summary>The committed text of the target with <paramref name="extension"/>, decoded as UTF-8.</summary>
    public string Text(string extension) => new UTF8Encoding(false).GetString(Bytes(extension));

    /// <summary>
    /// Every file in the directory that claims the private transaction namespace: a record, a
    /// stage, or a backup. A finished run — committed or rolled back — leaves none.
    /// </summary>
    public IReadOnlyList<string> Residue()
    {
        var residue = new List<string>();
        foreach (var path in System.IO.Directory.GetFiles(_temp.Path))
        {
            if (Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal))
            {
                residue.Add(Path.GetFileName(path));
            }
        }

        residue.Sort(StringComparer.Ordinal);
        return residue;
    }

    /// <summary>The transaction record's file name, when one is present.</summary>
    public string RecordName()
    {
        foreach (var name in Residue())
        {
            if (name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal))
            {
                return name;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Every <c>out*</c> file's name paired with its exact bytes — the oracle a rollback test
    /// compares before and after, so "restored byte-identically" means what it says.
    /// </summary>
    public Dictionary<string, byte[]> Snapshot()
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.GetFiles(_temp.Path, "out*"))
        {
            snapshot[Path.GetFileName(path)] = File.ReadAllBytes(path);
        }

        return snapshot;
    }

    public void Dispose() => _temp.Dispose();
}
