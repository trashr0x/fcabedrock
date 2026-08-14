namespace FcaBedrock.Cli.Publication;

// The transaction's preflight inspection, collision checks, and prior-run preparation.
// These members decide before a transaction exists and remain mutation-free.
internal sealed partial class PublicationTransaction
{
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
        // different run, whether that output replaced something or is brand new.
        var demotesManifest = !manifestIsFinal && Exists(files, manifest.FullPath);

        // A demoted marker is renamed aside and then deleted, so it is every bit as much a
        // mutated participant as a replaced artifact — and `--force` never authorizes destroying
        // an input. It joins the collision set BEFORE any of it happens.
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
        // cannot see cannot be recovered, and case variants alias on some directories.
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
        // fresh check later runs after recovery, too late to protect one it removes.
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
    // and knowing that here is knowing it before any record, stage, or claim exists.
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
    /// this run's inputs. Read-only: it mutates nothing and, on a match, the run
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

}
