namespace FcaBedrock.Spec.Tests;

public sealed class BedReaderTests
{
    [Fact]
    public void Read_WhenMushroomBed_ThenParsesNamesTypesAndConvertFlags()
    {
        var document = BedReader.Read(BedFixtures.MushroomBed);

        Assert.Equal(5, document.AttributeCount);
        Assert.Equal(["class", "bruises?", "gill-size", "veil-type", "ring-number"], document.Names);
        Assert.Equal(["c", "b", "c", "c", "c"], document.Types);
        Assert.Equal([false, true, true, true, true], document.Convert);
    }

    [Fact]
    public void Read_WhenMushroomBed_ThenSeparatesRawValuesFromDisplayCategories()
    {
        var document = BedReader.Read(BedFixtures.MushroomBed);

        Assert.Equal(["b", "n"], document.Values[2]);            // gill-size raw values
        Assert.Equal(["broad", "narrow"], document.Categories[2]); // gill-size display labels
    }

    [Fact]
    public void Read_WhenRestrictSectionBlank_ThenRestrictionsAreEmptyPerAttribute()
    {
        var document = BedReader.Read(BedFixtures.MushroomBed);

        Assert.Equal(5, document.RestrictTo.Count);
        Assert.All(document.RestrictTo, r => Assert.Equal("", r));
    }

    [Fact]
    public void Read_WhenSourceUsesCrlf_ThenParsesIdenticallyToLf()
    {
        // Line endings are not data: a .bed file must parse the same whether it
        // arrived with LF (Unix) or CRLF (Windows) endings. The reader normalizes
        // CRLF->LF before splitting, so no terminator survives into a token.
        var fromLf = BedReader.Read(BedFixtures.MushroomBed);
        var fromCrlf = BedReader.Read(BedFixtures.MushroomBed.Replace("\n", "\r\n"));

        Assert.Equal(fromLf.AttributeCount, fromCrlf.AttributeCount);
        Assert.Equal(fromLf.Names, fromCrlf.Names);
        Assert.Equal(fromLf.Types, fromCrlf.Types);
        Assert.Equal(fromLf.Convert, fromCrlf.Convert);
        Assert.Equal(fromLf.Categories, fromCrlf.Categories);
        Assert.Equal(fromLf.Values, fromCrlf.Values);
        Assert.Equal(fromLf.RestrictTo, fromCrlf.RestrictTo);
        Assert.Equal("ring-number", fromCrlf.Names[^1]); // a token ending its line — no trailing '\r'
    }
}
