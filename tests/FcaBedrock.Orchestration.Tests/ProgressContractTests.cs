using System.Runtime.CompilerServices;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The operation progress through the public operations (D-141): which passes each operation reports
/// and how each ends, the post-input step a grouping plan shows after its input closed, the consumer
/// boundary of an enumeration scope, the publication steps, the refusal of a completed progress, and
/// parity: every outcome, diagnostic, attempt fact, byte and filesystem operation is the same with and
/// without a progress. Every observed scenario also checks the recorded grammar and that each data
/// open recorded a pass.
/// </summary>
public sealed class ProgressContractTests
{
    /// <summary>A wide source larger than one read-ahead, so a schema read stops before its end.</summary>
    private static readonly string LargeWide = ProgressSpecs.WideData(120_000);

    // ---- preparation ------------------------------------------------------------------------------

    [Fact]
    public async Task Preparation_WhenTheDataIsLargerThanOneReadAhead_ThenTheSchemaPassEndsClosedBeforeEndOfInput()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, LargeWide, observed: true);

        var (prepared, _) = await run.PrepareAsync();

        var schema = Assert.Single(run.Input.DataOpens);
        Assert.True(schema.Bytes < schema.FileLength, $"the schema read consumed {schema.Bytes} of {schema.FileLength} bytes");
        Assert.Equal([(1, PassKind.Schema)], run.Recorder!.Passes());
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, run.Recorder.EndOf(1));
        Assert.Equal(schema.FileLength, run.Recorder.Events[0].Length);
        run.CompleteAndAssertGrammar(prepared);
        var final = run.Progress!.Current;
        Assert.True(final.IsCompleted);
        Assert.Equal(schema.Bytes, final.BytesConsumed);
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, final.PassEnd);
    }

    [Fact]
    public async Task Preparation_WhenTheDataFitsInOneReadAhead_ThenTheSchemaPassEndsWhereTheCurrentReaderLeavesIt()
    {
        // Not a contract: whether a small file's schema read reaches its end is the reader's behaviour.
        // The current reader fills its first span until the decoder returns zero, so it reaches the end
        // of a file this small; this pins that observation.
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed: true);

        var (prepared, _) = await run.PrepareAsync();

        var schema = Assert.Single(run.Input.DataOpens);
        Assert.Equal(schema.FileLength, schema.Bytes);
        Assert.Equal(PassEnd.EndOfInput, run.Recorder!.EndOf(1));
        run.CompleteAndAssertGrammar(prepared);
    }

    [Fact]
    public async Task Preparation_WhenTheSpecCalibrates_ThenTheCalibrationPassEndsAtEndOfInputAndCalibratingRunsUntilTheCallReturns()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideCalibrated, ProgressSpecs.WideData(3_000), observed: true);

        var (prepared, _) = await run.PrepareAsync();

        Assert.Equal([(1, PassKind.Schema), (2, PassKind.Calibration)], run.Recorder!.Passes());
        Assert.Equal(
            ["PassStarted(2, Calibration, " + run.Input.DataOpens[1].FileLength + ")", "Closed(2, eof)", "PassEnded(2, EndOfInput)", "Step(Calibrating)", "ScopeExited(2, 0, 0)"],
            run.Recorder.Events.SkipWhile(e => e.Sequence != 2).Where(e => e.Name != ProgressEvent.Consumed).Select(e => e.ToString()));
        Assert.Equal((WorkingStep)0, run.Progress!.Current.Step);
        run.CompleteAndAssertGrammar(prepared);
    }

    [Fact]
    public async Task Preparation_WhenAnUnorderedCountSensitiveTripleCalibrates_ThenTwoCalibrationPassesFollowTheSchemaPass()
    {
        using var run = ProgressRun.Create(ProgressSpecs.TripleCountSensitive, ProgressSpecs.AgeData(3_000), observed: true);

        var (prepared, _) = await run.PrepareAsync();

        var recorder = run.Recorder!;
        Assert.Equal([(1, PassKind.Schema), (2, PassKind.Calibration), (3, PassKind.Calibration)], recorder.Passes());
        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(2));
        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(3));

        // The raw pass closes and the calibrator names its work before the grouped pass opens, whose
        // start returns the step to 0.
        var events = recorder.Events.Where(e => e.Name != ProgressEvent.Consumed).Select(e => e.ToString()).ToList();
        var calibrating = events.IndexOf("Step(Calibrating)");
        Assert.True(calibrating > events.IndexOf("Closed(2, eof)"), recorder.Describe());
        Assert.True(calibrating < events.FindIndex(e => e.StartsWith("PassStarted(3,", StringComparison.Ordinal)), recorder.Describe());
        run.CompleteAndAssertGrammar(prepared);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_WhenTheDataOpenThrows_ThenNoPassStartsAndTheOutcomeIsDataUnreadable(bool observed)
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed);
        run.Input.FailOpen = path => path == run.DataPath ? new FileNotFoundException("the data is gone.") : null;

        var failure = Assert.IsType<OperationHostFailure<PreparedFileRun>>(await run.PrepareOutcomeAsync());

        Assert.Equal(HostErrorKind.DataUnreadable, failure.Error.Kind);
        if (observed)
        {
            Assert.Empty(run.Recorder!.Passes());
            Assert.Equal(["ScopeExited(0, 0, 0)"], run.Recorder.Events.Select(e => e.ToString()));
            run.CompleteAndAssertGrammar(null);
        }
    }

    // ---- statistics -------------------------------------------------------------------------------

    [Fact]
    public async Task Statistics_WhenCounted_ThenTheCountingPassFollowsWithItsObjectsAndEndsAtEndOfInput()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideCalibrated, ProgressSpecs.WideData(3_000), observed: true);
        var (prepared, diagnostics) = await run.PrepareAsync();

        var counted = Assert.IsType<OperationCompleted<ContextStatistics>>(
            await ContextStatistics.CountAsync(prepared, diagnostics, run.Signals.Token));

        var recorder = run.Recorder!;
        Assert.Equal((3, PassKind.Statistics), recorder.Passes()[^1]);
        Assert.Equal(PassEnd.EndOfInput, recorder.EndOf(3));
        Assert.Equal(counted.Value.Objects, recorder.Events.Last(e => e.Name == ProgressEvent.Emitted).Value);
        Assert.DoesNotContain(WorkingStep.Grouping, recorder.NamedSteps());
        Assert.Equal(counted.Value.Objects, run.Progress!.Current.ObjectsEmitted);
        run.CompleteAndAssertGrammar(prepared);
    }

    [Fact]
    public async Task Statistics_WhenTheDedupePlanGroupsACompleteInput_ThenGroupingRunsFromClosureWhileObjectsStillRise()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDedupe, ProgressSpecs.DedupeData(3_000, offender: false, 0), observed: true);
        var (prepared, diagnostics) = await run.PrepareAsync();

        var counted = Assert.IsType<OperationCompleted<ContextStatistics>>(
            await ContextStatistics.CountAsync(prepared, diagnostics, run.Signals.Token));

        var events = run.Recorder!.Events.ToList();
        var closed = events.FindIndex(e => e.Name == ProgressEvent.Closed && e.Sequence == 2);
        var grouping = events.FindIndex(e => e.Name == ProgressEvent.StepChanged && e.Step == WorkingStep.Grouping);
        var firstObject = events.FindIndex(e => e.Name == ProgressEvent.Emitted && e.Sequence == 2);
        Assert.True(closed >= 0 && closed < grouping && grouping < firstObject, run.Recorder.Describe());
        Assert.True(events[closed].EndOfInput);
        Assert.Equal(PassEnd.EndOfInput, run.Recorder.EndOf(2));
        Assert.Equal(counted.Value.Objects, events.Last(e => e.Name == ProgressEvent.Emitted).Value);
        run.CompleteAndAssertGrammar(prepared);
    }

    // ---- the real prefix cases: a grouping plan halted at an unusable key ---------------------------

    public static TheoryData<string> PrefixShapes() => ["wide dedupe", "triple unordered"];

    private static (string Spec, string Data) PrefixCase(string shape) => shape == "wide dedupe"
        ? (ProgressSpecs.WideDedupe, ProgressSpecs.DedupeData(2_000, offender: true, 150_000))
        : (ProgressSpecs.TripleUnordered, ProgressSpecs.TripleData(2_000, offender: true, 150_000));

    [Theory]
    [MemberData(nameof(PrefixShapes))]
    public async Task Statistics_WhenAGroupingPlanHaltsAtAnUnusableKey_ThenGroupingPrecedesAClosedBeforeEndOfInputEnd(string shape)
    {
        var (spec, data) = PrefixCase(shape);
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", spec);
        var dataPath = temp.Write("data.csv", data);

        var plain = await CountAsync(specPath, dataPath, observed: false);
        var watched = await CountAsync(specPath, dataPath, observed: true);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains(nameof(DiagnosticCode.ObjectKeyValueInvalid), watched.Text, StringComparison.Ordinal);
        AssertGroupingPrefixPass(watched.Recorder!, watched.Opens, sequence: 2);
    }

    [Theory]
    [MemberData(nameof(PrefixShapes))]
    public async Task Publish_WhenAGroupingPlanHaltsAtAnUnusableKey_ThenEveryEmitPassEndsClosedBeforeEndOfInputAndNothingElseChanges(
        string shape)
    {
        // Both formats: the .cxt artifact stages, the .dat artifact stages too because its stage failed
        // nothing, and only then does the run fail at its final diagnostic check and roll back. So the
        // schema pass is followed by three emit passes, each truncated at the offender.
        var (spec, data) = PrefixCase(shape);
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", spec);
        var dataPath = temp.Write("data.csv", data);
        ProgressObservation.Arrangement? watchedArrangement = null;

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false);
        var watched = await ProgressObservation.ConvertAsync(
            temp.Path, specPath, dataPath, observed: true, arrange: arrangement => watchedArrangement = arrangement);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("publish: OperationDiagnosticFailure", watched.Text, StringComparison.Ordinal);
        Assert.Contains("attempt BegunNotCommitted rollback Completed", watched.Text, StringComparison.Ordinal);
        var recorder = watched.Recorder!;
        Assert.Equal(
            [(1, PassKind.Schema), (2, PassKind.CxtNames), (3, PassKind.CxtRows), (4, PassKind.Dat)],
            recorder.Passes());
        var opens = watchedArrangement!.Input.DataOpens;
        foreach (var sequence in new[] { 2, 3, 4 })
        {
            AssertGroupingPrefixPass(recorder, opens, sequence);
        }

        Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
        Assert.DoesNotContain(WorkingStep.Committing, recorder.NamedSteps());
    }

    // The prefix case's pass: its input closed before the end of the file, Grouping was named at the
    // closure, objects still counted, and exactly one end followed, closed before end of input, with no
    // post-input step at or after it.
    private static void AssertGroupingPrefixPass(ProgressRecorder recorder, IReadOnlyList<ScriptedStream> opens, int sequence)
    {
        var open = opens[sequence - 1];
        Assert.True(open.Bytes < open.FileLength, $"pass {sequence} consumed {open.Bytes} of {open.FileLength} bytes: the suffix was read");
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, recorder.EndOf(sequence));

        var events = recorder.Events.ToList();
        var closed = events.FindIndex(e => e.Name == ProgressEvent.Closed && e.Sequence == sequence);
        Assert.False(events[closed].EndOfInput, recorder.Describe());
        Assert.Equal(new ProgressEvent(ProgressEvent.StepChanged, Step: WorkingStep.Grouping), events[closed + 1]);
        var end = events.FindIndex(e => e.Sequence == sequence && e.End != 0);
        Assert.True(end > closed + 1, recorder.Describe());
        Assert.Contains(events[(closed + 1)..end], e => e.Name == ProgressEvent.Emitted && e.Sequence == sequence);
        var next = events.FindIndex(end, e => e.Name == ProgressEvent.PassStarted);
        var after = next < 0 ? events[(end + 1)..] : events[(end + 1)..next];
        Assert.DoesNotContain(after, e => e.Name == ProgressEvent.StepChanged && e.Step is WorkingStep.Grouping or WorkingStep.Calibrating);
    }

    private sealed record Counted(string Text, ProgressRecorder? Recorder, IReadOnlyList<ScriptedStream> Opens);

    private static async Task<Counted> CountAsync(string specPath, string dataPath, bool observed)
    {
        var input = new ScriptedInput(dataPath);
        var host = input.Host(new RecordingPublicationFileSystem());
        OperationProgress? progress = null;
        ProgressRecorder? recorder = null;
        if (observed)
        {
            (progress, recorder) = ProgressRecorder.Create();
        }

        var text = new System.Text.StringBuilder();
        var prepared = Assert.IsType<OperationCompleted<PreparedFileRun>>(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = specPath, DataPath = dataPath }, host, progress, CancellationToken.None));
        ProgressObservation.Render(text, "prepare", prepared);
        var counted = await ContextStatistics.CountAsync(prepared.Value, prepared.Diagnostics, CancellationToken.None);
        ProgressObservation.Render(text, "count", counted);
        if (progress is not null)
        {
            progress.Complete();
            recorder!.AssertGrammar(observedOpens: prepared.Value.Input.ObservedOpens);
            Assert.Equal(input.DataOpens.Count, prepared.Value.Input.ObservedOpens);
        }

        return new Counted(text.ToString(), recorder, input.DataOpens);
    }

    // ---- the consumer boundary --------------------------------------------------------------------

    [Fact]
    public async Task Publish_WhenTheDatStageWriteFailsBeforeEndOfInput_ThenThePassEndsClosedBeforeEndOfInputAndTheStageFails()
    {
        // The write fails in the writer's own loop body, which is never passed to the enumerator, so the
        // enumeration scope sees no exception: its input simply closed early.
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", ProgressSpecs.WideDeclared);
        var dataPath = temp.Write("data.csv", LargeWide);
        ProgressObservation.Arrangement? watchedArrangement = null;
        static void FailTheStage(ProgressObservation.Arrangement arrangement) =>
            arrangement.Files.FailStreamWritePrefix = "out.dat.fcabedrock-stage-";

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false, cxt: false, arrange: FailTheStage);
        var watched = await ProgressObservation.ConvertAsync(
            temp.Path, specPath, dataPath, observed: true, cxt: false, arrange: arrangement =>
            {
                FailTheStage(arrangement);
                watchedArrangement = arrangement;
            });

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("publish: OperationHostFailure`1 StageFailed", watched.Text, StringComparison.Ordinal);
        Assert.Contains("attempt BegunNotCommitted rollback Completed", watched.Text, StringComparison.Ordinal);
        var recorder = watched.Recorder!;
        Assert.Equal([(1, PassKind.Schema), (2, PassKind.Dat)], recorder.Passes());
        var dat = watchedArrangement!.Input.DataOpens[1];
        Assert.True(dat.Bytes < dat.FileLength, $"the .dat pass consumed {dat.Bytes} of {dat.FileLength} bytes");
        Assert.Equal(PassEnd.ClosedBeforeEndOfInput, recorder.EndOf(2));
        Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenTheCxtRowsPassYieldsAnotherNameSequence_ThenThePassEndsClosedBeforeEndOfInputAndTheWriterExceptionPropagates(
        bool observed)
    {
        // A stand-in run whose emitter reads a real tracker pass each time and yields a different name
        // on its second enumeration: the .cxt writer's name check throws in its loop body.
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, LargeWide, observed);
        var (real, diagnostics) = await run.PrepareAsync();
        var enumerations = 0;
        var standIn = ProgressSpecs.StandIn(real, (_, _, token) => Names(real.Input, ++enumerations == 1 ? "b" : "c", token));
        var conversion = ProgressRun.Plan(standIn, diagnostics);
        var handle = run.Preflight(standIn, run.Output(dat: false, manifest: false));

        var thrown = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => run.PublishAsync(conversion, handle, diagnostics));

        Assert.Contains("name", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.Completed, handle.Attempt.Rollback);
        Assert.Empty(run.Residue());
        if (observed)
        {
            var recorder = run.Recorder!;
            Assert.Equal([(1, PassKind.Schema), (2, PassKind.CxtNames), (3, PassKind.CxtRows)], recorder.Passes());
            Assert.Equal(PassEnd.ClosedBeforeEndOfInput, recorder.EndOf(2));
            Assert.Equal(PassEnd.ClosedBeforeEndOfInput, recorder.EndOf(3));
            Assert.Single(recorder.NamedSteps(), step => step == WorkingStep.Cleaning);
            run.CompleteAndAssertGrammar(real);
        }
    }

    // Opens one real tracker pass, reads a little of it, closes it before its end and yields "a" and
    // then the given second name.
    private static async IAsyncEnumerable<EmittedObject> Names(
        InputHashTracker input, string second, [EnumeratorCancellation] CancellationToken cancellation)
    {
        await using (var stream = input.OpenHashed())
        {
            _ = await stream.ReadAsync(new byte[64], cancellation);
        }

        yield return ProgressSpecs.Object("a");
        yield return ProgressSpecs.Object(second);
    }

    [Fact]
    public async Task Publish_WhenADatReadFails_ThenThePassEndsFailedAndTheOutcomeIsDataUnreadable()
    {
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", ProgressSpecs.WideDeclared);
        var dataPath = temp.Write("data.csv", LargeWide);
        static void FailARead(ProgressObservation.Arrangement arrangement) =>
            arrangement.Input.BeforeRead = (stream, call) =>
            {
                if (stream.Index == 1 && call == 3)
                {
                    throw new IOException("the disk failed.");
                }
            };

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false, cxt: false, arrange: FailARead);
        var watched = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: true, cxt: false, arrange: FailARead);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("publish: OperationHostFailure`1 DataUnreadable", watched.Text, StringComparison.Ordinal);
        Assert.Contains("attempt BegunNotCommitted rollback Completed", watched.Text, StringComparison.Ordinal);
        Assert.Equal(PassEnd.Failed, watched.Recorder!.EndOf(2));
    }

    [Fact]
    public async Task Publish_WhenTheCallsOwnCancellationArrivesAtADatRead_ThenThePassEndsCancelled()
    {
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", ProgressSpecs.WideDeclared);
        var dataPath = temp.Write("data.csv", LargeWide);
        static void CancelAtARead(ProgressObservation.Arrangement arrangement) =>
            arrangement.Input.BeforeRead = (stream, call) =>
            {
                if (stream.Index == 1 && call == 3)
                {
                    arrangement.Signals.Cancel();
                }
            };

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false, cxt: false, arrange: CancelAtARead);
        var watched = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: true, cxt: false, arrange: CancelAtARead);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("(token host)", watched.Text, StringComparison.Ordinal);
        Assert.Contains("attempt BegunNotCommitted rollback Completed", watched.Text, StringComparison.Ordinal);
        Assert.Equal(PassEnd.Cancelled, watched.Recorder!.EndOf(2));
    }

    [Fact]
    public async Task Publish_WhenTheDataThrowsAnotherTokensCancellation_ThenThePassEndsFailedAndThatExceptionPropagates()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, LargeWide, observed: true);
        using var other = new CancellationTokenSource();
        var foreign = new OperationCanceledException("another operation was cancelled.", other.Token);
        var (prepared, diagnostics) = await run.PrepareAsync();
        run.Input.BeforeRead = (stream, call) =>
        {
            if (stream.Index == 1 && call == 3)
            {
                throw foreign;
            }
        };
        var conversion = ProgressRun.Plan(prepared, diagnostics);
        var handle = run.Preflight(prepared, run.Output(cxt: false));

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => run.PublishAsync(conversion, handle, diagnostics));

        Assert.Same(foreign, thrown);
        Assert.Equal(other.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        Assert.Equal(PassEnd.Failed, run.Recorder!.EndOf(2));
        run.CompleteAndAssertGrammar(prepared);
    }

    [Fact]
    public async Task Publish_WhenCancelledAsTheZeroLengthReadArrives_ThenThePassEndsAtEndOfInputAndTheCallThrowsTheHostCancellation()
    {
        // End of input is not completion: the pass consumed its bytes to the end, and the operation is
        // still cancelled, whichever check observes the cancellation.
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", ProgressSpecs.WideDeclared);
        var dataPath = temp.Write("data.csv", ProgressSpecs.WideData(3_000));
        static void CancelAtTheEnd(ProgressObservation.Arrangement arrangement) =>
            arrangement.Input.AfterRead = (stream, _, read, requested) =>
            {
                if (stream.Index == 1 && read == 0 && requested > 0)
                {
                    arrangement.Signals.Cancel();
                }
            };

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false, cxt: false, arrange: CancelAtTheEnd);
        var watched = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: true, cxt: false, arrange: CancelAtTheEnd);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("(token host)", watched.Text, StringComparison.Ordinal);
        Assert.Equal(PassEnd.EndOfInput, watched.Recorder!.EndOf(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_WhenTheEmitterCancelsAfterItsLastObject_ThenThePassEndsAtEndOfInputAndTheCallThrowsTheHostCancellation(
        bool observed)
    {
        // A stand-in emitter reads its pass to the end, closes it, yields its objects and requests
        // cancellation after the last one; the .dat writer's final flush observes it.
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, ProgressSpecs.WideData(500), observed);
        var (real, diagnostics) = await run.PrepareAsync();
        var standIn = ProgressSpecs.StandIn(real, (_, _, token) => DrainThenCancel(real.Input, run.Signals, token));
        var conversion = ProgressRun.Plan(standIn, diagnostics);
        var handle = run.Preflight(standIn, run.Output(cxt: false, manifest: false));

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.PublishAsync(conversion, handle, diagnostics));

        Assert.Equal(run.Signals.Token, thrown.CancellationToken);
        Assert.Equal(PublicationAttemptState.BegunNotCommitted, handle.Attempt.State);
        if (observed)
        {
            Assert.Equal(PassEnd.EndOfInput, run.Recorder!.EndOf(2));
            run.CompleteAndAssertGrammar(real);
        }
    }

    private static async IAsyncEnumerable<EmittedObject> DrainThenCancel(
        InputHashTracker input, CancellationTokenSource signals, [EnumeratorCancellation] CancellationToken cancellation)
    {
        await using (var stream = input.OpenHashed())
        {
            var buffer = new byte[4096];
            while (await stream.ReadAsync(buffer, cancellation) > 0)
            {
            }
        }

        yield return ProgressSpecs.Object("a");
        yield return ProgressSpecs.Object("b");
        await signals.CancelAsync();
    }

    // ---- single-file delivery ---------------------------------------------------------------------

    [Fact]
    public async Task SingleFile_WhenAFrozenSpecIsDelivered_ThenThePublicationStepsFollowInOrder()
    {
        // calibrate --out at the coordinator: one progress for the preparation and the delivery.
        using var run = ProgressRun.Create(ProgressSpecs.WideCalibrated, ProgressSpecs.WideData(200), observed: true);
        var (prepared, diagnostics) = await run.PrepareAsync();
        var frozen = Assert.IsType<OperationCompleted<SpecDocument>>(SpecFreezing.Freeze(prepared, diagnostics)).Value;
        var target = Path.Combine(run.Directory, "frozen.toml");

        var handle = Assert.IsType<OperationCompleted<PreflightedSpecFile>>(SpecPublication.Preflight(
            new SpecDelivery { Target = target, ReplaceExisting = false, Operands = [run.SpecPath, run.DataPath], Chain = prepared.SpecChain },
            run.Host,
            run.Progress,
            run.Signals.Token)).Value;
        Assert.IsType<OperationCompleted<Published>>(await SpecPublication.PublishAsync(frozen, handle, run.Signals.Token));

        Assert.Equal(
            [WorkingStep.Calibrating, WorkingStep.PreparingOutput, WorkingStep.Staging, WorkingStep.Sealing, WorkingStep.Committing, WorkingStep.Cleaning],
            run.Recorder!.NamedSteps());
        Assert.Equal((WorkingStep)0, run.Progress!.Current.Step);
        Assert.True(File.Exists(target));
        run.CompleteAndAssertGrammar(prepared);
    }

    // ---- a completed progress refuses new work ----------------------------------------------------

    [Fact]
    public async Task PrepareAsync_WhenTheProgressIsCompleted_ThenItRefusesBeforeAnyFileOpens()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed: true);
        run.Progress!.Complete();

        var refused = await Assert.ThrowsAsync<ArgumentException>(run.PrepareOutcomeAsync);

        Assert.Equal("progress", refused.ParamName);
        Assert.Empty(run.Input.Opened);
    }

    [Fact]
    public async Task CountAsync_WhenTheRunsProgressIsCompleted_ThenItRefusesBeforeAnyFileOpens()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed: true);
        var (prepared, diagnostics) = await run.PrepareAsync();
        var opened = run.Input.Opened.Count;
        run.Progress!.Complete();

        var refused = await Assert.ThrowsAsync<ArgumentException>(
            () => ContextStatistics.CountAsync(prepared, diagnostics, run.Signals.Token));

        Assert.Equal("run", refused.ParamName);
        Assert.Equal(opened, run.Input.Opened.Count);
    }

    [Fact]
    public async Task ConvertPreflight_WhenTheRunsProgressIsCompleted_ThenItRefusesBeforeAnyFilesystemOperation()
    {
        using var run = ProgressRun.Create(ProgressSpecs.WideDeclared, PublicationRun.Data, observed: true);
        var (prepared, _) = await run.PrepareAsync();
        run.Progress!.Complete();

        var refused = Assert.Throws<ArgumentException>(
            () => ConvertPublication.Preflight(run.Output(), prepared, run.Host, run.Signals.Token));

        Assert.Equal("run", refused.ParamName);
        Assert.Empty(run.Files.Operations);
    }

    [Fact]
    public void SpecPreflight_WhenTheProgressIsCompleted_ThenItRefusesBeforeAnyFilesystemOperation()
    {
        using var temp = TempDirectory.Create();
        var files = new RecordingPublicationFileSystem();
        var progress = new OperationProgress();
        progress.Complete();

        var refused = Assert.Throws<ArgumentException>(() => SpecPublication.Preflight(
            new SpecDelivery { Target = temp.Resolve("out.toml"), ReplaceExisting = false },
            new OrchestrationHost(File.OpenRead) { PublicationFiles = files },
            progress,
            CancellationToken.None));

        Assert.Equal("progress", refused.ParamName);
        Assert.Empty(files.Operations);
    }

    [Theory]
    [InlineData(PublicationRun.Convert, "conversion")]
    [InlineData(PublicationRun.SpecFile, "handle")]
    public async Task Publish_WhenTheCarriedProgressIsCompleted_ThenItRefusesWithoutSpendingTheHandle(string family, string parameter)
    {
        // The refusal comes after the argument and association checks and before the handle is spent:
        // the handle stays unspent and disposable, but can never publish through that progress.
        // An existing target and authorized replacement, so the handle holds a live reference to it.
        var progress = new OperationProgress();
        using var run = await PublicationRun.CreateAsync(family, progress: progress);
        PublicationRun.Occupy(run.Target);
        var handle = run.Preflight(replace: true);
        progress.Complete();

        var first = await Assert.ThrowsAsync<ArgumentException>(() => run.PublishAsync(handle));
        var second = await Assert.ThrowsAsync<ArgumentException>(() => run.PublishAsync(handle));

        Assert.Equal(parameter, first.ParamName);
        Assert.Equal(parameter, second.ParamName);
        Assert.Equal(PublicationAttemptState.NotBegun, handle.Attempt.State);
        Assert.Equal(RollbackOutcome.NotOwed, handle.Attempt.Rollback);
        Assert.Empty(run.Mutations());
        Assert.Equal(PublicationRun.Old, File.ReadAllText(run.Target));
        if (OperatingSystem.IsWindows())
        {
            // The positive control: the unspent handle still holds the target, so the seam's removal
            // leaves the name occupied. Disposing the handle releases it, and the name becomes free.
            PublicationRun.AssertReferenceHeld(run.Target);
            handle.Dispose();
            File.WriteAllText(run.Target, "the next occupant");
            Assert.Equal("the next occupant", File.ReadAllText(run.Target));
        }
        else
        {
            handle.Dispose();
        }
    }

    // ---- parity at the coordinator (EP-7) ---------------------------------------------------------

    public static TheoryData<string, bool, bool> AuthoredParityCases()
    {
        var cases = new TheoryData<string, bool, bool>();
        foreach (var shape in new[] { "wide", "wide dedupe", "triple grouped", "triple unordered" })
        {
            foreach (var manifest in new[] { true, false })
            {
                foreach (var v2 in new[] { false, true })
                {
                    cases.Add(shape, manifest, v2);
                }
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(AuthoredParityCases))]
    public async Task Convert_WhenAnAuthoredSpecIsObservedOrNot_ThenEveryFactIsEqual(string shape, bool manifest, bool v2)
    {
        var (spec, data) = shape switch
        {
            "wide" => (ProgressSpecs.WideDeclared, ProgressSpecs.WideData(2_000)),
            "wide dedupe" => (ProgressSpecs.WideDedupe, ProgressSpecs.DedupeData(2_000, offender: false, 0)),
            "triple grouped" => (ProgressSpecs.TripleGrouped, ProgressSpecs.TripleData(2_000, offender: false, 0, contiguous: true)),
            _ => (ProgressSpecs.TripleUnordered, ProgressSpecs.TripleData(2_000, offender: false, 0)),
        };
        using var temp = TempDirectory.Create();
        var specPath = temp.Write("spec.toml", spec);
        var dataPath = temp.Write("data.csv", data);

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false, manifest: manifest, v2: v2);
        var watched = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: true, manifest: manifest, v2: v2);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("publish: OperationCompleted", watched.Text, StringComparison.Ordinal);
        Assert.Equal(
            [PassKind.Schema, PassKind.CxtNames, PassKind.CxtRows, PassKind.Dat],
            watched.Recorder!.Passes().Select(pass => pass.Kind));
    }

    /// <summary>Every active v2 golden: its family, variant, binding the .bed never recorded, scaling and data variant.</summary>
    private static readonly (string Family, string Variant, BindingSection Binding, ScalingMode Scaling, string Data)[] Goldens =
    [
        ("mini-mushroom", "mini-mushroom", Wide(',', true), ScalingMode.Discrete, "mini-mushroom"),
        ("mini-mushroom", "mini-mushroom_tabbed_noheader", Wide('\t', false), ScalingMode.Discrete, "mini-mushroom_tabbed_noheader"),
        ("mini-adult", "mini-adult", Wide(',', true), ScalingMode.Discrete, "mini-adult"),
        ("mini-adult", "mini-adult_noheader", Wide(',', false), ScalingMode.Discrete, "mini-adult_noheader"),
        ("mini-adult", "mini-adult_employment_ordinal_discrete", Wide(',', true), ScalingMode.Discrete, "mini-adult"),
        ("mini-adult", "mini-adult_employment_ordinal_progressive", Wide(',', true), ScalingMode.Progressive, "mini-adult"),
        ("mini-mushroom", "mini-mushroom_triples", Triple(), ScalingMode.Discrete, "mini-mushroom_triples"),
        ("mini-adult", "mini-adult_triples", Triple(), ScalingMode.Discrete, "mini-adult_triples"),
        ("mini-adult", "mini-adult_triples_named", Triple(), ScalingMode.Discrete, "mini-adult_triples_named"),
    ];

    private static BindingSection Wide(char delimiter, bool hasHeader) =>
        new(SourceShape.Wide, Encoding: null, delimiter, QuoteChar: null, hasHeader,
            Locale: null, MissingToken: null, Ordering: null, Columns: null, ObjectKey: null);

    private static BindingSection Triple() =>
        new(SourceShape.Triple, Encoding: null, ',', QuoteChar: null, HasHeader: false,
            Locale: null, MissingToken: null, Ordering: TripleOrdering.Unordered, Columns: null, ObjectKey: null);

    public static TheoryData<string, bool, bool> GoldenParityCases()
    {
        var cases = new TheoryData<string, bool, bool>();
        foreach (var golden in Goldens)
        {
            foreach (var manifest in new[] { true, false })
            {
                foreach (var v2 in new[] { false, true })
                {
                    cases.Add(golden.Variant, manifest, v2);
                }
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(GoldenParityCases))]
    public async Task Convert_WhenAGoldenIsObservedOrNot_ThenEveryFactIsEqual(string variant, bool manifest, bool v2)
    {
        var golden = Goldens.Single(row => row.Variant == variant);
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", golden.Family);
        var bedPath = Path.Combine(directory, golden.Variant + ".bed");
        var dataPath = Path.Combine(directory, golden.Data + ".data");
        var read = BedReader.Read(await File.ReadAllTextAsync(bedPath), bedPath);
        Assert.True(read.TryGetValue(out var bed), string.Join("\n", read.Diagnostics.Select(ProgressObservation.Describe)));
        var migrated = BedMigrator.Migrate(bed, golden.Binding, golden.Scaling, bedPath);
        Assert.True(migrated.TryGetValue(out var document), string.Join("\n", migrated.Diagnostics.Select(ProgressObservation.Describe)));
        using var temp = TempDirectory.Create();
        var specPath = temp.Write(golden.Variant + ".toml", SpecWriter.Write(document));

        var plain = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: false, manifest: manifest, v2: v2);
        var watched = await ProgressObservation.ConvertAsync(temp.Path, specPath, dataPath, observed: true, manifest: manifest, v2: v2);

        Assert.Equal(plain.Text, watched.Text);
        Assert.Contains("publish: OperationCompleted", watched.Text, StringComparison.Ordinal);
        Assert.Equal(4, watched.Recorder!.Passes().Count);
    }
}
