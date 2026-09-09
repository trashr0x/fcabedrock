namespace FcaBedrock.Cli.Publication;

// The transaction's nested cleanup decisions, ownership proofs, and recovery gate.
// They stay nested together so private same-class access and captured state remain unchanged.
internal sealed partial class PublicationTransaction
{
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
    /// handle it deletes through. The path-shaped forms are decisions, not
    /// authorizations.
    /// </para>
    /// </summary>
    private sealed class TransactionView(
        IPublicationFileSystem files,
        PublicationReferences references,
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

        /// <summary>
        /// The live references authorizing this view's mutations: the transaction's own while it is
        /// still running, and the ones a resumed run acquired before it decided anything.
        /// </summary>
        public PublicationReferences References => references;

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
        /// <paramref name="targetFileName"/>'s <b>stage</b> path.
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

        /// <summary>
        /// Anchors <paramref name="path"/> for this pass — see
        /// <see cref="PublicationTransaction.Anchor"/>. False means an object is there and cannot
        /// be held, so nothing about it may be decided or done.
        /// </summary>
        public bool Anchor(string path) => PublicationTransaction.Anchor(files, references, path);

        public bool Owns(string targetFileName) =>
            At(FinalPath(targetFileName), PublishedObject(targetFileName));

        public bool BackupIsExpected(string targetFileName) =>
            BackupPath(targetFileName) is { } backup && At(backup, BackedUpObject(targetFileName));

        /// <summary>
        /// Whether the object now at <paramref name="path"/> is the one this transaction renamed
        /// aside, asked of an anchor the caller <b>already holds</b>.
        /// <para>
        /// The restoring rename is the case this exists for: its source reference is what still
        /// holds the moved object, and it is deliberately not re-filed under the destination until
        /// the result has been proved. Anchoring by the destination path instead would take a
        /// second reference to the same object — release-and-reacquire-by-name in all but name,
        /// which is precisely what the lifetime rule forbids.
        /// </para>
        /// </summary>
        public bool IsBackedUpObjectAt(
            string targetFileName, string path, PublicationObjectReference? anchor) =>
            At(path, anchor, BackedUpObject(targetFileName));

        public bool RemoveMarker(TransactionPhase phase, RecoveryGuard guard)
        {
            var path = Path.Combine(directory, PublicationTargets.MarkerName(record.BaseFileName, phase, token));

            // In process the transaction knows exactly which markers its create-new produced. One
            // it never created is not its own, whatever occupies that name.
            if (marked is not null && !marked.Contains(phase))
            {
                return !Exists(path);
            }

            // And a resumed run asks the same question of the bytes: exactly this transaction's
            // marker for exactly this phase, or nothing happens to it.
            return RemoveOwned(files, path, ControlIs(ControlDocument.RoleOf(phase)), guard, references);
        }

        public bool RemoveEvidence(PublicationTargetKind kind, string targetFileName, RecoveryGuard guard)
        {
            var path = Path.Combine(directory, PublicationTargets.EvidenceName(record.BaseFileName, kind, token));

            // The authoritative evidence arrives by a rename whose result this run proved, and it
            // is self-validating besides: the bytes must be this run's own evidence for this target
            // and must agree with the record. A raced-in occupant at that name — one whose presence
            // refused the publishing rename — can prove neither.
            return RemoveOwned(
                files,
                path,
                (_, bytes) => bytes is not null && IsOurEvidence(bytes, targetFileName),
                guard,
                references);
        }

        /// <summary>
        /// A pending evidence file is removed in process by the run that created it, against the
        /// identity that creation reported. A resumed run has no such statement — nothing durable
        /// names that object — so it preserves whatever is there and reports the cleanup
        /// incomplete, rather than inferring ownership from the name or the bytes.
        /// </summary>
        public bool RemovePendingEvidence(PublicationTargetKind kind) =>
            !Exists(Path.Combine(directory, PublicationTargets.EvidencePendingName(record.BaseFileName, kind, token)));

        /// <summary>
        /// A stage claim goes only when the object at its name is exactly this transaction's claim
        /// for this target kind and this acknowledged identity — proved, like every removal, from
        /// the handle the deletion acts through. An object substituted at that name inside the
        /// removal itself fails that proof and survives.
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
                guard,
                references);
        }

        public bool RemoveIntent(RecoveryGuard guard) =>
            intentFileName is not { } name
            || RemoveOwned(
                files, Path.Combine(directory, name), ControlIs(ControlDocument.IntentRole), guard, references);

        private RemovalProof ControlIs(string role) => (_, bytes) =>
            ControlDocument.Matches(bytes, token, record.BaseFileName, role, record.Digest);

        /// <summary>
        /// The pending record, once the authoritative one exists. Its acknowledgement — the intent
        /// descriptor — is what names the object, so a run that no longer has one removes nothing
        /// and says the cleanup is incomplete.
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
                guard,
                references);
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
                guard,
                references);
        }

        private bool IsOurEvidence(byte[] bytes, string targetFileName) =>
            StageEvidence.TryParse(bytes, token, record.BaseFileName, targetFileName) is { } parsed
            && parsed.Matches(record, directory);

        private bool Is(FileIdentityKey? identity, string role, string targetFileName, string expected) =>
            string.Equals(
                IdentityEvidence.Of(token, role, targetFileName, identity), expected, StringComparison.Ordinal);

        // A DECISION about a path, taken fresh: what is there right now. It never authorizes a
        // mutation on its own, which is why it does not itself require an anchor — classification
        // asks these questions of a location it has not yet taken any reference to, and its only
        // possible answer is to refuse.
        //
        // Every mutation it feeds is separately conditioned on the anchor: a removal re-asks the
        // proof of the object it holds open (RemoveOwned), a restore proves its source reference is
        // still at the name it is moving, and the recovery passes anchor every participant before
        // they decide anything at all. So an unanchored identity can start no destructive step
        // (D-125).
        private bool At(string path, RemovalProof proof) =>
            proof(identityFactory().KeyFor(path), ReadControl(files, path));

        // The same question asked of an anchor the caller already holds: the answer must describe
        // the object that reference keeps alive, not merely whatever the name resolves to.
        private bool At(string path, PublicationObjectReference? anchor, RemovalProof proof) =>
            anchor is not null && anchor.IsStillAt(path) && At(path, proof);
    }

    /// <summary>
    /// The gate every recovery mutation passes: the exact host token, and a fresh check that the
    /// path is not one of this run's inputs.
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
