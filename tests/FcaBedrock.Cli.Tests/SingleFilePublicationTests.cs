using System.Text;
using FcaBedrock.Cli.Publication;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The single-file publication family (D-122 part 4, D-123 point 7), driven directly through
/// <see cref="PublicationTransaction.PreflightSingle"/> with the injected publication filesystem
/// and identity service. This checkpoint has no command and no host, so <b>no case here asserts a
/// CLI exit code</b>: a cancellation is proved by the exact token the transaction throws and by
/// the state it leaves. Every case inspects the real directory afterwards — one that claims the
/// old target survived compares the <em>bytes</em> there against the bytes that were there.
/// </summary>
public sealed class SingleFilePublicationTests
{
    private const string Old = "the old file";
    private const string New = "the new file";

    [Theory]
    [InlineData("draft.toml")]
    [InlineData("draft")]
    [InlineData("draft.cxt")]
    [InlineData("draft.manifest.toml")]
    public async Task Publish_WhenTheTargetIsAnArbitraryPath_ThenTheFileIsWrittenAtExactlyThatSpelling(string name)
    {
        // Including spellings that look like the artifacts family's own: the extension is empty.
        using var run = SingleRun.Create(name);

        Assert.Null(await run.PublishAsync(New));

        Assert.Equal(New, await File.ReadAllTextAsync(run.Out));
        Assert.Equal(new[] { "input.csv", name }.Order(StringComparer.Ordinal), run.Names());
    }

    [Theory]
    [InlineData(".cxt")]
    [InlineData(".dat")]
    [InlineData(".manifest.toml")]
    [InlineData(".toml")]
    public async Task Publish_WhenTheTargetAlreadyCarriesAnExtension_ThenNothingIsAppendedOrRemoved(string extension)
    {
        // Nothing lands at `draft.toml<extension>`, and nothing lands at a trimmed `draft` either.
        using var run = SingleRun.Create("draft.toml");

        Assert.Null(await run.PublishAsync(New));

        Assert.False(File.Exists(run.Out + extension), $"an extension was appended: {extension}");
        Assert.False(File.Exists(Path.Combine(run.Directory, "draft")), "the operand was trimmed");
    }

    [Fact]
    public async Task Publish_WhenTheTargetExistsWithoutForce_ThenTheRunIsRefusedAndNothingIsTouched()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var before = run.Snapshot();

        Assert.Equal(
            $"the output '{run.Out}' already exists; use --force to replace it.", await run.PublishAsync(New));

        // Preflight finishes before the transaction begins: no record, no stage, no backup.
        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());
    }

    [Fact]
    public async Task Publish_WhenForceReplacesADistinctTarget_ThenTheOldFileIsRenamedAsideNeverCopied()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);

        Assert.Null(await run.PublishAsync(New, force: true));

        // Renamed aside first, then the stage committed into the freed path — never a copy.
        var moves = run.Folded("Move:");
        Assert.Equal($"Move:{run.Name}->{run.Name}.fcabedrock-backup-T", moves[^2]);
        Assert.Equal($"Move:{run.Name}.fcabedrock-stage-T->{run.Name}", moves[^1]);
        Assert.Equal(New, await File.ReadAllTextAsync(run.Out));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheStageWriteFails_ThenTheOldTargetIsRestoredByteIdentically()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var before = run.Snapshot();
        run.Files.FailStreamWritePrefix = $"{run.Name}.fcabedrock-stage-";

        Assert.Equal($"cannot write the output '{run.Out}'.", await run.PublishAsync(New, force: true));

        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheCommitRenameFails_ThenTheOldTargetIsRestoredByteIdentically()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var before = run.Snapshot();
        run.Files.FailKind = "Move";
        run.Files.FailMoveTo = run.Name;

        Assert.Equal($"cannot publish the output '{run.Out}'.", await run.PublishAsync(New, force: true));

        // The backup rename already happened, so this proves the restore path.
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheHostTokenIsCancelledBeforeTheCommitPoint_ThenTheExactTokenPropagatesAndTheOldTargetIsUnchanged()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var before = run.Snapshot();
        run.Files.CancelAfterMoveToPrefix = $"{run.Name}.fcabedrock-backup-";
        run.Files.CancelAfterMove = run.Cancel;

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.PublishAsync(New, force: true));

        // The EXACT supplied token, not merely some cancellation.
        Assert.Equal(run.Token, thrown.CancellationToken);
        Assert.Equal(before, run.Snapshot());
        Assert.Empty(run.Residue());
    }

    [Theory]

    // Three different pre-commit instants: before the stage object exists, after it is written as
    // its evidence publishes, and after the old target has been renamed aside.
    [InlineData("Confidential:{0}.fcabedrock-stage-T")]
    [InlineData("StreamWrite:{0}.fcabedrock-ep-s-T")]
    [InlineData("backup")]
    public async Task Publish_WhenTheHostTokenIsCancelledAtEachPreCommitBoundary_ThenEveryOutcomeIsTokenExactAndRecoverable(
        string boundary)
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var before = run.Snapshot();

        if (string.Equals(boundary, "backup", StringComparison.Ordinal))
        {
            run.Files.CancelAfterMoveToPrefix = $"{run.Name}.fcabedrock-backup-";
            run.Files.CancelAfterMove = run.Cancel;
        }
        else
        {
            run.Files.MutateBefore = boundary.Replace("{0}", run.Name, StringComparison.Ordinal);
            run.Files.Mutate = run.Cancel;
        }

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.PublishAsync(New, force: true));

        Assert.Equal(run.Token, thrown.CancellationToken);
        Assert.Equal(before, run.Snapshot());

        // Recoverable: whatever survived, a later ordinary run completes and publishes.
        run.Restart();
        Assert.Null(await run.PublishAsync(New, force: true));
        Assert.Equal(New, await File.ReadAllTextAsync(run.Out));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheTokenIsCancelledAfterTheCommitPoint_ThenTheRunStands()
    {
        using var run = SingleRun.Create();
        run.Files.CancelAfterMoveTo = run.Name;
        run.Files.CancelAfterMove = run.Cancel;

        // No cancellation check runs after the commit point, so a late signal changes nothing.
        Assert.Null(await run.PublishAsync(New));

        Assert.Equal(New, await File.ReadAllTextAsync(run.Out));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheTargetIsTheInputFile_ThenItIsRefusedEvenWithForce()
    {
        using var run = SingleRun.Create();

        var refusal = await run.PublishAsync(New, force: true, outPath: run.Input);

        Assert.Equal($"the output '{run.Input}' and the input '{run.Input}' are the same file.", refusal);
        Assert.Equal(SingleRun.InputText, await File.ReadAllTextAsync(run.Input));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheTargetIsAHardLinkAliasOfTheInput_ThenItIsRefusedEvenWithForce()
    {
        // Path comparison cannot see this; actual filesystem identity can.
        using var run = SingleRun.Create();
        if (!PlatformLinks.TryCreateHardLink(run.Out, run.Input, out var reason))
        {
            Assert.Skip($"hard links are unavailable on this host: {reason}");
        }

        var refusal = await run.PublishAsync(New, force: true);

        Assert.Equal($"the output '{run.Out}' and the input '{run.Input}' are the same file.", refusal);
        Assert.Equal(SingleRun.InputText, await File.ReadAllTextAsync(run.Input));
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Publish_WhenTheTargetIsASymbolicLinkAliasOfTheInput_ThenItIsRefusedEvenWithForce()
    {
        using var run = SingleRun.Create();
        if (!PlatformLinks.TryCreateSymbolicLink(run.Out, run.Input, out var reason))
        {
            Assert.Skip($"symbolic links are unavailable on this host: {reason}");
        }

        var refusal = await run.PublishAsync(New, force: true);

        Assert.Equal($"the output '{run.Out}' and the input '{run.Input}' are the same file.", refusal);
        Assert.Equal(SingleRun.InputText, await File.ReadAllTextAsync(run.Input));
    }

    [Fact]
    public async Task Publish_WhenTheStageIsCreated_ThenItIsRequestedThroughTheConfidentialPath()
    {
        // A stage holds user data and may survive a crash, so it gets a durable access boundary.
        using var run = SingleRun.Create();

        Assert.Null(await run.PublishAsync(New));

        Assert.Contains(
            run.Files.Operations,
            operation => operation.StartsWith($"Confidential:{run.Name}.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            run.Files.Operations,
            operation => operation.StartsWith($"CreateNew:{run.Name}.fcabedrock-stage-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_WhenTheRunSucceeds_ThenNoRecordStageBackupOrMarkerSurvives()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);

        Assert.Null(await run.PublishAsync(New, force: true));

        Assert.Equal(["input.csv", run.Name], run.Names());
    }

    [Fact]
    public async Task Publish_WhenAnythingIsReported_ThenNoPrivateNameOrTokenAppears()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var refusals = new List<string?> { await run.PublishAsync(New) };

        run.Restart();
        run.Files.FailKind = "Move";
        run.Files.FailMoveTo = run.Name;
        refusals.Add(await run.PublishAsync(New, force: true));

        run.Restart();
        await File.WriteAllTextAsync(
            Path.Combine(run.Directory, $"{run.Name}.fcabedrock-transaction-notatoken.toml"), "lookalike");
        refusals.Add(await run.PublishAsync(New, force: true));

        foreach (var message in refusals)
        {
            Assert.NotNull(message);
            foreach (var fragment in new[]
                { "fcabedrock-stage", "fcabedrock-backup", "fcabedrock-transaction", "fcabedrock-intent", "-e-", "-ep-", "-sc-" })
            {
                Assert.DoesNotContain(fragment, message, StringComparison.Ordinal);
            }

            // The operand legitimately appears, and its temp directory carries a 32-hex name of
            // its own, so the check is made on what is left after the caller's own directory.
            Assert.DoesNotMatch("[0-9a-f]{32}", message.Replace(run.Directory, string.Empty, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Publish_WhenAPriorSingleRunLeftOnlyItsRecord_ThenItIsRecoveredAndTheNewRunSucceeds()
    {
        // Crashed before staging: nothing owned exists but the record itself.
        using var run = SingleRun.Create();
        var residue = Residue.Create(run.Directory, run.Name, SingleRun.ResidueToken);
        residue.WriteRecord([("stage", run.Name)]);

        Assert.Null(await run.PublishAsync(New));

        Assert.Empty(run.Residue());
        Assert.Equal(New, await File.ReadAllTextAsync(run.Out));
    }

    [Fact]
    public async Task Publish_WhenAPriorSingleRunLeftAProvableStage_ThenTheStageIsRemovedAndTheTargetIsUntouched()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        var residue = Residue.Create(run.Directory, run.Name, SingleRun.ResidueToken);
        residue.WriteRecord([("stage", run.Name)]);
        residue.WritePrivate("stage", run.Name, "half-written");

        // Accompanied by its valid stage claim, so the stage IS provable and may be removed.
        residue.WriteStageClaim("s", run.Name);

        Assert.Equal(
            $"the output '{run.Out}' already exists; use --force to replace it.", await run.PublishAsync(New));

        Assert.Empty(run.Residue());
        Assert.Equal(Old, await File.ReadAllTextAsync(run.Out));
    }

    [Fact]
    public async Task Publish_WhenAPriorSingleRunWasBackedUpButNotCommitted_ThenTheOldTargetIsRestored()
    {
        using var run = SingleRun.Create();
        var residue = Residue.Create(run.Directory, run.Name, SingleRun.ResidueToken);
        residue.WriteRecord([("backup", run.Name), ("stage", run.Name)]);
        residue.WritePrivate("backup", run.Name, Old);
        residue.WritePrivate("stage", run.Name, "half-written");
        residue.WriteEvidence(
            "s", run.Name, residue.Identity("backup", run.Name), residue.Identity("stage", run.Name));
        residue.WriteMarker("staged");

        Assert.Equal(
            $"the output '{run.Out}' already exists; use --force to replace it.", await run.PublishAsync(New));

        Assert.Equal(Old, await File.ReadAllTextAsync(run.Out));
        Assert.Empty(run.Residue());
    }

    [Theory]
    [InlineData("fcabedrock-transaction-aaaaaaaabbbbbbbbccccccccdddddddd.toml", "notes about my run\n")]
    [InlineData("fcabedrock-transaction-nothex.toml", "version = 1\n")]
    [InlineData("fcabedrock-stage-aaaaaaaabbbbbbbbccccccccdddddddd", "someone else's file")]
    [InlineData("fcabedrock-backup-not-a-token", "someone else's file")]
    public async Task Publish_WhenTheLocationHoldsAnUnknownLookalike_ThenItIsRefusedAndLeftByteIdentical(
        string suffix, string content)
    {
        // The name matches the reserved grammar and still confers nothing: it is neither believed
        // nor removed, and the run refuses with the location exactly as it was found.
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(Path.Combine(run.Directory, $"{run.Name}.{suffix}"), content);
        var before = run.Snapshot();

        Assert.Equal(
            $"the output base '{run.Out}' has unrecognized fcabedrock transaction residue; remove it and retry.",
            await run.PublishAsync(New, force: true));

        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());
    }

    [Theory]
    [InlineData("")]
    [InlineData("half a control")]
    public async Task Publish_WhenAControlNameHoldsSomethingElse_ThenItIsRefusedAndPreserved(string content)
    {
        // An empty and a partial object at a marker name. Neither is this transaction's control, so
        // neither selects a recovery direction nor is removed as its residue.
        using var run = SingleRun.Create();
        var residue = Residue.Create(run.Directory, run.Name, SingleRun.ResidueToken);
        residue.WriteRecord([("stage", run.Name)]);
        residue.WritePrivate("stage", run.Name, "half-written");
        await File.WriteAllTextAsync(residue.MarkerPath("staged"), content);
        var before = run.Snapshot();

        Assert.Equal(
            $"the output base '{run.Out}' has unrecognized fcabedrock transaction residue; remove it and retry.",
            await run.PublishAsync(New, force: true));

        run.AssertNoMutation();
        Assert.Equal(before, run.Snapshot());
    }

    [Fact]
    public async Task Publish_WhenTheProcessCrashesAtEachTransition_ThenARetryConverges()
    {
        // Every material transition of a forced replacement, stopped immediately after it. A retry
        // ends at exactly one of {old preserved, new published}, or refuses deterministically.
        var transitions = await TransitionsAsync();
        Assert.NotEmpty(transitions);

        for (var index = 0; index < transitions.Count; index++)
        {
            using var run = SingleRun.Create();
            await File.WriteAllTextAsync(run.Out, Old);
            run.Files.CrashAfter = transitions[index];
            await run.CrashAsync(New);

            Assert.True(run.Files.Crashed, $"the crash after '{transitions[index]}' never fired");

            run.Restart();
            var refusal = await run.PublishAsync(New, force: true);
            var context = $"crash after '{transitions[index]}' ({index + 1}/{transitions.Count})";
            var content = File.Exists(run.Out) ? await File.ReadAllTextAsync(run.Out) : null;

            if (refusal is null)
            {
                Assert.Equal(New, content);
                Assert.Empty(run.Residue());
                continue;
            }

            // Nothing of the previous file was destroyed: it is in place or held in a backup.
            Assert.True(
                string.Equals(content, Old, StringComparison.Ordinal) || run.BackupHolds(Old),
                $"{context}: the old file is neither in place nor held in a backup");

            // And the refusal is deterministic over the same bytes.
            var state = run.Snapshot();
            run.Restart();
            Assert.Equal(refusal, await run.PublishAsync(New, force: true));
            Assert.Equal(state, run.Snapshot());
        }
    }

    // The mutating transitions of one clean forced run, folded so no token needs guessing.
    private static async Task<List<string>> TransitionsAsync()
    {
        using var run = SingleRun.Create();
        await File.WriteAllTextAsync(run.Out, Old);
        Assert.Null(await run.PublishAsync(New, force: true));

        return
        [
            .. run.Files.Operations
                .Where(operation =>
                    operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                    || operation.StartsWith("Confidential:", StringComparison.Ordinal)
                    || operation.StartsWith("Move:", StringComparison.Ordinal)
                    || operation.StartsWith("Delete:", StringComparison.Ordinal)
                    || operation.StartsWith("StreamWrite:", StringComparison.Ordinal)
                    || operation.StartsWith("StreamClose:", StringComparison.Ordinal))
                .Select(RecordingPublicationFileSystem.Fold),
        ];
    }

    [Fact]
    public async Task Publish_WhenTheHostCannotIdentifyTheStage_ThenTheRunFailsClosedAndTheRecordAndStageArePreserved()
    {
        // The settled unusable-identity limitation applies here unchanged: PreflightSingle
        // publishes through the untouched Begin and StageAsync. This is a fail-closed PRESERVATION
        // contract — not a cleanup, not a downgrade route, and not a removal predicate.
        using var run = SingleRun.Create();
        Assert.Null(await run.PublishAsync(Old));
        var target = await File.ReadAllBytesAsync(run.Out);

        run.Restart();
        run.Files.SuppressStageIdentity = true;

        // Snapshot at the first removal rollback attempts: the record, the durable rollback
        // marker, and the stage all exist, and Finish has not yet touched anything.
        Dictionary<string, byte[]>? beforeRollback = null;
        run.Files.MutateBefore = $"Delete:{run.Name}.fcabedrock-stage-T";
        run.Files.Mutate = () => beforeRollback = run.Snapshot();

        // 1. The writer never proceeds past the settled failure point: the stage is acquired, and
        //    then nothing is written to it, no target is renamed aside, and nothing is committed.
        Assert.Equal($"cannot publish the output '{run.Out}'.", await run.PublishAsync(New, force: true));
        Assert.Contains(
            run.Files.Operations,
            operation => operation.StartsWith($"Confidential:{run.Name}.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            run.Files.Operations,
            operation => operation.StartsWith($"StreamWrite:{run.Name}.fcabedrock-stage-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            run.Files.Operations,
            operation => operation.StartsWith("Move:", StringComparison.Ordinal)
                && operation.Contains(".fcabedrock-backup-", StringComparison.Ordinal));
        Assert.DoesNotContain(
            run.Files.Operations,
            operation => operation.EndsWith($"->{run.Name}", StringComparison.Ordinal));

        // 2. The previous public target is byte-identical. Force authorized replacing it and it was
        //    still not replaced: the failure lands before the first backup rename.
        Assert.Equal(target, await File.ReadAllBytesAsync(run.Out));

        // 3. The record, the rollback marker, and the exact unidentifiable stage are all still
        //    there, byte-for-byte — and the stage is the same OBJECT, not merely the same name.
        Assert.NotNull(beforeRollback);
        var stage = Directory.GetFiles(run.Directory, $"{run.Name}.fcabedrock-stage-*").Single();
        var identity = FileIdentity.CreateDefault().KeyFor(stage);
        var residue = run.Residue();
        Assert.Contains(residue, name => name.Contains(".fcabedrock-transaction-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-rollback-", StringComparison.Ordinal));
        Assert.Contains(residue, name => name.Contains(".fcabedrock-stage-", StringComparison.Ordinal));
        Assert.Equal(beforeRollback, run.Snapshot());

        // No stage claim and no evidence exist for it, which is exactly why it is unprovable.
        Assert.DoesNotContain(residue, name => name.Contains(".fcabedrock-sc-", StringComparison.Ordinal));
        Assert.DoesNotContain(residue, name => name.Contains(".fcabedrock-e-", StringComparison.Ordinal));

        // 4/5. Two compatible retries on fresh harnesses without identity suppression. Each
        //      classifies the valid same-family record, enters recovery, and reaches the removal
        //      boundary for the unprovable stage, where the proof refuses. The literal message is
        //      ruled text, never captured: UnknownResidue would mean classification had regressed.
        var map = run.Snapshot();
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            run.Restart();

            var refused = Assert.IsType<PublicationRefused>(run.Preflight(force: true));
            Assert.Equal(
                $"cannot clean up an incomplete fcabedrock run for the output base '{run.Out}'.",
                refused.Message);
            Assert.DoesNotContain(".fcabedrock-", refused.Message, StringComparison.Ordinal);

            // Nothing created or renamed; exactly one removal ATTEMPT, at the same preserved stage.
            // That attempt beside an unchanged byte map and identity key proves the acting-boundary
            // proof refused it — a stronger statement than never having tried.
            run.AssertOnlyAttemptedDelete(Path.GetFileName(stage));
            Assert.Equal(map, run.Snapshot());
            Assert.Equal(identity, FileIdentity.CreateDefault().KeyFor(stage));
        }
    }
}

/// <summary>
/// One temporary single-file publication: an input, an output path, and the injected filesystem
/// and cancellation source the transaction is driven with. <see cref="PublishAsync"/> drives the
/// same sequence a caller does — preflight, begin, stage, seal, commit, roll back on any failure.
/// </summary>
internal sealed class SingleRun : IDisposable
{
    /// <summary>The bytes of the file every run reads, so a collision case can prove it survived.</summary>
    public const string InputText = "colour\nred\ngreen\n";

    /// <summary>A fixed token for hand-authored residue; a real run's own token is unpredictable.</summary>
    public const string ResidueToken = "aaaaaaaabbbbbbbbccccccccdddddddd";

    private readonly TempDirectory _temp;
    private CancellationTokenSource _signals = new();

    private SingleRun(TempDirectory temp, string input, string outPath)
    {
        _temp = temp;
        Input = input;
        Out = outPath;
    }

    /// <summary>The injected publication filesystem for the current attempt.</summary>
    public RecordingPublicationFileSystem Files { get; private set; } = new();

    /// <summary>The input the run reads — what every identity collision is measured against.</summary>
    public string Input { get; }

    /// <summary>The verbatim <c>--out</c> operand.</summary>
    public string Out { get; }

    /// <summary>The target's file name, which for this family is also the record's base name.</summary>
    public string Name => Path.GetFileName(Out);

    /// <summary>The directory the target and any residue live in.</summary>
    public string Directory => _temp.Path;

    /// <summary>The exact host token this attempt supplies to the transaction.</summary>
    public CancellationToken Token => _signals.Token;

    public static SingleRun Create(string name = "out")
    {
        var temp = TempDirectory.Create();
        return new SingleRun(temp, temp.Write("input.csv", InputText), temp.Resolve(name));
    }

    public void Cancel() => _signals.Cancel();

    public void Restart()
    {
        Files = new RecordingPublicationFileSystem();
        _signals.Dispose();
        _signals = new CancellationTokenSource();
    }

    public PublicationPreparation Preflight(bool force = false, string? outPath = null) =>
        PublicationTransaction.PreflightSingle(
            Files, FileIdentity.CreateDefault, outPath ?? Out,
            [new PublicationInput(Input, Input)], force, _signals.Token);

    /// <summary>
    /// Publishes <paramref name="content"/>, answering the sanitized failure message or
    /// <see langword="null"/> on success. Cancellation is rethrown, as it is for a real caller.
    /// </summary>
    public async Task<string?> PublishAsync(string content, bool force = false, string? outPath = null)
    {
        // Preflight runs exactly once: a second call would re-run residue recovery and could
        // answer a different question than the one this attempt is about to act on.
        var preparation = Preflight(force, outPath);
        if (preparation is PublicationRefused refused)
        {
            return refused.Message;
        }

        // Disposed on every exit, exactly as the real command handlers do it. A transaction whose
        // references outlive it would keep an already-requested Windows deletion pending, and — in
        // the crash cases — would let a retry prove ownership from the DEAD invocation's own live
        // handles instead of from the cold state on disk.
        using var transaction = ((PublicationReady)preparation).Transaction;
        try
        {
            if (transaction.Begin() is { } begun)
            {
                transaction.Rollback();
                return begun.Message;
            }

            if (await transaction.StageAsync(PublicationTargetKind.Single, Writer(content)) is { } staged)
            {
                transaction.Rollback();
                return staged.Message;
            }

            if (transaction.Seal(_signals.Token) is { } sealing)
            {
                transaction.Rollback();
                return sealing.Message;
            }

            _signals.Token.ThrowIfCancellationRequested();

            if (transaction.Commit(_signals.Token) is { } committing)
            {
                transaction.Rollback();
                return committing.Message;
            }
        }
        catch
        {
            if (!transaction.Committed)
            {
                transaction.Rollback();
            }

            throw;
        }

        return null;
    }

    /// <summary>
    /// Publishes under an armed <see cref="RecordingPublicationFileSystem.CrashAfter"/>: every later
    /// operation fails, so the attempt ends as a disappearing process would end it.
    /// </summary>
    public async Task CrashAsync(string content)
    {
        try
        {
            await PublishAsync(content, force: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Every file name in the directory, ordinally sorted.</summary>
    public IReadOnlyList<string> Names() =>
        [.. System.IO.Directory.GetFiles(Directory).Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.Ordinal)];

    /// <summary>Every file that claims the private transaction namespace.</summary>
    public IReadOnlyList<string> Residue() =>
        [.. Names().Where(name => name.Contains(".fcabedrock-", StringComparison.Ordinal))];

    /// <summary>Every file name paired with its exact bytes — the before/after preservation oracle.</summary>
    public Dictionary<string, byte[]> Snapshot() =>
        System.IO.Directory.GetFiles(Directory)
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);

    /// <summary>True when some transaction-owned backup currently holds <paramref name="content"/>.</summary>
    public bool BackupHolds(string content) =>
        System.IO.Directory.GetFiles(Directory).Any(path =>
            Path.GetFileName(path).Contains(".fcabedrock-backup-", StringComparison.Ordinal)
            && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal));

    /// <summary>The recorded operations of one kind, with each token folded to <c>T</c>.</summary>
    public IReadOnlyList<string> Folded(string kind) =>
        [.. Files.Operations.Where(operation => operation.StartsWith(kind, StringComparison.Ordinal))
            .Select(RecordingPublicationFileSystem.Fold)];

    /// <summary>Asserts that not one mutating filesystem operation was even attempted.</summary>
    public void AssertNoMutation() => Assert.Empty(Attempts());

    /// <summary>
    /// Asserts the acting-boundary attempt and only it: nothing created or renamed, and exactly
    /// one removal aimed at <paramref name="expected"/>. With an unchanged byte map and identity
    /// key that proves the removal's own proof refused it.
    /// </summary>
    public void AssertOnlyAttemptedDelete(string expected) =>
        Assert.Equal([$"Delete:{expected}"], Attempts());

    // Every operation that could change the location, in order — attempted, not necessarily done.
    private IReadOnlyList<string> Attempts() =>
        [.. Files.Operations.Where(operation =>
            operation.StartsWith("CreateNew:", StringComparison.Ordinal)
            || operation.StartsWith("Confidential:", StringComparison.Ordinal)
            || operation.StartsWith("Move:", StringComparison.Ordinal)
            || operation.StartsWith("Delete:", StringComparison.Ordinal))];

    public void Dispose()
    {
        _signals.Dispose();
        _temp.Dispose();
    }

    // The output side is tagged at its origin, exactly as a real writer does, so a stage write
    // failure is reported against the output rather than the source.
    private static Func<Stream, Task> Writer(string content) => async stream =>
    {
        try
        {
            await stream.WriteAsync(new UTF8Encoding(false).GetBytes(content));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PublicationStreamException(exception);
        }
    };
}
