using System.Security.Cryptography;
using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The attempt record on every exit, in both families (D-140): the state from the transaction's
/// flags, each provisional fact written right after the state change it describes and before the
/// next step that can fail, one build at the publish's exit, and the exception that escaped
/// propagated exactly as it was. Every case asserts the propagated exception (the same tagged
/// <see cref="PublicationFaultException"/>, or the host token's cancellation), the files on disk,
/// that no field reports a completion or restoration the routine did not report, that the record
/// is unchanged after <see cref="PreflightedPublication.Dispose"/>, and, on Windows, that no
/// reference is still held.
/// </summary>
public sealed class PublicationAttemptTests
{
    private const string Convert = PublicationRun.Convert;
    private const string SpecFile = PublicationRun.SpecFile;

    // ---- past the commit point --------------------------------------------------------------------

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCreatingTheCommittedMarkerRaisesAContractFault_ThenTheAttemptIsCommittedWithCleanupNotStarted(
        string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-committed-";
        run.Files.FailWith = static () => new ObjectDisposedException("publication");
        var handle = run.Preflight();

        var fault = await Assert.ThrowsAsync<PublicationFaultException>(() => run.PublishAsync(handle));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        AssertCommittedWithCleanup(run, handle, CleanupOutcome.NotStarted);

        // No cleanup began, so the transaction's private state is still there for a later run.
        Assert.NotEmpty(run.Residue());
        AssertUnchangedByDisposal(handle);
        AssertReleased(run.Target);
    }

    [Theory]
    [InlineData(Convert, "write")]
    [InlineData(Convert, "close")]
    [InlineData(SpecFile, "write")]
    [InlineData(SpecFile, "close")]
    public async Task PublishAsync_WhenWritingOrClosingTheCommittedMarkerRaisesAContractFault_ThenTheAttemptIsCommittedWithCleanupNotStarted(
        string family, string boundary)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var marker = run.BaseName + ".fcabedrock-committed-";
        if (boundary == "write")
        {
            run.Files.FailStreamWritePrefix = marker;
            run.Files.FailStreamWith = static () => new ObjectDisposedException("publication");
        }
        else
        {
            run.Files.FailStreamClosePrefix = marker;
            run.Files.FailStreamCloseWith = static () => new ObjectDisposedException("publication");
        }

        var handle = run.Preflight();

        var fault = await Assert.ThrowsAsync<PublicationFaultException>(() => run.PublishAsync(handle));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        AssertCommittedWithCleanup(run, handle, CleanupOutcome.NotStarted);
        AssertUnchangedByDisposal(handle);
        AssertReleased(run.Target);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenTheCommittedMarkerCannotBeCreated_ThenCleanupStillRunsAndItsOutcomeIsRecorded(string family)
    {
        // An environment failure: the marker answers false and changes nothing, and the cleanup that
        // follows still runs and reports itself.
        using var run = await PublicationRun.CreateAsync(family);
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-committed-";
        var handle = run.Preflight();

        var completed = Assert.IsType<OperationCompleted<Published>>(await run.PublishAsync(handle));

        // The injected boundary fired: the marker's create-new was attempted, and refused.
        Assert.Contains(
            $"CreateNew:{run.BaseName}.fcabedrock-committed-T",
            run.Files.Operations.Select(RecordingPublicationFileSystem.Fold));
        Assert.Equal(CleanupOutcome.Completed, completed.Value.Cleanup);
        Assert.Null(completed.Value.CleanupBlocker);
        Assert.Null(completed.Value.CleanupWarning);
        Assert.Empty(run.Residue());
        AssertCommittedWithCleanup(run, handle, CleanupOutcome.Completed);
        AssertUnchangedByDisposal(handle);
        AssertReleased(run.Target);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenAPostCommitRemovalRaisesAContractFault_ThenTheAttemptIsCommittedWithCleanupInterrupted(string family)
    {
        // The cleanup pass began and an exception ended it: what it removed is unknown, and no
        // warning is claimed.
        using var run = await PublicationRun.CreateAsync(family);
        run.Files.FailDeletePrefix = run.BaseName + ".fcabedrock-committed-";
        run.Files.FailWith = static () => new ObjectDisposedException("publication");
        var handle = run.Preflight();

        var fault = await Assert.ThrowsAsync<PublicationFaultException>(() => run.PublishAsync(handle));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        AssertCommittedWithCleanup(run, handle, CleanupOutcome.Interrupted);
        AssertUnchangedByDisposal(handle);
        AssertReleased(run.Target);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCommittedAndCleanupFallsShort_ThenTheBlockerAndTheExistingWarningAgree(string family)
    {
        // An environment failure on a cleanup removal: the pass reports itself incomplete, and the
        // warning is the existing text built from the same first unfinished step.
        using var run = await PublicationRun.CreateAsync(family);
        run.Files.FailDeletePrefix = run.BaseName + ".fcabedrock-committed-";
        var handle = run.Preflight();

        var completed = Assert.IsType<OperationCompleted<Published>>(await run.PublishAsync(handle));

        var published = completed.Value;
        Assert.Equal(CleanupOutcome.Incomplete, published.Cleanup);
        var blocker = Assert.IsType<CleanupFact>(published.CleanupBlocker);
        Assert.Equal(CleanupStep.Remove, blocker.Step);
        Assert.StartsWith(run.BaseName + ".fcabedrock-committed-", blocker.EntryName, StringComparison.Ordinal);
        Assert.Equal(PublicationMessages.CleanupIncomplete(run.Base, blocker), published.CleanupWarning);
        AssertCommittedWithCleanup(run, handle, CleanupOutcome.Incomplete);
        AssertUnchangedByDisposal(handle);
        AssertReleased(run.Target);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCommitted_ThenEachFileCarriesTheWrittenBytesHashAndTheCommittedIdentity(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var handle = run.Preflight();

        var completed = Assert.IsType<OperationCompleted<Published>>(await run.PublishAsync(handle));

        var expected = family == Convert
            ? new[] { (PublishedFileKind.Cxt, run.Base + ".cxt"), (PublishedFileKind.Manifest, run.Base + ".manifest.toml") }
            : new[] { (PublishedFileKind.SpecFile, run.Base) };
        Assert.Equal(expected, completed.Value.Files.Select(file => (file.Kind, file.Spelling)));

        foreach (var file in completed.Value.Files)
        {
            // An independent hash of what the committed file holds, and a fresh identity of it.
            var bytes = File.ReadAllBytes(file.FullPath);
            Assert.Equal("sha256:" + System.Convert.ToHexStringLower(SHA256.HashData(bytes)), file.Hash);
            Assert.Equal(FileIdentity.CreateDefault().KeyFor(file.FullPath), file.Identity);
            Assert.Equal(Path.GetFullPath(file.Spelling), file.FullPath);
        }

        Assert.Equal(CleanupOutcome.Completed, completed.Value.Cleanup);
        Assert.Empty(run.Residue());
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCommitted_ThenTheOutcomeValueIsTheAttemptsCommittedRecord(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var handle = run.Preflight();

        var completed = Assert.IsType<OperationCompleted<Published>>(await run.PublishAsync(handle));

        Assert.Same(completed.Value, handle.Attempt.Committed);
    }

    // ---- before the commit point ------------------------------------------------------------------

    [Theory]
    [InlineData(Convert, "create")]
    [InlineData(Convert, "write")]
    [InlineData(Convert, "close")]
    [InlineData(SpecFile, "create")]
    [InlineData(SpecFile, "write")]
    [InlineData(SpecFile, "close")]
    public async Task PublishAsync_WhenCancelledAndTheRollingBackMarkerRaisesAContractFault_ThenTheHostTokenPropagatesAndTheRollbackIsNotStarted(
        string family, string boundary)
    {
        // The rollback's precondition faults, so no cleanup began; that fault does not get to
        // replace the cancellation that sent the publish there.
        using var run = await PublicationRun.CreateAsync(family);
        CancelOnceBegun(run);
        var marker = run.BaseName + ".fcabedrock-rollback-";
        switch (boundary)
        {
            case "create":
                run.Files.FailCreateNewPrefix = marker;
                run.Files.FailWith = static () => new ObjectDisposedException("publication");
                break;
            case "write":
                run.Files.FailStreamWritePrefix = marker;
                run.Files.FailStreamWith = static () => new ObjectDisposedException("publication");
                break;
            default:
                run.Files.FailStreamClosePrefix = marker;
                run.Files.FailStreamCloseWith = static () => new ObjectDisposedException("publication");
                break;
        }

        var handle = run.Preflight();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, cancelled.CancellationToken);
        AssertBegunWithRollback(handle, RollbackOutcome.NotStarted);
        Assert.False(File.Exists(run.Target));
        Assert.NotEmpty(run.Residue());
        AssertUnchangedByDisposal(handle);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenTheRollbackPhaseCannotBeRecorded_ThenTheRollbackIsNotStarted(string family)
    {
        // The existing safe refusal: an environment failure creating the rolling-back marker starts
        // no cleanup, and the location keeps its less advanced phase.
        using var run = await PublicationRun.CreateAsync(family);
        CancelOnceBegun(run);
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-rollback-";
        var handle = run.Preflight();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, cancelled.CancellationToken);
        AssertBegunWithRollback(handle, RollbackOutcome.NotStarted);
        Assert.False(File.Exists(run.Target));
        AssertUnchangedByDisposal(handle);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenCancelledAndTheRollbackFaults_ThenTheHostTokenPropagatesAndTheRollbackIsInterrupted(string family)
    {
        // The rollback pass began and a contract fault ended it: what it restored is unknown, which
        // is Interrupted, never Completed, and the cancellation still reaches the caller.
        using var run = await PublicationRun.CreateAsync(family);
        CancelOnceBegun(run);
        run.Files.FailDeletePrefix = Path.GetFileName(run.Target) + ".fcabedrock-stage-";
        run.Files.FailWith = static () => new ObjectDisposedException("publication");
        var handle = run.Preflight();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, cancelled.CancellationToken);
        AssertBegunWithRollback(handle, RollbackOutcome.Interrupted);
        Assert.False(File.Exists(run.Target));
        AssertUnchangedByDisposal(handle);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenAStageFailsAndARollbackRemovalFails_ThenTheRollbackIsIncompleteWithItsStep(string family)
    {
        // An injected I/O failure writing the stage is the returned stage failure; the rollback's
        // removal of that stage then fails for an environment reason, so the pass reports itself
        // incomplete and names the step that fell short.
        using var run = await PublicationRun.CreateAsync(family);
        var stage = Path.GetFileName(run.Target) + ".fcabedrock-stage-";
        run.Files.FailStreamWritePrefix = stage;
        run.Files.FailDeletePrefix = stage;
        var handle = run.Preflight();

        var failed = Assert.IsType<OperationHostFailure<Published>>(await run.PublishAsync(handle));

        Assert.Equal(HostErrorKind.StageFailed, failed.Error.Kind);
        AssertBegunWithRollback(handle, RollbackOutcome.Incomplete);
        var blocker = Assert.IsType<CleanupFact>(handle.Attempt.RollbackBlocker);
        Assert.Equal(CleanupStep.Remove, blocker.Step);
        Assert.StartsWith(stage, blocker.EntryName, StringComparison.Ordinal);
        Assert.False(File.Exists(run.Target));
        AssertUnchangedByDisposal(handle);
        AssertReleased(run.Directory, blocker.EntryName);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenBeginFails_ThenTheAttemptHasNotBegunAndOwesNoRollback(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-pending-";
        var handle = run.Preflight();

        var failed = Assert.IsType<OperationHostFailure<Published>>(await run.PublishAsync(handle));

        Assert.Equal(HostErrorKind.PublicationControlFailed, failed.Error.Kind);
        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, handle.Attempt.Rollback);
        Assert.Null(handle.Attempt.RollbackBlocker);
        Assert.Null(handle.Attempt.Committed);
        Assert.Empty(run.Residue());
        AssertUnchangedByDisposal(handle);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task PublishAsync_WhenBeginFaultsAfterPublishingItsRecord_ThenTheAttemptIsBegunAndItsRollbackIsRecorded(string family)
    {
        // The intent descriptor's removal faults after the record was published and the transaction
        // counted as begun: the flags, not Begin's missing answer, decide the state, and the rollback
        // that exit owed is recorded. The rollback meets the same fault and is Interrupted.
        using var run = await PublicationRun.CreateAsync(family);
        run.Files.FailDeletePrefix = run.BaseName + ".fcabedrock-intent-";
        run.Files.FailWith = static () => new ObjectDisposedException("publication");
        var handle = run.Preflight();

        var fault = await Assert.ThrowsAsync<PublicationFaultException>(() => run.PublishAsync(handle));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        AssertBegunWithRollback(handle, RollbackOutcome.Interrupted);
        Assert.False(File.Exists(run.Target));
        AssertUnchangedByDisposal(handle);
    }

    // ---- the record itself ------------------------------------------------------------------------

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Attempt_WhenTheHandleIsNeverPublished_ThenItHasNotBegunAndOwesNoRollback(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var handle = run.Preflight();

        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, handle.Attempt.Rollback);
        Assert.Null(handle.Attempt.RollbackBlocker);
        Assert.Null(handle.Attempt.Committed);
        AssertUnchangedByDisposal(handle);
    }

    [Theory]
    [InlineData(Convert, "committed")]
    [InlineData(Convert, "refused")]
    [InlineData(Convert, "faulted")]
    [InlineData(Convert, "cancelled")]
    [InlineData(SpecFile, "committed")]
    [InlineData(SpecFile, "refused")]
    [InlineData(SpecFile, "faulted")]
    [InlineData(SpecFile, "cancelled")]
    public async Task Attempt_WhenTheHandleIsDisposedAfterAnyExit_ThenTheRecordIsUnchanged(string family, string exit)
    {
        using var run = await PublicationRun.CreateAsync(family);
        switch (exit)
        {
            case "refused":
                run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-pending-";
                break;
            case "faulted":
                run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-committed-";
                run.Files.FailWith = static () => new ObjectDisposedException("publication");
                break;
            case "cancelled":
                CancelOnceBegun(run);
                break;
        }

        var handle = run.Preflight();
        try
        {
            await run.PublishAsync(handle);
        }
        catch (Exception exception) when (exception is PublicationFaultException or OperationCanceledException)
        {
            // The exits under test; the record is what is asserted.
        }

        var before = handle.Attempt;
        var expected = exit switch
        {
            "refused" => PublicationAttemptState.NotBegun,
            "cancelled" => PublicationAttemptState.BegunNotCommitted,
            _ => PublicationAttemptState.Committed,
        };
        Assert.Equal(expected, before.State);

        handle.Dispose();
        handle.Dispose();

        Assert.Same(before, handle.Attempt);
    }

    [Theory]
    [InlineData(Convert)]
    [InlineData(SpecFile)]
    public async Task Attempt_WhenFinal_ThenNoListItReachesCanBeChangedThroughACast(string family)
    {
        using var run = await PublicationRun.CreateAsync(family);
        var handle = run.Preflight();
        await run.PublishAsync(handle);

        var files = handle.Attempt.Committed!.Files;

        Assert.False(files is List<PublishedFile>);
        Assert.False(files is PublishedFile[]);
        var collection = Assert.IsAssignableFrom<ICollection<PublishedFile>>(files);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(files[0]));
        Assert.Throws<NotSupportedException>(collection.Clear);
    }

    [Theory]
    [InlineData(nameof(PublicationAttemptState))]
    [InlineData(nameof(RollbackOutcome))]
    [InlineData(nameof(CleanupOutcome))]
    public void AttemptEnums_ShouldHaveNoMemberWithTheValueZero(string name)
    {
        // So an unset field can never read as an outcome.
        var type = typeof(PublicationAttempt).Assembly.GetType("FcaBedrock.Orchestration." + name, throwOnError: true)!;

        Assert.NotEmpty(Enum.GetValues(type));
        Assert.DoesNotContain(0L, Enum.GetValues(type).Cast<object>().Select(value => System.Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ---- helpers ----------------------------------------------------------------------------------

    // Cancels the host token the moment the transaction's record is published, so the publish is
    // begun and the next cancellation check it meets throws for that exact token.
    private static void CancelOnceBegun(PublicationRun run)
    {
        run.Files.CancelAfterMoveToPrefix = run.BaseName + ".fcabedrock-transaction-";
        run.Files.CancelAfterMove = run.Signals.Cancel;
    }

    private static void AssertCommittedWithCleanup(PublicationRun run, PreflightedPublication handle, CleanupOutcome cleanup)
    {
        var attempt = handle.Attempt;
        Assert.Equal(PublicationAttemptState.Committed, attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, attempt.Rollback);
        Assert.Null(attempt.RollbackBlocker);
        var committed = Assert.IsType<Published>(attempt.Committed);
        Assert.Equal(cleanup, committed.Cleanup);
        if (cleanup != CleanupOutcome.Incomplete)
        {
            Assert.Null(committed.CleanupBlocker);
            Assert.Null(committed.CleanupWarning);
        }

        // The files stand, whatever the cleanup did.
        Assert.NotEmpty(committed.Files);
        Assert.All(committed.Files, file => Assert.True(File.Exists(file.FullPath), $"{file.Spelling} does not stand"));
        Assert.Equal(run.Target, committed.Files[0].Spelling);
    }

    private static void AssertBegunWithRollback(PreflightedPublication handle, RollbackOutcome rollback)
    {
        var attempt = handle.Attempt;
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, attempt.State);
        Assert.Equal(rollback, attempt.Rollback);
        if (rollback != RollbackOutcome.Incomplete)
        {
            Assert.Null(attempt.RollbackBlocker);
        }

        Assert.Null(attempt.Committed);
    }

    private static void AssertUnchangedByDisposal(PreflightedPublication handle)
    {
        var before = handle.Attempt;
        handle.Dispose();
        Assert.Same(before, handle.Attempt);
    }

    // On Windows, the classic disposition keeps a name occupied while any handle on the object is
    // open, so a free name after a seam removal shows the publish released its references.
    private static void AssertReleased(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            PublicationRun.AssertNoReferenceHeld(path);
        }
    }

    private static void AssertReleased(string directory, string entryName)
    {
        var path = Path.Combine(directory, entryName);
        if (File.Exists(path))
        {
            AssertReleased(path);
        }
    }
}
