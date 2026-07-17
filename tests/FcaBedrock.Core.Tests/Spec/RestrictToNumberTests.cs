using System.Reflection;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

/// <summary>
/// The exact numeric restrict entry (§10.4/D-091/D-105). It means <b>parsed numeric identity</b> —
/// not string-spelling equality, and not a single-point range — and it is deliberately a
/// positional carrier able to hold non-finite authored state long enough for the resolve seam to
/// diagnose it on the user-facing channel (P-14).
/// </summary>
public sealed class RestrictToNumberTests
{
    [Fact]
    public void RestrictToNumber_WhenInspected_ThenIsAPublicSealedRestrictToEntryInCoreSpec()
    {
        var type = typeof(RestrictToNumber);

        Assert.Equal("FcaBedrock.Core.Spec", type.Namespace);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Equal(typeof(RestrictToEntry), type.BaseType);
    }

    [Fact]
    public void RestrictToNumber_WhenInspected_ThenCarriesOneReadOnlyDoubleValue()
    {
        var value = typeof(RestrictToNumber).GetProperty(nameof(RestrictToNumber.Value));

        Assert.NotNull(value);
        Assert.Equal(typeof(double), value!.PropertyType);
        Assert.True(value.CanRead);

        // Positional record ⇒ init-only, never settable after construction.
        Assert.False(value.CanWrite && value.SetMethod!.ReturnParameter
            .GetRequiredCustomModifiers()
            .All(m => m != typeof(System.Runtime.CompilerServices.IsExternalInit)));
    }

    [Fact]
    public void RestrictToNumber_WhenValuesAreEqual_ThenEntriesAreEqual()
    {
        // Value equality (record semantics) is what makes the fingerprint's canonical dedup and
        // the plan's entry comparisons mean the same thing.
        Assert.Equal(new RestrictToNumber(30.0), new RestrictToNumber(30.0));
        Assert.NotEqual(new RestrictToNumber(30.0), new RestrictToNumber(30.5));
    }

    [Fact]
    public void RestrictToNumber_WhenBuiltFromDifferentSpellings_ThenTheyAreTheSameEntry()
    {
        // §10.4/D-091: "30", "30.0", and "3e1" are one numeric identity — the reader parses each
        // to the same double, so the carrier cannot tell them apart. This is the difference
        // between numeric identity and string-spelling equality, asserted at the carrier itself.
        // (The literals are written as distinct source spellings on purpose.)
        Assert.Equal(new RestrictToNumber(30), new RestrictToNumber(30.0));
        Assert.Equal(new RestrictToNumber(30), new RestrictToNumber(3e1));
    }

    [Fact]
    public void RestrictToNumber_WhenConstructedWithNonFinite_ThenItDoesNotThrow()
    {
        // Deliberate (D-091, round-6 High-2): the Spec document model reuses this union (D-057),
        // so the carrier MUST be able to hold an authored `{ value = nan }` long enough for the
        // resolve seam to report RestrictToRangeInvalid on the diagnostic channel. A throwing
        // factory would turn an authoring error into a parse-time exception — the wrong channel
        // (P-14). The boundary is layered instead: ResolvedSpec.Create and the calibrated-state
        // factories throw for anything non-finite that survives past the seam.
        Assert.Equal(double.NaN, new RestrictToNumber(double.NaN).Value);
        Assert.Equal(double.PositiveInfinity, new RestrictToNumber(double.PositiveInfinity).Value);
    }

    [Fact]
    public void RestrictToNumber_WhenInspected_ThenExposesNoMatchingOrCultureSurface()
    {
        // P-3/P-6: the carrier is data. Matching lives in one place (the emitter's shared
        // restriction filter), so no Matches/Culture/Tolerance member may appear here — a second
        // matching entry point is exactly how wide and triple semantics would drift.
        var declared = typeof(RestrictToNumber)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain("Matches", declared);
        Assert.DoesNotContain("Culture", declared);
        Assert.DoesNotContain("Tolerance", declared);
        Assert.DoesNotContain("Epsilon", declared);
        Assert.DoesNotContain("Create", declared);
    }

    [Fact]
    public void RestrictToEntry_WhenInspected_ThenHasExactlyTheThreeRecognizedVariants()
    {
        // §10.4/D-105: the M4 execution union is exactly {value-string, exact-number, range}.
        // Anything else is rejected at Core's trust boundary rather than silently ignored.
        var variants = typeof(RestrictToEntry).Assembly
            .GetExportedTypes()
            .Where(t => t.IsSubclassOf(typeof(RestrictToEntry)))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["RestrictToNumber", "RestrictToRange", "RestrictToValue"], variants);
    }
}
