using System.Collections.Immutable;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration;

/// <summary>
/// An ordinary host or environment failure, without a diagnostic code (D-122 part 2): one member of
/// a closed set of conditions, each produced by one existing message builder.
/// <para>
/// <see cref="Message"/> is the existing sanitized plain text, byte for byte, and some messages
/// name CLI options because they are the CLI's byte-locked text. A host that presents its own
/// wording classifies by <see cref="Kind"/> and reads the structured fields, never the message. A
/// host that shows the message escapes it once.
/// </para>
/// </summary>
public sealed record HostError
{
    internal HostError(
        HostErrorKind kind,
        string message,
        string? subject = null,
        string? counterpart = null,
        PublicationCause? cause = null,
        CleanupFact? blocker = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Kind = kind;
        Message = message;
        Subject = subject;
        Counterpart = counterpart;
        Cause = cause;
        Blocker = blocker;
    }

    /// <summary>The condition, one per existing message builder.</summary>
    public HostErrorKind Kind { get; }

    /// <summary>The existing sanitized message, byte for byte.</summary>
    public string Message { get; }

    /// <summary>The spelling the message names first: an operand, a target or an output base; absent where none is named.</summary>
    public string? Subject { get; }

    /// <summary>The second spelling a collision or a relocation names: the input, the second output or the authored reference.</summary>
    public string? Counterpart { get; }

    /// <summary>
    /// The cause the failed publication step established, where it established one; never read
    /// from a message or from a later look at the directory.
    /// </summary>
    public PublicationCause? Cause { get; }

    /// <summary>A recovery refusal's first unfinished cleanup step, where the recovery recorded one.</summary>
    public CleanupFact? Blocker { get; }
}

/// <summary>
/// The closed set of host-failure conditions, one member per existing message builder. Values
/// start at 1, so the default value 0 names no condition and no outcome carries it.
/// </summary>
public enum HostErrorKind
{
    /// <summary>The temporary-directory value is not a usable path; reported before any file is opened.</summary>
    TemporaryDirectoryInvalid = 1,

    /// <summary>The root spec could not be read or decoded. Subject: the spec operand.</summary>
    SpecUnreadable = 2,

    /// <summary>The data file could not be read. Subject: the data operand.</summary>
    DataUnreadable = 3,

    /// <summary>The data file changed between passes. Subject: the data operand.</summary>
    DataChanged = 4,

    /// <summary>The base is on another drive or share, so no relative reference reaches it. Subject: the destination; counterpart: the authored reference.</summary>
    RelocationOtherRoot = 5,

    /// <summary>No reference to the base could be verified from the destination's folder. Subject: the destination; counterpart: the authored reference.</summary>
    RelocationUnverified = 6,

    /// <summary>
    /// An existing target without replacement authorized, reported after the target's own residue
    /// was recovered and after the input-collision and directory checks. Subject: the target.
    /// </summary>
    TargetOccupied = 7,

    /// <summary>An earlier run's manifest a manifest-less run would leave beside its outputs. Subject: the manifest.</summary>
    StaleManifest = 8,

    /// <summary>A directory occupies a target. Subject: the target.</summary>
    TargetIsDirectory = 9,

    /// <summary>A target the run would replace is read-only. Subject: the target; cause: <see cref="PublicationCause.ReadOnly"/>.</summary>
    TargetReadOnly = 10,

    /// <summary>An earlier manifest the run would remove is read-only. Subject: the manifest; cause: <see cref="PublicationCause.ReadOnly"/>.</summary>
    ManifestReadOnly = 11,

    /// <summary>An output is the same file as an input. Subject: the output; counterpart: the input.</summary>
    InputCollision = 12,

    /// <summary>Two outputs are the same file. Subject: the first output; counterpart: the second.</summary>
    OutputCollision = 13,

    /// <summary>Unrecognized transaction residue was left untouched. Subject: the output base.</summary>
    UnknownResidue = 14,

    /// <summary>An earlier incomplete run could not be cleaned up. Subject: the output base; blocker: its first unfinished step, where recorded.</summary>
    RecoveryFailed = 15,

    /// <summary>
    /// Publication could not be started or a control file could not be made, the text saying
    /// "cannot start publication". Subject: the output base; cause: a missing or refusing folder
    /// where the failed step established it.
    /// </summary>
    PublicationControlFailed = 16,

    /// <summary>A staged output could not be written. Subject: the target.</summary>
    StageFailed = 17,

    /// <summary>An output could not be published. Subject: the target; cause: where the failed rename established it.</summary>
    CommitFailed = 18,
}

/// <summary>
/// The host failure of an operation that also carries the run's diagnostics: one code-less
/// <see cref="HostError"/> (D-122 part 2) and the run's complete diagnostic list at the moment it
/// failed, in library order (D-140 part 4). A fact record: only Orchestration builds one, and
/// <c>with</c> can only clone an instance an operation returned. Record equality compares the list
/// by reference, and no contract relies on it.
/// </summary>
public sealed record HostFailure
{
    internal HostFailure(HostError error, IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Error = error;
        Diagnostics = OutcomeLists.Snapshot(diagnostics);
    }

    /// <summary>The failure.</summary>
    public HostError Error { get; }

    /// <summary>
    /// The run's complete diagnostic list when the failure occurred, never sorted, grouped or
    /// deduplicated; an immutable snapshot that a change to the list passed in cannot reach.
    /// </summary>
    public IReadOnlyList<BedrockDiagnostic> Diagnostics { get; }
}

// The one place an outcome or a fact record turns a caller's list into the immutable snapshot it
// keeps (the ImmutableArray-backed precedent of D-098): a list that is already such a snapshot is
// kept as it is, and anything else is copied.
internal static class OutcomeLists
{
    public static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> items) =>
        items is ImmutableArray<T> ? items : items.ToImmutableArray();
}

// What a pure operation returns (D-140 part 4): a completion always carries its value, and every
// list is the complete run list as an immutable snapshot. The class constraint keeps the
// value-presence rule true: a value-type payload needs its own rule before it can use this.
internal static class PureOutcome<T>
    where T : class
{
    public static Diagnosed<T> Completed(T value, IReadOnlyList<BedrockDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Diagnosed<T>(value, OutcomeLists.Snapshot(diagnostics));
    }

    public static Diagnosed<T> Failed(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        Diagnosed<T>.Failed(OutcomeLists.Snapshot(diagnostics));
}

// What a mixed operation returns: the pure carrier inside Ok, or the host failure with the list
// collected so far.
internal static class MixedOutcome<T>
    where T : class
{
    public static Result<Diagnosed<T>, HostFailure> Completed(T value, IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        Result<Diagnosed<T>, HostFailure>.Ok(PureOutcome<T>.Completed(value, diagnostics));

    public static Result<Diagnosed<T>, HostFailure> Failed(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        Result<Diagnosed<T>, HostFailure>.Ok(PureOutcome<T>.Failed(diagnostics));

    public static Result<Diagnosed<T>, HostFailure> HostFailed(HostError error, IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        Result<Diagnosed<T>, HostFailure>.Err(new HostFailure(error, diagnostics));
}
