using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec.Tests;

public sealed class BedReaderTests
{
    private static BedDocument Read(string text)
    {
        var read = BedReader.Read(text);
        Assert.True(read.TryGetValue(out var document));
        return document;
    }

    [Fact]
    public void Read_WhenMushroomBed_ThenParsesNamesTypesAndConvertFlags()
    {
        var document = Read(BedFixtures.MushroomBed);

        Assert.Equal(5, document.AttributeCount);
        Assert.Equal(["class", "bruises?", "gill-size", "veil-type", "ring-number"], document.Names);
        Assert.Equal(["c", "b", "c", "c", "c"], document.Types);
        Assert.Equal([false, true, true, true, true], document.Convert);
    }

    [Fact]
    public void Read_WhenMushroomBed_ThenSeparatesRawValuesFromDisplayCategories()
    {
        var document = Read(BedFixtures.MushroomBed);

        Assert.Equal(["b", "n"], document.Values[2]);            // gill-size raw values
        Assert.Equal(["broad", "narrow"], document.Categories[2]); // gill-size display labels
    }

    [Fact]
    public void Read_WhenRestrictSectionBlank_ThenRestrictionsAreEmptyPerAttribute()
    {
        var document = Read(BedFixtures.MushroomBed);

        Assert.Equal(5, document.RestrictTo.Count);
        Assert.All(document.RestrictTo, r => Assert.Equal("", r));
    }

    [Fact]
    public void Read_WhenSourceUsesCrlf_ThenParsesIdenticallyToLf()
    {
        // Line endings are not data: a .bed file must parse the same whether it
        // arrived with LF (Unix) or CRLF (Windows) endings. The reader normalizes
        // CRLF->LF before splitting, so no terminator survives into a token.
        var fromLf = Read(BedFixtures.MushroomBed);
        var fromCrlf = Read(BedFixtures.MushroomBed.Replace("\n", "\r\n"));

        Assert.Equal(fromLf.AttributeCount, fromCrlf.AttributeCount);
        Assert.Equal(fromLf.Names, fromCrlf.Names);
        Assert.Equal(fromLf.Types, fromCrlf.Types);
        Assert.Equal(fromLf.Convert, fromCrlf.Convert);
        Assert.Equal(fromLf.Categories, fromCrlf.Categories);
        Assert.Equal(fromLf.Values, fromCrlf.Values);
        Assert.Equal(fromLf.RestrictTo, fromCrlf.RestrictTo);
        Assert.Equal("ring-number", fromCrlf.Names[^1]); // a token ending its line — no trailing '\r'
    }

    [Fact]
    public void Read_WhenSectionMissing_ThenFatalBedStructureInvalid()
    {
        var withoutTypes = BedFixtures.MushroomBed.Replace("[Attribute Type]", "[Attribute Kind]");

        var read = BedReader.Read(withoutTypes, filePath: "mini-mushroom.bed");

        Assert.False(read.TryGetValue(out _));
        var diagnostic = Assert.Single(read.Diagnostics, d => d.Code == DiagnosticCode.BedStructureInvalid);
        Assert.Equal(DiagnosticSeverity.Fatal, diagnostic.Severity);
        Assert.Contains("[Attribute Type]", diagnostic.Message);
        Assert.Equal("mini-mushroom.bed", diagnostic.Location?.File);
    }

    [Fact]
    public void Read_WhenMultipleSectionsMissing_ThenAllAggregate()
    {
        // P-13: everything checkable is reported in one pass, not first-failure-wins.
        var withoutTwo = BedFixtures.MushroomBed
            .Replace("[Attribute Type]", "[Attribute Kind]")
            .Replace("[Convert Attribute]", "[Convert]");

        var read = BedReader.Read(withoutTwo);

        Assert.False(read.TryGetValue(out _));
        Assert.Contains(read.Diagnostics, d => d.Message.Contains("[Attribute Type]", StringComparison.Ordinal));
        Assert.Contains(read.Diagnostics, d => d.Message.Contains("[Convert Attribute]", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WhenAttributeCountUnparseable_ThenFatalBedStructureInvalid()
    {
        var badCount = BedFixtures.MushroomBed.Replace("[Number of Attributes]\n5\n", "[Number of Attributes]\nfive\n");

        var read = BedReader.Read(badCount);

        Assert.False(read.TryGetValue(out _));
        var diagnostic = Assert.Single(read.Diagnostics);
        Assert.Equal(DiagnosticCode.BedStructureInvalid, diagnostic.Code);
        Assert.Contains("'five'", diagnostic.Message);
        Assert.Equal(2, diagnostic.Location?.Line);
    }

    [Fact]
    public void Read_WhenSectionHasFewerEntriesThanCount_ThenFatalBedStructureInvalid()
    {
        // Declare six attributes over the five-attribute body. Sections followed by a
        // blank separator line absorb one extra entry (the v2 layout is that brittle —
        // the point of D-009); the shortfall lands on [Restrict To Values] (five lines)
        // and the blank sixth convert flag fails to parse. Both report (aggregated).
        var overdeclared = BedFixtures.MushroomBed.Replace("[Number of Attributes]\n5\n", "[Number of Attributes]\n6\n");

        var read = BedReader.Read(overdeclared);

        Assert.False(read.TryGetValue(out _));
        Assert.All(read.Diagnostics, d => Assert.Equal(DiagnosticCode.BedStructureInvalid, d.Code));
        Assert.Contains(read.Diagnostics, d => d.Message.Contains("[Restrict To Values]", StringComparison.Ordinal));
        Assert.Contains(read.Diagnostics, d => d.Message.Contains("[Convert Attribute]", StringComparison.Ordinal));
    }
}
