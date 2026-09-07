using System.Text;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The pin on an <b>acquired</b> corpus: the exact bytes the suite accepts, checked independently
/// of what the catalog recorded for itself.
/// <para>
/// A generated corpus needs no such thing — its bytes are a function of a committed generator at a
/// recorded revision, so the catalog's digest is a complete check. An acquired one is different in
/// a way that matters: the catalog records <em>whatever arrived</em>, so a changed upstream file
/// and a catalog rewritten beside it agree with each other perfectly, and every measurement stated
/// against the old bytes would silently be compared with a different corpus.
/// </para>
/// <para>
/// These tests use a local pinned case rather than the real one. The mechanism is what is under
/// test, and it must be testable without a network: no test in this repository downloads anything.
/// That the real Adult case is actually wired to it is asserted in <c>CorpusRegistryTests</c>.
/// </para>
/// </summary>
public sealed class AcquiredCorpusIdentityTests
{
    private static readonly byte[] Published = "age,city\n39,sheffield\n50,leeds\n"u8.ToArray();

    [Fact]
    public void Prepare_WhenTheAcquiredBytesAreThePinnedOnes_ThenTheCaseIsPreparedAndCatalogued()
    {
        var corpus = Acquired("pinned-ok", Published);

        var prepared = CorpusPreparer.Prepare(corpus);

        Assert.Equal(Published.Length, prepared.InputBytes);
        Assert.Equal(CorpusCatalog.HashBytes(Published), prepared.Entry.Data.Sha256);
        // The record count is still MEASURED from the acquired file; pinning the bytes does not
        // turn an acquired corpus into one whose size was chosen here.
        Assert.Equal(3, prepared.Records);
    }

    [Fact]
    public void Prepare_WhenTheAcquiredBytesHaveTheWrongLength_ThenTheyAreRefusedAndRemoved()
    {
        var corpus = Acquired("pinned-longer", Published, delivers: Concat(Published, "60,york\n"u8.ToArray()));

        var failure = Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Prepare(corpus));

        Assert.Contains("not the input this suite accepts", failure.Message, StringComparison.Ordinal);
        Assert.Contains(CorpusCatalog.HashBytes(Published), failure.Message, StringComparison.Ordinal);
        // Nothing on disk may look prepared: no catalog entry, and no file left behind that a
        // relaxed check could later mistake for a complete one.
        Assert.False(File.Exists(CorpusCatalog.PathFor(corpus.Id)));
        Assert.False(File.Exists(Path.Combine(BenchmarkPaths.CorpusDirectory, corpus.Id + ".csv")));
        Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Require(corpus));
    }

    [Fact]
    public void Prepare_WhenTheAcquiredBytesHaveTheRightLengthAndAWrongDigest_ThenTheyAreRefused()
    {
        // The interesting shape: a length check alone would pass this. One byte differs.
        var altered = Published.ToArray();
        altered[^2] = (byte)'X';
        var corpus = Acquired("pinned-same-length", Published, delivers: altered);

        var failure = Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Prepare(corpus));

        Assert.Contains("sha256", failure.Message, StringComparison.Ordinal);
        Assert.Contains(CorpusCatalog.HashBytes(altered), failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(CorpusCatalog.PathFor(corpus.Id)));
    }

    [Fact]
    public void Require_WhenTheDataChangedAndTheCatalogWasRewrittenToAgreeWithIt_ThenItIsStillRefused()
    {
        // The failure the pin exists for, and the one a self-recorded receipt cannot catch: the
        // file and the entry describing it are perfectly consistent, and both are wrong.
        var corpus = Acquired("pinned-rewritten", Published);
        var prepared = CorpusPreparer.Prepare(corpus);
        var changed = Concat(Published, "60,york\n"u8.ToArray());

        File.WriteAllBytes(prepared.DataPath, changed);
        CorpusCatalog.Write(prepared.Entry with
        {
            Data = prepared.Entry.Data with
            {
                ByteLength = changed.Length,
                Sha256 = CorpusCatalog.HashBytes(changed),
            },
        });

        // Both the entry and the file on disk say the same thing, and it is not what the source
        // pins, so the corpus is refused.
        Assert.Null(CorpusPreparer.TryLoad(CorpusPreparer.Describe(corpus)));
        Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Require(corpus));
    }

    [Fact]
    public void Require_WhenThePreparationWasInterrupted_ThenTheIncompleteCorpusIsRefused()
    {
        // The catalog is written last, so files with no entry are exactly what an interrupted
        // acquisition leaves behind - and a pinned case must not accept them either.
        var corpus = Acquired("pinned-interrupted", Published);
        var prepared = CorpusPreparer.Prepare(corpus);
        File.Delete(CorpusCatalog.PathFor(prepared.Entry.Id));

        Assert.True(File.Exists(prepared.DataPath));
        Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Require(corpus));
    }

    [Fact]
    public void Require_WhenAPreparedPinnedCorpusIsIntact_ThenItIsReusedWithoutReacquiring()
    {
        var corpus = Acquired("pinned-reused", Published);
        var first = CorpusPreparer.Prepare(corpus);
        var written = File.GetLastWriteTimeUtc(first.DataPath);

        var reused = CorpusPreparer.Require(corpus);

        // Compare the recorded identity rather than the entry object: `RecordsDeclared` is a
        // property of the EXPECTATION, not of the stored entry, and is deliberately not
        // serialized - a re-read entry always carries a real measured count.
        Assert.Equal(CorpusCatalog.Render(first.Entry), CorpusCatalog.Render(reused.Entry));
        Assert.Equal(first.Records, reused.Records);
        Assert.Equal(first.DataPath, reused.DataPath);
        Assert.Equal(written, File.GetLastWriteTimeUtc(reused.DataPath));
    }

    [Fact]
    public void Require_WhenAPreparedPinnedCorpusWasAlteredOnDisk_ThenItIsRefused()
    {
        var corpus = Acquired("pinned-altered", Published);
        var prepared = CorpusPreparer.Prepare(corpus);

        File.AppendAllText(prepared.DataPath, "60,york\n");

        Assert.Throws<InvalidOperationException>(() => CorpusPreparer.Require(corpus));
    }

    [Fact]
    public void Describe_WhenACaseIsPinned_ThenTheExpectedDataIdentityIsKnownBeforeAnythingIsAcquired()
    {
        // A generated case leaves the data identity unset until the bytes exist; a pinned one
        // states it up front, exactly as the committed spec's digest is stated up front.
        var pinned = CorpusPreparer.Describe(Acquired("pinned-described", Published));
        var generated = CorpusPreparer.Describe(CorpusCases.W16(CorpusTier.Small));

        Assert.Equal(Published.Length, pinned.Data.ByteLength);
        Assert.Equal(CorpusCatalog.HashBytes(Published), pinned.Data.Sha256);
        Assert.Equal(-1, generated.Data.ByteLength);
        Assert.Empty(generated.Data.Sha256);
    }

    private static byte[] Concat(byte[] first, byte[] second) => [.. first, .. second];

    /// <summary>
    /// A local stand-in for an acquired corpus: pinned to <paramref name="pinned"/>, and delivering
    /// <paramref name="delivers"/> when it is prepared. Passing different bytes for the two is how
    /// a changed upstream file is reproduced without a network.
    /// </summary>
    private static CorpusCase Acquired(string family, byte[] pinned, byte[]? delivers = null)
    {
        var bytes = delivers ?? pinned;
        return new CorpusCase(
            family,
            Variant: string.Empty,
            CorpusTier.External,
            GeneratorRevision: 1,
            Columns: 2,
            SpecText: "# a stand-in spec; these tests never convert the corpus\n",
            (stream, records, token) => stream.Write(bytes, 0, bytes.Length))
        {
            Origin = CorpusOrigin.External,
            CountRecords = path => File.ReadLines(path, Encoding.UTF8).Count(),
            DataIdentity = new CorpusIdentity(pinned.Length, CorpusCatalog.HashBytes(pinned)),
        };
    }
}
