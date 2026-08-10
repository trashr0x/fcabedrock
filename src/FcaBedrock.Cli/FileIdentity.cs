namespace FcaBedrock.Cli;

/// <summary>
/// The identity of one file, as this run understands it. Two spellings of the same file
/// produce equal keys; two different files never do.
/// <para>
/// <b>An equivalence relation by construction.</b> A key is either an OS identity triple or
/// a single canonical path string, and equality is exact comparison of one of those — never
/// a comparison mode negotiated between two operands. So equality is reflexive, symmetric,
/// transitive, and hash-consistent, which is what a dictionary key must be and what keeps
/// canonicalization independent of insertion order.
/// </para>
/// <para>
/// <b>Opaque and internal.</b> A key is never a path, never renders, and never reaches
/// output bytes, fingerprints, manifests, or a user-facing message (§13, D-122 part 11).
/// <see cref="ValueType.ToString"/> is deliberately not overridden, so a key can never be
/// mistaken for a filesystem path if one ever leaks into a format string.
/// </para>
/// </summary>
internal readonly struct FileIdentityKey : IEquatable<FileIdentityKey>
{
    private readonly ulong _volume;
    private readonly ulong _low;
    private readonly ulong _high;
    private readonly string? _canonicalPath;

    private FileIdentityKey(ulong volume, ulong low, ulong high)
    {
        _volume = volume;
        _low = low;
        _high = high;
        _canonicalPath = null;
    }

    private FileIdentityKey(string canonicalPath)
    {
        _volume = 0;
        _low = 0;
        _high = 0;
        _canonicalPath = canonicalPath;
    }

    /// <summary>
    /// Real OS identity — Windows volume serial plus the 128-bit file id, or Unix
    /// device plus inode. This is what unifies supported symlink <b>and hardlink</b>
    /// aliases, which path comparison cannot see.
    /// </summary>
    public static FileIdentityKey FromOperatingSystem(ulong volume, ulong low, ulong high) => new(volume, low, high);

    /// <summary>
    /// The fallback, used only when OS identity is genuinely unavailable: one canonical
    /// path — the final link target, with every component in its real on-disk spelling —
    /// compared exactly. It makes no hardlink guarantee (D-122 part 11).
    /// </summary>
    public static FileIdentityKey FromPath(string canonicalPath)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        return new FileIdentityKey(canonicalPath);
    }

    /// <summary>True when this key came from the operating system rather than the path fallback.</summary>
    public bool IsOperatingSystemIdentity => _canonicalPath is null;

    /// <summary>
    /// The operating system's own identity components, when this key has them.
    /// <para>
    /// The only consumer is <see cref="FcaBedrock.Cli.Publication.IdentityEvidence"/>, which
    /// digests them so a transaction can prove — durably, across a crash — that the file at a
    /// target path is the very object it staged. A path-fallback key deliberately answers
    /// <see langword="false"/>: it identifies a <em>name</em>, and a name proves nothing about the
    /// object occupying it.
    /// </para>
    /// </summary>
    internal bool TryGetOperatingSystemIdentity(out ulong volume, out ulong low, out ulong high)
    {
        volume = _volume;
        low = _low;
        high = _high;
        return _canonicalPath is null;
    }

    public static bool operator ==(FileIdentityKey left, FileIdentityKey right) => left.Equals(right);

    public static bool operator !=(FileIdentityKey left, FileIdentityKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(FileIdentityKey other)
    {
        // An OS key and a fallback key are never equal: one is a statement about the
        // filesystem, the other about a canonical path, and conflating them would let a
        // capability failure silently unify unrelated files.
        if (_canonicalPath is null || other._canonicalPath is null)
        {
            return _canonicalPath is null
                && other._canonicalPath is null
                && _volume == other._volume
                && _low == other._low
                && _high == other._high;
        }

        // Ordinal, always. Case folding already happened during canonicalization, where the
        // filesystem itself supplied each component's real spelling; deciding it here from
        // per-key flags is what makes equality non-transitive.
        return string.Equals(_canonicalPath, other._canonicalPath, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FileIdentityKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        _canonicalPath is null
            ? HashCode.Combine(_volume, _low, _high)
            : StringComparer.Ordinal.GetHashCode(_canonicalPath);
}

/// <summary>Reports the operating system's identity for an existing file, or null when unavailable.</summary>
internal interface IFileIdentityProbe
{
    /// <summary>OS identity for <paramref name="fullPath"/>, or null when the capability is unavailable.</summary>
    FileIdentityKey? TryGetIdentity(string fullPath);
}

/// <summary>
/// The shared filesystem-identity service (D-123 part 6): OS identity for
/// existing files where the platform exposes it, one canonical path only as a fallback. It
/// is consumed by the file-backed <c>extends</c> host now and by publication collision
/// checks later, so "is this the same file?" has one answer in the whole CLI.
/// <para>
/// <b>The fallback asks the filesystem rather than assuming a rule.</b> Each path component
/// is replaced by its real on-disk spelling, obtained from the directory that actually
/// contains it, and any link-bearing component — file or directory — is replaced by its
/// final target. So case behaviour is measured where each entry lives (it is a per-directory
/// property on NTFS, and a per-volume one elsewhere), a case-varying ancestor cannot make two
/// distinct files compare equal, and a self-aliasing directory link collapses instead of
/// generating ever-longer paths. Whatever cannot be determined keeps the supplied spelling,
/// which can only fail to unify two spellings — never unify two different files.
/// </para>
/// <para>
/// <b>Results are memoized per normalized path.</b> That makes identity a deterministic
/// function of the path within a run, which is what guarantees <c>extends</c> chain walking
/// terminates: a chain can only revisit a key, never oscillate between two answers.
/// </para>
/// <para>
/// A failed identity call is a <b>capability fallback</b>, never a crash and never a
/// diagnostic. Only the expected capability failures are absorbed — a missing file, a
/// permission or sharing refusal, an absent library or entry point. Marshalling and
/// contract defects propagate rather than hiding behind the fallback.
/// </para>
/// </summary>
internal sealed class FileIdentity
{
    // A guard, not a policy: a link chain longer than this is pathological, and giving up
    // conservatively beats recursing forever.
    private const int MaxLinkDepth = 32;

    private readonly IFileIdentityProbe _probe;
    private readonly Dictionary<string, FileIdentityKey> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _realSpellings = new(StringComparer.Ordinal);

    /// <summary>Creates a service over <paramref name="probe"/>.</summary>
    public FileIdentity(IFileIdentityProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    /// <summary>Creates the service over the platform's real identity probe.</summary>
    public static FileIdentity CreateDefault() =>
        new(OperatingSystem.IsWindows() ? new WindowsFileIdentityProbe() : new UnixFileIdentityProbe());

    /// <summary>The identity of the file at <paramref name="path"/>.</summary>
    public FileIdentityKey KeyFor(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);
        if (_keys.TryGetValue(full, out var cached))
        {
            return cached;
        }

        var key = _probe.TryGetIdentity(full) ?? FileIdentityKey.FromPath(CanonicalPath(full));
        _keys[full] = key;
        return key;
    }

    /// <summary>
    /// The canonical form of <paramref name="fullPath"/>: the final target of every link on
    /// the way, with each component in the spelling the containing directory actually stores.
    /// Exposed for the fallback's own tests; it is never a user-facing path.
    /// </summary>
    internal string CanonicalPath(string fullPath) => Canonicalize(fullPath, depth: 0);

    private string Canonicalize(string fullPath, int depth)
    {
        if (depth > MaxLinkDepth || Path.GetPathRoot(fullPath) is not { Length: > 0 } root)
        {
            return fullPath;
        }

        var current = NormalizeRoot(root);
        var parts = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var candidate = Path.Combine(current, RealSpelling(current, part));

            // A link ANYWHERE in the path is followed, not just a link at the end: a regular
            // file reached through a linked directory is the same file as the one reached
            // through the physical directory, and a directory that links to itself would
            // otherwise generate a fresh path on every traversal.
            if (LinkTargetOf(candidate) is { } target)
            {
                current = Canonicalize(Path.GetFullPath(target), depth + 1);
                continue;
            }

            current = candidate;
        }

        return current;
    }

    // Drive letters and UNC server/share names are case-insensitive by definition on Windows,
    // so folding the root can only merge spellings of one volume. Elsewhere the root is "/".
    private static string NormalizeRoot(string root) =>
        OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;

    private string RealSpelling(string directory, string name)
    {
        var cacheKey = directory + "\0" + name;
        if (_realSpellings.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var resolved = ResolveSpelling(directory, name) ?? name;
        _realSpellings[cacheKey] = resolved;
        return resolved;
    }

    // Asks the containing directory for the entry's stored name. An EXACT match always wins,
    // so on a case-sensitive directory holding both "A.toml" and "a.toml" each keeps its own
    // spelling and the two stay distinct; only when no exact entry exists does a
    // case-insensitive match supply the stored spelling, which is precisely the case-folding
    // behaviour of a case-insensitive directory.
    private static string? ResolveSpelling(string directory, string name)
    {
        try
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                // Nothing exists under this spelling, so no directory can say anything about
                // it — enumerating would cost a scan and answer nothing.
                return null;
            }

            string? insensitive = null;
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return entry.Name;
                }

                if (insensitive is null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    insensitive = entry.Name;
                }
            }

            return insensitive;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? LinkTargetOf(string path)
    {
        try
        {
            var directory = Directory.Exists(path);
            if (!directory && !File.Exists(path))
            {
                return null;
            }

            FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            // Includes a link chain the OS refuses to follow; the lexical path stands.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
