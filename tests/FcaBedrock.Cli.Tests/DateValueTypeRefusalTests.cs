namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The reserved <c>value_type = "date"</c> at the argv boundary (spec §10.2/§11.7/§20, D-038):
/// <c>validate</c> accepts it, and every command that plans refuses it with the Fatal plan-phase
/// <c>DateValueTypeNotImplementedV1</c>, exit 1, writing and publishing nothing. The expectations
/// are rendered text, so the suite names no library member that a build without the refusal lacks.
/// </summary>
public sealed class DateValueTypeRefusalTests
{
    // One headerless column with no declared_domain: the shape of the reserved-scale refusal, with
    // the date in place of the scale. Nothing here needs calibration.
    private const string DateSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = false

        [[attribute]]
        name = "born"
        source = { kind = "column", index = 0, value_type = "date" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        """;

    // The date beside an attribute that needs calibration: the data pass runs before the planner
    // refuses the date (D-010). It reads whole records, the date cells included, but it never
    // observes, types or calibrates the date attribute.
    private const string MixedSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = false

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "born"
        source = { kind = "column", index = 1, value_type = "date" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        """;

    private const string DateData = "2026-01-01\n2026-02-01\n";

    // The second date cell is not a date: a date attribute that calibration observed would report it.
    private const string MixedData = "red,2026-01-01\nblue,not a date\n";

    // The single stderr line of every plan-refusing command over DateSpec.
    private const string Refusal =
        """attribute="born": fatal DateValueTypeNotImplementedV1: Attribute 'born' declares value_type = \"date\", which v1 does not implement (§10.2/§11.7/§20).""" + "\n";

    private const string ColourCalibrated =
        """attribute="colour": warning ObservedDomainUsed: Attribute 'colour' had no declared_domain; it was calibrated from 2 observed value(s), so the schema depends on this input (§10.3).""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validate_WhenTheSpecDeclaresADate_ThenExitZeroAndSilence(bool withData)
    {
        // validate stops at resolve, and the date resolves; only planning refuses it.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", DateSpec);
        var harness = new CliTestHarness();

        var exit = withData
            ? await harness.RunAsync("validate", spec, temp.Write("data.csv", DateData))
            : await harness.RunAsync("validate", spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Report_WhenTheSpecDeclaresADate_ThenExitOneWithTheRefusalAlone(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(command, temp.Write("spec.toml", DateSpec), temp.Write("data.csv", DateData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(Refusal, harness.StdErr);
    }

    [Fact]
    public async Task Convert_WhenTheSpecDeclaresADate_ThenExitOneAndNothingIsPublished()
    {
        using var run = ConvertRun.Wide(DateSpec, DateData);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(Refusal, run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
        Assert.Equal(["data.csv", "spec.toml"], FileNames(run.Directory));
    }

    [Fact]
    public async Task Calibrate_WhenTheSpecDeclaresADate_ThenExitOneAndNoFrozenSpecIsWritten()
    {
        using var temp = TempDirectory.Create();
        var target = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "calibrate", temp.Write("spec.toml", DateSpec), temp.Write("data.csv", DateData), "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(Refusal, harness.StdErr);
        Assert.Equal(["data.csv", "spec.toml"], FileNames(temp.Path));
    }

    [Fact]
    public async Task Calibrate_WhenTheFrozenSpecWouldGoToStdout_ThenExitOneAndStdoutStaysEmpty()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "calibrate", temp.Write("spec.toml", DateSpec), temp.Write("data.csv", DateData), "--out", "-");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(Refusal, harness.StdErr);
    }

    [Fact]
    public async Task FingerprintWrite_WhenTheSpecDeclaresADate_ThenExitOneAndNoSpecIsWritten()
    {
        using var temp = TempDirectory.Create();
        var target = temp.Resolve("stamped.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "fingerprint", temp.Write("spec.toml", DateSpec), temp.Write("data.csv", DateData), "--write", "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(Refusal, harness.StdErr);
        Assert.Equal(["data.csv", "spec.toml"], FileNames(temp.Path));
    }

    [Theory]
    [InlineData("include = false")]
    [InlineData("include = false\nrestrict_to = [\"2026-01-01\"]")]
    public async Task Plan_WhenTheDateAttributeIsExcludedOrFilterOnly_ThenItIsStillRefused(string lines)
    {
        // A source is live configuration on an excluded attribute too (§10.1, D-076), and a
        // filter-only attribute would read the date column, so the refusal does not depend on include.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "plan", temp.Write("spec.toml", DateSpec + "\n" + lines + "\n"), temp.Write("data.csv", DateData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(Refusal, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Report_WhenADateSitsBesideACalibratedAttribute_ThenTheDataPassRunsFirstAndNeverObservesTheDate(string command)
    {
        // The refusal is a plan-phase diagnostic (D-010), so colour's calibration pass reads the data
        // first. That pass decodes the date cells with their records, but the date attribute has no
        // discretizer, so nothing observes "not a date" and it draws nothing.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(command, temp.Write("spec.toml", MixedSpec), temp.Write("data.csv", MixedData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(ColourCalibrated + "\n" + Refusal, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Report_WhenCancelledWhileCalibratingBesideADate_ThenExitThreeAndSilence(string command)
    {
        // Cancellation during the data pass ends the run before the planner is reached, exactly as
        // for any spec that needs calibration (D-122 part 2).
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", MixedSpec);
        var data = temp.Write("data.csv", MixedData);
        var harness = new CliTestHarness();

        // Data open 0 is schema acquisition; open 1 is the calibration pass.
        harness.OpenInput = CancelOnDataOpen(harness, data, dataOpen: 1);

        var exit = await harness.RunAsync(command, spec, data);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Convert_WhenCancelledWhileCalibratingBesideADate_ThenExitThreeAndNothingIsPublished()
    {
        using var run = ConvertRun.Wide(MixedSpec, MixedData);
        run.Harness.OpenInput = CancelOnDataOpen(run.Harness, run.Data, dataOpen: 1);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Empty(run.Residue());
        Assert.Equal(["data.csv", "spec.toml"], FileNames(run.Directory));
    }

    // Every file name in the directory, in ordinal order: the whole published state of a run.
    private static string[] FileNames(string directory)
    {
        var names = new List<string>();
        foreach (var path in Directory.GetFiles(directory))
        {
            names.Add(Path.GetFileName(path));
        }

        names.Sort(StringComparer.Ordinal);
        return [.. names];
    }

    // Cancels the host token as the numbered DATA open happens, so cancellation lands inside the
    // pass that open feeds rather than at a phase boundary.
    private static Func<string, Stream> CancelOnDataOpen(CliTestHarness harness, string dataPath, int dataOpen)
    {
        var seen = 0;
        return path =>
        {
            if (string.Equals(path, dataPath, StringComparison.Ordinal) && seen++ == dataOpen)
            {
                harness.Signals.Cancel();
            }

            return File.OpenRead(path);
        };
    }
}
