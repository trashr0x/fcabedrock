using System.Security.Cryptography;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// A prepared run's chain facts (D-140): every spec file it read, root first, each with its §15
/// spelling, the hash of its raw bytes as read and the identity it had when loaded.
/// </summary>
public sealed class PreparedRunTests
{
    [Fact]
    public async Task PrepareAsync_WhenTheRootExtendsABase_ThenTheChainIsRootFirstWithHashesAndIdentities()
    {
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("shared/base.toml", """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            """);

        // A byte-order mark and CRLF endings, which the hash must cover exactly as read.
        var rootPath = temp.Resolve("root.toml");
        File.WriteAllBytes(rootPath, [
            0xEF, 0xBB, 0xBF,
            .. System.Text.Encoding.UTF8.GetBytes(
                "[spec]\r\nversion = 1\r\nextends = \"shared/base.toml\"\r\n\r\n[[attribute]]\r\nname = \"colour\"\r\n"
                + "source = { kind = \"column\", index = 0 }\r\ndiscretizer = { kind = \"identity\" }\r\n"
                + "scale = { kind = \"nominal\" }\r\ndeclared_domain = [\"red\", \"green\"]\r\n"),
        ]);
        var data = temp.Write("data.csv", PublicationRun.Data);

        var run = OutcomeAssert.Completed(await RunPreparation.PrepareAsync(
            new FileRunRequest { SpecPath = rootPath, DataPath = data }, new OrchestrationHost(File.OpenRead), CancellationToken.None)).Value;

        Assert.Equal(2, run.SpecChain.Count);
        var (root, loaded) = (run.SpecChain[0], run.SpecChain[1]);

        Assert.Equal(rootPath, root.Spelling);
        Assert.Equal(Path.GetFullPath(rootPath), root.FullPath);
        Assert.Equal("shared/base.toml", loaded.Spelling);
        Assert.Equal(Path.GetFullPath(basePath), loaded.FullPath);

        foreach (var file in run.SpecChain)
        {
            Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.FullPath))), file.Hash);
            Assert.Equal(FileIdentity.CreateDefault().KeyFor(file.FullPath), file.Identity);
        }
    }
}
