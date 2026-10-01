using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The reader's record length limit (spec §5.1.1: about 16 million UTF-16 code units for one
/// record or the buffer holding it) is a read failure with one invariant message, whether it is
/// reached while the first record is read or later, and a quoted field that is never closed can run
/// into it. Only the provider's own refusal is recognized as the limit: a caller stream's
/// <see cref="NotSupportedException"/> stays itself.
/// </summary>
public sealed class SourceLimitTests
{
    private const string LimitMessage =
        "The source could not be read: a record exceeded the reader's row and buffer limit of about "
        + "16 million UTF-16 code units. A quoted field that is never closed can run into this limit.";

    [Fact]
    public async Task Read_WhenAQuoteIsNeverClosedPastTheLimit_ThenTheLimitIsAReadFailureCarryingTheProvidersException()
    {
        var (records, error) = await DrainCapturingAsync(
            WideSession(Opener("a,b\n\"" + new string('x', 20 * 1024 * 1024))).ReadAsync());

        Assert.Single(records);
        var failure = Assert.IsType<SourceReadException>(error);
        Assert.Equal(LimitMessage, failure.Message);
        Assert.IsType<NotSupportedException>(failure.InnerException);
    }

    [Fact]
    public async Task Read_WhenTheCallersStreamThrowsNotSupported_ThenItPropagatesAsItselfAndTheStreamIsClosedOnce()
    {
        var failure = new NotSupportedException("the caller's stream");
        var stream = new ScriptedStream(Utf8("a,b\n")) { ReadFailure = failure };

        var thrown = await Assert.ThrowsAsync<NotSupportedException>(() => DrainAsync(WideSession(() => stream).ReadAsync()));

        Assert.Same(failure, thrown);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenARecordIsLongButBelowTheLimit_ThenItIsReadExactly()
    {
        var value = new string('v', 3_000_000);

        var records = await DrainAsync(WideSession(Opener("a,\"" + value + "\"\nb,c\n")).ReadAsync());

        Assert.Equal(2, records.Count);
        Assert.Equal(value, records[0].Field(1));
        Assert.Equal("c", records[1].Field(1));
    }
}
