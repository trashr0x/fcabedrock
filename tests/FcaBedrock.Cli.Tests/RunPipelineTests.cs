using System.Text;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The shared preparation path behind <c>plan</c>, <c>stats</c>, and <c>fingerprint</c>
/// (D-122 part 10), driven at the argv boundary: phase order, failure ownership, cancellation,
/// input stability, and the boundaries this slice must <b>not</b> move.
/// </summary>
public sealed class RunPipelineTests
{
    /// <summary>Requires calibration: the domain is omitted, so Calibrate observes it (§10.3).</summary>
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

    private static readonly string[] ReportCommands = ["plan", "stats", "fingerprint"];

    // ---- successful pipelines ------------------------------------------------------------

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheSourceIsWide_ThenTheRunSucceeds(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.NotEqual(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheSourceIsTriple_ThenTheRunSucceeds(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.TripleSpec),
            temp.Write("data.csv", CliFixtures.TripleData));

        Assert.Equal(0, exit);
        Assert.NotEqual(string.Empty, harness.StdOut);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheSourceBindsByHeaderName_ThenTheSchemaResolvesIt(string command)
    {
        // A name-bound source cannot resolve without a schema, so a successful run proves the
        // schema was acquired and fed to the full resolve.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.NameBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheBoundColumnIsAbsent_ThenTheLibraryDiagnosticFailsTheRun(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.MissingColumnSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("SourceBindingInvalid", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- failure ownership -----------------------------------------------------------------

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheRootSpecCannotBeRead_ThenItIsACodelessHostFailure(string command)
    {
        using var temp = TempDirectory.Create();
        var missing = temp.Resolve("absent.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(command, missing, temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.SpecReadMessage(missing)), harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheDataCannotBeRead_ThenItIsACodelessHostFailure(string command)
    {
        using var temp = TempDirectory.Create();
        var missing = temp.Resolve("absent.csv");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(command, temp.Write("spec.toml", CliFixtures.IndexBoundSpec), missing);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(missing)), harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenAReferencedBaseIsMissing_ThenItKeepsItsOwnDiagnostic(string command)
    {
        // Only the ROOT operand is the CLI's own code-less failure; a base is a phase-owned
        // condition and keeps its registry code (§16.4).
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", """
            [spec]
            version = 1
            extends = "no-such-base.toml"

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "colour"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["red"]
            """);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(command, spec, temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("SpecExtendsNotFound", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("error: ", harness.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenTheSpecHasAnError_ThenNoReportIsWrittenAndExitIsOne(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.ErrorSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("error AttributeScalingMissing", harness.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenOnlyWarningsAreProduced_ThenTheReportStillPrintsWithExitZero(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.WarningOnlySpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(0, exit);
        Assert.NotEqual(string.Empty, harness.StdOut);
        Assert.Contains("warning RestrictToValueNotInDomain", harness.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenDataIsOmitted_ThenItIsAUsageError(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(command, temp.Write("spec.toml", CliFixtures.IndexBoundSpec));

        Assert.Equal(2, exit);
        Assert.Empty(harness.Opened);
    }

    // ---- diagnostic order -------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_ThenDiagnosticsKeepTheirPhaseAndLibraryOrder()
    {
        // A resolve-phase Warning, then the stale-fingerprint Warning verified after planning:
        // the later phase's diagnostic must come later, and nothing is sorted or grouped.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec.Replace(
            "version = 1",
            """
            version = 1
            schema_fingerprint = "sha256:aaa"
            """,
            StringComparison.Ordinal));
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("plan", spec, temp.Write("data.csv", CliFixtures.WideData)));

        var codes = harness.StdErr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Contains("RestrictToValueNotInDomain", StringComparison.Ordinal)
                ? nameof(DiagnosticCode.RestrictToValueNotInDomain)
                : nameof(DiagnosticCode.SchemaFingerprintStale))
            .ToArray();

        Assert.Equal(
            [nameof(DiagnosticCode.RestrictToValueNotInDomain), nameof(DiagnosticCode.SchemaFingerprintStale)],
            codes);
    }

    [Fact]
    public async Task Pipeline_WhenReadSettingsResolveCleanly_ThenTheyAddNoDiagnosticOfTheirOwn()
    {
        // The read-settings stage shares its helpers with the full resolve, so a condition it
        // could report is re-evaluated there; reporting it twice is exactly what must not happen.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("plan", spec, temp.Write("data.csv", CliFixtures.WideData)));

        var occurrences = harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains("RestrictToValueNotInDomain", StringComparison.Ordinal));
        Assert.Equal(1, occurrences);
    }

    // ---- cancellation ------------------------------------------------------------------------

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenCancelledBeforeDispatch_ThenExitThreeAndSilence(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        harness.Signals.Cancel();

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenCancelledWhileTheSpecIsRead_ThenExitThreeAndSilence(string command)
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();
        harness.OpenInput = path =>
        {
            if (string.Equals(path, spec, StringComparison.Ordinal))
            {
                harness.Signals.Cancel();
            }

            return File.OpenRead(path);
        };

        var exit = await harness.RunAsync(command, spec, data);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenCancelledDuringCalibration_ThenExitThreeAndSilence(string command)
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        // Data open 0 is schema acquisition; open 1 is the calibration pass.
        harness.OpenInput = CancelOnDataOpen(harness, data, dataOpen: 1);

        var exit = await harness.RunAsync(command, spec, data);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Stats_WhenCancelledDuringTheCountingPass_ThenExitThreeAndSilence()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        // Fully declared, so data open 0 is the schema and open 1 is the counting enumeration.
        harness.OpenInput = CancelOnDataOpen(harness, data, dataOpen: 1);

        var exit = await harness.RunAsync("stats", spec, data);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    // ---- input stability, through argv ------------------------------------------------------

    [Fact]
    public async Task Stats_WhenTheDataChangesBetweenPasses_ThenTheRunFailsBeforeAnythingIsWritten()
    {
        // A calibration-requiring run completes the calibration pass and then the counting
        // pass, so pass-specific bytes are exactly the case D-122 part 5 guards.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Resolve("data.csv");
        var harness = new CliTestHarness();
        harness.OpenInput = PassIndexed(
            spec, ObservedDomainSpec, data, "colour\nred\n", "colour\nred\n", "colour\ngreen\n");

        var exit = await harness.RunAsync("stats", spec, data);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.EndsWith(
            DiagnosticRenderer.RenderHostError(RunPipeline.InputChangedMessage(data)),
            harness.StdErr,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_WhenEveryPassAgrees_ThenTheRunSucceeds()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Resolve("data.csv");
        var harness = new CliTestHarness();
        harness.OpenInput = PassIndexed(spec, ObservedDomainSpec, data, "colour\nred\n");

        var exit = await harness.RunAsync("stats", spec, data);

        Assert.Equal(0, exit);
        Assert.Contains("objects = 1", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plan_WhenTheDataChangesBetweenSchemaAndCalibration_ThenTheRunFailsBeforeTheReport()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Resolve("data.csv");
        var harness = new CliTestHarness();

        // A schema read that reaches end of stream completes a pass of its own; a longer second
        // pass therefore disagrees with it either way.
        harness.OpenInput = PassIndexed(
            spec, ObservedDomainSpec, data, "colour\nred\n", "colour\nred\ngreen\nblue\n");

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.EndsWith(
            DiagnosticRenderer.RenderHostError(RunPipeline.InputChangedMessage(data)),
            harness.StdErr,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pipeline_ThenTheDataIsNeverOpenedJustToHashIt()
    {
        // Hashing rides along with the passes the sources were already making: a fully declared
        // plan opens the data once for its schema, and that is the whole tally.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("plan", spec, data));
        Assert.Equal(1, harness.Opened.Count(path => string.Equals(path, data, StringComparison.Ordinal)));
    }

    // ---- host and output failures --------------------------------------------------------------

    [Fact]
    public async Task Pipeline_WhenAnUnexpectedFaultOccurs_ThenItStaysTheSanitizedExitFour()
    {
        // Not an input failure: a programmer/state defect must not be absorbed into the
        // code-less exit-1 path, or a defect would masquerade as a broken data file.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();
        harness.OpenInput = path => string.Equals(path, data, StringComparison.Ordinal)
            ? throw new InvalidOperationException("a defect, not an input problem.")
            : File.OpenRead(path);

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(4, exit);
        Assert.Equal(DiagnosticRenderer.RenderHostError(CliHost.UnexpectedFaultMessage), harness.StdErr);
    }

    // ---- DATA read failures classify identically on every pass ---------------------------------
    //
    // An unreadable source must not change its public exit classification merely because the
    // failure landed after schema acquisition. Each case below lets the schema pass succeed and
    // then fails the LATER open — calibration for `plan`, the counting enumeration for `stats`.

    /// <summary>The established source-neutral provider/open/read families (the `Prober` boundary).</summary>
    public static TheoryData<string> ExpectedDataFailures() =>
    [
        nameof(SourceReadException),
        nameof(IOException),
        nameof(UnauthorizedAccessException),
        nameof(ObjectDisposedException),
        nameof(DecoderFallbackException),
        nameof(InvalidDataException),
    ];

    [Theory]
    [MemberData(nameof(ExpectedDataFailures))]
    public async Task Plan_WhenTheCalibrationPassCannotBeRead_ThenItIsTheSameCodelessDataFailure(string family)
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Resolve("data.csv");
        var harness = new CliTestHarness();
        harness.OpenInput = FailOnDataOpen(spec, ObservedDomainSpec, data, "colour\nred\n", failFrom: 1, family);

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(data)), harness.StdErr);
    }

    [Theory]
    [MemberData(nameof(ExpectedDataFailures))]
    public async Task Stats_WhenTheCountingPassCannotBeRead_ThenItIsTheSameCodelessDataFailure(string family)
    {
        // Fully declared, so open 0 is the schema and open 1 is the counting enumeration —
        // the pass that lives in the handler rather than the shared pipeline.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        var harness = new CliTestHarness();
        harness.OpenInput = FailOnDataOpen(
            spec, CliFixtures.IndexBoundSpec, data, CliFixtures.WideData, failFrom: 1, family);

        var exit = await harness.RunAsync("stats", spec, data);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(data)), harness.StdErr);
    }

    [Theory]
    [MemberData(nameof(ExpectedDataFailures))]
    public async Task Pipeline_WhenTheSchemaPassCannotBeRead_ThenItIsTheSameCodelessDataFailure(string family)
    {
        // The first pass, for comparison: one unreadable source, one classification, whichever
        // pass happens to meet it.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Resolve("data.csv");
        var harness = new CliTestHarness();
        harness.OpenInput = FailOnDataOpen(
            spec, CliFixtures.IndexBoundSpec, data, CliFixtures.WideData, failFrom: 0, family);

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(data)), harness.StdErr);
    }

    [Fact]
    public async Task Plan_WhenTheCalibrationPassFailsMidRead_ThenItIsTheSameCodelessDataFailure()
    {
        // Not only the open: a source that starts fine and then breaks is the same failure.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Resolve("data.csv");
        var opened = 0;
        var harness = new CliTestHarness();
        harness.OpenInput = path =>
        {
            if (string.Equals(path, spec, StringComparison.Ordinal))
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(ObservedDomainSpec), writable: false);
            }

            var bytes = Encoding.UTF8.GetBytes("colour\nred\ngreen\n");
            return opened++ == 0 ? new MemoryStream(bytes, writable: false) : new BreakingStream(bytes, after: 4);
        };

        var exit = await harness.RunAsync("plan", spec, data);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(data)), harness.StdErr);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    public async Task Pipeline_WhenALaterDataPassThrowsABroadContractException_ThenItStaysExitFour(string command)
    {
        // The counterexamples that keep the classification honest. DecoderFallbackException
        // derives from ArgumentException and ObjectDisposedException from
        // InvalidOperationException, and both are exit 1 above — so the classifier must be
        // matching those specific types, not their bases. A plain instance of either base is a
        // contract/state defect and must still be the sanitized internal fault.
        foreach (var family in new[] { nameof(ArgumentException), nameof(InvalidOperationException) })
        {
            using var temp = TempDirectory.Create();
            var spec = temp.Write("spec.toml", ObservedDomainSpec);
            var data = temp.Resolve("data.csv");
            var harness = new CliTestHarness();
            harness.OpenInput = FailOnDataOpen(spec, ObservedDomainSpec, data, "colour\nred\n", failFrom: 1, family);

            var exit = await harness.RunAsync(command, spec, data);

            Assert.Equal(4, exit);
            Assert.Equal(string.Empty, harness.StdOut);
            Assert.Equal(DiagnosticRenderer.RenderHostError(CliHost.UnexpectedFaultMessage), harness.StdErr);
        }
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("fingerprint")]
    public async Task Pipeline_WhenStandardOutputFails_ThenItIsTheOutputFailureExit(string command)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness
        {
            OutOverride = new ThrowingWriter(failOnWrite: true, failOnFlush: false),
        };

        var exit = await harness.RunAsync(
            command,
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData));

        Assert.Equal(1, exit);
        Assert.Equal(DiagnosticRenderer.RenderHostError(CliHost.OutputFailureMessage), harness.StdErr);
    }

    // ---- boundaries this slice must not move ------------------------------------------------------

    [Fact]
    public async Task Validate_WhenTheSpecWouldNeedCalibration_ThenItStillReadsNoRows()
    {
        // validate is schema validation, not a dry run: it must not acquire the pipeline's
        // calibration pass merely because one now exists next door.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", ObservedDomainSpec);
        var data = temp.Write("data.csv", CliFixtures.WideDataWithHostileRows);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("validate", spec, data);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal([spec, data], harness.Opened);
    }

    [Theory]
    [InlineData("calibrate", "--out", "-")]
    public async Task Pipeline_WhenALaterSlicesCommandRuns_ThenItIsStillDeferredAndOpensNothing(
        string command, params string[] options)
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        string[] argv =
        [
            command,
            temp.Write("spec.toml", CliFixtures.IndexBoundSpec),
            temp.Write("data.csv", CliFixtures.WideData),
            .. options,
        ];

        var exit = await harness.RunAsync(argv);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"the '{command}' command is not implemented yet."), harness.StdErr);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public void CommandTable_ThenExactlyTheImplementedCommandsHaveAHandler()
    {
        // The wiring is the whole production change to the table; calibrate must still be
        // waiting for its own slice.
        var handled = CommandTable.Commands
            .Where(command => command.Handler is not null)
            .Select(command => command.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["convert", "fingerprint", "migrate", "plan", "probe", "stats", "validate"], handled);
        Assert.All(ReportCommands, name => Assert.NotNull(CommandTable.Find(name)!.Handler));
    }

    // ---- helpers -----------------------------------------------------------------------------------

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

    // Serves the spec from memory, the DATA normally until <paramref name="failFrom"/>, and then
    // throws the named family from the open — so a test can let schema acquisition succeed and
    // fail only a later pass.
    private static Func<string, Stream> FailOnDataOpen(
        string specPath, string specText, string dataPath, string dataText, int failFrom, string family)
    {
        var opened = 0;
        return path =>
        {
            if (string.Equals(path, specPath, StringComparison.Ordinal))
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(specText), writable: false);
            }

            if (!string.Equals(path, dataPath, StringComparison.Ordinal))
            {
                throw new FileNotFoundException("unexpected path", path);
            }

            return opened++ >= failFrom
                ? throw Create(family)
                : new MemoryStream(Encoding.UTF8.GetBytes(dataText), writable: false);
        };
    }

    private static Exception Create(string family) => family switch
    {
        nameof(SourceReadException) => new SourceReadException("the source could not be read."),
        nameof(IOException) => new IOException("the device is not ready."),
        nameof(UnauthorizedAccessException) => new UnauthorizedAccessException("access is denied."),
        nameof(ObjectDisposedException) => new ObjectDisposedException("stream"),
        nameof(DecoderFallbackException) => new DecoderFallbackException("the bytes are not valid UTF-8."),
        nameof(InvalidDataException) => new InvalidDataException("the source is corrupt."),
        nameof(ArgumentException) => new ArgumentException("a contract violation, not a read failure."),
        nameof(InvalidOperationException) => new InvalidOperationException("a state defect, not a read failure."),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown exception family."),
    };

    /// <summary>A source that reads a prefix and then fails, like a truncated or unplugged device.</summary>
    private sealed class BreakingStream(byte[] bytes, int after) : MemoryStream(bytes, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            Position >= after ? throw new IOException("the device is not ready.") : base.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            Position >= after ? throw new IOException("the device is not ready.") : base.Read(buffer);
    }

    // Serves the spec from memory and the DATA from a per-open script, so successive passes can
    // differ deterministically — no file mutation, no timing race (CX-M7P-008). The last entry
    // repeats once the script runs out.
    private static Func<string, Stream> PassIndexed(
        string specPath, string specText, string dataPath, params string[] dataPasses)
    {
        var index = 0;
        return path =>
        {
            if (string.Equals(path, specPath, StringComparison.Ordinal))
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(specText), writable: false);
            }

            if (!string.Equals(path, dataPath, StringComparison.Ordinal))
            {
                throw new FileNotFoundException("unexpected path", path);
            }

            var pass = dataPasses[Math.Min(index++, dataPasses.Length - 1)];
            return new MemoryStream(Encoding.UTF8.GetBytes(pass), writable: false);
        };
    }
}
