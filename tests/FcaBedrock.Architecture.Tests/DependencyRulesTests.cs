using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using FcaBedrock.Core.Planning;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
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
        // The documented invariant (CLAUDE.md, D-039) is that the PACKAGES — the production
        // assemblies — form no cycles; Core's sub-namespaces (Core.Spec, Core.Discretization,
        // Core.Fingerprinting, …) are organizational, not independent packages, so intra-Core
        // edges (e.g. FreePerValueDiscretizer → CanonicalNumber / SourceValueType, both inside
        // FcaBedrock.Core) are not package cycles. Slices().Matching("FcaBedrock.(*)") sliced by
        // full namespace, accidentally treating those sub-namespaces as separate packages — a
        // latent semantic bug corrected here (M4 Slice B / D-101): assign every type to its
        // production assembly, so each production assembly is exactly one slice.
        var packages = ProductionPackageSlices();

        // Non-vacuity: prove the slicing produced exactly one slice per loaded production package.
        var expected = Production
            .Select(assembly => assembly.GetName().Name)
            .OfType<string>()
            .Order(StringComparer.Ordinal);
        var actual = packages.GetObjects(Architecture)
            .Select(slice => slice.Description)
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, actual);

        packages.Should().BeFreeOfCycles().Check(Architecture);
    }

    // One ArchUnitNET slice per production assembly (keyed by assembly name), so BeFreeOfCycles
    // enforces the package-level acyclicity CLAUDE.md/D-039 document; non-production types (BCL,
    // dependencies) are ignored.
    private static ArchUnitNET.Fluent.Slices.GivenSlices ProductionPackageSlices()
    {
        var productionNames = Production
            .Select(assembly => assembly.GetName().Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var creator = new ArchUnitNET.Fluent.Slices.SliceRuleCreator();
        creator.SetSliceAssignment(new ArchUnitNET.Fluent.Slices.SliceAssignment(
            type => productionNames.Contains(type.Assembly.Name)
                ? ArchUnitNET.Domain.SliceIdentifier.Of(type.Assembly.Name)
                : ArchUnitNET.Domain.SliceIdentifier.Ignore(),
            "assigned by production assembly"));
        return new ArchUnitNET.Fluent.Slices.GivenSlices(creator);
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
    public void Discovery_ShouldOnlyDependOnSourcesSpecCoreAndDiagnostics()
    {
        // D-109 pins Discovery's allowed reference set to {Sources, Spec, Core, Diagnostics}. The
        // two it must NOT reach are the ones it would be most tempting to: Conversion (whose
        // private domain observer has exactly the semantics probe needs — so probe re-implements
        // them and a cross-check test pins the two equal) and Export (the caller owns all output).
        var forbidden = ProductionExcept(
            "FcaBedrock.Discovery", "FcaBedrock.Sources", "FcaBedrock.Spec",
            "FcaBedrock.Core", "FcaBedrock.Diagnostics");

        // Non-vacuity, in both directions: the rule must have real subjects AND real targets.
        // Forgetting the csproj ProjectReference would silently exempt Discovery from this rule
        // and from the cycle check, which is precisely the failure a green vacuous test hides.
        Assert.NotEmpty(Discovery());
        Assert.NotEmpty(forbidden);

        Types().That().ResideInAssembly(Asm("FcaBedrock.Discovery"))
            .Should().NotDependOnAny(Types().That().ResideInAssembly(forbidden[0], forbidden[1..]))
            .Check(Architecture);
    }

    [Fact]
    public void Discovery_ShouldNotDependOnSystemIoBeyondTheTwoClassificationExceptions()
    {
        // Discovery performs no I/O - it classifies failures crossing the source-session seam
        // (M5-IP-008); D-109's ban on opening paths/streams/files remains absolute.
        //
        // So the allowlist is exactly two EXCEPTION TYPES, named in catch clauses. Everything
        // else in System.IO — Stream, File, Path, Directory, readers/writers, pipelines,
        // compression — stays forbidden, because the caller and the session own I/O. Core's
        // blanket System.IO ban above is unchanged and stricter.
        //
        // ArchUnitNET does not reliably surface catch-handler metadata, so a green result here
        // is necessary but not sufficient: ProbeReadFailureTests drives every admitted family
        // AND counterexamples that must NOT be absorbed, which is what actually proves the
        // filter is narrow.
        Assert.NotEmpty(Discovery());

        // NotDependOnAnyTypesThat (not NotDependOnAny) is load-bearing here: the latter
        // intersects with types MODELLED in the architecture, and the BCL is not loaded, so it
        // would pass vacuously against any System.IO use whatsoever. This form filters the
        // subject's actual dependency targets, which is what Core's rule above does. The
        // allowlist rides in the predicate because `.And()` after a target filter starts a new
        // rule rather than narrowing the target set.
        Types().That().ResideInAssembly(Asm("FcaBedrock.Discovery"))
            .Should().NotDependOnAnyTypesThat().FollowCustomPredicate(
                type => type.FullName.StartsWith("System.IO.", StringComparison.Ordinal)
                    && type.FullName is not ("System.IO.IOException" or "System.IO.InvalidDataException"),
                "reside in System.IO other than the two classification-only exception types")
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

    [Fact]
    public void Core_ShouldRemainTemplateAndMatcherFree()
    {
        // D-118/D-121: template ids, matcher selectors, compiled regexes, ranges, and
        // precedence syntax all die at the resolve seam. Core receives only effective
        // per-attribute configuration, which is exactly why an equivalent flat,
        // materialized, matcher-driven, or extends-composed spec produces the identical
        // Core graph — and therefore the identical plan, fingerprints, and bytes.
        //
        // A NAME-based rule rather than a reference-based one, deliberately:
        // Core_ShouldOnlyDependOnDiagnostics already proves Core cannot reference Spec at
        // all, so the reference rules would stay green while someone re-implemented a
        // TemplateTable or MatcherEvaluation INSIDE Core. That is the drift D-118 makes
        // permanent, and a type name is the observable signal for it.
        // Non-vacuity in both directions. A "no type matches" rule is worthless if the
        // predicate matches nothing anywhere, so: Core must have real types to inspect,
        // AND the same predicate must genuinely fire on the assembly that legitimately
        // owns this vocabulary — Spec, where the sections and the application internals
        // live. Without the second assertion a typo in the predicate would pass forever.
        Assert.NotEmpty(Asm("FcaBedrock.Core").GetTypes());
        Assert.Contains(Asm("FcaBedrock.Spec").GetTypes(), IsTemplateOrMatcherNamed);

        Types().That().ResideInAssembly(Asm("FcaBedrock.Core"))
            .And().FollowCustomPredicate(
                type => IsTemplateOrMatcherNamed(type.Name),
                "is named for a template or a matcher")
            .Should().NotExist()
            .Check(Architecture);
    }

    private static bool IsTemplateOrMatcherNamed(System.Type type) => IsTemplateOrMatcherNamed(type.Name);

    private static bool IsTemplateOrMatcherNamed(string name) =>
        name.Contains("Template", StringComparison.Ordinal) || name.Contains("Matcher", StringComparison.Ordinal);

    // The Discovery types the two rules above are asserted over; empty would mean the package is
    // absent from this project's output (a missing ProjectReference), not that it is clean.
    private static IReadOnlyList<System.Type> Discovery() =>
        [.. Asm("FcaBedrock.Discovery").GetTypes()];

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
