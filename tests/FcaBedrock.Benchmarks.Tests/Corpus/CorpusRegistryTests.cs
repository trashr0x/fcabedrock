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
        // Micro is Small-category work: fast, generated from the same pinned arithmetic as the rest,
        // and not a target-scale claim. External is not - its corpus is acquired from a third-party
        // host, so it is opt-in for a reason that has nothing to do with size.
        Assert.Equal(BenchmarkCategories.Small, CorpusTiers.Category(CorpusTier.Micro));
        Assert.Equal(BenchmarkCategories.Small, CorpusTiers.Category(CorpusTier.Small));
        Assert.Equal(BenchmarkCategories.External, CorpusTiers.Category(CorpusTier.External));
        Assert.Equal(BenchmarkCategories.Working, CorpusTiers.Category(CorpusTier.Working));
        Assert.Equal(BenchmarkCategories.Scale, CorpusTiers.Category(CorpusTier.Scale7M));
        Assert.Equal(BenchmarkCategories.Scale, CorpusTiers.Category(CorpusTier.Scale73M));
    }

    [Fact]
    public void TheSmallTier_ShouldNotCarryTheAcquiredCase()
    {
        // `prepare small` prepares what a routine run needs, and a routine run must not need a
        // download. This is the preparation half of the selection guarantee: the CI job prepares
        // `micro small`, so Adult appearing under either token would put the network back.
        var smallIds = CorpusCases.ForTier(CorpusTier.Small).Select(corpus => corpus.Id).ToList();
        var microIds = CorpusCases.ForTier(CorpusTier.Micro).Select(corpus => corpus.Id).ToList();

        Assert.DoesNotContain(CorpusCases.Adult.Id, smallIds);
        Assert.DoesNotContain(CorpusCases.Adult.Id, microIds);
        Assert.All(
            CorpusCases.ForTier(CorpusTier.Small).Concat(CorpusCases.ForTier(CorpusTier.Micro)),
            corpus => Assert.Equal(CorpusOrigin.Generated, corpus.Origin));
        Assert.Contains(CorpusCases.Adult.Id, CorpusCases.ForTier(CorpusTier.External).Select(c => c.Id));
    }

    [Fact]
    public void TheAcquiredCase_ShouldPinTheExactEntryItConsumes()
    {
        // The wiring assertion. The mechanism is tested offline in AcquiredCorpusIdentityTests;
        // this is what says the real case actually uses it, and with which bytes: the length and
        // digest of the `adult.data` entry every M8 measurement was stated against.
        var identity = CorpusCases.Adult.DataIdentity;

        Assert.NotNull(identity);
        Assert.Equal(3_974_305L, identity.ByteLength);
        Assert.Equal(
            "5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d", identity.Sha256);
        Assert.Equal(AdultCorpus.DataByteLength, identity.ByteLength);
        Assert.Equal(AdultCorpus.DataSha256, identity.Sha256);

        // The pin and the acquisition revision are two halves of one fact, so a case that carries a
        // pin must also carry the revision the pinned bytes were catalogued under.
        Assert.Equal(AdultCorpus.AcquisitionRevision, CorpusCases.Adult.GeneratorRevision);
    }

    [Fact]
    public void AGeneratedCase_ShouldCarryNoPinnedIdentity()
    {
        // A generated corpus needs none: its bytes are a function of a committed generator at a
        // recorded revision, so its identity is derived rather than asserted.
        Assert.All(
            CorpusCases.All.Where(corpus => corpus.Origin == CorpusOrigin.Generated),
            corpus => Assert.Null(corpus.DataIdentity));
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
