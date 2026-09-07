using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The catalog decides whether cached inputs may be believed. Everything it cannot fully read, or
/// that no longer matches what it recorded, must be refused — a stale corpus quietly compared
/// against a fresh one is the exact failure this exists to prevent.
/// </summary>
public sealed class CorpusCatalogTests
{
    private static CorpusEntry Sample() => new(
        "w16-small",
        "w16",
        "small",
        GeneratorRevision: 1,
        Records: 10_000,
        Columns: 16,
        new CorpusFile("w16-small.csv", 624_470, new string('a', 64)),
        new CorpusFile("w16-small.toml", 1_024, new string('b', 64)));

    [Fact]
    public void RenderAndParse_ShouldRoundTripEveryRecordedFact()
    {
        var parsed = CorpusCatalog.TryParse(CorpusCatalog.Render(Sample()));

        Assert.Equal(Sample(), parsed);
    }

    [Fact]
    public void Render_ShouldProduceStableLineOrientedBytes()
    {
        var text = CorpusCatalog.Render(Sample());

        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.StartsWith("catalog_format = 1\n", text, StringComparison.Ordinal);
        Assert.Equal(CorpusCatalog.Render(Sample()), text);
    }

    [Theory]
    [InlineData("catalog_format = 2")]        // a format this reader does not understand
    [InlineData("records = ten-thousand")]    // an unparseable number
    [InlineData("records = -1")]              // a negative count, which the strict style rejects
    [InlineData("id")]                        // a line with no separator
    public void TryParse_WhenTheTextIsNotFullyUnderstood_ThenItIsRefused(string replacement)
    {
        var key = replacement.Split(' ')[0];
        var lines = CorpusCatalog.Render(Sample())
            .Split('\n')
            .Where(line => line.Length > 0)
            .Select(line => line.StartsWith(key + " ", StringComparison.Ordinal) ? replacement : line);

        Assert.Null(CorpusCatalog.TryParse(string.Join('\n', lines)));
    }

    [Fact]
    public void TryParse_WhenARequiredKeyIsAbsent_ThenItIsRefused()
    {
        var lines = CorpusCatalog.Render(Sample())
            .Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith("spec_sha256 ", StringComparison.Ordinal));

        Assert.Null(CorpusCatalog.TryParse(string.Join('\n', lines)));
    }

    [Fact]
    public void HashFile_ShouldMatchTheDigestOfTheSameBytes()
    {
        using var temp = TempDirectory.Create();
        var path = temp.File("bytes");
        var bytes = "the quick brown fox\n"u8.ToArray();
        File.WriteAllBytes(path, bytes);

        Assert.Equal(CorpusCatalog.HashBytes(bytes), CorpusCatalog.HashFile(path));
    }
}

/// <summary>
/// Preparation is explicit, idempotent, and verified: a case is reused only when everything the
/// catalog recorded still holds, and a run that finds anything else refuses to measure.
/// </summary>
public sealed class CorpusPreparerTests
{
    [Fact]
    public void PrepareW16_ShouldGenerateTheTierAndRecordItsIdentity()
    {
        var prepared = CorpusPreparer.Prepare(CorpusCases.W16(CorpusTier.Small));

        Assert.Equal(10_000, prepared.Records);
        Assert.True(File.Exists(prepared.DataPath));
        Assert.True(File.Exists(prepared.SpecPath));
        Assert.Equal(new FileInfo(prepared.DataPath).Length, prepared.InputBytes);
        Assert.Equal(CorpusCatalog.HashFile(prepared.DataPath), prepared.Entry.Data.Sha256);
        Assert.Equal(CorpusCatalog.HashFile(prepared.SpecPath), prepared.Entry.Spec.Sha256);
    }

    [Fact]
    public void PrepareW16_WhenRunTwice_ThenTheSecondRunReusesTheIdenticalCorpus()
    {
        var first = CorpusPreparer.Prepare(CorpusCases.W16(CorpusTier.Small));
        var written = File.GetLastWriteTimeUtc(first.DataPath);

        var second = CorpusPreparer.Prepare(CorpusCases.W16(CorpusTier.Small));

        Assert.Equal(first.Entry, second.Entry);
        Assert.Equal(written, File.GetLastWriteTimeUtc(second.DataPath));
    }

    [Fact]
    public void Require_WhenTheDataNoLongerMatchesItsRecordedDigest_ThenItIsRefused()
    {
        var prepared = CorpusPreparer.Prepare(CorpusCases.W16(CorpusTier.Small));
        var original = File.ReadAllBytes(prepared.DataPath);
        try
        {
            File.AppendAllText(prepared.DataPath, "n_seq,n_ties\n");

            var failure = Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Require(CorpusCases.W16(CorpusTier.Small)));
            Assert.Contains("no longer matches its recorded", failure.Message, StringComparison.Ordinal);
            Assert.Contains("prepare small", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.WriteAllBytes(prepared.DataPath, original);
        }
    }

    [Fact]
    public void Require_WhenTheCatalogEntryIsAbsent_ThenItIsRefused()
    {
        var prepared = CorpusPreparer.Prepare(CorpusCases.W16(CorpusTier.Small));
        var catalogPath = CorpusCatalog.PathFor(prepared.Entry.Id);
        var original = File.ReadAllBytes(catalogPath);
        try
        {
            File.Delete(catalogPath);

            // Files with no entry are exactly what an interrupted preparation leaves behind.
            Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Require(CorpusCases.W16(CorpusTier.Small)));
        }
        finally
        {
            File.WriteAllBytes(catalogPath, original);
        }
    }

    [Fact]
    public void TryLoad_WhenTheGeneratorRevisionMoved_ThenTheCorpusIsSuperseded()
    {
        CorpusPreparer.Prepare(CorpusCases.W16(CorpusTier.Small));
        var expected = CorpusPreparer.Describe(CorpusCases.W16(CorpusTier.Small));

        Assert.NotNull(CorpusPreparer.TryLoad(expected));
        Assert.Null(CorpusPreparer.TryLoad(expected with { GeneratorRevision = expected.GeneratorRevision + 1 }));
        Assert.Null(CorpusPreparer.TryLoad(expected with { Records = expected.Records + 1 }));
    }

    [Fact]
    public void Describe_ShouldRecordTheSpecDigestBeforeAnythingIsGenerated()
    {
        // The spec is committed source, so its digest is known up front; the data's is not, and is
        // deliberately left unset until the bytes actually exist.
        var corpus = CorpusCases.W16(CorpusTier.Small);
        var expected = CorpusPreparer.Describe(corpus);

        Assert.Equal(CorpusCatalog.HashBytes(corpus.SpecBytes()), expected.Spec.Sha256);
        Assert.Equal(-1, expected.Data.ByteLength);
    }

    [Fact]
    public void Prepare_ShouldPrepareEveryFamilyATierDefines()
    {
        // The registry is what the `prepare` verb, the benchmarks, and the report columns all read,
        // so every case it names has to be preparable - a family added to the registry and forgotten
        // in the preparer would only surface as a benchmark failing hours later.
        foreach (var corpus in CorpusCases.ForTier(CorpusTier.Small))
        {
            var prepared = CorpusPreparer.Prepare(corpus);

            Assert.Equal(corpus.Id, prepared.Entry.Id);
            Assert.Equal(corpus.Records, prepared.Records);
            Assert.True(prepared.InputBytes > 0);
            Assert.NotNull(CorpusPreparer.TryLoad(CorpusPreparer.Describe(corpus)));
        }
    }

    [Fact]
    public void TryLoad_WhenTheSpecTextChanged_ThenTheCorpusIsADifferentCase()
    {
        // The spec is part of a case's identity: the same data under a different spec is a different
        // measurement, however identical the bytes on disk happen to be.
        var corpus = CorpusCases.W16(CorpusTier.Small);
        CorpusPreparer.Prepare(corpus);
        var expected = CorpusPreparer.Describe(corpus);

        Assert.NotNull(CorpusPreparer.TryLoad(expected));
        Assert.Null(CorpusPreparer.TryLoad(
            expected with { Spec = expected.Spec with { Sha256 = new string('0', 64) } }));
    }
}
