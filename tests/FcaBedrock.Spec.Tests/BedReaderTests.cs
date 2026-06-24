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
}
