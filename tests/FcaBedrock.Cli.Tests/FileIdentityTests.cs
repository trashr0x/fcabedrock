namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The shared filesystem-identity service (D-123 part 6). Capability-bearing
/// cases assert what the host actually supports rather than what a platform is assumed to
/// do, and each skip names its reason.
/// </summary>
public sealed class FileIdentityTests
{
    private static FileIdentity Real() => FileIdentity.CreateDefault();

    private static FileIdentity WithoutOsIdentity() => new(new UnavailableFileIdentityProbe());

    [Fact]
    public void KeyFor_WhenTheFileExists_ThenThisHostSuppliesOperatingSystemIdentity()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");

        var key = Real().KeyFor(file);

        Assert.SkipUnless(
            key.IsOperatingSystemIdentity,
            "OS file identity is unavailable on this host; the path fallback is exercised by the injected-failure tests.");
        Assert.True(key.IsOperatingSystemIdentity);
    }

    [Fact]
    public void KeyFor_WhenThePathIsSpelledWithDotAndDotDot_ThenTheKeysUnify()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");
        var viaDot = Path.Combine(temp.Path, ".", "a.toml");
        var viaParent = Path.Combine(temp.Path, "sub", "..", "a.toml");
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));

        var identity = Real();

        Assert.Equal(identity.KeyFor(file), identity.KeyFor(viaDot));
        Assert.Equal(identity.KeyFor(file), identity.KeyFor(viaParent));
    }

    [Fact]
    public void KeyFor_WhenThePathIsSpelledWithDotAndDotDotAndIdentityIsUnavailable_ThenTheKeysStillUnify()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        var viaParent = Path.Combine(temp.Path, "sub", "..", "a.toml");

        var identity = WithoutOsIdentity();
        var key = identity.KeyFor(file);

        Assert.False(key.IsOperatingSystemIdentity);
        Assert.Equal(key, identity.KeyFor(viaParent));
    }

    [Fact]
    public void KeyFor_WhenCaseSpellingDiffers_ThenTheKeysFollowThisFilesystemsActualBehaviour()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("Mixed.toml", "x");
        var otherCase = Path.Combine(temp.Path, "mixed.toml");

        // The expectation is READ FROM THE FILESYSTEM, not assumed from the OS: a
        // case-sensitive directory on Windows and a case-insensitive volume on macOS are
        // both real, and either would break a hardcoded rule.
        var filesystemFindsBothSpellings = File.Exists(otherCase);
        var identity = Real();

        if (filesystemFindsBothSpellings)
        {
            Assert.Equal(identity.KeyFor(file), identity.KeyFor(otherCase));
        }
        else
        {
            Assert.NotEqual(identity.KeyFor(file), identity.KeyFor(otherCase));
        }
    }

    [Fact]
    public void KeyFor_WhenCaseSpellingDiffersAndIdentityIsUnavailable_ThenTheFallbackFollowsTheSameBehaviour()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("Mixed.toml", "x");
        var otherCase = Path.Combine(temp.Path, "mixed.toml");

        var filesystemFindsBothSpellings = File.Exists(otherCase);
        var identity = WithoutOsIdentity();

        Assert.Equal(
            filesystemFindsBothSpellings,
            identity.KeyFor(file).Equals(identity.KeyFor(otherCase)));
    }

    [Fact]
    public void KeyFor_WhenAChildDirectoryIsCaseSensitiveInsideACaseInsensitiveParent_ThenDistinctFilesStayDistinct()
    {
        // The topology a parent-based case probe gets wrong: asking whether the PARENT can
        // find "Child" and "child" says nothing about whether "Child" itself distinguishes
        // its own entries.
        using var temp = TempDirectory.Create();
        var child = Path.Combine(temp.Path, "Child");
        Directory.CreateDirectory(child);

        Assert.SkipUnless(
            PlatformLinks.TryMakeCaseSensitive(child, out var reason),
            $"per-directory case sensitivity is unavailable on this host: {reason}");

        var upper = Path.Combine(child, "A.toml");
        var lower = Path.Combine(child, "a.toml");
        File.WriteAllText(upper, "upper");
        File.WriteAllText(lower, "lower");

        Assert.SkipUnless(
            File.ReadAllText(upper) != File.ReadAllText(lower),
            "the directory did not become case-sensitive, so there are no two distinct entries to compare.");

        var identity = WithoutOsIdentity();

        Assert.NotEqual(identity.KeyFor(upper), identity.KeyFor(lower));

        // ...while the case-insensitive PARENT still folds its own child-directory spelling.
        var viaOtherParentSpelling = Path.Combine(temp.Path, "child", "A.toml");
        if (Directory.Exists(Path.Combine(temp.Path, "child")))
        {
            Assert.Equal(identity.KeyFor(upper), identity.KeyFor(viaOtherParentSpelling));
        }
    }

    [Fact]
    public void Equality_WhenFallbackKeysAreCompared_ThenItIsAProperEquivalenceRelation()
    {
        // A dictionary key must be reflexive, symmetric, transitive and hash-consistent. A
        // per-key "compare case-insensitively" bit combined across operands is none of those:
        // one insensitive key would bridge two distinct sensitive ones.
        using var temp = TempDirectory.Create();
        var identity = WithoutOsIdentity();

        var keys = new[]
        {
            identity.KeyFor(temp.Write("abc.toml", "1")),
            identity.KeyFor(Path.Combine(temp.Path, "Abc.toml")),
            identity.KeyFor(Path.Combine(temp.Path, "ABC.toml")),
            identity.KeyFor(temp.Write("other.toml", "2")),
            identity.KeyFor(Path.Combine(temp.Path, "missing.toml")),
        };

        foreach (var left in keys)
        {
            Assert.Equal(left, left);
            foreach (var right in keys)
            {
                Assert.Equal(left.Equals(right), right.Equals(left));
                if (left.Equals(right))
                {
                    Assert.Equal(left.GetHashCode(), right.GetHashCode());
                }

                foreach (var third in keys)
                {
                    if (left.Equals(right) && right.Equals(third))
                    {
                        Assert.True(left.Equals(third), "fallback identity equality is not transitive");
                    }
                }
            }
        }
    }

    [Fact]
    public void KeyFor_WhenTheFileIsReachedThroughALinkedDirectory_ThenTheKeysUnify()
    {
        // The approved fallback is the normalized FINAL-TARGET path, which means links
        // anywhere in the path — not only a link at the end.
        using var temp = TempDirectory.Create();
        var real = Path.Combine(temp.Path, "real");
        Directory.CreateDirectory(real);
        var file = Path.Combine(real, "a.toml");
        File.WriteAllText(file, "x");

        Assert.SkipUnless(
            PlatformLinks.TryCreateDirectoryLink(Path.Combine(temp.Path, "alias"), real, out var reason),
            $"directory links are unavailable on this host: {reason}");

        var viaLink = Path.Combine(temp.Path, "alias", "a.toml");

        Assert.Equal(Real().KeyFor(file), Real().KeyFor(viaLink));

        var fallback = WithoutOsIdentity();
        Assert.False(fallback.KeyFor(file).IsOperatingSystemIdentity);
        Assert.Equal(fallback.KeyFor(file), fallback.KeyFor(viaLink));
    }

    [Fact]
    public void KeyFor_WhenADirectoryLinksToItself_ThenEveryAliasDepthCollapsesToOneKey()
    {
        // Without resolving directory components, each traversal would invent a longer path
        // and a fresh key, so an extends chain would never revisit one and never terminate.
        using var temp = TempDirectory.Create();
        var real = Path.Combine(temp.Path, "real");
        Directory.CreateDirectory(real);
        var file = Path.Combine(real, "a.toml");
        File.WriteAllText(file, "x");

        Assert.SkipUnless(
            PlatformLinks.TryCreateDirectoryLink(Path.Combine(real, "self"), real, out var reason),
            $"directory links are unavailable on this host: {reason}");

        var fallback = WithoutOsIdentity();
        var expected = fallback.KeyFor(file);

        Assert.Equal(expected, fallback.KeyFor(Path.Combine(real, "self", "a.toml")));
        Assert.Equal(expected, fallback.KeyFor(Path.Combine(real, "self", "self", "a.toml")));
        Assert.Equal(expected, fallback.KeyFor(Path.Combine(real, "self", "self", "self", "a.toml")));
    }

    [Fact]
    public void KeyFor_WhenASymbolicLinkAliasesTheFile_ThenTheKeysUnify()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");
        var link = temp.Resolve("link.toml");

        Assert.SkipUnless(
            PlatformLinks.TryCreateSymbolicLink(link, file, out var reason),
            $"symbolic links are unavailable on this host: {reason}");

        var identity = Real();

        Assert.Equal(identity.KeyFor(file), identity.KeyFor(link));
    }

    [Fact]
    public void KeyFor_WhenASymbolicLinkAliasesTheFileAndIdentityIsUnavailable_ThenTheFallbackStillUnifiesThem()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");
        var link = temp.Resolve("link.toml");

        Assert.SkipUnless(
            PlatformLinks.TryCreateSymbolicLink(link, file, out var reason),
            $"symbolic links are unavailable on this host: {reason}");

        // The fallback resolves the FINAL link target, so it keeps the symlink alias class
        // even without OS identity — which is the one alias class path comparison can see.
        var identity = WithoutOsIdentity();

        Assert.False(identity.KeyFor(file).IsOperatingSystemIdentity);
        Assert.Equal(identity.KeyFor(file), identity.KeyFor(link));
    }

    [Fact]
    public void KeyFor_WhenAHardLinkAliasesTheFile_ThenTheKeysUnify()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");
        var link = temp.Resolve("hard.toml");

        Assert.SkipUnless(
            PlatformLinks.TryCreateHardLink(link, file, out var reason),
            $"hard links are unavailable on this host: {reason}");

        var identity = Real();
        var key = identity.KeyFor(file);

        Assert.SkipUnless(
            key.IsOperatingSystemIdentity,
            "OS file identity is unavailable on this host, and path comparison makes no hard-link guarantee (D-122 part 11).");

        // The case path normalization CANNOT see: two directory entries, one file.
        Assert.Equal(key, identity.KeyFor(link));
    }

    [Fact]
    public void KeyFor_WhenTheFilesAreDifferent_ThenTheKeysDiffer()
    {
        using var temp = TempDirectory.Create();
        var a = temp.Write("a.toml", "x");
        var b = temp.Write("b.toml", "x");

        var real = Real();
        Assert.NotEqual(real.KeyFor(a), real.KeyFor(b));

        var fallback = WithoutOsIdentity();
        Assert.NotEqual(fallback.KeyFor(a), fallback.KeyFor(b));
    }

    [Fact]
    public void KeyFor_WhenCalledRepeatedly_ThenTheAnswerIsStable()
    {
        // Determinism is what guarantees an extends chain terminates: a file that answered
        // one way can never answer another way later in the same run.
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");
        var identity = Real();

        Assert.Equal(identity.KeyFor(file), identity.KeyFor(file));
    }

    [Fact]
    public void KeyFor_WhenTheFileDoesNotExist_ThenAFallbackKeyIsReturnedRatherThanAFailure()
    {
        using var temp = TempDirectory.Create();
        var missing = temp.Resolve("nope.toml");

        var key = Real().KeyFor(missing);

        Assert.False(key.IsOperatingSystemIdentity);
        Assert.Equal(key, Real().KeyFor(missing));
    }

    [Fact]
    public void Equals_WhenComparingAnOsKeyToAFallbackKey_ThenTheyAreNeverEqual()
    {
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");

        var osKey = Real().KeyFor(file);
        var fallbackKey = WithoutOsIdentity().KeyFor(file);

        Assert.SkipUnless(osKey.IsOperatingSystemIdentity, "OS file identity is unavailable on this host.");
        Assert.NotEqual(osKey, fallbackKey);
    }

    [Fact]
    public void KeyFor_WhenRendered_ThenItCannotBeMistakenForAPath()
    {
        // Identity keys are internal and must never reach output bytes, fingerprints,
        // manifests, or a user-facing path (§13).
        using var temp = TempDirectory.Create();
        var file = temp.Write("a.toml", "x");

        var rendered = Real().KeyFor(file).ToString();

        Assert.Equal(typeof(FileIdentityKey).ToString(), rendered);
        Assert.DoesNotContain("a.toml", rendered, StringComparison.Ordinal);
    }
}
