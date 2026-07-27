using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FcaBedrock.Cli;

/// <summary>
/// The strictly necessary platform interop behind <see cref="FileIdentity"/>. .NET exposes
/// no managed API for a file's OS identity, and D-122 part 11 names symlink <b>and
/// hardlink</b> aliasing as behaviour the host must resolve where the platform exposes it —
/// which path normalization cannot do. No dependency is added; both entry points are
/// source-generated (<c>LibraryImport</c>, since <c>DllImport</c> is a build error here).
/// </summary>
internal static partial class FileIdentityInterop
{
    // FILE_INFO_BY_HANDLE_CLASS.FileIdInfo.
    private const int FileIdInfoClass = 18;

    // FILE_INFO_BY_HANDLE_CLASS.FileDispositionInfo.
    private const int FileDispositionInfoClass = 4;

    private const uint DeleteAccess = 0x00010000;
    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;
    private const int AccessDenied = 5;

    /// <summary>
    /// Opens <paramref name="fullPath"/> for an <b>identity-bound removal</b>: read access to prove
    /// what the object is, <c>DELETE</c> access to remove it through this very handle, and no
    /// sharing at all — so between the proof and the removal nobody can rename the name away,
    /// delete the object, or put a different one there (CX-M7H-040).
    /// <para>
    /// Returns null when nothing is at that path. Deletion is <em>not</em> requested here: the
    /// disposition is set only after the caller has proved the object, so a handle opened on
    /// something unowned is simply closed and the file survives.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static SafeFileHandle? TryOpenForRemoval(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        var raw = CreateFile(fullPath, DeleteAccess | GenericRead, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (raw != -1)
        {
            return new SafeFileHandle(raw, ownsHandle: true);
        }

        var error = Marshal.GetLastPInvokeError();
        return error switch
        {
            FileNotFound or PathNotFound => null,
            AccessDenied => throw new UnauthorizedAccessException("The publication object could not be opened."),
            _ => throw new IOException("The publication object could not be opened.", error),
        };
    }

    /// <summary>
    /// Marks the object <paramref name="handle"/> refers to for deletion, which takes effect when
    /// the handle closes. It names the <b>object</b>, not the path, so no interval exists in which
    /// the name could come to mean something else.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static void MarkForDeletion(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
                handle, FileDispositionInfoClass, ref disposition, (uint)Marshal.SizeOf<FileDispositionInfo>()))
        {
            throw new IOException(
                "The publication object could not be marked for deletion.", Marshal.GetLastPInvokeError());
        }
    }

    // Comfortably larger than struct stat on every supported platform (Linux arm64 128,
    // Linux x64 / macOS 144, FreeBSD 224). Only the first 16 bytes are read.
    private const int StatBufferBytes = 512;

    /// <summary>Windows OS identity for an open handle, or null when the call fails.</summary>
    [SupportedOSPlatform("windows")]
    internal static FileIdentityKey? TryGetWindowsIdentity(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return GetFileInformationByHandleEx(handle, FileIdInfoClass, out var info, (uint)Marshal.SizeOf<FileIdInfo>())
            ? FileIdentityKey.FromOperatingSystem(info.VolumeSerialNumber, info.FileIdLow, info.FileIdHigh)
            : null;
    }

    /// <summary>
    /// OS identity for an <b>already open</b> handle, or null when the host has none.
    /// <para>
    /// The handle route is not an optimization: a file created <see cref="FileShare.None"/> — as
    /// every data-bearing stage is — cannot be identified through its path while it is held, and
    /// identifying it after the close would describe whatever occupies the name by then rather
    /// than the object that was created (CX-M7H-019/024).
    /// </para>
    /// </summary>
    internal static FileIdentityKey? TryGetIdentity(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (OperatingSystem.IsWindows())
        {
            return TryGetWindowsIdentity(handle);
        }

        try
        {
            return TryGetUnixIdentity(handle);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Unix device/inode identity for an open descriptor, or null when <c>fstat</c> fails.</summary>
    private static FileIdentityKey? TryGetUnixIdentity(SafeFileHandle handle)
    {
        var referenced = false;
        try
        {
            handle.DangerousAddRef(ref referenced);
            Span<byte> buffer = stackalloc byte[StatBufferBytes];
            buffer.Clear();
            return Fstat((int)handle.DangerousGetHandle(), ref MemoryMarshal.GetReference(buffer)) != 0
                ? null
                : ReadStat(buffer);
        }
        finally
        {
            if (referenced)
            {
                handle.DangerousRelease();
            }
        }
    }

    /// <summary>Unix device/inode identity for <paramref name="fullPath"/>, or null when <c>stat</c> fails.</summary>
    internal static FileIdentityKey? TryGetUnixIdentity(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        // The path is marshalled by hand as NUL-terminated UTF-8 so the signature stays
        // fully blittable; the buffer is opaque bytes for the same reason — no struct stat
        // layout is declared, only the two offsets that are stable across the supported
        // platforms.
        var path = new byte[Encoding.UTF8.GetByteCount(fullPath) + 1];
        Encoding.UTF8.GetBytes(fullPath, path);

        Span<byte> buffer = stackalloc byte[StatBufferBytes];
        buffer.Clear();
        return Stat(ref path[0], ref MemoryMarshal.GetReference(buffer)) != 0 ? null : ReadStat(buffer);
    }

    // st_ino is a 64-bit field at offset 8 on Linux, macOS and FreeBSD alike. st_dev sits at
    // offset 0 and is 64-bit there too, except on Darwin where it is a 32-bit dev_t followed by
    // st_mode/st_nlink — fields that CHANGE (chmod, a new hardlink), so reading 8 bytes there
    // would make identity unstable rather than merely wider.
    private static FileIdentityKey ReadStat(ReadOnlySpan<byte> buffer)
    {
        var device = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            ? BitConverter.ToUInt32(buffer[..4])
            : BitConverter.ToUInt64(buffer[..8]);
        var inode = BitConverter.ToUInt64(buffer.Slice(8, 8));
        return FileIdentityKey.FromOperatingSystem(device, inode, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    // FILE_DISPOSITION_INFO: a single BOOLEAN.
    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int fileInformationClass, out FileIdInfo fileInformation, uint size);

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(
        SafeFileHandle handle, int fileInformationClass, ref FileDispositionInfo fileInformation, uint size);

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

    [LibraryImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static partial int Stat(ref byte path, ref byte statBuffer);

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int Fstat(int descriptor, ref byte statBuffer);
}

/// <summary>Windows identity: a real handle plus <c>FILE_ID_INFO</c> (volume serial + 128-bit file id).</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsFileIdentityProbe : IFileIdentityProbe
{
    /// <inheritdoc/>
    public FileIdentityKey? TryGetIdentity(string fullPath)
    {
        try
        {
            // FileShare.ReadWrite | Delete so identifying a file never blocks another
            // process, and never fails merely because someone else has it open.
            using var handle = File.OpenHandle(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FileIdentityInterop.TryGetWindowsIdentity(handle);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

/// <summary>Unix identity: <c>stat</c>'s device and inode.</summary>
internal sealed class UnixFileIdentityProbe : IFileIdentityProbe
{
    /// <inheritdoc/>
    public FileIdentityKey? TryGetIdentity(string fullPath)
    {
        try
        {
            return FileIdentityInterop.TryGetUnixIdentity(fullPath);
        }
        catch (DllNotFoundException)
        {
            // No libc under this name — a capability the host does not offer.
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            // A libc that does not export `stat` directly (older glibc exported __xstat).
            return null;
        }
    }
}
