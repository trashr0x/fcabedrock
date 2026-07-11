using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using FcaBedrock.Core.Planning;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static ArchUnitNET.Fluent.Slices.SliceRuleDefinition;
using ArchModel = ArchUnitNET.Domain.Architecture;
using Assembly = System.Reflection.Assembly;

namespace FcaBedrock.Architecture.Tests;

// Executable encoding of the CLAUDE.md dependency rule, via ArchUnitNET:
// Diagnostics is a leaf, Core depends only on Diagnostics, and the packages form
// no cycles. Rules are expressed against the loaded production assemblies (exact,
// and correct-by-construction as packages are added). ArchUnitNET reads compiled
// type-level metadata, so these become load-bearing the moment M1 gives the
// (currently empty) assemblies real types and references; until then the
// dependency/cycle rules are vacuously satisfied, and
// Architecture_ShouldIncludeProductionAssemblies is the non-vacuous M0 check that
// the suite really inspected the production assemblies rather than nothing.
public sealed class DependencyRulesTests
{
    private static readonly Assembly[] Production = LoadProductionAssemblies();

    private static readonly ArchModel Architecture =
        new ArchLoader().LoadAssemblies(Production).Build();

    [Fact]
    public void Architecture_ShouldIncludeProductionAssemblies()
    {
        var names = Production.Select(a => a.GetName().Name).ToList();

        Assert.Contains("FcaBedrock.Core", names);
        Assert.Contains("FcaBedrock.Diagnostics", names);
    }

    [Fact]
    public void Diagnostics_ShouldNotDependOnOtherPackages()
    {
        var others = ProductionExcept("FcaBedrock.Diagnostics");
        Assert.NotEmpty(others); // at least FcaBedrock.Core is present

        Types().That().ResideInAssembly(Asm("FcaBedrock.Diagnostics"))
            .Should().NotDependOnAny(Types().That().ResideInAssembly(others[0], others[1..]))
            .WithoutRequiringPositiveResults() // tolerate empty subject on M0's typeless assemblies
            .Check(Architecture);
    }

    [Fact]
    public void Core_ShouldOnlyDependOnDiagnostics()
    {
        var forbidden = ProductionExcept("FcaBedrock.Core", "FcaBedrock.Diagnostics");
        if (forbidden.Length == 0)
        {
            return; // M0: no other packages exist yet; this rule gains teeth at M1
        }

        Types().That().ResideInAssembly(Asm("FcaBedrock.Core"))
            .Should().NotDependOnAny(Types().That().ResideInAssembly(forbidden[0], forbidden[1..]))
            .WithoutRequiringPositiveResults() // tolerate empty subject on M0's typeless assemblies
            .Check(Architecture);
    }

    [Fact]
    public void Packages_ShouldBeFreeOfCycles()
    {
        Slices().Matching("FcaBedrock.(*)").Should().BeFreeOfCycles().Check(Architecture);
    }

    [Fact]
    public void Core_ShouldNotDependOnSystemIo()
    {
        // P-13: Core is pure — no file/stream I/O. ArchUnitNET sees type-level
        // dependencies (incl. BCL targets by namespace) that the package-reference
        // rules cannot; this is the purity guard D-039 anticipated for M1. Core now
        // has real types, so this is non-vacuous (no WithoutRequiringPositiveResults).
        Types().That().ResideInAssembly(Asm("FcaBedrock.Core"))
            .Should().NotDependOnAnyTypesThat().ResideInNamespace("System.IO")
            .Check(Architecture);
    }

    [Fact]
    public void SourceExecutionHierarchy_ShouldResideInCore()
    {
        // D-082: the shape-specific execution hierarchy (SourceExecution + variants) is Core-only
        // value; it must not leak into Sources/Conversion/Export, which reference Core, not the reverse.
        Classes().That().AreAssignableTo(typeof(SourceExecution))
            .Should().ResideInAssembly(Asm("FcaBedrock.Core"))
            .Check(Architecture);
    }

    private static Assembly Asm(string simpleName) =>
        Production.Single(a => a.GetName().Name == simpleName);

    private static Assembly[] ProductionExcept(params string[] simpleNames) =>
        [.. Production.Where(a => !simpleNames.Contains(a.GetName().Name))];

    private static Assembly[] LoadProductionAssemblies()
    {
        var result = new List<Assembly>();
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "FcaBedrock.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(Assembly.LoadFrom(path));
        }

        return [.. result];
    }
}
