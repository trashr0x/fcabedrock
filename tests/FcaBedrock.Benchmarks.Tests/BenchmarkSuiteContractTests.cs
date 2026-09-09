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
    public void EveryExternalCase_ShouldBeExcludedFromEveryImplicitSelectionAndReachableWhenNamed()
    {
        // The External tier is opt-in for a different reason from Scale - its corpus is acquired
        // from a third-party host rather than generated here - but the selection guarantee is the
        // same one: nothing but naming the category reaches it.
        var bare = SelectionPolicy.FromArguments([]);
        var broad = SelectionPolicy.FromArguments(["--filter", "*"]);
        var byName = SelectionPolicy.FromArguments(["--filter", "*Adult*"]);
        var bySurface = SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Convert]);
        var opted = SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.External]);

        var externalCases = BenchmarkTypes.Where(type => Categories(type)
            .Contains(BenchmarkCategories.External, StringComparer.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(externalCases);
        foreach (var type in externalCases)
        {
            var categories = Categories(type);
            Assert.False(bare.Includes(categories), $"{type.Name} runs without opting in.");
            Assert.False(broad.Includes(categories), $"{type.Name} is reachable by a broad name filter.");
            Assert.False(byName.Includes(categories), $"{type.Name} is reachable by its own name.");
            Assert.False(bySurface.Includes(categories), $"{type.Name} is reachable by a surface category.");
            Assert.True(opted.Includes(categories), $"{type.Name} is unreachable even when opted in.");
        }
    }

    [Fact]
    public void EveryWorkingCase_ShouldBeExcludedFromEveryImplicitSelectionAndReachableWhenNamed()
    {
        // The Working tier's own sweep, over the categories BenchmarkDotNet will actually read.
        // The reason it is opt-in is cost — 730,000 records per case — and the surface-category
        // shape is the one that matters here: `--anyCategories Source` is an ordinary way to ask
        // "every source-drain case", and it must mean the Small ones.
        var bare = SelectionPolicy.FromArguments([]);
        var broad = SelectionPolicy.FromArguments(["--filter", "*"]);
        var byName = SelectionPolicy.FromArguments(["--filter", "*Working*"]);
        var bySurface = SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Source]);
        var opted = SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Working]);

        var workingCases = BenchmarkTypes.Where(type => Categories(type)
            .Contains(BenchmarkCategories.Working, StringComparer.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(workingCases);
        foreach (var type in workingCases)
        {
            var categories = Categories(type);
            Assert.False(bare.Includes(categories), $"{type.Name} runs without opting in.");
            Assert.False(broad.Includes(categories), $"{type.Name} is reachable by a broad name filter.");
            Assert.False(byName.Includes(categories), $"{type.Name} is reachable by its own name.");
            Assert.False(bySurface.Includes(categories), $"{type.Name} is reachable by a surface category.");
            Assert.True(opted.Includes(categories), $"{type.Name} is unreachable even when opted in.");
        }
    }

    [Fact]
    public void TheSelectionFilter_ShouldRefuseADiscoveredWorkingCaseUnderASurfaceOnlySelection()
    {
        // End to end through the real path: BenchmarkDotNet's own converter reads the attributes,
        // and the suite's real filter — the one the configuration attaches — decides on the
        // descriptor those attributes produced rather than on a category array written here.
        var discovered = BenchmarkConverter.TypeToBenchmarks(typeof(CliHostConvertWideWorking)).BenchmarksCases;
        Assert.NotEmpty(discovered);

        var surfaceOnly = new TierSelectionFilter(
            SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Convert]));
        var opted = new TierSelectionFilter(
            SelectionPolicy.FromArguments(["--anyCategories", BenchmarkCategories.Working]));

        Assert.All(discovered, benchmark =>
        {
            Assert.Contains(
                BenchmarkCategories.Working, benchmark.Descriptor.Categories, StringComparer.OrdinalIgnoreCase);
            Assert.False(surfaceOnly.Predicate(benchmark), $"{benchmark.Descriptor.WorkloadMethod.Name} was admitted.");
            Assert.True(opted.Predicate(benchmark), $"{benchmark.Descriptor.WorkloadMethod.Name} was refused.");
        });
    }

    [Fact]
    public void TheAcquiredCorpusCases_ShouldBeExactlyTheExternalOnes()
    {
        // Read off the attributes BenchmarkDotNet will actually see, not off a category array
        // written here: a case whose corpus is acquired but whose category still said Small would
        // put a third-party download back into the default selection, and this is the check that
        // notices.
        var acquired = BenchmarkTypes
            .Where(type => type.GetCustomAttribute<BenchmarkCorpusAttribute>()?.Tier == CorpusTier.External)
            .ToList();
        var external = BenchmarkTypes
            .Where(type => Categories(type).Contains(BenchmarkCategories.External, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(acquired);
        Assert.Equal(acquired, external);
        Assert.All(acquired, type =>
            Assert.DoesNotContain(BenchmarkCategories.Small, Categories(type), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void BenchmarkDotNet_ShouldDiscoverTheAcquiredCasesUnderTheExternalCategory()
    {
        // The end-to-end fact: BenchmarkDotNet's own converter reads the attributes, so this is
        // what a real `--anyCategories External` run will match on.
        var discovered = BenchmarkConverter.TypeToBenchmarks(typeof(AdultSourceDrain)).BenchmarksCases;

        Assert.NotEmpty(discovered);
        Assert.All(discovered, benchmark =>
        {
            Assert.Contains(
                BenchmarkCategories.External, benchmark.Descriptor.Categories, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                BenchmarkCategories.Small, benchmark.Descriptor.Categories, StringComparer.OrdinalIgnoreCase);
        });
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
