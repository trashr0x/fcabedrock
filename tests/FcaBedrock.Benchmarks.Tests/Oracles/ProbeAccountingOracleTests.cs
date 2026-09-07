using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;
using FcaBedrock.Diagnostics;
using FcaBedrock.Discovery;
using FcaBedrock.Sources;

namespace FcaBedrock.Benchmarks.Tests.Oracles;

/// <summary>
/// The probe-boundary benchmarks are only a test of the <b>strictly greater</b> rule if the number
/// they sit on is the number the guards actually count. That number comes from a model written here
/// rather than from the prober, so the model itself has to be checked — against a real probe, at the
/// exact threshold, from both sides.
/// <para>
/// This is the test that would catch a disagreement between the accounting D-110 specifies and the
/// accounting this suite assumes. If it ever fails, one of the two is wrong and the failure says
/// which numbers to compare.
/// </para>
/// </summary>
public sealed class ProbeAccountingOracleTests
{
    private const long Records = 500;

    private static async Task<Diagnosed<Spec.Toml.SpecDocument>> ProbeAsync(
        TempDirectory temp, string name, string specText, Action<Stream> write, ProbeOptions options)
    {
        var dataPath = temp.File(name + ".csv");
        var specPath = temp.File(name + ".toml");

        await using (var data = File.Create(dataPath))
        {
            write(data);
        }

        await File.WriteAllTextAsync(specPath, specText, TestContext.Current.CancellationToken);

        var settings = ConversionPipeline.RequireReadSettings(ConversionPipeline.RequireDocument(specText));
        var session = (IWideSourceSession)ConversionPipeline.CreateSession(settings, dataPath);
        return await Prober.ProbeAsync(session, settings, options);
    }

    [Fact]
    public async Task ValueGuard_ShouldBreachOneBelowTheModelledTotalAndPassAtIt()
    {
        using var temp = TempDirectory.Create();
        var retained = ProbeAccountingOracle.W16(Records).Values;

        var atLimit = await ProbeAsync(
            temp, "at", W16Specs.Declared, stream => W16Corpus.Write(stream, Records),
            ProbeOptions.Create(maxTotalRetainedValues: retained));
        var belowLimit = await ProbeAsync(
            temp, "below", W16Specs.Declared, stream => W16Corpus.Write(stream, Records),
            ProbeOptions.Create(maxTotalRetainedValues: retained - 1));

        // Exactly at the limit is not a breach; one more is. An implementation using >= rather
        // than > would fail the first of these and pass the second.
        Assert.True(atLimit.TryGetValue(out _), "a guard set to the exact retained total must not breach.");
        Assert.DoesNotContain(atLimit.Diagnostics, d => d.Code == DiagnosticCode.ProbeLimitExceeded);

        Assert.False(belowLimit.TryGetValue(out _), "a guard one below the retained total must breach.");
        Assert.Contains(belowLimit.Diagnostics, d => d.Code == DiagnosticCode.ProbeLimitExceeded);
    }

    [Fact]
    public async Task TextGuard_ShouldBreachOneBelowTheModelledTotalAndPassAtIt()
    {
        using var temp = TempDirectory.Create();
        var units = ProbeAccountingOracle.LongText(Records).TextUnits;

        var atLimit = await ProbeAsync(
            temp, "at", LongTextSpecs.Declared, stream => LongTextCorpus.Write(stream, Records),
            ProbeOptions.Create(maxTotalRetainedValueText: units));
        var belowLimit = await ProbeAsync(
            temp, "below", LongTextSpecs.Declared, stream => LongTextCorpus.Write(stream, Records),
            ProbeOptions.Create(maxTotalRetainedValueText: units - 1));

        Assert.True(atLimit.TryGetValue(out _), "a text guard set to the exact retained total must not breach.");
        Assert.False(belowLimit.TryGetValue(out _), "a text guard one below the retained total must breach.");
        Assert.Contains(belowLimit.Diagnostics, d => d.Code == DiagnosticCode.ProbeLimitExceeded);
    }

    [Fact]
    public async Task RetentionLimit_ShouldTruncateOneBelowTheLargestDomainAndNotAtIt()
    {
        using var temp = TempDirectory.Create();
        var largest = ProbeAccountingOracle.W16LargestDomain(Records);

        var atLimit = await ProbeAsync(
            temp, "at", W16Specs.Declared, stream => W16Corpus.Write(stream, Records),
            ProbeOptions.Create(valueRetentionLimit: largest));
        var belowLimit = await ProbeAsync(
            temp, "below", W16Specs.Declared, stream => W16Corpus.Write(stream, Records),
            ProbeOptions.Create(valueRetentionLimit: largest - 1));

        Assert.DoesNotContain(atLimit.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
        Assert.Contains(belowLimit.Diagnostics, d => d.Code == DiagnosticCode.ProbeDomainTruncated);
    }

    [Fact]
    public void Accounting_ShouldCountAValueOncePerRetainingAttribute()
    {
        // No cross-attribute deduplication: a value shared by two columns is two retained values,
        // and its text is counted twice. Modelling it any other way would model a different guard.
        var (values, text) = ProbeAccountingOracle.Accumulate(
            records: 4,
            columns: 2,
            cleanedValue: (_, _) => "shared");

        Assert.Equal(2, values);
        Assert.Equal("shared".Length * 2, text);
    }

    [Fact]
    public void Accounting_ShouldNotRetainMissingValues()
    {
        var (values, text) = ProbeAccountingOracle.Accumulate(
            records: 10,
            columns: 1,
            cleanedValue: (row, _) => row % 2 == 0 ? "x" : null);

        Assert.Equal(1, values);
        Assert.Equal(1, text);
    }
}
