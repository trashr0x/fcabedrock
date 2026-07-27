using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FcaBedrock.Cli.Publication;

/// <summary>
/// A newly created file: the stream to write it through, and the identity of the <b>exact
/// object</b> that creation produced.
/// <para>
/// The identity is read from the created file's own handle, while that handle is still open and
/// before a single byte is written. It cannot be obtained afterwards from the path: the file is
/// held <see cref="FileShare.None"/>, so a second open is refused, and observing after the close
/// would open a window in which the answer describes whatever now occupies the name rather than
/// the object this transaction created (CX-M7H-019/024).
/// </para>
/// <para>
/// <b>Successful creation is what creates ownership</b> (CX-M7H-036/037/038/044). A derived
/// private name predicts a path; only this value says which object now sits at it. Every control
/// file the transaction later publishes, verifies, or removes is bound to the identity reported
/// here, so a create-new that was <em>refused</em> — and therefore reported nothing — can never
/// authorize touching the occupant that refused it.
/// </para>
/// </summary>
/// <param name="Content">The write stream. The caller owns it.</param>
/// <param name="Identity">The created object's OS identity, or null where the host has none.</param>
internal readonly record struct CreatedFile(Stream Content, FileIdentityKey? Identity);

/// <summary>
/// Whether the object a removal has opened is the one the transaction meant to remove.
/// </summary>
/// <param name="identity">The object's OS identity, read from the removal's own handle.</param>
/// <param name="bytes">
/// The object's bytes, read from that same handle, or null when it is larger than
/// <see cref="PublicationTargets.MaxRecordBytes"/> — which no control document ever is.
/// </param>
internal delegate bool RemovalProof(FileIdentityKey? identity, byte[]? bytes);

/// <summary>
/// Every filesystem operation the publication transaction performs (D-123 point 7).
/// <para>
/// <b>One seam, no bypass.</b> Existence checks, create-new, flush-to-disk, rename, delete,
/// enumeration, and record reads all go through here, so a direct test can fail any single
/// boundary deterministically and observe exactly what the transaction does next. Production
/// is the real filesystem and nothing else.
/// </para>
/// <para>
/// <b>Deliberately not a path library.</b> Path composition stays in
/// <see cref="PublicationTargets"/>: the seam receives full paths and answers only about the
/// files at them, so failure injection cannot accidentally change which paths a run derives.
/// </para>
/// </summary>
internal interface IPublicationFileSystem
{
    /// <summary>True when a file exists at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>
    /// Creates <paramref name="path"/> for writing, <b>failing when it already exists</b>, and
    /// reports the identity of the object it created. The create-new semantics are load-bearing:
    /// a stage, a backup, and the transaction record may never silently replace something already
    /// there (CX-M7P-003), and the reported identity is what makes a <em>successful</em> creation
    /// — rather than a derived name — the thing that confers ownership (CX-M7H-037/044).
    /// <para>
    /// For <b>control</b> files only — the transaction record and its phase markers. They carry
    /// file names already visible in the directory listing and no converted data.
    /// </para>
    /// </summary>
    CreatedFile CreateNew(string path);

    /// <summary>
    /// Creates <paramref name="path"/> as <see cref="CreateNew"/> does, but restricted to the
    /// owner (CX-M7H-017/019/020).
    /// <para>
    /// Used for every <b>data-bearing</b> stage. A staged artifact holds converted user data and
    /// may survive a crash as recovery residue, so an unpredictable name is not enough: a name is
    /// not an access-control boundary, and the directory it sits in may be listable by others.
    /// The restriction is <b>durable</b> — a Unix creation mode, a Windows owner-only DACL — not
    /// merely a share mode, which lasts only as long as the handle (D-082).
    /// </para>
    /// </summary>
    CreatedFile CreateNewConfidential(string path);

    /// <summary>
    /// Flushes <paramref name="stream"/> all the way to disk. The transaction hashes and flushes
    /// a stage before anything is committed, so "the bytes are on disk" is a step the run takes
    /// rather than an assumption it makes.
    /// </summary>
    void Flush(Stream stream);

    /// <summary>
    /// Renames <paramref name="source"/> to <paramref name="destination"/> <b>without
    /// overwriting</b>. Every commit, backup, and restore is one of these: a same-directory,
    /// same-filesystem metadata rename, never a copy and never a rehash (CX-M7P-002).
    /// </summary>
    void Move(string source, string destination);

    /// <summary>
    /// Removes the object at <paramref name="path"/> — and <b>only</b> the object
    /// <paramref name="isExpected"/> accepts (CX-M7H-040).
    /// <para>
    /// <c>unlink</c> names a path, not a file, and a delete leaves no result whose identity could
    /// be checked afterwards, so an ownership proof taken <em>before</em> a path-based delete can
    /// still be a proof about a different object by the time the kernel acts. The proof is
    /// therefore never taken from a second look at the path: the identity and bytes handed to
    /// <paramref name="isExpected"/> are read from a handle opened on the object itself.
    /// </para>
    /// <para>
    /// <b>The guarantee is platform-divergent, and deliberately stated as such.</b> On Windows the
    /// handle carries <c>DELETE</c> access with no sharing and the deletion is requested against
    /// that handle, so no interval exists at all. On Unix, POSIX offers no unlink-by-descriptor —
    /// there is no <c>funlink</c>, and <c>unlinkat</c> still takes a name — so the object is proved
    /// from the open handle immediately before the path is unlinked, and any observable mismatch
    /// fails closed. <b>The Unix removal is not atomic</b>; that interval is a platform constraint,
    /// not a design choice.
    /// </para>
    /// <para>
    /// Returns <see langword="true"/> when the path no longer holds that object — removed, or
    /// already absent — and <see langword="false"/> when the object there is not the expected one,
    /// in which case <b>nothing is touched</b>.
    /// </para>
    /// </summary>
    /// <param name="path">The object's path.</param>
    /// <param name="isExpected">
    /// The proof, given the object's OS identity (or null where the host has none) and its bytes
    /// (or null when it is larger than <see cref="PublicationTargets.MaxRecordBytes"/>).
    /// </param>
    bool Remove(string path, RemovalProof isExpected);

    /// <summary>
    /// The files in <paramref name="directory"/> whose names begin with
    /// <paramref name="namePrefix"/> <b>ignoring case</b>, as full paths. Used once per run to
    /// discover this base's transaction records and private-name residue.
    /// <para>
    /// Case-insensitive on purpose, and only here: on a case-insensitive directory
    /// <c>OUT.fcabedrock-…</c> and <c>out.fcabedrock-…</c> name the same files, and residue that
    /// discovery cannot see is residue a later run can act on destructively (CX-M7H-016). This
    /// yields a deliberate <em>superset</em>; whether a case-variant entry really belongs to this
    /// run's namespace is then settled by actual filesystem identity, not by the comparison used
    /// here.
    /// </para>
    /// </summary>
    IReadOnlyList<string> EnumerateFiles(string directory, string namePrefix);

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> bytes from <paramref name="path"/>, returning
    /// <see langword="null"/> when the file is larger.
    /// <para>
    /// The bound is a security property, not an optimization: a transaction record is discovered
    /// by <em>name</em>, and an unrelated — possibly hostile — file can wear that name. Reading
    /// it whole would let a lookalike dictate an unbounded allocation before a single validity
    /// rule has run. An over-long file is simply not a record this run wrote, so it is refused
    /// unread and left untouched.
    /// </para>
    /// </summary>
    byte[]? ReadBounded(string path, int maxBytes);
}

/// <summary>The real filesystem; the only production implementation.</summary>
internal sealed class PublicationFileSystem : IPublicationFileSystem
{
    private PublicationFileSystem()
    {
    }

    /// <summary>The shared instance.</summary>
    public static PublicationFileSystem Instance { get; } = new();

    /// <inheritdoc/>
    public bool Exists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public CreatedFile CreateNew(string path)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return Created(stream);
    }

    /// <inheritdoc/>
    public CreatedFile CreateNewConfidential(string path) =>
        Created(OperatingSystem.IsWindows() ? CreateOwnerOnlyWindows(path) : CreateOwnerOnlyUnix(path));

    // Read from THIS handle, before any byte is written and long before it closes. The path
    // cannot answer the question — the file is held FileShare.None — and asking after the close
    // would describe whatever occupies the name by then.
    private static CreatedFile Created(FileStream stream) =>
        new(stream, FileIdentityInterop.TryGetIdentity(stream.SafeFileHandle));

    /// <inheritdoc/>
    public void Flush(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // flushToDisk: the ordinary Flush only pushes the managed buffer into the OS. A staged
        // artifact is about to be committed by rename, so the bytes must be durable first.
        if (stream is FileStream file)
        {
            file.Flush(flushToDisk: true);
            return;
        }

        stream.Flush();
    }

    /// <inheritdoc/>
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: false);

    /// <inheritdoc/>
    public bool Remove(string path, RemovalProof isExpected)
    {
        ArgumentNullException.ThrowIfNull(isExpected);
        return OperatingSystem.IsWindows() ? RemoveWindows(path, isExpected) : RemoveUnix(path, isExpected);
    }

    // The object is opened with DELETE access and NO sharing, so from the instant it is proved to
    // the instant it is unlinked nothing else can rename the name away, delete the object, or put
    // a different one there; the deletion is then requested against that handle rather than
    // against the path. There is no interval at all (CX-M7H-040).
    [SupportedOSPlatform("windows")]
    private static bool RemoveWindows(string path, RemovalProof isExpected)
    {
        using var handle = FileIdentityInterop.TryOpenForRemoval(Path.GetFullPath(path));
        if (handle is null)
        {
            return true;
        }

        if (!isExpected(FileIdentityInterop.TryGetWindowsIdentity(handle), ReadBounded(handle)))
        {
            // Not ours. The handle closes with no disposition set, so the object is untouched.
            return false;
        }

        FileIdentityInterop.MarkForDeletion(handle);
        return true;
    }

    // POSIX exposes no unlink-by-descriptor — no `funlink`, and `unlinkat` still takes a name — so
    // this is NOT atomic and is not claimed to be. What it does guarantee: the object's identity
    // and bytes are proved from the open handle, that proof is the last thing done before the
    // unlink, any mismatch fails closed without touching anything, and the handle is still held
    // across the unlink so the object cannot be reclaimed underneath it.
    private static bool RemoveUnix(string path, RemovalProof isExpected)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }

        using (stream)
        {
            var identity = FileIdentityInterop.TryGetIdentity(stream.SafeFileHandle);
            var bytes = ReadBounded(stream);
            if (!isExpected(identity, bytes))
            {
                return false;
            }

            File.Delete(path);
        }

        return true;
    }

    // One byte past the bound, exactly as ReadBounded does: an object larger than any control
    // document this code writes answers "null", which no content proof accepts.
    private static byte[]? ReadBounded(SafeFileHandle handle)
    {
        var buffer = new byte[PublicationTargets.MaxRecordBytes + 1];
        var read = 0;
        int chunk;
        while (read < buffer.Length && (chunk = RandomAccess.Read(handle, buffer.AsSpan(read), read)) > 0)
        {
            read += chunk;
        }

        return read > PublicationTargets.MaxRecordBytes ? null : buffer.AsSpan(0, read).ToArray();
    }

    private static byte[]? ReadBounded(FileStream stream) => ReadBounded(stream.SafeFileHandle);

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateFiles(string directory, string namePrefix)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(namePrefix);

        // A location that does not exist holds no residue. Saying so here keeps preflight's one
        // discovery step from turning a missing output directory into a recovery failure; the
        // real problem surfaces where it belongs, when the record cannot be created.
        var matches = new List<string>();
        if (!Directory.Exists(directory))
        {
            return matches;
        }

        // Enumerated with no wildcard and filtered here rather than through the platform's own
        // pattern matching, whose case behaviour varies with the host. The filter is deliberately
        // case-INSENSITIVE so no residue can hide from discovery; membership is decided afterwards
        // by filesystem identity (CX-M7H-016).
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (Path.GetFileName(path).StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(path);
            }
        }

        // Ordinal order, so preflight examines residue in the same sequence on every run and
        // any failure it reports is reproducible.
        matches.Sort(StringComparer.Ordinal);
        return matches;
    }

    // The creation mode is applied AT creation rather than afterwards: a chmod after the fact
    // leaves a window in which the bytes exist under the ambient umask.
    [UnsupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnlyUnix(string path) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });

    // An explicit owner-only DACL with inheritance disabled, applied at creation, exactly as the
    // spool workspace does (D-082): FileShare.None guards only a live handle, and the disclosure
    // case is precisely a stage that OUTLIVES its process as crash residue. Fail-closed — if the
    // DACL cannot be established the creation throws rather than proceeding unprotected.
    [SupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnlyWindows(string path)
    {
        var owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows identity has no security identifier.");

        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));

        return new FileInfo(path).Create(
            FileMode.CreateNew,
            FileSystemRights.Write | FileSystemRights.Synchronize,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None,
            security);
    }

    /// <inheritdoc/>
    public byte[]? ReadBounded(string path, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();

        // One byte past the bound is read on purpose: it is what distinguishes "exactly at the
        // bound" from "larger than the bound" without trusting a reported length.
        var remaining = maxBytes + 1;
        var chunk = new byte[4096];
        while (remaining > 0)
        {
            var read = stream.Read(chunk, 0, Math.Min(chunk.Length, remaining));
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            remaining -= read;
        }

        return buffer.Length > maxBytes ? null : buffer.ToArray();
    }
}
