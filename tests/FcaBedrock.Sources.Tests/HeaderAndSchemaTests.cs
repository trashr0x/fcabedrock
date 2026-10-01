using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Spec §5.1.1 header and schema rules: the first non-blank record is the header, decoded by the
/// field grammar and never missing-normalized; empty and duplicate names are legal; a quoted empty
/// header has one column; a source with no non-blank record has no header and no columns; and
/// without a header the first non-blank record gives the width, which later records do not
/// change. The schema read stops after the record it needs.
/// </summary>
public sealed class HeaderAndSchemaTests
{
    private static string ShowSchema(SourceSchema schema) =>
        $"width={schema.ColumnCount} header={(schema.Header is null ? "none" : Render(schema.Header))}";

    [Theory]
    [InlineData("\n\n  \nid,size\n\n1,2\n\n3,4\n", true, "width=2 header=[<id>,<size>]", "0:[<1>,<2>] 1:[<3>,<4>]")]
    [InlineData("\n a,b,c\n1,2\n", false, "width=3 header=none", "0:[<a>,<b>,<c>] 1:[<1>,<2>]")]
    [InlineData("\"\"\n1\n", true, "width=1 header=[<>]", "0:[<1>]")]
    [InlineData("\n", true, "width=0 header=none", "")]
    [InlineData("", true, "width=0 header=none", "")]
    [InlineData("a,b\n", true, "width=2 header=[<a>,<b>]", "")]
    [InlineData("a,a,,\n1,2,3,4", true, "width=4 header=[<a>,<a>,<>,<>]", "0:[<1>,<2>,<3>,<4>]")]
    [InlineData("?,b\n?,x", true, "width=2 header=[<?>,<b>]", "0:[null,<x>]")]
    [InlineData("  \t\n\n", false, "width=0 header=none", "")]
    [InlineData(" \"h 1\" ,\t\"h,2\" \n", true, "width=2 header=[<h 1>,<h,2>]", "")]
    [InlineData("a\n\nb\n  \nc", false, "width=1 header=none", "0:[<a>] 1:[<b>] 2:[<c>]")]
    public async Task Schema_WhenReadBesideTheRecords_ThenHeaderWidthAndRecordsFollowTheRules(
        string text, bool hasHeader, string expectedSchema, string expectedRecords)
    {
        var schema = await WideSession(Opener(text), hasHeader: hasHeader).GetSchemaAsync();
        var records = await DrainAsync(WideSession(Opener(text), hasHeader: hasHeader).ReadAsync());

        Assert.Equal(expectedSchema, ShowSchema(schema));
        Assert.Equal(expectedRecords, string.Join(" ", records.Select(Render)));
    }

    [Fact]
    public async Task Schema_WhenEveryReadingRouteReadsTheSameHeader_ThenTheyAgree()
    {
        const string Text = "\n \"s\" ,\tp\t,v" + "\n" + "a,b,c\n";
        var expected = "width=3 header=[<s>,<p>,<v>]";

        Assert.Equal(expected, ShowSchema(await WideSession(Opener(Text), hasHeader: true).GetSchemaAsync()));
        Assert.Equal(expected, ShowSchema(await TripleSession(Opener(Text), hasHeader: true).GetSchemaAsync()));
        Assert.Equal(expected, ShowSchema(await new WideCsvSource(Opener(Text), WideBinding(hasHeader: true)).GetSchemaAsync()));
        Assert.Equal(expected, ShowSchema(await new TripleCsvSource(Opener(Text), TripleBinding(hasHeader: true)).GetSchemaAsync()));
    }

    [Fact]
    public async Task Schema_WhenTheHeaderHasAQuotedCellWithAnEscapedQuote_ThenItIsDecoded()
    {
        var schema = await WideSession(Opener("h1,\"h \"\"2\"\"\"\n1,2\n"), hasHeader: true).GetSchemaAsync();

        Assert.Equal(["h1", "h \"2\""], schema.Header);
    }

    [Fact]
    public async Task Schema_WhenHeaderlessAndTheFirstRecordHasAQuote_ThenItIsValidatedAndGivesTheWidth()
    {
        var schema = await WideSession(Opener("\"x,y\",z\n1\n")).GetSchemaAsync();

        Assert.Equal(2, schema.ColumnCount);
    }

    [Fact]
    public async Task Read_WhenLaterRecordsAreShorterOrLonger_ThenTheyAreToleratedAndTheSchemaKeepsItsWidth()
    {
        const string Text = "a,b\n1\n1,2,3\n";

        var schema = await WideSession(Opener(Text)).GetSchemaAsync();
        var records = await DrainAsync(WideSession(Opener(Text)).ReadAsync());

        Assert.Equal(2, schema.ColumnCount);
        Assert.Equal(["0:[<a>,<b>]", "1:[<1>]", "2:[<1>,<2>,<3>]"], records.Select(Render));
    }

    [Fact]
    public async Task Schema_WhenTheHeaderIsValidAndTheBodyIsMalformed_ThenOnlyTheDataReadRefusesIt()
    {
        // The schema read stops after the header; the body is validated by the data read.
        const string Text = "a,b\nx\"y,1\n";

        var schema = await WideSession(Opener(Text), hasHeader: true).GetSchemaAsync();
        var refused = await Assert.ThrowsAsync<SourceReadException>(() => DrainAsync(WideSession(Opener(Text), hasHeader: true).ReadAsync()));

        Assert.Equal("width=2 header=[<a>,<b>]", ShowSchema(schema));
        Assert.Contains("data record 0, column 0", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_WhenTheHeaderIsMalformedAfterABlankLine_ThenTheRefusalNamesTheHeaderAndItsLine()
    {
        var refused = await Assert.ThrowsAsync<SourceReadException>(
            async () => await WideSession(Opener("\n\"a\"b,c\n1,2\n"), hasHeader: true).GetSchemaAsync());

        Assert.Null(refused.InnerException);
        Assert.Equal(
            DelimitedGrammarConformanceTests.Message(DelimitedGrammarConformanceTests.Defect.TextAfterClosingQuote, null, 2, 0, 2),
            refused.Message);
    }

    [Fact]
    public async Task Schema_WhenTheSourceIsLargerThanEveryBuffer_ThenTheReadStopsEarlyAndClosesTheStreamOnce()
    {
        var text = "h1,h2\n" + string.Concat(Enumerable.Range(0, 200_000).Select(i => $"{i},v\n"));
        var stream = ScriptedStream.Utf8(text);

        var schema = await WideSession(() => stream, hasHeader: true).GetSchemaAsync();

        Assert.Equal(["h1", "h2"], schema.Header);
        Assert.True(stream.BytesRead < Utf8(text).Length, $"consumed {stream.BytesRead} bytes");
        Assert.Equal(1, stream.DisposeCount);
    }
}
