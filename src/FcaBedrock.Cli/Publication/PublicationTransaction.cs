namespace FcaBedrock.Cli.Publication;

/// <summary>One file a run reads, for the canonical-identity collision check.</summary>
/// <param name="Spelling">How the invocation named it — the operand, or an authored <c>extends</c> reference.</param>
/// <param name="FullPath">Its resolved absolute path.</param>
internal sealed record PublicationInput(string Spelling, string FullPath);

/// <summary>
/// A validated intent descriptor: the control file whose exact canonical bytes authorize removing
/// exactly one pending record object — the one whose identity its name repeats.
/// </summary>
internal sealed record IntentClaim(
    string Token, string Path, string PendingPath, string PendingIdentity, TransactionRecord Described);

/// <summary>
/// A discovered prior transaction: what it owns, how far it durably got, and its evidence.
/// <para>
/// <c>StageClaims</c> maps a target file name to the identity digest its stage claim carries — the
/// durable statement that this transaction's create-new produced <em>that object</em> at that
/// private path, which a derived name alone can never say.
/// </para>
/// </summary>
internal sealed record DiscoveredTransaction(
    TransactionRecord Record,
    TransactionPhase Phase,
    bool Staged,
    IReadOnlyDictionary<string, StageEvidence> Evidence,
    IReadOnlyDictionary<string, string> StageClaims);

/// <summary>
/// Everything a discovery pass found in this base's private namespace: at most one prior
/// transaction, and at most one preparatory intent that never became one.
/// </summary>
internal sealed record DiscoveredResidue(DiscoveredTransaction? Prior, IntentClaim? Intent)
{
    /// <summary>A clean location.</summary>
    public static DiscoveredResidue None { get; } = new(null, null);

    /// <summary>True when nothing of this base's private namespace exists.</summary>
    public bool IsEmpty => Prior is null && Intent is null;
}

/// <summary>What preflight decided.</summary>
internal abstract record PublicationPreparation;

/// <summary>Preflight passed; the transaction may begin.</summary>
internal sealed record PublicationReady(PublicationTransaction Transaction) : PublicationPreparation;

/// <summary>Preflight refused; the message is a sanitized, code-less host error (exit 1).</summary>
internal sealed record PublicationRefused(string Message) : PublicationPreparation;

/// <summary>The sanitized reason a publication step failed.</summary>
internal sealed record PublicationFailure(string Message);

/// <summary>
/// The code-less host-error texts publication produces (D-122 part 2). None names an exception
/// type, a stack, a localized OS message, a transaction token, a private path, or an identity
/// key: every one is written from the run's own operands.
/// </summary>
internal static class PublicationMessages
{
    /// <summary>An existing selected target, without <c>--force</c>.</summary>
    public static string ExistingTarget(string spelling) =>
        $"the output '{spelling}' already exists; use --force to replace it.";

    /// <summary>An output that is canonically the same file as an input — refused even with <c>--force</c>.</summary>
    public static string InputCollision(string output, string input) =>
        $"the output '{output}' and the input '{input}' are the same file.";

    /// <summary>Two outputs that are canonically the same file — refused even with <c>--force</c>.</summary>
    public static string OutputCollision(string first, string second) =>
        $"the outputs '{first}' and '{second}' are the same file.";

    /// <summary>Residue claiming the private namespace that no valid transaction record accounts for.</summary>
    public static string UnknownResidue(string baseSpelling) =>
        $"the output base '{baseSpelling}' has unrecognized fcabedrock transaction residue; remove it and retry.";

    /// <summary>A prior incomplete run was found but could not be cleaned up.</summary>
    public static string RecoveryFailed(string baseSpelling) =>
        $"cannot clean up an incomplete fcabedrock run for the output base '{baseSpelling}'.";

    /// <summary>Publication could not be started — the location is unusable, or a control file refused to be created.</summary>
    public static string RecordFailed(string baseSpelling) =>
        $"cannot start publication for the output base '{baseSpelling}'.";

    /// <summary>A staged artifact could not be created, written, flushed, or closed.</summary>
    public static string StageFailed(string spelling) => $"cannot write the output '{spelling}'.";

    /// <summary>An artifact could not be committed.</summary>
    public static string CommitFailed(string spelling) => $"cannot publish the output '{spelling}'.";
}

/// <summary>
/// The staged publication transaction (D-122 parts 4–6, D-123 point 7).
/// <para>
/// <b>Nothing content-bearing is mutated unless the transaction can prove, at that instant, which
/// exact object it is about to touch.</b> That is the whole design in one sentence. A derived
/// private name predicts a <em>path</em>; only a successful create-new, a verified rename result,
/// or a durable identity digest says which <em>object</em> is at it. So the transaction record
/// names the stages and backups it owns; the <em>intent descriptor</em> names the one pending
/// record object that may precede it; each <em>stage claim</em> is written only <b>after</b> its
/// create-new succeeded and names the identity of the object that creation produced; and per-target
/// <em>identity evidence</em> names the exact filesystem objects — the staged one and the backed-up
/// one.
/// </para>
/// <para>
/// <b>A private name classifies and confines a control; it never proves one.</b> The descriptor,
/// the phase markers, and the stage claims are each authoritative only through their exact
/// canonical bytes (<see cref="ControlDocument"/>), which bind the run token, the base, the
/// authoritative record's digest, the exact role or phase, and — for a claim — the stage identity
/// it acknowledges. Discovery and removal both require those exact bytes, so an empty, partial,
/// refused, invalid, or substituted object at any of those names is preserved and authorizes no
/// mutation. This supersedes the earlier zero-byte mechanism.
/// </para>
/// <para>
/// <b>A refused acquisition creates nothing.</b> Where a create-new or a publishing rename is
/// refused, the occupant that refused it is exactly the object this transaction did <em>not</em>
/// create — so no durable state naming it is written, none is left behind by a failed withdrawal,
/// and neither this run's rollback nor any later recovery may remove it.
/// </para>
/// <para>
/// <b>Durable phase, not filesystem guesswork.</b> Recovery cannot read intent out of file
/// presence alone: immediately after the record is written, "no stage and a present target" means
/// a pre-existing file <c>--force</c> has not renamed aside yet, while after staging completes
/// the very same shape means a commit rename consumed the stage. The transaction therefore records
/// how far it got with create-new marker files, each holding the exact canonical bytes for its
/// phase: the transition happened when that body is there, and not otherwise.
/// </para>
/// <para>
/// <b>Preflight settles everything before anything new moves.</b> Residue is validated in full;
/// nothing recovery would touch may be an input; a single prior transaction is completed; and only
/// <em>then</em> are identities reacquired and the complete current-run collision, existing-target,
/// and control-path checks run — so a recovery that restores a target cannot slip an aliased output
/// past the one identity check. A participant this run would rename aside but
/// cannot identify is refused <em>there</em>, before a record, a stage, or one pass of conversion
/// work exists.
/// </para>
/// <para>
/// <b>Then: record, stage, seal, back up, commit.</b> Forced replacement renames each existing
/// target aside to a transaction-owned backup — a same-directory metadata rename, never a copy and
/// never a rehash — and an old public manifest marker is demoted <em>before</em> any artifact it
/// could certify is published, including when a <c>--no-manifest</c> run merely introduces one.
/// Commit is per-file non-overwriting atomic rename in canonical order, manifest
/// last, with the host token observed and both sides of every rename verified;
/// a rename whose result is not the object it moved is put straight
/// back, so no unowned file is ever left at a published path — least of all at the manifest, which
/// <em>is</em> the run's public commit marker. No cross-file atomicity is claimed.
/// </para>
/// </summary>
internal sealed class PublicationTransaction
{
    private readonly IPublicationFileSystem _files;
    private readonly Func<FileIdentity> _identityFactory;
    private readonly IReadOnlyList<PublicationInput> _inputs;
    private readonly string _directory;
    private readonly string _baseSpelling;
    private readonly string _baseFileName;
    private readonly string _token;
    private readonly TransactionRecord _record;
    private readonly IReadOnlyList<PublicationTarget> _finals;
    private readonly IReadOnlyDictionary<PublicationTargetKind, PublicationTarget> _targets;
    private readonly IReadOnlyDictionary<string, string> _preflightIdentity;
    private readonly Dictionary<PublicationTargetKind, string> _stageIdentity = [];
    private readonly Dictionary<string, string> _stageClaims = new(StringComparer.Ordinal);
    private readonly HashSet<TransactionPhase> _marked = [];
    private readonly Dictionary<string, StageEvidence> _evidence = new(StringComparer.Ordinal);
    private readonly Dictionary<PublicationTargetKind, string> _hashes = [];

    private string? _intentName;
    private string? _pendingIdentity;
    private bool _begun;
    private bool _staged;
    private bool _rolledBack;
    private bool _hazard;

    private PublicationTransaction(
        IPublicationFileSystem files,
        Func<FileIdentity> identityFactory,
        IReadOnlyList<PublicationInput> inputs,
        string directory,
        string baseSpelling,
        string baseFileName,
        string token,
        TransactionRecord record,
        IReadOnlyList<PublicationTarget> finals,
        IReadOnlyDictionary<PublicationTargetKind, PublicationTarget> targets,
        IReadOnlyDictionary<string, string> preflightIdentity)
    {
        _files = files;
        _identityFactory = identityFactory;
        _inputs = inputs;
        _directory = directory;
        _baseSpelling = baseSpelling;
        _baseFileName = baseFileName;
        _token = token;
        _record = record;
        _finals = finals;
        _targets = targets;
        _preflightIdentity = preflightIdentity;
    }

    /// <summary>True once every requested artifact — the manifest last, when written — is committed.</summary>
    public bool Committed { get; private set; }

    /// <summary>
    /// Runs the complete preflight for <paramref name="baseOperand"/> and, when it passes,
    /// produces the transaction that will publish <paramref name="finalKinds"/>.
    /// </summary>
    /// <param name="files">The injected filesystem.</param>
    /// <param name="identityFactory">
    /// Produces the shared filesystem-identity service. It is a factory rather than an
    /// instance because identity is memoized per path — including the canonical-path fallback for a
    /// path that does not exist — so every check that must see the location as it is <b>now</b>
    /// takes a fresh service.
    /// </param>
    /// <param name="baseOperand">The verbatim <c>--out BASE</c> operand.</param>
    /// <param name="finalKinds">The selected artifacts, in canonical order.</param>
    /// <param name="inputs">Every file this run reads: DATA, the root spec, and each loaded base.</param>
    /// <param name="force">Whether <c>--force</c> authorizes replacing an existing distinct target.</param>
    /// <param name="cancellation">The exact host token.</param>
    public static PublicationPreparation Preflight(
        IPublicationFileSystem files,
        Func<FileIdentity> identityFactory,
        string baseOperand,
        IReadOnlyList<PublicationTargetKind> finalKinds,
        IReadOnlyList<PublicationInput> inputs,
        bool force,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(identityFactory);
        ArgumentNullException.ThrowIfNull(baseOperand);
        ArgumentNullException.ThrowIfNull(finalKinds);
        ArgumentNullException.ThrowIfNull(inputs);

        try
        {
            return Inspect(files, identityFactory, baseOperand, finalKinds, inputs, force, cancellation);
        }
        catch (Exception exception) when (FailureFamily.IsPublicationFailure(exception))
        {
            // An unusable output LOCATION — an operand that is not a path at all, a missing or
            // unreadable directory. This is the one boundary where an ArgumentException or a
            // NotSupportedException genuinely describes the user's operand rather than an internal
            // defect, which is why the broad family is admitted only here.
            return new PublicationRefused(PublicationMessages.RecordFailed(baseOperand));
        }
    }

    /// <summary>
    /// The same preflight for one arbitrary file (D-122 part 4, D-123 point 7): the target is the
    /// <c>--out PATH</c> operand exactly as given, because
    /// <see cref="PublicationTargetKind.Single"/>'s extension is empty. The <b>same</b>
    /// transaction, record format, and recovery routine as <see cref="Preflight"/> — not a sibling
    /// protocol — differing only in the target set and the family it may complete.
    /// </summary>
    public static PublicationPreparation PreflightSingle(
        IPublicationFileSystem files, Func<FileIdentity> identityFactory, string outPath,
        IReadOnlyList<PublicationInput> inputs, bool force, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(identityFactory);
        ArgumentNullException.ThrowIfNull(outPath);
        ArgumentNullException.ThrowIfNull(inputs);

        try
        {
            return InspectSingle(files, identityFactory, outPath, inputs, force, cancellation);
        }
        catch (Exception exception) when (FailureFamily.IsPublicationFailure(exception))
        {
            // The same one boundary where the broad family describes the user's own operand.
            return new PublicationRefused(PublicationMessages.RecordFailed(outPath));
        }
    }

    /// <summary>
    /// Creates the transaction's root: the pending record, the <b>intent descriptor</b> that
    /// acknowledges it, and then the record itself.
    /// <para>
    /// <b>Acquisition first, acknowledgement second</b>. The pending record is
    /// created before anything names its path, so a create-new that is <em>refused</em> leaves no
    /// durable state at all and the occupant that refused it can never be removed by this run or
    /// any later one. Only once the object exists is the descriptor written, naming the identity
    /// digest of the object that creation produced. That is the only thing that authorizes removing
    /// a pending record: not its path, not the run token, not its length, but "this transaction
    /// created <b>that object</b> there".
    /// </para>
    /// <para>
    /// The descriptor is authoritative through its exact canonical bytes, not through existing: one
    /// whose body did not land whole names nothing and is preserved, and the run says so.
    /// </para>
    /// <para>
    /// <b>Atomic by construction.</b> The record's bytes are written, flushed, and closed under the
    /// pending name and the finished record is published with a single rename, so the discoverable
    /// record name never holds an empty or partial encoding.
    /// </para>
    /// <para>
    /// <b>The record is the root of every later authority, so its own publication is proved on both
    /// sides</b>. The pending object is re-proved immediately before the publishing
    /// rename, and the rename's result is proved to be that same object carrying those same bytes
    /// before the descriptor is dropped, before a stage exists, and before the transaction counts
    /// as begun.
    /// </para>
    /// </summary>
    public PublicationFailure? Begin()
    {
        var pending = PendingRecordPath;
        var bytes = _record.ToBytes();

        CreatedFile file;
        try
        {
            file = _files.CreateNew(pending);
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            // Refused. Nothing durable was written naming this path, and nothing will be: the
            // occupant is preserved by this run and by every later one.
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        var identity = IdentityEvidence.Of(_token, PublicationTargets.RecordRole, _baseFileName, file.Identity);
        var isPendingObject = IdentityIs(PublicationTargets.RecordRole, _baseFileName, identity);
        var isTheRecord = IdentityAndBytes(PublicationTargets.RecordRole, _baseFileName, identity, bytes);

        // A host that cannot identify what it just created cannot bind the record to it, so the
        // root of the transaction fails closed rather than publishing an authority it cannot prove.
        //
        // And it does not take the object back out either. Removal is bound to the
        // exact object the create returned; where the seam cannot name that object, there is no
        // proof to remove it by — and "it is zero bytes" is the same path-and-length guess this
        // protocol removed everywhere else. The pending record is left exactly as it is, which is
        // unacknowledged state under the fail-closed interval: nothing names it, so no run touches
        // it and every retry says the same thing.
        if (!IdentityEvidence.IsIdentity(identity))
        {
            CloseQuietly(file.Content);
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        // The acknowledgement. From here on the pending object is provably this run's, whatever
        // state its bytes are left in.
        _intentName = IntentNameOf(_baseFileName, _token, _record, identity);
        _pendingIdentity = identity;
        if (!CreateControlFile(Path.Combine(_directory, _intentName), IntentDocument))
        {
            CloseQuietly(file.Content);
            Remove(pending, isPendingObject);
            _intentName = null;
            _pendingIdentity = null;
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        // Deliberately NOT inside a broad catch: the stream operations are classified by the
        // narrow family inside WriteControl, so a contract defect on the already-open record
        // stream reaches the sanitized unexpected-fault exit instead of being reported as an
        // environment failure.
        if (!WriteControl(file.Content, bytes)
            || !TryMutate(() => _files.Move(pending, RecordPath)))
        {
            AbandonPending(pending, isPendingObject);
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        // The rename's RESULT. A different object substituted inside that boundary would otherwise
        // be accepted as this transaction's authority, published over, and finally deleted as owned.
        // It is put back where the rename took it from and nothing is begun.
        if (!MatchesObject(RecordPath, isTheRecord))
        {
            TryMutate(() => _files.Move(RecordPath, pending));
            AbandonIntent();
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        _begun = true;

        // The descriptor is consumed by the publish; if this fails the next run removes it as
        // part of the record's own owned set.
        AbandonIntent();
        return null;
    }

    /// <summary>
    /// Stages <paramref name="kind"/>: a create-new confidential sibling on the destination
    /// filesystem, written by <paramref name="write"/> through the inline hasher, flushed to disk,
    /// and only then finalized. The identity of the object created is captured from its own open
    /// handle — before a byte is written and before the writer is ever invoked — and is the
    /// evidence rollback and recovery later use to prove what this transaction
    /// published.
    /// <para>
    /// <b>Nothing durable names the stage path until the acquisition has succeeded.</b>
    /// A refused create-new therefore leaves the occupant that refused it with no
    /// claim, no evidence, and no other statement that this transaction created it — so neither
    /// this rollback nor any later recovery may remove it. Only once the object exists is the stage
    /// claim written, and its exact canonical bytes — token, base, record digest, this role with
    /// its target kind, and that object's identity — are what discovery and removal require. A
    /// claim that is empty, partial, or substituted proves nothing: it authorizes no mutation of
    /// the stage beside it and is itself preserved.
    /// </para>
    /// <para>
    /// <b>A stage this host cannot identify is refused before the writer runs</b>. Such
    /// a stage can never be proved at commit, so writing it — and then renaming every last-good
    /// output aside for a run that cannot possibly publish — is avoidable work and an avoidable
    /// outage.
    /// </para>
    /// <para>
    /// <b>Failure origin is preserved.</b> Creating, writing, flushing, or closing the stage is an
    /// <em>output</em> failure and becomes <see cref="PublicationMessages.StageFailed"/>; anything
    /// else <paramref name="write"/> raises — a source read failure, a cancellation, a contract
    /// defect — is rethrown untouched for the caller to classify.
    /// </para>
    /// </summary>
    public async Task<PublicationFailure?> StageAsync(PublicationTargetKind kind, Func<Stream, Task> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var target = _targets[kind];
        var stagePath = StagePath(target.FileName);

        CreatedFile stage;
        try
        {
            // Confidential: a stage holds converted user data and may survive a crash as recovery
            // residue, so it gets an actual access boundary rather than only an opaque name.
            stage = _files.CreateNewConfidential(stagePath);
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            // Refused. Nothing durable was written naming this path, and nothing will be: the
            // occupant is preserved by this run and by every later one.
            return new PublicationFailure(PublicationMessages.StageFailed(target.Spelling));
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        var digest = IdentityEvidence.Of(
            _token, PublicationTargets.StageRole, target.FileName, stage.Identity);

        if (!IdentityEvidence.IsIdentity(digest))
        {
            // Known unpublishable before one record is read or one byte is written —
            // so the writer is never invoked, DATA is never enumerated for it, and no target is
            // renamed aside.
            //
            // The stage is also left exactly where it is. Removal is bound to the
            // exact object the create returned, and this is precisely the host that cannot name
            // that object; "it is zero bytes" is a length, not a proof, and this protocol does not
            // reclaim on one. It stays beside its record as unacknowledged state under the
            // fail-closed interval. The message is the one Seal and Commit give for the same cause
            // — the host cannot prove what it would publish — rather than one about writing, which
            // is not what failed.
            CloseQuietly(stage.Content);
            return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
        }

        var claim = Path.Combine(
            _directory, PublicationTargets.StageClaimName(_baseFileName, kind, _token, digest));

        // The claim states, in BOTH its name and its body, the identity of the object this
        // acquisition produced. The name keeps it unpredictable; the body is what discovery and
        // removal require exactly, so an empty object that refuses this create-new — or one that
        // later replaces the claim outright — is neither believed as authority over the stage
        // beside it nor removed as this transaction's residue.
        if (!CreateControlFile(claim, ClaimDocument(kind, digest)))
        {
            CloseQuietly(stage.Content);
            Remove(stagePath, IdentityIs(PublicationTargets.StageRole, target.FileName, digest));
            return new PublicationFailure(PublicationMessages.StageFailed(target.Spelling));
        }

        // Acquisition succeeded and is now durably proved. A path whose create-new failed is never
        // recorded, so rollback and recovery never remove its occupant.
        _stageIdentity[kind] = digest;
        _stageClaims[target.FileName] = digest;

        var content = stage.Content;
        var hashing = new HashingWriteStream(content);
        var failed = false;
        try
        {
            // The exporter enumerates the SOURCE while writing the stage, so the two failure
            // origins meet here. Only the tagged one is an output failure; everything else —
            // a source read, a cancellation, a contract defect — belongs to the caller and is
            // rethrown untouched.
            try
            {
                await write(hashing).ConfigureAwait(false);
            }
            catch (PublicationStreamException)
            {
                failed = true;
            }

            if (!failed)
            {
                try
                {
                    _files.Flush(content);
                    _hashes[kind] = hashing.Complete();
                }
                catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
                {
                    failed = true;
                }
            }
        }
        catch
        {
            Close(hashing, content);
            throw;
        }

        // Closing is part of writing the artifact: a deferred failure surfacing here means the
        // stage is not trustworthy, so it fails the same way an earlier write would have.
        failed |= !Close(hashing, content);
        if (failed)
        {
            return new PublicationFailure(PublicationMessages.StageFailed(target.Spelling));
        }

        return PublishEvidence(kind, target.FileName)
            ? null
            : new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
    }

    /// <summary>
    /// Seals the transaction: publishes each target's durable identity evidence, then records that
    /// <b>every</b> stage is written and flushed.
    /// <para>
    /// The evidence is what makes ownership provable rather than inferred, so it must be durable
    /// before the staged marker — the fact that authorizes reading a missing stage as "a commit
    /// rename consumed it" — and therefore before any commit mutation. Each backup-bearing target
    /// is re-observed here and must still be the object preflight approved, and every staged target
    /// must carry usable stage identity: this runs after <see cref="Begin"/> and staging, so a
    /// failure rolls back this run's own private residue, but it is <em>before</em> any final
    /// target is touched, so the externally observable target set is unchanged and no residue is
    /// left.
    /// </para>
    /// </summary>
    public PublicationFailure? Seal(CancellationToken cancellation)
    {
        foreach (var targetFileName in _record.Targets)
        {
            cancellation.ThrowIfCancellationRequested();

            if (_preflightIdentity.TryGetValue(targetFileName, out var expected)
                && (!IdentityEvidence.IsIdentity(expected)
                    || !Matches(
                        Path.Combine(_directory, targetFileName),
                        PublicationTargets.BackupRole,
                        targetFileName,
                        expected)))
            {
                // The object about to be renamed aside must still be the one preflight approved,
                // and it must be provable at all: a host that cannot identify it cannot authorize
                // a forced replacement.
                return new PublicationFailure(PublicationMessages.CommitFailed(SpellingOf(targetFileName)));
            }

            // Every staged target already published its evidence as its stage closed; what remains
            // is a participant this run only demotes — the old manifest of a --no-manifest run.
            if (!_evidence.ContainsKey(targetFileName) && !PublishEvidence(KindOf(targetFileName), targetFileName))
            {
                return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
            }

            // Independent of the staging-time check, and taken from the durable evidence rather
            // than from memory: a selected target whose sealed evidence cannot name its staged
            // object must never reach the staged marker, let alone a backup rename.
            if (HasStageEntry(targetFileName)
                && (!_evidence.TryGetValue(targetFileName, out var sealedEvidence)
                    || !IdentityEvidence.IsIdentity(sealedEvidence.Stage)))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(SpellingOf(targetFileName)));
            }
        }

        cancellation.ThrowIfCancellationRequested();

        if (!Mark(TransactionPhase.Staged))
        {
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        _staged = true;
        return null;
    }

    /// <summary>The prefixed raw-bytes hash of the staged <paramref name="kind"/>.</summary>
    public string HashOf(PublicationTargetKind kind) => _hashes[kind];

    /// <summary>The verbatim invoked spelling of <paramref name="kind"/>'s target — what §15 records.</summary>
    public string Spelling(PublicationTargetKind kind) => _targets[kind].Spelling;

    /// <summary>
    /// Backs up every target the record claims — the demoted manifest first — commits each staged
    /// artifact by non-overwriting atomic rename in canonical order with the manifest last, and
    /// then, past the commit point, removes the backups, the evidence, the markers, and finally the
    /// record.
    /// <para>
    /// <paramref name="cancellation"/> is observed before <b>every</b> pre-commit transition and
    /// never after the last one: a signal that arrives while backups or earlier renames are still
    /// running must roll the run back and exit 3, while one that arrives after the commit point
    /// leaves the published run standing.
    /// </para>
    /// <para>
    /// <b>Every transition is verified on both sides, and a rename that produced the wrong object is
    /// undone.</b> A backup-bearing target must still be the very object preflight approved, and
    /// after it is renamed aside the backup must hold that same object. A target about
    /// to receive a stage must still be absent; the stage must still be the exact object this
    /// transaction created and sealed, and after the rename the published final must be that object.
    /// Where a rename's result is <em>not</em> what it moved, that exact object is
    /// renamed straight back: it is not this transaction's to delete, and leaving it at a published
    /// path would mean a failed run had put an unrelated file where its output belongs — at the
    /// manifest, the path that <em>is</em> the public commit marker, it would certify a run that
    /// never happened.
    /// </para>
    /// </summary>
    public PublicationFailure? Commit(CancellationToken cancellation)
    {
        foreach (var entry in BackupEntries)
        {
            cancellation.ThrowIfCancellationRequested();

            var targetPath = TransactionRecord.TargetPathOf(_directory, entry);
            var backupPath = _record.PathOf(_directory, entry);
            var spelling = SpellingOf(entry.TargetFileName);
            if (!_preflightIdentity.TryGetValue(entry.TargetFileName, out var expected)
                || !IdentityEvidence.IsIdentity(expected))
            {
                // Renaming aside is destructive and the object must be provable. A host that
                // supplies no identity cannot prove it, so a forced replacement fails closed rather
                // than moving a file it cannot recognize.
                return new PublicationFailure(PublicationMessages.CommitFailed(spelling));
            }

            if (!Matches(targetPath, PublicationTargets.BackupRole, entry.TargetFileName, expected))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(spelling));
            }

            if (!TryMutate(() => _files.Move(targetPath, backupPath)))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(spelling));
            }

            // The interval between the check and the rename is irreducible, so the rename's RESULT
            // is checked too — and, unlike before, its result is undone: the
            // object that actually moved is put back at the public path it came from, so a failed
            // forced replacement never leaves a user's file stranded under a private name.
            if (!Matches(backupPath, PublicationTargets.BackupRole, entry.TargetFileName, expected))
            {
                if (!TryMutate(() => _files.Move(backupPath, targetPath)))
                {
                    _hazard = true;
                }

                return new PublicationFailure(PublicationMessages.CommitFailed(spelling));
            }
        }

        foreach (var target in _finals)
        {
            cancellation.ThrowIfCancellationRequested();

            if (Exists(_files, target.FullPath))
            {
                // Something now occupies the destination that preflight found free — or freed by
                // its own backup rename. The non-overwriting rename would refuse it anyway; failing
                // here keeps the file untouched and leaves the reason exact.
                return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
            }

            // The stage must still be the exact object this transaction created, wrote, hashed, and
            // sealed. Renaming whatever occupies that path would publish bytes the manifest does
            // not describe — a successful run certifying a file it never wrote.
            var sealedStage = _evidence.TryGetValue(target.FileName, out var evidence) ? evidence.Stage : null;
            if (sealedStage is null
                || !IdentityEvidence.IsIdentity(sealedStage)
                || !Matches(StagePath(target.FileName), PublicationTargets.StageRole, target.FileName, sealedStage))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
            }

            if (!TryMutate(() => _files.Move(StagePath(target.FileName), target.FullPath)))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
            }

            // And the published final must be that same object before the next artifact — or the
            // manifest, last — can commit. If it is not, the object that landed there is put back
            // at the stage path it was taken from: preserved, out of the public namespace, and
            // recognizable to the rollback that follows.
            if (!Matches(target.FullPath, PublicationTargets.StageRole, target.FileName, sealedStage))
            {
                if (!TryMutate(() => _files.Move(target.FullPath, StagePath(target.FileName))))
                {
                    // The unowned object could not be moved off a published path. Nothing may
                    // erase the transaction's authority while it sits there.
                    _hazard = true;
                }

                return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
            }
        }

        // Past the commit point the run is public, so cleanup is best-effort and never downgrades
        // the outcome. Anything that survives stays owned by the record, which a later run
        // recovers as an already-committed transaction.
        Committed = true;
        Mark(TransactionPhase.Committed);
        Finish(View(), forward: true, Guard(CancellationToken.None), hazard: false);
        return null;
    }

    /// <summary>
    /// Undoes everything this transaction did. Rollback intent is made <b>durable first</b>, so a
    /// crash part-way through is resumed as a rollback by the next run rather than being
    /// reinterpreted as a commit from file presence; the work itself is the same
    /// idempotent routine recovery uses, so an in-process rollback and a resumed one cannot drift.
    /// </summary>
    public void Rollback()
    {
        // Single-shot, never after the commit point, and never for a transaction that never began:
        // a caller may roll back and then hit cancellation on the way out, and a second pass over
        // an already-restored location must not undo the restoration.
        if (!_begun || _rolledBack || Committed)
        {
            return;
        }

        _rolledBack = true;

        // The durable rollback phase GATES the destructive work; it is not an annotation on it.
        // Rolling back without it can leave every stage consumed and every final
        // populated — the shape a completed commit leaves — with `staged` as the only durable
        // fact, and the next run would then finish that failed run forward.
        //
        // Refusing to start is strictly safer: the location keeps its less advanced phase, every
        // owned file stays owned, and recovery under that phase reaches the same end state.
        if (!Mark(TransactionPhase.RollingBack))
        {
            return;
        }

        Finish(View(), forward: false, Guard(CancellationToken.None), _hazard);
    }

    private string RecordPath => Path.Combine(_directory, PublicationTargets.RecordName(_baseFileName, _token));

    private string PendingRecordPath =>
        Path.Combine(_directory, PublicationTargets.PendingRecordName(_baseFileName, _token));

    private IEnumerable<TransactionFileEntry> BackupEntries
    {
        get
        {
            foreach (var entry in _record.Files)
            {
                if (string.Equals(entry.Role, PublicationTargets.BackupRole, StringComparison.Ordinal))
                {
                    yield return entry;
                }
            }
        }
    }

    private static PublicationPreparation Inspect(
        IPublicationFileSystem files,
        Func<FileIdentity> identityFactory,
        string baseOperand,
        IReadOnlyList<PublicationTargetKind> finalKinds,
        IReadOnlyList<PublicationInput> inputs,
        bool force,
        CancellationToken cancellation)
    {
        var directory = PublicationTargets.Directory(baseOperand);
        var baseFileName = PublicationTargets.BaseFileName(baseOperand);

        var targets = new Dictionary<PublicationTargetKind, PublicationTarget>();
        foreach (var kind in PublicationTargets.CommitOrder)
        {
            targets[kind] = PublicationTargets.Target(baseOperand, kind);
        }

        var finals = new List<PublicationTarget>();
        var manifestIsFinal = false;
        foreach (var kind in finalKinds)
        {
            finals.Add(targets[kind]);
            manifestIsFinal |= kind == PublicationTargetKind.Manifest;
        }

        if (PrepareLocation(
                files, identityFactory, baseOperand, directory, baseFileName,
                PublicationFamily.Artifacts, targets, inputs, cancellation, out var identity) is { } refusal)
        {
            return refusal;
        }

        var manifest = targets[PublicationTargetKind.Manifest];

        // Any existing manifest is a marker hazard for a run that publishes artifacts it will not
        // describe: leaving it visible beside new output lets it appear to certify a file from a
        // different run, whether that output replaced something or is brand new (CX-M7H-005).
        var demotesManifest = !manifestIsFinal && Exists(files, manifest.FullPath);

        // A demoted marker is renamed aside and then deleted, so it is every bit as much a
        // mutated participant as a replaced artifact — and `--force` never authorizes destroying
        // an input. It joins the collision set BEFORE any of it happens (CX-M7H-013).
        var participants = new List<PublicationTarget>(finals);
        if (demotesManifest)
        {
            participants.Add(manifest);
        }

        if (Collisions(identity, participants, inputs) is { } participantCollision)
        {
            return new PublicationRefused(participantCollision);
        }

        if (demotesManifest && !force)
        {
            return new PublicationRefused(PublicationMessages.ExistingTarget(manifest.Spelling));
        }

        foreach (var target in finals)
        {
            if (Exists(files, target.FullPath) && !force)
            {
                return new PublicationRefused(PublicationMessages.ExistingTarget(target.Spelling));
            }
        }

        // Backups lead the record and the old manifest leads them, so the public marker is
        // demoted before any artifact it could certify is touched.
        var entries = new List<TransactionFileEntry>();
        if (demotesManifest || (manifestIsFinal && Exists(files, manifest.FullPath)))
        {
            entries.Add(new TransactionFileEntry(PublicationTargets.BackupRole, manifest.FileName));
        }

        foreach (var target in finals)
        {
            if (target.Kind != PublicationTargetKind.Manifest && Exists(files, target.FullPath))
            {
                entries.Add(new TransactionFileEntry(PublicationTargets.BackupRole, target.FileName));
            }
        }

        foreach (var target in finals)
        {
            entries.Add(new TransactionFileEntry(PublicationTargets.StageRole, target.FileName));
        }

        var token = PublicationTargets.NewToken();
        var record = TransactionRecord.Create(token, baseFileName, entries);

        // Every control path this transaction can ever own — its record, its markers, its evidence,
        // and its stages and backups — is a function of the base, the role, and the token, so the
        // complete set is resolvable here and is collision-checked before a single file is created.
        // (The stage claim is the one exception: its name carries the 128-bit identity digest of an
        // object that does not exist yet. It cannot be resolved in advance and, by the same
        // construction, cannot name a pre-existing file — and its own create-new refuses an occupant
        // rather than replacing it.)
        if (ControlCollision(identity, directory, record, token, participants, inputs))
        {
            return new PublicationRefused(PublicationMessages.RecordFailed(baseOperand));
        }

        // What each backup-bearing target IS, at the one moment the collision check approved it.
        var preflightIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryPreflightIdentity(identity, directory, token, entries, preflightIdentity, out var unprovable))
        {
            return new PublicationRefused(
                PublicationMessages.CommitFailed(SpellingOfName(unprovable, targets)));
        }

        cancellation.ThrowIfCancellationRequested();

        return new PublicationReady(new PublicationTransaction(
            files,
            identityFactory,
            inputs,
            directory,
            baseOperand,
            baseFileName,
            token,
            record,
            finals,
            targets,
            preflightIdentity));
    }

    // The single-file tail: one target, so no manifest to demote and no selection to make — the
    // record reserves one stage and, when something is there, the backup rollback restores from.
    private static PublicationPreparation InspectSingle(
        IPublicationFileSystem files, Func<FileIdentity> identityFactory, string outPath,
        IReadOnlyList<PublicationInput> inputs, bool force, CancellationToken cancellation)
    {
        var directory = PublicationTargets.Directory(outPath);
        var baseFileName = PublicationTargets.BaseFileName(outPath);
        var target = PublicationTargets.Target(outPath, PublicationTargetKind.Single);
        var targets = new Dictionary<PublicationTargetKind, PublicationTarget>
            { [PublicationTargetKind.Single] = target };
        var finals = new List<PublicationTarget> { target };
        if (PrepareLocation(
                files, identityFactory, outPath, directory, baseFileName,
                PublicationFamily.Single, targets, inputs, cancellation, out var identity) is { } refusal)
        {
            return refusal;
        }

        if (Collisions(identity, finals, inputs) is { } collision)
        {
            return new PublicationRefused(collision);
        }

        var exists = Exists(files, target.FullPath);
        if (exists && !force)
        {
            return new PublicationRefused(PublicationMessages.ExistingTarget(target.Spelling));
        }

        var entries = new List<TransactionFileEntry>();
        if (exists)
        {
            entries.Add(new TransactionFileEntry(PublicationTargets.BackupRole, target.FileName));
        }

        entries.Add(new TransactionFileEntry(PublicationTargets.StageRole, target.FileName));
        var token = PublicationTargets.NewToken();
        var record = TransactionRecord.Create(token, baseFileName, entries);
        if (ControlCollision(identity, directory, record, token, finals, inputs))
        {
            return new PublicationRefused(PublicationMessages.RecordFailed(outPath));
        }

        var preflightIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!TryPreflightIdentity(identity, directory, token, entries, preflightIdentity, out var unprovable))
        {
            return new PublicationRefused(PublicationMessages.CommitFailed(SpellingOfName(unprovable, targets)));
        }

        cancellation.ThrowIfCancellationRequested();
        return new PublicationReady(new PublicationTransaction(
            files, identityFactory, inputs, directory, outPath, baseFileName, token, record, finals,
            targets, preflightIdentity));
    }

    /// <summary>
    /// Everything both preflights do before they look at the location they will publish into:
    /// validate the residue, protect the run's own inputs from recovery, complete a prior
    /// transaction of <paramref name="expectedFamily"/>, and hand back a fresh identity service.
    /// Answers the refusal, or <see langword="null"/> when the location is ready.
    /// <b>The expected family is authority, not an inference:</b> at one exact base a convert and
    /// a single-file record are both well formed, and each names files the other command never
    /// asked to write, so a valid foreign record is refused as unknown residue before any state is
    /// assembled, any collision is evaluated, or <see cref="Recover"/> runs — preserved as found.
    /// </summary>
    private static PublicationPreparation? PrepareLocation(
        IPublicationFileSystem files, Func<FileIdentity> identityFactory, string baseOperand,
        string directory, string baseFileName, PublicationFamily expectedFamily,
        IReadOnlyDictionary<PublicationTargetKind, PublicationTarget> targets,
        IReadOnlyList<PublicationInput> inputs, CancellationToken cancellation,
        out FileIdentity identity)
    {
        cancellation.ThrowIfCancellationRequested();
        identity = identityFactory();
        // Residue is validated in full before a single byte moves, so a malformed record, an
        // impossible shape or state, a foreign family, or an unknown lookalike refuses the run with
        // the location exactly as found. Discovery takes its own identity service: residue it
        // cannot see cannot be recovered, and case variants alias on some directories (CX-M7H-016).
        if (!TryClassifyResidue(files, identityFactory, directory, baseFileName, expectedFamily, out var residue))
        {
            return new PublicationRefused(PublicationMessages.UnknownResidue(baseOperand));
        }

        cancellation.ThrowIfCancellationRequested();
        if (residue.IsEmpty)
        {
            return null;
        }

        // Nothing recovery would delete, move, or replace may be one of THIS run's inputs; the
        // fresh check later runs after recovery, too late to protect one it removes (CX-M7H-023).
        if (PriorCollision(files, identity, directory, baseOperand, residue, inputs, targets) is { } collision)
        {
            return new PublicationRefused(collision);
        }

        // A validated prior transaction is completed FIRST — it belongs to that run, not this one.
        if (!Recover(files, identityFactory, directory, residue, new RecoveryGuard(identityFactory, inputs, cancellation)))
        {
            return new PublicationRefused(PublicationMessages.RecoveryFailed(baseOperand));
        }

        // Recovery may have restored a target, and a memoized "does not exist" would still say so.
        identity = identityFactory();
        return null;
    }

    // What each backup-bearing target IS, at the one moment the collision check approved it. False
    // names the target whose identity the host could not supply: it can never be committed over,
    // and knowing that here is knowing it before any record, stage, or claim exists (CX-M7H-042).
    private static bool TryPreflightIdentity(
        FileIdentity identity, string directory, string token,
        IReadOnlyList<TransactionFileEntry> entries, Dictionary<string, string> preflightIdentity,
        out string unprovable)
    {
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.Role, PublicationTargets.BackupRole, StringComparison.Ordinal))
            {
                continue;
            }

            var evidence = IdentityEvidence.Of(
                token, PublicationTargets.BackupRole, entry.TargetFileName,
                identity.KeyFor(TransactionRecord.TargetPathOf(directory, entry)));

            if (!IdentityEvidence.IsIdentity(evidence))
            {
                unprovable = entry.TargetFileName;
                return false;
            }

            preflightIdentity[entry.TargetFileName] = evidence;
        }

        unprovable = string.Empty;
        return true;
    }

    private static string IntentNameOf(
        string baseFileName, string token, TransactionRecord record, string pendingIdentity) =>
        PublicationTargets.IntentName(
            baseFileName, token, record.ShapeCode, record.Digest, pendingIdentity);

    private string StagePath(string targetFileName) =>
        Path.Combine(_directory, PublicationTargets.PrivateName(targetFileName, PublicationTargets.StageRole, _token));

    private bool HasStageEntry(string targetFileName)
    {
        foreach (var entry in _record.Files)
        {
            if (string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal)
                && string.Equals(entry.TargetFileName, targetFileName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private PublicationTargetKind KindOf(string targetFileName)
    {
        foreach (var target in _targets.Values)
        {
            if (string.Equals(target.FileName, targetFileName, StringComparison.Ordinal))
            {
                return target.Kind;
            }
        }

        throw new InvalidOperationException("The record names a target this run does not compute.");
    }

    private TransactionView View() =>
        new(
            _files,
            _identityFactory,
            _directory,
            _record,
            _token,
            _staged,
            _evidence,
            _stageClaims,
            _intentName,
            _pendingIdentity,
            _marked);

    private RecoveryGuard Guard(CancellationToken cancellation = default) =>
        new(_identityFactory, _inputs, cancellation);

    /// <summary>
    /// The proof that an object is the one this transaction created in <paramref name="role"/> for
    /// <paramref name="targetFileName"/>. It is stated over the object — its identity and its
    /// bytes — rather than over a path, because that is what the removal primitive can observe
    /// through the very handle it deletes through (CX-M7H-040).
    /// </summary>
    private RemovalProof IdentityIs(string role, string targetFileName, string digest) =>
        (identity, _) => string.Equals(
            IdentityEvidence.Of(_token, role, targetFileName, identity), digest, StringComparison.Ordinal);

    private RemovalProof IdentityAndBytes(
        string role, string targetFileName, string digest, byte[] expected) =>
        (identity, bytes) => string.Equals(
                IdentityEvidence.Of(_token, role, targetFileName, identity), digest, StringComparison.Ordinal)
            && bytes is not null
            && bytes.AsSpan().SequenceEqual(expected);

    // A decision, not a removal: what IS at this path right now, taken fresh. Removal re-asks the
    // same question of the object it has open, which is the observation that actually authorizes
    // destroying it.
    private bool MatchesObject(string path, RemovalProof proof) =>
        proof(_identityFactory().KeyFor(path), ReadControl(_files, path));

    private bool Remove(string path, RemovalProof proof) => RemoveOwned(_files, path, proof, Guard());

    // A record publication that never completed. The pending object goes only when it is provably
    // the object this run created there — whatever state its bytes are in — so an occupant that
    // refused the create-new is preserved (CX-M7H-037).
    private void AbandonPending(string pending, RemovalProof isPendingObject)
    {
        Remove(pending, isPendingObject);
        AbandonIntent();
    }

    private void AbandonIntent()
    {
        var document = IntentDocument;
        if (_intentName is { } name
            && Remove(
                Path.Combine(_directory, name),
                (_, bytes) => bytes is not null && bytes.AsSpan().SequenceEqual(document)))
        {
            _intentName = null;
        }
    }

    /// <summary>
    /// Publishes one target's identity evidence: written under a pending name, then made
    /// authoritative by one non-overwriting rename, so the discoverable name never holds a partial
    /// encoding.
    /// <para>
    /// <b>Both sides of that rename are proved</b> (CX-M7H-038/044). A refused create-new leaves no
    /// evidence and no claim on the occupant; a rename that lands a different object at the
    /// authoritative name is undone rather than accepted, so cleanup can never remove an evidence
    /// file this transaction did not publish.
    /// </para>
    /// </summary>
    private bool PublishEvidence(PublicationTargetKind kind, string targetFileName)
    {
        var backup = _preflightIdentity.TryGetValue(targetFileName, out var expected)
            ? expected
            : IdentityEvidence.NotApplicable;

        var stage = _stageIdentity.TryGetValue(kind, out var digest) ? digest : IdentityEvidence.NotApplicable;

        var evidence = StageEvidence.Create(_token, _baseFileName, targetFileName, backup, stage);
        var bytes = evidence.ToBytes();
        var pending = Path.Combine(_directory, PublicationTargets.EvidencePendingName(_baseFileName, kind, _token));
        var published = Path.Combine(_directory, PublicationTargets.EvidenceName(_baseFileName, kind, _token));

        CreatedFile file;
        try
        {
            file = _files.CreateNew(pending);
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return false;
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        var identity = IdentityEvidence.Of(
            _token, PublicationTargets.EvidenceRole, targetFileName, file.Identity);

        // The object this creation produced, whatever state its bytes are left in — the proof that
        // removes it — and the finished document it must be before it may be published.
        var isThatObject = IdentityIs(PublicationTargets.EvidenceRole, targetFileName, identity);
        var isTheEvidence = IdentityAndBytes(PublicationTargets.EvidenceRole, targetFileName, identity, bytes);
        var written = WriteControl(file.Content, bytes);

        if (!written || !IdentityEvidence.IsIdentity(identity) || !MatchesObject(pending, isTheEvidence))
        {
            Remove(pending, isThatObject);
            return false;
        }

        if (!TryMutate(() => _files.Move(pending, published)))
        {
            Remove(pending, isThatObject);
            return false;
        }

        if (!MatchesObject(published, isTheEvidence))
        {
            TryMutate(() => _files.Move(published, pending));
            return false;
        }

        _evidence[targetFileName] = evidence;
        return true;
    }

    // Writes a control document to an already-open stream. The narrow predicate is deliberate: at
    // a write call an ArgumentException means the caller passed an invalid range and an
    // ObjectDisposedException means it wrote to a closed stream — contract defects that must reach
    // the sanitized unexpected-fault exit rather than be disguised as an environment failure
    // (CX-M7H-015/021).
    private bool WriteControl(Stream stream, byte[] bytes)
    {
        var failed = false;
        try
        {
            try
            {
                stream.Write(bytes, 0, bytes.Length);
                _files.Flush(stream);
            }
            catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
            {
                failed = true;
            }
            catch (Exception exception) when (FailureFamily.IsContractFault(exception))
            {
                throw new PublicationFaultException(exception);
            }
        }
        catch
        {
            CloseQuietly(stream);
            throw;
        }

        // Closed on every path: a handle still open holds the file, and the tidy-up that follows a
        // failure would then be unable to remove what it just created.
        return CloseControl(stream) && !failed;
    }

    private static bool CloseControl(Stream stream)
    {
        try
        {
            stream.Dispose();
            return true;
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return false;
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }
    }

    // Closing on the way out of a failure: the original exception is the one that matters, so a
    // deferred failure here — of either family — is absorbed rather than replacing it. Letting a
    // contract fault out of this path would overwrite a tagged fault with an untagged one, and the
    // host would then read it as its own writer failing (CX-M7H-034).
    private static void CloseQuietly(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception) || FailureFamily.IsContractFault(exception))
        {
        }
    }

    // Creates a phase marker. Best-effort by design for the phases where the LESS advanced
    // interpretation is the safe one; the rollback phase is not one of those, and its caller
    // treats a failure as "do not start" (CX-M7H-010). A marker whose create-new was refused is
    // NOT recorded, so cleanup never removes the occupant that refused it (CX-M7H-038).
    private bool Mark(TransactionPhase phase)
    {
        var path = Path.Combine(_directory, PublicationTargets.MarkerName(_baseFileName, phase, _token));
        if (!CreateControlFile(path, MarkerDocument(phase)))
        {
            return false;
        }

        _marked.Add(phase);
        return true;
    }

    private byte[] IntentDocument =>
        ControlDocument.Bytes(_token, _baseFileName, ControlDocument.IntentRole, _record.Digest);

    private byte[] MarkerDocument(TransactionPhase phase) =>
        ControlDocument.Bytes(_token, _baseFileName, ControlDocument.RoleOf(phase), _record.Digest);

    private byte[] ClaimDocument(PublicationTargetKind kind, string stageDigest) =>
        ControlDocument.Bytes(
            _token, _baseFileName, ControlDocument.ClaimRole(kind), _record.Digest, stageDigest);

    // A control file whose ROLE is its whole meaning — an intent descriptor, a phase marker, a
    // stage claim — created and then given the exact canonical bytes that make it authoritative;
    // its name only confines and classifies it. Acquisition failures are the environment family,
    // and failures on the already-open stream are judged the same way, so a contract defect at
    // either boundary still reaches the sanitized unexpected-fault exit (CX-M7H-021/041).
    //
    // A create-new that was REFUSED leaves nothing and touches nothing. One that succeeded and then
    // could not be made durable is this run's own object — proved by the identity its own creation
    // reported — so it is taken back out through the same identity-bound removal everything else
    // uses. That matters most for the stage claim and the intent descriptor, whose names carry a
    // runtime digest no later run can predict and which would otherwise strand the base.
    private bool CreateControlFile(string path, byte[] content)
    {
        CreatedFile file;
        try
        {
            file = _files.CreateNew(path);
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return false;
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        var mine = IdentityIs(
            PublicationTargets.EvidenceRole,
            Path.GetFileName(path),
            IdentityEvidence.Of(
                _token, PublicationTargets.EvidenceRole, Path.GetFileName(path), file.Identity));

        // Its bytes are what a later run proves it by, so a control whose body did not land whole
        // is not a control at all — it is taken back out here, and if even that cannot be done it is
        // preserved and the state stays classifiable.
        if (WriteControl(file.Content, content))
        {
            return true;
        }

        Remove(path, mine);
        return false;
    }

    private string SpellingOf(string targetFileName) => SpellingOfName(targetFileName, _targets);

    private static string SpellingOfName(
        string targetFileName, IReadOnlyDictionary<PublicationTargetKind, PublicationTarget> targets)
    {
        foreach (var target in targets.Values)
        {
            if (string.Equals(target.FileName, targetFileName, StringComparison.Ordinal))
            {
                return target.Spelling;
            }
        }

        return targetFileName;
    }

    // A fresh observation, never a memoized one: the question is what the file at this path IS
    // right now, and the answer must not be one taken before the last mutation (CX-M7H-024).
    private bool Matches(string path, string role, string targetFileName, string expected) =>
        IdentityEvidence.IsIdentity(expected)
        && string.Equals(
            IdentityEvidence.Of(_token, role, targetFileName, _identityFactory().KeyFor(path)),
            expected,
            StringComparison.Ordinal);

    // Closing is part of writing the artifact, so it is judged by the environment family: a
    // deferred I/O failure fails the stage, while a contract defect surfacing at close still
    // reaches the unexpected-fault exit.
    private static bool Close(HashingWriteStream hashing, Stream file)
    {
        hashing.Dispose();
        return CloseControl(file);
    }

    // Canonical identity, not path text: a hardlinked or symlinked alias of the input is the
    // input, and D-122 part 4 refuses it even with --force.
    private static string? Collisions(
        FileIdentity identity, IReadOnlyList<PublicationTarget> finals, IReadOnlyList<PublicationInput> inputs)
    {
        foreach (var target in finals)
        {
            var key = identity.KeyFor(target.FullPath);

            foreach (var input in inputs)
            {
                if (key == identity.KeyFor(input.FullPath))
                {
                    return PublicationMessages.InputCollision(target.Spelling, input.Spelling);
                }
            }
        }

        for (var i = 0; i < finals.Count; i++)
        {
            for (var j = i + 1; j < finals.Count; j++)
            {
                if (identity.KeyFor(finals[i].FullPath) == identity.KeyFor(finals[j].FullPath))
                {
                    return PublicationMessages.OutputCollision(finals[i].Spelling, finals[j].Spelling);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// True when any file this transaction will create or consume is the same file as one of the
    /// run's inputs, one of its targets, or another of its own control paths.
    /// </summary>
    private static bool ControlCollision(
        FileIdentity identity,
        string directory,
        TransactionRecord record,
        string token,
        IReadOnlyList<PublicationTarget> participants,
        IReadOnlyList<PublicationInput> inputs)
    {
        var control = ControlPaths(directory, record, token);
        var keys = new List<FileIdentityKey>(control.Count);
        foreach (var path in control)
        {
            var key = identity.KeyFor(path);

            foreach (var input in inputs)
            {
                if (key == identity.KeyFor(input.FullPath))
                {
                    return true;
                }
            }

            foreach (var participant in participants)
            {
                if (key == identity.KeyFor(participant.FullPath))
                {
                    return true;
                }
            }

            foreach (var seen in keys)
            {
                if (key == seen)
                {
                    return true;
                }
            }

            keys.Add(key);
        }

        return false;
    }

    /// <summary>
    /// Every private path one transaction owns whose name is a function of the base, the role, and
    /// the token — derived, never read from anywhere.
    /// <para>
    /// Two are deliberately absent: the intent descriptor and each stage claim carry, in their own
    /// names, the 128-bit identity digest of an object that does not exist until the acquisition
    /// they acknowledge has succeeded. They cannot be resolved in advance and, by the same
    /// construction, cannot name a pre-existing file; their own create-new refuses an occupant
    /// rather than replacing it, and a refusal writes nothing.
    /// </para>
    /// </summary>
    private static List<string> ControlPaths(string directory, TransactionRecord record, string token)
    {
        var paths = new List<string>
        {
            Path.Combine(directory, PublicationTargets.RecordName(record.BaseFileName, token)),
            Path.Combine(directory, PublicationTargets.PendingRecordName(record.BaseFileName, token)),
        };

        foreach (var phase in PublicationTargets.MarkedPhases)
        {
            paths.Add(Path.Combine(directory, PublicationTargets.MarkerName(record.BaseFileName, phase, token)));
        }

        foreach (var entry in record.Files)
        {
            paths.Add(record.PathOf(directory, entry));
        }

        foreach (var targetFileName in record.Targets)
        {
            var kind = KindOfTarget(targetFileName, record.BaseFileName);
            paths.Add(Path.Combine(directory, PublicationTargets.EvidenceName(record.BaseFileName, kind, token)));
            paths.Add(Path.Combine(directory, PublicationTargets.EvidencePendingName(record.BaseFileName, kind, token)));
        }

        return paths;
    }

    private static PublicationTargetKind KindOfTarget(string targetFileName, string baseFileName)
    {
        foreach (var kind in PublicationTargets.AllKinds)
        {
            if (string.Equals(targetFileName, baseFileName + PublicationTargets.Extension(kind), StringComparison.Ordinal))
            {
                return kind;
            }
        }

        throw new InvalidOperationException("A validated record named a target outside its own base.");
    }

    /// <summary>
    /// True when nothing a prior transaction's recovery would delete, move, or replace is one of
    /// this run's inputs (CX-M7H-023). Read-only: it mutates nothing and, on a match, the run
    /// refuses with the location exactly as it was found.
    /// </summary>
    private static string? PriorCollision(
        IPublicationFileSystem files,
        FileIdentity identity,
        string directory,
        string baseSpelling,
        DiscoveredResidue residue,
        IReadOnlyList<PublicationInput> inputs,
        IReadOnlyDictionary<PublicationTargetKind, PublicationTarget> targets)
    {
        var privatePaths = new List<string>();
        var finals = new List<string>();

        if (residue.Intent is { } intent)
        {
            privatePaths.Add(intent.Path);
            privatePaths.Add(intent.PendingPath);
        }

        if (residue.Prior is { } prior)
        {
            privatePaths.AddRange(ControlPaths(directory, prior.Record, prior.Record.Token));
            foreach (var (targetFileName, digest) in prior.StageClaims)
            {
                privatePaths.Add(Path.Combine(
                    directory,
                    PublicationTargets.StageClaimName(
                        prior.Record.BaseFileName,
                        KindOfTarget(targetFileName, prior.Record.BaseFileName),
                        prior.Record.Token,
                        digest)));
            }

            foreach (var targetFileName in prior.Record.Targets)
            {
                finals.Add(Path.Combine(directory, targetFileName));
            }
        }

        foreach (var path in finals)
        {
            if (!Exists(files, path))
            {
                continue;
            }

            var key = identity.KeyFor(path);
            foreach (var input in inputs)
            {
                if (key == identity.KeyFor(input.FullPath))
                {
                    return PublicationMessages.InputCollision(SpellingOfName(Path.GetFileName(path), targets), input.Spelling);
                }
            }
        }

        foreach (var path in privatePaths)
        {
            if (!Exists(files, path))
            {
                continue;
            }

            var key = identity.KeyFor(path);
            foreach (var input in inputs)
            {
                if (key == identity.KeyFor(input.FullPath))
                {
                    // A private path is never named on stderr, so this reports the one thing it
                    // can honestly say: the incomplete run cannot be cleaned up.
                    return PublicationMessages.RecoveryFailed(baseSpelling);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Splits everything claiming this base's private namespace into <b>one</b> transaction this
    /// code could have written, its preparatory intent, and anything else. False means "anything
    /// else was found" — the caller refuses and touches nothing.
    /// <para>
    /// <b>Discovery follows the directory, not the spelling.</b> An entry whose base differs only
    /// in case belongs to this namespace exactly when the containing directory says the two
    /// spellings are one file; that is measured with the shared identity service rather than
    /// assumed either way, so case variants unify where the filesystem unifies them and stay
    /// distinct where it does not (CX-M7H-016). Two different spellings both bearing residue is
    /// ambiguous and is refused.
    /// </para>
    /// <para>
    /// <b>The whole state is validated, not just the record.</b> The intent descriptor, the stage
    /// claims, the phase markers, the evidence, the stages, the backups, and the finals must
    /// together describe a state production can reach; an impossible one authorizes nothing and is
    /// left byte-identical (CX-M7H-011/019/029).
    /// </para>
    /// </summary>
    private static bool TryClassifyResidue(
        IPublicationFileSystem files,
        Func<FileIdentity> identityFactory,
        string directory,
        string baseFileName,
        PublicationFamily expectedFamily,
        out DiscoveredResidue residue)
    {
        residue = DiscoveredResidue.None;

        var identity = identityFactory();
        var privateFiles = new List<(string Path, string Name)>();
        string? claimed = null;
        foreach (var path in Guarded(() => files.EnumerateFiles(directory, baseFileName)))
        {
            var name = Path.GetFileName(path);
            if (!BelongsToNamespace(identity, directory, name, baseFileName, out var spelling))
            {
                continue;
            }

            if (claimed is not null && !string.Equals(claimed, spelling, StringComparison.Ordinal))
            {
                // Residue under two different spellings of one physical namespace: which
                // transaction owns the targets is genuinely ambiguous, so neither is believed.
                return false;
            }

            claimed = spelling;
            privateFiles.Add((path, name));
        }

        if (claimed is null)
        {
            return true;
        }

        // ---- the record, at most one -------------------------------------------------------
        TransactionRecord? record = null;
        foreach (var (path, name) in privateFiles)
        {
            if (PublicationTargets.TokenOfRecord(name, claimed) is not { } token)
            {
                continue;
            }

            if (record is not null
                || ReadControl(files, path) is not { } bytes
                || TransactionRecord.TryParse(bytes, token, claimed) is not { } parsed)
            {
                // A file wearing a record's name that this code did not write, or a second record
                // where only one transaction can exist. Left exactly as found; it refuses the run.
                return false;
            }

            // Valid, and still not this caller's to complete. A record of the other family at this
            // exact base names files this command never asked to write, so it is refused here —
            // before any state is assembled — and preserved byte-for-byte with everything it owns.
            if (parsed.Family != expectedFamily)
            {
                return false;
            }

            record = parsed;
        }

        // ---- the intent descriptor, at most one --------------------------------------------
        IntentClaim? intent = null;
        foreach (var (path, name) in privateFiles)
        {
            if (PublicationTargets.IntentOf(name, claimed) is not { } claim)
            {
                continue;
            }

            if (intent is not null)
            {
                return false;
            }

            // Self-validating: the shape must name a transaction this code could have created, and
            // the digest must be the digest of the exact record bytes that shape produces. A
            // hand-authored name cannot satisfy both without running this serializer.
            if (TransactionRecord.FromShape(claim.Token, claimed, claim.Shape) is not { } described
                || !string.Equals(described.Digest, claim.Digest, StringComparison.Ordinal))
            {
                return false;
            }

            // And the same authority check for a descriptor that never became a record.
            if (described.Family != expectedFamily)
            {
                return false;
            }

            // And its BODY must be the canonical descriptor for that transaction. An empty file, a
            // partial write, or a different role is not this control — it is neither trusted nor
            // removed (ruling 3, superseding the zero-byte mechanism).
            if (!ControlDocument.Matches(
                    ReadControl(files, path),
                    claim.Token,
                    claimed,
                    ControlDocument.IntentRole,
                    described.Digest))
            {
                return false;
            }

            // Beside a record it must be that record's own descriptor, not another claim. The
            // identity part of the name is deliberately NOT compared: it names an object only the
            // interrupted run observed, and it is the very thing the descriptor exists to state.
            if (record is not null
                && (!string.Equals(record.Token, claim.Token, StringComparison.Ordinal)
                    || claim.Shape != record.ShapeCode
                    || !string.Equals(claim.Digest, record.Digest, StringComparison.Ordinal)))
            {
                return false;
            }

            intent = new IntentClaim(
                claim.Token,
                path,
                Path.Combine(directory, PublicationTargets.PendingRecordName(claimed, claim.Token)),
                claim.PendingIdentity,
                described);
        }

        // ---- everything discovered must be owned by one of them ------------------------------
        var owned = new HashSet<string>(StringComparer.Ordinal);
        if (intent is not null)
        {
            owned.Add(Path.GetFileName(intent.Path));
            owned.Add(Path.GetFileName(intent.PendingPath));
        }

        if (record is not null)
        {
            owned.Add(PublicationTargets.RecordName(claimed, record.Token));
            owned.Add(PublicationTargets.PendingRecordName(claimed, record.Token));

            foreach (var phase in PublicationTargets.MarkedPhases)
            {
                owned.Add(PublicationTargets.MarkerName(claimed, phase, record.Token));
            }

            foreach (var entry in record.Files)
            {
                owned.Add(PublicationTargets.PrivateName(entry.TargetFileName, entry.Role, record.Token));
            }

            foreach (var targetFileName in record.Targets)
            {
                var kind = KindOfTarget(targetFileName, claimed);
                owned.Add(PublicationTargets.EvidenceName(claimed, kind, record.Token));
                owned.Add(PublicationTargets.EvidencePendingName(claimed, kind, record.Token));
            }
        }

        var staged = false;
        var rollback = false;
        var committed = false;
        var reached = TransactionPhase.Preparing;
        var pendingRecord = false;
        var pendingEvidence = false;
        var evidencePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var stageClaims = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, name) in privateFiles)
        {
            // A stage claim's name carries the identity digest of the object its create-new
            // produced, so it cannot be predicted into `owned` — it is recognized by grammar and
            // then bound to this record's token (CX-M7H-036). Its BODY must then be exactly this
            // transaction's claim for this target kind and this identity: name, record, kind and
            // acknowledged identity all agreeing. An empty, partial, or substituted object at that
            // name is none of those, so it is neither believed as authority over the stage beside
            // it nor removed — the run refuses with the location as it was found (CX-M7H-046).
            if (PublicationTargets.StageClaimOf(name, claimed) is { } stageClaim)
            {
                if (record is null
                    || !string.Equals(stageClaim.Token, record.Token, StringComparison.Ordinal)
                    || !ControlDocument.Matches(
                        ReadControl(files, path),
                        record.Token,
                        claimed,
                        ControlDocument.ClaimRole(stageClaim.Kind),
                        record.Digest,
                        stageClaim.Digest))
                {
                    return false;
                }

                if (!stageClaims.TryAdd(claimed + PublicationTargets.Extension(stageClaim.Kind), stageClaim.Digest))
                {
                    // Two claims for one target: one create-new produces one object.
                    return false;
                }

                continue;
            }

            if (!owned.Contains(name))
            {
                return false;
            }

            if (record is not null && string.Equals(name, PublicationTargets.PendingRecordName(claimed, record.Token), StringComparison.Ordinal))
            {
                // The rename consumed it: a pending record cannot coexist with the record it was
                // going to become.
                pendingRecord = true;
                continue;
            }

            if (PublicationTargets.EvidencePendingOf(name, claimed) is not null)
            {
                pendingEvidence = true;
                continue;
            }

            if (PublicationTargets.EvidenceOf(name, claimed) is { } evidenceName)
            {
                evidencePaths[claimed + PublicationTargets.Extension(evidenceName.Kind)] = path;
                continue;
            }

            if (PublicationTargets.MarkerOf(name, claimed) is not { } marker)
            {
                continue;
            }

            // A genuine phase marker carries the canonical body for THIS transaction and THIS
            // phase. A file with the right name and any other contents — empty included — is not
            // transaction state: it must neither select a recovery direction nor be removed as
            // control residue (ruling 3, superseding CX-M7H-029's zero-byte mechanism).
            if (record is null
                || !ControlDocument.Matches(
                    ReadControl(files, path),
                    record.Token,
                    claimed,
                    ControlDocument.RoleOf(marker.Phase),
                    record.Digest))
            {
                return false;
            }

            staged |= marker.Phase == TransactionPhase.Staged;
            rollback |= marker.Phase == TransactionPhase.RollingBack;
            committed |= marker.Phase == TransactionPhase.Committed;

            if (marker.Phase > reached)
            {
                reached = marker.Phase;
            }
        }

        if (record is null)
        {
            // Only the intent descriptor and its pending record can exist without one: anything
            // else would have failed the ownership check above.
            residue = new DiscoveredResidue(null, intent);
            return true;
        }

        if (pendingRecord)
        {
            return false;
        }

        // A claim names a stage; a record that never reserved one could not have produced it.
        foreach (var targetFileName in stageClaims.Keys)
        {
            if (!HasStageEntry(record, targetFileName))
            {
                return false;
            }
        }

        // Rollback and commit are mutually exclusive outcomes of one transaction: rollback is
        // refused once the commit point is crossed, and commit never starts after rollback.
        if (rollback && committed)
        {
            return false;
        }

        if (rollback)
        {
            reached = TransactionPhase.RollingBack;
        }

        // A committed marker without a staged one describes a transaction that skipped staging;
        // no run produces that.
        if (committed && !staged)
        {
            return false;
        }

        // Evidence precedes the staged marker, so once that marker exists every target must have
        // its evidence and none may still be pending.
        var evidence = new Dictionary<string, StageEvidence>(StringComparer.Ordinal);
        foreach (var (targetFileName, path) in evidencePaths)
        {
            if (ReadControl(files, path) is not { } bytes
                || StageEvidence.TryParse(bytes, record.Token, claimed, targetFileName) is not { } parsed
                || !parsed.Matches(record, directory))
            {
                return false;
            }

            evidence[targetFileName] = parsed;
        }

        if (staged)
        {
            if (pendingEvidence)
            {
                return false;
            }

            foreach (var targetFileName in record.Targets)
            {
                if (!evidence.ContainsKey(targetFileName))
                {
                    return false;
                }
            }
        }

        var view = new TransactionView(
            files, identityFactory, directory, record, record.Token, staged, evidence, stageClaims);
        if (!IsReachableState(view, reached))
        {
            return false;
        }

        residue = new DiscoveredResidue(
            new DiscoveredTransaction(record, reached, staged, evidence, stageClaims), intent);
        return true;
    }

    private static bool HasStageEntry(TransactionRecord record, string targetFileName)
    {
        foreach (var entry in record.Files)
        {
            if (string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal)
                && string.Equals(entry.TargetFileName, targetFileName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the files on disk are a layout <paramref name="phase"/> can actually produce
    /// (CX-M7H-011/019). Phase markers and evidence are small, discoverable files, not
    /// authenticated authority, so a state no run reaches must authorize no cleanup at all.
    /// <para>
    /// The refusals are exactly the tuples that could otherwise send a destructive step at a file
    /// this transaction does not own; every other combination is either reachable or provably
    /// inert. Before the staged marker the transaction claims <b>nothing</b>, so a final may be in
    /// any state — which is what lets the forward- and rollback-cleanup tails, where the markers
    /// are removed before the record, converge instead of bricking the base.
    /// </para>
    /// </summary>
    private static bool IsReachableState(TransactionView view, TransactionPhase phase)
    {
        foreach (var targetFileName in view.Record.Targets)
        {
            var hasStage = view.HasStage(targetFileName);
            var hasBackup = view.HasBackup(targetFileName);
            var stagePresent = hasStage && view.Exists(view.StagePath(targetFileName));
            var backupPresent = hasBackup && view.Exists(view.BackupPath(targetFileName)!);
            var finalPresent = view.Exists(view.FinalPath(targetFileName));

            if (!view.Staged)
            {
                // Backups are taken during commit, which begins only once staging is complete.
                if (backupPresent)
                {
                    return false;
                }

                continue;
            }

            // A non-overwriting rename cannot leave both the stage and its target: either the
            // rename has not run, or it consumed the stage.
            if (hasStage && !hasBackup && stagePresent && finalPresent)
            {
                return false;
            }

            // A taken backup emptied the target path, and a demoted marker is never republished:
            // a file there now is this transaction's own commit, or it is not ours at all.
            if (backupPresent && finalPresent && !view.Owns(targetFileName))
            {
                return false;
            }

            // A surviving backup must still be the object this transaction renamed aside. Where
            // the file at that path is a different object, no phase authorizes acting on it —
            // forward cleanup least of all (CX-M7H-026).
            if (backupPresent && !view.BackupIsExpected(targetFileName))
            {
                return false;
            }

            if (phase != TransactionPhase.Committed)
            {
                continue;
            }

            // Committed means every stage was consumed by its rename and every selected final is
            // published — and provably so, since a transaction whose stage or backup identity
            // cannot be established never reaches a backup rename at all (CX-M7H-042/043).
            if (hasStage && !view.Owns(targetFileName))
            {
                return false;
            }

            if (!hasStage && finalPresent)
            {
                return false;
            }
        }

        // The aggregate a per-target pass cannot see (CX-M7H-025): a Staged transaction whose
        // staged outputs are all its own published objects, beside a demoted old manifest that is
        // visible again. Production reaches neither cleanup tail that way — a completed rollback
        // would have removed those published artifacts, and a crossed commit point leaves the old
        // marker gone — so this mixed set authorizes nothing and is left byte-identical.
        if (phase == TransactionPhase.Staged && DemotedMarkerVisible(view) && StagedOutputsAllOwned(view))
        {
            return false;
        }

        return true;
    }

    private static bool DemotedMarkerVisible(TransactionView view)
    {
        foreach (var targetFileName in view.Record.Targets)
        {
            if (!view.HasStage(targetFileName) && view.Exists(view.FinalPath(targetFileName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StagedOutputsAllOwned(TransactionView view)
    {
        var any = false;
        foreach (var targetFileName in view.Record.Targets)
        {
            if (!view.HasStage(targetFileName))
            {
                continue;
            }

            any = true;
            if (!view.Owns(targetFileName))
            {
                return false;
            }
        }

        return any;
    }

    // An entry belongs to this run's namespace when its base spelling matches ordinally, or when
    // the containing directory itself treats the two spellings as one file. Identity answers the
    // second question: on a case-insensitive directory both spellings resolve to the same existing
    // file and share an OS identity, while on a case-sensitive one the other spelling does not
    // exist and falls back to a path key, which never equals an OS key.
    private static bool BelongsToNamespace(
        FileIdentity identity, string directory, string entryName, string baseFileName, out string claimedBase)
    {
        claimedBase = string.Empty;
        if (entryName.Length < baseFileName.Length
            || !PublicationTargets.IsPrivateName(entryName, baseFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        claimedBase = entryName[..baseFileName.Length];
        if (string.Equals(claimedBase, baseFileName, StringComparison.Ordinal))
        {
            return true;
        }

        var mine = Path.Combine(directory, baseFileName + entryName[baseFileName.Length..]);
        return identity.KeyFor(Path.Combine(directory, entryName)) == identity.KeyFor(mine);
    }

    // Every bounded control read: a contract defect is tagged at its origin by Guarded, and a
    // genuine I/O or permission failure — including "the file is not there" — answers "not this".
    private static byte[]? ReadControl(IPublicationFileSystem files, string path)
    {
        try
        {
            return Guarded(() => files.ReadBounded(path, PublicationTargets.MaxRecordBytes));
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Completes a prior run the record proves ownership of, and only the exact files it names.
    /// <para>
    /// The durable phase decides, not the file layout. <see cref="TransactionPhase.Preparing"/>
    /// removes owned residue and <b>never touches a final</b>, because no ownership evidence is in
    /// force yet. <see cref="TransactionPhase.Committed"/> and a fully committed
    /// <see cref="TransactionPhase.Staged"/> transaction go forward. Everything else — including
    /// every <see cref="TransactionPhase.RollingBack"/> transaction, whatever the files look like
    /// — is rolled back.
    /// </para>
    /// </summary>
    private static bool Recover(
        IPublicationFileSystem files,
        Func<FileIdentity> identityFactory,
        string directory,
        DiscoveredResidue residue,
        RecoveryGuard guard)
    {
        // Preparatory record state, when no record ever became authoritative. The intent descriptor
        // is the only thing that authorizes removing that pending file, and it authorizes removing
        // exactly one thing: the object whose identity its own name states — the object this
        // transaction's create-new produced. A file that refused that create-new has no descriptor
        // naming it and stays, whatever its length or its bytes (CX-M7H-018/037).
        if (residue is { Prior: null, Intent: { } intent })
        {
            var pendingIsOurs = new RemovalProof((identity, _) => string.Equals(
                IdentityEvidence.Of(
                    intent.Token, PublicationTargets.RecordRole, intent.Described.BaseFileName, identity),
                intent.PendingIdentity,
                StringComparison.Ordinal));

            var descriptorIsOurs = new RemovalProof((_, bytes) => ControlDocument.Matches(
                bytes,
                intent.Token,
                intent.Described.BaseFileName,
                ControlDocument.IntentRole,
                intent.Described.Digest));

            return RemoveOwned(files, intent.PendingPath, pendingIsOurs, guard)
                && RemoveOwned(files, intent.Path, descriptorIsOurs, guard);
        }

        if (residue.Prior is not { } prior)
        {
            return true;
        }

        var view = new TransactionView(
            files,
            identityFactory,
            directory,
            prior.Record,
            prior.Record.Token,
            prior.Staged,
            prior.Evidence,
            prior.StageClaims,
            residue.Intent is { } descriptor ? Path.GetFileName(descriptor.Path) : null,
            residue.Intent?.PendingIdentity);

        if (prior.Phase == TransactionPhase.Preparing)
        {
            return Clear(view, guard);
        }

        // The DURABLE PHASE decides the direction, never the file layout (CX-M7H-027). Committed
        // always finishes forward and RollingBack always finishes backward — a durable rollback
        // intent is the whole point of the marker, and letting an ownership inference override it
        // would restore the hazard CX-M7H-002/010 closed. Only the ambiguous Staged phase, where
        // the run may have stopped on either side of its commit point, asks what the files prove.
        var forward = prior.Phase switch
        {
            TransactionPhase.Committed => true,
            TransactionPhase.RollingBack => false,
            _ => IsFullyCommitted(view),
        };

        return Finish(view, forward, guard, hazard: false);
    }

    /// <summary>
    /// Whether the <b>complete</b> intended publication state is proven: every staged final is
    /// this transaction's own published object, and every backup-only participant — the demoted
    /// old manifest — is in the state a crossed commit point leaves it, namely absent.
    /// <para>
    /// The aggregate is what matters (CX-M7H-025). Judging only the targets that carry stages
    /// would let a run whose new artifact is published <em>and</em> whose old public marker has
    /// been restored count as committed; forward cleanup would then remove every private control
    /// and leave that marker apparently certifying bytes from a different run.
    /// </para>
    /// </summary>
    private static bool IsFullyCommitted(TransactionView view)
    {
        foreach (var targetFileName in view.Record.Targets)
        {
            var committed = view.HasStage(targetFileName)
                ? view.Owns(targetFileName)
                : !view.Exists(view.FinalPath(targetFileName));

            if (!committed)
            {
                return false;
            }
        }

        return true;
    }

    // Preparing: remove every owned private entry and the control files, and never touch a final.
    // A stage path goes only when this transaction can prove which object it created there: the
    // record predicted the name, but a create-new collision means the occupant is someone else's
    // (CX-M7H-031/036).
    private static bool Clear(TransactionView view, RecoveryGuard guard)
    {
        var complete = true;
        foreach (var entry in view.Record.Files)
        {
            var isStage = string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal);
            complete &= RemoveOwned(
                view.Files,
                view.Record.PathOf(view.Directory, entry),
                isStage ? view.StageObject(entry.TargetFileName) : view.BackedUpObject(entry.TargetFileName),
                guard);
        }

        return complete && RemoveControl(view, guard);
    }

    /// <summary>
    /// The one routine that ends a transaction, used by in-process rollback, by post-commit
    /// cleanup, and by a later run resuming either — so those three can never drift apart.
    /// <para>
    /// <b>Every decision is taken from the state as found, before anything moves — and proved
    /// again at the moment it acts.</b> Going <b>forward</b>, the backups are superseded and are
    /// dropped. Going <b>backward</b>, each target is returned to the state preflight found it in,
    /// and the restoring rename's own result is proved before any evidence can be discarded
    /// (CX-M7H-039). Either way it acts only on objects the transaction can prove are its own: a
    /// final is deleted only when it is the very object this transaction staged, and a backup is
    /// deleted or renamed home only when it is the very object this transaction renamed aside. A
    /// file it cannot prove is preserved and the cleanup reports itself incomplete, which keeps the
    /// record in place for a later attempt rather than destroying something unowned
    /// (CX-M7H-019/024/026/030/031/040).
    /// </para>
    /// <para>
    /// The two passes are not redundant. The first is what keeps a decision from being taken
    /// against evidence a later step in the same pass already destroyed; the second is what keeps
    /// a proof from being <em>reused</em> after another target's mutation, during which the
    /// filesystem can have changed underneath it (CX-M7H-030).
    /// </para>
    /// <para>
    /// <paramref name="hazard"/> is set when a commit rename put an object this transaction cannot
    /// identify at a published path and could not put it back. Nothing may then erase the private
    /// state that lets a later run classify what is there (CX-M7H-045).
    /// </para>
    /// </summary>
    private static bool Finish(TransactionView view, bool forward, RecoveryGuard guard, bool hazard)
    {
        var complete = !hazard;

        // 1. Decide everything first, from the state as found.
        var decisions = new List<TargetDecision>();
        foreach (var targetFileName in view.Record.Targets)
        {
            decisions.Add(new TargetDecision(
                targetFileName,
                view.HasBackup(targetFileName),
                view.BackupPath(targetFileName) is { } backup && view.Exists(backup),
                view.BackupIsExpected(targetFileName),
                view.Exists(view.FinalPath(targetFileName)),
                view.Owns(targetFileName)));
        }

        // 2. The finals — each proof re-taken against a fresh observation at its own boundary.
        foreach (var decision in decisions)
        {
            var finalPath = view.FinalPath(decision.TargetFileName);

            if (decision.Owns && !forward)
            {
                complete &= RemoveOwned(
                    view.Files, finalPath, view.PublishedObject(decision.TargetFileName), guard);
            }

            var present = view.Exists(finalPath);

            // The manifest final IS the run's public commit marker (D-122), and a run that
            // introduced one — no backup means nothing was there at preflight — must end its
            // backward path with that path clear. A file there this transaction cannot prove is
            // either the object its own rejected commit rename put there and could not take back,
            // or one that appeared during the run; it is never deleted, but erasing the private
            // state that lets a later run recognize it would leave a failed run wearing a success
            // marker with nothing to classify it (CX-M7H-045). This is the durable form of that
            // rule: a resumed rollback reaches it from the files alone.
            if (!forward && !decision.HasBackup && present && view.IsManifest(decision.TargetFileName))
            {
                complete = false;
            }

            if (!decision.HasBackup)
            {
                continue;
            }

            var backupPath = view.BackupPath(decision.TargetFileName)!;

            if (forward)
            {
                // A superseded backup is still an object, and the evidence says which one. Only
                // that object may be dropped (CX-M7H-026).
                if (decision.BackupPresent && decision.BackupIsExpected)
                {
                    complete &= RemoveOwned(
                        view.Files, backupPath, view.BackedUpObject(decision.TargetFileName), guard);
                }

                continue;
            }

            if (!decision.BackupPresent)
            {
                // No backup file left: the old object is either already home — the ordinary case,
                // including a backup rename that never ran — or beyond this transaction's reach
                // entirely. Both are finished states, and neither authorizes touching anything.
                continue;
            }

            // Restore only the object this transaction renamed aside — verified against the
            // durable evidence, at this instant — and only into a path nothing else occupies.
            var restored = decision.BackupIsExpected
                && view.BackupIsExpected(decision.TargetFileName)
                && !present
                && guard.Allows(backupPath)
                && guard.Allows(finalPath)
                && TryMutate(() => view.Files.Move(backupPath, finalPath));

            // And the restoring rename's RESULT, before anything can discard the evidence that
            // makes this state recognizable (CX-M7H-039). A different object substituted inside
            // that boundary is put back rather than published as the restored prior target.
            if (restored && !view.IsBackedUpObjectAt(decision.TargetFileName, finalPath))
            {
                TryMutate(() => view.Files.Move(finalPath, backupPath));
                restored = false;
            }

            complete &= restored;
        }

        // 3. The stages last, so an interruption cannot leave a decision half-taken against
        //    evidence that is already gone — and only where this transaction can prove which
        //    object it created at that private path (CX-M7H-031/036).
        foreach (var entry in view.Record.Files)
        {
            if (!string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal))
            {
                continue;
            }

            complete &= RemoveOwned(
                view.Files,
                view.Record.PathOf(view.Directory, entry),
                view.StageObject(entry.TargetFileName),
                guard);
        }

        return complete && RemoveControl(view, guard);
    }

    /// <summary>
    /// Removes the transaction's control files, most advanced first, and <b>stops at the first
    /// deletion that fails</b> (CX-M7H-028).
    /// <para>
    /// Descending order is what keeps an interrupted cleanup at a <em>less</em> advanced phase:
    /// removing <c>staged</c> first would transiently leave <c>committed</c> alone, a combination
    /// no transaction reaches. Stopping on failure is the other half of the same guarantee —
    /// continuing past a <c>committed</c> marker that could not be deleted would go on to delete
    /// the <c>staged</c> marker and the evidence beneath it, manufacturing exactly that
    /// unrecognizable state and stranding the base until a human intervened. What survives instead
    /// is the last complete, classifiable phase, which a later run resumes.
    /// </para>
    /// <para>
    /// Every one of these is an object, not a path, and every one goes only when it is provably
    /// still its own exact bytes: the descriptor, the markers and the claims their canonical
    /// role-specific documents, evidence and the record their own encodings. A raced-in occupant at
    /// any of those names refused this transaction's create-new or rename — and an empty, partial,
    /// or substituted object proves nothing whatever its length — so it is preserved and the
    /// cleanup reports itself incomplete (CX-M7H-038/040/046).
    /// </para>
    /// </summary>
    private static bool RemoveControl(TransactionView view, RecoveryGuard guard)
    {
        for (var i = PublicationTargets.MarkedPhases.Length - 1; i >= 0; i--)
        {
            if (!view.RemoveMarker(PublicationTargets.MarkedPhases[i], guard))
            {
                return false;
            }
        }

        // Evidence outlives every phase marker, so a state that can still claim ownership always
        // still carries its proof.
        foreach (var targetFileName in view.Record.Targets)
        {
            var kind = KindOfTarget(targetFileName, view.Record.BaseFileName);
            if (!view.RemoveEvidence(kind, targetFileName, guard)
                || !view.RemovePendingEvidence(kind)
                || !view.RemoveStageClaim(kind, targetFileName, guard))
            {
                return false;
            }
        }

        return view.RemoveIntent(guard) && view.RemovePendingRecord(guard) && view.RemoveRecord(guard);
    }

    /// <summary>
    /// Removes the object at <paramref name="path"/> — and <b>only</b> the object
    /// <paramref name="isExpected"/> accepts (CX-M7H-040).
    /// <para>
    /// The proof is not taken here and then acted on somewhere else: it is evaluated by the removal
    /// primitive against the identity and bytes of the object it has <em>open</em>. A file that
    /// appeared at the path after this transaction's last look is therefore refused by the
    /// operation that would have destroyed it, rather than by a check the operation had already
    /// left behind. On Windows the deletion is requested against that same handle and no interval
    /// exists; on Unix the proof is the last thing done before the unlink, which POSIX cannot make
    /// atomic.
    /// </para>
    /// <para>
    /// The gate in front of it is unchanged: the exact host token, then a fresh check that this
    /// path is not one of the run's own inputs (CX-M7H-022/023). Returns true when the path no
    /// longer holds that object — removed, or never there.
    /// </para>
    /// </summary>
    private static bool RemoveOwned(
        IPublicationFileSystem files, string path, RemovalProof isExpected, RecoveryGuard guard)
    {
        guard.ThrowIfCancelled();

        // Nothing there is nothing to do. This is an optimization, not a check: an object that
        // appears between here and the removal is one the removal's own proof will refuse.
        if (!Exists(files, path))
        {
            return true;
        }

        if (!guard.Allows(path))
        {
            return false;
        }

        try
        {
            return files.Remove(path, isExpected);
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return false;
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }
    }

    private static bool Exists(IPublicationFileSystem files, string path)
    {
        try
        {
            return Guarded(() => files.Exists(path));
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            // An unanswerable existence question is read as "present": that can only make the run
            // refuse or preserve residue, never overwrite or delete something.
            return true;
        }
    }

    // Every publication-filesystem MUTATION goes through here. A genuine I/O, access, or provider
    // failure is the ordinary environment condition the caller reports as exit 1; an
    // ObjectDisposedException, an ArgumentException, or a NotSupportedException from a create,
    // rename, or delete of a path this code derived is a contract or state defect with no
    // user-facing reading at all, and is tagged at its origin so it reaches the sanitized
    // unexpected-fault exit rather than being blamed on the user's output location (CX-M7H-041).
    private static bool TryMutate(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return false;
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }
    }

    // Every seam READ goes through here. A genuine I/O or permission failure stays an ordinary
    // publication failure for the caller to classify; a contract defect is tagged at its origin
    // (CX-M7H-035), so an internal misuse of the residue seam can never be reported as the user's
    // output location being unusable.
    private static T Guarded<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }
    }

    /// <summary>What one target's cleanup will do, decided before anything moves.</summary>
    private sealed record TargetDecision(
        string TargetFileName,
        bool HasBackup,
        bool BackupPresent,
        bool BackupIsExpected,
        bool FinalPresent,
        bool Owns);

    /// <summary>
    /// One transaction's files and the questions worth asking about them — in particular the two
    /// that decide whether anything may be deleted: is the file at this target the object this
    /// transaction <b>staged</b>, or the object it <b>renamed aside</b>?
    /// <para>
    /// Each is stated as a proof over an <em>object</em> — its identity and its bytes — rather than
    /// over a path, because that is exactly what the removal primitive can observe through the very
    /// handle it deletes through (CX-M7H-040). The path-shaped forms are decisions, not
    /// authorizations.
    /// </para>
    /// </summary>
    private sealed class TransactionView(
        IPublicationFileSystem files,
        Func<FileIdentity> identityFactory,
        string directory,
        TransactionRecord record,
        string token,
        bool staged,
        IReadOnlyDictionary<string, StageEvidence> evidence,
        IReadOnlyDictionary<string, string> claims,
        string? intentFileName = null,
        string? pendingIdentity = null,
        IReadOnlySet<TransactionPhase>? marked = null)
    {
        public IPublicationFileSystem Files => files;

        public string Directory => directory;

        public TransactionRecord Record => record;

        public bool Staged => staged;

        public bool Exists(string path) => PublicationTransaction.Exists(files, path);

        public string FinalPath(string targetFileName) => Path.Combine(directory, targetFileName);

        public string StagePath(string targetFileName) =>
            Path.Combine(directory, PublicationTargets.PrivateName(targetFileName, PublicationTargets.StageRole, token));

        public string? BackupPath(string targetFileName) => record.BackupOf(directory, targetFileName);

        public bool HasBackup(string targetFileName) => BackupPath(targetFileName) is not null;

        public bool HasStage(string targetFileName) => HasStageEntry(record, targetFileName);

        /// <summary>True when this target is the run's public commit marker.</summary>
        public bool IsManifest(string targetFileName) =>
            string.Equals(
                targetFileName,
                record.BaseFileName + PublicationTargets.Extension(PublicationTargetKind.Manifest),
                StringComparison.Ordinal);

        /// <summary>
        /// Whether an object is the one this transaction created at
        /// <paramref name="targetFileName"/>'s <b>stage</b> path (CX-M7H-031/036).
        /// <para>
        /// A record entry <em>predicts</em> a private name; it does not prove the create-new
        /// succeeded. The stage claim does: it is written only after acquisition succeeded and
        /// carries the created object's identity digest in its own name. The published evidence
        /// says the same thing once the artifact is closed. With neither, nothing on disk
        /// distinguishes an object this run had just created from one whose presence refused its
        /// create-new — so nothing is removed, the cleanup reports itself incomplete, and the run
        /// says so rather than guessing from a length or a name.
        /// </para>
        /// </summary>
        public RemovalProof StageObject(string targetFileName) => (identity, _) =>
        {
            if (!HasStage(targetFileName))
            {
                return false;
            }

            var digest = evidence.TryGetValue(targetFileName, out var value)
                && IdentityEvidence.IsIdentity(value.Stage)
                ? value.Stage
                : claims.GetValueOrDefault(targetFileName);

            return digest is not null && Is(identity, PublicationTargets.StageRole, targetFileName, digest);
        };

        /// <summary>
        /// Whether an object is the exact object this transaction staged — the only thing that
        /// authorizes deleting a published final.
        /// <para>
        /// The stage must be <b>absent</b> as well: while it is still there the commit rename has
        /// not run, so a file at the target is something else — an external hardlink of the stage
        /// included, which would otherwise match the evidence.
        /// </para>
        /// </summary>
        public RemovalProof PublishedObject(string targetFileName) => (identity, _) =>
            staged
            && HasStage(targetFileName)
            && evidence.TryGetValue(targetFileName, out var value)
            && IdentityEvidence.IsIdentity(value.Stage)
            && !Exists(StagePath(targetFileName))
            && Is(identity, PublicationTargets.StageRole, targetFileName, value.Stage);

        /// <summary>Whether an object is the exact object this transaction renamed aside.</summary>
        public RemovalProof BackedUpObject(string targetFileName) => (identity, _) =>
            HasBackup(targetFileName)
            && evidence.TryGetValue(targetFileName, out var value)
            && IdentityEvidence.IsIdentity(value.Backup)
            && Is(identity, PublicationTargets.BackupRole, targetFileName, value.Backup);

        public bool Owns(string targetFileName) =>
            At(FinalPath(targetFileName), PublishedObject(targetFileName));

        public bool BackupIsExpected(string targetFileName) =>
            BackupPath(targetFileName) is { } backup && At(backup, BackedUpObject(targetFileName));

        public bool IsBackedUpObjectAt(string targetFileName, string path) =>
            At(path, BackedUpObject(targetFileName));

        public bool RemoveMarker(TransactionPhase phase, RecoveryGuard guard)
        {
            var path = Path.Combine(directory, PublicationTargets.MarkerName(record.BaseFileName, phase, token));

            // In process the transaction knows exactly which markers its create-new produced. One
            // it never created is not its own, whatever occupies that name (CX-M7H-038).
            if (marked is not null && !marked.Contains(phase))
            {
                return !Exists(path);
            }

            // And a resumed run asks the same question of the bytes: exactly this transaction's
            // marker for exactly this phase, or nothing happens to it (ruling 3).
            return RemoveOwned(files, path, ControlIs(ControlDocument.RoleOf(phase)), guard);
        }

        public bool RemoveEvidence(PublicationTargetKind kind, string targetFileName, RecoveryGuard guard)
        {
            var path = Path.Combine(directory, PublicationTargets.EvidenceName(record.BaseFileName, kind, token));

            // The authoritative evidence arrives by a rename whose result this run proved, and it
            // is self-validating besides: the bytes must be this run's own evidence for this target
            // and must agree with the record. A raced-in occupant at that name — one whose presence
            // refused the publishing rename — can prove neither (CX-M7H-038).
            return RemoveOwned(
                files,
                path,
                (_, bytes) => bytes is not null && IsOurEvidence(bytes, targetFileName),
                guard);
        }

        /// <summary>
        /// A pending evidence file is removed in process by the run that created it, against the
        /// identity that creation reported. A resumed run has no such statement — nothing durable
        /// names that object — so it preserves whatever is there and reports the cleanup
        /// incomplete, rather than inferring ownership from the name or the bytes (CX-M7H-037).
        /// </summary>
        public bool RemovePendingEvidence(PublicationTargetKind kind) =>
            !Exists(Path.Combine(directory, PublicationTargets.EvidencePendingName(record.BaseFileName, kind, token)));

        /// <summary>
        /// A stage claim goes only when the object at its name is exactly this transaction's claim
        /// for this target kind and this acknowledged identity — proved, like every removal, from
        /// the handle the deletion acts through. An object substituted at that name inside the
        /// removal itself fails that proof and survives (CX-M7H-046).
        /// </summary>
        public bool RemoveStageClaim(PublicationTargetKind kind, string targetFileName, RecoveryGuard guard)
        {
            if (!claims.TryGetValue(targetFileName, out var digest))
            {
                return true;
            }

            return RemoveOwned(
                files,
                Path.Combine(directory, PublicationTargets.StageClaimName(record.BaseFileName, kind, token, digest)),
                (_, bytes) => ControlDocument.Matches(
                    bytes,
                    token,
                    record.BaseFileName,
                    ControlDocument.ClaimRole(kind),
                    record.Digest,
                    digest),
                guard);
        }

        public bool RemoveIntent(RecoveryGuard guard) =>
            intentFileName is not { } name
            || RemoveOwned(
                files, Path.Combine(directory, name), ControlIs(ControlDocument.IntentRole), guard);

        private RemovalProof ControlIs(string role) => (_, bytes) =>
            ControlDocument.Matches(bytes, token, record.BaseFileName, role, record.Digest);

        /// <summary>
        /// The pending record, once the authoritative one exists. Its acknowledgement — the intent
        /// descriptor — is what names the object, so a run that no longer has one removes nothing
        /// and says the cleanup is incomplete (CX-M7H-037).
        /// </summary>
        public bool RemovePendingRecord(RecoveryGuard guard)
        {
            var path = Path.Combine(directory, PublicationTargets.PendingRecordName(record.BaseFileName, token));
            if (pendingIdentity is not { } digest)
            {
                return !Exists(path);
            }

            return RemoveOwned(
                files,
                path,
                (identity, _) => Is(identity, PublicationTargets.RecordRole, record.BaseFileName, digest),
                guard);
        }

        /// <summary>
        /// The authoritative record, the last thing a finished transaction removes. It arrived at
        /// that name by a rename this run proved on both sides, and it is its own exact bytes; an
        /// object that is not those bytes is not the record and is left where it is.
        /// </summary>
        public bool RemoveRecord(RecoveryGuard guard)
        {
            var bytes = record.ToBytes();
            return RemoveOwned(
                files,
                Path.Combine(directory, PublicationTargets.RecordName(record.BaseFileName, token)),
                (_, actual) => actual is not null && actual.AsSpan().SequenceEqual(bytes),
                guard);
        }

        private bool IsOurEvidence(byte[] bytes, string targetFileName) =>
            StageEvidence.TryParse(bytes, token, record.BaseFileName, targetFileName) is { } parsed
            && parsed.Matches(record, directory);

        private bool Is(FileIdentityKey? identity, string role, string targetFileName, string expected) =>
            string.Equals(
                IdentityEvidence.Of(token, role, targetFileName, identity), expected, StringComparison.Ordinal);

        // A DECISION about a path, taken fresh: what is there right now. It never authorizes a
        // removal on its own — the removal re-asks the same proof of the object it has open.
        private bool At(string path, RemovalProof proof) =>
            proof(identityFactory().KeyFor(path), ReadControl(files, path));
    }

    /// <summary>
    /// The gate every recovery mutation passes: the exact host token, and a fresh check that the
    /// path is not one of this run's inputs (CX-M7H-022/023).
    /// </summary>
    private sealed class RecoveryGuard(
        Func<FileIdentity> identityFactory, IReadOnlyList<PublicationInput> inputs, CancellationToken cancellation)
    {
        public void ThrowIfCancelled() => cancellation.ThrowIfCancellationRequested();

        public bool Allows(string path)
        {
            var identity = identityFactory();
            var key = identity.KeyFor(path);
            foreach (var input in inputs)
            {
                if (key == identity.KeyFor(input.FullPath))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
