using System.IO.Compression;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The packaging script's <b>archive writer</b>, exercised against a folder this test controls.
/// <para>
/// The gated self-contained smoke proves the whole distribution, but it costs a full publish and
/// runs only for the platform it is on. This runs on every target in the ordinary suite, in
/// seconds, and asks the one question that was answered wrongly for every archive this project has
/// shipped: does the zip record the apphost as executable? A Windows machine can answer it for a
/// Linux distribution, because the answer is in the archive's own metadata rather than in the
/// filesystem it came from — and the reverse holds too, so a Linux machine answers it for a
/// Windows distribution and must get the same answer a Windows machine would.
/// </para>
/// <para>
/// It runs the real script through <c>-ArchiveOnly</c> rather than reimplementing its rules — the
/// point of a packaging test is that the packaging command is what was tested.
/// </para>
/// </summary>
public sealed class DistributionArchiveTests
{
    [Theory]
    [InlineData("linux-x64")]
    [InlineData("osx-arm64")]
    public async Task Archive_WhenTheDistributionIsUnix_ThenTheApphostIsExecutableAndNothingElseIs(string rid)
    {
        var script = RequireScript();
        using var root = TempDirectory.Create();

        var published = PublishFolder(root, rid);
        await RunScriptAsync(script, rid, root.Path);

        // The whole contract, in one place: safe flat names, no links, no case collisions, the
        // apphost at 0100755, and every ordinary file still at 0100644.
        DistributionArchive.AssertValid(ArchivePath(root, rid), rid);

        // And the specific regression, named rather than implied. `Compress-Archive` recorded
        // 0100644 for every entry, so an unzipped `./FcaBedrock.Cli` answered "Permission denied".
        Assert.Equal(
            DistributionArchive.ExecutableFileMode, ModeOf(ArchivePath(root, rid), DistributionArchive.ExecutableName(rid)));
        Assert.NotEqual(
            DistributionArchive.RegularFileMode, ModeOf(ArchivePath(root, rid), DistributionArchive.ExecutableName(rid)));
        Assert.Equal(
            DistributionArchive.RegularFileMode, ModeOf(ArchivePath(root, rid), "FcaBedrock.Cli.runtimeconfig.json"));

        Assert.True(Directory.Exists(published), "the archive-only run must not disturb the folder it archives.");
    }

    [Fact]
    public async Task Archive_WhenTheDistributionIsWindows_ThenItCarriesTheApphostAndClaimsNoUnixMode()
    {
        // The counterexample that keeps the rule honest: a Windows distribution has no Unix mode to
        // record, and inventing one would be a claim about a platform this archive is not for. Asked
        // on EVERY host, because the answer is the target's and not the writer's: the entry default
        // is zero on Windows and 0100644 on Linux and macOS, so a writer that assigns nothing here
        // produces a different archive depending on the machine the packaging command ran on.
        var script = RequireScript();
        using var root = TempDirectory.Create();

        PublishFolder(root, "win-x64");
        await RunScriptAsync(script, "win-x64", root.Path);

        DistributionArchive.AssertValid(ArchivePath(root, "win-x64"), "win-x64");

        // Both halves of the Windows rule, named rather than implied: the apphost records nothing,
        // and neither does an ordinary file - the two entries whose Unix counterparts differ. The
        // RAW field rather than the shifted mode, because the whole field is the claim: shifting
        // first would accept the writing host's own attribute byte in the low half.
        Assert.Equal(0, ExternalAttributesOf(ArchivePath(root, "win-x64"), "FcaBedrock.Cli.exe"));
        Assert.Equal(0, ExternalAttributesOf(ArchivePath(root, "win-x64"), "FcaBedrock.Cli.runtimeconfig.json"));
    }

    [Fact]
    public void Archive_WhenAWindowsEntryCarriesLowAttributeBits_ThenTheSharedValidatorRejectsIt()
    {
        // The counterexample a shifted check cannot see, and the reason the assertion is on the raw
        // field: 0x00000001 is the DOS read-only bit, exactly the kind of value a writer that
        // assigned nothing would leave behind. It shifts to a Unix mode of zero, so an archive
        // carrying it satisfied every metadata check while being different bytes than the contract
        // asks for.
        //
        // It is put in front of DistributionArchive.AssertValid rather than a local assertion,
        // because that helper is the one the gated self-contained smoke calls: a duplicate check
        // here would prove nothing about the validator the delivery archive is actually held to.
        using var root = TempDirectory.Create();
        var archive = Path.Combine(root.Path, "fcabedrock-win-x64.zip");
        WriteArchive(archive, externalAttributes: 0x00000001);

        var failure = Record.Exception(() => DistributionArchive.AssertValid(archive, "win-x64"));

        Assert.NotNull(failure);
        Assert.Contains("0x00000001", failure.Message, StringComparison.Ordinal);
        Assert.Contains("records none at all", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_WhenAWindowsEntryRecordsNothing_ThenTheSharedValidatorAcceptsIt()
    {
        // The same synthetic archive carrying the field the packaging script really writes, so the
        // rejection above is known to be about the attribute bits rather than about the shape of a
        // hand-built zip.
        using var root = TempDirectory.Create();
        var archive = Path.Combine(root.Path, "fcabedrock-win-x64.zip");
        WriteArchive(archive, externalAttributes: 0);

        DistributionArchive.AssertValid(archive, "win-x64");
    }

    // A two-entry `win-x64` archive built directly, so a value no producing path emits can still be
    // put in front of the validator. The apphost carries the attributes under test; the ordinary
    // file carries the script's own zero, which keeps the failing entry the named one.
    private static void WriteArchive(string archivePath, int externalAttributes)
    {
        using var stream = File.Create(archivePath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        Add(zip, DistributionArchive.ExecutableName("win-x64"), "an apphost", externalAttributes);
        Add(zip, "FcaBedrock.Cli.runtimeconfig.json", "{}", externalAttributes: 0);

        static void Add(ZipArchive zip, string name, string content, int externalAttributes)
        {
            var entry = zip.CreateEntry(name);
            entry.ExternalAttributes = externalAttributes;
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    [Fact]
    public async Task Archive_WhenExtracted_ThenItYieldsExactlyThePublishedFilesAndTheirBytes()
    {
        // Extraction is the step the README documents, so it is the step that is tested: what comes
        // out has to be what went in, at the same names, with the same bytes.
        var script = RequireScript();
        using var root = TempDirectory.Create();

        var rid = "linux-x64";
        var published = PublishFolder(root, rid);
        await RunScriptAsync(script, rid, root.Path);

        var extracted = DistributionArchive.ExtractTo(
            ArchivePath(root, rid), Path.Combine(root.Path, "extracted"));

        var before = Directory.GetFiles(published).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        var after = Directory.GetFiles(extracted).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(before, after);

        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(published, "FcaBedrock.Cli"), TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(Path.Combine(extracted, "FcaBedrock.Cli"), TestContext.Current.CancellationToken));
    }

    // A minimal stand-in for a published folder: the apphost the writer must mark executable, and an
    // ordinary file it must leave alone. Two files rather than a runtime, because what is under test
    // is the archive's metadata, not the SDK's output.
    private static string PublishFolder(TempDirectory root, string rid)
    {
        var folder = Path.Combine(root.Path, rid);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, DistributionArchive.ExecutableName(rid)), "an apphost");
        File.WriteAllText(Path.Combine(folder, "FcaBedrock.Cli.runtimeconfig.json"), "{}");
        return folder;
    }

    private static string ArchivePath(TempDirectory root, string rid) =>
        Path.Combine(root.Path, $"fcabedrock-{rid}.zip");

    /// <summary>The Unix mode one entry records — the high half of the external-attributes field.</summary>
    private static int ModeOf(string archivePath, string entryName) =>
        ExternalAttributesOf(archivePath, entryName) >>> 16;

    /// <summary>
    /// The whole external-attributes field one entry records, unshifted. What a Windows target
    /// claims is this value, not the Unix half of it.
    /// </summary>
    private static int ExternalAttributesOf(string archivePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        return entry.ExternalAttributes;
    }

    private static async Task RunScriptAsync(string script, string rid, string outputRoot) =>
        await ToolProcess.RequireSuccessAsync(
            ToolProcess.PowerShellHost()!,
            ["-NoLogo", "-NoProfile", "-File", script, "-Rid", rid, "-OutputRoot", outputRoot, "-ArchiveOnly"],
            Path.GetDirectoryName(script)!,
            environment: null,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);

    // PowerShell 7 is what the packaging script is written for, and what every CI target and the
    // documented developer workflow already use. Where it is genuinely absent this reports as a
    // skip with the reason, rather than failing for a missing shell.
    private static string RequireScript()
    {
        Assert.SkipUnless(
            ToolProcess.PowerShellHost() is not null,
            "PowerShell 7 (pwsh) is not on PATH; the packaging script cannot be run here.");

        var script = DistributionArchive.Script(RepositoryRoot.Find());
        Assert.True(File.Exists(script), $"the packaging script was not found at '{script}'.");
        return script;
    }
}
