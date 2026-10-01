using nietras.SeparatedValues;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The whole-span reader's contract (D-137), driven directly over a scripted decoder: it fills each
/// requested span in full or to the end of input, never transforms or reorders characters, owns
/// nothing, checks the operation's token before and after every decoder read, lets a decoder
/// failure win over a cancellation observed after it, never returns partial success after a later
/// failure, and refuses a decoder count outside the requested span.
/// </summary>
public sealed class WholeSpanTextReaderTests
{
    // NUL, CR, LF, a two-byte character and a surrogate pair, so no unit is special-cased.
    private static readonly string Text = "ab\rc\nd" + (char)0xE9 + char.ConvertFromUtf32(0x1F600) + "\0z";

    private static string Fill(TextReader reader, int size, out int count)
    {
        var buffer = new char[size];
        count = reader.Read(buffer.AsSpan());
        return new string(buffer, 0, count);
    }

    [Fact]
    public void Read_WhenTheRequestIsEmpty_ThenNothingIsReadAndNothingIsChecked()
    {
        var inner = new ScriptedTextReader(Text);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Equal(0, new WholeSpanTextReader(inner, cts.Token).Read(Span<char>.Empty));
        Assert.Equal(0, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenTheDecoderReturnsOneUnitAtATime_ThenTheRequestIsFilledInOrder()
    {
        var inner = new ScriptedTextReader(Text) { Chunk = (_, _) => 1 };

        var all = Fill(new WholeSpanTextReader(inner, CancellationToken.None), Text.Length, out var count);

        Assert.Equal(Text.Length, count);
        Assert.Equal(Text, all);
        Assert.Equal(Text.Length, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenTheDecoderReturnsIrregularShortReads_ThenTheRequestIsFilledExactly()
    {
        var sizes = new[] { 3, 1, 4, 2 };
        var inner = new ScriptedTextReader(Text) { Chunk = (call, _) => sizes[call % sizes.Length] };

        var all = Fill(new WholeSpanTextReader(inner, CancellationToken.None), Text.Length, out var count);

        Assert.Equal(Text, all);
        Assert.Equal(Text.Length, count);
        Assert.Equal(5, inner.SpanCalls); // 3 + 1 + 4 + 2 + 1 of the 11 units
    }

    [Fact]
    public void Read_WhenTheDecoderFillsTheRequest_ThenOneDecoderReadSuffices()
    {
        var inner = new ScriptedTextReader(Text);

        Assert.Equal(Text, Fill(new WholeSpanTextReader(inner, CancellationToken.None), Text.Length, out _));
        Assert.Equal(1, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenTheInputIsEmpty_ThenEveryReadReturnsZeroAfterAskingTheDecoder()
    {
        var inner = new ScriptedTextReader(string.Empty);
        var reader = new WholeSpanTextReader(inner, CancellationToken.None);

        Assert.Equal(0, reader.Read(new char[8].AsSpan()));
        Assert.Equal(0, reader.Read(new char[8].AsSpan()));
        Assert.Equal(2, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenLessRemainsThanRequested_ThenWhatRemainsIsReturnedOnlyAfterTheDecoderReportsTheEnd()
    {
        var inner = new ScriptedTextReader("abcde") { Chunk = (_, _) => 2 };
        var reader = new WholeSpanTextReader(inner, CancellationToken.None);

        var part = Fill(reader, 8, out var count);
        var callsForTheFirstRequest = inner.SpanCalls;
        var after = reader.Read(new char[8].AsSpan());

        Assert.Equal("abcde", part);
        Assert.Equal(5, count);
        Assert.Equal(4, callsForTheFirstRequest); // 2 + 2 + 1, then 0
        Assert.Equal(0, after);
        Assert.Equal(5, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenTheArrayArgumentsAreInvalid_ThenTheBaseContractRefusesThemBeforeAnyDecoderRead()
    {
        var inner = new ScriptedTextReader(Text);
        var reader = new WholeSpanTextReader(inner, CancellationToken.None);

        Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => reader.Read(null!, 0, 0)).ParamName);
        Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(new char[4], -1, 1)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(new char[4], 0, -1)).ParamName);
        Assert.Throws<ArgumentException>(() => reader.Read(new char[4], 2, 3));
        Assert.Equal(0, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenAnArraySliceIsRequested_ThenOnlyTheSliceIsWritten()
    {
        var reader = new WholeSpanTextReader(new ScriptedTextReader(Text), CancellationToken.None);
        var buffer = new char[Text.Length + 2];

        var count = reader.Read(buffer, 1, Text.Length);

        Assert.Equal(Text.Length, count);
        Assert.Equal(Text, new string(buffer, 1, Text.Length));
        Assert.Equal('\0', buffer[0]);
        Assert.Equal('\0', buffer[^1]);
    }

    [Fact]
    public void ReadAndPeek_WhenUnitsAreReadOneByOne_ThenNulIsACharacterAndTheEndIsMinusOneRepeatedly()
    {
        var reader = new WholeSpanTextReader(new ScriptedTextReader("\0\r\n" + (char)0xE9 + char.ConvertFromUtf32(0x1F600)), CancellationToken.None);

        var peeked = reader.Peek();
        var units = new List<int>();
        int unit;
        while ((unit = reader.Read()) != -1)
        {
            units.Add(unit);
        }

        Assert.Equal(0, peeked);
        Assert.Equal([0, 13, 10, 0xE9, 0xD83D, 0xDE00], units);
        Assert.Equal(-1, reader.Read());
        Assert.Equal(-1, reader.Peek());
    }

    [Fact]
    public void Dispose_WhenTheReaderOrTheProviderOverItIsDisposed_ThenTheDecoderIsNotDisposed()
    {
        var inner = new ScriptedTextReader("a,b\nc,d\n");
        var reader = new WholeSpanTextReader(inner, CancellationToken.None);
        using (var sep = DelimitedSourceReader.ProviderOptions(',').From(reader, leaveOpen: true))
        {
            while (sep.MoveNext())
            {
            }
        }

        reader.Dispose();
        reader.Close();

        Assert.Equal(0, inner.DisposeCalls);
    }

    [Fact]
    public void Read_WhenTheDecoderFailsAfterAnEarlierFragment_ThenTheFailurePropagatesAsItselfWithNoPartialSuccess()
    {
        var failure = new IOException("decoder failed");
        var inner = new ScriptedTextReader("abcdefgh") { Chunk = (_, _) => 3, ThrowOnCall = 1, Failure = failure };

        var thrown = Assert.Throws<IOException>(() => Fill(new WholeSpanTextReader(inner, CancellationToken.None), 8, out _));

        Assert.Same(failure, thrown);
        Assert.Equal(2, inner.SpanCalls);
    }

    [Theory]
    [InlineData(3, 0, 1)] // cancelled during a short read: observed after it, before the next read
    [InlineData(3, 1, 2)] // cancelled during a later short read
    [InlineData(4, 1, 2)] // cancelled during the read that fills the request: still no success
    public void Read_WhenCancelledDuringADecoderRead_ThenTheCancellationIsObservedRightAfterIt(int chunk, int cancelOnCall, int expectedCalls)
    {
        using var cts = new CancellationTokenSource();
        var inner = new ScriptedTextReader("abcdefgh") { Chunk = (_, _) => chunk, CancelOnCall = cancelOnCall, Cancel = cts };

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => Fill(new WholeSpanTextReader(inner, cts.Token), 8, out _));

        Assert.Equal(cts.Token, canceled.CancellationToken);
        Assert.Equal(expectedCalls, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenCancelledDuringTheReadThatReportsTheEnd_ThenTheCancellationIsObserved()
    {
        using var cts = new CancellationTokenSource();
        var inner = new ScriptedTextReader("abc") { CancelOnCall = 1, Cancel = cts };

        var canceled = Assert.ThrowsAny<OperationCanceledException>(() => Fill(new WholeSpanTextReader(inner, cts.Token), 8, out _));

        Assert.Equal(cts.Token, canceled.CancellationToken);
        Assert.Equal(2, inner.SpanCalls);
    }

    [Fact]
    public void Read_WhenAlreadyCancelled_ThenNoDecoderReadHappens()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var inner = new ScriptedTextReader("abc");
        var reader = new WholeSpanTextReader(inner, cts.Token);

        Assert.Equal(cts.Token, Assert.ThrowsAny<OperationCanceledException>(() => Fill(reader, 8, out _)).CancellationToken);
        Assert.Equal(cts.Token, Assert.ThrowsAny<OperationCanceledException>(() => reader.Read()).CancellationToken);
        Assert.Equal(cts.Token, Assert.ThrowsAny<OperationCanceledException>(() => reader.Peek()).CancellationToken);
        Assert.Equal(0, inner.SpanCalls);
        Assert.Equal(0, inner.ScalarCalls);
        Assert.Equal(0, inner.PeekCalls);
    }

    [Fact]
    public void Read_WhenCancelledDuringAScalarRead_ThenTheCancellationIsObservedAfterIt()
    {
        using var cts = new CancellationTokenSource();
        var inner = new ScriptedTextReader("abc") { CancelOnCall = 0, Cancel = cts };

        Assert.Equal(cts.Token, Assert.ThrowsAny<OperationCanceledException>(() => new WholeSpanTextReader(inner, cts.Token).Read()).CancellationToken);
        Assert.Equal(1, inner.ScalarCalls);
    }

    [Fact]
    public void Read_WhenADecoderFailureAndACancellationHappenInTheSameRead_ThenTheFailureWins()
    {
        using var cts = new CancellationTokenSource();
        var failure = new IOException("decoder failed");
        var inner = new ScriptedTextReader("abcdefgh") { Chunk = (_, _) => 3, CancelOnCall = 1, Cancel = cts, ThrowOnCall = 1, Failure = failure };

        Assert.Same(failure, Assert.Throws<IOException>(() => Fill(new WholeSpanTextReader(inner, cts.Token), 8, out _)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(5)] // 4 of 8 already read, so 4 remain
    public void Read_WhenTheDecoderReportsACountOutsideTheRequestedSpan_ThenInvalidOperation(int badCount)
    {
        var inner = new ScriptedTextReader("abcdefgh") { Chunk = (_, _) => 4, BadCountOnCall = 1, BadCount = badCount };

        Assert.Throws<InvalidOperationException>(() => new WholeSpanTextReader(inner, CancellationToken.None).Read(new char[8].AsSpan()));
        Assert.Equal(2, inner.SpanCalls);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(0x10000)]
    public void ReadAndPeek_WhenTheDecoderReturnsNeitherACharacterNorTheEnd_ThenInvalidOperation(int badValue)
    {
        var inner = new ScriptedTextReader("abc") { BadScalar = badValue };
        var reader = new WholeSpanTextReader(inner, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => reader.Read());
        Assert.Throws<InvalidOperationException>(() => reader.Peek());
    }

    [Fact]
    public void InheritedMembers_WhenReadLineReadBlockAndReadToEndAreUsed_ThenTheyStayCoherent()
    {
        var sizes = new[] { 2, 1, 3 };
        var reader = new WholeSpanTextReader(
            new ScriptedTextReader("line1\r\nline2\nend") { Chunk = (call, _) => sizes[call % sizes.Length] }, CancellationToken.None);

        var line = reader.ReadLine();
        var block = new char[4];
        var blockCount = reader.ReadBlock(block.AsSpan());
        var rest = reader.ReadToEnd();

        Assert.Equal("line1", line);
        Assert.Equal(4, blockCount);
        Assert.Equal("line", new string(block));
        Assert.Equal("2\nend", rest);
    }

    /// <summary>
    /// A reader over a string whose span reads follow a chunk schedule (short reads that still
    /// return at least one character while input remains). It can throw on a call, cancel a token
    /// during a call, return a count or a scalar outside the contract, and counts every member call.
    /// </summary>
    internal sealed class ScriptedTextReader(string text) : TextReader
    {
        private int _position;

        public Func<int, int, int>? Chunk { get; init; }

        public int ThrowOnCall { get; init; } = -1;

        public Exception? Failure { get; init; }

        public int CancelOnCall { get; init; } = -1;

        public CancellationTokenSource? Cancel { get; init; }

        public int BadCountOnCall { get; init; } = -1;

        public int BadCount { get; init; }

        public int? BadScalar { get; init; }

        public int SpanCalls { get; private set; }

        public int ScalarCalls { get; private set; }

        public int PeekCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public override int Read(Span<char> buffer)
        {
            var call = SpanCalls++;
            if (call == CancelOnCall)
            {
                Cancel!.Cancel();
            }

            if (call == ThrowOnCall)
            {
                throw Failure!;
            }

            if (call == BadCountOnCall)
            {
                return BadCount;
            }

            var n = Math.Min(buffer.Length, text.Length - _position);
            if (Chunk is { } chunk && n > 0)
            {
                n = Math.Min(n, Math.Max(1, chunk(call, buffer.Length)));
            }

            text.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));

        public override int Read()
        {
            var call = ScalarCalls++;
            if (call == CancelOnCall)
            {
                Cancel!.Cancel();
            }

            if (BadScalar is { } bad)
            {
                return bad;
            }

            return _position < text.Length ? text[_position++] : -1;
        }

        public override int Peek()
        {
            var call = PeekCalls++;
            if (call == CancelOnCall)
            {
                Cancel!.Cancel();
            }

            if (BadScalar is { } bad)
            {
                return bad;
            }

            return _position < text.Length ? text[_position] : -1;
        }

        protected override void Dispose(bool disposing) => DisposeCalls++;
    }
}
