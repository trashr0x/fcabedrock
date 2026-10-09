namespace FcaBedrock.Sources;

/// <summary>
/// Hands the pinned Sep 0.17.1 reader every character span it requests in full, reading the
/// Sources-owned decoder as many times as needed, so that a request comes back short only at the
/// end of the input.
/// <para>
/// <b>Why it exists.</b> When one of Sep's reads returns fewer characters than requested and the
/// last of them is a carriage return, Sep reads one more character to see whether a line feed
/// follows. If that character is another carriage return, Sep sets it aside, keeps filling its
/// buffer from the reader, and restores the set-aside carriage return only at the start of its next
/// fill: after everything read in between. Legal input then changes: a quoted value loses a
/// carriage return and a later record gains one, and no field validation can notice, because the
/// characters Sep hands over are well formed. With whole requests, a read that ends in a carriage
/// return either fills Sep's request (its fill loop then ends, and the next fill restores the
/// carriage return in order) or is the last read of the input (nothing follows to set aside). The
/// same logic exists in Sep's asynchronous fill, so an asynchronous read path would need its own
/// whole-span reader and its own review and proof. The behavior is verified in Sep 0.17.1 by
/// execution (D-137).
/// </para>
/// <para>
/// <b>Contract.</b> It changes read sizes only: it never transforms, buffers, reorders or retains
/// characters, and it owns nothing, so disposing it never disposes the decoder. It checks the
/// operation's cancellation token before and after every read of the decoder, which bounds how much
/// input a cancelled operation keeps reading; it does not interrupt a read that is blocked, and it
/// is not what keeps a record from being exposed after cancellation (the read pipeline checks that).
/// A decoder that reports a negative count, or more characters than it was given room for, breaks
/// the <see cref="TextReader"/> contract and gets an <see cref="InvalidOperationException"/>.
/// </para>
/// </summary>
internal sealed class WholeSpanTextReader : TextReader
{
    private readonly TextReader _inner;
    private readonly CancellationToken _cancellationToken;

    public WholeSpanTextReader(TextReader inner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _cancellationToken = cancellationToken;
    }

    public override int Read(Span<char> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var remaining = buffer.Length - total;
            var read = _inner.Read(buffer[total..]);
            if ((uint)read > (uint)remaining)
            {
                throw new InvalidOperationException(
                    "The decoder returned a character count outside the requested span.");
            }

            _cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    // The base TextReader argument contract (verified in 10.0.12: null, then negative index, then
    // negative count, then the range), then the one span loop.
    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
        {
            throw new ArgumentException("Offset and length were out of bounds for the array.");
        }

        return Read(buffer.AsSpan(index, count));
    }

    public override int Read()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var value = _inner.Read();
        if (value < -1 || value > char.MaxValue)
        {
            throw new InvalidOperationException("The decoder returned a value that is neither a character nor end of input.");
        }

        _cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    public override int Peek()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var value = _inner.Peek();
        if (value < -1 || value > char.MaxValue)
        {
            throw new InvalidOperationException("The decoder returned a value that is neither a character nor end of input.");
        }

        _cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    // Owns nothing: the read pipeline disposes the decoder and the stream itself.
    protected override void Dispose(bool disposing)
    {
    }
}
