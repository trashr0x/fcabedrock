using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>convert</c> vertical at the argv boundary (D-122 parts 4–7): the committed bytes, the
/// target names, the stdout/stderr/exit contract, the <c>--v2-compat</c> override, the size
/// advisory, and the rule that an invalid run publishes nothing.
/// <para>
/// Every expected artifact below is derived from its spec text and data alone: by hand from the
/// two data rows of a small fixture, or from the rule that generates the rows of the large one.
/// So the byte locks are an independent statement of what the format is, not a recording of
/// what the writer did.
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
        // stripped, so `--out out.cxt` publishes `out.cxt.cxt`.
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
        // No calibration pass at all (the spec declares its domain), so the data is opened once
        // for the schema and then once per pass the selected writers need: two for the
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
        // to stdout. It does not: `-` names an ordinary file, and stdout is still exactly empty.
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
        // trailing space on non-empty .dat lines: the full §8 native surface at once.
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
        // diagnostics are the run's failure, and they only appear for the run that has them.
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
    public async Task Convert_WhenTheSpecDelimiterIsHash_ThenSpecFieldInvalidExit1()
    {
        // §5.1.1: the spec read refuses a '#' delimiter, so nothing is read or published.
        using var run = ConvertRun.Wide(
            CliFixtures.IndexBoundSpec.Replace("shape = \"wide\"", "shape = \"wide\"\ndelimiter = \"#\"", StringComparison.Ordinal));

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Contains(
            "error SpecFieldInvalid: [binding] key 'delimiter' expects TAB or one character from U+001F to U+007E other than '#' (§5.1.1).",
            run.Harness.StdErr,
            StringComparison.Ordinal);
        Assert.DoesNotContain(run.Data, run.Harness.Opened);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenAnEmitErrorOccurs_ThenEveryStagedArtifactIsDiscarded()
    {
        // unknown_value_policy = "fail" turns the out-of-domain value into an Error at emit. The
        // enumeration still completes (the aggregate needs the whole population), so bytes were
        // staged, and every one of them is discarded rather than committed.
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

    // ---- a halt before any pass reaches the end of DATA --------------------------------------
    //
    // The halt cases below place a structural halt (an unusable or duplicate object key, or a
    // non-contiguous subject) about halfway through DATA, with far more data after it than the
    // reader reads ahead. Every pass over that unchanged input stops before the end of DATA (each
    // emit pass at the halting record), so no input digest completes, though nothing failed to
    // read. Unless a signal cancels the run, the halt's diagnostic is the whole report. Each halt
    // case first checks that no read reached the end of DATA, so it cannot pass for the wrong
    // reason if the reader ever reads further ahead.

    [Theory]
    [InlineData("dat", false)]
    [InlineData("cxt", false)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    public async Task Convert_WhenAnUnusableKeyHaltsBeforeAnyPassReachesTheEnd_ThenItsDiagnosticIsTheWholeReport(
        string format, bool noManifest)
    {
        using var run = ConvertRun.Wide(KeyedSpec, KeyedData(",red"));
        var ends = 0;
        run.Harness.OpenInput = CountingEnds(run.Data, () => ends++);

        var exit = await run.ConvertAsync(Options(format, noManifest));

        Assert.True(ends == 0, $"a read reached the end of DATA {ends} time(s)");
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(UnusableKeyLine(HaltRecord), run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Theory]
    [InlineData("dat", false)]
    [InlineData("cxt", true)]
    public async Task Convert_WhenADuplicateKeyHaltsBeforeAnyPassReachesTheEnd_ThenItsDiagnosticIsTheWholeReport(
        string format, bool noManifest)
    {
        // duplicate_object_policy defaults to "fail" (§6.1), so repeating an earlier key halts.
        using var run = ConvertRun.Wide(KeyedSpec, KeyedData("k0000010,red"));
        var ends = 0;
        run.Harness.OpenInput = CountingEnds(run.Data, () => ends++);

        var exit = await run.ConvertAsync(Options(format, noManifest));

        Assert.True(ends == 0, $"a read reached the end of DATA {ends} time(s)");
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(DuplicateKeyLine, run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenANonContiguousSubjectHaltsBeforeAnyPassReachesTheEnd_ThenItsDiagnosticIsTheWholeReport()
    {
        // The same rule for a triple halt: under ordering = "subject_grouped", a subject recurs
        // after its group has closed.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", GroupedTripleSpec);
        var data = temp.Write("data.csv", GroupedTripleData());
        var ends = 0;
        harness.OpenInput = CountingEnds(data, () => ends++);

        var exit = await harness.RunAsync("convert", spec, data, "--out", temp.Resolve("out"), "--format", "both");

        Assert.True(ends == 0, $"a read reached the end of DATA {ends} time(s)");
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(NonContiguousSubjectLine, harness.StdErr);
        Assert.Empty(Directory.GetFiles(temp.Path, "out*"));
    }

    [Fact]
    public async Task Convert_WhenATinyInputHaltsAfterAPassReachedTheEnd_ThenItsDiagnosticIsTheWholeReport()
    {
        // The control for the halt cases: the whole input arrives in the reader's first fill, so a
        // pass reaches the end and completes the input digest before the halt.
        using var run = ConvertRun.Wide(KeyedSpec, "id,colour\nk1,red\n,green\nk3,blue\n");
        var ends = 0;
        run.Harness.OpenInput = CountingEnds(run.Data, () => ends++);

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.True(ends > 0, "no read reached the end of DATA");
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(UnusableKeyLine(1), run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenAValidInputSpansManyReaderFills_ThenTheRunCommitsWithItsRawHashes()
    {
        // The success side of the same boundary: a pass that reads to the end completes the digest
        // however many fills it takes, and the manifest records the raw SHA-256 of DATA and of both
        // committed files.
        using var run = ConvertRun.Wide(KeyedSpec, KeyedData(null));
        var ends = 0;
        run.Harness.OpenInput = CountingEnds(run.Data, () => ends++);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.True(ends > 0, "no read reached the end of DATA");
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Equal(
            $"B\n\n{KeyedRows}\n3\n\n"
                + string.Concat(Enumerable.Range(0, KeyedRows).Select(i => $"k{i:D7}\n"))
                + "colour-red\ncolour-green\ncolour-blue\n"
                + string.Concat(Enumerable.Range(0, KeyedRows).Select(i => Incidence[i % 3] + "\n")),
            run.Text(".cxt"));
        Assert.Equal(string.Concat(Enumerable.Range(0, KeyedRows).Select(i => $"{(i % 3) + 1}\n")), run.Text(".dat"));

        var manifest = run.Text(".manifest.toml");
        Assert.Contains($"input_hash = \"{HashOf(File.ReadAllBytes(run.Data))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{HashOf(run.Bytes(".cxt"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{HashOf(run.Bytes(".dat"))}\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenAMalformedFieldFollowsTheReadAhead_ThenItStaysTheCodelessDataFailure()
    {
        // A read failure is not a halt: DATA could not be read, so the code-less DATA error is the
        // whole report.
        using var run = ConvertRun.Wide(KeyedSpec, KeyedData("k0030000,re\"d"));

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(run.Data)), run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenTheDataChangesAndTheChangedBytesHalt_ThenTheChangeIsReportedAfterTheHalt()
    {
        // Tiny inputs complete a pass on every read. The schema pass reads valid bytes and the .dat
        // pass reads bytes that halt at record 1, so two completed passes disagree. The changed
        // input is the run's failure: its code-less error follows the halt's diagnostic.
        using var run = ConvertRun.Wide(KeyedSpec, "id,colour\nk1,red\nk2,green\n");
        var opens = 0;
        run.Harness.OpenInput = path =>
        {
            if (!string.Equals(path, run.Data, StringComparison.Ordinal))
            {
                return File.OpenRead(path);
            }

            var pass = opens++ == 0 ? "id,colour\nk1,red\nk2,green\n" : "id,colour\nk1,red\n,green\n";
            return new MemoryStream(Encoding.UTF8.GetBytes(pass), writable: false);
        };

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            UnusableKeyLine(1) + DiagnosticRenderer.RenderHostError(RunPipeline.InputChangedMessage(run.Data)),
            run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenTheSignalArrivesAfterAHaltedPassIsStaged_ThenTheRunIsCancelledAndReportsNothing()
    {
        // The signal lands as the halted .dat stage closes: the halt's diagnostic exists and the
        // run's result checks have not run yet. Cancellation still wins: nothing is reported or
        // committed, and the run's private files are removed.
        using var run = ConvertRun.Wide(KeyedSpec, KeyedData(",red"));
        var ends = 0;
        run.Harness.OpenInput = CountingEnds(run.Data, () => ends++);
        run.Harness.PublicationFiles.MutateBefore = "StreamClose:out.dat.fcabedrock-stage-T";
        run.Harness.PublicationFiles.Mutate = run.Harness.Signals.Cancel;

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.True(ends == 0, $"a read reached the end of DATA {ends} time(s)");
        Assert.Equal(1, run.Harness.PublicationFiles.MutationsFired);
        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
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
        // §8 gives the threshold exactly two readings, a positive threshold or 0 to disable, so
        // reading a negative one as "disabled" would invent a third. The spec reader refuses it
        // (D-135), so the run stops at the read with the registry diagnostic, never reaches the
        // writer, and creates nothing.
        using var run = ConvertRun.Wide(CliFixtures.IndexBoundSpec + "\n[output.cxt]\nsize_advisory_bytes = -1\n");

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            $"file=\"{Path.GetFullPath(run.Spec).Replace("\\", "\\\\")}\" line=15 column=23: error SpecFieldInvalid: "
            + "[output.cxt] key 'size_advisory_bytes' is -1; expected 0 (disables the advisory) or a positive number of bytes (§8).\n",
            run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Convert_WhenBaseIndexIsNeitherZeroNorOne_ThenTheRunIsRefusedBeforeAnythingIsCreated()
    {
        // §8 allows base_index 1 or 0. The spec reader refuses any other value (D-135), so the
        // run publishes nothing.
        using var run = ConvertRun.Wide(CliFixtures.IndexBoundSpec + "\n[output.dat]\nbase_index = 5\n");

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            $"file=\"{Path.GetFullPath(run.Spec).Replace("\\", "\\\\")}\" line=15 column=14: error SpecFieldInvalid: "
            + "[output.dat] key 'base_index' is 5; expected 1 (the default) or 0 (§8).\n",
            run.Harness.StdErr);
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

    // Keyed wide rows `k0000000,red`, `k0000001,green`, `k0000002,blue` and so on under an
    // `id,colour` header: about 840 KB, in which every declared value occurs and every object
    // crosses one column. A test replaces record HaltRecord with a halting or malformed row; it
    // starts about 420 KB in, well past the reader's first fill, and about 420 KB of rows follow it.
    private const int KeyedRows = 60_000;

    private const int HaltRecord = 30_000;

    private const string KeyedSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [binding.object_key]
        mode = "column"
        column = 0

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 1 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green", "blue"]
        """;

    private const string GroupedTripleSpec = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "subject_grouped"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "colour"
        source = { kind = "predicate", name = "colour" }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green", "blue"]
        """;

    private const string DuplicateKeyLine =
        "record=30000: error DuplicateObjectKey: The wide object key 'k0000010' at record 30000 duplicates an "
        + "earlier record; duplicate_object_policy = \\\"fail\\\" (§6.1).\n";

    private const string NonContiguousSubjectLine =
        "record=30000: error TripleSubjectNotContiguous: Triple subject 's0000005' recurs at record 30000 after "
        + "an intervening subject; ordering = \\\"subject_grouped\\\" requires contiguous subjects (§5.3).\n";

    private static readonly string[] Colours = ["red", "green", "blue"];

    private static readonly string[] Incidence = ["X..", ".X.", "..X"];

    // The rendered ObjectKeyValueInvalid line for the empty key at `record`.
    private static string UnusableKeyLine(int record) =>
        $"record={record}: error ObjectKeyValueInvalid: The wide object key at record {record} is empty, "
        + "whitespace-only, a missing token, absent, or contains a control character; it cannot name an object (§5.4).\n";

    // KeyedRows keyed rows, with `replacement`, when given, in place of record HaltRecord.
    private static string KeyedData(string? replacement) =>
        "id,colour\n" + string.Concat(Enumerable.Range(0, KeyedRows).Select(i =>
            (i == HaltRecord && replacement is not null ? replacement : $"k{i:D7},{Colours[i % 3]}") + "\n"));

    // One subject_grouped triple per subject, except that record HaltRecord repeats `s0000005` after
    // its group has closed.
    private static string GroupedTripleData() =>
        string.Concat(Enumerable.Range(0, KeyedRows).Select(i =>
            (i == HaltRecord ? "s0000005,colour,red" : $"s{i:D7},colour,{Colours[i % 3]}") + "\n"));

    private static string[] Options(string format, bool noManifest) =>
        noManifest ? ["--format", format, "--no-manifest"] : ["--format", format];

    // Opens every input as a file except DATA, whose bytes are served from memory by a stream that
    // reports each read reaching the end of DATA.
    private static Func<string, Stream> CountingEnds(string data, Action reachedEnd)
    {
        var bytes = File.ReadAllBytes(data);
        return path =>
        {
            if (string.Equals(path, data, StringComparison.Ordinal))
            {
                return new EndCountingStream(bytes, reachedEnd);
            }

            return File.OpenRead(path);
        };
    }

    private static string HashOf(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

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

    /// <summary>
    /// DATA's bytes served from memory, reporting each read that reached the end of the input: a
    /// read that returned nothing for a non-empty request. A pass completes its input digest only on
    /// such a read.
    /// </summary>
    private sealed class EndCountingStream(byte[] bytes, Action reachedEnd) : MemoryStream(bytes, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count) => Count(base.Read(buffer, offset, count), count);

        public override int Read(Span<byte> buffer) => Count(base.Read(buffer), buffer.Length);

        private int Count(int read, int requested)
        {
            if (read == 0 && requested > 0)
            {
                reachedEnd();
            }

            return read;
        }
    }
}

/// <summary>
/// One temporary convert run: a spec, a data source, an output base, and the harness that drives
/// it. The base is absolute so no test depends on (or changes) the process working directory.
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
    /// Every file in the run's directory whose name carries the private transaction marker
    /// <c>.fcabedrock-</c> (a record, a stage, a backup or another control file), in ordinal order.
    /// Tests assert it is empty where they expect the run's cleanup to have completed, and inspect
    /// what it holds where they expect files to remain.
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
    /// Every <c>out*</c> file's name paired with its exact bytes: the oracle a rollback test
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
