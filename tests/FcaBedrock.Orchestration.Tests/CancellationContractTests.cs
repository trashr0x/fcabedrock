using System.Runtime.CompilerServices;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;
using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The cancellation contract (D-141 part 9), boundary by boundary, by terminal results: what a call
/// throws or returns, the attempt record and the files, the same with and without an operation
/// progress. Where a progress observes, the pass ends and the working steps are asserted too, and the
/// held cases show that a requested cancellation stays pending, with the last pass and step still shown,
/// until the call returns through its cleanup. Tests assert terminal results and files, never a token's
/// state, and hold an operation on a signal, never on time.
/// </summary>
public sealed class CancellationContractTests
{
    private const string Convert = PublicationRun.Convert;
    private const string SpecFile = PublicationRun.SpecFile;

    // ---- preparation and statistics ---------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_WhenTheRootSpecOpenThrowsTheHostCancellation_ThenItPropagatesAndNoPassStarts(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed);
        run.Input.FailOpen = path =>
        {
            if (path != run.SpecPath)
            {
                return null;
            }

            run.Signals.Cancel();
            return new OperationCanceledException(run.Signals.Token);
        };

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(run.PrepareOutcomeAsync);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Empty(run.Input.DataOpens);
        if (observed)
        {
            Assert.Empty(run.Recorder!.Events);
        }
    }

    [Theory]
    [InlineData("schema", false)]
    [InlineData("schema", true)]
    [InlineData("calibration", false)]
    [InlineData("calibration", true)]
    [InlineData("statistics", false)]
    [InlineData("statistics", true)]
    public async Task Read_WhenTheCallsOwnCancellationArrivesDuringAPass_ThenThePassEndsCancelledAndNothingIsReturned(
        string pass, bool observed)
    {
        var spec = pass == "calibration" ? ProgressSpecs.WideCalibrated : ProgressSpecs.WideDeclared;
        using var run = ProgressRun.Create(spec, ProgressSpecs.WideData(40_000), observed);
        var open = pass == "schema" ? 0 : 1;
        PreparedFileRun? prepared = null;
        Task<Result<Diagnosed<ContextStatistics>, HostFailure>>? counting = null;
        if (pass == "statistics")
        {
            (prepared, var diagnostics) = await run.PrepareAsync();
            CancelAtRead(run, open, call: 1);
            counting = ContextStatistics.CountAsync(prepared, diagnostics, run.Signals.Token);
        }
        else
        {
            CancelAtRead(run, open, call: pass == "schema" ? 0 : 1);
        }

        var thrown = counting is null
            ? await Assert.ThrowsAnyAsync<OperationCanceledException>(run.PrepareOutcomeAsync)
            : await Assert.ThrowsAnyAsync<OperationCanceledException>(() => counting!);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        if (observed)
        {
            Assert.Equal(PassEnd.Cancelled, run.Recorder!.EndOf(open + 1));
            Assert.Equal(open + 1, run.Recorder.Passes().Count);
            run.Progress!.Complete();
            run.Recorder.AssertGrammar(observedOpens: run.Input.DataOpens.Count);
        }
    }

    [Theory]
    [InlineData("schema", false)]
    [InlineData("schema", true)]
    [InlineData("calibration", false)]
    [InlineData("calibration", true)]
    [InlineData("statistics", false)]
    [InlineData("statistics", true)]
    public async Task Read_WhenTheDataThrowsTheCallsOwnTokenBeforeAnyCancellationRequest_ThenThePassEndsCancelledAndThatExceptionPropagates(
        string pass, bool observed)
    {
        // The pass end looks at the first exception's token alone (D-141 part 3): this one carries the
        // call's own token, which nothing cancels, so the pass ends Cancelled. Whether the operation was
        // cancelled is not the progress's to say: the same exception reaches the caller either way.
        var spec = pass == "calibration" ? ProgressSpecs.WideCalibrated : ProgressSpecs.WideDeclared;
        using var run = ProgressRun.Create(spec, ProgressSpecs.WideData(40_000), observed);
        var own = new OperationCanceledException("the data source stopped.", run.Signals.Token);
        var open = pass == "schema" ? 0 : 1;
        Task<Result<Diagnosed<ContextStatistics>, HostFailure>>? counting = null;
        if (pass == "statistics")
        {
            var (prepared, diagnostics) = await run.PrepareAsync();
            ThrowAtRead(run, open, call: 1, own);
            counting = ContextStatistics.CountAsync(prepared, diagnostics, run.Signals.Token);
        }
        else
        {
            ThrowAtRead(run, open, call: pass == "schema" ? 0 : 1, own);
        }

        var thrown = counting is null
            ? await Assert.ThrowsAsync<OperationCanceledException>(run.PrepareOutcomeAsync)
            : await Assert.ThrowsAsync<OperationCanceledException>(() => counting!);

        Assert.Same(own, thrown);
        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(open + 1, run.Input.DataOpens.Count);
        if (observed)
        {
            Assert.Equal(PassEnd.Cancelled, run.Recorder!.EndOf(open + 1));
            Assert.Equal(open + 1, run.Recorder.Passes().Count);
            run.Progress!.Complete();
            run.Recorder.AssertGrammar(observedOpens: run.Input.DataOpens.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_WhenCancelledAsTheLastCalibrationReadReturnsZero_ThenThePassEndsAtEndOfInputAndTheCallStillThrows(bool observed)
    {
        // End of input is not completion: the calibration pass consumed its bytes to the end, and the
        // preparation is still cancelled at a later observation point.
        using var run = ProgressRun.Create(ProgressSpecs.WideCalibrated, ProgressSpecs.WideData(500), observed);
        run.Input.AfterRead = (stream, _, read, requested) =>
        {
            if (stream.Index == 1 && read == 0 && requested > 0)
            {
                run.Signals.Cancel();
            }
        };

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(run.PrepareOutcomeAsync);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        if (observed)
        {
            Assert.Equal(PassEnd.EndOfInput, run.Recorder!.EndOf(2));
            run.Progress!.Complete();
            run.Recorder.AssertGrammar(observedOpens: run.Input.DataOpens.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_WhenCancellationIsObservedAtAPhaseBoundary_ThenTheLastPassKeepsItsEndAndNoPassEndsCancelled(bool observed)
    {
        // The cancellation arrives as the schema read's stream closes, after its last check: the schema
        // call returns, its scope ends the pass closed before end of input, and preparation observes the
        // token at its next phase boundary, outside every pass.
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, ProgressSpecs.WideData(120_000), observed);
        run.Input.AfterDispose = stream =>
        {
            if (stream.Index == 0)
            {
                run.Signals.Cancel();
            }
        };

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(run.PrepareOutcomeAsync);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        var schema = Assert.Single(run.Input.DataOpens);
        Assert.True(schema.Bytes < schema.FileLength, "the schema read reached the end of the file");
        if (observed)
        {
            Assert.Equal([(1, PassKind.Schema)], run.Recorder!.Passes());
            Assert.Equal(PassEnd.ClosedBeforeEndOfInput, run.Recorder.EndOf(1));
            Assert.DoesNotContain(run.Recorder.Events, e => e.End == PassEnd.Cancelled);
            run.Progress!.Complete();
            run.Recorder.AssertGrammar(observedOpens: run.Input.DataOpens.Count);
        }
    }

    // ---- before the commit point ------------------------------------------------------------------

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenCancelledBeforeBegin_ThenNothingBeginsTheHandleIsSpentAndNoStagingIsReported(string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        PublicationRun.Occupy(run.Target);
        var handle = run.Preflight(replace: true);
        await run.Signals.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, handle.Attempt.Rollback);
        Assert.DoesNotContain(run.Mutations(), operation => operation.StartsWith("CreateNew:", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.PublishAsync(handle));
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        AssertReleased(run.Target);
        if (recorder is not null)
        {
            Assert.Equal([WorkingStep.PreparingOutput], recorder.NamedSteps());
            Complete(run, recorder);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenCancelledDuringTheCxtNamesPass_ThenTheRollbackLeavesTheEarlierPublicationAsItWas(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed);
        await PublishEarlierAsync(run);
        var before = Snapshot(run);
        var (prepared, diagnostics) = await run.PrepareAsync();
        var conversion = ProgressRun.Plan(prepared, diagnostics);
        var handle = run.Preflight(prepared, run.Output(dat: false, replace: true));
        CancelAtRead(run, open: 1, call: 0);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(conversion, handle, diagnostics));

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.Completed, handle.Attempt.Rollback);
        Assert.Equal(before, Snapshot(run));
        Assert.Empty(run.Residue());
        if (observed)
        {
            Assert.Equal(PassEnd.Cancelled, run.Recorder!.EndOf(2));
            Assert.Single(run.Recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
            run.CompleteAndAssertGrammar(prepared);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenCancelledAtTheSeal_ThenTheRollbackCompletesAndTheLocationIsAsBefore(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed);
        var (prepared, diagnostics) = await run.PrepareAsync();
        var conversion = ProgressRun.Plan(prepared, diagnostics);
        var handle = run.Preflight(prepared, run.Output(dat: false));
        var before = run.Names();
        run.Files.MutateBefore = "CreateNew:out.fcabedrock-staged-T";
        run.Files.Mutate = run.Signals.Cancel;

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(conversion, handle, diagnostics));

        Assert.Equal(1, run.Files.MutationsFired);
        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.Completed, handle.Attempt.Rollback);
        Assert.Equal(before, run.Names());
        if (observed)
        {
            Assert.Single(run.Recorder!.NamedSteps(), step => step == WorkingStep.Cleaning);
            Assert.DoesNotContain(WorkingStep.Committing, run.Recorder.NamedSteps());
            run.CompleteAndAssertGrammar(prepared);
        }
    }

    // ---- past the commit point --------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenCancellationArrivesPastTheCommitPoint_ThenItIsNotObservedAndThePublicationStands(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed);
        var (prepared, diagnostics) = await run.PrepareAsync();
        var conversion = ProgressRun.Plan(prepared, diagnostics);
        var handle = run.Preflight(prepared, run.Output(dat: false));
        run.Files.MutateBefore = "CreateNew:out.fcabedrock-committed-T";
        run.Files.Mutate = run.Signals.Cancel;

        var completed = OutcomeAssert.Completed(await run.PublishAsync(conversion, handle, diagnostics));

        Assert.Equal(1, run.Files.MutationsFired);
        Assert.Equal(PublicationAttemptState.Committed, handle.Attempt.State);
        Assert.Equal(CleanupOutcome.Completed, completed.Value.Cleanup);
        Assert.All(completed.Value.Files, file => Assert.True(File.Exists(file.FullPath)));
        Assert.Empty(run.Residue());
        if (observed)
        {
            Assert.Single(run.Recorder!.NamedSteps(), step => step == WorkingStep.Cleaning);
            run.CompleteAndAssertGrammar(prepared);
        }
    }

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenCreatingTheCommittedMarkerRaisesAContractFault_ThenTheFaultEscapesWithCleanupNotStartedAndNoCleaning(
        string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-committed-";
        run.Files.FailWith = static () => new ObjectDisposedException("publication");
        var handle = run.Preflight();

        var fault = await Assert.ThrowsAsync<PublicationFaultException>(() => run.PublishAsync(handle));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        AssertCommitted(handle, CleanupOutcome.NotStarted);
        if (recorder is not null)
        {
            Assert.DoesNotContain(WorkingStep.Cleaning, recorder.NamedSteps());
            Complete(run, recorder);
        }
    }

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenTheCleanupPassRaisesAContractFault_ThenTheFaultEscapesWithCleanupInterruptedAndCleaningOnce(
        string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        run.Files.FailDeletePrefix = run.BaseName + ".fcabedrock-committed-";
        run.Files.FailWith = static () => new ObjectDisposedException("publication");
        var handle = run.Preflight();

        var fault = await Assert.ThrowsAsync<PublicationFaultException>(() => run.PublishAsync(handle));

        Assert.IsType<ObjectDisposedException>(fault.InnerException);
        AssertCommitted(handle, CleanupOutcome.Interrupted);
        if (recorder is not null)
        {
            Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
            Complete(run, recorder);
        }
    }

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenTheCleanupFallsShortForAnOrdinaryReason_ThenTheRunCompletesWithTheWarningAndCleaningOnce(
        string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        run.Files.FailDeletePrefix = run.BaseName + ".fcabedrock-committed-";
        var handle = run.Preflight();

        var published = OutcomeAssert.Committed(await run.PublishAsync(handle));

        Assert.Equal(CleanupOutcome.Incomplete, published.Cleanup);
        var blocker = Assert.IsType<CleanupFact>(published.CleanupBlocker);
        Assert.Equal(PublicationMessages.CleanupIncomplete(run.Base, blocker), published.CleanupWarning);
        AssertCommitted(handle, CleanupOutcome.Incomplete);
        if (recorder is not null)
        {
            Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
            Complete(run, recorder);
        }
    }

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenTheCommittedMarkerIsRefusedForAnOrdinaryReason_ThenTheCleanupStillRunsAndCleaningIsReportedOnce(
        string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-committed-";
        var handle = run.Preflight();

        var published = OutcomeAssert.Committed(await run.PublishAsync(handle));

        Assert.Equal(CleanupOutcome.Completed, published.Cleanup);
        AssertCommitted(handle, CleanupOutcome.Completed);
        Assert.Empty(run.Residue());
        if (recorder is not null)
        {
            Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
            Complete(run, recorder);
        }
    }

    // ---- rollbacks and refusals -------------------------------------------------------------------

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenTheRollingBackMarkerCannotBeRecorded_ThenTheRollbackIsNotStartedAndNoCleaningIsReported(
        string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        run.Files.CancelAfterMoveToPrefix = run.BaseName + ".fcabedrock-transaction-";
        run.Files.CancelAfterMove = run.Signals.Cancel;
        run.Files.FailCreateNewPrefix = run.BaseName + ".fcabedrock-rollback-";
        var handle = run.Preflight();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(handle));

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.NotStarted, handle.Attempt.Rollback);
        Assert.False(File.Exists(run.Target));
        if (recorder is not null)
        {
            Assert.DoesNotContain(WorkingStep.Cleaning, recorder.NamedSteps());
            Complete(run, recorder);
        }
    }

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Publish_WhenAStageWriteFails_ThenTheStageFailsTheRollbackCompletesAndCleaningIsReportedOnce(string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        run.Files.FailStreamWritePrefix = Path.GetFileName(run.Target) + ".fcabedrock-stage-";
        var handle = run.Preflight();

        var failed = OutcomeAssert.PublishHostFailed(await run.PublishAsync(handle));

        Assert.Equal(HostErrorKind.StageFailed, failed.Kind);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.Completed, handle.Attempt.Rollback);
        Assert.Empty(run.Residue());
        if (recorder is not null)
        {
            Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
            Complete(run, recorder);
        }
    }

    [Theory]
    [InlineData(Convert, false)]
    [InlineData(Convert, true)]
    [InlineData(SpecFile, false)]
    [InlineData(SpecFile, true)]
    public async Task Preflight_WhenAnExistingTargetIsRefused_ThenPreparingOutputIsReportedThenClearedAndNoReferenceIsHeld(
        string family, bool observed)
    {
        var (run, recorder) = await CreateAsync(family, observed);
        using var _ = run;
        PublicationRun.Occupy(run.Target);

        var refused = run.Refused();

        Assert.Equal(HostErrorKind.TargetOccupied, refused.Kind);
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        AssertReleased(run.Target);
        if (recorder is not null)
        {
            Assert.Equal(
                [WorkingStep.PreparingOutput, (WorkingStep)0],
                recorder.Events.Where(e => e.Name == ProgressEvent.StepChanged).Select(e => e.Step));
            Complete(run, recorder);
        }
    }

    // ---- requested cancellation stays pending until the call returns ------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenCancellationIsRequestedWhileASealIsHeld_ThenItStaysPendingWithTheStepShownUntilTheCallReturns(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed);
        using var hold = new Hold();
        var (prepared, diagnostics) = await run.PrepareAsync();
        var conversion = ProgressRun.Plan(prepared, diagnostics);
        var handle = run.Preflight(prepared, run.Output(dat: false));
        run.Files.MutateBefore = "CreateNew:out.fcabedrock-staged-T";
        run.Files.Mutate = hold.Wait;

        var publishing = Task.Run(() => run.PublishAsync(conversion, handle, diagnostics), TestContext.Current.CancellationToken);
        hold.WaitForArrival();
        await run.Signals.CancelAsync();

        Assert.False(publishing.IsCompleted);
        if (observed)
        {
            var pending = run.Progress!.Current;
            Assert.Equal(PassEnd.EndOfInput, pending.PassEnd);
            Assert.Equal(WorkingStep.Sealing, pending.Step);
            Assert.False(pending.IsCompleted);
        }

        hold.Release();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publishing);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.Completed, handle.Attempt.Rollback);
        if (observed)
        {
            run.Progress!.Complete();
            Assert.Same(run.Progress.Current, run.Progress.Current);
            Assert.True(run.Progress.Current.IsCompleted);
            run.Recorder!.AssertGrammar(observedOpens: prepared.Input.ObservedOpens);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Count_WhenCancellationIsRequestedWhileAReadIsBlocked_ThenTheReadIsNotInterruptedAndThePassEndsCancelledAfterIt(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, ProgressSpecs.WideData(40_000), observed);
        using var hold = new Hold();
        var (prepared, diagnostics) = await run.PrepareAsync();
        run.Input.BeforeRead = (stream, call) =>
        {
            if (stream.Index == 1 && call == 1)
            {
                hold.Wait();
            }
        };

        var counting = Task.Run(
            () => ContextStatistics.CountAsync(prepared, diagnostics, run.Signals.Token), TestContext.Current.CancellationToken);
        hold.WaitForArrival();
        await run.Signals.CancelAsync();

        Assert.False(counting.IsCompleted);
        var blocked = run.Input.DataOpens[1];
        Assert.False(blocked.Disposed);
        if (observed)
        {
            var pending = run.Progress!.Current;
            Assert.Equal(2, pending.PassSequence);
            Assert.Equal(PassKind.Statistics, pending.PassKind);
            Assert.Equal((PassEnd)0, pending.PassEnd);
            Assert.Equal((WorkingStep)0, pending.Step);
            Assert.Equal(blocked.Bytes, pending.BytesConsumed);
            Assert.False(pending.IsCompleted);
        }

        hold.Release();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => counting);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.True(blocked.Reads >= 2, $"the blocked read did not return: {blocked.Reads} read(s)");
        if (observed)
        {
            Assert.Equal(PassEnd.Cancelled, run.Recorder!.EndOf(2));
            run.CompleteAndAssertGrammar(prepared);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Count_WhenAGroupingEnumerationIsHeldAfterItsInputClosed_ThenTheGroupingStepIsShownAndCancellationStaysPending(
        bool endOfInput, bool observed)
    {
        // A stand-in run over a grouping plan: its emitter opens one real tracker pass, reads it to the
        // end or only partly, disposes the wrapper, and holds before yielding more objects. The pending
        // displays read this state: the input closed, Grouping runs, and the end is either recorded at
        // closure or still pending.
        using var run = ProgressRun.Create(ProgressSpecs.WideDedupe, ProgressSpecs.DedupeData(20_000, offender: false, 0), observed);
        using var hold = new Hold();
        var (real, diagnostics) = await run.PrepareAsync();
        var standIn = ProgressSpecs.StandIn(real, (_, _, token) => CloseThenHold(real.Input, endOfInput, hold, token));

        var counting = Task.Run(
            () => ContextStatistics.CountAsync(standIn, diagnostics, run.Signals.Token), TestContext.Current.CancellationToken);
        hold.WaitForArrival();
        if (observed)
        {
            var held = run.Progress!.Current;
            Assert.Equal(2, held.PassSequence);
            Assert.Equal(endOfInput ? PassEnd.EndOfInput : (PassEnd)0, held.PassEnd);
            Assert.Equal(WorkingStep.Grouping, held.Step);
            Assert.False(held.IsCompleted);
        }

        await run.Signals.CancelAsync();
        Assert.False(counting.IsCompleted);
        if (observed)
        {
            Assert.Equal(WorkingStep.Grouping, run.Progress!.Current.Step);
        }

        hold.Release();
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => counting);

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        if (observed)
        {
            var recorder = run.Recorder!;
            var exit = recorder.Events.Last(e => e.Name == ProgressEvent.ScopeExited);
            Assert.Equal(new ProgressEvent(ProgressEvent.ScopeExited, 2, End: endOfInput ? 0 : PassEnd.Cancelled, Step: 0), exit);
            Assert.Equal(endOfInput ? PassEnd.EndOfInput : PassEnd.Cancelled, recorder.EndOf(2));
            run.Progress!.Complete();
            Assert.Same(run.Progress.Current, run.Progress.Current);
            recorder.AssertGrammar(observedOpens: real.Input.ObservedOpens);
        }
    }

    private static async IAsyncEnumerable<EmittedObject> CloseThenHold(
        InputHashTracker input, bool endOfInput, Hold hold, [EnumeratorCancellation] CancellationToken cancellation)
    {
        await using (var stream = input.OpenHashed())
        {
            var buffer = new byte[4096];
            if (endOfInput)
            {
                while (await stream.ReadAsync(buffer, cancellation) > 0)
                {
                }
            }
            else
            {
                _ = await stream.ReadAsync(buffer, cancellation);
            }
        }

        yield return ProgressSpecs.Object("k0");
        hold.Wait();
        cancellation.ThrowIfCancellationRequested();
        yield return ProgressSpecs.Object("k1");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static async Task<(PublicationRun Run, ProgressRecorder? Recorder)> CreateAsync(string family, bool observed)
    {
        OperationProgress? progress = null;
        ProgressRecorder? recorder = null;
        if (observed)
        {
            (progress, recorder) = ProgressRecorder.Create();
        }

        return (await PublicationRun.CreateAsync(family, progress: progress), recorder);
    }

    private static void Complete(PublicationRun run, ProgressRecorder recorder)
    {
        run.Progress!.Complete();
        recorder.AssertGrammar(observedOpens: run.Run.Input.ObservedOpens);
    }

    // Cancels the host token immediately before read call `call` of data open `open`.
    private static void CancelAtRead(ProgressRun run, int open, int call) =>
        run.Input.BeforeRead = (stream, read) =>
        {
            if (stream.Index == open && read == call)
            {
                run.Signals.Cancel();
            }
        };

    // Throws `exception` from read call `call` of data open `open`, before that read returns.
    private static void ThrowAtRead(ProgressRun run, int open, int call, Exception exception) =>
        run.Input.BeforeRead = (stream, read) =>
        {
            if (stream.Index == open && read == call)
            {
                throw exception;
            }
        };

    // An earlier, unobserved publication at the same base, through its own host, so the case can show
    // the location after a cancelled replacement is exactly as it was.
    private static async Task PublishEarlierAsync(ProgressRun run)
    {
        var host = new OrchestrationHost(File.OpenRead) { PublicationFiles = new RecordingPublicationFileSystem() };
        var prepared = OutcomeAssert.Completed(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = run.SpecPath, DataPath = run.DataPath }, host, CancellationToken.None));
        var conversion = ProgressRun.Plan(prepared.Value, prepared.Diagnostics);
        using var handle = OutcomeAssert.Completed(
            ConvertPublication.Preflight(run.Output(dat: false), prepared.Value, host, CancellationToken.None));
        OutcomeAssert.Completed(await ConvertPublication.PublishAsync(
            conversion, handle, run.Facts, prepared.Diagnostics, CancellationToken.None));
    }

    private static List<(string Name, string Hash)> Snapshot(ProgressRun run) =>
        [.. run.Names().Select(name => (name, System.Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(run.Directory, name))))))];

    private static void AssertCommitted(PreflightedPublication handle, CleanupOutcome cleanup)
    {
        Assert.Equal(PublicationAttemptState.Committed, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, handle.Attempt.Rollback);
        var committed = Assert.IsType<Published>(handle.Attempt.Committed);
        Assert.Equal(cleanup, committed.Cleanup);
        Assert.All(committed.Files, file => Assert.True(File.Exists(file.FullPath), $"{file.Spelling} does not stand"));
    }

    // On Windows, the classic disposition keeps a name occupied while any handle on the object is open,
    // so a free name after a seam removal shows that no reference is still held.
    private static void AssertReleased(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            PublicationRun.AssertNoReferenceHeld(path);
        }
    }
}
