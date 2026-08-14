namespace FcaBedrock.Cli.Publication;

// The transaction's residue discovery, validation, and reachability checks.
// Classification stays whole so durable state keeps one interpretation.
internal sealed partial class PublicationTransaction
{
    /// <summary>
    /// Splits everything claiming this base's private namespace into <b>one</b> transaction this
    /// code could have written, its preparatory intent, and anything else. False means "anything
    /// else was found" — the caller refuses and touches nothing.
    /// <para>
    /// <b>Discovery follows the directory, not the spelling.</b> An entry whose base differs only
    /// in case belongs to this namespace exactly when the containing directory says the two
    /// spellings are one file; that is measured with the shared identity service rather than
    /// assumed either way, so case variants unify where the filesystem unifies them and stay
    /// distinct where it does not. Two different spellings both bearing residue is
    /// ambiguous and is refused.
    /// </para>
    /// <para>
    /// <b>The whole state is validated, not just the record.</b> The intent descriptor, the stage
    /// claims, the phase markers, the evidence, the stages, the backups, and the finals must
    /// together describe a state production can reach; an impossible one authorizes nothing and is
    /// left byte-identical.
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
            // removed — the exact-bytes rule superseding the earlier zero-byte mechanism.
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
            // then bound to this record's token. Its BODY must then be exactly this
            // transaction's claim for this target kind and this identity: name, record, kind and
            // acknowledged identity all agreeing. An empty, partial, or substituted object at that
            // name is none of those, so it is neither believed as authority over the stage beside
            // it nor removed — the run refuses with the location as it was found.
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
            // control residue — the exact-bytes rule superseding the earlier zero-byte mechanism.
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
    /// Phase markers and evidence are small, discoverable files, not
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
            // forward cleanup least of all.
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
            // cannot be established never reaches a backup rename at all.
            if (hasStage && !view.Owns(targetFileName))
            {
                return false;
            }

            if (!hasStage && finalPresent)
            {
                return false;
            }
        }

        // The aggregate a per-target pass cannot see: a Staged transaction whose
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

}
