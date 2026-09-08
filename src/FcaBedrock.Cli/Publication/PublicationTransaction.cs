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
/// <b>An identity is only as good as the object that still holds it</b> (D-125). An identifier
/// describes an object that exists; unlink a file's last name and the host may hand the very same
/// number to the next creation, so a proof taken from a closed handle can be a proof about a
/// substitute. Every object whose identity authorizes a later mutation is therefore held open from
/// the moment that identity is captured until its last authorized use — the reference acquired
/// while the creating or approving handle is still open and proved equal to it, transferred with
/// overlapping references, and never released and re-acquired by name. A substitute cannot then be
/// handed the original's identifier, and every proof below compares exactly what it always
/// compared. These are object-lifetime references, not writer streams or reader locks: the writer's
/// flush-and-close boundary is precisely where it was.
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
internal sealed partial class PublicationTransaction : IDisposable
{
    private readonly IPublicationFileSystem _files;
    private readonly PublicationReferences _references;
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
        IReadOnlyDictionary<string, string> preflightIdentity,
        PublicationReferences references)
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

        // Handed over by preflight, which already holds a reference to every existing participant
        // it approved: the objects a forced replacement will rename aside were anchored at the one
        // moment the collision and identity checks passed, and stay anchored from there.
        _references = references;
    }

    /// <summary>True once every requested artifact — the manifest last, when written — is committed.</summary>
    public bool Committed { get; private set; }

    /// <summary>
    /// Releases every live reference this transaction still holds. Deterministic, and called on
    /// success, refusal, cancellation and fault alike: a reference outliving its transaction would
    /// keep a Windows deletion pending and an object allocated for no purpose. It is idempotent, so
    /// a caller that disposes on both the normal and the exceptional path is correct.
    /// </summary>
    public void Dispose() => _references.Dispose();

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

        // The object this creation produced, held open from here on: its identifier cannot be
        // reissued to anything else while the transaction may still act on it.
        if (file.Reference is { } reference)
        {
            _references.Adopt(pending, reference);
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
            || !TryMutate(() => _files.Move(pending, RecordPath, _references.Of(pending))))
        {
            AbandonPending(pending, isPendingObject);
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        // The rename's RESULT. A different object substituted inside that boundary would otherwise
        // be accepted as this transaction's authority, published over, and finally deleted as owned.
        // It is put back where the rename took it from and nothing is begun.
        if (!MatchesObject(RecordPath, isTheRecord))
        {
            TryMutate(() => _files.Move(RecordPath, pending, _references.Of(pending)));
            AbandonIntent();
            return new PublicationFailure(PublicationMessages.RecordFailed(_baseSpelling));
        }

        // Verified, so the object at the record's name IS the one this run created: the reference
        // is re-filed under the new name rather than released and taken again, which is what keeps
        // the anchor continuous across the rename.
        _references.Rekey(pending, RecordPath);

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

        // Held from creation. The stage's identity is the evidence every later step verifies it by
        // — through the seal, the commit rename, and any rollback that removes what it published —
        // so the object stays anchored for exactly that long.
        if (stage.Reference is { } reference)
        {
            _references.Adopt(stagePath, reference);
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

            if (!TryMutate(() => _files.Move(targetPath, backupPath, _references.Of(targetPath))))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(spelling));
            }

            // The interval between the check and the rename is irreducible, so the rename's RESULT
            // is checked too — and, unlike before, its result is undone: the
            // object that actually moved is put back at the public path it came from, so a failed
            // forced replacement never leaves a user's file stranded under a private name.
            if (!Matches(backupPath, PublicationTargets.BackupRole, entry.TargetFileName, expected))
            {
                // The object that moved is NOT the one preflight approved, so the reference stays
                // filed under the name it still anchors; only a verified rename re-keys it.
                if (!TryMutate(() => _files.Move(backupPath, targetPath, _references.Of(targetPath))))
                {
                    _hazard = true;
                }

                return new PublicationFailure(PublicationMessages.CommitFailed(spelling));
            }

            _references.Rekey(targetPath, backupPath);
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

            var stagePath = StagePath(target.FileName);
            if (!TryMutate(() => _files.Move(stagePath, target.FullPath, _references.Of(stagePath))))
            {
                return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
            }

            // And the published final must be that same object before the next artifact — or the
            // manifest, last — can commit. If it is not, the object that landed there is put back
            // at the stage path it was taken from: preserved, out of the public namespace, and
            // recognizable to the rollback that follows.
            if (!Matches(target.FullPath, PublicationTargets.StageRole, target.FileName, sealedStage))
            {
                if (!TryMutate(() => _files.Move(target.FullPath, stagePath, _references.Of(stagePath))))
                {
                    // The unowned object could not be moved off a published path. Nothing may
                    // erase the transaction's authority while it sits there.
                    _hazard = true;
                }

                return new PublicationFailure(PublicationMessages.CommitFailed(target.Spelling));
            }

            // Verified: the published final IS the staged object, so its anchor follows it to the
            // public name a rollback would have to remove it from.
            _references.Rekey(stagePath, target.FullPath);
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
            _references,
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
    /// through the very handle it deletes through.
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

    private bool Remove(string path, RemovalProof proof) =>
        RemoveOwned(_files, path, proof, Guard(), _references);

    // A record publication that never completed. The pending object goes only when it is provably
    // the object this run created there — whatever state its bytes are in — so an occupant that
    // refused the create-new is preserved.
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
    /// <b>Both sides of that rename are proved</b>. A refused create-new leaves no
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

        if (file.Reference is { } reference)
        {
            _references.Adopt(pending, reference);
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

        if (!TryMutate(() => _files.Move(pending, published, _references.Of(pending))))
        {
            Remove(pending, isThatObject);
            return false;
        }

        if (!MatchesObject(published, isTheEvidence))
        {
            TryMutate(() => _files.Move(published, pending, _references.Of(pending)));
            return false;
        }

        _references.Rekey(pending, published);
        _evidence[targetFileName] = evidence;
        return true;
    }

    // Writes a control document to an already-open stream. The narrow predicate is deliberate: at
    // a write call an ArgumentException means the caller passed an invalid range and an
    // ObjectDisposedException means it wrote to a closed stream — contract defects that must reach
    // the sanitized unexpected-fault exit rather than be disguised as an environment
    // failure.
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
    // host would then read it as its own writer failing.
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
    // treats a failure as "do not start". A marker whose create-new was refused is
    // NOT recorded, so cleanup never removes the occupant that refused it.
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
    // either boundary still reaches the sanitized unexpected-fault exit.
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

        // Adopted like every other acquisition: the reference this creation produced belongs to the
        // transaction from here on, and is released by the removal that eventually takes the control
        // out. A reference nobody owns would keep a completed Windows deletion pending forever.
        if (file.Reference is { } reference)
        {
            _references.Adopt(path, reference);
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
    // right now, and the answer must not be one taken before the last mutation.
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

}
