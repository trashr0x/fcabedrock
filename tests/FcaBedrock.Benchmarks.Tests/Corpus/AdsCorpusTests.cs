using System.Security.Cryptography;
using System.Text;
using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Tests.Corpus;

/// <summary>
/// The Ads-width corpus exists to pressure <em>width</em>. These tests hold the two things that
/// makes true: the geometry really is 1,559 columns, and the incidence really is sparse. A generator
/// that quietly produced a dense wide corpus would still run, and every width measurement taken over
/// it would then be measuring something else.
/// </summary>
public sealed class AdsCorpusTests
{
    private static byte[] Generate(long records)
    {
        using var buffer = new MemoryStream();
        AdsCorpus.Write(buffer, records);
        return buffer.ToArray();
    }

    [Fact]
    public void Write_WhenTheSameTierIsGeneratedTwice_ThenTheBytesAreIdentical()
    {
        Assert.Equal(SHA256.HashData(Generate(200)), SHA256.HashData(Generate(200)));
    }

    [Fact]
    public void Geometry_ShouldBeThreeNumericALocalFlagTheTermsAndAClass()
    {
        Assert.Equal(1_559, AdsCorpus.ColumnCount);
        Assert.Equal(1_554, AdsCorpus.TermColumns);
        Assert.Equal(AdsCorpus.ColumnCount, AdsCorpus.Columns.Count);
        Assert.Equal(AdsCorpus.ColumnCount - 1, AdsCorpus.ColClass);
        Assert.Equal("height", AdsCorpus.Columns[AdsCorpus.ColHeight]);
        Assert.Equal("local", AdsCorpus.Columns[AdsCorpus.ColLocal]);
        Assert.Equal("t0000", AdsCorpus.Columns[AdsCorpus.ColTermFirst]);
        Assert.Equal("class", AdsCorpus.Columns[AdsCorpus.ColClass]);
    }

    [Fact]
    public void Write_ShouldProduceTheHeaderPlusExactlyOneLinePerRecordWithEveryColumn()
    {
        var lines = Encoding.UTF8.GetString(Generate(11)).Split('\n');

        Assert.Equal(AdsCorpus.HeaderLine, lines[0]);
        Assert.Equal(11 + 2, lines.Length);
        Assert.Equal(string.Empty, lines[^1]);
        foreach (var line in lines[..^1])
        {
            Assert.Equal(AdsCorpus.ColumnCount, line.Split(',').Length);
        }
    }

    [Fact]
    public void Write_ShouldNeedNoEscapingAtAll()
    {
        // Every value is a digit run, a fixed decimal, or one of two class labels, so the quoting
        // path is unreachable here by construction - which is what keeps this family's bytes a
        // function of its width alone. The W16 family owns the quoting case.
        var text = Encoding.UTF8.GetString(Generate(50));

        Assert.DoesNotContain('"', text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void TermFlags_ShouldBeSparse()
    {
        // About 1.2% set. Sparsity is the property that makes a wide row's incidence small even
        // though its schema is enormous; a dense corpus would measure a different workload.
        var set = 0L;
        var total = 0L;
        for (var row = 0L; row < 200; row++)
        {
            for (var term = 0; term < AdsCorpus.TermColumns; term++)
            {
                total++;
                if (AdsCorpus.TermIsSet(row, term))
                {
                    set++;
                }
            }
        }

        var density = set / (double)total;
        Assert.InRange(density, 0.005, 0.03);
    }

    [Fact]
    public void EveryColumn_ShouldProduceAPresentValue()
    {
        // No missing cells in this family, deliberately: a second pressure mixed into a width case
        // would make a measurement harder to attribute, not more realistic.
        for (var column = 0; column < AdsCorpus.ColumnCount; column++)
        {
            Assert.NotNull(AdsCorpus.CleanedValue(7, column));
        }
    }

    [Fact]
    public void NumericColumns_ShouldSpanTheirDeclaredCutsInBothDirections()
    {
        // A cut bin nothing lands in is a column of zeros, which is a plausible-looking but useless
        // measurement; this asserts the numeric columns actually straddle their cuts.
        var heights = Enumerable.Range(0, 500).Select(row => AdsCorpus.Height(row)).ToList();
        var aspects = Enumerable.Range(0, 500)
            .Select(row => Determinism.HundredthsValue(AdsCorpus.AspectHundredths(row))).ToList();

        Assert.Contains(heights, height => height < AdsSpecs.SizeCuts[0]);
        Assert.Contains(heights, height => height >= AdsSpecs.SizeCuts[^1]);
        Assert.Contains(aspects, aspect => aspect < AdsSpecs.AspectCuts[0]);
        Assert.Contains(aspects, aspect => aspect >= AdsSpecs.AspectCuts[^1]);
    }

    [Fact]
    public void Classes_ShouldCarryBothLabels()
    {
        var labels = Enumerable.Range(0, 200).Select(row => AdsCorpus.ClassLabel(row)).Distinct().ToList();

        Assert.Equal(2, labels.Count);
    }
}
