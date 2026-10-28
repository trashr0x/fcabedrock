using FcaBedrock.Orchestration;

namespace FcaBedrock.TestSupport;

/// <summary>One effect an <see cref="OperationProgress"/> recorded, in the sink's vocabulary (D-141).</summary>
internal sealed record ProgressEvent(
    string Name,
    int Sequence = 0,
    PassKind Kind = 0,
    long? Length = null,
    long Value = 0,
    bool EndOfInput = false,
    PassEnd End = 0,
    WorkingStep Step = 0)
{
    public const string PassStarted = nameof(PassStarted);
    public const string Consumed = nameof(Consumed);
    public const string Emitted = nameof(Emitted);
    public const string Closed = nameof(Closed);
    public const string PassEnded = nameof(PassEnded);
    public const string StepChanged = "Step";
    public const string ScopeExited = nameof(ScopeExited);
    public const string Completed = nameof(Completed);

    public override string ToString() => Name switch
    {
        PassStarted => $"PassStarted({Sequence}, {Kind}, {Length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"})",
        Consumed => $"Consumed({Sequence}, {Value})",
        Emitted => $"Emitted({Sequence}, {Value})",
        Closed => $"Closed({Sequence}, {(EndOfInput ? "eof" : "early")})",
        PassEnded => $"PassEnded({Sequence}, {End})",
        StepChanged => $"Step({Step})",
        ScopeExited => $"ScopeExited({Sequence}, {End}, {Step})",
        _ => Name,
    };
}

/// <summary>
/// The test sink: records every effect an <see cref="OperationProgress"/> reports, in order, and checks
/// the ordering guarantees of D-141 over them. Thread-safe, because a test may read it while the
/// operation runs on another thread.
/// </summary>
internal sealed class ProgressRecorder : IProgressEvents
{
    private readonly Lock _gate = new();
    private readonly List<ProgressEvent> _events = [];

    /// <summary>A copy of every recorded effect, in order.</summary>
    public IReadOnlyList<ProgressEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>A fresh progress whose sink is a fresh recorder.</summary>
    public static (OperationProgress Progress, ProgressRecorder Recorder) Create()
    {
        var recorder = new ProgressRecorder();
        return (new OperationProgress { Events = recorder }, recorder);
    }

    /// <summary>The passes in start order: sequence and kind.</summary>
    public List<(int Sequence, PassKind Kind)> Passes() =>
        [.. Events.Where(e => e.Name == ProgressEvent.PassStarted).Select(e => (e.Sequence, e.Kind))];

    /// <summary>Every end recorded for <paramref name="sequence"/>, from a closure, a next start or an exit.</summary>
    public List<PassEnd> EndsOf(int sequence) =>
        [.. Events.Where(e => e.Sequence == sequence && e.End != 0
            && e.Name is ProgressEvent.PassEnded or ProgressEvent.ScopeExited).Select(e => e.End)];

    /// <summary>The one end recorded for <paramref name="sequence"/>.</summary>
    public PassEnd EndOf(int sequence) => Assert.Single(EndsOf(sequence));

    /// <summary>The steps in the order they were set, by a step report or restored by an exit.</summary>
    public List<WorkingStep> Steps() =>
        [.. Events.Where(e => e.Name is ProgressEvent.StepChanged or ProgressEvent.ScopeExited).Select(e => e.Step)];

    /// <summary>The named working steps in the order they were reported, without the zeros and exit restorations.</summary>
    public List<WorkingStep> NamedSteps() =>
        [.. Events.Where(e => e.Name == ProgressEvent.StepChanged && e.Step != 0).Select(e => e.Step)];

    /// <summary>The event list, one per line, for a failure message.</summary>
    public string Describe() => string.Join("\n  ", Events);

    /// <summary>
    /// Asserts D-141's ordering guarantees over the recorded effects: (1) a pass's start precedes its
    /// bytes, objects, closure and end; (2) every started pass ends exactly once, before the next start
    /// and before the completion; (3) sequences are 1, 2, 3, ... with no gap; (4) the completion, when
    /// recorded, is last and recorded once; (7) no step is set between a pass's start and its closure
    /// or exit; (8) no closure, and so no post-input step, follows a non-end-of-input end of the same
    /// pass; every pass has a kind. When <paramref name="observedOpens"/> is given, it must equal the
    /// number of passes started: an open made outside every scope would record no pass.
    /// </summary>
    public void AssertGrammar(bool completed = true, int? observedOpens = null)
    {
        var events = Events;
        var context = "\n  " + string.Join("\n  ", events);
        var started = new HashSet<int>();
        var ended = new Dictionary<int, PassEnd>();
        var closed = new HashSet<int>();
        var exited = new HashSet<int>();
        var expectedSequence = 1;
        var current = 0;
        var currentOpen = false;

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Name)
            {
                case ProgressEvent.PassStarted:
                    Assert.True(e.Sequence == expectedSequence, $"(3) pass {e.Sequence} started where {expectedSequence} was due:{context}");
                    Assert.True(e.Kind != 0, $"pass {e.Sequence} has no kind:{context}");
                    if (current > 0)
                    {
                        Assert.True(ended.ContainsKey(current), $"(2) pass {current} has no end before pass {e.Sequence} started:{context}");
                    }

                    started.Add(e.Sequence);
                    current = e.Sequence;
                    currentOpen = true;
                    expectedSequence++;
                    break;

                case ProgressEvent.Consumed or ProgressEvent.Emitted:
                    Assert.True(started.Contains(e.Sequence), $"(1) {e} precedes its pass's start:{context}");
                    break;

                case ProgressEvent.Closed:
                    Assert.True(started.Contains(e.Sequence), $"(1) {e} precedes its pass's start:{context}");
                    Assert.True(closed.Add(e.Sequence), $"pass {e.Sequence} closed twice:{context}");
                    Assert.False(
                        ended.TryGetValue(e.Sequence, out var before) && before != PassEnd.EndOfInput,
                        $"(8) pass {e.Sequence} closed after its {before} end:{context}");
                    if (e.Sequence == current)
                    {
                        currentOpen = false;
                    }

                    break;

                case ProgressEvent.PassEnded:
                    Assert.True(started.Contains(e.Sequence), $"(1) {e} precedes its pass's start:{context}");
                    Assert.True(ended.TryAdd(e.Sequence, e.End), $"(2) pass {e.Sequence} ended twice:{context}");
                    break;

                case ProgressEvent.ScopeExited:
                    if (e.Sequence != 0)
                    {
                        Assert.True(started.Contains(e.Sequence), $"(1) {e} precedes its pass's start:{context}");
                        Assert.True(exited.Add(e.Sequence), $"pass {e.Sequence} exited twice:{context}");
                        if (e.End != 0)
                        {
                            Assert.True(ended.TryAdd(e.Sequence, e.End), $"(2) pass {e.Sequence} ended twice:{context}");
                        }

                        Assert.True(ended.ContainsKey(e.Sequence), $"(2) pass {e.Sequence}'s scope exited with no end:{context}");
                        if (e.Sequence == current)
                        {
                            currentOpen = false;
                        }
                    }

                    break;

                case ProgressEvent.StepChanged:
                    Assert.False(currentOpen, $"(7) {e} was set while pass {current}'s input was open:{context}");
                    break;

                case ProgressEvent.Completed:
                    Assert.True(i == events.Count - 1, $"(4) the completion is not the last effect:{context}");
                    break;
            }
        }

        // Without a completion the last pass may still be running; every other pass has ended.
        foreach (var sequence in started)
        {
            if (completed || sequence != current)
            {
                Assert.True(ended.ContainsKey(sequence), $"(2) pass {sequence} has no end:{context}");
            }
        }

        Assert.Equal(completed, events.Count > 0 && events[^1].Name == ProgressEvent.Completed);
        Assert.True(events.Count(e => e.Name == ProgressEvent.Completed) <= 1, $"(4) completed more than once:{context}");

        if (observedOpens is { } opens)
        {
            Assert.True(opens == started.Count, $"{opens} observed open(s) but {started.Count} pass(es) recorded:{context}");
        }
    }

    public void PassStarted(int sequence, PassKind kind, long? length) =>
        Add(new(ProgressEvent.PassStarted, sequence, kind, length));

    public void Consumed(int sequence, long total) => Add(new(ProgressEvent.Consumed, sequence, Value: total));

    public void Emitted(int sequence, long count) => Add(new(ProgressEvent.Emitted, sequence, Value: count));

    public void Closed(int sequence, bool endOfInput) => Add(new(ProgressEvent.Closed, sequence, EndOfInput: endOfInput));

    public void PassEnded(int sequence, PassEnd reason) => Add(new(ProgressEvent.PassEnded, sequence, End: reason));

    public void Step(WorkingStep step) => Add(new(ProgressEvent.StepChanged, Step: step));

    public void ScopeExited(int sequence, PassEnd end, WorkingStep step) =>
        Add(new(ProgressEvent.ScopeExited, sequence, End: end, Step: step));

    public void Completed() => Add(new(ProgressEvent.Completed));

    private void Add(ProgressEvent effect)
    {
        lock (_gate)
        {
            _events.Add(effect);
        }
    }
}
