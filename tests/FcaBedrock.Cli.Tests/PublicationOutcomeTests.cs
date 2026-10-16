using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// What a publishing run reports when it cannot do everything it was asked (D-122 parts 2–5,
/// D-123 point 7, D-125): a Windows read-only participant refused before anything moves, a
/// committed run whose private cleanup did not finish, a recovery that names the entry it could
/// not remove, and the controlled reasons for an unusable output location.
/// <para>
/// Every case runs through argv and inspects the real directory afterwards. Where a cleanup step
/// is made to fail, the failure is injected at the publication filesystem seam, and the test says
/// so; a read-only attribute is always a real one.
/// </para>
/// </summary>
public sealed class PublicationOutcomeTests
{
    // ---- read-only participants ----------------------------------------------------------------

    [Fact]
    public async Task Publication_WhenForceWouldReplaceAReadOnlyOutput_ThenWindowsRefusesBeforeAnythingMoves()
    {
        using var run = ConvertRun.Wide();
        try
        {
            Assert.Equal(0, await run.ConvertAsync("--format", "both"));
            SetReadOnly(run.Target(".cxt"), true);
            var before = run.Snapshot();

            var second = new CliTestHarness();
            var exit = await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

            if (!OperatingSystem.IsWindows())
            {
                // There is no write-bit veto off Windows: a file without write permission in a
                // writable directory is renamed aside and removed like any other.
                Assert.Equal(0, exit);
                Assert.Equal(string.Empty, second.StdErr);
                Assert.Empty(run.Residue());
                return;
            }

            Assert.Equal(1, exit);
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"the output '{run.Target(".cxt")}' is read-only; clear its read-only attribute to replace it, "
                    + "or choose a different --out."),
                second.StdErr);

            // Refused before a record, a stage or a backup existed: nothing moved, and the
            // attribute is the user's, so it is left exactly as it was.
            Assert.Equal(before, run.Snapshot());
            Assert.True(IsReadOnly(run.Target(".cxt")));
            Assert.Empty(run.Residue());
            Assert.DoesNotContain(second.PublicationFiles.Operations, IsMutation);
        }
        finally
        {
            ClearReadOnly(run.Directory);
        }
    }

    [Fact]
    public async Task Publication_WhenForceWouldRemoveAReadOnlyStaleManifest_ThenWindowsRefusesBeforeAnythingMoves()
    {
        // --no-manifest demotes an old manifest and then removes it, so a read-only one blocks
        // exactly the same cleanup a read-only replaced output does.
        using var run = ConvertRun.Wide();
        try
        {
            Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
            File.Delete(run.Target(".cxt"));
            SetReadOnly(run.Target(".manifest.toml"), true);
            var before = run.Snapshot();

            var second = new CliTestHarness();
            var exit = await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--no-manifest", "--force");

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(0, exit);
                Assert.False(File.Exists(run.Target(".manifest.toml")));
                Assert.Empty(run.Residue());
                return;
            }

            Assert.Equal(1, exit);
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"the manifest '{run.Target(".manifest.toml")}' is read-only, so --force cannot remove it; "
                    + "clear its read-only attribute, or choose a different --out."),
                second.StdErr);
            Assert.Equal(before, run.Snapshot());
            Assert.False(File.Exists(run.Target(".cxt")));
            Assert.Empty(run.Residue());
        }
        finally
        {
            ClearReadOnly(run.Directory);
        }
    }

    [Fact]
    public async Task Publication_WhenAnOutputBecomesReadOnlyBeforeSealing_ThenTheRunIsRefusedBeforeAnyFinalMoves()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the cleanup-blocking attribute is a Windows property.");

        using var run = ConvertRun.Wide();
        try
        {
            Assert.Equal(0, await run.ConvertAsync("--format", "both"));

            // The attribute appears after preflight approved the output and before the seal: the
            // second check, made through the same held reference, is what refuses it.
            var second = new CliTestHarness();
            second.PublicationFiles.MutateBefore = "Confidential:out.cxt.fcabedrock-stage-T";
            second.PublicationFiles.Mutate = () => SetReadOnly(run.Target(".cxt"), true);
            var before = run.Snapshot();

            var exit = await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

            Assert.Equal(1, second.PublicationFiles.MutationsFired);
            Assert.Equal(1, exit);
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"the output '{run.Target(".cxt")}' is read-only; clear its read-only attribute to replace it, "
                    + "or choose a different --out."),
                second.StdErr);

            // No final target was renamed aside or published; rollback removed this run's own
            // private files and nothing else.
            Assert.DoesNotContain(
                second.PublicationFiles.Operations,
                operation => operation.StartsWith("Move:out.", StringComparison.Ordinal)
                    && !operation.Contains("fcabedrock", StringComparison.Ordinal));
            Assert.Equal(before, run.Snapshot());
            Assert.Empty(run.Residue());
        }
        finally
        {
            ClearReadOnly(run.Directory);
        }
    }

    [Fact]
    public async Task Publication_WhenAParticipantIsReplaced_ThenItsHeldObjectIsAskedAtPreflightAndAgainAtTheSeal()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var second = new CliTestHarness();
        Assert.Equal(
            0,
            await second.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));

        // Exactly twice per participant, both before the first rename aside; no third query
        // precedes each rename.
        var operations = second.PublicationFiles.Operations.Select(RecordingPublicationFileSystem.Fold).ToList();
        var firstBackup = operations.FindIndex(operation => operation.StartsWith("Move:out.", StringComparison.Ordinal)
            && operation.EndsWith(".fcabedrock-backup-T", StringComparison.Ordinal));
        var seal = operations.IndexOf("CreateNew:out.fcabedrock-staged-T");
        var firstStage = operations.FindIndex(operation => operation.StartsWith("Confidential:", StringComparison.Ordinal));
        foreach (var participant in new[] { "out.manifest.toml", "out.cxt", "out.dat" })
        {
            var queries = operations
                .Select((operation, index) => (operation, index))
                .Where(entry => entry.operation == "ReadOnly:" + participant)
                .Select(entry => entry.index)
                .ToList();
            Assert.Equal(2, queries.Count);
            Assert.True(queries[0] < firstStage, $"{participant} was not asked before staging");
            Assert.True(firstStage < queries[1] && queries[1] < seal, $"{participant} was not asked at the seal");
            Assert.True(queries[1] < firstBackup);
        }
    }

    [Fact]
    public async Task Publication_WhenTheReadOnlyQueryIsUnavailable_ThenNoClaimIsMadeAndTheExistingOutcomesApply()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the read-only query is answered only on Windows.");

        using var run = ConvertRun.Wide();
        try
        {
            Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
            SetReadOnly(run.Target(".cxt"), true);

            // Injected: the query answers "unknown" for the old output. Nothing is refused for that
            // alone, so the replacement commits, and the removal the attribute then blocks is
            // reported from what that removal itself established, with no read-only claim.
            var second = new CliTestHarness();
            second.PublicationFiles.ReadOnlyUnavailablePrefix = "out.cxt";
            Assert.Equal(
                0,
                await second.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));

            Assert.True(second.PublicationFiles.ReadOnlyUnavailable >= 2);
            AssertManifestCertifiesOutputs(run);
            var backup = Single(run.Residue(), ".cxt.fcabedrock-backup-");
            Assert.Equal(
                DiagnosticRenderer.RenderHostWarning(
                    $"the output base '{run.Base}' is published and complete, but its private cleanup did not finish: "
                    + $"'{backup}' could not be removed because access to it was denied; a later run tries the "
                    + "cleanup again."),
                second.StdErr);

            // The next run, whose query does answer, names the attribute.
            var retry = new CliTestHarness();
            Assert.Equal(
                1,
                await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));
            Assert.Contains("because it is read-only", retry.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            ClearReadOnly(run.Directory);
        }
    }

    // ---- a committed run whose cleanup did not finish -------------------------------------------

    [Fact]
    public async Task Publication_WhenABackupBecomesReadOnlyAfterSealing_ThenTheRunStandsWarnsAndARetryNamesTheBlocker()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the cleanup-blocking attribute is a Windows property.");

        using var run = ConvertRun.Wide();
        try
        {
            Assert.Equal(0, await run.ConvertAsync("--format", "both"));

            // After the seal the attribute is no longer checked: the rename aside still succeeds
            // (a read-only file can be renamed), and the removal of the superseded backup after
            // the commit point is what it blocks.
            var second = new CliTestHarness();
            second.PublicationFiles.MutateBefore = "Move:out.cxt->out.cxt.fcabedrock-backup-T";
            second.PublicationFiles.Mutate = () => SetReadOnly(run.Target(".cxt"), true);

            var exit = await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

            Assert.Equal(1, second.PublicationFiles.MutationsFired);
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, second.StdOut);

            // The run is public and complete: its manifest certifies exactly the bytes on disk.
            AssertManifestCertifiesOutputs(run);
            var published = run.Snapshot();

            var backup = Single(run.Residue(), ".cxt.fcabedrock-backup-");
            Assert.True(IsReadOnly(Path.Combine(run.Directory, backup)));
            Assert.Equal(
                DiagnosticRenderer.RenderHostWarning(
                    $"the output base '{run.Base}' is published and complete, but its private cleanup did not "
                    + $"finish: '{backup}' could not be removed because it is read-only; after that attribute is "
                    + "cleared, a later run tries the cleanup again."),
                second.StdErr);

            // A later run meets the same blocker and names it, preserving everything.
            var residue = run.Residue();
            var retry = new CliTestHarness();
            Assert.Equal(
                1,
                await retry.RunAsync(
                    "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}': '{backup}' "
                    + "could not be removed because it is read-only; after that attribute is cleared, a later run "
                    + "tries the cleanup again."),
                retry.StdErr);
            Assert.Contains(backup, run.Residue());
            Assert.Equal(published["out.cxt"], run.Snapshot()["out.cxt"]);

            // Clearing that one attribute is necessary, not sufficient: here the next attempt
            // meets a different failure and says so, without claiming the repair worked.
            SetReadOnly(Path.Combine(run.Directory, backup), false);
            var blocked = new CliTestHarness();
            blocked.PublicationFiles.FailDeletePrefix = "out.fcabedrock-committed-";
            Assert.Equal(
                1,
                await blocked.RunAsync(
                    "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));
            var marker = Single(run.Residue(), ".fcabedrock-committed-");
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"cannot clean up an incomplete fcabedrock run for the output base '{run.Base}': '{marker}' "
                    + "could not be removed; a later run tries the cleanup again."),
                blocked.StdErr);
            Assert.True(residue.Count > run.Residue().Count);

            // And with nothing else in the way, an ordinary retry finishes the cleanup and publishes.
            var last = new CliTestHarness();
            Assert.Equal(
                0,
                await last.RunAsync(
                    "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));
            Assert.Equal(string.Empty, last.StdErr);
            Assert.Empty(run.Residue());
            AssertManifestCertifiesOutputs(run);
        }
        finally
        {
            ClearReadOnly(run.Directory);
        }
    }

    [Theory]
    [InlineData("out.cxt.fcabedrock-backup-")]
    [InlineData("out.fcabedrock-committed-")]
    [InlineData("out.fcabedrock-staged-")]
    [InlineData("out.fcabedrock-e-c-")]
    [InlineData("out.fcabedrock-sc-c-")]
    [InlineData("out.fcabedrock-intent-")]
    [InlineData("out.fcabedrock-transaction-")]
    public async Task Publication_WhenAPostCommitCleanupStepFails_ThenTheRunStandsWithOneWarningAndARetryConverges(
        string failing)
    {
        // Injected: the removal of one private entry fails with an ordinary I/O error, every time
        // this run attempts it. The commit point has passed, so nothing is rolled back.
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var second = new CliTestHarness();
        second.PublicationFiles.FailDeletePrefix = failing;
        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        Assert.Equal(0, exit);
        AssertManifestCertifiesOutputs(run);

        var blocker = Single(run.Residue(), failing["out".Length..]);
        Assert.Equal(
            DiagnosticRenderer.RenderHostWarning(
                $"the output base '{run.Base}' is published and complete, but its private cleanup did not finish: "
                + $"'{blocker}' could not be removed; a later run tries the cleanup again."),
            second.StdErr);

        var retry = new CliTestHarness();
        Assert.Equal(
            0,
            await retry.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force"));
        Assert.Equal(string.Empty, retry.StdErr);
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenCleanupIsIncomplete_ThenTheWarningFollowsTheRunsDiagnostics()
    {
        using var run = ConvertRun.Wide(CliFixtures.WarningOnlySpec);
        var first = new CliTestHarness();
        Assert.Equal(0, await first.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt"));
        var diagnostics = first.StdErr;
        Assert.NotEqual(string.Empty, diagnostics);

        var second = new CliTestHarness();
        second.PublicationFiles.FailDeletePrefix = "out.cxt.fcabedrock-backup-";
        Assert.Equal(
            0,
            await second.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));

        var backup = Single(run.Residue(), ".cxt.fcabedrock-backup-");
        Assert.Equal(
            diagnostics
            + DiagnosticRenderer.RenderHostWarning(
                $"the output base '{run.Base}' is published and complete, but its private cleanup did not finish: "
                + $"'{backup}' could not be removed; a later run tries the cleanup again."),
            second.StdErr);
    }

    [Fact]
    public async Task Publication_WhenTheSignalArrivesAfterTheCommitPointAndCleanupFails_ThenTheRunStillExitsZero()
    {
        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        var second = new CliTestHarness();
        second.PublicationFiles.CancelAfterMoveTo = "out.manifest.toml";
        second.PublicationFiles.CancelAfterMove = second.Signals.Cancel;
        second.PublicationFiles.FailDeletePrefix = "out.dat.fcabedrock-backup-";

        var exit = await second.RunAsync(
            "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");

        // A late signal neither rolls back a public run nor turns its cleanup warning into exit 3.
        Assert.True(second.Signals.Token.IsCancellationRequested);
        Assert.Equal(0, exit);
        AssertManifestCertifiesOutputs(run);
        var backup = Single(run.Residue(), ".dat.fcabedrock-backup-");
        Assert.Equal(
            DiagnosticRenderer.RenderHostWarning(
                $"the output base '{run.Base}' is published and complete, but its private cleanup did not finish: "
                + $"'{backup}' could not be removed; a later run tries the cleanup again."),
            second.StdErr);
    }

    [Fact]
    public async Task Publication_WhenTheCommittedMarkerCannotBeCreated_ThenACompletedCleanupWarnsAboutNothing()
    {
        // Marking the commit point is evidence for a later run. When it cannot be made but the
        // cleanup that follows still removes every private file, nothing remains to warn about.
        using var run = ConvertRun.Wide();
        run.Harness.PublicationFiles.FailCreateNewPrefix = "out.fcabedrock-committed-";

        Assert.Equal(0, await run.ConvertAsync("--format", "both"));

        Assert.Equal(string.Empty, run.Harness.StdErr);
        Assert.Empty(run.Residue());
        AssertManifestCertifiesOutputs(run);
    }

    [Fact]
    public async Task Publication_WhenTheBaseNameHoldsAControlCharacter_ThenTheWarningEscapesItExactlyOnce()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var outBase = temp.Resolve("o\u0085ut");

        var first = new CliTestHarness();
        Assert.Equal(0, await first.RunAsync("convert", spec, data, "--out", outBase, "--format", "cxt"));

        var second = new CliTestHarness();
        second.PublicationFiles.FailDeletePrefix = "o\u0085ut.cxt.fcabedrock-backup-";
        Assert.Equal(0, await second.RunAsync("convert", spec, data, "--out", outBase, "--format", "cxt", "--force"));

        // One transport pass: the raw character arrives as one escape, never as an escaped escape.
        var line = second.StdErr;
        Assert.StartsWith("warning: ", line, StringComparison.Ordinal);
        Assert.EndsWith("\n", line, StringComparison.Ordinal);
        Assert.Single(line.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("\u0085", line, StringComparison.Ordinal);
        Assert.Contains("o\\u0085ut.cxt.fcabedrock-backup-", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\\u0085", line, StringComparison.Ordinal);
    }

    // ---- the location and the targets ---------------------------------------------------------

    [Fact]
    public async Task Publication_WhenTheOutputFolderDoesNotExist_ThenTheRunSaysSoAndCreatesNothing()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var outBase = Path.Combine(temp.Resolve("missing"), "out");
        var harness = new CliTestHarness();

        Assert.Equal(1, await harness.RunAsync("convert", spec, data, "--out", outBase, "--format", "both"));

        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{outBase}': its folder does not exist, and "
                + "fcabedrock does not create folders; create it, or choose a different --out."),
            harness.StdErr);
        Assert.False(Directory.Exists(temp.Resolve("missing")));
    }

    [Fact]
    public async Task Publication_WhenTheOutputFolderDeniesCreatingFiles_ThenTheRunSaysAccessWasDenied()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the folder is denied through a Windows ACL.");

        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.IndexBoundSpec);
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var folder = Directory.CreateDirectory(temp.Resolve("denied"));
        var outBase = Path.Combine(folder.FullName, "out");
        var harness = new CliTestHarness();

        var deny = DenyCreatingFiles(folder);
        try
        {
            Assert.Equal(1, await harness.RunAsync("convert", spec, data, "--out", outBase, "--format", "cxt"));
        }
        finally
        {
            AllowCreatingFiles(folder, deny);
        }

        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{outBase}': access to its folder was denied; "
                + "choose a folder you can write to."),
            harness.StdErr);
        Assert.Empty(Directory.GetFileSystemEntries(folder.FullName));
    }

    [Fact]
    public async Task Publication_WhenATargetIsADirectory_ThenPreflightNamesItWhetherOrNotForced()
    {
        using var run = ConvertRun.Wide();
        Directory.CreateDirectory(run.Target(".dat"));

        foreach (var options in new[] { new[] { "--format", "both" }, ["--format", "both", "--force"] })
        {
            var harness = new CliTestHarness();
            Assert.Equal(1, await harness.RunAsync(["convert", run.Spec, run.Data, "--out", run.Base, .. options]));
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"the output '{run.Target(".dat")}' is a directory; choose a different --out."),
                harness.StdErr);
            Assert.DoesNotContain(harness.PublicationFiles.Operations, IsMutation);
        }

        Assert.True(Directory.Exists(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAnotherProgramHoldsTheOldOutputWithoutDeleteSharing_ThenTheRunSaysItIsInUse()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "sharing modes are a Windows property.");

        using var run = ConvertRun.Wide();
        Assert.Equal(0, await run.ConvertAsync("--format", "both"));
        var before = run.Snapshot();

        // Readers and writers may share it, but nobody may rename or delete it while this handle
        // is open: the rename aside is the operation that reports the sharing violation.
        var second = new CliTestHarness();
        int exit;
        using (new FileStream(run.Target(".dat"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            exit = await second.RunAsync(
                "convert", run.Spec, run.Data, "--out", run.Base, "--format", "both", "--force");
        }

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot publish the output '{run.Target(".dat")}': another program is using it; close that program, "
                + "or choose a different --out."),
            second.StdErr);
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publication_WhenAManifestBearingRunMeetsAnOldManifestWithoutForce_ThenItStillSaysReplace()
    {
        // The counterpart of the --no-manifest refusal: here the new manifest does replace the old
        // one, so the ordinary existing-target wording stays.
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(run.Target(".manifest.toml"), "the old marker");

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt"));

        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{run.Target(".manifest.toml")}' already exists; use --force to replace it."),
            run.Harness.StdErr);
    }

    [Fact]
    public async Task Publication_WhenUnknownResidueIsFound_ThenItIsLeftUntouchedWithoutDeletionAdvice()
    {
        using var run = ConvertRun.Wide();
        await File.WriteAllTextAsync(
            Path.Combine(run.Directory, "out.fcabedrock-transaction-notatoken.toml"), "lookalike");
        var before = run.Snapshot();

        Assert.Equal(1, await run.ConvertAsync("--format", "cxt", "--force"));

        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output base '{run.Base}' has unrecognized fcabedrock transaction residue, which was left "
                + "untouched; inspect it, or choose a different --out."),
            run.Harness.StdErr);
        Assert.Equal(before, run.Snapshot());
    }

    [Fact]
    public async Task Publication_WhenAnOutputIsSubstitutedAfterInspection_ThenNoReadOnlyClaimIsMadeAboutTheSubstitute()
    {
        // The read-only question is asked of the object preflight approved, never of whatever
        // occupies the name later. The substitute fails the existing identity proof, which keeps
        // its own message, and neither object is touched.
        using var run = ConvertRun.Wide();
        try
        {
            Assert.Equal(0, await run.ConvertAsync("--format", "cxt"));
            var original = run.Bytes(".cxt");
            var aside = Path.Combine(run.Directory, "aside.cxt");

            var second = new CliTestHarness();
            second.PublicationFiles.MutateBefore = "Confidential:out.cxt.fcabedrock-stage-T";
            second.PublicationFiles.Mutate = () =>
            {
                File.Move(run.Target(".cxt"), aside);
                File.WriteAllText(run.Target(".cxt"), "a substitute");
                SetReadOnly(run.Target(".cxt"), true);
            };

            Assert.Equal(
                1,
                await second.RunAsync("convert", run.Spec, run.Data, "--out", run.Base, "--format", "cxt", "--force"));

            Assert.Equal(1, second.PublicationFiles.MutationsFired);
            Assert.Equal(
                DiagnosticRenderer.RenderHostError($"cannot publish the output '{run.Target(".cxt")}'."),
                second.StdErr);
            Assert.Equal("a substitute", await File.ReadAllTextAsync(run.Target(".cxt")));
            Assert.Equal(original, await File.ReadAllBytesAsync(aside));
            Assert.Empty(run.Residue());
        }
        finally
        {
            ClearReadOnly(run.Directory);
        }
    }

    // ---- the single-file commands share every outcome ------------------------------------------

    [Fact]
    public async Task Publication_WhenASingleFileForceMeetsAReadOnlyTarget_ThenWindowsRefusesItToo()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        try
        {
            SetReadOnly(target, true);
            var harness = new CliTestHarness();

            var exit = await harness.RunAsync("probe", data, "--shape", "wide", "--out", target, "--force");

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(0, exit);
                return;
            }

            Assert.Equal(1, exit);
            Assert.Equal(
                DiagnosticRenderer.RenderHostError(
                    $"the output '{target}' is read-only; clear its read-only attribute to replace it, "
                    + "or choose a different --out."),
                harness.StdErr);
            Assert.Equal("the old file", await File.ReadAllTextAsync(target));
            Assert.Equal(["data.csv", "draft.toml"], Names(temp.Path));
        }
        finally
        {
            ClearReadOnly(temp.Path);
        }
    }

    [Fact]
    public async Task Publication_WhenASingleFileCleanupIsIncomplete_ThenItWarnsOnceAndExitsZero()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = temp.Write("draft.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailDeletePrefix = "draft.toml.fcabedrock-backup-";

        Assert.Equal(0, await harness.RunAsync("probe", data, "--shape", "wide", "--out", target, "--force"));

        Assert.Equal(string.Empty, harness.StdOut);
        Assert.NotEqual("the old file", await File.ReadAllTextAsync(target));
        var backup = Single(Names(temp.Path), "draft.toml.fcabedrock-backup-");
        Assert.Equal(
            DiagnosticRenderer.RenderHostWarning(
                $"the output base '{target}' is published and complete, but its private cleanup did not finish: "
                + $"'{backup}' could not be removed; a later run tries the cleanup again."),
            harness.StdErr);

        var retry = new CliTestHarness();
        Assert.Equal(0, await retry.RunAsync("probe", data, "--shape", "wide", "--out", target, "--force"));
        Assert.Equal(["data.csv", "draft.toml"], Names(temp.Path));
    }

    [Fact]
    public async Task Publication_WhenASingleFileTargetIsADirectory_ThenPreflightNamesIt()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = Directory.CreateDirectory(temp.Resolve("draft.toml")).FullName;
        var harness = new CliTestHarness();

        Assert.Equal(1, await harness.RunAsync("probe", data, "--shape", "wide", "--out", target, "--force"));

        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"the output '{target}' is a directory; choose a different --out."),
            harness.StdErr);
        Assert.DoesNotContain(harness.PublicationFiles.Operations, IsMutation);
    }

    [Fact]
    public async Task Publication_WhenASingleFileFolderDoesNotExist_ThenTheRunSaysSo()
    {
        using var temp = TempDirectory.Create();
        var data = temp.Write("data.csv", CliFixtures.WideData);
        var target = Path.Combine(temp.Resolve("missing"), "draft.toml");
        var harness = new CliTestHarness();

        Assert.Equal(1, await harness.RunAsync("probe", data, "--shape", "wide", "--out", target));

        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"cannot start publication for the output base '{target}': its folder does not exist, and "
                + "fcabedrock does not create folders; create it, or choose a different --out."),
            harness.StdErr);
        Assert.False(Directory.Exists(temp.Resolve("missing")));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static bool IsMutation(string operation) =>
        operation.StartsWith("CreateNew:", StringComparison.Ordinal)
        || operation.StartsWith("Confidential:", StringComparison.Ordinal)
        || operation.StartsWith("Move:", StringComparison.Ordinal)
        || operation.StartsWith("Delete:", StringComparison.Ordinal);

    private static void SetReadOnly(string path, bool readOnly)
    {
        var attributes = File.GetAttributes(path);
        File.SetAttributes(
            path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
    }

    private static bool IsReadOnly(string path) => (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;

    // Read-only files would otherwise outlive the temporary directory's own cleanup.
    private static void ClearReadOnly(string directory)
    {
        foreach (var path in Directory.GetFiles(directory))
        {
            SetReadOnly(path, false);
        }
    }

    private static string Single(IEnumerable<string> names, string fragment) =>
        Assert.Single(names, name => name.Contains(fragment, StringComparison.Ordinal));

    private static IReadOnlyList<string> Names(string directory) =>
        [.. Directory.GetFiles(directory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    // The manifest names each output with the hash of its exact bytes; a committed run whose
    // cleanup did not finish must still be a run its manifest truthfully describes.
    private static void AssertManifestCertifiesOutputs(ConvertRun run)
    {
        var manifest = run.Text(".manifest.toml");
        foreach (var extension in new[] { ".cxt", ".dat" })
        {
            if (!File.Exists(run.Target(extension)))
            {
                continue;
            }

            var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(run.Bytes(extension)));
            Assert.Contains($"hash = \"{hash}\"", manifest, StringComparison.Ordinal);
        }
    }

    private static FileSystemAccessRule DenyCreatingFiles(DirectoryInfo folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
        return rule;
    }

    private static void AllowCreatingFiles(DirectoryInfo folder, FileSystemAccessRule deny)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        var security = folder.GetAccessControl();
        security.RemoveAccessRule(deny);
        folder.SetAccessControl(security);
    }
}
