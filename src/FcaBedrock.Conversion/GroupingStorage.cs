using System.Text;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion;

/// <summary>
/// The kind of spool operation a storage failure arose from — an <b>application-defined</b>
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
/// A small, stable classification of <i>why</i> a spool operation failed — mapped from the runtime
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
/// The internal, in-path (halting) grouping storage failure — thrown only from stream advancement
/// (never from disposal). It is recorded into the per-enumeration <see cref="GroupingReports"/> ledger as
/// an <b>Error</b> at its first-occurrence position (by the site that detects it, before any cleanup that
/// its unwinding triggers), and additionally thrown so the emitter halts — the emitter catches it only to
/// stop, not to record. Never public (P-14): storage failures cross the seam as
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
/// is application-authored and deterministic: identity, count, and up to three path samples in
/// first-occurrence order. Shared by the emitter's per-enumeration flush (the single-pass <c>.dat</c>
/// path) and the replay session's cross-pass final flush.
/// </summary>
internal static class GroupingStorageDiagnostics
{
    public static BedrockDiagnostic Render(GroupingStorageFailure failure, DiagnosticSeverity severity)
    {
        var message = new StringBuilder()
            .Append("Grouping spool storage failure (")
            .Append(failure.Operation)
            .Append('/')
            .Append(failure.Kind)
            .Append(") affected ")
            .Append(failure.Count)
            .Append(failure.Count == 1 ? " operation" : " operations");

        if (failure.PathSamples.Count > 0)
        {
            message.Append(" (e.g. ").AppendJoin(", ", failure.PathSamples).Append(')');
        }

        message.Append(" — the conversion used external sort-merge spool storage (§16.4, D-082).");

        return new BedrockDiagnostic(
            DiagnosticCode.GroupingStorageFailed, severity, message.ToString(), Location: null, Context: failure);
    }
}
