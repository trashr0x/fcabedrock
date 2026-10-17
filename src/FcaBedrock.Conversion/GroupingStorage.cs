using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion;

/// <summary>
/// The kind of spool operation a storage failure arose from: an <b>application-defined</b>
/// classification (D-082). Combined with <see cref="SpoolFailureKind"/> it forms the stable logical
/// identity of a grouping storage failure; runtime exception types and localized OS text are
/// supporting context only and never split or merge these.
/// </summary>
internal enum GroupingOperation
{
    /// <summary>Creating the per-enumeration spool workspace directory.</summary>
    Workspace,

    /// <summary>Writing an initial sorted run during intake spill.</summary>
    Spill,

    /// <summary>Reading a run during a merge stage.</summary>
    MergeRead,

    /// <summary>Writing an intermediate merged run.</summary>
    MergeWrite,

    /// <summary>Deleting a consumed run or tearing the workspace down (cleanup class).</summary>
    CleanupDelete,

    /// <summary>Closing a merge reader / disposing an opened run stream during cleanup (cleanup class).</summary>
    CleanupClose,
}

/// <summary>
/// A small, stable classification of <i>why</i> a spool operation failed, mapped from the runtime
/// exception where needed (D-082). Distinct conditions stay distinct (disk-full vs. corrupt framing,
/// both surfacing as <see cref="IOException"/>) and one condition stays one (delete-denied whether it
/// surfaces as <see cref="UnauthorizedAccessException"/> or <see cref="IOException"/>).
/// </summary>
internal enum SpoolFailureKind
{
    /// <summary>The filesystem denied access (create/open/delete).</summary>
    AccessDenied,

    /// <summary>The storage medium is full / quota exceeded.</summary>
    StorageExhausted,

    /// <summary>A run's intra-record framing is inconsistent (safely-identifiable corruption).</summary>
    CorruptRun,

    /// <summary>A run ended before a record it claimed (truncated).</summary>
    TruncatedRun,

    /// <summary>A consumed run or the workspace could not be deleted.</summary>
    DeleteFailed,

    /// <summary>The owner-restricted workspace directory could not be established.</summary>
    WorkspaceCreation,

    /// <summary>An otherwise-unclassified storage failure.</summary>
    Other,
}

/// <summary>
/// The structured payload carried on a <see cref="DiagnosticCode.GroupingStorageFailed"/> diagnostic's
/// <see cref="BedrockDiagnostic.Context"/> (D-082). Its stable logical identity is
/// <c>(Operation, Kind)</c>; <see cref="Count"/> and <see cref="PathSamples"/> are the bounded
/// aggregate. The random per-enumeration workspace path is <b>not</b> part of the identity; only up to
/// three path samples are retained for display, in first-occurrence order.
/// </summary>
internal sealed record GroupingStorageFailure(
    GroupingOperation Operation,
    SpoolFailureKind Kind,
    long Count,
    IReadOnlyList<string> PathSamples);

/// <summary>
/// The internal, in-path (halting) grouping storage failure, thrown only while a grouping stream
/// advances or a calibration step (intake, merge, replay, release) runs, never from disposal or
/// teardown. It is recorded into the per-enumeration <see cref="GroupingReports"/> ledger as an
/// <b>Error</b> at its first-occurrence position (by the site that detects it, before any cleanup that
/// its unwinding triggers), and also thrown so the emitter or calibration run halts; each catches it
/// only to stop, not to record. Never public (EP-14): storage failures cross the seam as
/// <see cref="DiagnosticCode.GroupingStorageFailed"/> diagnostics, not exceptions.
/// </summary>
internal sealed class GroupingStorageException : Exception
{
    public GroupingStorageException(GroupingOperation operation, SpoolFailureKind kind, string? pathSample, string message, Exception? inner = null)
        : base(message, inner)
    {
        Operation = operation;
        Kind = kind;
        PathSample = pathSample;
    }

    public GroupingOperation Operation { get; }

    public SpoolFailureKind Kind { get; }

    public string? PathSample { get; }
}

/// <summary>
/// Renders a <see cref="GroupingStorageFailure"/> aggregate into a single
/// <see cref="DiagnosticCode.GroupingStorageFailed"/> diagnostic (§16.4 / D-082). The rendered message
/// is application-authored and deterministic: what temporary storage was doing, whether the operation
/// stopped (Error) or continued past a cleanup failure (Warning), the cause the failure kind
/// establishes, then the identity, count, and up to three path samples in first-occurrence order.
/// Shared by the emitter's per-enumeration flush (the single-pass <c>.dat</c> path), the replay
/// session's cross-pass final flush, and the calibration run's completion.
/// <para>
/// The wording fits every caller (conversion, calibration, planning, statistics, fingerprinting), so it
/// names the operation, never a command. A stopping failure whose kind a different directory can
/// address ends with a remedy for the configured temporary directory; the remedy names no host option,
/// because a host spells that setting its own way. No cause is claimed beyond the kind: a workspace
/// that could not be created is not reported as a full or denied disk.
/// </para>
/// </summary>
internal static class GroupingStorageDiagnostics
{
    public static BedrockDiagnostic Render(GroupingStorageFailure failure, DiagnosticSeverity severity)
    {
        var stopped = severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;
        var message = new StringBuilder()
            .Append(stopped ? "Temporary storage failed while " : "Temporary storage cleanup failed while ")
            .Append(Activity(failure.Operation))
            .Append(stopped ? ", so the operation stopped: " : ": ")
            .Append(Cause(failure.Kind))
            .Append(stopped ? " (" : "; the operation continued, but some temporary storage may remain (")
            .Append(failure.Operation)
            .Append('/')
            .Append(failure.Kind)
            .Append(", ")
            .Append(failure.Count)
            .Append(failure.Count == 1 ? " occurrence" : " occurrences");

        if (failure.PathSamples.Count > 0)
        {
            message.Append(", e.g. ").AppendJoin(", ", failure.PathSamples);
        }

        message.Append("; §16.4, D-082).");

        if (stopped && Remedy(failure.Kind) is { } remedy)
        {
            message.Append(' ').Append(remedy);
        }

        return new BedrockDiagnostic(
            DiagnosticCode.GroupingStorageFailed, severity, message.ToString(), Location: null, Context: failure);
    }

    private static string Activity(GroupingOperation operation) => operation switch
    {
        GroupingOperation.Workspace => "creating its working directory",
        GroupingOperation.Spill => "writing sorted data",
        GroupingOperation.MergeRead => "reading sorted data back",
        GroupingOperation.MergeWrite => "writing merged data",
        GroupingOperation.CleanupDelete => "deleting files it no longer needed",
        GroupingOperation.CleanupClose => "closing files it no longer needed",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown grouping operation."),
    };

    private static string Cause(SpoolFailureKind kind) => kind switch
    {
        SpoolFailureKind.AccessDenied => "access was denied",
        SpoolFailureKind.StorageExhausted => "the storage was full",
        SpoolFailureKind.CorruptRun => "stored data was inconsistent",
        SpoolFailureKind.TruncatedRun => "stored data ended early",
        SpoolFailureKind.DeleteFailed => "files could not be deleted",
        SpoolFailureKind.WorkspaceCreation => "the directory could not be created",
        SpoolFailureKind.Other => "the cause is not known",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown spool failure kind."),
    };

    // Only the kinds a different or emptier temporary directory can address get a remedy; for
    // inconsistent or truncated stored data, or an unknown cause, there is no honest one to give.
    private static string? Remedy(SpoolFailureKind kind) => kind switch
    {
        SpoolFailureKind.WorkspaceCreation or SpoolFailureKind.AccessDenied =>
            "Choose an existing, writable directory for temporary storage.",
        SpoolFailureKind.StorageExhausted =>
            "Free space, or choose a directory with more free space for temporary storage.",
        _ => null,
    };
}
