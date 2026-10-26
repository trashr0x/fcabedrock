namespace FcaBedrock.Orchestration;

/// <summary>What <see cref="ExtendsRelocation.Relocate"/> decided for a root written into another folder.</summary>
internal abstract record RelocatedExtends;

/// <summary>The <c>extends</c> to write: the authored reference where it still holds, otherwise a rebuilt one.</summary>
internal sealed record RelocatedReference(string Reference) : RelocatedExtends;

/// <summary>No reference can be written and verified, so nothing may be published.</summary>
internal sealed record RelocationRefused(string Message) : RelocatedExtends
{
    /// <summary>Which refusal <see cref="Message"/> states, set where the refusal is built.</summary>
    public HostErrorKind Kind { get; init; }
}

/// <summary>
/// The root's <c>extends</c> when <c>fingerprint --write</c> writes the root spec to a file
/// (D-122 part 10, §13).
/// <para>
/// <b>What must hold is the base's resolution path, not only its identity.</b> A base resolves its
/// own relative <c>extends</c> against the folder it was reached through (§13), so a reference that
/// names the same file by another path, such as a hard link in the target's folder, can compose a
/// different chain. A reference is accepted only when two separate checks pass:
/// </para>
/// <list type="number">
/// <item>The anchor. Resolved from the written file exactly as a later load resolves it
/// (<see cref="FileSpecTextSource.Resolve"/>), it gives the base's retained full path. Only the
/// root is folded, by <see cref="FileIdentity.NormalizeRoot"/>; every later component compares
/// ordinally, because a case-sensitive folder can hold two names that differ only in case. Equal
/// paths resolve every deeper reference identically.</item>
/// <item>The object. A fresh identity service from the factory that loaded the chain finds, at that
/// path, the identity the base had when it was loaded. It is a new service because a memoized key
/// is not a new observation. Keys of different kinds never match, so a path fallback where the load
/// saw an OS identity refuses; two path fallbacks match on equal canonical paths, which is the
/// fallback's existing guarantee with no link-alias promise (D-122 part 11).</item>
/// </list>
/// <para>
/// The authored reference is kept when it passes, so a copy written beside its root, or anywhere it
/// still reaches the same path, keeps its text. Otherwise the reference is rebuilt from the
/// target's folder to the retained path: the leading components both share, compared ordinally,
/// are dropped, each remaining folder of the target becomes <c>..</c>, and the base's remaining
/// components follow, joined by <c>/</c>. Every platform reads <c>/</c> as a separator, and a
/// backslash, a file-name character off Windows, stays inside its component. The rebuilt
/// reference must pass both checks too. A target on another drive or share has no relative
/// reference, and an absolute one is not allowed (§13), so it is refused, as is any reference
/// that does not verify. Nothing here reads a spec again, holds a file open, or flattens the chain.
/// </para>
/// </summary>
internal static class ExtendsRelocation
{
    /// <summary>
    /// Decides the reference a root that authored <paramref name="authored"/> gets when it is written
    /// to <paramref name="outSpelling"/>.
    /// </summary>
    /// <param name="authored">The root's <c>extends</c>, as authored.</param>
    /// <param name="outSpelling">The <c>--out</c> value, as given.</param>
    /// <param name="immediateBase">The chain file that reference loaded: its resolution path and load-time identity.</param>
    /// <param name="identities">The factory that created the identity service the chain was loaded with.</param>
    public static RelocatedExtends Relocate(
        string authored, string outSpelling, SpecChainFile immediateBase, Func<FileIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(outSpelling);
        ArgumentNullException.ThrowIfNull(immediateBase);
        ArgumentNullException.ThrowIfNull(identities);

        if (Destination(outSpelling) is not { } destination)
        {
            return Unverified(outSpelling, authored);
        }

        if (Holds(authored, destination, immediateBase, identities))
        {
            return new RelocatedReference(authored);
        }

        if (Rebuilt(destination, immediateBase.FullPath) is not { } rebuilt)
        {
            return new RelocationRefused(OtherRootMessage(outSpelling, authored)) { Kind = HostErrorKind.RelocationOtherRoot };
        }

        return Holds(rebuilt, destination, immediateBase, identities)
            ? new RelocatedReference(rebuilt)
            : Unverified(outSpelling, authored);
    }

    /// <summary>
    /// Checks <paramref name="reference"/> again, with a fresh identity service, just before
    /// publication starts writing: null when it still holds, otherwise the refusal.
    /// </summary>
    public static string? Recheck(
        string reference, string authored, string outSpelling, SpecChainFile immediateBase, Func<FileIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(outSpelling);
        ArgumentNullException.ThrowIfNull(immediateBase);
        ArgumentNullException.ThrowIfNull(identities);

        return Destination(outSpelling) is { } destination && Holds(reference, destination, immediateBase, identities)
            ? null
            : UnverifiedMessage(outSpelling, authored);
    }

    /// <summary>The refusal for a target with no relative reference to the base.</summary>
    internal static string OtherRootMessage(string outSpelling, string authored) =>
        $"cannot write the output '{outSpelling}': the base that extends = \"{authored}\" names is on another drive or "
        + "share, and a relative extends cannot reach it from there; choose an --out on the same drive or share as "
        + "that base.";

    private static RelocationRefused Unverified(string outSpelling, string authored) =>
        new(UnverifiedMessage(outSpelling, authored)) { Kind = HostErrorKind.RelocationUnverified };

    /// <summary>The refusal for a reference that does not verify.</summary>
    internal static string UnverifiedMessage(string outSpelling, string authored) =>
        $"cannot write the output '{outSpelling}': the base that extends = \"{authored}\" names could not be verified "
        + "from the output's folder, so the written spec might inherit from a different file; check that the base was "
        + "not moved or replaced, or choose a different --out.";

    // Both checks, each from a fresh observation of where the reference leads.
    private static bool Holds(string reference, string destination, SpecChainFile immediateBase, Func<FileIdentity> identities)
    {
        if (FileSpecTextSource.Resolve(destination, reference) is not { } resolved
            || !SameResolutionPath(resolved, immediateBase.FullPath))
        {
            return false;
        }

        var identity = identities();
        return identity.KeyFor(resolved) == immediateBase.Identity
            && identity.KeyFor(immediateBase.FullPath) == immediateBase.Identity;
    }

    // The written file's full path, as a later load registers it. A spelling the runtime cannot make
    // a path of cannot be verified.
    private static string? Destination(string outSpelling)
    {
        try
        {
            return Path.GetFullPath(outSpelling);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool SameResolutionPath(string first, string second)
    {
        var firstRoot = Path.GetPathRoot(first.AsSpan());
        var secondRoot = Path.GetPathRoot(second.AsSpan());
        return SameRoot(firstRoot, secondRoot)
            && first.AsSpan(firstRoot.Length).SequenceEqual(second.AsSpan(secondRoot.Length));
    }

    private static bool SameRoot(ReadOnlySpan<char> first, ReadOnlySpan<char> second) =>
        !first.IsEmpty
        && !second.IsEmpty
        && string.Equals(
            FileIdentity.NormalizeRoot(first.ToString()), FileIdentity.NormalizeRoot(second.ToString()), StringComparison.Ordinal);

    // The relative reference from the destination's folder to basePath, or null when the two have
    // different roots and none exists.
    private static string? Rebuilt(string destination, string basePath)
    {
        if (Path.GetDirectoryName(destination) is not { } folder)
        {
            return null;
        }

        var folderRoot = Path.GetPathRoot(folder.AsSpan());
        var baseRoot = Path.GetPathRoot(basePath.AsSpan());
        if (!SameRoot(folderRoot, baseRoot))
        {
            return null;
        }

        var from = Components(folder[folderRoot.Length..]);
        var to = Components(basePath[baseRoot.Length..]);
        if (to.Length == 0)
        {
            return null;
        }

        // The base's file name always remains, so the reference never ends at a folder.
        var shared = 0;
        while (shared < from.Length && shared < to.Length - 1 && string.Equals(from[shared], to[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return string.Join('/', Enumerable.Repeat("..", from.Length - shared).Concat(to[shared..]));
    }

    private static string[] Components(string remainder) =>
        remainder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
}
