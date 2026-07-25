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
        if (Stat(ref path[0], ref MemoryMarshal.GetReference(buffer)) != 0)
        {
            return null;
        }

        // st_ino is a 64-bit field at offset 8 on Linux, macOS and FreeBSD alike. st_dev
        // sits at offset 0 and is 64-bit there too, except on Darwin where it is a 32-bit
        // dev_t followed by st_mode/st_nlink — fields that CHANGE (chmod, a new hardlink),
        // so reading 8 bytes there would make identity unstable rather than merely wider.
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

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int fileInformationClass, out FileIdInfo fileInformation, uint size);

    [LibraryImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static partial int Stat(ref byte path, ref byte statBuffer);
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
