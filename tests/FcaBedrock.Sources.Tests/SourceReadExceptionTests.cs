using System.Reflection;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The public read-failure contract (EP-4): the closed reason and record kind, the position value
/// and its validation, the one detail constructor, and the unchanged free-text constructors, whose
/// existing calls must keep binding where they did and carry no detail.
/// </summary>
public sealed class SourceReadExceptionTests
{
    [Fact]
    public void Reason_WhenInspected_ThenItIsTheClosedSetWithUnknownAsZero()
    {
        Assert.Equal(
            ["Unknown", "QuoteInUnquotedField", "TextAfterClosingQuote", "UnterminatedQuote", "ReaderLimit"],
            Enum.GetNames<SourceReadFailureReason>());
        Assert.Equal(0, (int)SourceReadFailureReason.Unknown);
        Assert.Equal(["Unknown", "Header", "Data"], Enum.GetNames<SourceReadRecordKind>());
        Assert.Equal(0, (int)SourceReadRecordKind.Unknown);
    }

    [Fact]
    public void FreeTextConstructors_WhenCalledAsBefore_ThenTheyKeepTheirMessageAndCauseAndCarryNoDetail()
    {
        var inner = new IOException("inner");
        SourceReadException[] failures =
        [
            new(),
            new("free text"),
            new("free text", inner),
            new(null!),
            new(null!, null!),
            new(default(string)!),
        ];

        Assert.All(failures, f => Assert.Equal(SourceReadFailureReason.Unknown, f.Reason));
        Assert.All(failures, f => Assert.Null(f.Position));
        // Without a message the runtime's default names the type, for a null message too.
        Assert.Contains(typeof(SourceReadException).FullName!, failures[0].Message, StringComparison.Ordinal);
        Assert.Equal(failures[0].Message, failures[3].Message);
        Assert.Equal("free text", failures[1].Message);
        Assert.Null(failures[1].InnerException);
        Assert.Equal("free text", failures[2].Message);
        Assert.Same(inner, failures[2].InnerException);
        Assert.Null(failures[4].InnerException);
    }

    [Fact]
    public void Constructors_WhenInspected_ThenTheDetailConstructorIsTheOnlyNewOneAndTakesThreeRequiredArguments()
    {
        var constructors = typeof(SourceReadException).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(
            [
                "",
                "FcaBedrock.Sources.SourceReadFailureReason, System.Nullable`1[FcaBedrock.Sources.SourceReadPosition], System.Exception",
                "System.String",
                "System.String, System.Exception",
            ],
            constructors.Select(c => string.Join(", ", c.GetParameters().Select(p => p.ParameterType.ToString()))).Order(StringComparer.Ordinal));
        Assert.All(constructors.SelectMany(c => c.GetParameters()), p => Assert.False(p.IsOptional));
    }

    [Fact]
    public void DetailConstructor_WhenGivenAReasonAndAPosition_ThenItCarriesBothAndAControlledMessage()
    {
        var inner = new IOException("SECRET");

        var failure = new SourceReadException(
            SourceReadFailureReason.UnterminatedQuote, new SourceReadPosition(SourceReadRecordKind.Data, 1, 0, 3, 4), inner);

        Assert.Equal(SourceReadFailureReason.UnterminatedQuote, failure.Reason);
        Assert.Equal(new SourceReadPosition(SourceReadRecordKind.Data, 1, 0, 3, 4), failure.Position);
        Assert.Same(inner, failure.InnerException);
        Assert.Equal(
            "The source could not be read: data record 1, column 0, starting on line 3 (the defect is on line 4): "
            + "a quoted field is not closed before the end of the input.",
            failure.Message);
    }

    public static TheoryData<SourceReadFailureReason, SourceReadPosition?, string> ControlledMessages() => new()
    {
        { SourceReadFailureReason.Unknown, null, "The source could not be read." },
        { SourceReadFailureReason.Unknown, default(SourceReadPosition), "The source could not be read." },
        {
            SourceReadFailureReason.ReaderLimit, null,
            "The source could not be read: a record exceeded the reader's row and buffer limit of about 16 million "
            + "UTF-16 code units. A quoted field that is never closed can run into this limit."
        },
        {
            SourceReadFailureReason.QuoteInUnquotedField, new SourceReadPosition(SourceReadRecordKind.Header, null, 2, 1, 1),
            "The source could not be read: the header record, column 2, starting on line 1 (the defect is on line 1): "
            + "a quote appears inside an unquoted field."
        },
        {
            SourceReadFailureReason.TextAfterClosingQuote, new SourceReadPosition(SourceReadRecordKind.Unknown, defectLine: 7),
            "The source could not be read: the defect is on line 7: text follows the closing quote of a quoted field."
        },
    };

    [Theory]
    [MemberData(nameof(ControlledMessages))]
    public void DetailConstructor_WhenOnlySomeDetailIsKnown_ThenTheMessageStatesOnlyThat(
        SourceReadFailureReason reason, SourceReadPosition? position, string message) =>
        Assert.Equal(message, new SourceReadException(reason, position, null).Message);

    [Fact]
    public void DetailConstructor_WhenTheReasonIsUndefined_ThenItIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceReadException((SourceReadFailureReason)99, null, null));

    [Fact]
    public void Position_WhenDefault_ThenItIsAnUnknownRecordWithNoCoordinates()
    {
        var position = default(SourceReadPosition);

        Assert.Equal(SourceReadRecordKind.Unknown, position.RecordKind);
        Assert.Null(position.DataRecordIndex);
        Assert.Null(position.FieldIndex);
        Assert.Null(position.RecordStartLine);
        Assert.Null(position.DefectLine);
        Assert.Equal(position, new SourceReadPosition(SourceReadRecordKind.Unknown));
    }

    [Fact]
    public void Position_WhenInspected_ThenEveryMemberIsGetOnly()
    {
        var properties = typeof(SourceReadPosition).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.True(typeof(SourceReadPosition).IsValueType);
        Assert.Equal(
            ["DataRecordIndex", "DefectLine", "FieldIndex", "RecordKind", "RecordStartLine"],
            properties.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    [Fact]
    public void Position_WhenItsFactsAreConsistent_ThenItKeepsEachOne()
    {
        // Each coordinate other than the data record index is independently optional.
        var data = new SourceReadPosition(SourceReadRecordKind.Data, 5, 0, 1, 1);
        var header = new SourceReadPosition(SourceReadRecordKind.Header, fieldIndex: 3, recordStartLine: 2, defectLine: 4);
        var lineOnly = new SourceReadPosition(SourceReadRecordKind.Unknown, recordStartLine: 9);
        var defectOnly = new SourceReadPosition(SourceReadRecordKind.Unknown, defectLine: 9);

        Assert.Equal((SourceReadRecordKind.Data, 5, 0, 1L, 1L), (data.RecordKind, data.DataRecordIndex, data.FieldIndex, data.RecordStartLine, data.DefectLine));
        Assert.Equal((SourceReadRecordKind.Header, (int?)null, 3, 2L, 4L), (header.RecordKind, header.DataRecordIndex, header.FieldIndex, header.RecordStartLine, header.DefectLine));
        Assert.Equal((9L, (long?)null), (lineOnly.RecordStartLine, lineOnly.DefectLine));
        Assert.Equal(((long?)null, 9L), (defectOnly.RecordStartLine, defectOnly.DefectLine));
        Assert.Equal(0, new SourceReadPosition(SourceReadRecordKind.Data, 0).DataRecordIndex);
        Assert.Equal(1, new SourceReadPosition(SourceReadRecordKind.Unknown, fieldIndex: 1).FieldIndex);
    }

    public static TheoryData<string, Func<SourceReadPosition>, Type> RefusedPositions() => new()
    {
        { "undefined kind", () => new SourceReadPosition((SourceReadRecordKind)7), typeof(ArgumentOutOfRangeException) },
        { "data without an index", () => new SourceReadPosition(SourceReadRecordKind.Data), typeof(ArgumentException) },
        { "data with a negative index", () => new SourceReadPosition(SourceReadRecordKind.Data, -1), typeof(ArgumentOutOfRangeException) },
        { "header with an index", () => new SourceReadPosition(SourceReadRecordKind.Header, 0), typeof(ArgumentException) },
        { "unknown with an index", () => new SourceReadPosition(SourceReadRecordKind.Unknown, 0), typeof(ArgumentException) },
        { "negative field", () => new SourceReadPosition(SourceReadRecordKind.Data, 0, -1), typeof(ArgumentOutOfRangeException) },
        { "start line zero", () => new SourceReadPosition(SourceReadRecordKind.Data, 0, recordStartLine: 0), typeof(ArgumentOutOfRangeException) },
        { "defect line zero", () => new SourceReadPosition(SourceReadRecordKind.Unknown, defectLine: 0), typeof(ArgumentOutOfRangeException) },
        { "defect before start", () => new SourceReadPosition(SourceReadRecordKind.Data, 0, 0, 5, 4), typeof(ArgumentException) },
    };

    [Theory]
    [MemberData(nameof(RefusedPositions))]
    public void Position_WhenItsFactsAreInconsistent_ThenItIsRefused(string label, Func<SourceReadPosition> create, Type expected)
    {
        _ = label;

        var thrown = Assert.ThrowsAny<ArgumentException>(() => create());

        Assert.Equal(expected, thrown.GetType());
    }
}
