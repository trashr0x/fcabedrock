namespace FcaBedrock.Orchestration;

/// <summary>
/// The progress of one owned operation (D-139 parts 8 and 9, D-141). The host creates one per owned
/// operation, passes it to the calls that start a chain (<see cref="RunPreparation"/> and
/// <see cref="SpecPublication.Preflight(SpecDelivery, OrchestrationHost, OperationProgress, CancellationToken)"/>),
/// and reads <see cref="Current"/> at its own rate. The prepared run and the publication handle a
/// chain produces carry it into the chain's later calls. Orchestration records into it; no host code
/// runs on the operation's threads.
/// <para>
/// <b>A presentation channel, never a fact channel.</b> No outcome, check result, committed file,
/// cleanup state or cancellation effect is read from it (D-140 part 14): those come from the
/// operation's outcome, the publication handle's attempt record and the exception a call throws.
/// </para>
/// <para>
/// <b>Cancellation.</b> A requested cancellation has no effect a host may present until the call
/// returns or throws, and a call returns only after the library's own cleanup for it has run; until
/// then the snapshot keeps showing the last pass and step. Cancellation is observed at the points the
/// code already has (D-141 part 9), no latency is promised and no blocked read is interrupted.
/// </para>
/// <para>
/// One progress belongs to one owned operation, whose calls are sequential. <see cref="Current"/> may
/// be read, and <see cref="Complete"/> called, from any thread. Passing one progress to two concurrent
/// operations is a host defect the recorder cannot detect.
/// </para>
/// </summary>
public sealed class OperationProgress
{
    // The structural fields are written and read together under the private lock, so one reader sees
    // one instant. Nothing inside the lock waits, calls out or performs I/O, and only Complete
    // allocates there. The lock is never entered while it is held, so the recursion failure of the
    // runtime's lock is unreachable (D-141 part 6).
    private readonly Lock _gate = new();
    private int _sequence;
    private PassKind _kind;
    private long? _length;
    private PassEnd _end;

    // The current pass's facts that decide its end: whether its scope exited, and whether its wrapper
    // observed end of input. Reset when a pass starts.
    private bool _exited;
    private bool _endOfInput;

    // The counters and the step are single volatile writes with no lock, so a report from a read, an
    // emitted object, a catch or a finally cannot stall on a reader or throw. They reset under the
    // lock when a pass starts, and only the current pass's producer writes them afterwards.
    private long _bytes;
    private long _objects;
    private int _step;

    // Latched once by Complete; every later read of Current returns this same instance.
    private ProgressSnapshot? _final;

    /// <summary>A progress with no pass, no step and no completion.</summary>
    public OperationProgress()
    {
    }

    /// <summary>
    /// The current state, complete and immutable; safe from any thread at any time. Before
    /// <see cref="Complete"/>, each read returns a new snapshot. After <see cref="Complete"/> has
    /// returned, every read returns the final snapshot it built: the same instance, which nothing
    /// changes.
    /// </summary>
    public ProgressSnapshot Current
    {
        get
        {
            if (Volatile.Read(ref _final) is { } final)
            {
                return final;
            }

            int sequence;
            PassKind kind;
            long? length;
            PassEnd end;
            long bytes;
            long objects;
            int step;
            lock (_gate)
            {
                if (_final is { } latched)
                {
                    return latched;
                }

                sequence = _sequence;
                kind = _kind;
                length = _length;
                end = _end;
                bytes = Volatile.Read(ref _bytes);
                objects = Volatile.Read(ref _objects);
                step = Volatile.Read(ref _step);
            }

            return new ProgressSnapshot(sequence, kind, length, bytes, objects, end, (WorkingStep)step, isCompleted: false);
        }
    }

    /// <summary>
    /// Declares the owned operation terminal. The owner calls it after its last coordinator call has
    /// returned or thrown and it has bound that call's facts. It builds and keeps the final snapshot,
    /// and is idempotent. It schedules nothing and raises nothing: a host that renders reads
    /// <see cref="Current"/> once more after its operation's terminal continuation. A call given, or
    /// carrying, a completed progress throws <see cref="ArgumentException"/> before any work.
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_final is not null)
            {
                return;
            }

            Volatile.Write(
                ref _final,
                new ProgressSnapshot(
                    _sequence,
                    _kind,
                    _length,
                    Volatile.Read(ref _bytes),
                    Volatile.Read(ref _objects),
                    _end,
                    (WorkingStep)Volatile.Read(ref _step),
                    isCompleted: true));
        }

        Events?.Completed();
    }

    /// <summary>
    /// The test sink: it receives each recorded effect, in order, so a friend test assembly can assert
    /// the exact sequence. Production never sets it, so no code outside Orchestration runs on an
    /// operation's threads there. An init member rather than a constructor parameter, because the
    /// public surface lists every constructor.
    /// </summary>
    internal IProgressEvents? Events { get; init; }

    /// <summary>True once <see cref="Complete"/> latched the final snapshot.</summary>
    internal bool IsCompleted => Volatile.Read(ref _final) is not null;

    /// <summary>The step currently reported: a scope's outer step, captured when the scope begins.</summary>
    internal WorkingStep CurrentStep => (WorkingStep)Volatile.Read(ref _step);

    /// <summary>
    /// Refuses new work for a completed progress: an <see cref="ArgumentException"/> naming
    /// <paramref name="parameterName"/>, the argument that gave or carried it. A null progress is
    /// allowed and means that the call is not observed.
    /// </summary>
    internal static void ThrowIfCompleted(OperationProgress? progress, string parameterName)
    {
        if (progress is { IsCompleted: true })
        {
            throw new ArgumentException(
                "The operation progress is completed: its owner declared the operation terminal, so it cannot observe further work.",
                parameterName);
        }
    }

    /// <summary>
    /// A pass opened under a scope: it ends the previous pass first when that pass has no end (end of
    /// input when its wrapper observed it, otherwise closed before end of input), then starts the next
    /// sequence with this kind and length, its counters, end and step at 0. Returns the new sequence,
    /// or 0 after completion, which makes every later event of that pass stale.
    /// </summary>
    internal int PassStarted(PassKind kind, long? length)
    {
        int previous;
        PassEnd previousEnd = 0;
        int sequence;
        lock (_gate)
        {
            if (_final is not null)
            {
                return 0;
            }

            previous = _sequence;
            if (previous > 0 && _end == 0)
            {
                previousEnd = _endOfInput ? PassEnd.EndOfInput : PassEnd.ClosedBeforeEndOfInput;
            }

            sequence = previous + 1;
            Volatile.Write(ref _sequence, sequence);
            _kind = kind;
            _length = length;
            _end = 0;
            Volatile.Write(ref _exited, false);
            Volatile.Write(ref _endOfInput, false);
            Volatile.Write(ref _bytes, 0);
            Volatile.Write(ref _objects, 0);
            Volatile.Write(ref _step, 0);
        }

        if (Events is { } sink)
        {
            if (previousEnd != 0)
            {
                sink.PassEnded(previous, previousEnd);
            }

            sink.PassStarted(sequence, kind, length);
        }

        return sequence;
    }

    /// <summary>The bytes pass <paramref name="sequence"/> has consumed so far; no effect unless it is the current pass.</summary>
    internal void Consumed(int sequence, long total)
    {
        if (!IsCurrent(sequence))
        {
            return;
        }

        Volatile.Write(ref _bytes, total);
        if (Events is { } sink && !IsCompleted)
        {
            sink.Consumed(sequence, total);
        }
    }

    /// <summary>The objects pass <paramref name="sequence"/>'s enumeration has yielded so far; no effect unless it is the current pass.</summary>
    internal void Emitted(int sequence, long count)
    {
        if (!IsCurrent(sequence))
        {
            return;
        }

        Volatile.Write(ref _objects, count);
        if (Events is { } sink && !IsCompleted)
        {
            sink.Emitted(sequence, count);
        }
    }

    /// <summary>Pass <paramref name="sequence"/>'s wrapper observed end of input; no effect unless it is the current pass.</summary>
    internal void EndOfInput(int sequence)
    {
        if (IsCurrent(sequence))
        {
            Volatile.Write(ref _endOfInput, true);
        }
    }

    /// <summary>
    /// Pass <paramref name="sequence"/>'s input closed: its end is recorded now only when its wrapper
    /// observed end of input, and <paramref name="postInputStep"/>, when set, names the work that runs
    /// after the input closed. No effect unless the pass is current and its scope has not exited, so a
    /// leaked wrapper disposed late can neither end a newer pass nor set a step that outlives its
    /// call.
    /// </summary>
    internal void Closed(int sequence, WorkingStep postInputStep)
    {
        if (!IsCurrent(sequence) || Volatile.Read(ref _exited))
        {
            return;
        }

        var endOfInput = Volatile.Read(ref _endOfInput);
        var recorded = false;
        if (endOfInput)
        {
            lock (_gate)
            {
                if (_final is not null || sequence != _sequence || _exited)
                {
                    return;
                }

                if (_end == 0)
                {
                    _end = PassEnd.EndOfInput;
                    recorded = true;
                }
            }
        }

        if (postInputStep != 0)
        {
            Volatile.Write(ref _step, (int)postInputStep);
        }

        if (Events is { } sink && !IsCompleted)
        {
            sink.Closed(sequence, endOfInput);
            if (recorded)
            {
                sink.PassEnded(sequence, PassEnd.EndOfInput);
            }

            if (postInputStep != 0)
            {
                sink.Step(postInputStep);
            }
        }
    }

    /// <summary>
    /// A scope's exit, one locked event: when the scope started pass <paramref name="sequence"/>, that
    /// pass is current and has no end yet, the end is recorded (end of input when its wrapper observed
    /// it, otherwise <paramref name="pendingReason"/>); then <paramref name="outerStep"/> is restored.
    /// A scope that started no pass (sequence 0) records no end and only restores its outer step. An
    /// exit for a pass that is no longer current has no effect.
    /// </summary>
    internal void ScopeExit(int sequence, PassEnd pendingReason, WorkingStep outerStep)
    {
        PassEnd recorded = 0;
        lock (_gate)
        {
            if (_final is not null)
            {
                return;
            }

            if (sequence != 0)
            {
                if (sequence != _sequence || _exited)
                {
                    return;
                }

                if (_end == 0)
                {
                    _end = _endOfInput ? PassEnd.EndOfInput : pendingReason;
                    recorded = _end;
                }

                Volatile.Write(ref _exited, true);
            }

            Volatile.Write(ref _step, (int)outerStep);
        }

        Events?.ScopeExited(sequence, recorded, outerStep);
    }

    /// <summary>A working step from the coordinator or the publication engine: one volatile write, no lock.</summary>
    internal void Step(WorkingStep step)
    {
        Volatile.Write(ref _step, (int)step);
        if (Events is { } sink && !IsCompleted)
        {
            sink.Step(step);
        }
    }

    // Sequence 0 is "no pass": an event that names it never changes anything.
    private bool IsCurrent(int sequence) => sequence != 0 && sequence == Volatile.Read(ref _sequence);
}

/// <summary>
/// The test sink's vocabulary: the effects an <see cref="OperationProgress"/> recorded, in the order it
/// recorded them, and nothing it ignored. Only friend test assemblies implement it; production never
/// sets a sink (D-141).
/// <para>
/// A pass's start implies its counters, its end and the step at 0, and is preceded by
/// <see cref="PassEnded"/> for the previous pass when that pass had no end. A closure is
/// <see cref="Closed"/>, then <see cref="PassEnded"/> when it recorded end of input, then
/// <see cref="Step"/> when it named a post-input step. A scope's exit is one
/// <see cref="ScopeExited"/> carrying the end it recorded, if any, and the outer step it restored.
/// </para>
/// </summary>
internal interface IProgressEvents
{
    /// <summary>Pass <paramref name="sequence"/> started with this kind and length.</summary>
    void PassStarted(int sequence, PassKind kind, long? length);

    /// <summary>Pass <paramref name="sequence"/> has consumed <paramref name="total"/> bytes.</summary>
    void Consumed(int sequence, long total);

    /// <summary>Pass <paramref name="sequence"/>'s enumeration has yielded <paramref name="count"/> objects.</summary>
    void Emitted(int sequence, long count);

    /// <summary>Pass <paramref name="sequence"/>'s input closed, with or without end of input.</summary>
    void Closed(int sequence, bool endOfInput);

    /// <summary>Pass <paramref name="sequence"/> ended with <paramref name="reason"/>, at its closure or at the next pass's start.</summary>
    void PassEnded(int sequence, PassEnd reason);

    /// <summary>The step became <paramref name="step"/>.</summary>
    void Step(WorkingStep step);

    /// <summary>
    /// A scope exited: it recorded <paramref name="end"/> for pass <paramref name="sequence"/> (0 when
    /// it recorded none, and sequence 0 when it started no pass) and restored <paramref name="step"/>.
    /// </summary>
    void ScopeExited(int sequence, PassEnd end, WorkingStep step);

    /// <summary>The owner completed the progress.</summary>
    void Completed();
}
