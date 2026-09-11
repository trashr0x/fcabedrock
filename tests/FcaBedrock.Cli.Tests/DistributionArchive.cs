using System.IO.Compression;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// What makes a zip a valid <b>standalone distribution archive</b> — stated once, so the gated
/// smoke and the archive-writer test cannot drift into two different answers.
/// <para>
/// The property that forced this to be written down is the Unix <b>mode</b>. A zip carries a file's
/// permissions in its external-attributes field, and an archive that records none — or records
/// <c>0100644</c> — unzips to a <c>FcaBedrock.Cli</c> nobody can run, whatever the published file's
/// own mode was. Every archive this project shipped before had exactly that defect, and every
/// structural check it had passed anyway: the entry was present, correctly named, the right size,
/// and completely unusable.
/// </para>
/// </summary>
internal static class DistributionArchive
{
    /// <summary>A regular file, readable by all — <c>0100644</c> as the mode field holds it.</summary>
    internal const int RegularFileMode = 0x81A4;

    /// <summary>The same, plus the execute bits: <c>0100755</c>. The apphost, and only the apphost.</summary>
    internal const int ExecutableFileMode = 0x81ED;

    /// <summary>The zip mode field's file-type bits, and the value that means "symbolic link".</summary>
    private const int FileTypeMask = 0xF000;

    private const int SymbolicLinkType = 0xA000;

    /// <summary>The packaging script every archive — CI's and a developer's — is produced by.</summary>
    internal static string Script(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "eng", "publish-selfcontained.ps1");

    /// <summary>
    /// The apphost's name for <paramref name="rid"/>: the assembly's, never the tool command's. The
    /// command name belongs to the global-tool shim, and a second name for one binary would make the
    /// two distributions disagree about what the program is called.
    /// </summary>
    internal static string ExecutableName(string rid) =>
        rid.StartsWith("win-", StringComparison.Ordinal) ? "FcaBedrock.Cli.exe" : "FcaBedrock.Cli";

    /// <summary>Whether <paramref name="rid"/>'s distribution is one whose modes a zip must carry.</summary>
    internal static bool IsUnix(string rid) => !rid.StartsWith("win-", StringComparison.Ordinal);

    /// <summary>
    /// Every check that makes <paramref name="archivePath"/> a distribution archive rather than a
    /// zip that happens to hold the right files.
    /// <para>
    /// <b>Safety first, because an archive is something a user extracts.</b> A rooted name, a
    /// <c>..</c> segment, a volume qualifier, a backslash, a directory entry or a symbolic link is
    /// each a way for extraction to write somewhere it was not asked to, and two names that differ
    /// only by case are a way for one entry to silently overwrite another.
    /// </para>
    /// <para>
    /// <b>Then the mode.</b> On a Linux or macOS distribution the apphost must be recorded
    /// <c>0100755</c> and every other entry left <c>0100644</c> — the second half matters as much as
    /// the first, because "make everything executable" would be a different and worse archive. On a
    /// Windows distribution every entry's external-attributes field must be <b>exactly zero</b>:
    /// what a zip claims about permissions belongs to the target it was built for, never to the
    /// machine that wrote it, and checking only the Unix half of that field would let the writing
    /// host's own attribute bits through in the other half.
    /// </para>
    /// </summary>
    internal static void AssertValid(string archivePath, string rid)
    {
        Assert.True(File.Exists(archivePath), $"the packaging script produced no archive at '{archivePath}'.");

        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries;
        Assert.NotEmpty(entries);

        var executable = ExecutableName(rid);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var name = entry.FullName;

            Assert.False(Path.IsPathRooted(name), $"'{name}' is a rooted entry name.");
            Assert.DoesNotContain('\\', name);
            Assert.DoesNotContain(':', name);
            Assert.False(name.EndsWith('/'), $"'{name}' is a directory entry.");
            Assert.DoesNotContain("..", name.Split('/'));

            // The payload is flat, and stays flat: every distribution this project has shipped puts
            // the apphost, the runtime and the assemblies side by side at the archive root.
            Assert.DoesNotContain('/', name);

            Assert.True(seen.Add(name), $"'{name}' appears twice where case does not distinguish it.");

            if (!IsUnix(rid))
            {
                // The RAW field, before any shift, because a Windows distribution records nothing
                // at all here — which is a stronger claim than "no Unix mode". The mode lives in
                // the high half; the low half is where a host's own attribute byte lands, so
                // `0x00000001` shifts to zero and reads as mode-less while being different bytes,
                // written by a different host, than the contract asks for. The packaging script
                // assigns a literal 0 to every entry of a `win-*` target, so exact zero is the
                // claim and anything else is a machine's default leaking into the archive.
                Assert.True(
                    entry.ExternalAttributes == 0,
                    $"'{name}' records external attributes 0x{entry.ExternalAttributes:X8}, but a Windows "
                    + "distribution records none at all. Every entry has to be given the field explicitly, "
                    + "or the archive keeps whatever default the host that wrote it supplied.");

                continue;
            }

            var mode = entry.ExternalAttributes >>> 16;
            Assert.NotEqual(SymbolicLinkType, mode & FileTypeMask);

            var expected = string.Equals(name, executable, StringComparison.Ordinal)
                ? ExecutableFileMode
                : RegularFileMode;

            Assert.True(
                mode == expected,
                $"'{name}' is recorded as 0{Convert.ToString(mode, 8)}, not 0{Convert.ToString(expected, 8)}. "
                + "A zip carries the mode; the published file's own permissions do not survive one that "
                + "records none.");
        }

        Assert.Contains(executable, entries.Select(entry => entry.FullName));
    }

    /// <summary>
    /// Extracts <paramref name="archivePath"/> into a fresh directory and returns it — the same
    /// <c>unzip</c> step the README documents, done by the framework extractor that refuses a
    /// traversing entry and, on Unix, restores each entry's recorded mode.
    /// </summary>
    internal static string ExtractTo(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: false);
        return destination;
    }

    /// <summary>
    /// Asserts the extracted apphost is one a user can actually run: present, and — on Unix, where
    /// the question means anything — carrying the owner's execute bit.
    /// </summary>
    internal static string AssertExtractedApphost(string extracted, string rid)
    {
        var apphost = Path.Combine(extracted, ExecutableName(rid));
        Assert.True(File.Exists(apphost), $"the extracted archive holds no '{ExecutableName(rid)}'.");

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(apphost);
            Assert.True(
                mode.HasFlag(UnixFileMode.UserExecute),
                $"the extracted apphost is {mode}, so the documented './{ExecutableName(rid)}' cannot be run.");
        }

        return apphost;
    }
}

/// <summary>The repository root, found rather than counted to.</summary>
internal static class RepositoryRoot
{
    /// <summary>Wherever the solution file actually is above the test binary — never a fixed hop count.</summary>
    internal static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FcaBedrock.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"FcaBedrock.slnx was not found above '{AppContext.BaseDirectory}'.");
    }
}
