namespace FcaBedrock.Orchestration;

/// <summary>
/// One moment of an owned operation's progress, for presentation only (D-141). The pass fields, the
/// end, the step and the completion flag come from one instant, and the two counters belong to that
/// same pass.
/// <para>
/// <see cref="PassEnd"/> 0 means that no end reason is recorded yet. A step set means that step runs,
/// and it takes display precedence over byte progress. <see cref="Step"/> is 0 while a pass's input is
/// open, so when a pass exists (<see cref="PassSequence"/> above 0), its <see cref="PassEnd"/> is 0
/// and the step is its post-input step, <see cref="WorkingStep.Calibrating"/> or
/// <see cref="WorkingStep.Grouping"/>, that pass's input has closed, the post-input work runs and the
/// end reason is pending. No other step says that an input closed: a delivery that reads no data
/// reports <see cref="WorkingStep.PreparingOutput"/> and the publication steps with no pass at all. A
/// pass with no end and no step has its input open or, after a closure with no post-input step, its
/// consumer finishing. <see cref="PassEnd"/> set, or no pass yet, with no step is the operation
/// working between reported phases.
/// </para>
/// <para>
/// Nothing here is a fact about the operation's result: no outcome, check result, committed file,
/// cleanup state or cancellation effect is read from a snapshot (D-140 part 14).
/// </para>
/// </summary>
public sealed record ProgressSnapshot
{
    internal ProgressSnapshot(
        int passSequence,
        PassKind passKind,
        long? passLength,
        long bytesConsumed,
        long objectsEmitted,
        PassEnd passEnd,
        WorkingStep step,
        bool isCompleted)
    {
        PassSequence = passSequence;
        PassKind = passKind;
        PassLength = passLength;
        BytesConsumed = bytesConsumed;
        ObjectsEmitted = objectsEmitted;
        PassEnd = passEnd;
        Step = step;
        IsCompleted = isCompleted;
    }

    /// <summary>The current or last pass's sequence, from 1 across the operation's calls; 0 before the first pass.</summary>
    public int PassSequence { get; }

    /// <summary>What the coordinator opened the current or last pass for; 0 before the first pass.</summary>
    public PassKind PassKind { get; }

    /// <summary>
    /// The opened stream's length, read once when the pass opened and only when the stream reported
    /// that it can seek; null otherwise. A display denominator, never a promise of how many bytes the
    /// pass reads.
    /// </summary>
    public long? PassLength { get; }

    /// <summary>The bytes the hashing wrapper handed to the reader in this pass.</summary>
    public long BytesConsumed { get; }

    /// <summary>
    /// The objects the enumeration that reads this pass has yielded; 0 for schema and calibration
    /// passes. A grouped enumeration keeps counting after its pass's input closed.
    /// </summary>
    public long ObjectsEmitted { get; }

    /// <summary>
    /// How the current or last pass ended; 0 while no end reason is recorded: its input is open, or it
    /// closed before end of input and the reason is decided when its consumer exits.
    /// </summary>
    public PassEnd PassEnd { get; }

    /// <summary>The working step in progress; 0 when none is named, and always 0 while a pass's input is open.</summary>
    public WorkingStep Step { get; }

    /// <summary>True once the owner declared the operation terminal; the snapshot is then final.</summary>
    public bool IsCompleted { get; }
}

/// <summary>What the coordinator opened a data pass for. Values start at 1, so 0 is no pass.</summary>
public enum PassKind
{
    /// <summary>The schema read: the header, or the first non-blank record.</summary>
    Schema = 1,

    /// <summary>A calibration pass; an unordered count-sensitive triple calibration makes two.</summary>
    Calibration = 2,

    /// <summary>The <c>.cxt</c> writer's first pass, the object names.</summary>
    CxtNames = 3,

    /// <summary>The <c>.cxt</c> writer's second pass, the incidence rows.</summary>
    CxtRows = 4,

    /// <summary>The <c>.dat</c> writer's pass.</summary>
    Dat = 5,

    /// <summary>The statistics counting pass.</summary>
    Statistics = 6,
}

/// <summary>How a data pass ended. Values start at 1, so 0 is no end recorded yet.</summary>
public enum PassEnd
{
    /// <summary>
    /// The hashing wrapper observed a zero-length result for a non-empty read: the pass's bytes were
    /// consumed to the end. Recorded when the input closes and never revised. It never says that the
    /// operation succeeded or that a digest matched.
    /// </summary>
    EndOfInput = 1,

    /// <summary>The input closed before end of input, and no exception reached the scope that owned the pass.</summary>
    ClosedBeforeEndOfInput = 2,

    /// <summary>Before the pass had ended, an exception other than the call's own cancellation reached the scope that owned it.</summary>
    Failed = 3,

    /// <summary>Before the pass had ended, an operation-cancelled exception carrying the call's own token reached the scope that owned it.</summary>
    Cancelled = 4,
}

/// <summary>An indeterminate step of an owned operation's work. Values start at 1, so 0 is no step.</summary>
public enum WorkingStep
{
    /// <summary>The calibrator runs after a calibration pass's input closed: merging, replaying, finishing or unwinding.</summary>
    Calibrating = 1,

    /// <summary>
    /// The grouped enumeration of an emit or statistics pass runs after that pass's input closed:
    /// sorting, merging, replaying or unwinding.
    /// </summary>
    Grouping = 2,

    /// <summary>
    /// A publication preflight: the operand paths, recovery of earlier residue, and the collision,
    /// directory and existing-target checks.
    /// </summary>
    PreparingOutput = 3,

    /// <summary>A publish after its check before beginning: the record and the staged files, outside a pass.</summary>
    Staging = 4,

    /// <summary>Publishing the identity evidence and the staged marker.</summary>
    Sealing = 5,

    /// <summary>The commit renames and the committed marker.</summary>
    Committing = 6,

    /// <summary>
    /// Removing this attempt's private files: a rollback whose rolling-back marker was made, or the
    /// cleanup after a commit whose committed-marker step returned.
    /// </summary>
    Cleaning = 7,
}
