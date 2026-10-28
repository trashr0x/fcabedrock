using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration;

/// <summary>
/// A publication location after preflight: residue recovered, collisions and existing targets
/// judged, live references held (D-125). Owned by the caller from the moment it is returned, until
/// it is published or disposed (D-140).
/// <para>
/// <b>Single use.</b> A publish spends the handle from the moment it has validated its arguments,
/// on every path after that. A second publish throws <see cref="InvalidOperationException"/>, and
/// a publish after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>; neither
/// changes <see cref="Attempt"/>. An argument or association failure throws before any mutation
/// and leaves the handle usable.
/// </para>
/// <para>
/// <b>Release.</b> A publish releases every reference the handle holds before it returns or
/// throws, so disposing afterwards is a no-op. Disposing a handle that was never published
/// releases its references and creates or deletes nothing: no record, stage or marker exists,
/// because no transaction began. Rolling back a begun, uncommitted transaction is a separate step
/// that the publish performs itself; disposal never stands in for it.
/// </para>
/// <para>
/// A closed family with two members, one per publication family; a handle belongs to one
/// operation and is never used concurrently, and <see cref="Dispose"/> is never called while a
/// publish of the same handle runs.
/// </para>
/// </summary>
public abstract class PreflightedPublication : IDisposable
{
    private static readonly PublicationAttempt NotBegunAttempt =
        new(PublicationAttemptState.NotBegun, RollbackOutcome.NotOwed, rollbackBlocker: null, committed: null);

    private readonly PublicationTransaction _transaction;
    private PublicationAttempt _attempt = NotBegunAttempt;
    private bool _spent;
    private bool _disposed;
    private bool _recorded;

    private protected PreflightedPublication(PublicationTransaction transaction)
    {
        _transaction = transaction;
    }

    /// <summary>
    /// The attempt's facts: the fixed <see cref="PublicationAttemptState.NotBegun"/> record until a
    /// publish of this handle exits, then that publish's final record, built once and never replaced,
    /// disposal included. Read it after the publish task has completed, normally or with an
    /// exception: a publish that throws after its commit point completes no awaited assignment, so a
    /// host binds committed facts from here on every exit, never from the awaited result alone.
    /// </summary>
    public PublicationAttempt Attempt => _attempt;

    /// <summary>
    /// The progress this handle's transaction carries, or null; read without spending the handle, so a
    /// publish can refuse a completed progress before it spends (D-141).
    /// </summary>
    internal OperationProgress? Progress => _transaction.Progress;

    /// <summary>Releases the references a never-published handle holds; idempotent; writes nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transaction.Dispose();
    }

    /// <summary>
    /// Spends the handle for a publish whose arguments are already validated, and hands the
    /// publish its transaction.
    /// </summary>
    internal PublicationTransaction Spend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_spent)
        {
            throw new InvalidOperationException("A preflighted publication can be published only once.");
        }

        _spent = true;
        return _transaction;
    }

    /// <summary>
    /// Builds the attempt record from the transaction's flags and provisional facts and stores it,
    /// once: a later call returns the record already stored.
    /// </summary>
    internal PublicationAttempt Record()
    {
        if (!_recorded)
        {
            _attempt = Build(_transaction);
            _recorded = true;
        }

        return _attempt;
    }

    /// <summary>
    /// Ends the publish: builds the record when the exit has not, then releases the transaction's
    /// references. Called last, in the publish's <c>finally</c>, after any rollback the exit
    /// performed.
    /// </summary>
    internal void Finish()
    {
        Record();
        _transaction.Dispose();
    }

    // The state follows the transaction's flags, never a method's result. Building is
    // conservative: a field reports an outcome only when the step that produced it ran, so a
    // begun attempt whose rollback recorded nothing reports NotStarted, and a committed attempt
    // whose cleanup recorded nothing reports NotStarted too.
    private static PublicationAttempt Build(PublicationTransaction transaction)
    {
        if (transaction.Committed)
        {
            var cleanup = transaction.CleanupProgress ?? CleanupOutcome.NotStarted;
            var incomplete = cleanup == CleanupOutcome.Incomplete;
            var published = new Published(
                transaction.CommittedFiles ?? [],
                cleanup,
                incomplete ? transaction.CleanupBlocker : null,
                incomplete ? transaction.CleanupWarning : null);
            return new PublicationAttempt(
                PublicationAttemptState.Committed, RollbackOutcome.NotOwed, rollbackBlocker: null, published);
        }

        if (transaction.Begun)
        {
            var rollback = transaction.RollbackProgress ?? RollbackOutcome.NotStarted;
            return new PublicationAttempt(
                PublicationAttemptState.BegunNotCommitted,
                rollback,
                rollback == RollbackOutcome.Incomplete ? transaction.RollbackBlocker : null,
                committed: null);
        }

        return NotBegunAttempt;
    }
}

/// <summary>A preflighted single-file publication: the one target a spec file is written to.</summary>
public sealed class PreflightedSpecFile : PreflightedPublication
{
    internal PreflightedSpecFile(PublicationTransaction transaction)
        : base(transaction)
    {
    }
}

/// <summary>A preflighted artifacts publication, bound to the prepared run it was preflighted for.</summary>
public sealed class PreflightedConvertOutput : PreflightedPublication
{
    internal PreflightedConvertOutput(
        PublicationTransaction transaction, PreparedFileRun run, bool writesCxt, bool writesDat, bool writesManifest)
        : base(transaction)
    {
        Run = run;
        WritesCxt = writesCxt;
        WritesDat = writesDat;
        WritesManifest = writesManifest;
    }

    /// <summary>The run this handle was preflighted for, compared by reference at publish.</summary>
    internal PreparedFileRun Run { get; }

    internal bool WritesCxt { get; }

    internal bool WritesDat { get; }

    internal bool WritesManifest { get; }
}

/// <summary>
/// One publication attempt's facts, built once when its publish exits (D-140). It describes this
/// attempt only: a preflight may first have recovered an earlier transaction's residue, and
/// "completed" means the routine reported completion, not that the location is byte for byte as
/// it was when the call began. Nothing here is reported that the step producing it did not report:
/// "interrupted" is never "completed", and "unknown" is never "restored".
/// </summary>
public sealed record PublicationAttempt
{
    internal PublicationAttempt(
        PublicationAttemptState state, RollbackOutcome rollback, CleanupFact? rollbackBlocker, Published? committed)
    {
        State = state;
        Rollback = rollback;
        RollbackBlocker = rollbackBlocker;
        Committed = committed;
    }

    /// <summary>How far the transaction got, from its own flags.</summary>
    public PublicationAttemptState State { get; }

    /// <summary>What the rollback did: <see cref="RollbackOutcome.NotOwed"/> unless <see cref="State"/> is <see cref="PublicationAttemptState.BegunNotCommitted"/>.</summary>
    public RollbackOutcome Rollback { get; }

    /// <summary>The first rollback step that fell short; only with <see cref="RollbackOutcome.Incomplete"/>, where the pass recorded one.</summary>
    public CleanupFact? RollbackBlocker { get; }

    /// <summary>The committed publication; present exactly when <see cref="State"/> is <see cref="PublicationAttemptState.Committed"/>.</summary>
    public Published? Committed { get; }
}

/// <summary>How far a publication attempt got. Values start at 1, so the default value 0 is no state.</summary>
public enum PublicationAttemptState
{
    /// <summary>
    /// No transaction began: the handle was never published, the publish stopped before
    /// <c>Begin</c>, or <c>Begin</c> failed or threw before it published the record. This claims
    /// nothing about residue a failed begin left; the next publication at that location classifies it.
    /// </summary>
    NotBegun = 1,

    /// <summary>The record was published and the commit point was not reached; the publish rolled back.</summary>
    BegunNotCommitted = 2,

    /// <summary>The commit point was crossed: every selected file stands, whatever its cleanup did (§16.2, D-125).</summary>
    Committed = 3,
}

/// <summary>What a rollback did. Values start at 1, so the default value 0 is no outcome.</summary>
public enum RollbackOutcome
{
    /// <summary>No rollback was owed: the attempt never began, or it committed.</summary>
    NotOwed = 1,

    /// <summary>No rollback cleanup began: the rolling-back marker could not be made, or making it raised a fault.</summary>
    NotStarted = 2,

    /// <summary>The rollback pass began and an exception ended it; what it restored is unknown.</summary>
    Interrupted = 3,

    /// <summary>The pass reported itself incomplete.</summary>
    Incomplete = 4,

    /// <summary>The pass reported itself complete.</summary>
    Completed = 5,
}

/// <summary>What a committed publication's cleanup did. Values start at 1, so the default value 0 is no outcome.</summary>
public enum CleanupOutcome
{
    /// <summary>The files stand and no cleanup began: making the committed marker raised a fault.</summary>
    NotStarted = 1,

    /// <summary>An exception ended the cleanup pass; no warning is recorded.</summary>
    Interrupted = 2,

    /// <summary>The pass reported itself incomplete; its first unfinished step and the existing warning are recorded.</summary>
    Incomplete = 3,

    /// <summary>The pass reported itself complete.</summary>
    Completed = 4,
}

/// <summary>A committed publication: it stands, whatever its cleanup did (§16.2, D-125).</summary>
public sealed record Published
{
    internal Published(
        IReadOnlyList<PublishedFile> files, CleanupOutcome cleanup, CleanupFact? cleanupBlocker, string? cleanupWarning)
    {
        Files = OutcomeLists.Snapshot(files);
        Cleanup = cleanup;
        CleanupBlocker = cleanupBlocker;
        CleanupWarning = cleanupWarning;
    }

    /// <summary>The committed files, an immutable snapshot in commit order: <c>.cxt</c>, <c>.dat</c>, manifest; or the one spec file.</summary>
    public IReadOnlyList<PublishedFile> Files { get; }

    /// <summary>What the post-commit cleanup did.</summary>
    public CleanupOutcome Cleanup { get; }

    /// <summary>The first unfinished cleanup step; only with <see cref="CleanupOutcome.Incomplete"/>, where the pass recorded one.</summary>
    public CleanupFact? CleanupBlocker { get; }

    /// <summary>The existing cleanup warning text; present exactly when <see cref="Cleanup"/> is <see cref="CleanupOutcome.Incomplete"/>.</summary>
    public string? CleanupWarning { get; }
}

/// <summary>One committed file.</summary>
public sealed record PublishedFile
{
    internal PublishedFile(PublishedFileKind kind, string spelling, string fullPath, string hash, FileIdentityKey identity)
    {
        Kind = kind;
        Spelling = spelling;
        FullPath = fullPath;
        Hash = hash;
        Identity = identity;
    }

    /// <summary>What the file is.</summary>
    public PublishedFileKind Kind { get; }

    /// <summary>The spelling §15 records.</summary>
    public string Spelling { get; }

    /// <summary>The full path it was committed at.</summary>
    public string FullPath { get; }

    /// <summary><c>sha256:</c> over the staged bytes, which the committed object holds.</summary>
    public string Hash { get; }

    /// <summary>
    /// The committed object's OS identity, from the reference the transaction held at commit. Never
    /// absent: a stage whose identity the host cannot report is refused before it is written.
    /// </summary>
    public FileIdentityKey Identity { get; }
}

/// <summary>What a committed file is.</summary>
public enum PublishedFileKind
{
    /// <summary>The Burmeister <c>.cxt</c> context.</summary>
    Cxt,

    /// <summary>The FIMI <c>.dat</c> context.</summary>
    Dat,

    /// <summary>The run manifest.</summary>
    Manifest,

    /// <summary>The one spec file of a single-file publication.</summary>
    SpecFile,
}
