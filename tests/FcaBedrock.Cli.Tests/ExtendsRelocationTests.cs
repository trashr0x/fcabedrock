namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The root's <c>extends</c> for a copy written into another folder, at its own seam: the
/// resolution-path check, the rebuilt reference, the platform path rules, and the fresh identity
/// check, including a host without OS file identity (D-122 parts 10 and 11). The lexical cases use
/// an injected identity probe, so they never touch the filesystem.
/// </summary>
public sealed class ExtendsRelocationTests
{
    /// <summary>An identity probe that names objects by an injected rule, so paths need not exist.</summary>
    private sealed class FakeIdentityProbe(Func<string, ulong> objectOf) : IFileIdentityProbe
    {
        public FileIdentityKey? TryGetIdentity(string fullPath) =>
            FileIdentityKey.FromOperatingSystem(volume: 1, low: objectOf(fullPath), high: 0);
    }

    // Every path is its own object unless the test says otherwise. Spellings that differ only in a
    // Windows root's case are one object, as they are to the operating system.
    private static Func<FileIdentity> Objects(params (string Path, ulong Object)[] known)
    {
        var map = known.ToDictionary(entry => entry.Path, entry => entry.Object, StringComparer.Ordinal);
        return () => new FileIdentity(new FakeIdentityProbe(path =>
            map.TryGetValue(path, out var id) ? id : (uint)StringComparer.Ordinal.GetHashCode(RootFolded(path)) | 1UL << 63));
    }

    private static string RootFolded(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return FileIdentity.NormalizeRoot(root) + path[root.Length..];
    }

    private static string Root => OperatingSystem.IsWindows() ? @"C:\fcabedrock-relocation" : "/fcabedrock-relocation";

    private static string At(params string[] components) => Path.Combine([Root, .. components]);

    private static SpecChainFile Base(string fullPath, Func<FileIdentity> identities) =>
        new(fullPath, "unused", "sha256:unused", identities().KeyFor(fullPath));

    private static string Relocated(RelocatedExtends result) => Assert.IsType<RelocatedReference>(result).Reference;

    private static string Refused(RelocatedExtends result) => Assert.IsType<RelocationRefused>(result).Message;

    // ---- the authored reference, kept where it still holds ------------------------------------------

    [Theory]
    [InlineData("../base/mid.toml")]
    [InlineData("./../base/mid.toml")]
    [InlineData("../base/./x/../mid.toml")]
    public void Relocate_WhenTheAuthoredReferenceReachesTheSamePathFromTheTarget_ThenItIsKeptAsWritten(string authored)
    {
        var identities = Objects();
        var immediateBase = Base(At("base", "mid.toml"), identities);

        var result = ExtendsRelocation.Relocate(authored, At("copies", "root.toml"), immediateBase, identities);

        Assert.Equal(authored, Relocated(result));
    }

    // ---- a rebuilt reference -----------------------------------------------------------------------

    public static TheoryData<string[], string> Targets() => new()
    {
        { ["root.toml"], "base/mid.toml" },
        { ["deep", "er", "root.toml"], "../../base/mid.toml" },
        { ["base", "root.toml"], "mid.toml" },
        { ["base", "inner", "root.toml"], "../mid.toml" },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void Relocate_WhenTheAuthoredReferenceMissesTheBase_ThenAReferenceToTheSamePathIsBuiltWithSlashes(
        string[] target, string expected)
    {
        var identities = Objects();
        var immediateBase = Base(At("base", "mid.toml"), identities);

        var result = ExtendsRelocation.Relocate("../somewhere/else.toml", At(target), immediateBase, identities);

        Assert.Equal(expected, Relocated(result));
    }

    [Fact]
    public void Relocate_WhenTheAuthoredReferenceNamesTheSameObjectAtAnotherPath_ThenItIsReplaced()
    {
        // A hard link of the base beside the target: same object, other folder, so its own extends
        // would resolve elsewhere. The anchor check refuses it before identity is asked.
        var original = At("left", "base.toml");
        var alias = At("right", "base.toml");
        var identities = Objects((original, 7), (alias, 7));
        var immediateBase = Base(original, identities);

        var result = ExtendsRelocation.Relocate("base.toml", At("right", "root2.toml"), immediateBase, identities);

        Assert.Equal("../left/base.toml", Relocated(result));
    }

    // ---- the object check --------------------------------------------------------------------------

    [Fact]
    public void Relocate_WhenTheObjectAtTheBasesPathIsNoLongerTheOneLoaded_ThenItIsRefused()
    {
        var basePath = At("base", "mid.toml");
        var loaded = Objects((basePath, 1));
        var immediateBase = Base(basePath, loaded);
        var target = At("copies", "root.toml");

        var result = ExtendsRelocation.Relocate("../base/mid.toml", target, immediateBase, Objects((basePath, 2)));

        Assert.Equal(ExtendsRelocation.UnverifiedMessage(target, "../base/mid.toml"), Refused(result));
    }

    [Fact]
    public void Recheck_WhenTheBaseChangedAfterRelocation_ThenItIsRefusedBecauseEachCheckProbesAfresh()
    {
        // The factory answers differently from its second service on: a memoized key would not see
        // that, a fresh service does.
        var basePath = At("base", "mid.toml");
        var services = 0;
        Func<FileIdentity> identities = () =>
            new FileIdentity(new FakeIdentityProbe(_ => services == 1 ? 1UL : 2UL));
        Func<FileIdentity> counting = () =>
        {
            services++;
            return identities();
        };
        var immediateBase = new SpecChainFile(basePath, "unused", "sha256:unused", FileIdentityKey.FromOperatingSystem(1, 1, 0));
        var target = At("deep", "root.toml");

        var reference = Relocated(ExtendsRelocation.Relocate("base/mid.toml", target, immediateBase, counting));

        Assert.Equal("../base/mid.toml", reference);
        Assert.Equal(
            ExtendsRelocation.UnverifiedMessage(target, "base/mid.toml"),
            ExtendsRelocation.Recheck(reference, "base/mid.toml", target, immediateBase, counting));
        Assert.Equal(2, services);
    }

    [Fact]
    public void Relocate_WhenTheHostHasNoOsIdentity_ThenTheExistingPathFallbackVerifiesTheBase()
    {
        // Both observations come from the same kind of probe, so two path-fallback keys compare by
        // their canonical paths and agree; the anchor check stays separate and exact.
        using var temp = TempDirectory.Create();
        var basePath = Path.GetFullPath(temp.Write(Path.Combine("base", "mid.toml"), "x"));
        Directory.CreateDirectory(temp.Resolve("out"));
        Func<FileIdentity> identities = () => new FileIdentity(new UnavailableFileIdentityProbe());
        var immediateBase = Base(basePath, identities);
        Assert.False(immediateBase.Identity.IsOperatingSystemIdentity);

        var result = ExtendsRelocation.Relocate("../elsewhere.toml", temp.Resolve(Path.Combine("out", "root.toml")), immediateBase, identities);

        Assert.Equal("../base/mid.toml", Relocated(result));
    }

    [Fact]
    public void Relocate_WhenTheLoadSawOsIdentityAndTheCheckSeesOnlyThePathFallback_ThenItIsRefused()
    {
        using var temp = TempDirectory.Create();
        var basePath = Path.GetFullPath(temp.Write("base.toml", "x"));
        var loaded = FileIdentity.CreateDefault().KeyFor(basePath);
        Assert.SkipUnless(loaded.IsOperatingSystemIdentity, "OS file identity is unavailable on this host.");
        var immediateBase = new SpecChainFile(basePath, "base.toml", "sha256:unused", loaded);
        var target = temp.Resolve("root.toml");

        var result = ExtendsRelocation.Relocate(
            "base.toml", target, immediateBase, () => new FileIdentity(new UnavailableFileIdentityProbe()));

        Assert.Equal(ExtendsRelocation.UnverifiedMessage(target, "base.toml"), Refused(result));
    }

    // ---- platform path rules -----------------------------------------------------------------------

    [Fact]
    public void Relocate_WhenOnlyTheWindowsDriveLetterCaseDiffers_ThenTheRootsAreOne()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "drive letters exist only on Windows");
        var identities = Objects();
        var immediateBase = Base(@"C:\work\base\mid.toml", identities);

        var result = ExtendsRelocation.Relocate("x.toml", @"c:\work\out\root.toml", immediateBase, identities);

        Assert.Equal("../base/mid.toml", Relocated(result));
    }

    [Fact]
    public void Relocate_WhenAFolderNameDiffersOnlyInCase_ThenItIsKeptAsAnotherFolder()
    {
        // Past the root every component compares ordinally: on a case-sensitive folder Work and work
        // are two places, so the reference climbs out and names Work exactly.
        var identities = Objects();
        var immediateBase = Base(At("Work", "base", "mid.toml"), identities);

        var result = ExtendsRelocation.Relocate("x.toml", At("work", "out", "root.toml"), immediateBase, identities);

        Assert.Equal("../../Work/base/mid.toml", Relocated(result));
    }

    public static TheoryData<string, string> OtherRoots() => new()
    {
        { @"C:\work\base\mid.toml", @"D:\work\root.toml" },
        { @"C:\work\base\mid.toml", @"\\server\share\work\root.toml" },
        { @"\\server\share\base\mid.toml", @"\\server\other\root.toml" },
    };

    [Theory]
    [MemberData(nameof(OtherRoots))]
    public void Relocate_WhenTheTargetHasAnotherWindowsRoot_ThenNoRelativeReferenceExistsAndItIsRefused(string basePath, string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows paths have more than one root");
        var identities = Objects();
        var immediateBase = Base(basePath, identities);

        var result = ExtendsRelocation.Relocate("mid.toml", target, immediateBase, identities);

        Assert.Equal(ExtendsRelocation.OtherRootMessage(target, "mid.toml"), Refused(result));
    }

    [Fact]
    public void Relocate_WhenAShareNameDiffersOnlyInCase_ThenTheRootsAreOne()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "UNC shares exist only on Windows paths");
        var identities = Objects();
        var immediateBase = Base(@"\\server\share\base\mid.toml", identities);

        var result = ExtendsRelocation.Relocate("x.toml", @"\\SERVER\Share\out\root.toml", immediateBase, identities);

        Assert.Equal("../base/mid.toml", Relocated(result));
    }

    [Fact]
    public void Relocate_WhenAUnixFolderNameHoldsABackslash_ThenItStaysInsideItsComponent()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "a backslash is a separator on Windows");
        var identities = Objects();
        var immediateBase = Base("/fcabedrock-relocation/a\\b/mid.toml", identities);

        var result = ExtendsRelocation.Relocate("x.toml", "/fcabedrock-relocation/out/root.toml", immediateBase, identities);

        Assert.Equal("../a\\b/mid.toml", Relocated(result));
    }
}
