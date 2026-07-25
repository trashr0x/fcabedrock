using System.Diagnostics;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Creates the link kinds §13 names, where the host supports them. Both are genuine
/// capabilities rather than assumptions: Windows symbolic links need Developer Mode or
/// elevation, and .NET exposes no hard-link API at all, so hard links go through the
/// platform's own tool. A test that cannot create the link says so and skips.
/// </summary>
internal static class PlatformLinks
{
    /// <summary>Creates a symbolic link at <paramref name="link"/> pointing at <paramref name="target"/>.</summary>
    public static bool TryCreateSymbolicLink(string link, string target, out string reason)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            reason = string.Empty;
            return true;
        }
        catch (IOException exception)
        {
            reason = exception.Message;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            reason = exception.Message;
            return false;
        }
        catch (PlatformNotSupportedException exception)
        {
            reason = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Creates a directory link at <paramref name="link"/> pointing at <paramref name="target"/>.
    /// On Windows this is a junction, which — unlike a symbolic link — needs no special
    /// privilege, so the linked-directory cases are actually exercised on an ordinary host.
    /// </summary>
    public static bool TryCreateDirectoryLink(string link, string target, out string reason)
    {
        var created = OperatingSystem.IsWindows()
            ? Run("cmd.exe", ["/c", "mklink", "/J", link, target], out reason)
            : Run("/bin/ln", ["-s", target, link], out reason);

        if (created && !Directory.Exists(link))
        {
            reason = "the link command reported success but no directory link exists";
            return false;
        }

        return created;
    }

    /// <summary>
    /// Marks <paramref name="directory"/> case-sensitive where the host supports per-directory
    /// case sensitivity (Windows NTFS via <c>fsutil</c>). Used to build the mixed
    /// parent/child topology that a parent-based case probe gets wrong.
    /// </summary>
    public static bool TryMakeCaseSensitive(string directory, out string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Elsewhere the ordinary case-sensitive filesystem already provides the topology.
            reason = string.Empty;
            return true;
        }

        return Run("fsutil.exe", ["file", "setCaseSensitiveInfo", directory, "enable"], out reason);
    }

    /// <summary>Creates a hard link at <paramref name="link"/> for <paramref name="target"/>.</summary>
    public static bool TryCreateHardLink(string link, string target, out string reason)
    {
        var created = OperatingSystem.IsWindows()
            ? Run("cmd.exe", ["/c", "mklink", "/H", link, target], out reason)
            : Run("/bin/ln", [target, link], out reason);

        if (created && !File.Exists(link))
        {
            reason = "the link command reported success but no link exists";
            return false;
        }

        return created;
    }

    private static bool Run(string fileName, string[] arguments, out string reason)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                reason = $"{fileName} could not be started";
                return false;
            }

            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                reason = $"{fileName} did not exit";
                return false;
            }

            reason = process.ExitCode == 0 ? string.Empty : $"{fileName} exited {process.ExitCode}: {error.Trim()}";
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            reason = exception.Message;
            return false;
        }
        catch (IOException exception)
        {
            reason = exception.Message;
            return false;
        }
    }
}

/// <summary>An identity probe that always reports the capability unavailable.</summary>
internal sealed class UnavailableFileIdentityProbe : IFileIdentityProbe
{
    public FileIdentityKey? TryGetIdentity(string fullPath) => null;
}
