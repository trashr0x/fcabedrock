using System.Text;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The file-backed <c>extends</c> host: §13 composition over real files, with cycle
/// detection that alias spellings cannot evade (D-122 part 11). Composition itself stays
/// <see cref="SpecComposer"/>'s — the host adds path resolution and identity, and no
/// parallel composer, cycle detector, or diagnostic.
/// </summary>
public sealed class FileSpecTextSourceTests
{
    private const string Leaf = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        missing_token = "!"
        """;

    private static (Diagnosed<SpecDocument> Result, string RootKey) Compose(string rootPath)
    {
        var host = new FileSpecTextSource(CliEnvironment.OpenFile, FileIdentity.CreateDefault());
        var rootKey = host.RegisterRoot(rootPath);
        var read = SpecReader.Read(host.ReadText(rootPath), rootKey);
        Assert.True(read.TryGetValue(out var document), Describe(read.Diagnostics));
        return (SpecComposer.Compose(document, rootKey, host), rootKey);
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));

    private static BedrockDiagnostic Single(Diagnosed<SpecDocument> result, DiagnosticCode code)
    {
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        return diagnostic;
    }

    [Fact]
    public void Compose_WhenTheChainIsMultilevel_ThenEachReferenceResolvesAgainstItsOwnReferrersDirectory()
    {
        // specs/root.toml -> ../base/mid.toml -> ./deep.toml. The second hop is relative
        // to base/, not to specs/ and not to the process directory: referrer-relative all
        // the way down.
        using var temp = TempDirectory.Create();
        temp.Write("base/deep.toml", Leaf);
        temp.Write("base/mid.toml", "[spec]\nversion = 1\nextends = \"./deep.toml\"\n");
        var root = temp.Write("specs/root.toml", """
            [spec]
            version = 1
            extends = "../base/mid.toml"

            [binding]
            has_header = true
            """);

        var (result, _) = Compose(root);

        Assert.True(result.TryGetValue(out var composed), Describe(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        Assert.Null(composed.Spec!.Extends);
        Assert.Equal("!", composed.Binding!.MissingToken);
        Assert.True(composed.Binding.HasHeader);
    }

    [Fact]
    public void Compose_WhenAReferenceIsAbsolute_ThenItTakesTheEstablishedNotFoundOutcome()
    {
        // Authored references are relative-only; an absolute one is not resolved at all.
        using var temp = TempDirectory.Create();
        var target = temp.Write("base.toml", Leaf);
        var root = temp.Write("root.toml", $"[spec]\nversion = 1\nextends = \"{target.Replace("\\", "\\\\")}\"\n");

        var (result, rootKey) = Compose(root);

        var diagnostic = Single(result, DiagnosticCode.SpecExtendsNotFound);
        Assert.Equal(rootKey, diagnostic.Location!.Value.File);
    }

    [Fact]
    public void Compose_WhenTheBaseIsMissing_ThenSpecExtendsNotFound()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./nope.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsNotFound);
    }

    [Fact]
    public void Compose_WhenTheBaseIsADirectory_ThenSpecExtendsNotFound()
    {
        using var temp = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "base.toml"));
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsNotFound);
    }

    [Fact]
    public void Compose_WhenASpecExtendsItself_ThenSpecExtendsCycle()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./root.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsCycle);
    }

    [Fact]
    public void Compose_WhenTwoFilesFormACycle_ThenSpecExtendsCycle()
    {
        using var temp = TempDirectory.Create();
        temp.Write("b.toml", "[spec]\nversion = 1\nextends = \"./a.toml\"\n");
        var root = temp.Write("a.toml", "[spec]\nversion = 1\nextends = \"./b.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsCycle);
    }

    [Fact]
    public void Compose_WhenACycleIsSpelledThroughDotDot_ThenItIsStillDetected()
    {
        // A different path SPELLING of the same file is the same file.
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./sub/../root.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsCycle);
    }

    [Fact]
    public void Compose_WhenACycleIsSpelledThroughASymbolicLink_ThenItIsStillDetected()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./link.toml\"\n");

        Assert.SkipUnless(
            PlatformLinks.TryCreateSymbolicLink(temp.Resolve("link.toml"), root, out var reason),
            $"symbolic links are unavailable on this host: {reason}");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsCycle);
    }

    [Fact]
    public void Compose_WhenACycleIsSpelledThroughAHardLink_ThenItIsStillDetected()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./hard.toml\"\n");

        Assert.SkipUnless(
            PlatformLinks.TryCreateHardLink(temp.Resolve("hard.toml"), root, out var reason),
            $"hard links are unavailable on this host: {reason}");
        Assert.SkipUnless(
            FileIdentity.CreateDefault().KeyFor(root).IsOperatingSystemIdentity,
            "OS file identity is unavailable on this host, and path comparison makes no hard-link guarantee.");

        // Two directory entries, one file: only real OS identity can see this.
        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsCycle);
    }

    [Fact]
    public void Compose_WhenACycleIsSpelledThroughASelfAliasingDirectory_ThenItIsStillDetected()
    {
        // real/self -> real, and the spec extends "./self/root.toml". Without resolving link
        // components each load would invent a longer path — self/self/root.toml, and so on —
        // until the OS refused, reporting not-found or a fault instead of the cycle.
        using var temp = TempDirectory.Create();
        var real = Path.Combine(temp.Path, "real");
        Directory.CreateDirectory(real);
        var root = Path.Combine(real, "root.toml");
        File.WriteAllText(root, "[spec]\nversion = 1\nextends = \"./self/root.toml\"\n");

        Assert.SkipUnless(
            PlatformLinks.TryCreateDirectoryLink(Path.Combine(real, "self"), real, out var reason),
            $"directory links are unavailable on this host: {reason}");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsCycle);
    }

    [Fact]
    public void ReadText_WhenTheBytesAreUtf8_ThenTheyDecodeVerbatim()
    {
        using var temp = TempDirectory.Create();
        const string content = "# café 中文\n[spec]\nversion = 1\n";
        var path = temp.Write("spec.toml", content);
        var host = new FileSpecTextSource(CliEnvironment.OpenFile, FileIdentity.CreateDefault());

        Assert.Equal(content, host.ReadText(path));
    }

    [Fact]
    public void ReadText_WhenTheBytesCarryAUtf8ByteOrderMark_ThenItIsConsumed()
    {
        var host = HostOver([0xEF, 0xBB, 0xBF, (byte)'a', (byte)'=', (byte)'1']);

        Assert.Equal("a=1", host.ReadText("spec.toml"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0x00 })]                     // UTF-16 LE
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, (byte)'a' })]                     // UTF-16 BE
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00, (byte)'a', 0x00, 0x00, 0x00 })] // UTF-32 LE
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, (byte)'a' })] // UTF-32 BE
    public void ReadText_WhenTheBytesCarryANonUtf8ByteOrderMark_ThenTheyAreRejected(byte[] bytes)
    {
        // §2: "A Bedrock spec is a UTF-8 TOML 1.0 document." Silently transcoding UTF-16 or
        // UTF-32 would accept documents outside that contract.
        var host = HostOver(bytes);

        Assert.Throws<InvalidDataException>(() => host.ReadText("spec.toml"));
    }

    [Fact]
    public void ReadText_WhenTheBytesAreMalformedUtf8_ThenTheyAreRejectedRatherThanRepaired()
    {
        // 0xFF inside a comment: a permissive decoder substitutes U+FFFD and the document
        // still parses, silently changing authored content with no diagnostic.
        var host = HostOver([(byte)'#', 0xFF, (byte)'\n', (byte)'a', (byte)'=', (byte)'1']);

        Assert.Throws<DecoderFallbackException>(() => host.ReadText("spec.toml"));
    }

    [Fact]
    public void Load_WhenABaseIsNotUtf8_ThenItTakesTheEstablishedUnreadableBaseOutcome()
    {
        // An invalidly encoded BASE keeps the phase-owned diagnostic; it never becomes an
        // unexpected fault and never grows a new code.
        using var temp = TempDirectory.Create();
        File.WriteAllBytes(temp.Resolve("base.toml"), [0xFF, 0xFE, (byte)'a', 0x00]);
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsNotFound);
    }

    [Fact]
    public void Load_WhenABaseIsMalformedUtf8_ThenItTakesTheEstablishedUnreadableBaseOutcome()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllBytes(temp.Resolve("base.toml"), [(byte)'#', 0xFF, (byte)'\n']);
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\n");

        var (result, _) = Compose(root);

        Single(result, DiagnosticCode.SpecExtendsNotFound);
    }

    private static FileSpecTextSource HostOver(byte[] bytes) =>
        new(_ => new MemoryStream(bytes, writable: false), FileIdentity.CreateDefault());

    [Fact]
    public void Compose_WhenDiagnosticsCarryALocation_ThenItIsARealPathAndNeverAnIdentityKey()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./nope.toml\"\n");

        var (result, rootKey) = Compose(root);
        var file = Assert.Single(result.Diagnostics).Location!.Value.File!;

        Assert.Equal(rootKey, file);
        Assert.Equal(Path.GetFullPath(root), file);
        Assert.True(File.Exists(file), "the diagnostic location must be a usable filesystem path");
    }

    [Fact]
    public void Compose_WhenABaseFailsToParse_ThenItsDiagnosticsCarryTheBaseFilePath()
    {
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("base.toml", "this is not toml = = =\n");
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\n");

        var (result, _) = Compose(root);

        Assert.False(result.TryGetValue(out _));
        Assert.All(
            result.Diagnostics.Where(d => d.Location?.File is not null),
            d => Assert.Equal(Path.GetFullPath(basePath), d.Location!.Value.File));
    }

    [Fact]
    public void Compose_WhenABaseDeclaresTheWrongVersion_ThenSpecVersionUnsupportedNamesTheBase()
    {
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("base.toml", "[spec]\nversion = 2\n");
        var root = temp.Write("root.toml", "[spec]\nversion = 1\nextends = \"./base.toml\"\n");

        var (result, _) = Compose(root);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecVersionUnsupported, diagnostic.Code);
        Assert.Equal(Path.GetFullPath(basePath), diagnostic.Location!.Value.File);
    }

    [Fact]
    public void RegisterRoot_WhenTheOperandIsRelative_ThenTheCanonicalKeyIsItsFullPath()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", Leaf);
        var host = new FileSpecTextSource(CliEnvironment.OpenFile, FileIdentity.CreateDefault());

        Assert.Equal(Path.GetFullPath(root), host.RegisterRoot(root));
    }

    [Fact]
    public void Load_WhenTheSameFileIsReachedByTwoSpellings_ThenTheFirstSpellingsKeyIsReturned()
    {
        // The key is assigned once per identity, which is exactly what turns a second
        // visit into a cycle rather than an endless re-load.
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", Leaf);
        temp.Write("base.toml", Leaf);
        var host = new FileSpecTextSource(CliEnvironment.OpenFile, FileIdentity.CreateDefault());
        var rootKey = host.RegisterRoot(root);

        var first = host.Load("./base.toml", rootKey);
        var second = host.Load("./sub/../base.toml", rootKey);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.CanonicalKey, second.CanonicalKey);
    }

    [Fact]
    public void Load_WhenTheReferenceIsEmpty_ThenNothingIsLoaded()
    {
        using var temp = TempDirectory.Create();
        var root = temp.Write("root.toml", Leaf);
        var host = new FileSpecTextSource(CliEnvironment.OpenFile, FileIdentity.CreateDefault());

        Assert.Null(host.Load(string.Empty, host.RegisterRoot(root)));
    }
}
