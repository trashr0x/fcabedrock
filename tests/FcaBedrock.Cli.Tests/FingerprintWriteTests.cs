using System.Text;
using FcaBedrock.Cli.Commands;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>fingerprint --write</c> vertical (D-122 part 10; D-123 point 14; spec §3/§13/§14).
/// <para>
/// Report mode's byte grammar is locked next door in <see cref="FingerprintCommandTests"/> and is
/// untouched here; what is tested is write mode's own contract — the fully-frozen gate, which
/// document is rewritten and what survives, and the D-123 point 14 rule that a <b>file</b> target
/// keeps report-on-stdout plus spec-in-file while <c>--out -</c> emits only the spec.
/// </para>
/// </summary>
public sealed class FingerprintWriteTests
{
    private static bool Residue(string path) =>
        Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal);

    private static void AssertNoMutation(CliTestHarness harness) =>
        Assert.DoesNotContain(
            harness.PublicationFiles.Operations,
            operation => operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                || operation.StartsWith("Confidential:", StringComparison.Ordinal)
                || operation.StartsWith("Move:", StringComparison.Ordinal)
                || operation.StartsWith("Delete:", StringComparison.Ordinal));

    private static Dictionary<string, byte[]> Snapshot(TempDirectory temp) =>
        Directory.GetFiles(temp.Path).ToDictionary(
            path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);

    private static string[] Lines(string stream) => stream.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static SpecDocument Parse(string text)
    {
        var read = SpecReader.Read(text);
        Assert.True(read.TryGetValue(out var document));
        return document;
    }

    private static async Task<SpecDocument> ParseFileAsync(string path) =>
        Parse(await File.ReadAllTextAsync(path, new UTF8Encoding(false)));

    // The three states a report line can carry, asserted structurally so the pinned sha256 values
    // stay owned by FingerprintCommandTests and are not duplicated here.
    private static void AssertReport(string stdout, string state)
    {
        var lines = Lines(stdout);
        Assert.Equal(3, lines.Length);
        Assert.Collection(
            lines,
            line => AssertReportLine(line, "schema_fingerprint", state),
            line => AssertReportLine(line, "cxt_output_fingerprint", state),
            line => AssertReportLine(line, "dat_output_fingerprint", state));
        Assert.EndsWith("\n", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', stdout);
    }

    private static void AssertReportLine(string line, string field, string state)
    {
        Assert.Matches($"^{field} computed=sha256:[0-9a-f]{{64}} stored={state}$", line);
    }

    // ---- the committed spec and the post-commit report (D-123 point 14) ------------------------

    [Fact]
    public async Task FingerprintWrite_WhenTheSpecIsFullyFrozen_ThenTheCorrectedSpecIsCommittedAndTheReportFollowsOnStdout()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Resolve("new.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);

        // The input authored none, so every state describes the file as it was on entry.
        AssertReport(harness.StdOut, "absent");
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);

        // The committed file differs from the input in the three stored fields and nothing else.
        var input = Parse(CliFixtures.FrozenSpec);
        var written = await ParseFileAsync(target);
        Assert.NotNull(written.Spec!.SchemaFingerprint);
        Assert.NotNull(written.Spec.CxtOutputFingerprint);
        Assert.NotNull(written.Spec.DatOutputFingerprint);
        Assert.Equal(
            input.Spec! with { SchemaFingerprint = null, CxtOutputFingerprint = null, DatOutputFingerprint = null },
            written.Spec with { SchemaFingerprint = null, CxtOutputFingerprint = null, DatOutputFingerprint = null });
    }

    [Fact]
    public async Task FingerprintWrite_WhenOutIsStdout_ThenStdoutIsExactlyTheCorrectedSpecAndTheReportIsSuppressed()
    {
        // FBL-M7P-002's suppression clause, and it holds STRUCTURALLY: the stdout path is never
        // handed the committed payload at all.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", "-");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.DoesNotContain("computed=", harness.StdOut, StringComparison.Ordinal);

        // The independently produced library form: the root document with only the three fields
        // replaced, serialized by the canonical writer.
        var corrected = await CorrectedViaLibraryAsync(spec, data);
        Assert.Equal(corrected, harness.StdOut);

        Assert.Equal(
            [Path.GetFileName(data), Path.GetFileName(spec)],
            Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        AssertNoMutation(harness);
    }

    // The corrected document produced through the library alone: read the root, compute this
    // run's native fingerprints the way the pipeline does, apply the record `with`, serialize.
    private static async Task<string> CorrectedViaLibraryAsync(string specPath, string dataPath)
    {
        var text = await File.ReadAllTextAsync(specPath, new UTF8Encoding(false));
        var root = Parse(text);

        var settings = SpecResolver.ResolveReadSettings(root);
        Assert.True(settings.TryGetValue(out var readSettings));

        var session = new FcaBedrock.Sources.WideCsvSession(() => File.OpenRead(dataPath), readSettings);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);

        var resolvedResult = SpecResolver.Resolve(root, schema);
        Assert.True(resolvedResult.TryGetValue(out var resolved));

        var calibrated = FcaBedrock.Core.Calibration.CalibratedSpec.FromFullyDeclared(resolved.Resolved);
        var planned = FcaBedrock.Core.Planning.ConversionPlanner.Plan(
            calibrated, FcaBedrock.Core.Discretization.LabelStyle.Native);
        Assert.True(planned.TryGetValue(out var plan));

        var computed = SpecFingerprints.ComputeNative(resolved, plan);
        return SpecWriter.Write(root with
        {
            Spec = root.Spec! with
            {
                SchemaFingerprint = computed.SchemaFingerprint,
                CxtOutputFingerprint = computed.CxtOutputFingerprint,
                DatOutputFingerprint = computed.DatOutputFingerprint,
            },
        });
    }

    // ---- the fully-frozen gate (§14) --------------------------------------------------------------

    public static TheoryData<string, string?> GateReasons() => new()
    {
        { "omitted-domain", "ObservedDomainUsed" },
        { "omitted-domain-include", "ObservedDomainUsed" },
        { "authored-domain-include", "UnknownValuePolicyInclude" },
        { "passthrough", "ValueGroupsPassthroughDataDependent" },
        { "equal-frequency", null },
        { "equal-width-min-max", null },
        { "equal-width-percentile", null },
    };

    [Theory]
    [MemberData(nameof(GateReasons))]
    public async Task FingerprintWrite_WhenTheSpecIsNotFullyFrozen_ThenItIsRefusedAndAnyPreparationWarningStillRenders(
        string reason, string? warning)
    {
        // Every row calibrates SUCCESSFULLY and is refused by the gate, not by a failed
        // calibration. HostFailure renders preparation diagnostics first and the code-less line
        // second, so the four discovery rows carry their Warning then the error, and the three
        // cut rows — whose successful calibration is silent — carry the error alone.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.NotFullyFrozenSpec(reason));
        var data = temp.Write("data.csv", CliFixtures.GateData);
        var target = temp.Resolve("new.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);

        var expectedError = DiagnosticRenderer
            .RenderHostError(FingerprintCommand.WriteRequiresFullyFrozenMessage(spec))
            .TrimEnd('\n');
        var lines = Lines(harness.StdErr);

        if (warning is null)
        {
            Assert.Equal(expectedError, Assert.Single(lines));
        }
        else
        {
            Assert.Equal(2, lines.Length);
            Assert.Contains($"warning {warning}", lines[0], StringComparison.Ordinal);
            Assert.Equal(expectedError, lines[1]);
        }

        Assert.False(File.Exists(target));
        Assert.Equal(
            [Path.GetFileName(data), Path.GetFileName(spec)],
            Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        AssertNoMutation(harness);
    }

    // ---- root, not composed (§13 rule 8) ------------------------------------------------------------

    [Fact]
    public async Task FingerprintWrite_WhenTheRootExtendsABase_ThenExtendsIsPreservedAndOnlyTheStoredFieldsChange()
    {
        // The deliberate opposite of calibrate: the AUTHORED ROOT is rewritten, so its extends
        // survives verbatim and the chain is not flattened.
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("frozen-base.toml", CliFixtures.FrozenChainBaseSpec);
        var root = temp.Write("root.toml", CliFixtures.FrozenChainRootSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Resolve("new.toml");
        var baseBefore = await File.ReadAllBytesAsync(basePath);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", root, data, "--write", "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        AssertReport(harness.StdOut, "absent");

        var text = await File.ReadAllTextAsync(target, new UTF8Encoding(false));
        Assert.Contains("extends = \"frozen-base.toml\"", text, StringComparison.Ordinal);

        var input = Parse(CliFixtures.FrozenChainRootSpec);
        var written = Parse(text);
        Assert.Equal("frozen-base.toml", written.Spec!.Extends);
        Assert.Equal(
            input.Spec! with { SchemaFingerprint = null, CxtOutputFingerprint = null, DatOutputFingerprint = null },
            written.Spec with { SchemaFingerprint = null, CxtOutputFingerprint = null, DatOutputFingerprint = null });

        Assert.Equal(baseBefore, await File.ReadAllBytesAsync(basePath));
    }

    [Fact]
    public async Task FingerprintWrite_WhenTheRootHasOtherSections_ThenEveryNonSpecSectionIsUnchanged()
    {
        using var temp = TempDirectory.Create();
        temp.Write("frozen-base.toml", CliFixtures.FrozenChainBaseSpec);
        var root = temp.Write("root.toml", CliFixtures.FrozenChainRootSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Resolve("new.toml");
        var harness = new CliTestHarness();

        Assert.Equal(0, await harness.RunAsync("fingerprint", root, data, "--write", "--out", target));

        var input = Parse(CliFixtures.FrozenChainRootSpec);
        var written = await ParseFileAsync(target);

        Assert.Equal(input.Provenance, written.Provenance);
        Assert.Equal(input.Binding, written.Binding);
        Assert.Equal(input.Defaults, written.Defaults);
        Assert.Equal(input.Output, written.Output);
        Assert.Equal(input.Templates, written.Templates);
        Assert.Equal(input.Matchers, written.Matchers);
        Assert.Equal(input.Attributes, written.Attributes);
    }

    // ---- idempotence and stale correction --------------------------------------------------------

    [Fact]
    public async Task FingerprintWrite_WhenTheStoredFieldsAlreadyMatch_ThenARepeatedWriteIsByteIdentical()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var first = temp.Resolve("first.toml");
        var second = temp.Resolve("second.toml");

        var one = new CliTestHarness();
        Assert.Equal(0, await one.RunAsync("fingerprint", spec, data, "--write", "--out", first));
        AssertReport(one.StdOut, "absent");

        // The second run's INPUT is the first run's output, so its states read match.
        var two = new CliTestHarness();
        Assert.Equal(0, await two.RunAsync("fingerprint", first, data, "--write", "--out", second));
        AssertReport(two.StdOut, "match");
        Assert.Equal(string.Empty, two.StdErr);

        Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
    }

    [Fact]
    public async Task FingerprintWrite_WhenTheStoredFieldsAreStale_ThenTheWrittenSpecVerifiesSilently()
    {
        // The report OWNS the stored-versus-computed comparison, so a stale input reads
        // stored=stale on stdout and emits NO *FingerprintStale Warning: one fact, one place.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenStaleSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Resolve("new.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target);

        Assert.Equal(0, exit);
        AssertReport(harness.StdOut, "stale");
        Assert.Equal(string.Empty, harness.StdErr);

        var verified = new CliTestHarness();
        Assert.Equal(0, await verified.RunAsync("fingerprint", target, data));
        Assert.Equal(string.Empty, verified.StdErr);
        AssertReport(verified.StdOut, "match");
    }

    // ---- publication (D-122 part 4) ----------------------------------------------------------------

    public static TheoryData<string> CollisionTargets() => new() { "data", "root", "base" };

    [Theory]
    [MemberData(nameof(CollisionTargets))]
    public async Task FingerprintWrite_WhenTheOutputIsAnInputFile_ThenItIsRefusedEvenWithForce(string which)
    {
        // DATA, the root SPEC, and every composed chain file. As with calibrate, the base row
        // cannot pass unless the chain is threaded through as publication inputs.
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("frozen-base.toml", CliFixtures.FrozenChainBaseSpec);
        var root = temp.Write("root.toml", CliFixtures.FrozenChainRootSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var before = Snapshot(temp);
        var harness = new CliTestHarness();

        // The base's authored spelling is its referrer-relative extends reference, not its path.
        var (target, spelling) = which switch
        {
            "data" => (data, data),
            "root" => (root, root),
            _ => (basePath, "frozen-base.toml"),
        };

        var exit = await harness.RunAsync(
            "fingerprint", root, data, "--write", "--out", target, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{target}' and the input '{spelling}' are the same file."),
            harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        AssertNoMutation(harness);
    }

    [Fact]
    public async Task FingerprintWrite_WhenTheTargetExistsWithoutForce_ThenItIsRefusedAndTheOldFileIsUntouched()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Write("new.toml", "the old file");
        var before = Snapshot(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target);

        Assert.Equal(1, exit);

        // No report leaks past a refusal.
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{target}' already exists; use --force to replace it."),
            harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        AssertNoMutation(harness);
    }

    [Fact]
    public async Task FingerprintWrite_WhenForceIsSupplied_ThenTheDistinctTargetIsReplaced()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Write("new.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target, "--force");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        AssertReport(harness.StdOut, "absent");

        var moves = harness.PublicationFiles.Operations
            .Where(operation => operation.StartsWith("Move:", StringComparison.Ordinal))
            .Select(RecordingPublicationFileSystem.Fold)
            .ToList();
        Assert.Equal("Move:new.toml->new.toml.fcabedrock-backup-T", moves[^2]);
        Assert.Equal("Move:new.toml.fcabedrock-stage-T->new.toml", moves[^1]);
        Assert.NotEqual("the old file", await File.ReadAllTextAsync(target, new UTF8Encoding(false)));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task FingerprintWrite_WhenPublicationFails_ThenNoReportReachesStdout()
    {
        // The COMMIT rename fails, after the backup — so this is the restore path, and the
        // report must not appear because the run never reached its commit point.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Write("new.toml", "the old file");
        var before = Snapshot(temp);
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailKind = "Move";
        harness.PublicationFiles.FailMoveTo = "new.toml";

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{target}'."), harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task FingerprintWrite_WhenTheRunIsCancelledBeforeCommit_ThenExitIsThreeWithNoReportAndNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Write("new.toml", "the old file");
        var before = Snapshot(temp);
        var harness = new CliTestHarness();
        harness.PublicationFiles.CancelAfterMoveToPrefix = "new.toml.fcabedrock-backup-";
        harness.PublicationFiles.CancelAfterMove = harness.Signals.Cancel;

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target, "--force");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task FingerprintWrite_WhenStageCreationFails_ThenTheOldTargetSurvivesAndNoReportIsPrinted()
    {
        // A different seam from the commit-rename case above: the failure lands inside the
        // confidential stage creation, BEFORE any backup rename and before any commit — so the
        // old target was never moved and needs no restore, and the message is StageFailed rather
        // than CommitFailed. --force is what makes the seam reachable at all: without it the
        // existing-target gate refuses before a stage is ever created.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.FrozenSpec);
        var data = temp.Write("data.csv", CliFixtures.FrozenData);
        var target = temp.Write("new.toml", "the old file");
        var before = Snapshot(temp);
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailCreateNewPrefix = "new.toml.fcabedrock-stage-";

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot write the output '{target}'."), harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);

        // The failure preceded both target renames: the old file was never moved aside, which is
        // why it needs no restore. (The transaction's own pending→record rename is unrelated
        // bookkeeping and legitimately happened.)
        var targetMoves = harness.PublicationFiles.Operations
            .Select(RecordingPublicationFileSystem.Fold)
            .Where(operation => operation.StartsWith("Move:", StringComparison.Ordinal))
            .Where(operation =>
                operation.EndsWith("->new.toml", StringComparison.Ordinal)
                || operation.Contains("->new.toml.fcabedrock-backup-", StringComparison.Ordinal));
        Assert.Empty(targetMoves);
    }

    // ---- a warning alongside a committed write ------------------------------------------------------

    [Fact]
    public async Task FingerprintWrite_WhenAFullyFrozenSpecWarnsAndTheFileCommits_ThenTheWarningIsOnStderrAndTheReportOnStdout()
    {
        // WarningOnlySpec is reused unedited: it is fully frozen (an explicit non-empty domain
        // under identity, no include), and its restrict_to value outside that domain is its ONLY
        // diagnostic. The two streams are asserted independently.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.WarningOnlySpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Resolve("new.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("fingerprint", spec, data, "--write", "--out", target);

        Assert.Equal(0, exit);
        AssertReport(harness.StdOut, "absent");

        // The rendered line: the attribute location, then the code, then the library message with
        // its embedded quotes JSON-escaped per the D-122 part 3 grammar.
        Assert.Equal(
            "attribute=\"colour\": warning RestrictToValueNotInDomain: restrict_to value \\\"blue\\\""
                + " on attribute 'colour' is not in its declared_domain (§10.4).",
            Assert.Single(Lines(harness.StdErr)));

        Assert.True(File.Exists(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }
}
