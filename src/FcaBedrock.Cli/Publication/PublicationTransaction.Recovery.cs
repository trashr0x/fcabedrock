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
    /// <paramref name="hazard"/> is set when a commit rename put an object this transaction cannot
    /// identify at a published path and could not put it back. Nothing may then erase the private
    /// state that lets a later run classify what is there.
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
            // makes this state recognizable. A different object substituted inside
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
                guard);
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
