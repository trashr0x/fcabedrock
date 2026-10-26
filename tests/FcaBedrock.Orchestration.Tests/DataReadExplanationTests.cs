using System.Text;
using FcaBedrock.Sources;

namespace FcaBedrock.Orchestration.Tests;

/// <summary>
/// The DATA read explanation at its own seam: each closed reason, every partial position, each
/// byte order mark, each refused open, and the generic sentence for everything without closed
/// detail. No exception's own message ever reaches the result.
/// </summary>
public sealed class DataReadExplanationTests
{
    private const string Data = "in.csv";

    public static TheoryData<string, SourceReadException, string> ClosedReasons() => new()
    {
        {
            "a data record with every coordinate",
            new SourceReadException(SourceReadFailureReason.QuoteInUnquotedField, new SourceReadPosition(SourceReadRecordKind.Data, 4, 2, 7, 9), null),
            "cannot read the data file 'in.csv': a quote appears inside an unquoted field (data record 4, field 2, counted "
            + "from 0; record starts on line 7, defect on line 9). Quote the whole field and double each quote inside it."
        },
        {
            "the header record without a field",
            new SourceReadException(SourceReadFailureReason.TextAfterClosingQuote, new SourceReadPosition(SourceReadRecordKind.Header, recordStartLine: 1, defectLine: 1), null),
            "cannot read the data file 'in.csv': text follows the closing quote of a quoted field (the header record; line "
            + "1). End the field right after its closing quote, or double each quote inside it."
        },
        {
            "an unknown record with only a start line",
            new SourceReadException(SourceReadFailureReason.UnterminatedQuote, new SourceReadPosition(SourceReadRecordKind.Unknown, recordStartLine: 12), null),
            "cannot read the data file 'in.csv': a quoted field is not closed before the end of the input (record starts on "
            + "line 12). Add the missing closing quote, or double each quote inside the field."
        },
        {
            "an unknown record with only a defect line and a field",
            new SourceReadException(SourceReadFailureReason.UnterminatedQuote, new SourceReadPosition(SourceReadRecordKind.Unknown, fieldIndex: 0, defectLine: 3), null),
            "cannot read the data file 'in.csv': a quoted field is not closed before the end of the input (field 0, counted "
            + "from 0; defect on line 3). Add the missing closing quote, or double each quote inside the field."
        },
        {
            "the reader limit, which has no position",
            new SourceReadException(SourceReadFailureReason.ReaderLimit, null, new NotSupportedException("SECRET")),
            "cannot read the data file 'in.csv': a record exceeded the reader's row and buffer limit of about 16 million "
            + "UTF-16 code units. Look for a quoted field that is never closed."
        },
        {
            "a position with no coordinates",
            new SourceReadException(SourceReadFailureReason.QuoteInUnquotedField, default(SourceReadPosition), null),
            "cannot read the data file 'in.csv': a quote appears inside an unquoted field. Quote the whole field and double "
            + "each quote inside it."
        },
    };

    [Theory]
    [MemberData(nameof(ClosedReasons))]
    public void Message_WhenTheReasonIsClosed_ThenItStatesTheReasonOnlyTheKnownPositionAndTheFix(
        string what, SourceReadException cause, string expected) =>
        Assert.True(expected == DataReadExplanation.Message(Data, cause, DataByteOrderMark.None), what);

    [Theory]
    [InlineData(nameof(DataByteOrderMark.None), "it is not valid UTF-8. Save it as UTF-8.")]
    [InlineData(nameof(DataByteOrderMark.Utf16LittleEndian), "it starts with a UTF-16 little-endian byte order mark, but data must be UTF-8. Save it as UTF-8.")]
    [InlineData(nameof(DataByteOrderMark.Utf16BigEndian), "it starts with a UTF-16 big-endian byte order mark, but data must be UTF-8. Save it as UTF-8.")]
    [InlineData(nameof(DataByteOrderMark.Utf32LittleEndian), "it starts with a UTF-32 little-endian byte order mark, but data must be UTF-8. Save it as UTF-8.")]
    [InlineData(nameof(DataByteOrderMark.Utf32BigEndian), "it starts with a UTF-32 big-endian byte order mark, but data must be UTF-8. Save it as UTF-8.")]
    [InlineData(nameof(DataByteOrderMark.Utf16OrUtf32), "it starts with a UTF-16 or UTF-32 byte order mark, but data must be UTF-8. Save it as UTF-8.")]
    public void Message_WhenTheDecoderRefusedTheBytes_ThenItNamesTheFailingPassesMarkOrInvalidUtf8(string mark, string explanation) =>
        Assert.Equal(
            "cannot read the data file 'in.csv': " + explanation,
            DataReadExplanation.Message(Data, new DecoderFallbackException("SECRET bytes [FF] at index 3"), Enum.Parse<DataByteOrderMark>(mark)));

    public static TheoryData<Exception, string> RefusedOpens() => new()
    {
        { new FileNotFoundException("SECRET path"), "it does not exist. Check the path." },
        { new DirectoryNotFoundException("SECRET path"), "it does not exist. Check the path." },
        { new UnauthorizedAccessException("SECRET path"), "access to it was denied. Check that it is a file you can read." },
    };

    [Theory]
    [MemberData(nameof(RefusedOpens))]
    public void Message_WhenTheOpenWasRefused_ThenItSaysWhyFromTheExceptionTypeAlone(Exception cause, string explanation)
    {
        // A mark explains only a decoding failure, so it never changes an open's explanation.
        foreach (var mark in Enum.GetValues<DataByteOrderMark>())
        {
            Assert.Equal("cannot read the data file 'in.csv': " + explanation, DataReadExplanation.Message(Data, cause, mark));
        }
    }

    public static TheoryData<Exception> GenericFailures()
    {
        var data = new TheoryData<Exception>();
        data.Add(new SourceReadException("SECRET free text", new IOException("SECRET inner")));
        data.Add(new SourceReadException(SourceReadFailureReason.Unknown, new SourceReadPosition(SourceReadRecordKind.Data, 1), null));
        data.Add(new IOException("SECRET device text"));
        data.Add(new PathTooLongException("SECRET"));
        data.Add(new InvalidDataException("SECRET"));
        data.Add(new ObjectDisposedException("SECRET"));
        return data;
    }

    [Theory]
    [MemberData(nameof(GenericFailures))]
    public void Message_WhenNoClosedDetailApplies_ThenItIsTheGenericSentenceWhateverTheMark(Exception cause)
    {
        // A mark explains only a decoding failure; it is never attached to another failure.
        foreach (var mark in Enum.GetValues<DataByteOrderMark>())
        {
            Assert.Equal(RunPipeline.DataReadMessage(Data), DataReadExplanation.Message(Data, cause, mark));
        }
    }
}
