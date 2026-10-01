using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Cancellation through the public reading API (spec §5.1.1): a cancelled read ends with
/// <see cref="OperationCanceledException"/> carrying the operation's token, never a read failure,
/// and exposes no further record or schema once cancellation is observed, including records the
/// provider already holds in its buffer and the record whose bytes were being read when the token
/// was cancelled. Every stream is closed exactly once. A read blocked in the caller's stream is not
/// interrupted. The checkpoints inside long records are covered directly in
/// <c>CsvReadPipelineCancellationTests</c> and <c>DelimitedFieldGrammarCancellationTests</c>,
/// because no stream schedule can place a cancellation inside them deterministically.
/// </summary>
public sealed class SourceCancellationTests
{
    public static TheoryData<string, string> LongSecondRecords() => new()
    {
        { "many fields", new string(',', 299_999) + "\n" },
        { "one long quoted field", "\"" + new string('x', 300_000) + "\"\n" },
        { "one long unquoted field", new string('x', 300_000) + "\n" },
        { "a long blank record before a record", new string(' ', 300_000) + "\nc,d\n" },
    };

    [Theory]
    [MemberData(nameof(LongSecondRecords))]
    public async Task Read_WhenCancelledWhileTheLastBytesOfALaterRecordArrive_ThenThatRecordIsNeverExposed(string label, string second)
    {
        using var cts = new CancellationTokenSource();
        var stream = new ScriptedStream(Utf8("a,b\n" + second)) { CancelAtEnd = cts };

        var (records, error) = await DrainCapturingAsync(WideSession(() => stream).ReadAsync(cts.Token));

        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.True(cts.Token == canceled.CancellationToken, label);
        Assert.Single(records);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task ReadRows_WhenCancelledWhileTheLastBytesOfALaterRowArrive_ThenThatRowIsNeverExposed()
    {
        using var cts = new CancellationTokenSource();
        var stream = new ScriptedStream(Utf8("s,p,v\ns2,p2," + new string('v', 300_000) + "\n")) { CancelAtEnd = cts };

        var (rows, error) = await DrainCapturingAsync(TripleSession(() => stream).ReadRowsAsync(new TripleColumns(0, 1, 2), cts.Token));

        Assert.Equal(cts.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.Single(rows);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenCancelledDuringTheFirstFill_ThenNoRecordIsExposed()
    {
        // The whole input arrives in the provider's first fill, while the read is being opened.
        using var cts = new CancellationTokenSource();
        var stream = new ScriptedStream(Utf8("a,b\nc,d\n")) { CancelAtEnd = cts };

        var (records, error) = await DrainCapturingAsync(WideSession(() => stream).ReadAsync(cts.Token));

        Assert.Equal(cts.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.Empty(records);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Schema_WhenCancelledWhileALongHeaderArrives_ThenNoSchemaIsReturnedOrCached()
    {
        using var cts = new CancellationTokenSource();
        var opens = 0;
        var session = new WideCsvSession(
            () => ++opens == 1
                ? new ScriptedStream(Utf8(new string(',', 299_999) + "\n1\n")) { CancelAtEnd = cts }
                : ScriptedStream.Utf8("a,b\n"),
            SourceReadSettings.CreateWide());

        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.GetSchemaAsync(cts.Token));
        var schema = await session.GetSchemaAsync();

        Assert.Equal(cts.Token, canceled.CancellationToken);
        Assert.Equal(["a", "b"], schema.Header);
    }

    [Fact]
    public async Task Read_WhenTheConsumerCancelsWhileLaterRecordsAreAlreadyBuffered_ThenNoneOfThemIsExposed()
    {
        using var cts = new CancellationTokenSource();
        var stream = ScriptedStream.Utf8("a,b\nc,d\ne,f\n");
        var readsWhenCancelled = -1;

        var (records, error) = await DrainCapturingAsync(
            WideSession(() => stream).ReadAsync(cts.Token),
            afterItem: _ =>
            {
                readsWhenCancelled = stream.ReadCalls;
                cts.Cancel();
            });

        Assert.Equal(cts.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.Single(records);
        Assert.Equal(readsWhenCancelled, stream.ReadCalls);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task Read_WhenAlreadyCancelled_ThenTheStreamIsNeverOpened()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var opens = 0;

        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DrainAsync(WideSession(() => { opens++; return ScriptedStream.Utf8("a\n"); }).ReadAsync(cts.Token)));

        Assert.Equal(cts.Token, canceled.CancellationToken);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task Read_WhenCancelledWhileTheCallersStreamIsBlocked_ThenTheReadIsNotInterruptedAndNothingMoreIsRead()
    {
        // Four-byte reads make the decoder go back to the stream within the provider's first fill; the
        // second stream read blocks until the test releases it.
        using var cts = new CancellationTokenSource();
        var stream = new ScriptedStream(Utf8("a,b\nc,d\ne,f\n")) { MaxChunk = 4, BlockOnRead = 1 };
        var drain = Task.Run(() => DrainCapturingAsync(WideSession(() => stream).ReadAsync(cts.Token)));

        Assert.True(stream.Entered.Wait(TimeSpan.FromSeconds(30)));
        cts.Cancel();
        var stillRunning = !drain.IsCompleted;
        stream.Release.Set();
        var (records, error) = await drain;

        Assert.True(stillRunning);
        Assert.Equal(cts.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        Assert.Empty(records);
        Assert.Equal(2, stream.ReadCalls);
        Assert.Equal(1, stream.DisposeCount);
    }
}
