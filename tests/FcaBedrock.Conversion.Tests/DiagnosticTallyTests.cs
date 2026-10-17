namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// The aggregated-diagnostic tally (§16.4, D-059): an exact count of every occurrence and at most
/// three distinct examples in first-observed order, each quoted with the four escapes D-120 pins
/// for invalid-name samples. Expectations are written out literally so they cannot agree with the
/// tally by sharing its logic.
/// </summary>
public sealed class DiagnosticTallyTests
{
    [Fact]
    public void Record_WhenValuesRepeat_ThenTheCountIsEveryOccurrenceAndTheSampleIsTheFirstThreeDistinct()
    {
        var tally = RecordAll("a", "a", "b", "c", "d", "a");

        Assert.Equal(6, tally.Count);
        Assert.Equal("\"a\", \"b\", \"c\"", tally.Sample);
    }

    [Fact]
    public void Record_WhenAFourthDistinctValueArrives_ThenOnlyTheCountMoves()
    {
        var tally = RecordAll("a", "b", "c");
        Assert.Equal("\"a\", \"b\", \"c\"", tally.Sample);

        tally.Record("d");
        tally.Record("a");
        tally.Record("d");

        Assert.Equal(6, tally.Count);
        Assert.Equal("\"a\", \"b\", \"c\"", tally.Sample);
    }

    [Fact]
    public void Record_WhenADuplicateArrivesBeforeTheCapFills_ThenItTakesNoSlot()
    {
        var tally = RecordAll("a", "a", "a", "b");

        Assert.Equal(4, tally.Count);
        Assert.Equal("\"a\", \"b\"", tally.Sample);
    }

    [Fact]
    public void Record_WhenValuesDifferOnlyByCase_ThenBothAreKeptBecauseMembershipIsOrdinal()
    {
        var tally = RecordAll("a", "A", "a");

        Assert.Equal(3, tally.Count);
        Assert.Equal("\"a\", \"A\"", tally.Sample);
    }

    [Fact]
    public void Sample_WhenNothingWasRecorded_ThenTheCountIsZeroAndTheSampleIsEmpty()
    {
        var tally = new DiagnosticTally();

        Assert.Equal(0, tally.Count);
        Assert.Equal(string.Empty, tally.Sample);
    }

    // Each row is one value and its exact sample text. Only backslash, double quote, CR and LF are
    // escaped; every other character, other controls included, stays itself for the host's own pass.
    public static TheoryData<string, string> EscapedSamples() => new()
    {
        { "", "\"\"" },
        { "a, b", "\"a, b\"" },
        { "say \"hi\"", "\"say \\\"hi\\\"\"" },
        { "C:\\temp", "\"C:\\\\temp\"" },
        { "line\nbreak", "\"line\\nbreak\"" },
        { "carriage\rreturn", "\"carriage\\rreturn\"" },
        { "crlf\r\nend", "\"crlf\\r\\nend\"" },
        { "literal\\nescape", "\"literal\\\\nescape\"" },
        { "tab\there", "\"tab\there\"" },
        { "next" + (char)0x85 + "line", "\"next" + (char)0x85 + "line\"" },
        { "line" + (char)0x2028 + "sep", "\"line" + (char)0x2028 + "sep\"" },
        { "para" + (char)0x2029 + "sep", "\"para" + (char)0x2029 + "sep\"" },
        { "nul" + (char)0x01 + "ctl", "\"nul" + (char)0x01 + "ctl\"" },
    };

    [Theory]
    [MemberData(nameof(EscapedSamples))]
    public void Sample_WhenAValueNeedsQuoting_ThenItIsQuotedWithExactlyTheFourEscapes(string value, string expected)
    {
        var tally = RecordAll(value);

        Assert.Equal(1, tally.Count);
        Assert.Equal(expected, tally.Sample);
    }

    [Fact]
    public void Sample_WhenAnActualLineBreakAndALiteralBackslashNArrive_ThenTheyStayDistinct()
    {
        var tally = RecordAll("a\nb", "a\\nb", "a\nb");

        Assert.Equal(3, tally.Count);
        Assert.Equal("\"a\\nb\", \"a\\\\nb\"", tally.Sample);
    }

    [Fact]
    public void RecordRename_WhenPairsArrive_ThenEachSampleQuotesTheKeyAndTheNameAndRepeatsTakeNoSlot()
    {
        var tally = new DiagnosticTally();

        tally.RecordRename("P001", "P001#2#1");
        tally.RecordRename("P001", "P001#2#1");
        tally.RecordRename("a\"b", "a\"b#1");

        Assert.Equal(3, tally.Count);
        Assert.Equal("\"P001\"→\"P001#2#1\", \"a\\\"b\"→\"a\\\"b#1\"", tally.Sample);
    }

    private static DiagnosticTally RecordAll(params string[] values)
    {
        var tally = new DiagnosticTally();
        foreach (var value in values)
        {
            tally.Record(value);
        }

        return tally;
    }
}
