using System.Buffers.Binary;

namespace FcaBedrock.Conversion;

/// <summary>
/// A row tagged with its first-appearance <see cref="Rank"/> and global arrival <see cref="Seq"/>
/// (D-082). Runs are sorted by <c>(Rank, Seq)</c>, so a k-way merge by that key yields all rows of a
/// group contiguously, groups in first-appearance order, and source order preserved within a group.
/// </summary>
internal readonly record struct RankedRow<TRow>(int Rank, long Seq, TRow Row);

/// <summary>A written run on disk: its path and byte size (for live-bytes accounting and the 3T bound).</summary>
internal readonly record struct SpoolRunHandle(string Path, long SizeBytes);

/// <summary>
/// The framed on-disk record layout, shared by <see cref="SpoolRunWriter{TRow}"/> and
/// <see cref="SpoolRunReader{TRow}"/>: a 4-byte record length, then <c>rank</c> (int) + <c>seq</c>
/// (long) + the codec payload. The length prefix bounds every read against the file, so truncation and
/// corruption are detected rather than mis-decoded.
/// </summary>
internal static class SpoolRunFormat
{
    public const int HeaderBytes = sizeof(int) + sizeof(long); // rank + seq, ahead of the codec payload
}

/// <summary>Writes <see cref="RankedRow{TRow}"/> records to a run stream. Not thread-safe; single writer.</summary>
internal sealed class SpoolRunWriter<TRow> : IDisposable
{
    private readonly Stream _stream;
    private readonly IRowCodec<TRow> _codec;
    private byte[] _buffer = new byte[256];

    public SpoolRunWriter(Stream stream, IRowCodec<TRow> codec)
    {
        _stream = stream;
        _codec = codec;
    }

    /// <summary>Total bytes written so far (the run's size).</summary>
    public long BytesWritten { get; private set; }

    public void Write(RankedRow<TRow> entry)
    {
        var payload = _codec.Measure(entry.Row);
        var recordLength = SpoolRunFormat.HeaderBytes + payload;
        if (recordLength > int.MaxValue)
        {
            throw new InvalidOperationException("A single spool row exceeds the 2 GiB record limit.");
        }

        var total = sizeof(int) + (int)recordLength;
        if (_buffer.Length < total)
        {
            _buffer = new byte[Math.Max(total, _buffer.Length * 2)];
        }

        var span = _buffer.AsSpan(0, total);
        var offset = 0;
        RowFraming.WriteInt32(span, ref offset, (int)recordLength);
        RowFraming.WriteInt32(span, ref offset, entry.Rank);
        RowFraming.WriteInt64(span, ref offset, entry.Seq);
        _codec.Write(entry.Row, span.Slice(offset, (int)payload));

        _stream.Write(span);
        BytesWritten += total;
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>
/// Reads <see cref="RankedRow{TRow}"/> records from a run stream, validating each record's length
/// against the file (truncation → <see cref="SpoolFailureKind.TruncatedRun"/>) and each field against
/// the record buffer (corruption → <see cref="SpoolFailureKind.CorruptRun"/>). Never allocates beyond a
/// record's declared length. The declared <c>operation</c> labels the failures it raises.
/// </summary>
internal sealed class SpoolRunReader<TRow> : IDisposable
{
    private readonly Stream _stream;
    private readonly IRowCodec<TRow> _codec;
    private readonly string _path;
    private readonly GroupingOperation _operation;
    private readonly long _length;
    private long _position;
    private byte[] _buffer = new byte[256];

    public SpoolRunReader(Stream stream, IRowCodec<TRow> codec, string path, GroupingOperation operation)
    {
        _stream = stream;
        _codec = codec;
        _path = path;
        _operation = operation;
        _length = stream.Length;
    }

    /// <summary>The run file path (for a cleanup-channel <see cref="GroupingOperation.CleanupClose"/> sample on a close failure).</summary>
    public string Path => _path;

    /// <summary>The next record; <see langword="false"/> at clean EOF; throws on truncation/corruption.</summary>
    public bool TryRead(out RankedRow<TRow> entry)
    {
        entry = default;
        if (_position >= _length)
        {
            return false;
        }

        if (_length - _position < sizeof(int))
        {
            throw Truncated();
        }

        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
        ReadExactly(lengthBuffer);
        var recordLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (recordLength < SpoolRunFormat.HeaderBytes)
        {
            throw Corrupt($"record length {recordLength} is below the minimum {SpoolRunFormat.HeaderBytes}");
        }

        if (recordLength > _length - _position)
        {
            throw Truncated();
        }

        if (_buffer.Length < recordLength)
        {
            _buffer = new byte[recordLength];
        }

        var record = _buffer.AsSpan(0, recordLength);
        ReadExactly(record);

        var offset = 0;
        var rank = RowFraming.ReadInt32(record, ref offset);
        var seq = RowFraming.ReadInt64(record, ref offset);
        TRow row;
        try
        {
            row = _codec.Read(record[offset..]);
        }
        catch (SpoolFramingException ex)
        {
            throw new GroupingStorageException(_operation, SpoolFailureKind.CorruptRun, _path, ex.Message, ex);
        }

        entry = new RankedRow<TRow>(rank, seq, row);
        return true;
    }

    public void Dispose() => _stream.Dispose();

    private void ReadExactly(Span<byte> destination)
    {
        try
        {
            _stream.ReadExactly(destination);
        }
        catch (EndOfStreamException ex)
        {
            // EndOfStreamException : IOException, so it must be caught first to keep its distinct kind.
            throw new GroupingStorageException(_operation, SpoolFailureKind.TruncatedRun, _path, "A spool run ended before a complete record.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A device/read fault after the run opened — an owned storage failure, never escaping the seam.
            throw new GroupingStorageException(_operation, SpoolFailures.Classify(ex), _path, $"Failed to read spool run '{_path}'.", ex);
        }

        _position += destination.Length;
    }

    private GroupingStorageException Truncated() =>
        new(_operation, SpoolFailureKind.TruncatedRun, _path, "A spool run ended before a complete record.");

    private GroupingStorageException Corrupt(string detail) =>
        new(_operation, SpoolFailureKind.CorruptRun, _path, $"A spool run record is malformed: {detail}.");
}
