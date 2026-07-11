using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FcaBedrock.Conversion;

/// <summary>
/// The spool filesystem seam (D-082). Real by default; a test can substitute a decorator that injects
/// failures to exercise the two-channel storage-failure model (P-6). Implementations own confidentiality
/// (the spool holds raw source data): a workspace is created owner-restricted, and open run handles use
/// <see cref="FileShare.None"/> with non-inheritable OS handles. Callers own the
/// <see cref="GroupingOperation"/> classification — these methods throw raw exceptions, which the caller
/// maps to a <see cref="GroupingStorageException"/> via <see cref="SpoolFailures.Classify"/>.
/// </summary>
internal interface ISpoolFileSystem
{
    /// <summary>
    /// Creates a uniquely-named, owner-restricted workspace directory under <paramref name="root"/> and
    /// returns its path (create-new — never adopts an existing directory). Throws if the directory or
    /// its restrictive ACL cannot be established (so an un-protectable workspace halts rather than
    /// proceeding unprotected).
    /// </summary>
    string CreateWorkspace(string root);

    /// <summary>Opens a new run file for writing (create-new, <see cref="FileShare.None"/>, non-inheritable).</summary>
    Stream CreateRunForWrite(string path);

    /// <summary>Opens an existing run file for reading (<see cref="FileShare.None"/>, non-inheritable).</summary>
    Stream OpenRunForRead(string path);

    /// <summary>Deletes a consumed run file. Throws on failure (the cleanup channel catches).</summary>
    void DeleteRun(string path);

    /// <summary>Recursively deletes the workspace. Throws on failure (the cleanup channel catches).</summary>
    void DeleteWorkspace(string path);
}

/// <summary>The production <see cref="ISpoolFileSystem"/>: real files with owner-only confidentiality.</summary>
internal sealed class SpoolFileSystem : ISpoolFileSystem
{
    public static readonly SpoolFileSystem Default = new();

    public string CreateWorkspace(string root)
    {
        Directory.CreateDirectory(root);

        string path;
        do
        {
            path = Path.Combine(root, "fcabedrock-spool-" + Guid.NewGuid().ToString("N"));
        }
        while (Directory.Exists(path));

        if (OperatingSystem.IsWindows())
        {
            CreateOwnerOnlyDirectoryWindows(path);
        }
        else
        {
            // Unix mode 700: owner rwx only. Protects the spool from other non-privileged local users.
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    public Stream CreateRunForWrite(string path) =>
        new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None, // OS file handles are non-inheritable by default (bInheritHandle = false)
        });

    public Stream OpenRunForRead(string path) =>
        new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.None,
        });

    public void DeleteRun(string path) => File.Delete(path);

    public void DeleteWorkspace(string path) => Directory.Delete(path, recursive: true);

    // An explicit owner-only DACL with inheritance disabled — FileShare.None cannot protect runs whose
    // handles are closed between merge stages, so the directory ACL is the durable guard (persists even
    // for hard-kill orphans). Threat model: other non-privileged local users; not administrators/root.
    [SupportedOSPlatform("windows")]
    private static void CreateOwnerOnlyDirectoryWindows(string path)
    {
        var owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows identity has no security identifier.");

        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(path).Create(security);
    }
}

/// <summary>
/// Maps a raw storage exception to a stable <see cref="SpoolFailureKind"/> (D-082). Runtime exception
/// types are supporting context only: delete failures classify by <i>operation</i> (always
/// <see cref="SpoolFailureKind.DeleteFailed"/>) regardless of exception type, so delete-denied is one
/// identity whether it surfaces as <see cref="UnauthorizedAccessException"/> or <see cref="IOException"/>.
/// </summary>
internal static class SpoolFailures
{
    /// <summary>Whether <paramref name="ex"/> is a storage failure we own (vs. a programmer error to propagate).</summary>
    public static bool IsStorage(Exception ex) => ex is IOException or UnauthorizedAccessException;

    /// <summary>Classifies a create/open/write failure. Deletes and workspace creation classify by operation, not here.</summary>
    public static SpoolFailureKind Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => SpoolFailureKind.AccessDenied,
        IOException io when IsDiskFull(io) => SpoolFailureKind.StorageExhausted,
        _ => SpoolFailureKind.Other,
    };

    private static bool IsDiskFull(IOException ex)
    {
        // ERROR_DISK_FULL (0x70) / ERROR_HANDLE_DISK_FULL (0x27), as HRESULTs (0x8007____).
        var code = ex.HResult & 0xFFFF;
        return code is 0x70 or 0x27;
    }
}
