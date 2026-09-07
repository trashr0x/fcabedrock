using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Tests;

/// <summary>
/// Structural facts about the suite that a run cannot show you.
/// <para>
/// A benchmark case with no tier category would be silently unreachable under the default
/// selection; one whose declared tier disagrees with its category would publish a false
/// denominator; and a benchmark project that had drifted into being a test project would make
/// <c>dotnet test</c> start running multi-minute benchmarks. None of those announce themselves.
/// </para>
/// </summary>
public sealed class BenchmarkSuiteContractTests
{
    private static readonly Assembly Suite = typeof(Program).Assembly;

    private static IReadOnlyList<Type> BenchmarkTypes { get; } =
        [.. Suite.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(type => type.GetMethods().Any(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    [Fact]
    public void Suite_ShouldDeclareAtLeastOneRunnableBenchmark()
    {
        Assert.NotEmpty(BenchmarkTypes);
    }

    [Fact]
    public void EveryBenchmarkClass_ShouldCarryExactlyOneTierCategory()
    {
        foreach (var type in BenchmarkTypes)
        {
            var tiers = Categories(type).Intersect(BenchmarkCategories.Tiers, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.True(
                tiers.Count == 1,
                $"{type.Name} carries {tiers.Count} tier categories ({string.Join(", ", tiers)}); "
                + "a case with none is unreachable by default, and a case with two is ambiguous.");
        }
    }

    [Fact]
    public void EveryBenchmarkClass_ShouldCarryASurfaceCategory()
    {
        foreach (var type in BenchmarkTypes)
        {
            var surfaces = Categories(type)
                .Except(BenchmarkCategories.Tiers, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.True(surfaces.Count > 0, $"{type.Name} names no production surface.");
        }
    }

    [Fact]
    public void EveryDeclaredCorpus_ShouldAgreeWithItsTierCategoryAndExistInTheRegistry()
    {
        // A false denominator is worse than an absent one, so the declared corpus and the category
        // that selects it must be the same claim - and the corpus it names must actually exist,
        // or the report would print "n/a" for a case that does read data.
        foreach (var type in BenchmarkTypes)
        {
            if (type.GetCustomAttribute<BenchmarkCorpusAttribute>() is not { } declared)
            {
                continue;
            }

            Assert.Contains(
                CorpusTiers.Category(declared.Tier), Categories(type), StringComparer.OrdinalIgnoreCase);
            Assert.NotNull(declared.Resolve());
        }
    }

    [Fact]
    public void EveryScaleCase_ShouldBeExcludedFromTheDefaultSelectionAndReachableWithIt()
    {
        var bare = SelectionPolicy.FromArguments([]);
        var opted = SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Scale]);

        var scaleCases = BenchmarkTypes.Where(type => Categories(type)
            .Contains(BenchmarkCategories.Scale, StringComparer.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(scaleCases);
        foreach (var type in scaleCases)
        {
            Assert.False(bare.Includes(Categories(type)), $"{type.Name} is reachable without opting in.");
            Assert.True(opted.Includes(Categories(type)), $"{type.Name} is unreachable even when opted in.");
        }
    }

    [Fact]
    public void EveryBenchmarkClass_ShouldBeUnsealedSoTheOutOfProcessToolchainCanGenerateAgainstIt()
    {
        // BenchmarkDotNet's default toolchain derives from the benchmark type; a sealed class fails
        // validation at run time, which is a slow way to learn a compile-time fact.
        foreach (var type in BenchmarkTypes)
        {
            Assert.False(type.IsSealed, $"{type.Name} is sealed.");
        }
    }

    [Fact]
    public void Suite_ShouldNotReferenceATestFramework()
    {
        // The benchmark host opts out of the shared test props precisely so `dotnet test` can never
        // run it (P-20). A test-framework reference appearing here would mean that opt-out lapsed.
        var frameworks = Suite.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Microsoft.Testing", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Microsoft.NET.Test", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(frameworks);
    }

    [Fact]
    public void Suite_ShouldBeDiscoverableByBenchmarkDotNet()
    {
        // The end-to-end fact the structural checks above are protecting: BenchmarkDotNet's own
        // discovery finds every case, so a filter can reach them.
        var discovered = BenchmarkConverter.TypeToBenchmarks(typeof(WideSourceDrainSmall));

        Assert.NotEmpty(discovered.BenchmarksCases);
    }

    private static IReadOnlyList<string> Categories(Type type) =>
        [.. type.GetCustomAttributes<BenchmarkCategoryAttribute>().SelectMany(attribute => attribute.Categories)];
}
