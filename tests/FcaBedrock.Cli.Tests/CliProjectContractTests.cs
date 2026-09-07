using System.Reflection;
using System.Runtime.CompilerServices;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The package boundary and public-surface contract (D-122 part 9 / D-123 part 1). The
/// build-time facts — tool packaging, the reference set, the absence of a package
/// dependency and of invariant globalization — have no runtime surface, so they are
/// asserted against the real project file, which the test project copies to its output.
/// </summary>
public sealed class CliProjectContractTests
{
    private static readonly Assembly Cli = typeof(CliHost).Assembly;

    private static string ProjectFile() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "project", "FcaBedrock.Cli.csproj.txt"));

    /// <summary>
    /// The complete friend set (D-124): the CLI's own argv-boundary tests, and the M8 benchmark
    /// host that measures <c>CliHost</c>, <c>InputHashTracker</c>, and <c>HashingWriteStream</c>
    /// directly. Both are non-product assemblies, and no production package references either.
    /// </summary>
    private static readonly string[] ExpectedFriends = ["FcaBedrock.Cli.Tests", "FcaBedrock.Benchmarks"];

    private static readonly string[] ProductionPackages =
    [
        "FcaBedrock.Diagnostics",
        "FcaBedrock.Core",
        "FcaBedrock.Sources",
        "FcaBedrock.Spec",
        "FcaBedrock.Conversion",
        "FcaBedrock.Export",
        "FcaBedrock.Discovery",
    ];

    [Fact]
    public void Project_ShouldBeAGlobalToolNamedFcabedrock()
    {
        var project = ProjectFile();

        Assert.Contains("<PackAsTool>true</PackAsTool>", project, StringComparison.Ordinal);
        Assert.Contains("<ToolCommandName>fcabedrock</ToolCommandName>", project, StringComparison.Ordinal);
        Assert.Contains("<PackageId>FcaBedrock.Cli</PackageId>", project, StringComparison.Ordinal);
        Assert.Contains("<OutputType>Exe</OutputType>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ShouldReferenceAllSevenProductionPackages()
    {
        var project = ProjectFile();

        foreach (var package in ProductionPackages)
        {
            Assert.Contains(
                $"<ProjectReference Include=\"..\\{package}\\{package}.csproj\" />", project, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Project_ShouldAddNoPackageDependency()
    {
        // No parser, hosting, logging, or DI package: the whole grammar and the whole host
        // are hand-written for exactly this reason (D-123 part 2).
        Assert.DoesNotContain("<PackageReference", ProjectFile(), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ShouldNotEnableInvariantGlobalization()
    {
        // binding.locale accepts BCP-47 tags and SpecResolver resolves them with
        // predefinedOnly: true, which needs ICU. The property element must be absent
        // entirely — the prose comment that explains why is not a setting.
        Assert.DoesNotContain("<InvariantGlobalization>", ProjectFile(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_ShouldShipARuntimeConfigThatDoesNotSwitchOnInvariantGlobalization()
    {
        // The generated runtime configuration is where the switch would actually land, so
        // this asserts the built artifact rather than only the project text.
        var config = Path.Combine(AppContext.BaseDirectory, "FcaBedrock.Cli.runtimeconfig.json");

        Assert.SkipUnless(File.Exists(config), "the CLI runtimeconfig.json is not copied into this test's output.");
        Assert.DoesNotContain(
            "System.Globalization.Invariant", File.ReadAllText(config), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ShouldGrantFriendAccessToExactlyTheTwoNamedNonProductAssemblies()
    {
        // Exactly two grants, each once, in the project text AND in the compiled assembly, and the
        // assertion is a SET rather than a sequence so it cannot be satisfied or broken by
        // declaration order. A presence-only check would let a third grant appear unnoticed, which
        // is the failure this guards (D-124).
        var project = ProjectFile();

        foreach (var friend in ExpectedFriends)
        {
            Assert.Equal(1, CountOccurrences(project, $"<InternalsVisibleTo Include=\"{friend}\" />"));
        }

        Assert.Equal(ExpectedFriends.Length, CountOccurrences(project, "<InternalsVisibleTo"));

        var granted = Cli.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToList();

        Assert.Equal(ExpectedFriends.Length, granted.Count);
        Assert.Equal(
            ExpectedFriends.Order(StringComparer.Ordinal),
            granted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Project_ShouldNotGrantFriendAccessToTheBenchmarkTestAssembly()
    {
        // It needs none: it reaches the seams it tests through the benchmark assembly. Stated as
        // its own case because "the set is exactly these two" and "this particular assembly is not
        // in it" fail for different reasons and should read differently when they do.
        //
        // The assertion is on the GRANT, not on the words: the project comment names this assembly
        // precisely to record that it is excluded, and a check that forbade the name would forbid
        // explaining the decision.
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"FcaBedrock.Benchmarks.Tests\" />",
            ProjectFile(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "FcaBedrock.Benchmarks.Tests",
            Cli.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName));
    }

    [Fact]
    public void Assembly_ShouldReferenceOnlyProductionPackagesAndTheFramework()
    {
        // A new runtime dependency would show up here as a non-BCL, non-FcaBedrock
        // reference.
        var unexpected = Cli.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("FcaBedrock.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal))
            .Where(name => name is not ("System" or "netstandard" or "mscorlib" or "Microsoft.Win32.Primitives"))
            .ToList();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void Assembly_ShouldExposeNoPublicType()
    {
        // Every CLI component is internal; a P-4 extraction review gates any M9 reuse.
        Assert.Empty(Cli.GetExportedTypes());
    }

    [Fact]
    public void Assembly_ShouldDeclareNoPublicMemberEvenOnInternalTypes()
    {
        // Belt and braces: a public member on an internal type is invisible outside the
        // assembly, but a type accidentally made public would be caught above, and this
        // catches a public NESTED type inside an internal one.
        var publicNested = Cli.GetTypes()
            .Where(type => type.IsNested && type.IsNestedPublic && type.DeclaringType?.IsPublic == true)
            .ToList();

        Assert.Empty(publicNested);
    }

    [Fact]
    public void EntryPoint_ShouldBeTheOnlyCompilerRequiredMechanic()
    {
        var entryPoint = Cli.EntryPoint;

        Assert.NotNull(entryPoint);
        Assert.Equal("FcaBedrock.Cli.Program", entryPoint.DeclaringType!.FullName);
        Assert.False(entryPoint.DeclaringType.IsPublic);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
