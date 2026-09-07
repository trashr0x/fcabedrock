using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The registry is the single place a family's tiers, layouts, and specs are named, so the
/// <c>prepare</c> verb, the benchmarks, and the report's denominator columns cannot disagree about
/// what exists. These tests hold that agreement, and the one thing it has to get right that a
/// generated corpus never raises: an <b>externally acquired</b> case, whose record count is measured
/// rather than declared.
/// </summary>
public sealed class CorpusRegistryTests
{
    [Fact]
    public void EveryCase_ShouldHaveAUniqueIdAndANonEmptySpec()
    {
        var ids = CorpusCases.All.Select(corpus => corpus.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(CorpusCases.All, corpus => Assert.NotEmpty(corpus.SpecText));
        Assert.All(CorpusCases.All, corpus => Assert.True(corpus.Columns > 0));
    }

    [Fact]
    public void EveryCase_ShouldBeFindableByItsOwnId()
    {
        foreach (var corpus in CorpusCases.All)
        {
            Assert.Equal(corpus.Id, CorpusCases.Find(corpus.Id)?.Id);
        }

        Assert.Null(CorpusCases.Find("no-such-case"));
    }

    [Fact]
    public void ForTier_ShouldPartitionTheRegistry()
    {
        // Every case belongs to exactly one tier, so preparing every tier prepares everything once.
        var byTier = CorpusTiers.All.SelectMany(CorpusCases.ForTier).ToList();

        Assert.Equal(CorpusCases.All.Count, byTier.Count);
        Assert.Equal(
            CorpusCases.All.Select(corpus => corpus.Id).Order(StringComparer.Ordinal),
            byTier.Select(corpus => corpus.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AGeneratedCase_ShouldDeclareItsRecordCountAndCarryItsTierInItsId()
    {
        var w16 = CorpusCases.W16(CorpusTier.Working);

        Assert.True(w16.RecordsDeclared);
        Assert.Equal(730_000, w16.Records);
        Assert.Equal("w16-working", w16.Id);
        Assert.Equal("working", CorpusPreparer.PrepareToken(w16));
    }

    [Fact]
    public void AVariantCase_ShouldCarryItsVariantAndTier()
    {
        Assert.Equal(
            "t10-unordered-scale7m",
            CorpusCases.T10(CorpusTier.Scale7M, TripleLayout.Interleaved).Id);
    }

    [Fact]
    public void TheExternalCase_ShouldMeasureItsRecordCountRatherThanDeclareOne()
    {
        var adult = CorpusCases.Adult;

        // Its size is a fact about the download, not a choice, so the id carries no tier and the
        // declared count is absent until the corpus has actually been acquired.
        Assert.Equal(CorpusOrigin.External, adult.Origin);
        Assert.False(adult.RecordsDeclared);
        Assert.Equal(0, adult.Records);
        Assert.Equal("adult", adult.Id);
        Assert.NotNull(adult.CountRecords);

        // ...and `prepare adult` is what prepares it: its tier names no record count, so a tier
        // token would be a meaningless instruction.
        Assert.Equal("adult", CorpusPreparer.PrepareToken(adult));
    }

    [Fact]
    public void TheExternalTier_ShouldRefuseToStateARecordCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CorpusTiers.Records(CorpusTier.External));
    }

    [Fact]
    public void EveryTier_ShouldMapToExactlyOneBenchmarkCategory()
    {
        // Micro and External are Small-category work: fast, safe to run by default, and neither is
        // a target-scale claim. Only the two scale tiers are opt-in.
        Assert.Equal(BenchmarkCategories.Small, CorpusTiers.Category(CorpusTier.Micro));
        Assert.Equal(BenchmarkCategories.Small, CorpusTiers.Category(CorpusTier.Small));
        Assert.Equal(BenchmarkCategories.Small, CorpusTiers.Category(CorpusTier.External));
        Assert.Equal(BenchmarkCategories.Working, CorpusTiers.Category(CorpusTier.Working));
        Assert.Equal(BenchmarkCategories.Scale, CorpusTiers.Category(CorpusTier.Scale7M));
        Assert.Equal(BenchmarkCategories.Scale, CorpusTiers.Category(CorpusTier.Scale73M));
    }

    [Fact]
    public void EveryTierToken_ShouldRoundTrip()
    {
        foreach (var tier in CorpusTiers.All)
        {
            Assert.True(CorpusTiers.TryParse(CorpusTiers.Token(tier), out var parsed));
            Assert.Equal(tier, parsed);
        }

        Assert.False(CorpusTiers.TryParse("enormous", out _));
    }

    [Fact]
    public void TheScale73MTier_ShouldNotCarryTheKeyedOrWidthFamilies()
    {
        // The matrix asks for keyed dedupe at the working and 7.3M tiers, and keeps the width family
        // out of the target-scale matrix entirely. A 73M corpus for either would cost a tier's
        // storage for a case nothing consumes.
        var ids = CorpusCases.ForTier(CorpusTier.Scale73M).Select(corpus => corpus.Family).ToList();

        Assert.DoesNotContain(CorpusCases.KeyedFamily, ids);
        Assert.DoesNotContain(CorpusCases.AdsFamily, ids);
        Assert.DoesNotContain(CorpusCases.LongTextFamily, ids);
        Assert.Contains(CorpusCases.W16Family, ids);
        Assert.Contains(CorpusCases.T10Family, ids);
    }
}
