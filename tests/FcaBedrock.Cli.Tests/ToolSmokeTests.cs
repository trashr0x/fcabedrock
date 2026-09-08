using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The distribution contract end to end (D-122 part 13): the packed tool installs from a local
/// feed, runs as an installed command, converts real files, records a truthful manifest, and
/// uninstalls cleanly.
/// <para>
/// It is <b>gated</b> because it is the only test that installs anything: every other suite runs
/// in process. The gate is the in-repo idiom — the fact is always discovered and reports as
/// skipped, so its absence is visible rather than silent.
/// </para>
/// <para>
/// Nothing here touches the machine. The feed is the fixture's own directory behind a
/// <c>&lt;clear /&gt;</c> configuration, the package cache and the tool path are inside one
/// disposable root, the noise variables go on the child alone, and the install is never
/// <c>--global</c>. Step 8 uninstalls because uninstalling is part of the contract; the
/// <c>using</c> root is what actually guarantees cleanup on every path.
/// </para>
/// </summary>
[Collection(ToolPackageCollection.Name)]
public sealed class ToolSmokeTests(ToolPackage package)
{
    [Fact]
    public async Task InstalledTool_WhenTheSmokeGateIsSet_ThenItPacksInstallsConvertsAndUninstalls()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("FCABEDROCK_TOOL_SMOKE") == "1",
            "set FCABEDROCK_TOOL_SMOKE=1 to run the installed-tool smoke.");

        var token = TestContext.Current.CancellationToken;
        var dotnet = ToolProcess.DotnetHost();
        var timeout = TimeSpan.FromMinutes(2);

        using var root = TempDirectory.Create();
        var tools = Path.Combine(root.Path, "tools");
        var packages = Path.Combine(root.Path, "packages");
        var work = Path.Combine(root.Path, "work");
        var target = Path.Combine(work, "out", "tiny");
        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(packages);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // (1) The package the shared fixture produced — this test packs nothing of its own.
        Assert.True(File.Exists(package.NupkgPath), $"the fixture packed nothing. {package.Describe()}");

        // `<clear />` first, so no machine-wide or default source can be consulted: if the local
        // feed does not hold the package, the install fails rather than reaching the network.
        // The document is CONSTRUCTED, never interpolated: a temporary root spelled with `&`, `<`
        // or a quote must survive into the parsed attribute verbatim instead of breaking the file.
        var configuration = root.Resolve("nuget.config");
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", standalone: null),
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement(
                        "add",
                        new XAttribute("key", "local"),
                        new XAttribute("value", package.FeedDirectory)))));

        var xml = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
        };

        using (var writer = XmlWriter.Create(configuration, xml))
        {
            document.Save(writer);
        }

        // Read back what was written: it parses, `<clear />` still comes first, exactly one source
        // is declared, and the feed path round-trips exactly however it happens to be spelled.
        var sources = XDocument.Load(configuration).Root!.Element("packageSources")!.Elements().ToList();
        var declared = Assert.Single(sources, element => element.Name.LocalName == "add");

        Assert.Equal("clear", sources[0].Name.LocalName);
        Assert.Equal(package.FeedDirectory, (string?)declared.Attribute("value"));

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NUGET_PACKAGES"] = packages,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        };

        // (2) Install exactly that version, into this root's tool path.
        await ToolProcess.RequireSuccessAsync(
            dotnet,
            [
                "tool", "install",
                "--tool-path", tools,
                "--configfile", configuration,
                "--version", package.Version,
                "FcaBedrock.Cli",
            ],
            root.Path,
            environment,
            timeout,
            token);

        // (3) It is listed there, at that version, under that command name.
        var listed = await ToolProcess.RequireSuccessAsync(
            dotnet, ["tool", "list", "--tool-path", tools], root.Path, environment, timeout, token);

        var row = Assert.Single(
            listed.StandardOutput.Split('\n'),
            line => line.TrimStart().StartsWith("fcabedrock.cli", StringComparison.Ordinal));

        Assert.Contains(package.Version, row, StringComparison.Ordinal);
        Assert.EndsWith("fcabedrock", row.TrimEnd(), StringComparison.Ordinal);

        // (4) The shim runs, and prints exactly one version line and nothing on stderr.
        var shim = Path.Combine(tools, OperatingSystem.IsWindows() ? "fcabedrock.exe" : "fcabedrock");
        var version = await ToolProcess.RequireSuccessAsync(shim, ["--version"], work, environment, timeout, token);

        Assert.Equal(ToolVersion.Current + "\n", version.StandardOutput);
        Assert.Equal(string.Empty, version.StandardError);

        // (5) A real conversion, over a fully declared spec whose two values are both inside its
        // declared domain — so a clean run emits no diagnostic at all.
        var spec = root.Write(Path.Combine("work", "tiny.toml"), CliFixtures.IndexBoundSpec);
        var data = root.Write(Path.Combine("work", "tiny.csv"), CliFixtures.WideData);

        var converted = await ToolProcess.RequireSuccessAsync(
            shim,
            ["convert", spec, data, "--out", target, "--format", "both"],
            work,
            environment,
            timeout,
            token);

        Assert.Equal(string.Empty, converted.StandardOutput);
        Assert.Equal(string.Empty, converted.StandardError);

        foreach (var extension in (string[])[".cxt", ".dat", ".manifest.toml"])
        {
            var produced = target + extension;
            Assert.True(File.Exists(produced), $"convert wrote no '{produced}'.");
            Assert.True(new FileInfo(produced).Length > 0, $"'{produced}' is empty.");
        }

        // The two artifacts this test EXPECTS, derived from the invocation it made and never from
        // the manifest it is about to read.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cxt"] = target + ".cxt",
            ["dat"] = target + ".dat",
        };

        // (6) The manifest describes THIS run: the installed tool's own version, and hashes this
        // test computes itself over the committed files. No literal hash is ever pinned.
        var manifest = await File.ReadAllTextAsync(target + ".manifest.toml", token);
        var lines = manifest.Split('\n');

        Assert.Contains($"tool_version = \"{ToolVersion.Current}\"", lines);
        Assert.Contains($"input_hash = \"{Sha256(data)}\"", lines);

        // The manifest is not allowed to choose what gets hashed: it must select exactly the two
        // outputs above — one entry per format, neither swapped, duplicated nor pointing anywhere
        // else — and only then are the EXPECTED files hashed and compared to its values.
        CheckOutputs(lines, Path.GetDirectoryName(target)!, expected);

        // (7) argv[0] survived the shim verbatim: the audit records the real entry
        // assembly under this tool path, never the bare command token.
        var commandLine = Assert.Single(
            lines, line => line.StartsWith("command_line = [", StringComparison.Ordinal));
        var argv0 = TomlString(commandLine);

        Assert.NotEqual("fcabedrock", argv0);
        Assert.EndsWith("FcaBedrock.Cli.dll", argv0, StringComparison.Ordinal);

        // Containment is a question about LOCATIONS, so both sides are resolved before they are
        // compared. A temporary root is routinely reached through a symlinked ancestor — on macOS
        // `/var` is a link to `/private/var`, so this test is handed `/var/folders/…` while the
        // process it launched reports its own entry assembly under `/private/var/folders/…` — and
        // the two spellings name one directory. Comparing them as text fails on the spelling while
        // the property under test holds perfectly. The resolution is the CLI's own, so "the same
        // place" means here exactly what it means everywhere else in this repository.
        var canonical = FileIdentity.CreateDefault();
        var toolPath = canonical.CanonicalPath(Path.GetFullPath(tools));
        var entryPoint = canonical.CanonicalPath(Path.GetFullPath(argv0));

        Assert.True(
            entryPoint.StartsWith(toolPath, PathComparison),
            $"the manifest's argv[0] '{argv0}' resolves to '{entryPoint}', which is not under the "
            + $"isolated tool path '{tools}' ('{toolPath}').");

        // (8) Uninstall, and (9) confirm it is gone.
        await ToolProcess.RequireSuccessAsync(
            dotnet,
            ["tool", "uninstall", "--tool-path", tools, "FcaBedrock.Cli"],
            root.Path,
            environment,
            timeout,
            token);

        var remaining = await ToolProcess.RequireSuccessAsync(
            dotnet, ["tool", "list", "--tool-path", tools], root.Path, environment, timeout, token);

        Assert.DoesNotContain("fcabedrock", remaining.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    // Windows aliases paths by case; nothing else here does.
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Sha256(string path) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>
    /// The whole output-binding contract, in one place: the manifest must select exactly the
    /// artifacts <paramref name="expected"/> names — right format, inside
    /// <paramref name="outputRoot"/>, one entry each, no duplicate and no third — and only then is
    /// each EXPECTED file hashed and compared to the manifest's value for it. A hash is never
    /// allowed to vouch for a file the manifest itself picked.
    /// </summary>
    private static void CheckOutputs(string[] lines, string outputRoot, IReadOnlyDictionary<string, string> expected)
    {
        var outputs = ParseOutputs(lines);

        Assert.Equal(expected.Count, outputs.Count);
        Assert.Equal(
            expected.Keys.Order(StringComparer.Ordinal),
            outputs.Select(output => output.Format).Order(StringComparer.Ordinal));

        var resolved = new List<string>();
        foreach (var output in outputs)
        {
            // Bounded and resolved BEFORE any file access, so a `..` escape, an absolute path
            // elsewhere, or a lexical prefix of the output root is refused while it is still text.
            var candidate = ResolveUnder(outputRoot, output.Path);
            resolved.Add(candidate);

            var wanted = expected[output.Format];
            Assert.Equal(Path.GetFullPath(wanted), candidate, PathComparer);
            Assert.Equal(Sha256(wanted), output.Hash);
        }

        Assert.Equal(expected.Count, resolved.Distinct(PathComparer).Count());
    }

    /// <summary>Every <c>[[run.outputs]]</c> table, exactly as the manifest spells it.</summary>
    private static List<(string Format, string Path, string Hash)> ParseOutputs(string[] lines)
    {
        var outputs = new List<(string Format, string Path, string Hash)>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.Equals(lines[i].TrimEnd(), "[[run.outputs]]", StringComparison.Ordinal))
            {
                continue;
            }

            string? format = null;
            string? path = null;
            string? hash = null;
            for (var j = i + 1; j < lines.Length && lines[j].TrimEnd().Length > 0; j++)
            {
                var line = lines[j].TrimEnd();
                if (line.StartsWith("format = ", StringComparison.Ordinal))
                {
                    format = TomlString(line);
                }
                else if (line.StartsWith("path = ", StringComparison.Ordinal))
                {
                    path = TomlString(line);
                }
                else if (line.StartsWith("hash = ", StringComparison.Ordinal))
                {
                    hash = TomlString(line);
                }
            }

            Assert.NotNull(format);
            Assert.NotNull(path);
            Assert.NotNull(hash);
            outputs.Add((format, path, hash));
        }

        return outputs;
    }

    // Resolution first, then containment: `Path.GetFullPath` collapses any `..`, and the trailing
    // separator on the boundary is what defeats a lexical prefix such as `<root>-elsewhere`.
    private static string ResolveUnder(string root, string path)
    {
        Assert.False(string.IsNullOrWhiteSpace(path), "the manifest names an empty output path.");

        var resolved = Path.GetFullPath(path);
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        Assert.True(
            resolved.Length > boundary.Length && resolved.StartsWith(boundary, PathComparison),
            $"the manifest's output path '{path}' resolves to '{resolved}', outside '{boundary}'.");

        return resolved;
    }

    // The first TOML basic string on the line, unescaped. Enough for the three fields this test
    // reads — and honest about the escaping the writer actually applies to a Windows path.
    private static string TomlString(string line)
    {
        var start = line.IndexOf('"', StringComparison.Ordinal);
        Assert.True(start >= 0, $"no quoted value in '{line}'.");

        var value = new StringBuilder();
        for (var i = start + 1; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '\\' && i + 1 < line.Length)
            {
                var escaped = line[++i];
                value.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped,
                });
                continue;
            }

            if (character == '"')
            {
                break;
            }

            value.Append(character);
        }

        return value.ToString();
    }
}
