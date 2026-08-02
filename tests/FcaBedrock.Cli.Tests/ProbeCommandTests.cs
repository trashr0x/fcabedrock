using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The probe vertical (D-122 part 10; spec §7.1), end to end through argv.
/// <para>
/// M7 adds no discovery semantics, so every draft-content case compares the command's bytes
/// against an <b>independently executed</b> library route — the same source, the same settings,
/// the same options, run through <see cref="Prober"/> and <see cref="SpecWriter"/> by the test
/// itself. What is tested here is the argv mapping and the CLI's own consequences: exit codes,
/// which stream carries what, when a diagnostic becomes visible, and what the filesystem holds
/// afterwards.
/// </para>
/// </summary>
public sealed class ProbeCommandTests
{
    // Every value of the second column is the missing token, so that attribute observes nothing.
    private const string AllMissingWide = "colour,note\nred,?\ngreen,?\n";

    // A triple source WITH a header, which is what header-name role addressing requires (§5.3).
    private const string NamedTriple = "s,p,v\no1,colour,red\no2,colour,green\n";

    // o1 recurs after o2 intervened — legal under `unordered`, a contiguity Error under an
    // explicitly selected `subject_grouped` (§5.3.1).
    private const string InterleavedTriple = "o1,colour,red\no2,colour,green\no1,size,1\n";

    // Every predicate is empty, so a triple probe discovers no vocabulary to author (§7.1).
    private const string PredicatelessTriple = "o1,,red\no2,,green\n";

    // ---- the library oracle -------------------------------------------------------------
    //
    // Never the command's own helpers: the settings and options below are built here, from the
    // library factories, to state what each argv line means.

    private static async Task<string> WideDraftAsync(
        string dataPath, SourceReadSettings settings, ProbeOptions? options = null)
    {
        var session = new WideCsvSession(() => File.OpenRead(dataPath), settings);
        var probed = await Prober.ProbeAsync(session, settings, options);
        Assert.True(probed.TryGetValue(out var document));
        return SpecWriter.Write(document);
    }

    private static async Task<string> TripleDraftAsync(
        string dataPath,
        SourceReadSettings settings,
        TripleColumnsSection? columns = null,
        ProbeOptions? options = null)
    {
        var session = new TripleCsvSession(() => File.OpenRead(dataPath), settings);
        var probed = await Prober.ProbeTripleAsync(session, settings, columns, options);
        Assert.True(probed.TryGetValue(out var document));
        return SpecWriter.Write(document);
    }

    private static string Render(IEnumerable<BedrockDiagnostic> diagnostics)
    {
        var writer = new StringWriter();
        DiagnosticRenderer.Write(writer, diagnostics);
        return writer.ToString();
    }

    // ---- draft bytes through argv -------------------------------------------------------

    [Fact]
    public async Task Probe_WhenWideSourceIsProbed_ThenStdoutIsExactlyTheLibraryDraftBytes()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-");

        Assert.Equal(0, exit);
        Assert.Equal(await WideDraftAsync(data, SourceReadSettings.CreateWide()), harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Probe_WhenTripleSourceIsProbed_ThenStdoutIsExactlyTheLibraryDraftBytes()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "triple", "--out", "-");

        Assert.Equal(0, exit);
        Assert.Equal(await TripleDraftAsync(data, SourceReadSettings.CreateTriple()), harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Probe_WhenReadSettingsAreOmitted_ThenTheDraftAuthorsTheShapeSpecificDefaults()
    {
        // §7.1's self-documenting draft: every effective §5.1 setting is authored even though
        // the invocation supplied none, and wide's has_header default is true.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-");

        Assert.Contains("shape = \"wide\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("encoding = \"utf-8\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("delimiter = \",\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("quote_char = \"\\\"\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("has_header = true", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("locale = \"invariant\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("missing_token = \"?\"", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenTripleSettingsAreOmitted_ThenOrderingAndRolesAreAuthoredExplicitly()
    {
        // Triple's own shape-specific defaults: has_header false, ordering unordered, and the
        // §5.3 role map authored explicitly at 0/1/2 rather than left to a reader's assumption.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        await harness.RunAsync("probe", data, "--shape", "triple", "--out", "-");

        Assert.Contains("has_header = false", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("ordering = \"unordered\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "columns = { subject = 0, predicate = 1, value = 2 }", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenRolesAreAllIndices_ThenTheDraftPreservesIndexAddressingVerbatim()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "triple", "--out", "-",
            "--subject", "0", "--predicate", "1", "--value", "2");

        var roles = new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2));
        Assert.Equal(0, exit);
        Assert.Equal(await TripleDraftAsync(data, SourceReadSettings.CreateTriple(), roles), harness.StdOut);
        Assert.Contains(
            "columns = { subject = 0, predicate = 1, value = 2 }", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenRolesAreAllNamesWithHeaderTrue_ThenTheDraftPreservesNameAddressingVerbatim()
    {
        // Names, not the indices they resolve to: the addressing mode the user typed survives.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", NamedTriple);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "triple", "--out", "-", "--header", "true",
            "--subject", "s", "--predicate", "p", "--value", "v");

        var roles = new TripleColumnsSection(new NameColumnRef("s"), new NameColumnRef("p"), new NameColumnRef("v"));
        var settings = SourceReadSettings.CreateTriple(hasHeader: true);
        Assert.Equal(0, exit);
        Assert.Equal(await TripleDraftAsync(data, settings, roles), harness.StdOut);
        Assert.Contains(
            "columns = { subject = \"s\", predicate = \"p\", value = \"v\" }",
            harness.StdOut,
            StringComparison.Ordinal);
    }

    // ---- retention (D-108) ---------------------------------------------------------------

    [Fact]
    public async Task Probe_WhenLimitIsOmitted_ThenTheDraftRecordsTheDefaultRetentionLimit()
    {
        // The always-written notes (§7.1), including at zero truncations, so a complete-looking
        // draft still says which limit produced it. 100,000 is D-108's default.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-");

        Assert.Equal(0, exit);
        Assert.Contains(
            "probe: per-attribute retention limit 100000; truncated attributes: 0.",
            harness.StdOut,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenLimitTruncatesADomain_ThenThePrefixIncludePolicyAndMarkerAreAuthored()
    {
        // Both columns hold two distinct values, so a limit of 1 truncates both: each authors its
        // first-observation prefix plus the include policy that recovers the rest at convert,
        // and one aggregated Warning reports it while the exit stays 0.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-", "--limit", "1");

        Assert.Equal(0, exit);
        Assert.Contains("declared_domain = [\"red\"]", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains("unknown_value_policy = \"include\"", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "probe: domain truncated after 1 distinct values; more exist.",
            harness.StdOut,
            StringComparison.Ordinal);
        Assert.Contains(
            "probe: per-attribute retention limit 1; truncated attributes: 2.",
            harness.StdOut,
            StringComparison.Ordinal);
        Assert.Single(harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("warning ProbeDomainTruncated:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenTheDomainFitsTheLimitExactly_ThenNothingIsTruncated()
    {
        // The strictly-greater boundary (D-108): two distinct values at a limit of two is a
        // complete domain, so no marker, no include policy, and no warning.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-", "--limit", "2");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.DoesNotContain("unknown_value_policy", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("more exist", harness.StdOut, StringComparison.Ordinal);
        Assert.Contains(
            "probe: per-attribute retention limit 2; truncated attributes: 0.",
            harness.StdOut,
            StringComparison.Ordinal);
    }

    // ---- what the draft must NOT carry ---------------------------------------------------

    [Fact]
    public async Task Probe_WhenEveryObservationIsMissing_ThenTheDraftOmitsDeclaredDomain()
    {
        // §7.1's closing rule and D-122 part 15: the field is OMITTED, never authored as [],
        // which since D-122 would mean a fixed EMPTY domain rather than "calibrate this".
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", AllMissingWide);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-");

        Assert.Equal(0, exit);
        Assert.Contains("name = \"note\"", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("declared_domain = []", harness.StdOut, StringComparison.Ordinal);
        Assert.Equal(
            1,
            harness.StdOut.Split("declared_domain", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Probe_WhenADraftIsProduced_ThenItStoresNoFingerprintTimestampOrToolVersion()
    {
        // §7.1: a probe draft is never a frozen artifact, there is no clock, and no tool version
        // is stamped — even though the harness has both a fixed clock and a version string.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-");

        Assert.DoesNotContain("fingerprint", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("created_at", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("tool_version", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.ToolVersion, harness.StdOut, StringComparison.Ordinal);
    }

    // ---- library-owned failures, forwarded unchanged --------------------------------------

    [Fact]
    public async Task Probe_WhenTheDataFileIsMissing_ThenProbeSourceReadFailedIsReportedAndExitIsOne()
    {
        // The CLI opens nothing itself, so the first open happens inside the library's schema
        // acquisition and the failure is Discovery's own diagnostic — never a second
        // classification, and never a code-less host line beside it (D-067).
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", temp.Resolve("absent.csv"), "--shape", "wide", "--out", "-");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("error ProbeSourceReadFailed:", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("error: ", harness.StdErr, StringComparison.Ordinal);
        Assert.Single(harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Probe_WhenTheRoleMapIsInvalid_ThenTheResolversOwnDiagnosticsAreForwarded()
    {
        // The role map is checked by resolving the exact [binding] the probe would author,
        // before any row is read. The CLI adds no code of its own and re-validates nothing.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.TripleData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "triple", "--out", "-",
            "--subject", "0", "--predicate", "0", "--value", "2");

        var roles = new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(0), new IndexColumnRef(2));
        var settings = SourceReadSettings.CreateTriple();
        var session = new TripleCsvSession(() => File.OpenRead(data), settings);
        var oracle = await Prober.ProbeTripleAsync(session, settings, roles);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal([data], harness.Opened);
        Assert.NotEmpty(oracle.Diagnostics);
        Assert.Equal(Render(oracle.Diagnostics), harness.StdErr);
    }

    [Fact]
    public async Task Probe_WhenOrderingIsSubjectGrouped_ThenItIsAuthoredAndContiguityIsEnforced()
    {
        // subject_grouped is explicit-only (§7.1) and carries the contiguity requirement with
        // it: the same input is a clean draft under `unordered` and an Error under this one.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", InterleavedTriple);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "triple", "--out", "-", "--ordering", "subject_grouped");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("error TripleSubjectNotContiguous:", harness.StdErr, StringComparison.Ordinal);

        var grouped = new CliTestHarness();
        Assert.Equal(0, await grouped.RunAsync("probe", data, "--shape", "triple", "--out", "-"));
        Assert.Contains("ordering = \"unordered\"", grouped.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenNoAttributesAreDiscovered_ThenNoDocumentIsWrittenAndExitIsOne()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", PredicatelessTriple);
        var target = temp.Resolve("draft.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "triple", "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("error ProbeNoAttributesDiscovered:", harness.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(target), "an Error must publish nothing");
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    // ---- the grammar the parser owns ------------------------------------------------------

    [Theory]
    [InlineData("--max-attributes")]
    [InlineData("--max-values")]
    [InlineData("--max-value-text")]
    public async Task Probe_WhenAGuardSpellingIsSupplied_ThenItIsAnUnknownOptionUsageError(string guard)
    {
        // The three aggregate guards stay pinned library defaults with no CLI flag (§7.1,
        // D-110), so their spellings are ordinary unknown options.
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", "d.csv", "--shape", "wide", "--out", "-", guard, "5");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.StartsWith($"error: unknown option '{guard}'", harness.StdErr, StringComparison.Ordinal);
    }

    // ---- delivery: which stream, which bytes ----------------------------------------------

    [Fact]
    public async Task Probe_WhenOutIsStdout_ThenStdoutIsExactlyTheDocumentAndNothingElse()
    {
        // A warning-producing run: the document is the whole of stdout, the warning is on
        // stderr, and the exit stays 0.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-", "--limit", "1");

        var settings = SourceReadSettings.CreateWide();
        Assert.Equal(0, exit);
        Assert.Equal(
            await WideDraftAsync(data, settings, ProbeOptions.Create(valueRetentionLimit: 1)), harness.StdOut);
        Assert.Contains("warning ProbeDomainTruncated:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenOutIsAFile_ThenStdoutIsEmpty()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Resolve("draft.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(
            await WideDraftAsync(data, SourceReadSettings.CreateWide()),
            await File.ReadAllTextAsync(target, new UTF8Encoding(false)));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    // ---- the publication matrix through argv ----------------------------------------------

    [Fact]
    public async Task Probe_WhenTheTargetExistsWithoutForce_ThenItIsRefusedAndTheOldFileIsUntouched()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"the output '{target}' already exists; use --force to replace it."),
            harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Probe_WhenForceIsSupplied_ThenTheDistinctTargetIsReplaced()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", target, "--force");

        Assert.Equal(0, exit);
        Assert.Equal(
            await WideDraftAsync(data, SourceReadSettings.CreateWide()),
            await File.ReadAllTextAsync(target, new UTF8Encoding(false)));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task Probe_WhenTheOutputIsTheDataFile_ThenItIsRefusedEvenWithForce()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", data, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"the output '{data}' and the input '{data}' are the same file."),
            harness.StdErr);
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(data));
    }

    // ---- the deferred-diagnostic ordering (D-122 part 2; D-112) ----------------------------

    [Fact]
    public async Task Probe_WhenAWarningIsProducedAndTheFileCommits_ThenTheWarningIsRenderedOnceAfterCommitAndExitIsZero()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Resolve("draft.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "wide", "--out", target, "--limit", "1");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Single(harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("warning ProbeDomainTruncated:", harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Probe_WhenAWarningIsProducedAndPublicationFails_ThenTheWarningAndOneHostLineAreRenderedOnceAndExitIsOne()
    {
        // The held warning becomes visible only once the failure is known, and then exactly
        // once, in library order, followed by exactly one sanitized code-less line.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailKind = "Move";
        harness.PublicationFiles.FailMoveTo = "draft.toml";

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "wide", "--out", target, "--force", "--limit", "1");

        var lines = harness.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("warning ProbeDomainTruncated:", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{target}'.").TrimEnd('\n'), lines[1]);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Probe_WhenAWarningIsProducedAndTheRunIsCancelledAtStageCreation_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.MutateBefore = "Confidential:draft.toml.fcabedrock-stage-T";
        harness.PublicationFiles.Mutate = harness.Signals.Cancel;

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "wide", "--out", target, "--force", "--limit", "1");

        await AssertSilentCancellationAsync(harness, temp, target);
        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task Probe_WhenAWarningIsProducedAndTheRunIsCancelledAtTheBackupTransition_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.CancelAfterMoveToPrefix = "draft.toml.fcabedrock-backup-";
        harness.PublicationFiles.CancelAfterMove = harness.Signals.Cancel;

        var exit = await harness.RunAsync(
            "probe", data, "--shape", "wide", "--out", target, "--force", "--limit", "1");

        await AssertSilentCancellationAsync(harness, temp, target);
        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task Probe_WhenOutIsStdoutAndTheRunIsCancelled_ThenExitIsThreeWithNoOutputAtAll()
    {
        // Cancelled at the first input open, so the run stops inside the library read and
        // neither stream carries anything; the warning-bearing proof is the two cases above.
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var harness = new CliTestHarness();
        harness.OpenInput = path =>
        {
            harness.Signals.Cancel();
            return File.OpenRead(path);
        };

        var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", "-", "--limit", "1");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    // ---- shared cancellation oracle --------------------------------------------------------

    // Silent exit 3 means all of: nothing on either stream, the old target byte-identical, no
    // committed new file, and no object left claiming the private transaction namespace.
    private static async Task AssertSilentCancellationAsync(
        CliTestHarness harness, TempDirectory temp, string target)
    {
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    private static bool Residue(string path) =>
        Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal);
}
