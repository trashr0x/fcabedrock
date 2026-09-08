using Microsoft.Win32.SafeHandles;

namespace FcaBedrock.Cli.Publication;

/// <summary>
/// A live reference to one filesystem object, held for as long as that object's identity authorizes
/// a mutation (D-125).
/// <para>
/// <b>Why an identifier alone is not ownership.</b> A device/inode pair, or a Windows volume plus
/// file id, identifies an object only while that object exists. Unlink the last name of a file on
/// ext4 and the inode is free immediately; the very next creation in that group can be handed the
/// same number. A proof taken from a closed handle and re-checked later therefore cannot tell "the
/// object I created" from "a different object wearing its identifier" — the substitution this
/// transaction exists to refuse. Holding the object open closes that hole at the source: while a
/// reference is alive the identifier cannot be reissued, so a substitute necessarily compares
/// unequal and every existing proof becomes sound without changing what it compares.
/// </para>
/// <para>
/// <b>It is a lifetime reference and nothing else.</b> Not a writer stream, not a reader lock, not
/// a content snapshot. It never holds bytes, never blocks another process, and never moves the
/// writer's flush-and-close boundary: the platform handle is chosen precisely so that it coexists
/// with this transaction's own creation, re-observation, rename and removal opens
/// (<see cref="PublicationNative.TryOpenReference"/>).
/// </para>
/// <para>
/// <b>What it cannot do.</b> It says nothing about an interval in which no process held it — the
/// namespace across a crash is the caller's precondition, not this type's guarantee — and on Unix
/// it cannot make the final proof-to-unlink interval atomic, because POSIX has no
/// compare-and-delete by descriptor.
/// </para>
/// </summary>
internal sealed class PublicationObjectReference : IDisposable
{
    private readonly SafeFileHandle _handle;

    private PublicationObjectReference(SafeFileHandle handle, FileIdentityKey identity)
    {
        _handle = handle;
        Identity = identity;
    }

    /// <summary>The operating system's identity for the object this reference holds open.</summary>
    public FileIdentityKey Identity { get; }

    /// <summary>
    /// Takes a reference to whatever is at <paramref name="fullPath"/> right now, or answers null
    /// when the host cannot supply one — a missing file, a refusal, or a platform with no identity
    /// at all. <b>A failed acquisition grants no mutation authority</b>; every caller fails closed
    /// on it rather than proceeding on an identity it cannot anchor.
    /// </summary>
    public static PublicationObjectReference? TryAcquire(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        var handle = PublicationNative.TryOpenReference(fullPath);
        if (handle is null)
        {
            return null;
        }

        // Read from THIS handle, never from the path: the answer must describe the object the
        // reference now holds, not whatever the name resolves to a moment later.
        var identity = FileIdentityInterop.TryGetIdentity(handle);
        if (identity is not { } key || !key.IsOperatingSystemIdentity)
        {
            handle.Dispose();
            return null;
        }

        return new PublicationObjectReference(handle, key);
    }

    /// <summary>
    /// Takes a reference and proves it is the object <paramref name="expected"/> names, for use
    /// while the acquiring creation handle is <b>still open</b> — which is what makes the reference
    /// provably the object that creation produced rather than one that replaced it in between.
    /// Answers null when the reference cannot be taken or the proof fails.
    /// </summary>
    public static PublicationObjectReference? TryAcquire(string fullPath, FileIdentityKey? expected)
    {
        if (expected is not { } key)
        {
            return null;
        }

        var reference = TryAcquire(fullPath);
        if (reference is null)
        {
            return null;
        }

        if (reference.Identity == key)
        {
            return reference;
        }

        reference.Dispose();
        return null;
    }

    /// <summary>
    /// Whether <paramref name="path"/> still resolves to the very object this reference holds. A
    /// name that now resolves elsewhere — or to nothing — answers false, and authorizes nothing.
    /// </summary>
    public bool IsStillAt(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // A fresh service, never a memoized one: the question is what this name resolves to now.
        return FileIdentity.CreateDefault().KeyFor(path) == Identity;
    }

    /// <inheritdoc/>
    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Every live reference one transaction holds, keyed by the full path the object was acquired at,
/// so acquisition, transfer and release are one bookkeeping decision rather than a habit spread
/// across the transaction.
/// <para>
/// <b>Two rules make the invariant hold.</b> A path is never released and then re-acquired by name
/// while it still authorizes something — a rename <em>re-keys</em> the same reference instead. And
/// a reference is released only once its object's last authorized use is done: on Windows that is
/// after the handle-bound removal has set the disposition, so the deletion completes and the name
/// becomes reusable rather than staying delete-pending under a reference nobody needs any more.
/// </para>
/// </summary>
internal sealed class PublicationReferences(IPublicationFileSystem files) : IDisposable
{
    private readonly Dictionary<string, PublicationObjectReference> _byPath = new(StringComparer.Ordinal);

    /// <summary>
    /// The reference for <paramref name="path"/>, taking one now if this transaction does not
    /// already hold it. Null means no reference could be taken, which authorizes nothing.
    /// </summary>
    public PublicationObjectReference? Ensure(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_byPath.TryGetValue(path, out var held))
        {
            return held;
        }

        // Through the seam like every other filesystem operation, so a test observes and can fail
        // the acquisition at the same boundary it fails a create, a rename or a delete.
        var acquired = files.TryAcquire(path);
        if (acquired is not null)
        {
            _byPath[path] = acquired;
        }

        return acquired;
    }

    /// <summary>Takes ownership of a reference acquired elsewhere — from a creation handle.</summary>
    public void Adopt(string path, PublicationObjectReference reference)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(reference);

        // A second reference to one path would mean two handles deferring the same Windows
        // deletion, and only one of them tracked at the moment the name must become free.
        Release(path);
        _byPath[path] = reference;
    }

    /// <summary>The reference held for <paramref name="path"/>, or null.</summary>
    public PublicationObjectReference? Of(string path) => _byPath.GetValueOrDefault(path);

    /// <summary>
    /// Follows a successful rename: the object is unchanged and its reference stays open, so only
    /// the name it is filed under moves. This is what "never release and re-acquire by path" looks
    /// like in practice.
    /// </summary>
    public void Rekey(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (!_byPath.Remove(from, out var reference))
        {
            return;
        }

        Release(to);
        _byPath[to] = reference;
    }

    /// <summary>Releases the reference for <paramref name="path"/>, if one is held.</summary>
    public void Release(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_byPath.Remove(path, out var reference))
        {
            reference.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var reference in _byPath.Values)
        {
            reference.Dispose();
        }

        _byPath.Clear();
    }
}
