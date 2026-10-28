using System.Reflection;
using System.Runtime.CompilerServices;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The package boundary and public-surface contract of the run and publication coordinator
/// (D-140). The build-time facts (the reference set, the absence of a package dependency, the
/// friend grants) are asserted against the real project file, which this test project copies to
/// its output, and the compiled surface is compared with the reviewed pin beside it.
/// </summary>
public sealed class OrchestrationProjectContractTests
{
    private static readonly Assembly Orchestration = typeof(OrchestrationHost).Assembly;

    /// <summary>The six library packages the coordinator uses: never Discovery, never a host.</summary>
    private static readonly string[] LibraryPackages =
    [
        "FcaBedrock.Diagnostics",
        "FcaBedrock.Core",
        "FcaBedrock.Sources",
        "FcaBedrock.Spec",
        "FcaBedrock.Conversion",
        "FcaBedrock.Export",
    ];

    /// <summary>
    /// The complete friend set: this test project, the CLI's argv-boundary tests (whose harness
    /// injects the internal seams and whose shared support implements an internal interface), and
    /// the benchmark host that measures the two hash seams directly (D-124). All three are
    /// non-product assemblies; no production package is granted anything.
    /// </summary>
    private static readonly string[] ExpectedFriends =
        ["FcaBedrock.Orchestration.Tests", "FcaBedrock.Cli.Tests", "FcaBedrock.Benchmarks"];

    /// <summary>
    /// The types a host constructs: the host seam, the four requests and the operation progress. Every
    /// other exported type is returned, never built.
    /// </summary>
    private static readonly string[] HostConstructed =
    [
        "FcaBedrock.Orchestration.ConvertOutput",
        "FcaBedrock.Orchestration.FileRunRequest",
        "FcaBedrock.Orchestration.ManifestHostFacts",
        "FcaBedrock.Orchestration.OperationProgress",
        "FcaBedrock.Orchestration.OrchestrationHost",
        "FcaBedrock.Orchestration.SpecDelivery",
    ];

    /// <summary>The closed families: abstract bases a host's switch can treat as complete.</summary>
    private static readonly string[] ClosedFamilies =
    [
        "FcaBedrock.Orchestration.OperationOutcome`1",
        "FcaBedrock.Orchestration.PreflightedPublication",
        "FcaBedrock.Orchestration.PreparedRun",
        "FcaBedrock.Orchestration.RelocationDecision",
    ];

    private static string ProjectFile() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "project", "FcaBedrock.Orchestration.csproj.txt"));

    [Fact]
    public void Project_ShouldReferenceExactlyTheSixLibraryPackagesItUses()
    {
        var project = ProjectFile();

        foreach (var package in LibraryPackages)
        {
            Assert.Equal(
                1, CountOccurrences(project, $"<ProjectReference Include=\"..\\{package}\\{package}.csproj\" />"));
        }

        Assert.Equal(LibraryPackages.Length, CountOccurrences(project, "<ProjectReference"));
    }

    [Fact]
    public void Project_ShouldAddNoPackageDependency()
    {
        Assert.DoesNotContain("<PackageReference", ProjectFile(), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ShouldGrantFriendAccessToExactlyTheThreeNamedNonProductAssemblies()
    {
        // Each grant once, in the project text AND in the compiled assembly, compared as a SET so
        // declaration order can neither satisfy nor break it. A presence-only check would let a
        // fourth grant appear unnoticed; growth is a reviewed change to this set (D-140).
        var project = ProjectFile();

        foreach (var friend in ExpectedFriends)
        {
            Assert.Equal(1, CountOccurrences(project, $"<InternalsVisibleTo Include=\"{friend}\" />"));
        }

        Assert.Equal(ExpectedFriends.Length, CountOccurrences(project, "<InternalsVisibleTo"));

        var granted = Orchestration.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToList();

        Assert.Equal(ExpectedFriends.Length, granted.Count);
        Assert.Equal(ExpectedFriends.Order(StringComparer.Ordinal), granted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Project_ShouldNotGrantFriendAccessToTheBenchmarkTestAssembly()
    {
        // The assertion is on the GRANT, not on the words: the project comment names this assembly
        // to record that it is excluded.
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"FcaBedrock.Benchmarks.Tests\" />", ProjectFile(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FcaBedrock.Benchmarks.Tests",
            Orchestration.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName));
    }

    [Fact]
    public void Assembly_ShouldReferenceOnlyLibraryPackagesAndTheFramework()
    {
        var references = Orchestration.GetReferencedAssemblies().Select(reference => reference.Name!).ToList();

        // Every FcaBedrock reference is one of the six; Discovery and the hosts never appear.
        Assert.All(
            references.Where(name => name.StartsWith("FcaBedrock.", StringComparison.Ordinal)),
            name => Assert.Contains(name, LibraryPackages));

        var unexpected = references
            .Where(name => !name.StartsWith("FcaBedrock.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal))
            .Where(name => name is not ("System" or "netstandard" or "mscorlib" or "Microsoft.Win32.Primitives"))
            .ToList();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void Assembly_ShouldExposeExactlyThePinnedPublicSurface()
    {
        // The pin was written and reviewed against D-140's surface; it does not bless whatever
        // reflection prints. A change here is a deliberate, reviewed edit of PublicSurface.txt.
        var pinned = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicSurface.txt"))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actual = PublicSurfaceFormatter.Lines(Orchestration);

        var missing = pinned.Except(actual, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(pinned, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0 && pinned.SequenceEqual(actual, StringComparer.Ordinal),
            "the public surface does not match PublicSurface.txt.\n"
                + $"missing from the assembly ({missing.Count}):\n  " + string.Join("\n  ", missing) + "\n"
                + $"not in the pin ({unexpected.Count}):\n  " + string.Join("\n  ", unexpected) + "\n"
                + "actual surface:\n" + string.Join("\n", actual));
    }

    [Fact]
    public void Assembly_ShouldGiveNoReturnedTypeAPublicConstructor()
    {
        // Only the host seam, the requests and the operation progress are built by a host; everything
        // else is obtained from the operation that produces it.
        var exported = Orchestration.GetExportedTypes();
        Assert.Equal(HostConstructed.Order(StringComparer.Ordinal), exported
            .Where(type => type.GetConstructors().Length > 0)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Assembly_ShouldGiveNoClosedFamilyBaseAConstructorOutsideTheAssembly()
    {
        // Every exported abstract class (static classes aside) is constructible only inside this
        // assembly: private protected or private constructors, and no protected one that another
        // assembly's derived type could call. That is a constructor-access claim, not a barrier
        // against reflection or the friend assemblies.
        var bases = Orchestration.GetExportedTypes()
            .Where(type => type.IsClass && type.IsAbstract && !type.IsSealed)
            .ToList();

        Assert.Equal(ClosedFamilies, bases.Select(type => type.FullName!).Order(StringComparer.Ordinal));
        Assert.All(bases, type =>
        {
            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotEmpty(constructors);
            Assert.All(constructors, constructor => Assert.True(
                constructor.IsFamilyAndAssembly || constructor.IsPrivate,
                $"{type.FullName} has a constructor another assembly can reach."));
        });
    }

    [Fact]
    public void Assembly_ShouldDeclareNoUnsealedPublicRecord()
    {
        // A non-sealed record keeps a protected copy constructor any assembly can call, which would
        // reopen a family or let a fact record be derived from.
        var records = Orchestration.GetExportedTypes()
            .Where(type => type.IsClass && type.GetMethod("<Clone>$") is not null)
            .ToList();

        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.True(record.IsSealed, $"{record.FullName} is a public record that is not sealed."));
    }

    [Fact]
    public void Assembly_ShouldDeclareNoPublicNestedTypeOnAnInternalType()
    {
        // A public type nested in an internal one is invisible outside the assembly but reads as
        // surface in review; none exists.
        var hidden = Orchestration.GetTypes()
            .Where(type => type.IsNested && type.IsNestedPublic && !type.DeclaringType!.IsVisible)
            .ToList();

        Assert.Empty(hidden);
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
