using System.Reflection;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The P-4 lock on Discovery's public surface. This slice ships the <b>wide</b> probe vertical
/// and nothing else, so the inventory is deliberately tiny — and
/// <c>ProbeTripleAsync</c> is <b>absent</b>, not stubbed or rejected: an absent method is an
/// honest "not yet", whereas a stub that throws would be surface a caller could bind to and a
/// transitional diagnostic nobody wants.
/// <para>
/// Asserted as an exact inventory rather than "contains": a spot check would let an
/// accidentally-public engine, observer, tally, or draft type slip out, and a public type is far
/// harder to withdraw than to withhold.
/// </para>
/// </summary>
public sealed class PublicSurfaceTests
{
    private static readonly Assembly Discovery = typeof(Prober).Assembly;

    [Fact]
    public void Discovery_WhenInspected_ThenExportsExactlyProberAndProbeOptions()
    {
        var exported = Discovery.GetExportedTypes().Select(t => t.FullName).Order(StringComparer.Ordinal);

        Assert.Equal(["FcaBedrock.Discovery.ProbeOptions", "FcaBedrock.Discovery.Prober"], exported);
    }

    [Fact]
    public void Prober_WhenInspected_ThenExposesOnlyProbeAsync()
    {
        var methods = typeof(Prober)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["ProbeAsync"], methods);
    }

    [Fact]
    public void Prober_WhenInspected_ThenProbeTripleAsyncIsAbsent() =>
        // Slice D's surface, stated as an absence so an early landing fails here rather than
        // shipping a half-milestone API.
        Assert.Null(typeof(Prober).GetMethod("ProbeTripleAsync"));

    [Fact]
    public void ProbeAsync_WhenInspected_ThenHasTheSettledSignature()
    {
        var method = typeof(Prober).GetMethod("ProbeAsync")!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(ValueTask<Diagnosed<SpecDocument>>), method.ReturnType);
        Assert.Equal(
            [typeof(IWideSourceSession), typeof(SourceReadSettings), typeof(ProbeOptions), typeof(CancellationToken)],
            parameters.Select(p => p.ParameterType));
        Assert.Equal(["session", "readSettings", "options", "cancellationToken"], parameters.Select(p => p.Name));

        // The two trailing parameters are optional; the first two are not. A caller must state
        // the source and the settings the draft will author — neither has a sane default.
        Assert.False(parameters[0].IsOptional);
        Assert.False(parameters[1].IsOptional);
        Assert.True(parameters[2].IsOptional);
        Assert.True(parameters[3].IsOptional);
        Assert.Null(parameters[2].DefaultValue);
    }

    [Fact]
    public void ProbeOptions_WhenInspected_ThenExposesOnlyTheSettledMembers()
    {
        var members = typeof(ProbeOptions)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m is not MethodInfo { IsSpecialName: true })
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "Create", "Default", "Locale", "MaxDiscoveredAttributes", "MaxTotalRetainedValueText",
                "MaxTotalRetainedValues", "ValueRetentionLimit",
            ],
            members);
    }

    [Fact]
    public void ProbeOptions_WhenInspected_ThenHasNoPublicConstructor() =>
        // P-10: construction goes through the validating factory, so an unvalidated locale or a
        // zero limit is unrepresentable rather than merely rejected later.
        Assert.Empty(typeof(ProbeOptions).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

    [Fact]
    public void Discovery_WhenInspected_ThenReferencesNoForbiddenPackage()
    {
        // The architecture suite owns the type-level rule; this is the assembly-reference face
        // of it, local to the package's own test project so a bad reference fails here too.
        var referenced = Discovery.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("FcaBedrock.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.DoesNotContain("FcaBedrock.Conversion", referenced);
        Assert.DoesNotContain("FcaBedrock.Export", referenced);
    }
}
