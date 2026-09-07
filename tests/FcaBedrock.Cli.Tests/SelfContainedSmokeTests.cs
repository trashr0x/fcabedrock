using System.IO.Compression;
using System.Runtime.InteropServices;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The standalone distribution contract: a self-contained publish of this native target runs
/// <b>without a .NET runtime installed</b>, keeps every CLI semantic the installed tool has, and
/// archives into something a user can download and unzip.
/// <para>
/// It is the packaged counterpart of <see cref="ToolSmokeTests"/>. The global tool is
/// framework-dependent — it needs a matching runtime on the machine — and the M8 release gate adds a
/// distribution that does not. What that costs is a copy of the runtime beside the executable, and
/// what it buys is a program that runs where <c>dotnet</c> is absent; both are checked here rather
/// than assumed from the publish command's exit code.
/// </para>
/// <para>
/// <b>Gated</b>, like the tool smoke, because it is slow and writes a few hundred megabytes: it
/// always reports as skipped rather than silently not existing. It publishes only for the
/// <b>running</b> RID — a cross-published folder proves the SDK can emit files for another platform
/// and nothing whatsoever about running there, and this suite does not make claims it cannot test.
/// </para>
/// </summary>
public sealed class SelfContainedSmokeTests
{
    private const string Gate = "FCABEDROCK_SELFCONTAINED_SMOKE";

    /// <summary>The apphost the publish produces, named from the assembly rather than the tool command.</summary>
    private static string ExecutableName =>
        OperatingSystem.IsWindows() ? "FcaBedrock.Cli.exe" : "FcaBedrock.Cli";

    /// <summary>
    /// A spec whose <c>binding.locale</c> is a real BCP-47 tag. Resolving it needs ICU, so it is the
    /// direct test of the CLI's deliberate refusal to enable invariant globalization: under an
    /// invariant build this spec fails to resolve, and under a correct self-contained publish it
    /// validates.
    /// </summary>
    private const string LocaleSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true
        delimiter = ","
        locale = "de-DE"

        [[attribute]]
        name = "colour"
        source = { kind = "column", index = 0 }
        discretizer = { kind = "identity" }
        scale = { kind = "nominal" }
        declared_domain = ["red", "green"]

        """;

    [Fact]
    public async Task SelfContainedPublish_WhenTheSmokeGateIsSet_ThenItRunsWithoutAnInstalledRuntime()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(Gate) == "1",
            $"set {Gate}=1 to run the self-contained publish smoke.");

        var token = TestContext.Current.CancellationToken;
        var dotnet = ToolProcess.DotnetHost();
        var timeout = TimeSpan.FromMinutes(10);
        var rid = RuntimeInformation.RuntimeIdentifier;

        using var root = TempDirectory.Create();
        var publish = Path.Combine(root.Path, "publish");
        var work = Path.Combine(root.Path, "work");
        Directory.CreateDirectory(work);

        var repository = RepositoryRoot();
        var project = Path.Combine(repository, "src", "FcaBedrock.Cli", "FcaBedrock.Cli.csproj");
        Assert.True(File.Exists(project), $"the CLI project was not found at '{project}'.");

        // (1) Publish for the RUNNING runtime identifier, self-contained.
        //
        // PackAsTool must be overridden: a tool package and a self-contained apphost are different
        // distributions of the same program, and the SDK refuses to produce the second while the
        // first is declared. Nothing else is overridden - no trimming, no single file, no AOT, and
        // no invariant globalization - because every one of those changes what the program does,
        // and this is meant to be the same program in a different wrapper.
        await ToolProcess.RequireSuccessAsync(
            dotnet,
            [
                "publish", project,
                "-c", "Release",
                "-r", rid,
                "--self-contained", "true",
                "-p:PackAsTool=false",
                "-o", publish,
            ],
            repository,
            environment: null,
            timeout,
            token);

        // (2) The apphost is there, under its assembly name. There is deliberately no rename to
        // `fcabedrock`: the tool COMMAND name belongs to the global-tool shim, and inventing a
        // second name for the same binary would make the two distributions disagree about what the
        // program is called.
        var executable = Path.Combine(publish, ExecutableName);
        Assert.True(File.Exists(executable), $"the publish produced no '{ExecutableName}' in '{publish}'.");

        // (3) The runtime really is bundled. These three are the host, the policy resolver, and the
        // runtime library itself: a framework-dependent publish has none of them.
        foreach (var bundled in BundledRuntimeFiles())
        {
            Assert.True(
                File.Exists(Path.Combine(publish, bundled)),
                $"'{bundled}' is absent, so this publish is not self-contained.");
        }

        // (4) ICU is deliberately NOT asserted as a bundled file, and that is a fact about the
        // distribution worth stating rather than a gap. "Self-contained" bundles the .NET runtime,
        // not the operating system's globalization data: .NET takes ICU from the host - Windows 10+
        // ships it, and a Linux host needs its libicu packages - so a correct publish carries no
        // icu* file of its own on any of the three required targets. The culture guarantee is
        // therefore checked where it can actually be checked: behaviourally, at step (9), by
        // resolving a real BCP-47 tag. That also records the real deployment caveat - a
        // self-contained archive still has OS-native prerequisites.
        //
        // (5) The published runtime configuration must not have acquired the invariant switch.
        var runtimeConfig = Path.Combine(publish, "FcaBedrock.Cli.runtimeconfig.json");
        Assert.True(File.Exists(runtimeConfig), "the publish produced no runtimeconfig.json.");
        Assert.DoesNotContain(
            "System.Globalization.Invariant",
            await File.ReadAllTextAsync(runtimeConfig, token),
            StringComparison.Ordinal);

        // (6) It runs with the SDK's own environment removed. DOTNET_ROOT and the multilevel-lookup
        // switch are what a framework-dependent host would use to FIND a runtime; blanking them
        // proves the executable is loading the copy beside it rather than one on the machine.
        var isolated = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_ROOT"] = string.Empty,
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
        };

        var version = await ToolProcess.RequireSuccessAsync(executable, ["--version"], work, isolated, timeout, token);
        Assert.Equal(ToolVersion.Current + "\n", version.StandardOutput);
        Assert.Equal(string.Empty, version.StandardError);

        // (7) A real conversion, both formats, with the manifest. The same invocation the installed
        // tool smoke makes, so the two distributions are compared on identical work.
        var spec = root.Write(Path.Combine("work", "tiny.toml"), CliFixtures.IndexBoundSpec);
        var data = root.Write(Path.Combine("work", "tiny.csv"), CliFixtures.WideData);
        var target = Path.Combine(work, "out", "tiny");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var converted = await ToolProcess.RequireSuccessAsync(
            executable,
            ["convert", spec, data, "--out", target, "--format", "both"],
            work,
            isolated,
            timeout,
            token);

        Assert.Equal(string.Empty, converted.StandardOutput);
        Assert.Equal(string.Empty, converted.StandardError);

        // (8) The CONTEXT BYTES agree with what this process produces from the same inputs. That is
        // the check that matters: a distribution that ran but converted differently would pass every
        // structural assertion above.
        foreach (var extension in (string[])[".cxt", ".dat"])
        {
            var produced = await File.ReadAllBytesAsync(target + extension, token);
            var inProcess = await ConvertInProcessAsync(root, spec, data, extension, token);

            Assert.Equal(inProcess, produced);
        }

        Assert.True(File.Exists(target + ".manifest.toml"), "the self-contained convert wrote no manifest.");

        // (9) Culture: a spec naming a real BCP-47 locale resolves. Under invariant globalization
        // GetCultureInfo(..., predefinedOnly: true) would reject it, so this fails loudly if the
        // publish ever acquires that switch.
        var localeSpec = root.Write(Path.Combine("work", "locale.toml"), LocaleSpec);
        var validated = await ToolProcess.RequireSuccessAsync(
            executable, ["validate", localeSpec], work, isolated, timeout, token);
        Assert.Equal(string.Empty, validated.StandardError);

        // (10) Failure semantics survive the wrapper: a refused convert exits non-zero, says why,
        // and commits nothing. A distribution that published a partial artifact on failure would
        // break the guarantee the publication transaction exists to make.
        var errorSpec = root.Write(Path.Combine("work", "bad.toml"), CliFixtures.ErrorSpec);
        var refusedTarget = Path.Combine(work, "out", "refused");
        var refused = await ToolProcess.RunAsync(
            executable,
            ["convert", errorSpec, data, "--out", refusedTarget, "--format", "dat"],
            work,
            isolated,
            timeout,
            token);

        Assert.NotEqual(0, refused.ExitCode);
        Assert.NotEqual(string.Empty, refused.StandardError);
        foreach (var extension in (string[])[".dat", ".cxt", ".manifest.toml"])
        {
            Assert.False(
                File.Exists(refusedTarget + extension),
                $"a refused convert left '{refusedTarget + extension}' behind.");
        }

        // (11) Archive: the folder zips, the archive carries the executable and the bundled runtime
        // at its root, and it is not trivially small.
        var archive = Path.Combine(root.Path, $"fcabedrock-{rid}.zip");
        ZipFile.CreateFromDirectory(publish, archive, CompressionLevel.Fastest, includeBaseDirectory: false);

        using var inspected = ZipFile.OpenRead(archive);
        var entries = inspected.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains(ExecutableName, entries);
        Assert.Contains("FcaBedrock.Cli.runtimeconfig.json", entries);
        foreach (var bundled in BundledRuntimeFiles())
        {
            Assert.Contains(bundled, entries);
        }

        Assert.True(new FileInfo(archive).Length > 10L * 1024 * 1024, "the archive is too small to carry a runtime.");
    }

    // The three files that only a self-contained publish carries: the host resolver, the policy
    // resolver, and the runtime library. Named per platform rather than globbed, so an absent one is
    // a specific failure rather than an empty set nobody notices.
    private static IReadOnlyList<string> BundledRuntimeFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll"];
        }

        return OperatingSystem.IsMacOS()
            ? ["libhostfxr.dylib", "libhostpolicy.dylib", "libcoreclr.dylib", "System.Private.CoreLib.dll"]
            : ["libhostfxr.so", "libhostpolicy.so", "libcoreclr.so", "System.Private.CoreLib.dll"];
    }

    // The same conversion, in this process, through the same argv boundary the executable used.
    // Comparing bytes across the two is what turns "it ran" into "it ran correctly".
    private static async Task<byte[]> ConvertInProcessAsync(
        TempDirectory root, string spec, string data, string extension, CancellationToken token)
    {
        var target = Path.Combine(root.Path, "inprocess", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var harness = new CliTestHarness();
        var exitCode = await harness.RunAsync("convert", spec, data, "--out", target, "--format", "both");

        Assert.Equal(0, exitCode);
        return await File.ReadAllBytesAsync(target + extension, token);
    }

    // Never a fixed `..` hop count: the answer is wherever the solution file actually is.
    private static string RepositoryRoot()
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
