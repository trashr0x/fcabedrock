namespace FcaBedrock.Cli.Publication;

// The transaction's recovery direction, cleanup, and transaction-ending routine.
// Finish stays whole so decide-then-act ordering has one implementation.
internal sealed partial class PublicationTransaction
{
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
    /// <para>
    /// <b>A resumed run holds no reference from the invocation that died</b>, so it takes its own —
    /// every one it will need, before it decides anything, and therefore before it mutates any
    /// earlier participant. That is what stops a decision made about one object from being carried
    /// out against a different object that inherited its identifier in between (D-125). A
    /// participant that is present and <em>cannot</em> be held grants no authority at all: the pass
    /// stops there with the location exactly as it was found, the run reports that it could not
    /// clean up an incomplete run, and a later attempt reaches the same state and says the same
    /// thing. It does not make the interval in which nothing was held provable: that remains the
    /// caller's undisturbed-namespace precondition, not a guarantee this pass can offer.
    /// </para>
    /// </summary>
    private static bool Recover(
        IPublicationFileSystem files,
        PublicationReferences references,
        Func<FileIdentity> identityFactory,
        string directory,
        DiscoveredResidue residue,
        RecoveryGuard guard)
    {
        // Preparatory record state, when no record ever became authoritative. The intent descriptor
        // is the only thing that authorizes removing that pending file, and it authorizes removing
        // exactly one thing: the object whose identity its own name states — the object this
        // transaction's create-new produced. A file that refused that create-new has no descriptor
        // naming it and stays, whatever its length or its bytes.
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

            // Anchored before either removal is attempted, and BOTH before the first of them: the
            // pending object's identity is the whole authority here, so it must name an object that
            // cannot be swapped underneath it — and the descriptor must be held before its own
            // pending record is removed, or a failure to anchor it would be discovered only after
            // the object it authorizes had already gone.
            //
            // Either one that is present and cannot be held ends the pass with nothing touched:
            // the residue stays exactly as it was found, and the next attempt says the same thing.
            if (!Anchor(files, references, intent.PendingPath) || !Anchor(files, references, intent.Path))
            {
                return false;
            }

            return RemoveOwned(files, intent.PendingPath, pendingIsOurs, guard, references)
                && RemoveOwned(files, intent.Path, descriptorIsOurs, guard, references);
        }

        if (residue.Prior is not { } prior)
        {
            return true;
        }

        var view = new TransactionView(
            files,
            references,
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

        // Anchored before the DIRECTION is decided, not merely before the first mutation: the
        // ownership question below authorizes forward cleanup over rollback, and an answer taken
        // from an identity nobody holds is exactly the authority D-125 withdraws. Finish anchors
        // again — it has in-process callers of its own — and Ensure is idempotent, so a reference
        // taken here is kept rather than released and taken a second time.
        if (!AnchorParticipants(view))
        {
            return false;
        }

        // The DURABLE PHASE decides the direction, never the file layout. Committed
        // always finishes forward and RollingBack always finishes backward — a durable rollback
        // intent is the whole point of the marker, and letting an ownership inference override it
        // would restore the hazard the phase-body rule closed. Only the ambiguous Staged phase, where
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
    /// Anchors every participant a <see cref="Finish"/> pass can consult or mutate — each target's
    /// final, its backup where it has one, and its stage where it has one — and answers whether all
    /// of them are now held.
    /// <para>
    /// <b>All of them, before any of them moves.</b> That ordering is the point: a pass that
    /// anchored each target as it reached it could remove the first target's final and only then
    /// discover it cannot hold the second's backup, having already spent the authority it can no
    /// longer complete. Anchoring first means a participant that cannot be held costs nothing —
    /// the location is exactly as it was found, the record survives, and a later attempt reaches
    /// the same state and reaches the same answer (D-125).
    /// </para>
    /// <para>
    /// Absence is not failure: a target with no final, no backup, or no stage on disk has nothing
    /// to anchor and nothing to act on, which is the ordinary idempotent case.
    /// </para>
    /// </summary>
    private static bool AnchorParticipants(TransactionView view)
    {
        foreach (var targetFileName in view.Record.Targets)
        {
            if (!view.Anchor(view.FinalPath(targetFileName)))
            {
                return false;
            }

            if (view.BackupPath(targetFileName) is { } backup && !view.Anchor(backup))
            {
                return false;
            }

            if (view.HasStage(targetFileName) && !view.Anchor(view.StagePath(targetFileName)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the <b>complete</b> intended publication state is proven: every staged final is
    /// this transaction's own published object, and every backup-only participant — the demoted
    /// old manifest — is in the state a crossed commit point leaves it, namely absent.
    /// <para>
    /// The aggregate is what matters. Judging only the targets that carry stages
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
    // record predicted the name, but a create-new collision means the occupant is someone
    // else's.
    private static bool Clear(TransactionView view, RecoveryGuard guard)
    {
        // Anchor every object this pass may remove before it removes any of them, so no removal can
        // destroy evidence another one's proof still depends on — and so a participant that cannot
        // be held is discovered while the location is still exactly as it was found. One that is
        // present and unanchorable ends the pass here: nothing is removed, the record stays in
        // place, and a later attempt reaches the same state and says the same thing.
        foreach (var entry in view.Record.Files)
        {
            if (!view.Anchor(view.Record.PathOf(view.Directory, entry)))
            {
                return false;
            }
        }

        var complete = true;
        foreach (var entry in view.Record.Files)
        {
            var isStage = string.Equals(entry.Role, PublicationTargets.StageRole, StringComparison.Ordinal);
            complete &= RemoveOwned(
                view.Files,
                view.Record.PathOf(view.Directory, entry),
                isStage ? view.StageObject(entry.TargetFileName) : view.BackedUpObject(entry.TargetFileName),
                guard,
                view.References);
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
    /// and the restoring rename's own result is proved before any evidence can be discarded.
    /// Either way it acts only on objects the transaction can prove are its own: a
    /// final is deleted only when it is the very object this transaction staged, and a backup is
    /// deleted or renamed home only when it is the very object this transaction renamed aside. A
    /// file it cannot prove is preserved and the cleanup reports itself incomplete, which keeps the
    /// record in place for a later attempt rather than destroying something
    /// unowned.
    /// </para>
    /// <para>
    /// The two passes are not redundant. The first is what keeps a decision from being taken
    /// against evidence a later step in the same pass already destroyed; the second is what keeps
    /// a proof from being <em>reused</em> after another target's mutation, during which the
    /// filesystem can have changed underneath it.
    /// </para>
    /// <para>
    /// Both rest on the anchor gate that precedes them: every present participant is held open
    /// before the first decision is taken, and one that cannot be held ends the pass with nothing
    /// touched (D-125).
    /// </para>
    /// <para>
    /// <paramref name="hazard"/> is set when a commit rename put an object this transaction cannot
    /// identify at a published path and could not put it back. Nothing may then erase the private
    /// state that lets a later run classify what is there.
    /// </para>
    /// </summary>
    private static bool Finish(TransactionView view, bool forward, RecoveryGuard guard, bool hazard)
    {
        // 0. Anchor every participant this pass can consult or mutate, BEFORE the decision pass and
        //    therefore before any of them is mutated — which is what makes a decision still true of
        //    the same object when it is acted on. A running transaction already holds most of these
        //    and simply keeps them; a resumed one takes them here. A participant that is present
        //    and cannot be held ends the pass with the location untouched: acquiring the later
        //    references only as each target came up would mean an earlier target had already been
        //    removed or restored by the time the failure surfaced, which is the ordering D-125
        //    requires and the reason this is a gate rather than a per-target check.
        if (!AnchorParticipants(view))
        {
            return false;
        }

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
                    view.Files,
                    finalPath,
                    view.PublishedObject(decision.TargetFileName),
                    guard,
                    view.References);
            }

            var present = view.Exists(finalPath);

            // The manifest final IS the run's public commit marker (D-122), and a run that
            // introduced one — no backup means nothing was there at preflight — must end its
            // backward path with that path clear. A file there this transaction cannot prove is
            // either the object its own rejected commit rename put there and could not take back,
            // or one that appeared during the run; it is never deleted, but erasing the private
            // state that lets a later run recognize it would leave a failed run wearing a success
            // marker with nothing to classify it. This is the durable form of that
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
                // that object may be dropped.
                if (decision.BackupPresent && decision.BackupIsExpected)
                {
                    complete &= RemoveOwned(
                        view.Files,
                        backupPath,
                        view.BackedUpObject(decision.TargetFileName),
                        guard,
                        view.References);
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
            //
            // And only an object this transaction is holding open. The reference is what the move
            // revalidates its source against and what proves afterwards which object arrived; a
            // rename made without one would be authorized by an identifier the host is free to have
            // reissued, which is the authority D-125 withdraws (the gate above has already refused
            // that case, and this states it where the mutation actually happens).
            var anchor = view.References.Of(backupPath);
            var restored = decision.BackupIsExpected
                && view.BackupIsExpected(decision.TargetFileName)
                && !present
                && anchor is not null
                && anchor.IsStillAt(backupPath)
                && guard.Allows(backupPath)
                && guard.Allows(finalPath)
                && TryMutate(() => view.Files.Move(backupPath, finalPath, anchor));

            // And the restoring rename's RESULT, before anything can discard the evidence that
            // makes this state recognizable. A different object substituted inside
            // that boundary is put back rather than published as the restored prior target.
            //
            // Asked of the backup's own reference, which is what still holds the object the rename
            // moved: it is re-filed under the destination only once this has proved that is where
            // it went.
            if (restored && !view.IsBackedUpObjectAt(decision.TargetFileName, finalPath, anchor))
            {
                TryMutate(() => view.Files.Move(finalPath, backupPath, anchor));
                restored = false;
            }
            else if (restored)
            {
                // Verified home. The anchor follows the object to the name it now occupies, rather
                // than being dropped and taken again from a path.
                view.References.Rekey(backupPath, finalPath);
            }

            complete &= restored;
        }

        // 3. The stages last, so an interruption cannot leave a decision half-taken against
        //    evidence that is already gone — and only where this transaction can prove which
        //    object it created at that private path.
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
                guard,
                view.References);
        }

        return complete && RemoveControl(view, guard);
    }

    /// <summary>
    /// Removes the transaction's control files, most advanced first, and <b>stops at the first
    /// deletion that fails</b>.
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
    /// cleanup reports itself incomplete.
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

}
