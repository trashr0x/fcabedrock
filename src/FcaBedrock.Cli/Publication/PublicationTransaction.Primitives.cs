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
    /// The gate in front of it is unchanged: the exact host token, then a fresh check that this
    /// path is not one of the run's own inputs. Returns true when the path no
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
