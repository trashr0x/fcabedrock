namespace FcaBedrock.Cli.Publication;

// The transaction's guarded filesystem reads, removals, existence checks, and mutation gate.
// They stay together so failure classification and proof-gate ordering remain unchanged.
internal sealed partial class PublicationTransaction
{
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
    /// Anchors <paramref name="path"/> for the pass that is about to consult or mutate it: takes
    /// the live reference if this transaction does not already hold one, and answers whether the
    /// object there is now held.
    /// <para>
    /// <b>Absence is not a failed acquisition.</b> A path with nothing at it has no object to
    /// anchor and nothing to mutate, which is the ordinary idempotent case every recovery pass is
    /// built on — so it answers true. A path that <em>does</em> hold an object whose reference
    /// cannot be taken answers false, and the caller grants no mutation authority for it: an
    /// identity nobody holds is a number the host is free to have reissued, which is exactly the
    /// authority D-125 withdraws.
    /// </para>
    /// <para>
    /// An unanswerable existence question reads as "present", so it fails closed too.
    /// </para>
    /// </summary>
    private static bool Anchor(IPublicationFileSystem files, PublicationReferences references, string path) =>
        references.Ensure(path) is not null || !Exists(files, path);

    /// <summary>
    /// Removes the object at <paramref name="path"/> — and <b>only</b> the object
    /// <paramref name="isExpected"/> accepts.
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
    /// The gate in front of it is the exact host token, a fresh check that this path is not one of
    /// the run's own inputs, and the <b>anchor</b>: an object that is there but cannot be held open
    /// authorizes nothing and is left exactly as it is. Returns true when the path no longer holds
    /// that object — removed, or never there.
    /// </para>
    /// <para>
    /// <b>Release sequencing.</b> The reference this run holds on that path overlaps the removal
    /// for the whole of it, which is what transfers the proof to the handle the deletion acts
    /// through. It is released the instant the removal answers true: on Windows that completes the
    /// handle-bound deletion and frees the name for a restore that may follow, and because the
    /// disposition names the <em>object</em> rather than the path, releasing it can delete nothing
    /// else. Where the removal refused or failed the reference is deliberately kept — the run may
    /// still have to act on that object, and re-acquiring it by name is exactly what this design
    /// does not do (D-125).
    /// </para>
    /// </summary>
    private static bool RemoveOwned(
        IPublicationFileSystem files,
        string path,
        RemovalProof isExpected,
        RecoveryGuard guard,
        PublicationReferences references)
    {
        guard.ThrowIfCancelled();

        // Nothing there is nothing to do. This is an optimization, not a check: an object that
        // appears between here and the removal is one the removal's own proof will refuse.
        if (!Exists(files, path))
        {
            references.Release(path);
            return true;
        }

        if (!guard.Allows(path))
        {
            return false;
        }

        // The object must be one this transaction HOLDS OPEN. Anchored here if the caller has not
        // already done so, and refused outright where it cannot be: the proof below compares an
        // identity, and an identity nobody holds is a number the host is free to have reissued
        // (D-125). Stating it at the removal itself — rather than trusting each pass to have
        // anchored first — is what stops a later call site reintroducing the gap.
        //
        // Holding it is the whole requirement, and no second observation is taken here. The removal
        // primitive already proves the object through the handle it deletes through, and while this
        // reference is alive that object's identifier cannot have been reissued to anything else, so
        // re-asking what the name resolves to would prove nothing the pair does not already prove —
        // at the cost of a file open on every removal, including the post-commit cleanup inside a
        // successful conversion. Where the transaction already holds the reference, which is every
        // in-process removal, this is a dictionary lookup and touches the filesystem not at all.
        if (references.Ensure(path) is null)
        {
            return false;
        }

        try
        {
            if (!files.Remove(path, isExpected))
            {
                return false;
            }
        }
        catch (Exception exception) when (FailureFamily.IsEnvironmentFailure(exception))
        {
            return false;
        }
        catch (Exception exception) when (FailureFamily.IsContractFault(exception))
        {
            throw new PublicationFaultException(exception);
        }

        references.Release(path);
        return true;
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
    // unexpected-fault exit rather than being blamed on the user's output location.
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
    // publication failure for the caller to classify; a contract defect is tagged at its origin,
    // so an internal misuse of the residue seam can never be reported as the user's
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

}
