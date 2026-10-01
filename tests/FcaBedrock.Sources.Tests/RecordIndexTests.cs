using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Spec §5.1.1 record indices and physical lines. Data records are numbered from 0 in input
/// order, counting neither the header nor blank records, the same way on every read and on
/// replay. A refusal names the physical line its record starts on and the line of the defect,
/// counting blank lines and the line breaks inside quoted fields (Sources computes them; the
/// provider's own line numbers are not used).
/// </summary>
public sealed class RecordIndexTests
{
    private const string Text = "h1,h2\n\n1,\"a\r\nb\"\n  \n2,x\n";

    [Fact]
    public async Task Read_WhenAHeaderBlanksAndAMultilineFieldPrecedeRecords_ThenIndicesCountOnlyDataRecords()
    {
        var wide = await DrainAsync(WideSession(Opener(Text), hasHeader: true).ReadAsync());
        var triple = await DrainAsync(TripleSession(Opener(Text), hasHeader: true).ReadRowsAsync(new TripleColumns(0, 1, 0)));

        Assert.Equal(["0:[<1>,<a\\r\\nb>]", "1:[<2>,<x>]"], wide.Select(Render));
        Assert.Equal([0, 1], triple.Select(r => r.RecordIndex));
    }

    [Fact]
    public async Task Read_WhenReplayedThroughBoundAndUnboundRoutes_ThenRecordsAndIndicesAreIdentical()
    {
        var session = WideSession(Opener(Text), hasHeader: true);
        var first = (await DrainAsync(session.ReadAsync())).Select(Render).ToList();
        var second = (await DrainAsync(session.ReadAsync())).Select(Render).ToList();
        var bound = (await DrainAsync(new WideCsvSource(Opener(Text), WideBinding(hasHeader: true)).ReadAsync())).Select(Render).ToList();
        var tripleSession = (await DrainAsync(TripleSession(Opener(Text), hasHeader: true).ReadRowsAsync(new TripleColumns(0, 1, 0)))).Select(Render).ToList();
        var tripleBound = (await DrainAsync(new TripleCsvSource(Opener(Text), TripleBinding(hasHeader: true, columns: new TripleColumns(0, 1, 0))).ReadRowsAsync())).Select(Render).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first, bound);
        Assert.Equal(tripleSession, tripleBound);
        Assert.Equal(["0:[<1>,<a\\r\\nb>,<1>]", "1:[<2>,<x>,<2>]"], tripleSession);
    }

    [Theory]
    // CRLF, lone CR and lone LF each end one physical line; the blank lines count.
    [InlineData("ok\r\n\r\nx\"y\n", 1, 0, 3, 3)]
    [InlineData("ok\r\rx\"y\n", 1, 0, 3, 3)]
    // A quoted CRLF is one line break; the defect is on the field's last line.
    [InlineData("ok\n\"a\r\nb\"c\n", 1, 0, 2, 3)]
    // Quoted line breaks in an earlier column move the defect's line, not the start line.
    [InlineData("\"p\nq\nr\",x\"y\n", 0, 1, 1, 3)]
    public async Task Read_WhenARecordIsRefused_ThenItsStartLineAndDefectLineAreCountedBySources(
        string text, int record, int column, int startLine, int defectLine)
    {
        var refused = await Assert.ThrowsAsync<SourceReadException>(() => DrainAsync(WideSession(Opener(text)).ReadAsync()));

        Assert.Contains(
            $"data record {record}, column {column}, starting on line {startLine} (the defect is on line {defectLine})",
            refused.Message,
            StringComparison.Ordinal);
    }
}
