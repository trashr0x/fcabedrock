using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FcaBedrock.Cli.Publication;

/// <summary>What an exclusive, non-overwriting rename attempt actually did.</summary>
internal enum ExclusiveRename
{
    /// <summary>The rename happened, without replacing anything.</summary>
    Renamed,

    /// <summary>Something already occupies the destination. <b>Never</b> a capability signal.</summary>
    Collision,

    /// <summary>
    /// This filesystem cannot perform the flagged, atomically non-replacing operation — the one
    /// result that permits the guarded classic fallback (D-125, approved choice B).
    /// </summary>
    CapabilityAbsent,

    /// <summary>Any other failure. Never permits a second, weaker move.</summary>
    Failed,
}

/// <summary>An exclusive-rename attempt's outcome and the error it was decided from.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Error">The errno or Win32 error captured immediately from the call; 0 on success.</param>
internal readonly record struct ExclusiveRenameResult(ExclusiveRename Outcome, int Error);

/// <summary>What a directory lookup found at a name, without following a final symbolic link.</summary>
internal enum EntryLookup
{
    /// <summary>The leaf does not exist — the <b>only</b> result that establishes absence.</summary>
    Absent,

    /// <summary>An entry of some kind is there: a file, a directory, or a dangling symbolic link.</summary>
    Present,

    /// <summary>The lookup itself failed, so absence is not established and the operation refuses.</summary>
    Failed,
}

/// <summary>
/// The three native operations the publication rename is built from, behind one seam so a test can
/// inject an <em>exact</em> capability or error outcome while the successful path still calls the
/// real primitive (D-125).
/// <para>
/// It is deliberately not a filesystem abstraction: it exposes exactly these three calls, takes
/// full paths, and decides nothing. The guarded protocol lives in
/// <see cref="PublicationRename"/>.
/// </para>
/// </summary>
internal interface IPublicationRenamePrimitives
{
    /// <summary>
    /// One attempt at the platform's atomically non-replacing rename: Linux
    /// <c>renameat2(RENAME_NOREPLACE)</c>, macOS <c>renamex_np(RENAME_EXCL)</c>, Windows
    /// <c>MoveFileExW</c> with no flags at all.
    /// </summary>
    ExclusiveRenameResult Exclusive(string source, string destination);

    /// <summary>
    /// A native <c>lstat</c> of <paramref name="path"/>'s leaf. It does not follow a final symbolic
    /// link, so a dangling link is <see cref="EntryLookup.Present"/> rather than absent —
    /// <c>File.Exists</c> returning false cannot say that.
    /// </summary>
    EntryLookup Lookup(string path);

    /// <summary>
    /// Exactly one classic, flagless native <c>rename</c>. Returns 0, or the errno captured
    /// immediately from the call. Reached only after a permitted capability result.
    /// </summary>
    int Classic(string source, string destination);
}

/// <summary>
/// The exclusive-first native rename and its narrowly gated fallback (D-125, approved choice B).
/// <para>
/// <b>Every publication rename is a same-directory, same-filesystem metadata rename.</b> It is
/// never a content copy, a clone, a link/unlink pair, a copy/delete pair, a destination pre-delete,
/// or a managed <c>File.Move</c> — the managed Unix non-overwrite path performs its own
/// check-then-rename and can end in <c>link</c>+<c>unlink</c> or copy+delete, which is precisely
/// what this seam must not do.
/// </para>
/// <para>
/// <b>The fallback's limit is disclosed, not repaired.</b> Between the final absence check and the
/// classic rename an actor that violates the exclusive-namespace precondition can create the
/// destination, and the classic rename can then replace it. A post-move identity match proves which
/// <em>source</em> object arrived; it cannot prove the destination stayed absent, nor restore an
/// overwritten foreign entry. This applies at every destination role — a public artifact or
/// manifest, a backup or private control name, and a source name used as a compensation
/// destination. Windows keeps its stronger native no-replace behaviour and has no fallback at all.
/// </para>
/// </summary>
internal static class PublicationRename
{
    /// <summary>
    /// Renames <paramref name="source"/> to <paramref name="destination"/> without overwriting,
    /// through the exclusive primitive where the filesystem has one and through the guarded classic
    /// fallback where — and only where — the capability matrix permits it.
    /// </summary>
    /// <param name="primitives">The native calls.</param>
    /// <param name="source">The full source path.</param>
    /// <param name="destination">The full destination path, a sibling of the source.</param>
    /// <param name="sourceReference">
    /// The live reference authorizing this move, when the caller holds one. It is what makes the
    /// fallback's source revalidation a statement about the <em>object</em> rather than the name.
    /// </param>
    public static void Move(
        IPublicationRenamePrimitives primitives,
        string source,
        string destination,
        PublicationObjectReference? sourceReference)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        // The same-directory precondition is structural rather than a property every call site has
        // to remember: it is what makes "same filesystem" true by construction, and what makes the
        // absence of any copy flag safe. A cross-directory request is a defect in this code, so it
        // takes the contract-fault boundary and never reaches a native call.
        if (!string.Equals(
                Path.GetDirectoryName(source), Path.GetDirectoryName(destination), StringComparison.Ordinal))
        {
            throw new NotSupportedException("A publication rename must stay within one directory.");
        }

        var attempt = primitives.Exclusive(source, destination);
        switch (attempt.Outcome)
        {
            case ExclusiveRename.Renamed:
                return;

            case ExclusiveRename.Collision:
                throw new IOException("The publication destination already exists.");

            case ExclusiveRename.CapabilityAbsent:
                Fallback(primitives, source, destination, sourceReference);
                return;

            default:
                throw PublicationNative.Failure(attempt.Error);
        }
    }

    // Reached only after exactly one permitted capability result, and it performs exactly one
    // further native rename: no retry, no second fallback, no placeholder, no pre-delete.
    private static void Fallback(
        IPublicationRenamePrimitives primitives,
        string source,
        string destination,
        PublicationObjectReference? sourceReference)
    {
        // 1. The failed exclusive attempt is not evidence that the namespace stayed still. Where the
        //    caller holds the authorizing reference, the source must still BE that object — a name
        //    that now resolves elsewhere authorizes nothing.
        if (sourceReference is not null && !sourceReference.IsStillAt(source))
        {
            throw new IOException("The publication source is no longer the object this run owns.");
        }

        // 2. Destination ENTRY absence, immediately before the rename. Only a missing leaf
        //    establishes it; any entry at all — including a directory or a dangling symbolic link —
        //    is a collision, and a lookup that failed establishes nothing.
        switch (primitives.Lookup(destination))
        {
            case EntryLookup.Absent:
                break;

            case EntryLookup.Present:
                throw new IOException("The publication destination already exists.");

            default:
                throw new IOException("The publication destination could not be inspected.");
        }

        // 3. Exactly one classic native rename.
        var error = primitives.Classic(source, destination);
        if (error != 0)
        {
            throw PublicationNative.Failure(error);
        }
    }
}

/// <summary>
/// The platform primitives themselves. Every entry point is source-generated
/// (<c>LibraryImport</c>), the errno or Win32 error is captured immediately from the indicated
/// call, and capability is never inferred from an exception message, an operating-system name, a
/// filesystem label, or a later operation.
/// </summary>
internal static partial class PublicationNative
{
    /// <summary>The production primitives for this host.</summary>
    public static IPublicationRenamePrimitives Primitives { get; } = SelectPrimitives();

    private static IPublicationRenamePrimitives SelectPrimitives()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPrimitives();
        }

        return new UnixPrimitives();
    }

    /// <summary>
    /// A lifetime handle on the object at <paramref name="fullPath"/>, or null when one cannot be
    /// taken. It keeps the object <em>allocated</em> so its identifier cannot be recycled; it is
    /// not a reader lock, a writer stream, or a content snapshot.
    /// <para>
    /// On Windows this is a non-inheritable, attribute-only open (<c>FILE_READ_ATTRIBUTES</c>) that
    /// shares read, write and delete. An open requesting none of read-data, write-data or delete
    /// access does not take part in share-access checking, which is what lets it coexist with the
    /// <see cref="FileShare.None"/> creation handle, with the later data-read re-observations, and
    /// with the share-mode-zero handle the removal deletes through.
    /// </para>
    /// <para>
    /// On Unix it is a raw close-on-exec <c>open(O_RDONLY)</c>. It is deliberately not a managed
    /// open: .NET emulates <see cref="FileShare"/> with advisory locks, so a managed handle would
    /// take <c>LOCK_SH</c> and make the removal's own <c>LOCK_EX</c> fail.
    /// </para>
    /// </summary>
    public static SafeFileHandle? TryOpenReference(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        if (OperatingSystem.IsWindows())
        {
            return OpenWindowsReference(fullPath);
        }

        return OpenUnixReference(fullPath);
    }

    /// <summary>
    /// The exception a native failure becomes. The <em>types</em> are exactly the ones the managed
    /// move raised before, so every existing failure classification, message and exit is unchanged
    /// (<see cref="FailureFamily"/>); no operating-system text ever reaches the user.
    /// </summary>
    public static Exception Failure(int error)
    {
        if (OperatingSystem.IsWindows())
        {
            return error switch
            {
                ErrorAccessDenied => new UnauthorizedAccessException("The publication rename was refused."),
                ErrorFileNotFound => new FileNotFoundException("The publication source no longer exists."),
                ErrorPathNotFound => new DirectoryNotFoundException("The publication directory no longer exists."),
                _ => new IOException("The publication rename failed.", error),
            };
        }

        return error switch
        {
            UnixEAccess or UnixEPerm or UnixERofs =>
                new UnauthorizedAccessException("The publication rename was refused."),
            UnixENoEnt => new FileNotFoundException("The publication source no longer exists."),
            _ => new IOException("The publication rename failed.", error),
        };
    }

    // ---- Windows -------------------------------------------------------------------------------

    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;

    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;

    // The traditional path limit. At or above it the Win32 call needs the extended prefix, exactly
    // as the managed move applies it — so replacing that wrapper changes no path's reach.
    private const int MaxShortPath = 260;

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle? OpenWindowsReference(string fullPath)
    {
        // A null security descriptor means a non-inheritable handle, which is what this must be:
        // it exists to hold an object alive inside this process, never to be handed to a child.
        var raw = CreateFile(
            Extended(fullPath),
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        // Any failure means no reference, and no reference means no mutation authority. The caller
        // fails closed on that; it never proceeds on an identity it cannot anchor.
        return raw == -1 ? null : new SafeFileHandle(raw, ownsHandle: true);
    }

    private static string Extended(string path)
    {
        if (path.Length < MaxShortPath
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }

        return Path.IsPathFullyQualified(path) ? @"\\?\" + path : path;
    }

    /// <summary>Windows: one <c>MoveFileExW</c> with no flags — no replacement, and no copy.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class WindowsPrimitives : IPublicationRenamePrimitives
    {
        public ExclusiveRenameResult Exclusive(string source, string destination)
        {
            if (MoveFileEx(Extended(source), Extended(destination), 0))
            {
                return new ExclusiveRenameResult(ExclusiveRename.Renamed, 0);
            }

            var error = Marshal.GetLastPInvokeError();

            // Windows answers the collision itself and has no capability gap to fall back through:
            // its no-replace behaviour is the primitive, not a flag on top of one.
            return new ExclusiveRenameResult(
                error is ErrorFileExists or ErrorAlreadyExists ? ExclusiveRename.Collision : ExclusiveRename.Failed,
                error);
        }

        // Unreachable by construction: Windows never reports an absent capability, so nothing ever
        // enters the fallback. Reaching either of these would be a defect in this file.
        public EntryLookup Lookup(string path) =>
            throw new NotSupportedException("Windows publication renames never use the fallback.");

        public int Classic(string source, string destination) =>
            throw new NotSupportedException("Windows publication renames never use the fallback.");
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    // ---- Unix ----------------------------------------------------------------------------------

    // Linux and Darwin agree on these three; the ones they disagree about are spelled per platform.
    private const int UnixENoEnt = 2;
    private const int UnixEPerm = 1;
    private const int UnixEAccess = 13;
    private const int UnixEExist = 17;
    private const int UnixEInval = 22;
    private const int UnixERofs = 30;

    private const int LinuxENoSys = 38;
    private const int LinuxEOpNotSupp = 95;

    private const int DarwinENotSup = 45;

    private const int AtFdCwd = -100;
    private const uint RenameNoReplace = 1;
    private const uint RenameExcl = 4;

    private const int OpenReadOnly = 0;
    private const int LinuxOCloExec = 0x80000;
    private const int DarwinOCloExec = 0x1000000;

    // Comfortably larger than struct stat everywhere this ships; nothing in it is read, because the
    // only question asked here is whether the lookup succeeded at all.
    private const int StatBufferBytes = 512;

    private static bool IsDarwin => OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();

    [UnsupportedOSPlatform("windows")]
    private static SafeFileHandle? OpenUnixReference(string fullPath)
    {
        var path = NulTerminated(fullPath);
        var descriptor = Open(ref path[0], OpenReadOnly | (IsDarwin ? DarwinOCloExec : LinuxOCloExec));
        return descriptor < 0 ? null : new SafeFileHandle(descriptor, ownsHandle: true);
    }

    private static byte[] NulTerminated(string path)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(path) + 1];
        Encoding.UTF8.GetBytes(path, bytes);
        return bytes;
    }

    /// <summary>
    /// Unix: the exclusive primitive first, and the capability matrix decided from the errno that
    /// call reported — never from a name, a message, or a later operation.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private sealed class UnixPrimitives : IPublicationRenamePrimitives
    {
        public ExclusiveRenameResult Exclusive(string source, string destination)
        {
            var from = NulTerminated(source);
            var to = NulTerminated(destination);

            var failed = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
                ? RenameExclusiveDarwin(ref from[0], ref to[0], RenameExcl) != 0
                : RenameAt2(AtFdCwd, ref from[0], AtFdCwd, ref to[0], RenameNoReplace) != 0;

            if (!failed)
            {
                return new ExclusiveRenameResult(ExclusiveRename.Renamed, 0);
            }

            var error = Marshal.GetLastPInvokeError();
            return new ExclusiveRenameResult(
                ClassifyUnixRename(error, OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()),
                error);
        }

        public EntryLookup Lookup(string path)
        {
            var name = NulTerminated(path);
            Span<byte> buffer = stackalloc byte[StatBufferBytes];
            buffer.Clear();

            if (LStat(ref name[0], ref MemoryMarshal.GetReference(buffer)) == 0)
            {
                // Something is there. A regular file, a directory, or a symbolic link whose target
                // does not exist — lstat does not follow the final link, which is exactly why
                // `File.Exists == false` cannot establish absence.
                return EntryLookup.Present;
            }

            // Only a missing leaf establishes absence. Every other lookup failure leaves the
            // question unanswered, and an unanswered question refuses the rename.
            return Marshal.GetLastPInvokeError() == UnixENoEnt ? EntryLookup.Absent : EntryLookup.Failed;
        }

        public int Classic(string source, string destination)
        {
            var from = NulTerminated(source);
            var to = NulTerminated(destination);
            return Rename(ref from[0], ref to[0]) == 0 ? 0 : Marshal.GetLastPInvokeError();
        }

    }

    /// <summary>
    /// The exhaustive permission gate: which errno from an <em>exclusive rename attempt</em> means
    /// "this filesystem cannot do that operation", and which means the operation simply failed.
    /// Anything not named here is an ordinary failure, and an ordinary failure never switches
    /// primitives.
    /// <para>
    /// The platform is a parameter rather than an ambient check, because the two tables genuinely
    /// differ and both have to be provable from either host. Which table a real call uses is a
    /// separate fact, proved by the native fast-path witnesses.
    /// </para>
    /// </summary>
    /// <param name="error">The errno captured immediately from the exclusive rename.</param>
    /// <param name="darwin">Whether the call was Apple's <c>renamex_np</c> rather than Linux's.</param>
    internal static ExclusiveRename ClassifyUnixRename(int error, bool darwin)
    {
        if (error == UnixEExist)
        {
            // A collision, and on Linux one the VFS decides before the filesystem's own rename is
            // even called. It is never a capability signal — not even if a later look finds the
            // destination gone again.
            return ExclusiveRename.Collision;
        }

        if (darwin)
        {
            // Apple documents ENOTSUP for a flag the filesystem does not support and EINVAL for an
            // invalid one, so Darwin's EINVAL is a failure. Its ENOSYS and its distinct modern
            // EOPNOTSUPP (102) are not documented as this API's capability answer, and Linux's
            // errno readings are deliberately not copied onto Darwin.
            return error == DarwinENotSup ? ExclusiveRename.CapabilityAbsent : ExclusiveRename.Failed;
        }

        // Linux's VFS contract requires a filesystem to answer EINVAL for a flag it does not
        // support, so EINVAL is the capability answer here — and the invocation is valid by
        // construction: one fixed flag, a correctly bound entry point, and two distinct sibling
        // leaves in a directory this transaction already resolved and guarded.
        //
        // ENOSYS covers a kernel that does not implement the call. A libc wrapper may fold that
        // into EINVAL, which the row above already accepts; where it is reported directly, it is
        // accepted here. Neither is ever fabricated from a missing or misbound import.
        return error is UnixEInval or LinuxEOpNotSupp or LinuxENoSys
            ? ExclusiveRename.CapabilityAbsent
            : ExclusiveRename.Failed;
    }

    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static partial int Open(ref byte path, int flags);

    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static partial int LStat(ref byte path, ref byte statBuffer);

    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "rename", SetLastError = true)]
    private static partial int Rename(ref byte from, ref byte to);

    [UnsupportedOSPlatform("windows")]
    [LibraryImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static partial int RenameAt2(
        int oldDirectory, ref byte oldPath, int newDirectory, ref byte newPath, uint flags);

    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("maccatalyst")]
    [LibraryImport("libc", EntryPoint = "renamex_np", SetLastError = true)]
    private static partial int RenameExclusiveDarwin(ref byte from, ref byte to, uint flags);
}
