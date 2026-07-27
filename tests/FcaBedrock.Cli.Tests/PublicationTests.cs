using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The publication transaction through argv (D-122 parts 4–5, D-123 point 7): preflight,
/// collisions, forced replacement with rename-aside backups, commit ordering, rollback, and
/// validated recovery.
/// <para>
/// Every case inspects the real directory afterwards. A test that claims a rollback restored the
/// previous run compares the <em>bytes</em> that are there against the bytes that were there, and
/// a test that claims residue was left untouched compares those bytes too — a mock callback would
/// prove neither.
/// </para>
/// </summary>
public sealed class PublicationTests
{
    // ---- refusal and preflight ------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenATargetExistsWithoutForce_ThenTheRunIsRefusedAndNothingIsTouched()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".cxt"), "previous");

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{run.Target(".cxt")}' already exists; use --force to replace it."),
            run.Harness.StdErr);

        // Zero mutation: the existing file is untouched, and no record, stage, or backup was
        // ever created — preflight finishes before the transaction begins.
        Assert.Equal("previous", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheManifestExistsWithoutForce_ThenTheRunIsRefused()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "previous");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("previous", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
    }

    [Fact]
    public async Task Publication_WhenAnOutputWouldBeTheDataInput_ThenItIsRefusedEvenWithForce()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("out.cxt", CliFixtures.WideData);

        // --out out means the .cxt target IS the data file.
        var exit = await harness.RunAsync(
            "convert", spec, data, "--out", temp.Resolve("out"), "--format", "cxt", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{temp.Resolve("out")}.cxt' and the input '{data}' are the same file."),
            harness.StdErr);
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(data));
    }

    [Fact]
    public async Task Publication_WhenAnOutputWouldBeTheRootSpec_ThenItIsRefusedEvenWithForce()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("out.cxt", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var exit = await harness.RunAsync(
            "convert", spec, data, "--out", temp.Resolve("out"), "--format", "cxt", "--force");

        Assert.Equal(1, exit);
        Assert.Contains("are the same file", harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(CliFixtures.IndexBoundSpec, await File.ReadAllTextAsync(spec));
    }

    [Fact]
    public async Task Publication_WhenAnOutputWouldBeAReferencedBaseSpec_ThenItIsRefusedEvenWithForce()
    {
        // The chain's base is an input just as much as the root is, and identity is checked
        // against every file the run actually loaded.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var basePath = temp.Write("out.cxt", CliFixtures.IndexBoundSpec);
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"out.cxt\"\n");
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var exit = await harness.RunAsync(
            "convert", root, data, "--out", temp.Resolve("out"), "--format", "cxt", "--force");

        Assert.Equal(1, exit);
        Assert.Contains(
            $"the input 'out.cxt' are the same file", harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(CliFixtures.IndexBoundSpec, await File.ReadAllTextAsync(basePath));
    }

    [Fact]
    public async Task Publication_WhenTheTwoOutputsAreHardLinkAliases_ThenTheCollisionIsRefusedEvenWithForce()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".cxt"), "previous");

        if (!PlatformLinks.TryCreateHardLink(run.Target(".dat"), run.Target(".cxt"), out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        var exit = await run.ConvertAsync("--format", "both", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the outputs '{run.Target(".cxt")}' and '{run.Target(".dat")}' are the same file."),
            run.Harness.StdErr);
        Assert.Equal("previous", await File.ReadAllTextAsync(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenTheDataIsAHardLinkAliasOfATarget_ThenItIsRefusedEvenWithForce()
    {
        // Path comparison cannot see this; actual filesystem identity can (CX-M7P-004).
        using var run = ConvertRun.Wide();
        if (!PlatformLinks.TryCreateHardLink(run.Target(".cxt"), run.Data, out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        var exit = await run.ConvertAsync("--format", "cxt", "--force");

        Assert.Equal(1, exit);
        Assert.Contains("are the same file", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
    }

    // ---- the record and the private names ---------------------------------------------------------

    [Fact]
    public async Task Publication_WhenTheRunSucceeds_ThenNoRecordStageOrBackupSurvives()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheRecordIsCreated_ThenItIsCreateNewAndPrecedesEveryOwnedFile()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        // Acquisition first, acknowledgement second, all the way down: the pending record, then the
        // zero-byte intent descriptor that names the object that creation produced; then each stage
        // and, immediately after, the claim that names ITS created object; then each target's
        // evidence; and only once all of that is durable does the staged marker appear, the fact
        // that makes forward recovery sound.
        //
        // The order is the ownership model itself. A descriptor or a claim written BEFORE the
        // acquisition it describes would survive a refusal and go on to authorize removing the very
        // occupant that refused it (CX-M7H-036/037). Both the record and the evidence are created
        // under a pending name and published by rename, so no discoverable authoritative name ever
        // holds partial bytes (CX-M7H-012/018).
        var creates = run.Harness.PublicationFiles.Operations
            .Where(operation =>
                operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                || operation.StartsWith("Confidential:", StringComparison.Ordinal))
            .Select(operation => Kind(operation[(operation.IndexOf(':', StringComparison.Ordinal) + 1)..]))
            .ToList();

        // Each stage's CLAIM follows its own acquisition and precedes the writer, and each stage's
        // evidence is published as that stage closes. The order is load-bearing: a claim written
        // before the create-new would survive a refusal and go on to authorize deleting the very
        // occupant that refused it (CX-M7H-031/036).
        Assert.Equal(
            [
                "pending", "intent",
                "stage", "sc", "ep",
                "stage", "sc", "ep",
                "stage", "sc", "ep",
                "staged", "committed",
            ],
            creates);

        // And the record's publish is the very first rename of the run.
        Assert.Equal("Move:out.fcabedrock-pending-T->out.fcabedrock-transaction-T.toml", Moves(run.Harness)[0]);
    }

    [Fact]
    public async Task Publication_WhenTheRunSucceeds_ThenThePhaseMarkersAreOwnedAndRemoved()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        // Every marker is a create-new empty file in the transaction's own namespace, and all of
        // them go with the record at the end.
        Assert.Contains(
            run.Harness.PublicationFiles.Operations,
            operation => operation.Contains(".fcabedrock-staged-", StringComparison.Ordinal));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public void Publication_WhenAPrivateNameIsFormed_ThenFcabedrockIsSpelledInFull()
    {
        // CX-M7P-003 forbids the `fb` abbreviation outright.
        Assert.Equal("out.cxt.fcabedrock-stage-abc", PublicationTargets.PrivateName("out.cxt", "stage", "abc"));
        Assert.Equal("out.cxt.fcabedrock-backup-abc", PublicationTargets.PrivateName("out.cxt", "backup", "abc"));
        Assert.Equal("out.fcabedrock-transaction-abc.toml", PublicationTargets.RecordName("out", "abc"));
    }

    [Fact]
    public async Task Publication_WhenTwoRunsPublishTheSameBase_ThenTheirTokensDiffer()
    {
        // A successful run removes its record, so the names are read from what each run actually
        // created. Fresh and unpredictable per attempt: no two runs can own the same private file.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        var second = new CliTestHarness();
        Assert.Equal(0, await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));

        Assert.NotEqual(RecordCreated(run.Harness), RecordCreated(second));
        Assert.NotEqual(string.Empty, RecordCreated(run.Harness));
    }

    // ---- staging failures ---------------------------------------------------------------------

    [Theory]
    [InlineData("CreateNew")]
    [InlineData("Flush")]
    public async Task Publication_WhenAStageCannotBeWritten_ThenNothingIsPublishedAndNoResidueSurvives(string kind)
    {
        using var run = ConvertRun.Wide();

        // The third occurrence: the first two are the intent descriptor and the pending record.
        run.Harness.PublicationFiles.FailKind = kind;
        run.Harness.PublicationFiles.FailOccurrence = 3;

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot write the output '{run.Target(".cxt")}'."),
            run.Harness.StdErr);
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- forced replacement --------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenForceReplacesACompleteRun_ThenTheOldFilesAreRenamedAsideNeverCopied()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var second = new CliTestHarness();
        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        Assert.Equal(0, exit);

        // The record, then each artifact's evidence as its stage closes, then the demoted marker's
        // evidence at sealing, then the backups — the old manifest ahead of the artifacts it
        // certified — then the commits in canonical order with the manifest last. Renames
        // throughout: no file is ever opened to copy or rehash an old target, and every
        // authoritative control name is published by one rename.
        //
        Assert.Equal(
            [
                "Move:out.fcabedrock-pending-T->out.fcabedrock-transaction-T.toml",
                "Move:out.fcabedrock-ep-c-T->out.fcabedrock-e-c-T",
                "Move:out.fcabedrock-ep-d-T->out.fcabedrock-e-d-T",
                "Move:out.fcabedrock-ep-m-T->out.fcabedrock-e-m-T",
                "Move:out.manifest.toml->out.manifest.toml.fcabedrock-backup-T",
                "Move:out.cxt->out.cxt.fcabedrock-backup-T",
                "Move:out.dat->out.dat.fcabedrock-backup-T",
                "Move:out.cxt.fcabedrock-stage-T->out.cxt",
                "Move:out.dat.fcabedrock-stage-T->out.dat",
                "Move:out.manifest.toml.fcabedrock-stage-T->out.manifest.toml",
            ],
            Moves(second));

        // And every REMOVAL names the object it destroys, in the cleanup's own order: the
        // descriptor as the record publishes, then the superseded backups, then the markers
        // most-advanced-first, then each target's evidence and stage claim, and the record last of
        // all. Each is an identity-bound removal — the proof is taken from the handle the deletion
        // acts through — so none of them can destroy a file that appeared at the name in between
        // (CX-M7H-040).
        Assert.Equal(
            [
                "out.fcabedrock-intent-T-3f-T-T",
                "out.manifest.toml.fcabedrock-backup-T",
                "out.cxt.fcabedrock-backup-T",
                "out.dat.fcabedrock-backup-T",
                "out.fcabedrock-committed-T",
                "out.fcabedrock-staged-T",
                "out.fcabedrock-e-m-T",
                "out.fcabedrock-sc-m-T-T",
                "out.fcabedrock-e-c-T",
                "out.fcabedrock-sc-c-T-T",
                "out.fcabedrock-e-d-T",
                "out.fcabedrock-sc-d-T-T",
                "out.fcabedrock-transaction-T.toml",
            ],
            Removals(second));

        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenACommitFails_ThenTheCompleteOldRunIsRestoredByteIdentically()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.FailKind = "Move";
        second.PublicationFiles.FailMoveTo = "out.dat";
        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        // The failure is reported, the old run is exactly as it was, and no residue is left.
        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".dat")}'."),
            second.StdErr);
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenABackupFails_ThenTheOldRunSurvivesAndNoMarkerIsLost()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.FailKind = "Move";
        second.PublicationFiles.FailName = "out.cxt";
        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenForcedNoManifestReplacesAManifestBearingRun_ThenTheStaleMarkerIsGone()
    {
        // The old manifest certifies bytes that no longer exist, so it must not survive the run
        // that replaced them — even though this run writes no manifest of its own.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var second = new CliTestHarness();
        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force", "--no-manifest");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAForcedNoManifestRunRollsBack_ThenTheOldManifestIsRestored()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var before = run.Snapshot();

        var second = new CliTestHarness();
        second.PublicationFiles.FailKind = "Move";
        second.PublicationFiles.FailMoveTo = "out.cxt";
        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force", "--no-manifest");

        Assert.Equal(1, exit);
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenNoManifestIsUsed_ThenTheCommitPointIsTheCompleteArtifactSet()
    {
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both", "--no-manifest"));

        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    // ---- recovery ---------------------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenAPriorRunLeftOnlyItsRecord_ThenItIsRecoveredAndTheNewRunSucceeds()
    {
        // Crashed before staging: nothing owned exists but the record itself.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "out.cxt")]);

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Empty(run.Residue());
        Assert.True(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenAPreStageCrashLeftAPreExistingFinal_ThenThatFinalIsNeverTouched()
    {
        // CX-M7H-001. The record lists three stages; none was created. Only `out.cxt` exists, and
        // it is a file the interrupted run had not yet renamed aside — not something it published.
        // Without a durable phase, "no stage, present target" would read as a partial commit and
        // delete it.
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".cxt"), "the untouched original");

        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([
            ("backup", "out.cxt"), ("stage", "out.cxt"), ("stage", "out.dat"), ("stage", "out.manifest.toml")]);

        var exit = await run.ConvertAsync("--format", "both");

        // The prior run is cleaned up; the pre-existing final survives byte-identically and still
        // blocks an unforced run.
        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the untouched original", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenARecordFlushNeverCompleted_ThenAPreExistingFinalStillSurvives()
    {
        // The same hazard reached through the real code path: the record is created but its flush
        // fails, so the transaction is durably no further than Preparing. The second flush is the
        // record's; the first belongs to the intent descriptor that precedes it.
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".cxt"), "the untouched original");
        run.Harness.PublicationFiles.FailKind = "Flush";
        run.Harness.PublicationFiles.FailOccurrence = 2;

        var exit = await run.ConvertAsync("--format", "both", "--force");

        Assert.Equal(1, exit);
        Assert.Equal("the untouched original", await File.ReadAllTextAsync(run.Target(".cxt")));

        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both"));
        Assert.Equal("the untouched original", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAPriorRunLeftAStage_ThenTheStageIsRemovedAndTheTargetIsUntouched()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteStageClaim("c", "out.cxt");

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Empty(run.Residue());
        Assert.Equal(
            "B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", run.Text(".cxt"));
    }

    [Fact]
    public async Task Publication_WhenAPriorRunWasBackedUpButNotCommitted_ThenTheOldTargetIsRestored()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "the old run");
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteEvidence(
            "c", "out.cxt", residue.Identity("backup", "out.cxt"), residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        // The recovered old target then blocks the new run, exactly as an ordinary existing
        // target does — recovery restores, it does not authorize a replacement.
        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the old run", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAPriorRunPartiallyCommitted_ThenItsCommitIsUndoneAndTheOldRunReturns()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt"), ("stage", "out.dat")]);
        residue.WritePrivate("backup", "out.cxt", "the old cxt");
        residue.WritePrivate("stage", "out.dat", "not committed");

        // out.cxt exists and its stage is gone: the interrupted run committed it. out.dat never
        // committed, so the run as a whole did not — the partial commit is undone.
        await File.WriteAllTextAsync(run.Target(".cxt"), "the interrupted new cxt");

        // The evidence names the objects: the old CXT now held in its backup, and the staged CXT
        // that the commit rename carried to the target.
        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteEvidence(
            "d", "out.dat", IdentityEvidence.NotApplicable, residue.Identity("stage", "out.dat"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal("the old cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAPriorRunCommittedButLeftBackups_ThenTheBackupsAreDroppedAndTheRunStands()
    {
        // Every stage is consumed and every target is present: the run crossed its commit point
        // and crashed during cleanup. Its result must stand, and its backups must go.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "superseded");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the committed run");
        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the committed run", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenACommittedNoManifestRunLeftADemotedManifestBackup_ThenItIsNotResurrected()
    {
        // The record's only manifest entry is a backup: the old marker was demoted, never
        // selected. Committedness is judged on the staged finals alone, so the run counts as
        // committed and the demoted marker is dropped rather than restored.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.manifest.toml"), ("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.manifest.toml", "the old marker");
        residue.WritePrivate("backup", "out.cxt", "the old cxt");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the committed cxt");
        residue.WriteEvidence(
            "m",
            "out.manifest.toml",
            residue.Identity("backup", "out.manifest.toml"),
            IdentityEvidence.NotApplicable);
        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal("the committed cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenARollbackWasInterrupted_ThenTheRetryFinishesItInsteadOfCommittingForward()
    {
        // CX-M7H-002. The interrupted run committed its new CXT, then failed and began rolling
        // back: it restored the old DAT and manifest but could not delete the new CXT. Every
        // stage is now gone and every final path is populated — the exact shape a completed
        // commit leaves. Only the durable rollback marker distinguishes them.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([
            ("backup", "out.manifest.toml"), ("backup", "out.cxt"), ("backup", "out.dat"),
            ("stage", "out.cxt"), ("stage", "out.dat"), ("stage", "out.manifest.toml")]);

        residue.WritePrivate("backup", "out.cxt", "the old cxt");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the failed run's cxt");
        await File.WriteAllTextAsync(run.Target(".dat"), "the old dat");
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old manifest");

        // The evidence is what tells the two apart: the DAT and manifest now at their targets are
        // the objects this transaction renamed aside and has already restored, while the CXT is
        // the object it staged and published — so only that one may be removed.
        residue.WriteEvidence(
            "m",
            "out.manifest.toml",
            residue.IdentityOf(run.Target(".manifest.toml"), "backup", "out.manifest.toml"),
            residue.ConsumedIdentity("stage", "out.manifest.toml"));
        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.IdentityOf(run.Target(".cxt"), "stage", "out.cxt"));
        residue.WriteEvidence(
            "d",
            "out.dat",
            residue.IdentityOf(run.Target(".dat"), "backup", "out.dat"),
            residue.ConsumedIdentity("stage", "out.dat"));
        residue.WriteMarker("staged");
        residue.WriteMarker("rollback");

        var exit = await run.ConvertAsync("--format", "both");

        // The complete prior set is restored; no mixed set survives and no old backup is dropped
        // as if the failed run had committed.
        Assert.Equal(1, exit);
        Assert.Contains("already exists; use --force", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the old cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Equal("the old dat", await File.ReadAllTextAsync(run.Target(".dat")));
        Assert.Equal("the old manifest", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenACommitAndItsRollbackCleanupBothFail_ThenARetryRestoresThePriorSet()
    {
        // The same defect reached causally: a real forced run publishes the new CXT, fails the
        // DAT rename, and then fails to delete the CXT it just published.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var failing = new CliTestHarness();
        failing.PublicationFiles.FailKind = "Move";
        failing.PublicationFiles.FailMoveTo = "out.dat";
        Assert.Equal(
            1,
            await failing.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));

        // The record survives because rollback could not finish, and it is a rollback record.
        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both"));

        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    // ---- no-manifest marker parity ------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenANoManifestRunWouldLeaveAnOldMarker_ThenItIsRefusedWithoutForce()
    {
        // CX-M7H-005. Only the old manifest exists; the requested artifact is brand new. Publishing
        // it would leave a public marker beside output it does not describe.
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old marker");

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{run.Target(".manifest.toml")}' already exists; use --force to replace it."),
            run.Harness.StdErr);
        Assert.Equal("the old marker", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAForcedNoManifestRunIntroducesAnArtifact_ThenTheOldMarkerIsRemoved()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old marker");

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest", "--force");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAForcedNoManifestIntroductionFails_ThenTheOldMarkerIsRestored()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old marker");

        run.Harness.PublicationFiles.FailKind = "Move";
        run.Harness.PublicationFiles.FailMoveTo = "out.cxt";
        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest", "--force");

        Assert.Equal(1, exit);
        Assert.Equal("the old marker", await File.ReadAllTextAsync(run.Target(".manifest.toml")));
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- cancellation between publication transitions -------------------------------------------------

    [Theory]
    [InlineData("out.manifest.toml.fcabedrock-backup", "both")]
    [InlineData("out.cxt.fcabedrock-backup", "both")]
    [InlineData("out.cxt", "both")]
    [InlineData("out.dat", "both")]
    public async Task Publication_WhenTheSignalArrivesBeforeTheCommitPoint_ThenTheRunRollsBackAndExitsThree(
        string afterMoveTo, string format)
    {
        // CX-M7H-004. A first signal delivered while backups or earlier renames are still running
        // must not be ignored until the whole irreversible sequence has finished.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var second = new CliTestHarness();
        if (afterMoveTo.EndsWith("backup", StringComparison.Ordinal))
        {
            second.PublicationFiles.CancelAfterMoveToPrefix = afterMoveTo + "-";
        }
        else
        {
            second.PublicationFiles.CancelAfterMoveTo = afterMoveTo;
        }

        second.PublicationFiles.CancelAfterMove = second.Signals.Cancel;

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", format, "--force");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, second.StdOut);
        Assert.Equal(string.Empty, second.StdErr);
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheSignalArrivesAfterTheNoManifestCommitPoint_ThenTheRunStands()
    {
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CancelAfterMoveTo = "out.dat";
        run.Harness.PublicationFiles.CancelAfterMove = run.Harness.Signals.Cancel;

        var exit = await run.ConvertAsync("--format", "both", "--no-manifest");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".dat")));
        Assert.Empty(run.Residue());
    }

    // ---- output-failure origin -------------------------------------------------------------------

    [Theory]
    [InlineData("out.cxt", "cxt")]
    [InlineData("out.dat", "dat")]
    [InlineData("out.manifest.toml", "cxt")]
    public async Task Publication_WhenAStageWriteFails_ThenItIsReportedAsAnOutputFailureNotADataFailure(
        string target, string format)
    {
        // CX-M7H-006. A full disk is an output problem. Reporting it against the DATA operand
        // would send the user to investigate a file that is perfectly fine.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailStreamWritePrefix = StageOf(target);

        var exit = await run.ConvertAsync("--format", format);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot write the output '{run.Base}{Extension(target)}'."),
            run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Theory]
    [InlineData("out.cxt", "cxt")]
    [InlineData("out.dat", "dat")]
    [InlineData("out.manifest.toml", "cxt")]
    public async Task Publication_WhenAStageCloseFails_ThenItIsReportedAsAnOutputFailure(string target, string format)
    {
        // A deferred failure surfacing at close means the stage is not trustworthy, so it fails
        // exactly as an earlier write would have — and before anything is committed.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailStreamClosePrefix = StageOf(target);

        var exit = await run.ConvertAsync("--format", format);

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot write the output '{run.Base}{Extension(target)}'."),
            run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Theory]
    [InlineData("out.cxt", "cxt")]
    [InlineData("out.dat", "dat")]
    [InlineData("out.manifest.toml", "cxt")]
    public async Task Publication_WhenAStageWriteRaisesAContractDefect_ThenItIsAnInternalFaultNotAnOutputFailure(
        string target, string format)
    {
        // CX-M7H-015. Failure ORIGIN and failure FAMILY are separate questions. A genuine I/O
        // failure on the output is exit 1 and names the output; a broken call contract inside the
        // writer is a product defect and must stay on the sanitized exit-4 path, so it is never
        // mistaken for a full disk.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailStreamWritePrefix = StageOf(target);
        run.Harness.PublicationFiles.FailStreamWith = static () => new ArgumentException("bad writer range");

        var exit = await run.ConvertAsync("--format", format);

        Assert.Equal(4, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(CliHost.UnexpectedFaultMessage), run.Harness.StdErr);

        // Still no committed run, and the transaction still cleaned up after itself.
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenTheDataFailsAtTheSameBoundary_ThenItKeepsTheDataMessage()
    {
        // The counterexample that makes the distinction above meaningful: the same emit boundary,
        // a genuine source failure, and the DATA message is retained.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var basePath = temp.Resolve("out");

        // Opens 0 and 1 are the schema and the .cxt replay's first pass; the second replay pass
        // fails while the very same stage is being written.
        var opens = 0;
        harness.OpenInput = path => path.EndsWith(".csv", StringComparison.Ordinal) && opens++ > 1
            ? throw new IOException("the device is not ready.")
            : File.OpenRead(path);

        var exit = await harness.RunAsync("convert", spec, data, "--out", basePath, "--format", "cxt");

        Assert.Equal(1, exit);
        Assert.EndsWith(
            DiagnosticRenderer.RenderHostError(RunPipeline.DataReadMessage(data)),
            harness.StdErr,
            StringComparison.Ordinal);
        Assert.False(File.Exists(basePath + ".cxt"));
    }

    // ---- post-recovery identity ------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenRecoveryWouldRestoreAnAliasOfTheData_ThenItIsRefusedBeforeAnyMutation()
    {
        // CX-M7H-023. Recovery is about to rename a backup that is a hard link to the DATA file.
        // The fresh collision check after recovery would catch the result — but only after the
        // input had already been moved, which is exactly what the gate before recovery prevents.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);

        if (!PlatformLinks.TryCreateHardLink(residue.PrivatePath("backup", "out.cxt"), run.Data, out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.ConsumedIdentity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
            run.Harness.StdErr);

        // Nothing moved: the DATA file is intact, its alias is still the backup, and the prior
        // transaction is exactly as it was found.
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(residue.PrivatePath("backup", "out.cxt")));
        Assert.True(File.Exists(residue.RecordPath));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenRecoveryRestoresAnAliasOfAnotherTarget_ThenTheFollowingPreflightStillRefuses()
    {
        // CX-M7H-007. At the first look `out.cxt` does not exist, so no collision is visible, and
        // a memoized "this path does not exist" answer would still say so afterwards. Recovery
        // restores a backup that is hard-linked to `out.dat`, and the reacquired identity is what
        // sees that the two selected outputs are now one file.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "the old cxt");

        if (!PlatformLinks.TryCreateHardLink(run.Target(".dat"), residue.PrivatePath("backup", "out.cxt"), out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        residue.WriteEvidence(
            "c",
            "out.cxt",
            residue.Identity("backup", "out.cxt"),
            residue.ConsumedIdentity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "both", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the outputs '{run.Target(".cxt")}' and '{run.Target(".dat")}' are the same file."),
            run.Harness.StdErr);

        // The prior transaction was completed — the old CXT is home — and this run began nothing.
        Assert.Equal("the old cxt", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    // ---- the durable rollback gate ------------------------------------------------------------------

    [Theory]
    [InlineData("CreateNew")]
    [InlineData("Flush")]
    public async Task Publication_WhenRollbackIntentCannotBeMadeDurable_ThenNoRollbackWorkBegins(string failing)
    {
        // CX-M7H-010. The commit publishes the new CXT and then fails; rollback is entered, but
        // its marker cannot be made durable. Rolling back anyway could restore the old DAT and
        // manifest while leaving the new CXT — no stages, all finals populated, `staged` the only
        // durable fact — which the next run would finish FORWARD, discarding the old CXT backup.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        var failingRun = new CliTestHarness();
        failingRun.PublicationFiles.FailKind = "Move";
        failingRun.PublicationFiles.FailMoveTo = "out.dat";
        if (string.Equals(failing, "CreateNew", StringComparison.Ordinal))
        {
            failingRun.PublicationFiles.FailCreateNewPrefix = "out.fcabedrock-rollback-";
        }
        else
        {
            failingRun.PublicationFiles.FailStreamClosePrefix = "out.fcabedrock-rollback-";
        }

        Assert.Equal(
            1,
            await failingRun.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));

        // No rollback work happened: the DAT and manifest targets are still renamed aside rather
        // than restored, and every old byte is still held in a backup waiting to come home.
        var residue = run.Residue();
        Assert.Contains(residue, name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-staged-", StringComparison.Ordinal));
        Assert.False(File.Exists(run.Target(".dat")), "rollback restored a target without durable intent");
        Assert.False(File.Exists(run.Target(".manifest.toml")), "rollback restored the marker without durable intent");
        Assert.Equal(3, residue.Count(name => name.Contains(".fcabedrock-backup-", StringComparison.Ordinal)));

        // An ordinary unforced retry: recovery must restore the complete previous set and then
        // refuse to overwrite it — never finish the failed run forward.
        var retry = new CliTestHarness();
        Assert.Equal(
            1, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both"));

        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    // ---- impossible recovery states -----------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenACommittedMarkerSitsOverASurvivingStage_ThenRecoveryRefusesAndTouchesNothing()
    {
        // CX-M7H-011. Committed means every stage was renamed to its final. A committed marker
        // beside a surviving stage and a missing final is a state no run reaches, so it authorizes
        // no cleanup at all — otherwise recovery would delete both the stage and the old backup
        // and leave nothing published.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WriteMarker("staged");
        residue.WriteMarker("committed");
        residue.WritePrivate("stage", "out.cxt", "the new bytes");
        residue.WritePrivate("backup", "out.cxt", "the old bytes");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the new bytes", await File.ReadAllTextAsync(residue.PrivatePath("stage", "out.cxt")));
        Assert.Equal("the old bytes", await File.ReadAllTextAsync(residue.PrivatePath("backup", "out.cxt")));
        Assert.True(File.Exists(residue.RecordPath));
    }

    [Fact]
    public async Task Publication_WhenBothRollbackAndCommittedAreMarked_ThenRecoveryRefusesAndTouchesNothing()
    {
        // Mutually exclusive outcomes of one transaction: rollback is refused once the commit
        // point is crossed, and commit never starts after rollback.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WriteMarker("staged");
        residue.WriteMarker("rollback");
        residue.WriteMarker("committed");
        await File.WriteAllTextAsync(run.Target(".cxt"), "the published bytes");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the published bytes", await File.ReadAllTextAsync(run.Target(".cxt")));
        Assert.True(File.Exists(residue.MarkerPath("committed")));
    }

    [Fact]
    public async Task Publication_WhenAPreparingRecordHasABackup_ThenRecoveryRefusesAndTouchesNothing()
    {
        // Backups are taken during commit, which begins only after staging completes. A backup
        // beside an unmarked record means something other than this transaction renamed a target
        // aside.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("backup", "out.cxt"), ("stage", "out.cxt")]);
        residue.WritePrivate("backup", "out.cxt", "the old bytes");

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("the old bytes", await File.ReadAllTextAsync(residue.PrivatePath("backup", "out.cxt")));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenACommittedMarkerHasNoStagedMarker_ThenRecoveryRefuses()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WriteMarker("committed");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(residue.MarkerPath("committed")));
    }

    // ---- the atomically published record --------------------------------------------------------------

    [Theory]
    [InlineData("someone else's file")]
    [InlineData("")]
    [InlineData("version = 1\ntoken =")]
    public async Task Publication_WhenAPendingRecordHasNoIntentDescriptor_ThenItIsRefusedAndLeftUntouched(
        string content)
    {
        // CX-M7H-018. A pending name carries 128 bits of this code's own randomness, but the
        // grammar is public: a file wearing that name proves nothing about who wrote it. Only the
        // intent descriptor authorizes removing one — including when the bytes look exactly like
        // an interrupted record, which is precisely the case name-and-token reasoning got wrong.
        using var run = ConvertRun.Wide();
        var pending = Path.Combine(run.Directory, "out.fcabedrock-pending-aaaaaaaabbbbbbbbccccccccdddddddd");
        await File.WriteAllTextAsync(pending, content);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(content, await File.ReadAllTextAsync(pending));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenAPendingNameIsMalformed_ThenItIsLeftUntouched()
    {
        using var run = ConvertRun.Wide();
        var lookalike = Path.Combine(run.Directory, "out.fcabedrock-pending-notatoken");
        await File.WriteAllTextAsync(lookalike, "someone else's file");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(lookalike));
    }

    [Fact]
    public async Task Publication_WhenAPendingFileSharesARecordsToken_ThenRecoveryRefuses()
    {
        // The pending file is the record's own preparatory state; the rename consumes it, so the
        // two cannot coexist.
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "out.cxt")]);
        var pending = Path.Combine(run.Directory, "out.fcabedrock-pending-aaaaaaaabbbbbbbbccccccccdddddddd");
        await File.WriteAllTextAsync(pending, string.Empty);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(pending));
        Assert.True(File.Exists(residue.RecordPath));
    }

    // ---- the demoted manifest is a transaction participant ---------------------------------------------

    [Fact]
    public async Task Publication_WhenTheDemotedManifestIsTheDataFile_ThenItIsRefusedEvenWithForce()
    {
        // CX-M7H-013. A --no-manifest --force run renames the old manifest aside and deletes it.
        // That makes it a mutated participant, and --force never authorizes destroying an input —
        // so it must be in the collision preflight, not discovered afterwards.
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("out.manifest.toml", CliFixtures.WideData);

        var exit = await harness.RunAsync(
            "convert", spec, data, "--out", temp.Resolve("out"), "--format", "cxt", "--no-manifest", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{temp.Resolve("out")}.manifest.toml' and the input '{data}' are the same file."),
            harness.StdErr);

        // The DATA file is still there, and no transaction was begun.
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(data));
        Assert.False(File.Exists(temp.Resolve("out.cxt")));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.fcabedrock-*"));
    }

    [Fact]
    public async Task Publication_WhenTheDemotedManifestIsTheRootSpec_ThenItIsRefusedEvenWithForce()
    {
        using var temp = TempDirectory.Create();
        var harness = new CliTestHarness();
        var spec = temp.Write("out.manifest.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);

        var exit = await harness.RunAsync(
            "convert", spec, data, "--out", temp.Resolve("out"), "--format", "cxt", "--no-manifest", "--force");

        Assert.Equal(1, exit);
        Assert.Contains("are the same file", harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(CliFixtures.IndexBoundSpec, await File.ReadAllTextAsync(spec));
    }

    [Fact]
    public async Task Publication_WhenTheDemotedManifestIsAHardLinkAliasOfTheData_ThenItIsRefusedEvenWithForce()
    {
        // Path text cannot see this; actual filesystem identity can.
        using var run = ConvertRun.Wide();
        if (!PlatformLinks.TryCreateHardLink(run.Target(".manifest.toml"), run.Data, out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest", "--force");

        Assert.Equal(1, exit);
        Assert.Contains("are the same file", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(CliFixtures.WideData, await File.ReadAllTextAsync(run.Data));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenTheDemotedManifestAliasesTheCxtTarget_ThenItIsRefusedEvenWithForce()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".cxt"), "previous");

        if (!PlatformLinks.TryCreateHardLink(run.Target(".manifest.toml"), run.Target(".cxt"), out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        var exit = await run.ConvertAsync("--format", "cxt", "--no-manifest", "--force");

        Assert.Equal(1, exit);
        Assert.Contains("are the same file", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("previous", await File.ReadAllTextAsync(run.Target(".cxt")));
    }

    // ---- case-aware transaction ownership ---------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenPriorResidueUsesAnotherCase_ThenItIsRecoveredBeforeTheNewRun()
    {
        // CX-M7H-016. On a case-insensitive directory `OUT` and `out` name the same physical
        // files. Residue this run cannot see is residue it can publish beside — and that a later
        // run spelled the other way can then delete as its own.
        using var run = ConvertRun.Wide();
        if (!IsCaseInsensitive(run.Directory))
        {
            Assert.Skip("this directory is case-sensitive, so the two spellings are different namespaces.");
        }

        var residue = Residue.Create(run.Directory, "OUT", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "OUT.cxt")]);
        residue.WritePrivate("stage", "OUT.cxt", "the interrupted run's bytes");
        residue.WriteEvidence(
            "c", "OUT.cxt", IdentityEvidence.NotApplicable, residue.Identity("stage", "OUT.cxt"));
        residue.WriteMarker("staged");

        var exit = await run.ConvertAsync("--format", "cxt");

        // The uppercase transaction was found, rolled back, and cleared — and only then did this
        // run publish. Nothing of it survives to act on the new artifact later.
        Assert.Equal(0, exit);
        Assert.Empty(run.Residue());
        Assert.Equal(
            "B\n\n2\n2\n\n0\n1\ncolour-red\ncolour-green\nX.\n.X\n", run.Text(".cxt"));
    }

    [Fact]
    public async Task Publication_WhenACaseVariantRecordIsMalformed_ThenTheRunIsRefused()
    {
        using var run = ConvertRun.Wide();
        if (!IsCaseInsensitive(run.Directory))
        {
            Assert.Skip("this directory is case-sensitive, so the two spellings are different namespaces.");
        }

        var lookalike = Path.Combine(run.Directory, "OUT.fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml");
        await File.WriteAllTextAsync(lookalike, "not a record");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal("not a record", await File.ReadAllTextAsync(lookalike));
    }

    [Fact]
    public async Task Publication_WhenTwoCaseSpellingsBothBearResidue_ThenTheRunIsRefusedAsAmbiguous()
    {
        using var run = ConvertRun.Wide();
        if (!IsCaseInsensitive(run.Directory))
        {
            Assert.Skip("this directory is case-sensitive, so the two spellings are different namespaces.");
        }

        Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd")
            .WriteRecord([("stage", "out.cxt")]);
        Residue.Create(run.Directory, "OUT", "bbbbbbbbccccccccddddddddeeeeeeee")
            .WriteRecord([("stage", "OUT.cxt")]);

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(2, run.Residue().Count);
    }

    // ---- confidential stages ------------------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenAStageIsCreated_ThenItIsRequestedThroughTheConfidentialPath()
    {
        // CX-M7H-017. The boundary is a property of the CALL, so it is asserted on the call: every
        // data-bearing stage asks for a confidential creation, while control files — whose names
        // are already visible in the directory listing — do not.
        using var run = ConvertRun.Wide();

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        foreach (var operation in run.Harness.PublicationFiles.Operations)
        {
            if (operation.Contains(".fcabedrock-stage-", StringComparison.Ordinal)
                && (operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                    || operation.StartsWith("Confidential:", StringComparison.Ordinal)))
            {
                Assert.StartsWith("Confidential:", operation, StringComparison.Ordinal);
            }
        }

        Assert.Equal(
            3,
            run.Harness.PublicationFiles.Operations
                .Count(operation => operation.StartsWith("Confidential:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Publication_WhenAStageSurvivesAsResidue_ThenItIsReadableOnlyByItsOwner()
    {
        // A stage that outlives its run is exactly the disclosure risk: uncommitted converted data
        // sitting in a directory others may list.
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes — asserted on Unix; Windows has no equivalent mode to read back.");
            return;
        }

        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CrashAfter = "StreamWrite:out.cxt.fcabedrock-stage-T";

        await run.ConvertAsync("--format", "cxt");

        var stage = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(stage));
    }

    [Fact]
    public async Task Publication_WhenAStageSurvivesAsResidue_ThenItsWindowsAclIsOwnerOnly()
    {
        // CX-M7H-020. `FileShare.None` guards only a live handle, and crash residue is exactly the
        // case where no handle is left — so the boundary that matters is the file's own DACL,
        // established at creation and protected from inheritance, as the spool workspace does.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows DACL — asserted on Windows.");
        if (OperatingSystem.IsWindows())
        {
            using var run = ConvertRun.Wide();
            run.Harness.PublicationFiles.CrashAfter = "StreamWrite:out.cxt.fcabedrock-stage-T";

            await run.ConvertAsync("--format", "cxt");

            var stage = Directory.GetFiles(run.Directory, "out.cxt.fcabedrock-stage-*").Single();
            var security = new FileInfo(stage).GetAccessControl();

            Assert.True(security.AreAccessRulesProtected, "the stage inherits the directory's access rules");

            var owner = WindowsIdentity.GetCurrent().User;
            Assert.Equal(owner, security.GetOwner(typeof(SecurityIdentifier)));

            var rules = security.GetAccessRules(
                includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
            Assert.NotEmpty(rules);
            foreach (FileSystemAccessRule rule in rules)
            {
                Assert.Equal(owner, rule.IdentityReference);
            }
        }
    }

    // ---- residue that is not ours -------------------------------------------------------------------

    [Theory]
    [InlineData("out.fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml", "version = 2\n")]
    [InlineData("out.fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml", "notes about my run\n")]
    [InlineData("out.fcabedrock-transaction-nothex.toml", "version = 1\n")]
    [InlineData("out.cxt.fcabedrock-stage-aaaaaaaabbbbbbbbccccccccdddddddd", "someone else's file")]
    [InlineData("out.cxt.fcabedrock-backup-not-a-token", "someone else's file")]
    public async Task Publication_WhenTheLocationHoldsAnUnknownLookalike_ThenItIsRefusedAndLeftByteIdentical(
        string name, string content)
    {
        using var run = ConvertRun.Wide();
        var path = Path.Combine(run.Directory, name);
        await File.WriteAllTextAsync(path, content);

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output base '{run.Base}' has unrecognized fcabedrock transaction residue;"
                + " remove it and retry."),
            run.Harness.StdErr);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(run.Target(".cxt")));
    }

    [Fact]
    public async Task Publication_WhenARecordNamesAnotherBase_ThenItIsNotBelieved()
    {
        // The record's own content claims a different base than the file name's namespace, so it
        // cannot be reconstructed and is not a record for this run.
        using var run = ConvertRun.Wide();
        var path = Path.Combine(run.Directory, "out.fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml");
        await File.WriteAllTextAsync(
            path,
            "version = 1\ntoken = \"aaaaaaaabbbbbbbbccccccccdddddddd\"\nbase = \"elsewhere\"\n",
            new UTF8Encoding(false));

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Publication_WhenARecordNamesATargetOutsideItsBase_ThenItIsNotBelieved()
    {
        // A crafted record cannot widen what recovery may delete: every target it may name is
        // one of the three this base computes, and every owned path is derived, not read.
        using var run = ConvertRun.Wide();
        var path = Path.Combine(run.Directory, "out.fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml");
        await File.WriteAllTextAsync(
            path,
            "version = 1\ntoken = \"aaaaaaaabbbbbbbbccccccccdddddddd\"\nbase = \"out\"\n\n"
            + "[[file]]\nrole = \"stage\"\ntarget = \"../../elsewhere.txt\"\n",
            new UTF8Encoding(false));

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Publication_WhenAFileWearsARecordNameButIsHuge_ThenItIsRefusedUnread()
    {
        // Bounded reads: a lookalike must not be able to name an unbounded allocation before a
        // single validity rule has run.
        using var run = ConvertRun.Wide();
        var path = Path.Combine(run.Directory, "out.fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml");
        await File.WriteAllBytesAsync(path, new byte[PublicationTargets.MaxRecordBytes + 1]);

        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Contains("unrecognized fcabedrock transaction residue", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.Equal(PublicationTargets.MaxRecordBytes + 1, new FileInfo(path).Length);
    }

    [Fact]
    public async Task Publication_WhenAnOrdinaryNeighbourSharesTheBasePrefix_ThenItIsNotResidue()
    {
        using var run = ConvertRun.Wide();
        var neighbour = Path.Combine(run.Directory, "out-notes.txt");
        await File.WriteAllTextAsync(neighbour, "unrelated");

        Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));

        Assert.Equal("unrelated", await File.ReadAllTextAsync(neighbour));
    }

    [Fact]
    public async Task Publication_WhenCleanupCannotFinish_ThenTheRecordSurvivesForALaterRecovery()
    {
        using var run = ConvertRun.Wide();
        var residue = Residue.Create(run.Directory, "out", "aaaaaaaabbbbbbbbccccccccdddddddd");
        residue.WriteRecord([("stage", "out.cxt")]);
        residue.WritePrivate("stage", "out.cxt", "half-written");
        residue.WriteEvidence(
            "c", "out.cxt", IdentityEvidence.NotApplicable, residue.Identity("stage", "out.cxt"));
        residue.WriteMarker("staged");

        run.Harness.PublicationFiles.FailDeletePrefix = "out.cxt.fcabedrock-stage-";
        var exit = await run.ConvertAsync("--format", "cxt");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}'."),
            run.Harness.StdErr);

        // Honest residue: the stage that could not be removed is still owned by a record, so a
        // later run can finish the job safely rather than meeting an orphan.
        Assert.True(File.Exists(residue.RecordPath));
        Assert.True(File.Exists(residue.PrivatePath("stage", "out.cxt")));

        // And an ordinary retry, with the removal no longer refused, finishes it.
        var retry = new CliTestHarness();
        Assert.Equal(0, await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        Assert.Empty(run.Residue());
    }

    // ---- cancellation ------------------------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenTheRunIsCancelledBeforeStaging_ThenNothingIsCommittedAndNothingIsReported()
    {
        using var run = ConvertRun.Wide();
        run.Harness.Signals.Cancel();

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Publication_WhenTheRunIsCancelledDuringEmission_ThenNoRunIsCommittedAndNoResidueSurvives()
    {
        using var run = ConvertRun.Wide();

        // Cancelled as the emit pass opens the data, so the signal lands inside the staging work
        // rather than at a phase boundary.
        var opens = 0;
        run.Harness.OpenInput = path =>
        {
            if (path.EndsWith(".csv", StringComparison.Ordinal) && opens++ == 1)
            {
                run.Harness.Signals.Cancel();
            }

            return File.OpenRead(path);
        };

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, run.Harness.StdOut);
        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Empty(Directory.GetFiles(run.Directory, "out*"));
    }

    [Fact]
    public async Task Publication_WhenTheRunIsCancelledAfterTheCommitPoint_ThenTheCommittedRunStands()
    {
        // A signal that arrives once the manifest has published must not report a public run as
        // cancelled, and must not unwind it.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.CancelAfterMove = run.Harness.Signals.Cancel;
        run.Harness.PublicationFiles.CancelAfterMoveTo = "out.manifest.toml";

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    // ---- helpers ---------------------------------------------------------------------------------

    // The stage-name prefix for `target`. The token is unpredictable, so the harness matches the
    // stage by the part of the name that is derived rather than random.
    private static string StageOf(string target) => $"{target}.fcabedrock-{PublicationTargets.StageRole}-";

    private static string Extension(string target) => target["out".Length..];

    // Measured, not assumed: whether this directory treats two spellings as one file is a
    // per-directory property, and the tests that depend on it say which one they need.
    private static bool IsCaseInsensitive(string directory)
    {
        var probe = Path.Combine(directory, "case-probe");
        File.WriteAllText(probe, "probe");
        try
        {
            return File.Exists(Path.Combine(directory, "CASE-PROBE"));
        }
        finally
        {
            File.Delete(probe);
        }
    }

    // The private-name role a created file carries: `intent`, `pending`, `stage`, `ep`, `staged`,
    // `rollback`, or `committed`. Everything after it — token, shape, digest, kind code — is
    // dropped, so the sequence is assertable without knowing an unpredictable value.
    private static string Kind(string fileName)
    {
        var marker = fileName.IndexOf(".fcabedrock-", StringComparison.Ordinal) + ".fcabedrock-".Length;
        var rest = fileName[marker..];
        var dash = rest.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? rest : rest[..dash];
    }

    // The transaction record this run published, read off its own operation log — the destination
    // of the rename that made it discoverable.
    private static string RecordCreated(CliTestHarness harness)
    {
        foreach (var operation in harness.PublicationFiles.Operations)
        {
            var arrow = operation.IndexOf("->", StringComparison.Ordinal);
            if (operation.StartsWith("Move:", StringComparison.Ordinal)
                && arrow >= 0
                && operation[(arrow + 2)..].Contains(".fcabedrock-transaction-", StringComparison.Ordinal))
            {
                return operation[(arrow + 2)..];
            }
        }

        return string.Empty;
    }

    // The recorded moves with each run token folded to `T`, so the ORDER is asserted without the
    // test having to know an unpredictable value.
    private static List<string> Moves(CliTestHarness harness)
    {
        var moves = new List<string>();
        foreach (var operation in harness.PublicationFiles.Operations)
        {
            if (operation.StartsWith("Move:", StringComparison.Ordinal))
            {
                moves.Add(RecordingPublicationFileSystem.Fold(operation));
            }
        }

        return moves;
    }

    // Every object the run removed, in order — with each token folded to `T`.
    private static List<string> Removals(CliTestHarness harness) =>
        [.. harness.PublicationFiles.Operations
            .Where(operation => operation.StartsWith("Delete:", StringComparison.Ordinal))
            .Select(operation => RecordingPublicationFileSystem.Fold(operation)["Delete:".Length..])];
}

/// <summary>
/// Hand-built transaction residue. The record format is written out literally here rather than
/// through the production writer, so a change to it fails these tests instead of silently keeping
/// them green.
/// </summary>
internal sealed class Residue
{
    private Residue(string directory, string baseName, string token)
    {
        Directory = directory;
        BaseName = baseName;
        Token = token;
    }

    public string Directory { get; }

    public string BaseName { get; }

    public string Token { get; }

    public string RecordPath =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-transaction-{Token}.toml");

    public static Residue Create(string directory, string baseName, string token) =>
        new(directory, baseName, token);

    public string PrivatePath(string role, string target) =>
        Path.Combine(Directory, $"{target}.fcabedrock-{role}-{Token}");

    public string MarkerPath(string phase) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-{phase}-{Token}");

    /// <summary>The evidence path for a target, by its single-character kind code.</summary>
    public string EvidencePath(string kind) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-e-{kind}-{Token}");

    /// <summary>The pending-evidence path for a target, by its single-character kind code.</summary>
    public string EvidencePendingPath(string kind) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-ep-{kind}-{Token}");

    /// <summary>
    /// Records that the interrupted transaction durably reached <paramref name="phase"/>. The body
    /// is the canonical control document — the run token, this base, the phase's own role, and the
    /// digest of the record this residue carries — spelled out here rather than produced by the
    /// writer under test, so a change to it fails these tests instead of silently keeping them
    /// green.
    /// </summary>
    public void WriteMarker(string phase) => File.WriteAllBytes(MarkerPath(phase), ControlBody(phase));

    /// <summary>
    /// The exact bytes a control of <paramref name="role"/> carries for this residue, with the
    /// identity it acknowledges where it acknowledges one.
    /// </summary>
    public byte[] ControlBody(string role, string? acknowledged = null)
    {
        var text = new StringBuilder()
            .Append("version = 1\n")
            .Append("token = \"").Append(Token).Append("\"\n")
            .Append("base = \"").Append(BaseName).Append("\"\n")
            .Append("control = \"").Append(role).Append("\"\n")
            .Append("record = \"").Append(RecordDigest).Append("\"\n");

        if (acknowledged is not null)
        {
            text.Append("stage = \"").Append(acknowledged).Append("\"\n");
        }

        return new UTF8Encoding(false).GetBytes(text.ToString());
    }

    /// <summary>The digest of the record this residue wrote — what every control of it binds to.</summary>
    public string RecordDigest { get; private set; } = string.Empty;

    /// <summary>
    /// The stage-claim path for a target, by its single-character kind code and the identity digest
    /// the claim asserts about the object its create-new produced.
    /// </summary>
    public string StageClaimPath(string kind, string digest) =>
        Path.Combine(Directory, $"{BaseName}.fcabedrock-sc-{kind}-{Token}-{digest}");

    /// <summary>
    /// Records that the interrupted transaction created the stage object for <paramref name="target"/>
    /// — the durable fact that separates its own stage from a file that merely occupies the path.
    /// The identity it acknowledges appears in both the name and the body, and the body is spelled
    /// out here rather than produced by the writer under test.
    /// </summary>
    public string WriteStageClaim(string kind, string target)
    {
        var digest = Identity("stage", target);
        var path = StageClaimPath(kind, digest);
        File.WriteAllBytes(path, ControlBody("claim-" + kind, digest));
        return path;
    }

    /// <summary>
    /// Writes one target's identity evidence, spelled out here rather than produced by the writer
    /// under test — so a change to the format fails these tests instead of silently keeping them
    /// green. The two identity <em>values</em> necessarily come from the real filesystem: they name
    /// objects, and only the host can say what an object is.
    /// </summary>
    public void WriteEvidence(string kind, string target, string backup, string stage)
    {
        var text = new StringBuilder()
            .Append("version = 1\n")
            .Append("token = \"").Append(Token).Append("\"\n")
            .Append("base = \"").Append(BaseName).Append("\"\n")
            .Append("target = \"").Append(target).Append("\"\n")
            .Append("backup = \"").Append(backup).Append("\"\n")
            .Append("stage = \"").Append(stage).Append("\"\n");

        File.WriteAllText(EvidencePath(kind), text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The identity digest of the file at <paramref name="path"/>, as this transaction would record it.</summary>
    public string IdentityOf(string path, string role, string target) =>
        IdentityEvidence.Of(Token, role, target, FileIdentity.CreateDefault().KeyFor(path));

    /// <summary>The identity digest of a private file this residue already holds.</summary>
    public string Identity(string role, string target) => IdentityOf(PrivatePath(role, target), role, target);

    /// <summary>
    /// The identity digest of an object that <b>no longer exists</b> — what evidence carries for a
    /// stage a commit rename consumed, or a backup a restore renamed home.
    /// </summary>
    public string ConsumedIdentity(string role, string target)
    {
        var path = PrivatePath(role, target);
        File.WriteAllText(path, "consumed");
        var digest = Identity(role, target);
        File.Delete(path);
        return digest;
    }

    public void WriteRecord(IReadOnlyList<(string Role, string Target)> files)
    {
        var text = new StringBuilder()
            .Append("version = 1\n")
            .Append("token = \"").Append(Token).Append("\"\n")
            .Append("base = \"").Append(BaseName).Append("\"\n");

        foreach (var (role, target) in files)
        {
            text.Append("\n[[file]]\nrole = \"").Append(role).Append("\"\ntarget = \"").Append(target).Append("\"\n");
        }

        var bytes = new UTF8Encoding(false).GetBytes(text.ToString());
        File.WriteAllBytes(RecordPath, bytes);

        // Every control of this transaction binds to this record; the digest is the first 128 bits
        // of its SHA-256, exactly as the writer under test computes it.
        RecordDigest = Convert.ToHexStringLower(SHA256.HashData(bytes).AsSpan(0, 16));
    }

    public void WritePrivate(string role, string target, string content) =>
        File.WriteAllText(PrivatePath(role, target), content, new UTF8Encoding(false));
}
