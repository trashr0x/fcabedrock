using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Spec §5.1.1 blank records: a record with no quote, no delimiter and only outer whitespace (the
/// empty record included) is skipped before the header, between records and at the end,
/// identically for schema, replay, bound and unbound reads of both shapes. Quoted empty, quoted
/// whitespace and quoted multiline values, and delimiter-only records, are records.
/// </summary>
public sealed class BlankRecordTests
{
    // Blank runs before, between and after the records, with LF, CRLF and lone CR endings.
    private const string WithBlankRuns = "\n\r\n  \t \ra,b\n\n \r\nc,d\n\n\r\n";

    [Fact]
    public async Task Read_WhenBlankRunsSurroundAndSeparateRecords_ThenEveryRouteSkipsThemAlike()
    {
        var expected = new[] { "0:[<a>,<b>]", "1:[<c>,<d>]" };

        Assert.Equal(expected, (await DrainAsync(WideSession(Opener(WithBlankRuns)).ReadAsync())).Select(Render));
        Assert.Equal(expected, (await DrainAsync(new WideCsvSource(Opener(WithBlankRuns), WideBinding()).ReadAsync())).Select(Render));
        Assert.Equal(
            ["0:[<a>,<b>,null]", "1:[<c>,<d>,null]"],
            (await DrainAsync(TripleSession(Opener(WithBlankRuns)).ReadRowsAsync(new TripleColumns(0, 1, 2)))).Select(Render));
        Assert.Equal(
            ["0:[<a>,<b>,null]", "1:[<c>,<d>,null]"],
            (await DrainAsync(new TripleCsvSource(Opener(WithBlankRuns), TripleBinding()).ReadRowsAsync())).Select(Render));
    }

    [Fact]
    public async Task Read_WhenReplayed_ThenTheBlankSkippingAndIndicesRepeat()
    {
        var session = WideSession(Opener(WithBlankRuns));

        var first = (await DrainAsync(session.ReadAsync())).Select(Render).ToList();
        var second = (await DrainAsync(session.ReadAsync())).Select(Render).ToList();

        Assert.Equal(["0:[<a>,<b>]", "1:[<c>,<d>]"], first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Schema_WhenBlankRunsPrecedeTheHeader_ThenTheFirstNonBlankRecordIsTheHeader()
    {
        var wide = await WideSession(Opener(WithBlankRuns), hasHeader: true).GetSchemaAsync();
        var triple = await TripleSession(Opener(WithBlankRuns), hasHeader: true).GetSchemaAsync();
        var bound = await new WideCsvSource(Opener(WithBlankRuns), WideBinding(hasHeader: true)).GetSchemaAsync();

        Assert.Equal(["a", "b"], wide.Header);
        Assert.Equal(["a", "b"], triple.Header);
        Assert.Equal(["a", "b"], bound.Header);
        Assert.Equal(["0:[<c>,<d>]"], (await DrainAsync(WideSession(Opener(WithBlankRuns), hasHeader: true).ReadAsync())).Select(Render));
    }

    [Fact]
    public async Task Schema_WhenHeaderlessAndBlankRunsPrecedeTheFirstRecord_ThenItsWidthIsTheSchema()
    {
        var schema = await WideSession(Opener(WithBlankRuns)).GetSchemaAsync();

        Assert.Equal(2, schema.ColumnCount);
        Assert.Null(schema.Header);
    }

    // Every member of W that is not a line break, alone on a line, is blank.
    [Theory]
    [InlineData(0x09)]
    [InlineData(0x0B)]
    [InlineData(0x0C)]
    [InlineData(0x20)]
    [InlineData(0x85)]
    [InlineData(0xA0)]
    [InlineData(0x1680)]
    [InlineData(0x2000)]
    [InlineData(0x2005)]
    [InlineData(0x200A)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0x202F)]
    [InlineData(0x205F)]
    [InlineData(0x3000)]
    public async Task Read_WhenALineHoldsOnlyWhitespace_ThenItIsBlank(int code)
    {
        var text = "a\n" + U(code) + U(code) + "\nb\n";

        Assert.Equal(["0:[<a>]", "1:[<b>]"], (await DrainAsync(WideSession(Opener(text)).ReadAsync())).Select(Render));
    }

    [Theory]
    [InlineData(0x200B)] // zero-width space
    [InlineData(0xFEFF)] // zero-width no-break space
    [InlineData(0x180E)] // Mongolian vowel separator
    public async Task Read_WhenALineHoldsOnlyAFormatCharacterThatIsNotWhitespace_ThenItIsARecord(int code)
    {
        var text = "a\n" + U(code) + "\nb\n";

        Assert.Equal(
            ["0:[<a>]", $"1:[{Show(U(code))}]", "2:[<b>]"],
            (await DrainAsync(WideSession(Opener(text)).ReadAsync())).Select(Render));
    }

    [Theory]
    [InlineData("\"\"\n", "0:[null]")]
    [InlineData("\" \"\n", "0:[< >]")]
    [InlineData("\"\r\n\"\n", "0:[<\\r\\n>]")]
    [InlineData(",\n", "0:[null,null]")]
    [InlineData(" , \n", "0:[null,null]")]
    public async Task Read_WhenARecordIsQuotedOrHasADelimiter_ThenItIsNotBlank(string text, string expected)
    {
        Assert.Equal([expected], (await DrainAsync(WideSession(Opener(text), missingToken: string.Empty).ReadAsync())).Select(Render));
    }

    [Fact]
    public async Task Read_WhenTheDelimiterIsWhitespace_ThenALineOfThatDelimiterIsADelimiterOnlyRecord()
    {
        // TAB delimiter: a TAB line has a delimiter, so it is a record of two empty fields. Space
        // delimiter: a space line is a record, and a TAB line is blank.
        Assert.Equal(["0:[null,null]"], (await DrainAsync(WideSession(Opener("\t\n"), '\t').ReadAsync())).Select(Render));
        Assert.Equal(["0:[null,null]"], (await DrainAsync(WideSession(Opener(" \n\t\n"), ' ').ReadAsync())).Select(Render));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n\r\n\r\n")]
    [InlineData("  \t \r\n")]
    public async Task Read_WhenTheInputIsEmptyOrOnlyBlank_ThenThereAreNoRecordsAndNoColumns(string text)
    {
        Assert.Empty(await DrainAsync(WideSession(Opener(text)).ReadAsync()));
        Assert.Empty(await DrainAsync(TripleSession(Opener(text)).ReadRowsAsync(new TripleColumns(0, 1, 2))));

        var headerless = await WideSession(Opener(text)).GetSchemaAsync();
        var withHeader = await WideSession(Opener(text), hasHeader: true).GetSchemaAsync();
        Assert.Equal(0, headerless.ColumnCount);
        Assert.Equal(0, withHeader.ColumnCount);
        Assert.Null(withHeader.Header);
    }
}
