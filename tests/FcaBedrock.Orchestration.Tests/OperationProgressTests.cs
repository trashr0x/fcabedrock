namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The recorder itself (D-141), driven through its internal producer events: what each event changes,
/// the per-pass guard that makes a stale event change nothing, the one locked exit, the latched final
/// instance, and the consistency a concurrent reader sees.
/// </summary>
public sealed class OperationProgressTests
{
    [Fact]
    public void Current_WhenNothingWasRecorded_ThenNoPassNoStepAndNotCompleted()
    {
        var current = new OperationProgress().Current;

        Assert.Equal(0, current.PassSequence);
        Assert.Equal((PassKind)0, current.PassKind);
        Assert.Null(current.PassLength);
        Assert.Equal(0, current.BytesConsumed);
        Assert.Equal(0, current.ObjectsEmitted);
        Assert.Equal((PassEnd)0, current.PassEnd);
        Assert.Equal((WorkingStep)0, current.Step);
        Assert.False(current.IsCompleted);
    }

    [Fact]
    public void PassStarted_WhenAPassBegins_ThenTheSequenceAdvancesTheCountersResetAndTheStepIsZero()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        var first = progress.PassStarted(PassKind.Schema, 100);
        progress.Consumed(first, 40);
        progress.Closed(first, WorkingStep.Calibrating);
        progress.ScopeExit(first, PassEnd.ClosedBeforeEndOfInput, WorkingStep.Staging);

        var second = progress.PassStarted(PassKind.Dat, null);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        var current = progress.Current;
        Assert.Equal(2, current.PassSequence);
        Assert.Equal(PassKind.Dat, current.PassKind);
        Assert.Null(current.PassLength);
        Assert.Equal(0, current.BytesConsumed);
        Assert.Equal(0, current.ObjectsEmitted);
        Assert.Equal((PassEnd)0, current.PassEnd);
        Assert.Equal((WorkingStep)0, current.Step);
        recorder.AssertGrammar(completed: false);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PassStarted_WhenThePreviousPassHasNoEnd_ThenItEndsFirst(bool endOfInput)
    {
        // Inside one call scope a second pass opens (the two-pass calibration): the first pass's end is
        // recorded before the second starts, from what its wrapper saw.
        var (progress, recorder) = ProgressRecorder.Create();
        var first = progress.PassStarted(PassKind.Calibration, 10);
        if (endOfInput)
        {
            progress.EndOfInput(first);
        }

        var second = progress.PassStarted(PassKind.Calibration, 10);

        var expected = endOfInput ? PassEnd.EndOfInput : PassEnd.ClosedBeforeEndOfInput;
        Assert.Equal(expected, recorder.EndOf(first));
        var events = recorder.Events;
        Assert.True(
            events.ToList().FindIndex(e => e.Name == ProgressEvent.PassEnded && e.Sequence == first)
                < events.ToList().FindIndex(e => e.Name == ProgressEvent.PassStarted && e.Sequence == second),
            recorder.Describe());
        Assert.Equal((PassEnd)0, progress.Current.PassEnd);
    }

    [Fact]
    public void Consumed_WhenTheSequenceIsNotTheCurrentPass_ThenNothingChanges()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        var first = progress.PassStarted(PassKind.CxtNames, 50);
        progress.Consumed(first, 10);
        var second = progress.PassStarted(PassKind.CxtRows, 50);
        var before = recorder.Events.Count;

        progress.Consumed(first, 49);
        progress.Consumed(0, 7);

        Assert.Equal(0, progress.Current.BytesConsumed);
        Assert.Equal(second, progress.Current.PassSequence);
        Assert.Equal(before, recorder.Events.Count);
    }

    [Fact]
    public void Emitted_WhenThePassInputClosedAndItsEnumerationContinues_ThenObjectsStillCount()
    {
        var progress = new OperationProgress();
        var pass = progress.PassStarted(PassKind.Statistics, 30);
        progress.Emitted(pass, 1);
        progress.Closed(pass, WorkingStep.Grouping);

        progress.Emitted(pass, 2);
        progress.Emitted(pass, 3);

        var current = progress.Current;
        Assert.Equal(3, current.ObjectsEmitted);
        Assert.Equal(WorkingStep.Grouping, current.Step);
        Assert.Equal((PassEnd)0, current.PassEnd);
    }

    [Fact]
    public void Emitted_WhenNoPassHasStarted_ThenNothingChanges()
    {
        // An enumeration whose scope started no pass reports sequence 0, which never counts.
        var progress = new OperationProgress();

        progress.Emitted(0, 5);

        Assert.Equal(0, progress.Current.ObjectsEmitted);
    }

    [Fact]
    public void Closed_WhenEndOfInputWasObserved_ThenTheEndIsRecordedAtOnceAndThePostInputStepIsSet()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.Calibration, 20);
        progress.Consumed(pass, 20);
        progress.EndOfInput(pass);

        progress.Closed(pass, WorkingStep.Calibrating);

        var current = progress.Current;
        Assert.Equal(PassEnd.EndOfInput, current.PassEnd);
        Assert.Equal(WorkingStep.Calibrating, current.Step);
        Assert.Equal(
            ["PassStarted(1, Calibration, 20)", "Consumed(1, 20)", "Closed(1, eof)", "PassEnded(1, EndOfInput)", "Step(Calibrating)"],
            recorder.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void Closed_WhenEndOfInputWasNotObserved_ThenNoEndIsRecordedAndThePostInputStepIsSet()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.Dat, 20);
        progress.Consumed(pass, 8);

        progress.Closed(pass, WorkingStep.Grouping);

        var current = progress.Current;
        Assert.Equal((PassEnd)0, current.PassEnd);
        Assert.Equal(WorkingStep.Grouping, current.Step);
        Assert.Empty(recorder.EndsOf(pass));
    }

    [Fact]
    public void Closed_WhenTheSequenceIsNotTheCurrentPass_ThenNothingChanges()
    {
        // A leaked wrapper that saw end of input: its scope's exit ended pass 1, pass 2 started, and only
        // then was the old wrapper disposed. Its closure must neither end pass 2 nor set a step while
        // pass 2's input is open.
        var (progress, recorder) = ProgressRecorder.Create();
        var first = progress.PassStarted(PassKind.CxtNames, 20);
        progress.EndOfInput(first);
        progress.ScopeExit(first, PassEnd.ClosedBeforeEndOfInput, WorkingStep.Staging);
        var second = progress.PassStarted(PassKind.CxtRows, 20);
        var before = recorder.Events.Count;

        progress.Closed(first, WorkingStep.Grouping);

        var current = progress.Current;
        Assert.Equal(second, current.PassSequence);
        Assert.Equal((PassEnd)0, current.PassEnd);
        Assert.Equal((WorkingStep)0, current.Step);
        Assert.Equal(before, recorder.Events.Count);
        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(first));
    }

    [Fact]
    public void Closed_WhenThePassScopeAlreadyExited_ThenNothingChanges()
    {
        // The pass is still current, but its scope has exited: a late closure must not set a post-input
        // step that outlives the call, nor follow its end.
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.Statistics, 20);
        progress.ScopeExit(pass, PassEnd.Failed, 0);

        progress.Closed(pass, WorkingStep.Grouping);

        Assert.Equal(PassEnd.Failed, progress.Current.PassEnd);
        Assert.Equal((WorkingStep)0, progress.Current.Step);
        recorder.AssertGrammar(completed: false);
    }

    [Fact]
    public void ScopeExit_WhenThePassHasNoEnd_ThenTheReasonAndTheOuterStepAppearTogether()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.Dat, 20);
        progress.Closed(pass, WorkingStep.Grouping);

        progress.ScopeExit(pass, PassEnd.Cancelled, WorkingStep.Staging);

        var current = progress.Current;
        Assert.Equal(PassEnd.Cancelled, current.PassEnd);
        Assert.Equal(WorkingStep.Staging, current.Step);
        Assert.Equal("ScopeExited(1, Cancelled, Staging)", recorder.Events[^1].ToString());
    }

    [Fact]
    public void ScopeExit_WhenThePassEndedAtEndOfInput_ThenTheEndStaysAndTheOuterStepIsRestored()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.Calibration, 20);
        progress.EndOfInput(pass);
        progress.Closed(pass, WorkingStep.Calibrating);

        progress.ScopeExit(pass, PassEnd.Cancelled, 0);

        var current = progress.Current;
        Assert.Equal(PassEnd.EndOfInput, current.PassEnd);
        Assert.Equal((WorkingStep)0, current.Step);
        Assert.Equal("ScopeExited(1, 0, 0)", recorder.Events[^1].ToString());
        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(pass));
    }

    [Fact]
    public void ScopeExit_WhenTheUndisposedWrapperObservedEndOfInput_ThenEndOfInputTakesPrecedence()
    {
        var progress = new OperationProgress();
        var pass = progress.PassStarted(PassKind.Dat, 20);
        progress.EndOfInput(pass);

        progress.ScopeExit(pass, PassEnd.Failed, 0);

        Assert.Equal(PassEnd.EndOfInput, progress.Current.PassEnd);
    }

    [Fact]
    public void ScopeExit_WhenTheScopeStartedNoPass_ThenOnlyTheOuterStepIsRestored()
    {
        // An enumeration whose first advance threw before any open: whatever the previous pass's state,
        // its exit records no end and restores the step that was current when it began.
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.CxtNames, 20);
        progress.ScopeExit(pass, PassEnd.ClosedBeforeEndOfInput, WorkingStep.Staging);
        progress.Step(WorkingStep.Sealing);

        progress.ScopeExit(0, PassEnd.Failed, WorkingStep.Staging);

        var current = progress.Current;
        Assert.Equal(pass, current.PassSequence);
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, current.PassEnd);
        Assert.Equal(WorkingStep.Staging, current.Step);
        Assert.Equal("ScopeExited(0, 0, Staging)", recorder.Events[^1].ToString());
        recorder.AssertGrammar(completed: false);
    }

    [Fact]
    public void ScopeExit_WhenTheSequenceIsNotTheCurrentPass_ThenNothingChanges()
    {
        // A scope that never exited (its consumer leaked it) exits after a newer pass started: it must
        // neither end the newer pass nor replace its step.
        var (progress, recorder) = ProgressRecorder.Create();
        var first = progress.PassStarted(PassKind.CxtNames, 20);
        var second = progress.PassStarted(PassKind.CxtRows, 20);
        progress.Closed(second, WorkingStep.Grouping);
        var before = recorder.Events.Count;

        progress.ScopeExit(first, PassEnd.Failed, WorkingStep.Staging);

        var current = progress.Current;
        Assert.Equal(second, current.PassSequence);
        Assert.Equal((PassEnd)0, current.PassEnd);
        Assert.Equal(WorkingStep.Grouping, current.Step);
        Assert.Equal(before, recorder.Events.Count);
    }

    [Fact]
    public void ScopeExit_WhenCalledTwiceForOnePass_ThenTheSecondChangesNothing()
    {
        var progress = new OperationProgress();
        var pass = progress.PassStarted(PassKind.Dat, 20);
        progress.ScopeExit(pass, PassEnd.Cancelled, WorkingStep.Staging);
        progress.Step(WorkingStep.Cleaning);

        progress.ScopeExit(pass, PassEnd.Failed, WorkingStep.Staging);

        Assert.Equal(PassEnd.Cancelled, progress.Current.PassEnd);
        Assert.Equal(WorkingStep.Cleaning, progress.Current.Step);
    }

    [Fact]
    public void Step_WhenReported_ThenItIsShownAndRecorded()
    {
        var (progress, recorder) = ProgressRecorder.Create();

        progress.Step(WorkingStep.PreparingOutput);
        var shown = progress.Current.Step;
        progress.Step(0);

        Assert.Equal(WorkingStep.PreparingOutput, shown);
        Assert.Equal((WorkingStep)0, progress.Current.Step);
        Assert.Equal(["Step(PreparingOutput)", "Step(0)"], recorder.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void Complete_WhenCalledTwice_ThenTheSecondCallChangesNothing()
    {
        var (progress, recorder) = ProgressRecorder.Create();
        progress.Step(WorkingStep.Committing);

        progress.Complete();
        var first = progress.Current;
        progress.Complete();

        Assert.Same(first, progress.Current);
        Assert.Single(recorder.Events, e => e.Name == ProgressEvent.Completed);
        Assert.True(first.IsCompleted);
        Assert.Equal(WorkingStep.Committing, first.Step);
    }

    [Fact]
    public void Current_WhenCompleted_ThenEveryReadIsTheSameInstance()
    {
        var progress = new OperationProgress();
        var pass = progress.PassStarted(PassKind.Statistics, 9);
        progress.Consumed(pass, 9);
        progress.Complete();

        var reads = Enumerable.Range(0, 5).Select(_ => progress.Current).ToList();

        Assert.All(reads, read => Assert.Same(reads[0], read));
        Assert.True(reads[0].IsCompleted);
        Assert.Equal(9, reads[0].BytesConsumed);
    }

    [Fact]
    public void Complete_WhenEventsArriveAfterIt_ThenTheSnapshotNeverChangesAgain()
    {
        // The terminal state cannot regress: events already in flight may land in the live fields, but no
        // read observes them, and the sink records none of them.
        var (progress, recorder) = ProgressRecorder.Create();
        var pass = progress.PassStarted(PassKind.Dat, 9);
        progress.Complete();
        var final = progress.Current;
        var recorded = recorder.Events.Count;

        progress.Consumed(pass, 5);
        progress.Emitted(pass, 2);
        progress.EndOfInput(pass);
        progress.Closed(pass, WorkingStep.Grouping);
        progress.ScopeExit(pass, PassEnd.Cancelled, WorkingStep.Staging);
        progress.Step(WorkingStep.Cleaning);
        var late = progress.PassStarted(PassKind.Statistics, 1);

        Assert.Equal(0, late);
        Assert.Same(final, progress.Current);
        Assert.Equal(recorded, recorder.Events.Count);
        Assert.Equal(new ProgressSnapshot(1, PassKind.Dat, 9, 0, 0, 0, 0, isCompleted: true), final);
    }

    [Fact]
    public void ThrowIfCompleted_WhenTheProgressIsCompleted_ThenItNamesTheArgumentAndANullProgressPasses()
    {
        var progress = new OperationProgress();
        OperationProgress.ThrowIfCompleted(progress, "progress");
        OperationProgress.ThrowIfCompleted(null, "progress");
        progress.Complete();

        var refused = Assert.Throws<ArgumentException>(() => OperationProgress.ThrowIfCompleted(progress, "run"));

        Assert.Equal("run", refused.ParamName);
    }

    [Fact]
    public void Current_WhenManyUpdatesPrecedeOneRead_ThenTheNewestPassKindAndLengthAreShown()
    {
        // No queue: a reader that reads late sees the newest complete state, never a lost pass's kind.
        var progress = new OperationProgress();
        var kinds = new[] { PassKind.Schema, PassKind.Calibration, PassKind.CxtNames, PassKind.CxtRows, PassKind.Dat };
        for (var i = 0; i < kinds.Length; i++)
        {
            var pass = progress.PassStarted(kinds[i], 1000 + i);
            for (var bytes = 1; bytes <= 100; bytes++)
            {
                progress.Consumed(pass, bytes);
            }

            progress.EndOfInput(pass);
            progress.Closed(pass, 0);
            progress.ScopeExit(pass, PassEnd.ClosedBeforeEndOfInput, 0);
        }

        var current = progress.Current;
        Assert.Equal(5, current.PassSequence);
        Assert.Equal(PassKind.Dat, current.PassKind);
        Assert.Equal(1004, current.PassLength);
        Assert.Equal(100, current.BytesConsumed);
        Assert.Equal(PassEnd.EndOfInput, current.PassEnd);
    }

    [Fact]
    public async Task Current_WhenReadConcurrentlyWithAProducer_ThenEverySnapshotIsConsistent()
    {
        // An invariant test, not a timing one: whatever interleaving happens, each snapshot's kind and
        // length match its sequence, its counters never exceed that pass's planned totals, sequences
        // never decrease, no snapshot shows a non-end-of-input end with a post-input step, and after
        // completion every read is the same instance.
        const int Passes = 2_000;
        var progress = new OperationProgress();
        using var done = new CancellationTokenSource();
        var token = TestContext.Current.CancellationToken;

        static PassKind KindOf(int sequence) => (PassKind)(1 + (sequence % 6));
        static long LengthOf(int sequence) => 10 + (sequence % 17);

        var producer = Task.Run(
            () =>
            {
                for (var n = 1; n <= Passes; n++)
                {
                    var pass = progress.PassStarted(KindOf(n), LengthOf(n));
                    var length = LengthOf(n);
                    for (long bytes = 1; bytes <= length; bytes++)
                    {
                        progress.Consumed(pass, bytes);
                        progress.Emitted(pass, bytes);
                    }

                    var eof = n % 3 == 0;
                    if (eof)
                    {
                        progress.EndOfInput(pass);
                    }

                    progress.Closed(pass, n % 2 == 0 ? WorkingStep.Grouping : WorkingStep.Calibrating);
                    progress.Emitted(pass, length + 1);
                    progress.ScopeExit(pass, n % 4 == 0 ? PassEnd.Failed : PassEnd.ClosedBeforeEndOfInput, WorkingStep.Staging);
                    progress.Step(WorkingStep.Sealing);
                }

                progress.Complete();
                done.Cancel();
            },
            token);

        var lastSequence = 0;
        var reads = 0;
        while (!done.IsCancellationRequested || reads == 0)
        {
            var snapshot = progress.Current;
            reads++;
            Assert.True(snapshot.PassSequence >= lastSequence, "sequences went backwards");
            lastSequence = snapshot.PassSequence;
            if (snapshot.PassSequence > 0)
            {
                Assert.Equal(KindOf(snapshot.PassSequence), snapshot.PassKind);
                Assert.Equal(LengthOf(snapshot.PassSequence), snapshot.PassLength);
                Assert.True(snapshot.BytesConsumed <= LengthOf(snapshot.PassSequence), "bytes from another pass");
                Assert.True(snapshot.ObjectsEmitted <= LengthOf(snapshot.PassSequence) + 1, "objects from another pass");
            }

            Assert.False(
                snapshot.PassEnd is PassEnd.ClosedBeforeEndOfInput or PassEnd.Failed or PassEnd.Cancelled
                && snapshot.Step is WorkingStep.Grouping or WorkingStep.Calibrating,
                $"a {snapshot.PassEnd} end shown with the post-input step {snapshot.Step}");
        }

        await producer;

        var final = progress.Current;
        Assert.True(final.IsCompleted);
        Assert.Equal(Passes, final.PassSequence);
        Assert.Same(final, progress.Current);
    }
}
