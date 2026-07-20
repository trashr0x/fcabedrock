using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The <see cref="ProbeOptions"/> contract: the pinned defaults (D-108/D-110), the exact
/// exception taxonomy of its validating factory (P-10), and — the one that would
/// otherwise rot silently — that its locale predicate agrees with the resolve seam's.
/// </summary>
public sealed class ProbeOptionsTests
{
    [Fact]
    public void Default_WhenInspected_ThenCarriesTheSettledDefaults()
    {
        var options = ProbeOptions.Default;

        Assert.Equal(100_000, options.ValueRetentionLimit);
        Assert.Equal(10_000, options.MaxDiscoveredAttributes);
        Assert.Equal(2_000_000L, options.MaxTotalRetainedValues);
        Assert.Equal(50_000_000L, options.MaxTotalRetainedValueText);
        Assert.Equal("invariant", options.Locale);
    }

    [Fact]
    public void Create_WhenCalledWithNoArguments_ThenMatchesDefault()
    {
        // Default must be exactly "Create with every default", so a caller who overrides one
        // option cannot silently get a different baseline for the rest.
        var created = ProbeOptions.Create();
        var fallback = ProbeOptions.Default;

        Assert.Equal(fallback.ValueRetentionLimit, created.ValueRetentionLimit);
        Assert.Equal(fallback.MaxDiscoveredAttributes, created.MaxDiscoveredAttributes);
        Assert.Equal(fallback.MaxTotalRetainedValues, created.MaxTotalRetainedValues);
        Assert.Equal(fallback.MaxTotalRetainedValueText, created.MaxTotalRetainedValueText);
        Assert.Equal(fallback.Locale, created.Locale);
    }

    [Fact]
    public void Default_WhenReadTwice_ThenIsTheSameInstance() =>
        // Immutability's practical face: there is nothing to copy, so a shared instance is safe.
        Assert.Same(ProbeOptions.Default, ProbeOptions.Default);

    [Fact]
    public void ProbeOptions_WhenInspected_ThenEveryPropertyIsGetOnly() =>
        Assert.All(
            typeof(ProbeOptions).GetProperties(),
            property => Assert.Null(property.SetMethod));

    [Fact]
    public void Create_WhenValueRetentionLimitBelowOne_ThenThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeOptions.Create(valueRetentionLimit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeOptions.Create(valueRetentionLimit: -1));
    }

    [Fact]
    public void Create_WhenAnyGuardBelowOne_ThenThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeOptions.Create(maxDiscoveredAttributes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeOptions.Create(maxTotalRetainedValues: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProbeOptions.Create(maxTotalRetainedValueText: 0));
    }

    [Fact]
    public void Create_WhenEveryLimitIsExactlyOne_ThenSucceeds()
    {
        // One is the smallest coherent value for each: a probe that retains one value per
        // attribute, one value in total, and one code unit of text is degenerate but well
        // defined. The boundary is inclusive.
        var options = ProbeOptions.Create(1, 1, 1L, 1L);

        Assert.Equal(1, options.ValueRetentionLimit);
        Assert.Equal(1, options.MaxDiscoveredAttributes);
        Assert.Equal(1L, options.MaxTotalRetainedValues);
        Assert.Equal(1L, options.MaxTotalRetainedValueText);
    }

    [Fact]
    public void Create_WhenLimitsAreMutuallyAbsurd_ThenStillSucceeds() =>
        // No cross-limit validation: a per-attribute limit far above the aggregate
        // guards is legal — the guards simply bite first. Rejecting it would invent a rule the
        // decisions do not have.
        Assert.Equal(
            1_000_000,
            ProbeOptions.Create(valueRetentionLimit: 1_000_000, maxTotalRetainedValues: 1L).ValueRetentionLimit);

    [Fact]
    public void Create_WhenLocaleNull_ThenThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() => ProbeOptions.Create(locale: null!));

    [Fact]
    public void Create_WhenLocaleUnknown_ThenThrowsArgumentException()
    {
        // ArgumentException, NOT ArgumentOutOfRangeException: the taxonomy is deliberate, and
        // ArgumentOutOfRangeException derives from ArgumentException, so assert the exact type.
        var ex = Assert.Throws<ArgumentException>(() => ProbeOptions.Create(locale: "not-a-locale"));
        Assert.Equal(typeof(ArgumentException), ex.GetType());
        Assert.Equal("locale", ex.ParamName);
    }

    [Fact]
    public void Create_WhenLocaleIsInvariantInAnyCasing_ThenAcceptedAndRetainedVerbatim() =>
        // Accepted case-insensitively (matching the resolve seam) but stored as written: the
        // draft authors what the caller asked for rather than silently rewriting it.
        Assert.Equal("INVARIANT", ProbeOptions.Create(locale: "INVARIANT").Locale);

    // The locales this suite pins agreement over: the invariant spellings, real predefined
    // cultures, and shapes that must be rejected (a well-formed but unknown tag is the case
    // predefinedOnly exists for — under ICU, GetCultureInfo would otherwise synthesize it).
    public static TheoryData<string> LocaleCases() =>
    [
        "invariant", "INVARIANT", "Invariant", "", "en-US", "fr-FR", "de-DE", "ja-JP", "en",
        "xx-YY", "zz", "not-a-locale", "en_US", "  ", "en-US-", "0",
    ];

    [Theory]
    [MemberData(nameof(LocaleCases))]
    public void Create_WhenGivenALocale_ThenAcceptsExactlyWhatTheResolveSeamAccepts(string locale)
    {
        // The reason this test exists: ProbeOptions duplicates SpecResolver's locale predicate
        // rather than sharing a public helper, so nothing structural stops the two
        // from drifting. If they drifted, probe could return a draft whose own `binding.locale`
        // fails to resolve — breaking the D-107 guarantee on a field the caller chose. This
        // makes that drift a test failure instead of a runtime surprise.
        var probeAccepts = true;
        try
        {
            ProbeOptions.Create(locale: locale);
        }
        catch (ArgumentException)
        {
            probeAccepts = false;
        }

        Assert.Equal(ResolverAccepts(locale), probeAccepts);
    }

    [Fact]
    public async Task Create_WhenGivenAPredefinedLocale_ThenTheDraftItAuthorsStillResolves()
    {
        // The parity test's consequence, end to end: an accepted locale must survive
        // probe -> canonical TOML -> strict reread -> resolve with no locale diagnostic.
        var result = await ProbeFixtures.ProbeCsvAsync("a\nx\n", options: ProbeOptions.Create(locale: "fr-FR"));
        var toml = ProbeFixtures.Toml(result);

        Assert.Contains("locale = \"fr-FR\"", toml, StringComparison.Ordinal);

        var reread = SpecReader.Read(toml);
        Assert.True(reread.TryGetValue(out var document), ProbeFixtures.Describe(reread.Diagnostics));
        var resolved = SpecResolver.Resolve(document, new SourceSchema(1, ["a"]));
        Assert.DoesNotContain(resolved.Diagnostics, d => d.Code == DiagnosticCode.BindingLocaleInvalid);
    }

    // Drives the real resolve seam over a minimal wide document whose only variable is the
    // locale, and reports solely on the locale verdict.
    private static bool ResolverAccepts(string locale)
    {
        var document = new SpecDocument(
            new SpecSection(1, null, null, null, null, null),
            null,
            new BindingSection(
                SourceShape.Wide, null, null, null, null, locale, null, null, null, null),
            null, null, [], [],
            [
                new AttributeSection(
                    "a", new ColumnSourceSection(0, null, SourceValueType.String), null, null, null,
                    new IdentityDiscretizerSection(), new NominalScaleSection(),
                    ["x"], null, null, null, null),
            ]);

        return !SpecResolver.Resolve(document, new SourceSchema(1))
            .Diagnostics.Any(d => d.Code == DiagnosticCode.BindingLocaleInvalid);
    }
}
