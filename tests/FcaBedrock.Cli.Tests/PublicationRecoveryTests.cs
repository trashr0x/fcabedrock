using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// What a transaction may and may not touch, and how it proves it.
/// <para>
/// Every case here pushes at one question: <b>does this transaction own this file?</b> A pending
/// record is owned only when its intent descriptor says so; a final is owned only when the durable
/// identity evidence proves it is the very object the transaction staged; a backup is restored only
/// when it is the very object the transaction renamed aside. Anything unproven is preserved
/// byte-for-byte, and the location is inspected directly to say so — a mocked call order would
/// prove none of it.
/// </para>
/// </summary>
public sealed class PublicationRecoveryTests
{
    private const string Token = "aaaaaaaabbbbbbbbccccccccdddddddd";

    // The six-bit record shape a fresh manifest-bearing `--format cxt` run writes: stage CXT (bit
    // 0) and stage manifest (bit 2), no backups. Spelled out rather than computed, so a change to
    // the encoding fails the test that depends on it instead of silently following it.
    private const string CxtAndManifestShape = "05";

    // ---- the intent descriptor: the only authority over a pending record ------------------------

    [Fact]
    public async Task Publication_WhenTheIntentDescriptorIsInterrupted_ThenARetryConverges()
    {
        // A crash immediately after the acknowledgement is complete on disk. Two files exist: the
        // pending record this run created, and the descriptor naming that exact object and carrying
        // its own canonical body. Together they are a complete, provable statement of what
        // happened, so the next run clears both.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter =
            $"StreamClose:out.fcabedrock-intent-T-{CxtAndManifestShape}-T-T";

        await run.ConvertAsync("--format", "cxt");
        Assert.True(run.Harness.PublicationFiles.Crashed, "the crash after the intent descriptor never fired");

        var residue = run.Residue();
        Assert.Equal(2, residue.Count);
        var intent = Assert.Single(residue, name => name.Contains(".fcabedrock-intent-", StringComparison.Ordinal));
        Assert.NotEqual(0, new FileInfo(Path.Combine(run.Directory, intent)).Length);
        Assert.Contains(residue, name => name.Contains(".fcabedrock-pending-", StringComparison.Ordinal));

        var retry = new CliTestHarness();
        Assert.Equal(0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheIntentDescriptorsBodyNeverLands_ThenItIsPreservedAndTheRunRefuses()
    {
        // The other side of the same boundary. A descriptor whose body did not land whole is not a
        // descriptor: it proves nothing, so it is neither trusted nor removed, and the run says so
        // — every time, having changed nothing.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter =
            $"CreateNew:out.fcabedrock-intent-T-{CxtAndManifestShape}-T-T";

        await run.ConvertAsync("--format", "cxt");
        Assert.True(run.Harness.PublicationFiles.Crashed, "the crash after the intent creation never fired");

        var intent = Directory.GetFiles(run.Directory, "out.fcabedrock-intent-*").Single();
        Assert.Empty(await File.ReadAllBytesAsync(intent));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
            Assert.Contains(
                "unrecognized fcabedrock transaction residue", retry.StdErr, StringComparison.Ordinal);
            Assert.Empty(await File.ReadAllBytesAsync(intent));
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    [Theory]
    [InlineData("StreamWrite:out.fcabedrock-pending-T")]
    [InlineData("StreamClose:out.fcabedrock-pending-T")]
    [InlineData("Move:out.fcabedrock-pending-T->out.fcabedrock-transaction-T.toml")]
    public async Task Publication_WhenRecordCreationIsInterrupted_ThenARetryConvergesWithoutHelp(string transition)
    {
        // Partway through the record's bytes, at its close, and immediately after
        // the rename that publishes it: each leaves a different residue, and none blocks the base.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter = transition;

        await run.ConvertAsync("--format", "cxt");
        Assert.True(run.Harness.PublicationFiles.Crashed, $"the crash after '{transition}' never fired");
        Assert.NotEmpty(run.Residue());

        var retry = new CliTestHarness();
        Assert.Equal(0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));

        Assert.Empty(run.Residue());
        Assert.Equal("B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", run.Text(".cxt"));
    }

    [Fact]
    public async Task Publication_WhenAnIntentDescriptorAuthorizesItsPendingRecord_ThenBothAreCleared()
    {
        // The hand-built form of the same state: a half-written pending record, beside a descriptor
        // whose name reproduces the record it was going to publish AND names that object's own
        // identity — the acknowledgement that makes it removable however its bytes were left.
        using var run = ConvertRun.Wide();
        var pending = Path.Combine(run.Directory, $"out.fcabedrock-pending-{Token}");
        await File.WriteAllTextAsync(pending, "version = 1\ntoken =");
        var intent = WriteIntent(run.Directory, "out", Token, [("stage", "out.cxt")]);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt", "--no-manifest"));

        Assert.False(File.Exists(intent));
        Assert.False(File.Exists(pending));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAnIntentDescriptorsBodyIsNotCanonical_ThenItIsRefusedAndLeftUntouched()
    {
        // The descriptor is authoritative only through its exact canonical body, which is why
        // removing one destroys nothing else: other bytes under that name are not ours.
        using var run = ConvertRun.Wide();
        var intent = WriteIntent(run.Directory, "out", Token, [("stage", "out.cxt")]);
        await File.WriteAllTextAsync(intent, "not ours");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("not ours", await File.ReadAllTextAsync(intent));
    }

    [Theory]

    // A digest that is not the digest of the record its own shape describes.
    [InlineData("05", "00000000000000000000000000000000")]

    // A shape no transaction writes: the manifest staged alone.
    [InlineData("04", null)]

    // A shape with a backup for an artifact it never stages.
    [InlineData("10", null)]
    public async Task Publication_WhenAnIntentDescriptorIsInconsistent_ThenItIsRefusedAndLeftUntouched(
        string shape, string? digest)
    {
        // Self-validating: the shape must name a transaction this code could have created, and the
        // digest must be the digest of the exact record bytes that shape produces.
        using var run = ConvertRun.Wide();
        digest ??= new string('a', 32);
        var name = $"out.fcabedrock-intent-{Token}-{shape}-{digest}";
        var path = Path.Combine(run.Directory, name);
        await File.WriteAllBytesAsync(path, []);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    // ---- identity evidence: fixed paths, published atomically -----------------------------------

    [Fact]
    public async Task Publication_WhenEveryControlPathIsResolved_ThenAllOfThemArePreflightedBeforeAnythingIsCreated()
    {
        // Every control name is a function of the base, the role, and the token — no runtime value
        // appears in any of them — so the complete set is resolvable before the transaction begins,
        // and the identity preflight sees all of it while the location is still untouched.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out");

        var probe = new RecordingIdentityProbe();
        var files = new RecordingPublicationFileSystem();
        var preparation = PublicationTransaction.Preflight(
            files,
            () => new FileIdentity(probe),
            basePath,
            [PublicationTargetKind.Cxt, PublicationTargetKind.Manifest],
            [new PublicationInput(data, data), new PublicationInput(spec, spec)],
            force: false,
            CancellationToken.None);

        Assert.IsType<PublicationReady>(preparation);

        // The token is unpredictable, so it is read back from the very set under test.
        var record = probe.Names.Single(name =>
            name.StartsWith("out.fcabedrock-transaction-", StringComparison.Ordinal));
        var token = record["out.fcabedrock-transaction-".Length..^".toml".Length];

        string[] expected =
        [
            $"out.fcabedrock-transaction-{token}.toml",
            $"out.fcabedrock-pending-{token}",
            $"out.fcabedrock-staged-{token}",
            $"out.fcabedrock-rollback-{token}",
            $"out.fcabedrock-committed-{token}",
            $"out.fcabedrock-e-c-{token}",
            $"out.fcabedrock-ep-c-{token}",
            $"out.fcabedrock-e-m-{token}",
            $"out.fcabedrock-ep-m-{token}",
            $"out.cxt.fcabedrock-stage-{token}",
            $"out.manifest.toml.fcabedrock-stage-{token}",
        ];

        foreach (var name in expected)
        {
            Assert.Contains(name, probe.Names);
        }

        // The intent descriptor and the stage claims are deliberately NOT here: each carries the
        // identity digest of an object that does not exist until the acquisition it acknowledges
        // has succeeded, so neither can be resolved in advance — and by the same construction
        // neither can name a pre-existing file.
        Assert.DoesNotContain(
            probe.Names, name => name.StartsWith("out.fcabedrock-intent-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            probe.Names, name => name.StartsWith("out.fcabedrock-sc-", StringComparison.Ordinal));

        // And none of it exists yet: preflight decides, it does not create.
        Assert.DoesNotContain(files.Operations, operation =>
            operation.StartsWith("CreateNew:", StringComparison.Ordinal)
            || operation.StartsWith("Confidential:", StringComparison.Ordinal)
            || operation.StartsWith("Move:", StringComparison.Ordinal)
            || operation.StartsWith("Delete:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publication_WhenAManifestIsOnlyDemoted_ThenItsEvidenceRecordsNoStage()
    {
        // A backup-only participant: the old marker is renamed aside and never republished, so its
        // evidence names the object it was and nothing else.
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old marker");

        run.Harness.PublicationFiles.CrashAfter = "CreateNew:out.fcabedrock-staged-T";
        await run.ConvertAsync("--format", "cxt", "--no-manifest", "--force");
        Assert.True(run.Harness.PublicationFiles.Crashed, "the crash after the staged marker never fired");

        var evidence = Directory.GetFiles(run.Directory, "out.fcabedrock-e-m-*").Single();
        var lines = (await File.ReadAllTextAsync(evidence)).Split('\n');

        Assert.Equal("version = 1", lines[0]);
        Assert.Equal("target = \"out.manifest.toml\"", lines[3]);
        Assert.Equal("stage = \"none\"", lines[5]);
        Assert.Matches("^backup = \"[0-9a-f]{32}\"$", lines[4]);
    }

    [Fact]
    public async Task Publication_WhenEvidenceIsStillPending_ThenARetryPreservesItAndSaysSo()
    {
        // A crash between creating the pending evidence and publishing it. The record names that
        // derived path — but a name is not a statement that this transaction's create-new produced
        // the object at it, and nothing else names one. The interrupted run could have proved it
        // from the identity its own creation reported; a resumed one cannot, so it removes nothing
        // and reports that it could not finish.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        await File.WriteAllTextAsync(residue.EvidencePendingPath("c"), "version = 1\ntoken =");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt", "--no-manifest"));
        Assert.Contains(
            "cannot clean up an incomplete fcabedrock run", run.Harness.StdErr, StringComparison.Ordinal);

        Assert.Equal("version = 1\ntoken =", await File.ReadAllTextAsync(residue.EvidencePendingPath("c")));
        Assert.True(File.Exists(residue.RecordPath));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenPendingEvidenceHasNoRecord_ThenItIsRefusedAndLeftUntouched()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        await File.WriteAllTextAsync(residue.EvidencePendingPath("c"), "version = 1\ntoken =");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("version = 1\ntoken =", await File.ReadAllTextAsync(residue.EvidencePendingPath("c")));
    }

    [Fact]
    public async Task Publication_WhenAuthoritativeEvidenceIsMalformed_ThenRecoveryRefusesAndTouchesNothing()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        await File.WriteAllTextAsync(residue.EvidencePath("c"), "version = 1\nnot the rest of it\n");
        residue.WriteMarker("staged");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("half-written", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        Assert.True(File.Exists(residue.RecordPath));
    }

    [Fact]
    public async Task Publication_WhenEvidenceContradictsTheRecord_ThenRecoveryRefusesAndTouchesNothing()
    {
        // The record reserved no backup for this target, so evidence claiming one describes a
        // transaction that could not have written it.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteEvidence("c", "out.cxt", new string('b', 32), residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(residue.EvidencePath("c")));
    }

    [Fact]
    public async Task Publication_WhenAStagedTransactionIsMissingEvidence_ThenRecoveryRefusesAndTouchesNothing()
    {
        // Evidence precedes the staged marker, so a staged transaction without it is a state no
        // run reaches — and it is precisely the state in which ownership could not be proved.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "the old cxt");
        residue.WriteMarker("staged");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the published bytes");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the published bytes", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Equal("the old cxt", await File.ReadAllTextAsync(residue.PrivatePath("backup", "out.cxt")));
    }

    [Fact]
    public async Task Publication_WhenAStagedTransactionStillHasPendingEvidence_ThenRecoveryRefuses()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteEvidence(
            "c", "out.cxt", IdentityEvidence.NotApplicable, residue.Identity("stage", "out.cxt"));
        await File.WriteAllTextAsync(residue.EvidencePendingPath("c"), string.Empty);
        residue.WriteMarker("staged");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(residue.EvidencePendingPath("c")));
    }

    // ---- unproven identity never authorizes destruction -----------------------------------------

    [Theory]
    [InlineData("unknown")]
    [InlineData("00112233445566778899aabbccddeeff")]
    public async Task Publication_WhenTheFinalIsNotTheStagedObject_ThenRollbackPreservesIt(string stage)
    {
        // The record says this transaction staged something and the marker says it got as far as
        // rolling back — but the file at the target is not the object it staged, whether because
        // the host reported no identity at all or because the identity does not match. Either way
        // it is not ours to delete.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        await File.WriteAllTextAsync(run.Target(".cxt"), "not this run's output");
        residue.WriteEvidence("c", "out.cxt", IdentityEvidence.NotApplicable, stage);
        residue.WriteMarker("staged");
        residue.WriteMarker("rollback");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal("not this run's output", await File.ReadAllTextAsync(run.Target(".cxt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_WhenTheBackupIsNotTheObjectItRenamedAside_ThenItIsNeitherMovedNorDeleted(
        bool committed)
    {
        // A substituted backup is an object this transaction never renamed aside —
        // whichever direction cleanup would take. Backward it must not be moved home; forward it
        // must not be dropped as superseded residue. The layout authorizes neither, so the run
        // refuses before mutation and every file stays exactly as it was.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "substituted");

        if (committed)
        {
            await File.WriteAllTextAsync(run.Target(".cxt"), "the committed cxt");
        }

        residue.WriteEvidence(
            "c",
            "out.cxt",
            "00112233445566778899aabbccddeeff",
            committed
                ? residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt")
                : residue.ConsumedIdentity("stage", "out.cxt"));
        residue.WriteMarker("staged");
        if (committed)
        {
            residue.WriteMarker("committed");
        }
        else
        {
            residue.WriteMarker("rollback");
        }

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("substituted", await File.ReadAllTextAsync(residue.PrivatePath("backup", "out.cxt")));
        Assert.True(File.Exists(residue.RecordPath));
        Assert.True(File.Exists(residue.EvidencePath("c")));
    }

    [Fact]
    public async Task Publication_WhenTheHostReportsNoStageIdentity_ThenNothingIsPublished()
    {
        // The commit point may be crossed only when the object about to become public
        // is provably the one this transaction wrote and hashed. A host that cannot identify it
        // supplies no such proof, so publication fails closed rather than certifying bytes it
        // cannot recognize — and it fails before anything becomes visible.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.SuppressStageIdentity = true;

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));

        // The object that host created is left exactly where it is: removal is bound to the exact
        // created object, and this is precisely the host that cannot name one. What
        // survives is classifiable — a record, and the stage beside it.
        var residue = run.Residue();
        Assert.Contains(residue, name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains("out.cxt.fcabedrock-stage-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publication_WhenTheHostReportsNoStageIdentity_ThenAPriorRunIsUntouched()
    {
        // And the previous run keeps every byte: the failure lands before the first backup rename,
        // so nothing of it is even renamed aside.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.SuppressStageIdentity = true;

        Assert.Equal(
            1,
            await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));

        // Every public byte of the previous run, unchanged — the failure lands before the first
        // backup rename. The transaction's own residue stays too: the object the host could not
        // name is never reclaimed on a length.
        foreach (var (name, bytes) in before)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(run.Directory, name)));
        }

        Assert.Contains(
            run.Residue(), name => name.Contains("out.cxt.fcabedrock-stage-", StringComparison.Ordinal));
    }

    // ---- impossible staged and rolling-back layouts ---------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_WhenAStageOnlyTargetHasBothItsStageAndItsFinal_ThenRecoveryRefuses(bool rollingBack)
    {
        // The commit rename is non-overwriting and consumes the stage, so this pair
        // cannot both exist. Reading it as rollback-owned would delete a file no run published.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "private stage");
        await File.WriteAllTextAsync(run.Target(".cxt"), "keep me");
        residue.WriteEvidence(
            "c", "out.cxt", IdentityEvidence.NotApplicable, residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");
        if (rollingBack)
        {
            residue.WriteMarker("rollback");
        }

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("keep me", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Equal("private stage", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        Assert.True(File.Exists(residue.RecordPath));
        Assert.True(File.Exists(residue.EvidencePath("c")));
    }

    [Fact]
    public async Task Publication_WhenABackedUpTargetIsOccupiedByAnUnownedFile_ThenRecoveryRefuses()
    {
        // The backup rename emptied the target path; a file there now is either this run's own
        // commit — which the evidence would prove — or something else entirely.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "the old cxt");
        residue.WritePrivate("stage", "out.cxt", "not committed");
        await File.WriteAllTextAsync(run.Target(".cxt"), "someone else's file");
        residue.WriteEvidence(
            "c", "out.cxt", residue.Identity("backup", "out.cxt"), residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Equal("the old cxt", await File.ReadAllTextAsync(residue.PrivatePath("backup", "out.cxt")));
    }

    [Fact]
    public async Task Publication_WhenADemotedMarkerReappearedBesideItsBackup_ThenRecoveryRefuses()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.manifest.toml"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.manifest.toml", "the demoted marker");
        residue.WritePrivate("stage", "out.cxt", "not committed");
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "a new marker");
        residue.WriteEvidence(
            "m",
            "out.manifest.toml",
            residue.Identity("backup", "out.manifest.toml"),
            IdentityEvidence.NotApplicable);
        residue.WriteEvidence(
            "c", "out.cxt", IdentityEvidence.NotApplicable, residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt", "--no-manifest"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("a new marker", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
    }

    // ---- the cleanup tails ------------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenForwardCleanupRemovedItsMarkersButNotItsRecord_ThenTheCommittedRunStands()
    {
        // The tail of a successful run: backups gone, every phase marker gone, the record not yet
        // deleted. It reads as Preparing — which claims nothing — so the published files stand and
        // only the residue goes.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.manifest.toml"), ("stage", "out.cxt")]);
        await File.WriteAllTextAsync(run.Target(".cxt"), "the committed cxt");

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the committed cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenRollbackCleanupRemovedItsMarkersButNotItsRecord_ThenThePriorSetStands()
    {
        // The mirror tail: a completed rollback whose markers are gone. Everything is back where
        // preflight found it, and the record's removal is all that is left.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        await File.WriteAllTextAsync(run.Target(".cxt"), "the restored old cxt");

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the restored old cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- races against the current transaction ----------------------------------------------------

    [Fact]
    public async Task Publication_WhenATargetAppearsBeforeItsCommitRename_ThenItIsPreservedAndNothingIsPublished()
    {
        // Preflight found nothing here, so the record reserved no backup — and a
        // rollback that read "no backup entry" as "this must be mine" would delete a file this run
        // never touched.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-stage-T->out.cxt";
        run.Harness.PublicationFiles.Mutate = () => File.WriteAllText(run.Target(".cxt"), "keep me");

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);
        Assert.Equal("keep me", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenATargetDisappearsBeforeItsBackupRename_ThenNothingIsCommitted()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var second = new CliTestHarness();
        // Sealing is the last thing before the commit's own revalidation, so this is the race the
        // check is there to catch: the target changes after preflight approved it.
        second.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-staged-T";
        second.PublicationFiles.Mutate = () => File.Delete(run.Target(".cxt"));

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            second.StdErr);

        // The target that vanished is gone — this run did not remove it — but nothing new was
        // published over the rest of the previous set, and no residue survives.
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Equal(before[Path.GetFileName(run.Target(".dat"))], await File.ReadAllBytesAsync(run.Target(".dat")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenATargetIsReplacedBeforeItsBackupRename_ThenTheReplacementIsNotRenamedAside()
    {
        // The path is the same; the object is not. Renaming aside what preflight never approved is
        // exactly the destruction the revalidation exists to prevent.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var second = new CliTestHarness();
        // Sealing is the last thing before the commit's own revalidation, so this is the race the
        // check is there to catch: the target changes after preflight approved it.
        second.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-staged-T";
        second.PublicationFiles.Mutate = () =>
        {
            File.Delete(run.Target(".cxt"));
            File.WriteAllText(run.Target(".cxt"), "a different object");
        };

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");

        Assert.Equal(1, exit);
        Assert.Equal("a different object", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenATargetBecomesAnAliasOfTheDataBeforeItsBackupRename_ThenTheDataSurvives()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var second = new CliTestHarness();
        var linked = false;
        // Sealing is the last thing before the commit's own revalidation, so this is the race the
        // check is there to catch: the target changes after preflight approved it.
        second.PublicationFiles.MutateBefore = "CreateNew:out.fcabedrock-staged-T";
        second.PublicationFiles.Mutate = () =>
        {
            File.Delete(run.Target(".cxt"));
            linked = PlatformLinks.TryCreateHardLink(run.Target(".cxt"), run.Data, out _);
        };

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");

        if (!linked)
        {
            Assert.Skip("hard links are unavailable on this host.");
        }

        Assert.Equal(1, exit);
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAnAppearedTargetBlockedTheCommit_ThenAForcedRetryConverges()
    {
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-stage-T->out.cxt";
        run.Harness.PublicationFiles.Mutate = () => File.WriteAllText(run.Target(".cxt"), "keep me");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt", "--no-manifest"));

        var retry = new CliTestHarness();
        Assert.Equal(
            0,
            await retry.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--no-manifest", "--force"));

        Assert.Equal("B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", run.Text(".cxt"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAStageIsSubstitutedBeforeItsCommit_ThenTheImpostorIsNeverTakenAsOurs()
    {
        // Evidence identifies the OBJECT the transaction created, not the path it created it at.
        // Here the path is repopulated with a different object after the stage is closed, so the
        // file the commit rename moves is not the one this run staged. It is not deleted — it is
        // not this transaction's — and it is not left at the published path either: the rename that
        // put it there is reversed, so a failed run publishes nothing at all.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-stage-T->out.cxt";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            var stage = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
            File.Delete(stage);
            File.WriteAllText(stage, "an impostor");
        };

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.False(File.Exists(run.Target(".cxt")));

        // Preserved, back where the rename took it from, and never deleted as owned. The record
        // survives with it, so the state stays classifiable rather than looking like a clean base.
        var occupant = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
        Assert.Equal("an impostor", await File.ReadAllTextAsync(occupant));
        Assert.Contains(
            run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
    }

    // ---- cancellation across the whole pre-commit path ---------------------------------------------

    [Fact]
    public async Task Publication_WhenTheSignalArrivesAfterResidueDiscovery_ThenNoTransactionBegins()
    {
        // The prior transaction is left exactly as it was, and — the point of the
        // check — this run creates no record, no stage, and no descriptor of its own.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteStageClaim("c", "out.cxt");

        run.Harness.PublicationFiles.MutateBefore = "EnumerateFiles:out";
        run.Harness.PublicationFiles.Mutate = run.Harness.Signals.Cancel;

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.True(File.Exists(residue.RecordPath));
        Assert.Equal("half-written", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        Assert.False(File.Exists(run.Target(".cxt")));

        // Strictly resumable: an ordinary retry finishes the prior run and publishes.
        var retry = new CliTestHarness();
        Assert.Equal(0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheSignalArrivesBetweenRecoveryMutations_ThenTheResidueStaysResumable()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt"), ("stage", "out.dat")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WritePrivate("stage", "out.dat", "half-written too");
        residue.WriteStageClaim("c", "out.cxt");
        residue.WriteStageClaim("d", "out.dat");

        run.Harness.PublicationFiles.MutateBefore = "Delete:out.cxt.fcabedrock-stage-T";
        run.Harness.PublicationFiles.Mutate = run.Harness.Signals.Cancel;

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdErr);

        // Part-way through, and still valid: the record survives with whatever it still owns.
        Assert.True(File.Exists(residue.RecordPath));

        var retry = new CliTestHarness();
        Assert.Equal(0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both"));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheSignalArrivesDuringSealing_ThenTheRunRollsBackAndExitsThree()
    {
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "StreamWrite:out.fcabedrock-ep-c-T";
        run.Harness.PublicationFiles.Mutate = run.Harness.Signals.Cancel;

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- the evidence boundaries: failure family and origin ----------------------------------------

    [Fact]
    public async Task Publication_WhenEvidenceCannotBeCreated_ThenNothingIsPublishedAndNoResidueSurvives()
    {
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailCreateNewPrefix = "out.fcabedrock-ep-";

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{run.Base}'."),
            run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Theory]
    [InlineData("out.fcabedrock-ep-", true)]
    [InlineData("out.fcabedrock-ep-", false)]
    [InlineData("out.fcabedrock-pending-", true)]
    [InlineData("out.fcabedrock-pending-", false)]
    [InlineData("out.fcabedrock-staged-", true)]
    [InlineData("out.fcabedrock-staged-", false)]
    public async Task Publication_WhenAControlStreamFails_ThenTheFamilyDecidesTheExitCode(string prefix, bool contract)
    {
        // Failure ORIGIN and failure FAMILY are separate questions, at every
        // control-file boundary as much as at an artifact's: a genuine I/O failure is an ordinary
        // environment problem, while a broken call contract is a product defect that must not be
        // disguised as one.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailStreamWritePrefix = prefix;
        run.Harness.PublicationFiles.FailStreamClosePrefix = prefix;
        if (contract)
        {
            run.Harness.PublicationFiles.FailStreamWith = static () => new ArgumentException("bad range");
            run.Harness.PublicationFiles.FailStreamCloseWith = static () => new ArgumentException("bad state");
        }

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(contract ? 4 : 1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                contract
                    ? CliHost.UnexpectedFaultMessage
                    : $"cannot start publication for the output base '{run.Base}'."),
            run.Harness.StdErr);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenEvidencePublicationIsInterruptedAfterItsRename_ThenARetryConverges()
    {
        // Past the rename that makes evidence authoritative, the object is self-validating — its
        // own bytes are this run's evidence for that target and agree with the record — so a later
        // run can prove it and finish.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter = "Move:out.fcabedrock-ep-c-T->out.fcabedrock-e-c-T";

        await run.ConvertAsync("--format", "cxt", "--no-manifest");
        Assert.True(run.Harness.PublicationFiles.Crashed, "the crash after the evidence rename never fired");

        var retry = new CliTestHarness();
        Assert.Equal(
            0,
            await retry.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--no-manifest"));

        Assert.Empty(run.Residue());
        Assert.Equal("B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", run.Text(".cxt"));
    }

    [Theory]
    [InlineData("CreateNew:out.fcabedrock-ep-c-T")]
    [InlineData("StreamWrite:out.fcabedrock-ep-c-T")]
    [InlineData("StreamClose:out.fcabedrock-ep-c-T")]
    public async Task Publication_WhenEvidencePublicationIsInterruptedBeforeItsRename_ThenTheStateStaysClassifiable(
        string transition)
    {
        // BEFORE that rename the object is under a pending name that nothing durable acknowledges.
        // The run that created it could prove it from the identity its own creation reported; a
        // resumed one cannot, and it will not infer ownership from the name or the bytes. So it
        // removes nothing, keeps the record, and says the clean-up cannot finish — every time.
        // This is the cost of the rule, and it is deliberate: the alternative is
        // deleting an object that may never have been ours.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter = transition;

        await run.ConvertAsync("--format", "cxt", "--no-manifest");
        Assert.True(run.Harness.PublicationFiles.Crashed, $"the crash after '{transition}' never fired");

        var pending = Directory.GetFiles(run.Directory, "out.fcabedrock-ep-c-*").Single();
        var bytes = await File.ReadAllBytesAsync(pending);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var retry = new CliTestHarness();
            Assert.Equal(
                1,
                await retry.RunAsync(
                    "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--no-manifest"));
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
                retry.StdErr);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(pending));
            Assert.Contains(
                run.Residue(), name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
            Assert.False(File.Exists(run.Target(".cxt")));
        }
    }

    // ---- the identity capture itself ----------------------------------------------------------------

    [Fact]
    public void Publication_WhenAConfidentialStageIsCreated_ThenItsIdentityComesFromThatHandle()
    {
        // The identity cannot come from the path while the stage is held: the file is created
        // FileShare.None, so a second open is refused. Taking it from the handle is what makes the
        // evidence describe the object this call created rather than whatever later occupies the
        // name.
        using var temp = TempDirectory.Create();
        var path = temp.Resolve("stage");

        var stage = PublicationFileSystem.Instance.CreateNewConfidential(path);
        try
        {
            Assert.NotNull(stage.Identity);
            Assert.True(stage.Identity!.Value.IsOperatingSystemIdentity);

            var whileOpen = new FileIdentity(
                OperatingSystem.IsWindows() ? new WindowsFileIdentityProbe() : new UnixFileIdentityProbe());

            if (OperatingSystem.IsWindows())
            {
                // Denied by the share mode — so a path probe could not have produced the value above.
                Assert.False(whileOpen.KeyFor(path).IsOperatingSystemIdentity);
            }
            else
            {
                Assert.Equal(stage.Identity!.Value, whileOpen.KeyFor(path));
            }
        }
        finally
        {
            stage.Content.Dispose();
        }

        // And once released, the object at that path is exactly the one that was created.
        var afterClose = new FileIdentity(
            OperatingSystem.IsWindows() ? new WindowsFileIdentityProbe() : new UnixFileIdentityProbe());
        Assert.Equal(stage.Identity!.Value, afterClose.KeyFor(path));
    }

    // ---- phase authority ----------------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenARollbackMarkedRunLooksFullyCommitted_ThenItStillRollsBack()
    {
        // Every staged final is this transaction's own published object and every old
        // backup is intact — the exact shape a completed commit leaves. The durable rollback marker
        // is the only thing that says otherwise, and it decides: a layout inference must never
        // reclassify a failed run as a successful one and discard its last-good backups.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "the old cxt");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the failed run's cxt");
        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteMarker("staged");
        residue.WriteMarker("rollback");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the old cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAStagedRunLeftAnOldMarkerBesideItsNewArtifact_ThenRecoveryRefuses()
    {
        // Each row is individually plausible — the CXT is this run's own published
        // object, the demoted manifest is back at its public path — but together they are a state
        // no run reaches. Treating it as committed would strip every private control and leave an
        // old marker certifying an artifact from a different run.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.manifest.toml"), ("stage", "out.cxt")]);
        await File.WriteAllTextAsync(run.Target(".cxt"), "the new artifact");
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old marker");
        residue.WriteEvidence(
            "m",
            "out.manifest.toml",
            residue.IdentityOf(run.Target(".manifest.toml"), "backup", "out.manifest.toml"),
            IdentityEvidence.NotApplicable);
        residue.WriteEvidence(
            "c",
            "out.cxt",
            IdentityEvidence.NotApplicable,
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the new artifact", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Equal("the old marker", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
        Assert.True(File.Exists(residue.RecordPath));
    }

    [Fact]
    public async Task Publication_WhenANoManifestRunCommittedCompletely_ThenTheTailStillConverges()
    {
        // The genuine forward tail of the same shape: the marker is gone, as a crossed commit point
        // leaves it. The aggregate rule must not brick this.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("backup", "out.manifest.toml"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.manifest.toml", "the demoted marker");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the new artifact");
        residue.WriteEvidence(
            "m",
            "out.manifest.toml",
            residue.Identity("backup", "out.manifest.toml"),
            IdentityEvidence.NotApplicable);
        residue.WriteEvidence(
            "c",
            "out.cxt",
            IdentityEvidence.NotApplicable,
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the new artifact", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("rollback")]
    [InlineData("committed")]
    public async Task Publication_WhenAPhaseMarkersBodyIsNotCanonical_ThenRecoveryRefusesAndPreservesIt(string phase)
    {
        // Superseded zero-byte mechanism: a genuine marker carries the canonical body for its own phase,
        // so a file with the right name and any other contents must neither select a recovery
        // direction nor be deleted as control residue.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteEvidence(
            "c", "out.cxt", IdentityEvidence.NotApplicable, residue.Identity("stage", "out.cxt"));

        if (!string.Equals(phase, "staged", StringComparison.Ordinal))
        {
            residue.WriteMarker("staged");
        }

        await File.WriteAllTextAsync(residue.MarkerPath(phase), "keep me");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("keep me", await File.ReadAllTextAsync(residue.MarkerPath(phase)));
        Assert.Equal("half-written", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        Assert.True(File.Exists(residue.RecordPath));
        Assert.True(File.Exists(residue.EvidencePath("c")));
    }

    [Fact]
    public async Task Publication_WhenAStageClaimsBodyIsNotCanonical_ThenRecoveryRefusesAndPreservesIt()
    {
        // The claim is authoritative only through its exact canonical body, which binds this
        // transaction, this target kind, and the acknowledged stage identity. Other bytes under
        // that name prove nothing: they neither authorize the stage beside it nor are removed.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        var claim = residue.StageClaimPath("c", residue.Identity("stage", "out.cxt"));
        await File.WriteAllTextAsync(claim, "keep me");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("keep me", await File.ReadAllTextAsync(claim));
        Assert.Equal("half-written", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
    }

    [Fact]
    public async Task Publication_WhenTheCommittedMarkerCannotBeDeleted_ThenTheStateStaysClassifiable()
    {
        // Descending cleanup is only half the guarantee: continuing past a committed
        // marker that could not be removed would delete the staged marker and the evidence beneath
        // it, leaving `committed` with no `staged` — a state the next run rightly refuses and could
        // never repair. Stopping at the first failure keeps the run recoverable.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailKind = "Delete";
        run.Harness.PublicationFiles.FailName = null;
        run.Harness.PublicationFiles.FailEveryMatch = false;

        // The first delete of the run is the intent descriptor; the committed marker's is the one
        // after the backups and stages of a clean first run, so it is targeted by name prefix.
        run.Harness.PublicationFiles.FailKind = null;
        run.Harness.PublicationFiles.FailDeletePrefix = "out.fcabedrock-committed-";

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        // The published run stands, and what survives is a complete committed state.
        var residue = run.Residue();
        Assert.Contains(residue, name => name.Contains(".fcabedrock-committed-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-staged-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-e-c-", StringComparison.Ordinal));

        // And an ordinary retry finishes that cleanup forward rather than meeting unknown residue.
        var retry = new CliTestHarness();
        Assert.Equal(
            0,
            await retry.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));

        Assert.Empty(run.Residue());
        Assert.Equal("B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", run.Text(".cxt"));
    }

    // ---- ownership proved at the moment of action ------------------------------------------------

    [Fact]
    public async Task Publication_WhenALaterTargetIsSubstitutedMidRollback_ThenItIsNotDeletedOnAStaleProof()
    {
        // Both finals match their stage evidence when the decision pass runs. The DAT
        // is then replaced while the CXT is being deleted — so the answer captured earlier is no
        // longer true of the object at that path, and acting on it would delete an unrelated file.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt"), ("stage", "out.dat")]);
        await File.WriteAllTextAsync(run.Target(".cxt"), "this run's cxt");
        await File.WriteAllTextAsync(run.Target(".dat"), "this run's dat");
        residue.WriteEvidence(
            "c",
            "out.cxt",
            IdentityEvidence.NotApplicable,
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteEvidence(
            "d",
            "out.dat",
            IdentityEvidence.NotApplicable,
            residue.IdentityOf(run.Target(".dat"), "stage", "out.dat"));
        residue.WriteMarker("staged");
        residue.WriteMarker("rollback");

        run.Harness.PublicationFiles.MutateBefore = "Delete:out.cxt";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            File.Delete(run.Target(".dat"));
            File.WriteAllText(run.Target(".dat"), "someone else's file");
        };

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(1, run.Harness.PublicationFiles.MutationsFired);
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenAFileOccupiesAStagePath_ThenItIsPreservedAndNeverClaimed()
    {
        // The record predicts every private name before the file exists. Create-new
        // refuses to overwrite an occupant — and rollback must not then delete the very collision
        // that refusal protected.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Confidential:out.cxt.fcabedrock-stage-T";
        run.Harness.PublicationFiles.MutateWith = operation =>
            File.WriteAllText(Path.Combine(run.Directory, operation["Confidential:".Length..]), "keep me");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot write the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);

        var occupant = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
        Assert.Equal("keep me", await File.ReadAllTextAsync(occupant));
        Assert.False(File.Exists(run.Target(".cxt")));

        // And a retry never deletes it either: it is not this or any transaction's object.
        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Equal("keep me", await File.ReadAllTextAsync(occupant));
    }

    [Fact]
    public async Task Publication_WhenAStageIsSubstitutedBeforeItsCommit_ThenTheRunCannotPublishIt()
    {
        // The manifest certifies the SHA-256 of the bytes this run wrote. Renaming
        // whatever occupies the stage path would publish something else under that hash — on an
        // otherwise entirely successful run, leaving no residue to reveal it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.MutateBefore = "Move:out.cxt.fcabedrock-stage-T->out.cxt";
        run.Harness.PublicationFiles.Mutate = () =>
        {
            var stage = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
            File.Delete(stage);
            File.WriteAllText(stage, "not a cxt");
        };

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);

        // The race really happened. Before the lifetime correction this case could pass or fail on
        // identical code depending on which inode the allocator handed the impostor, so what is
        // asserted is that the substitution fired — not merely that the outcome looks right.
        Assert.Equal(1, run.Harness.PublicationFiles.MutationsFired);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);

        // The impostor is preserved — it is not this transaction's to delete — but it is never
        // certified and never left at a published path: no artifact and no manifest becomes public,
        // and the object is put back where the rename took it from.
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));

        var occupant = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
        Assert.Equal("not a cxt", await File.ReadAllTextAsync(occupant));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_WhenTheTargetIsReplacedAtTheBackupRename_ThenNothingIsPublished(bool alias)
    {
        // The identity check and the rename cannot be one atomic act, so the rename's
        // RESULT is checked too: a replacement that slipped into that interval is not the object
        // preflight approved, publication stops before any artifact commits, and the file is
        // neither published over nor deleted as superseded residue.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
        var manifest = await File.ReadAllBytesAsync(run.Target(".manifest.toml"));

        var second = new CliTestHarness();
        var linked = false;
        second.PublicationFiles.MutateBefore = "Move:out.cxt->out.cxt.fcabedrock-backup-T";
        second.PublicationFiles.Mutate = () =>
        {
            File.Delete(run.Target(".cxt"));
            if (alias)
            {
                linked = PlatformLinks.TryCreateHardLink(run.Target(".cxt"), run.Data, out _);
            }
            else
            {
                File.WriteAllText(run.Target(".cxt"), "keep me");
            }
        };

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force");

        if (alias && !linked)
        {
            Assert.Skip("hard links are unavailable on this host.");
        }

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
            second.StdErr);

        // The replacement survives — wherever the rename left it — and this run published nothing:
        // the manifest is still the FIRST run's, byte for byte.
        var survivors = Directory.GetFiles(run.Directory, "out.cxt*");
        Assert.Contains(
            survivors,
            path => File.ReadAllText(path) == (alias ? CliFixtures.WideData : "keep me"));
        Assert.Equal(manifest, await File.ReadAllBytesAsync(run.Target(".manifest.toml")));
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
    }

    // ---- the exception taxonomy at every publication boundary ---------------------------------------

    [Theory]
    [InlineData("out.cxt.fcabedrock-stage-", "cxt")]
    [InlineData("out.dat.fcabedrock-stage-", "dat")]
    [InlineData("out.manifest.toml.fcabedrock-stage-", "cxt")]
    [InlineData("out.fcabedrock-pending-", "cxt")]
    [InlineData("out.fcabedrock-ep-", "cxt")]
    [InlineData("out.fcabedrock-staged-", "cxt")]
    public async Task Publication_WhenAPublicationStreamIsDisposed_ThenItIsAnInternalFaultNotAStdoutFailure(
        string prefix, string format)
    {
        // An ObjectDisposedException from a publication stream is a contract defect,
        // but it is also exactly what the host attributes to its own writers — so untagged it would
        // be reported as "cannot write to standard output" with exit 1, blaming a channel that was
        // never involved.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailStreamWritePrefix = prefix;
        run.Harness.PublicationFiles.FailStreamClosePrefix = prefix;
        run.Harness.PublicationFiles.FailStreamWith = static () => new ObjectDisposedException("stage");
        run.Harness.PublicationFiles.FailStreamCloseWith = static () => new ObjectDisposedException("stage");

        var exit = await run.ConvertAsync("--format", format);

        Assert.Equal(4, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(CliHost.UnexpectedFaultMessage), run.Harness.StdErr);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
    }

    [Theory]
    [InlineData("EnumerateFiles", null)]
    [InlineData("ReadBounded", "out.fcabedrock-transaction-")]
    [InlineData("Exists", "out.cxt")]
    public async Task Publication_WhenAResidueReadRaisesAContractFault_ThenItIsAnInternalFault(
        string kind, string? prefix)
    {
        // The read side of the same rule: a contract defect at an internal residue
        // enumeration, read, or existence check is a product bug. Reporting it as an unusable
        // output location — or as hostile residue — would send the user to inspect their own
        // directory for a defect in this code.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);

        run.Harness.PublicationFiles.FailReadKind = kind;
        run.Harness.PublicationFiles.FailReadNamePrefix = prefix;
        run.Harness.PublicationFiles.FailReadWith = static () => new ObjectDisposedException("record");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(4, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(CliHost.UnexpectedFaultMessage), run.Harness.StdErr);
        Assert.Equal(string.Empty, run.Harness.StdOut);

        // Nothing was touched: the residue is exactly as it was found.
        Assert.True(File.Exists(residue.RecordPath));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Theory]
    [InlineData("EnumerateFiles", null)]
    [InlineData("ReadBounded", "out.fcabedrock-transaction-")]
    public async Task Publication_WhenAResidueReadFailsWithIo_ThenItStaysAnOrdinaryPublicationFailure(
        string kind, string? prefix)
    {
        // The counterexample that makes the distinction meaningful: a genuine I/O failure at the
        // same boundary keeps the established code-less exit 1.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", Token);
        residue.WriteRecord([("stage", "out.cxt")]);

        run.Harness.PublicationFiles.FailReadKind = kind;
        run.Harness.PublicationFiles.FailReadNamePrefix = prefix;
        run.Harness.PublicationFiles.FailReadWith = static () => new IOException("the device is not ready.");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.StartsWith("error: ", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected internal error", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(residue.RecordPath));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    // A valid intent descriptor for the record `files` describes: the canonical intent body, and
    // a name carrying the shape, the digest of that record's bytes, and the identity of the
    // pending object it acknowledges — for a hand-built descriptor, whatever the test put there.
    private static string WriteIntent(
        string directory,
        string baseName,
        string token,
        IReadOnlyList<(string Role, string Target)> files,
        string? pendingIdentity = null)
    {
        var entries = new List<TransactionFileEntry>();
        foreach (var (role, target) in files)
        {
            entries.Add(new TransactionFileEntry(role, target));
        }

        var record = TransactionRecord.Create(token, baseName, entries);
        var pending = Path.Combine(directory, PublicationTargets.PendingRecordName(baseName, token));
        var identity = pendingIdentity ?? IdentityEvidence.Of(
            token, PublicationTargets.RecordRole, baseName, FileIdentity.CreateDefault().KeyFor(pending));

        var name = PublicationTargets.IntentName(baseName, token, record.ShapeCode, record.Digest, identity);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, IntentBody(token, baseName, record.Digest));
        return path;
    }

    // The canonical descriptor body: the run token, the base, the control's role, and the digest of
    // the record it belongs to — spelled out here rather than produced by the writer under test.
    internal static byte[] IntentBody(string token, string baseName, string recordDigest) =>
        new UTF8Encoding(false).GetBytes(
            "version = 1\n"
            + $"token = \"{token}\"\n"
            + $"base = \"{baseName}\"\n"
            + "control = \"intent\"\n"
            + $"record = \"{recordDigest}\"\n");
}

/// <summary>
/// An identity probe that records every path it is asked about. It answers "no OS identity", so
/// the service falls back to canonical paths — the question these tests ask is <em>which paths a
/// run checks</em>, not what the host says about them.
/// </summary>
internal sealed class RecordingIdentityProbe : IFileIdentityProbe
{
    /// <summary>Every path asked about, as file names, in order.</summary>
    public List<string> Names { get; } = [];

    public FileIdentityKey? TryGetIdentity(string fullPath)
    {
        Names.Add(Path.GetFileName(fullPath));
        return null;
    }
}
