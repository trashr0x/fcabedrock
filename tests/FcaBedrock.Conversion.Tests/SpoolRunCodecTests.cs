using System.Buffers.Binary;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// Direct run-format round-trip and validated-framing tests (D-082). Corruption tests use small, safe
// lengths (never huge ones), so a malformed record maps to an owned storage-failure outcome without a
// dangerous allocation.
public sealed class SpoolRunCodecTests
{
    [Fact]
    public void RoundTrip_PreservesFieldsIndexNullEmptyAndLoneSurrogates()
    {
        RankedRow<TripleRow>[] rows =
        [
            new(0, 0, new TripleRow(0, "s", "p", "v")),
            new(1, 1, new TripleRow(1, null, "", "x")),                          // null vs empty stay distinct
            new(0, 2, new TripleRow(2, "\uD800", "\uDC00", "𝄞")),     // lone high, lone low, valid pair
            new(2, 3, new TripleRow(3, "", null, null)),
        ];

        var readback = RoundTrip(rows);

        Assert.Equal(rows, readback);
    }

    [Fact]
    public void TryRead_WhenTruncatedMidRecord_ThenThrowsTruncatedRun()
    {
        var bytes = WriteRun([new(0, 0, new TripleRow(0, "subject", "predicate", "value"))]);
        var truncated = bytes[..(bytes.Length - 3)]; // drop the tail of the payload

        var ex = Assert.Throws<GroupingStorageException>(() => ReadAll(truncated));
        Assert.Equal(SpoolFailureKind.TruncatedRun, ex.Kind);
        Assert.Equal(GroupingOperation.MergeRead, ex.Operation);
    }

    [Fact]
    public void TryRead_WhenRecordLengthExceedsFile_ThenThrowsTruncatedRun()
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 100); // claims a 100-byte record; the file has none

        var ex = Assert.Throws<GroupingStorageException>(() => ReadAll(bytes));
        Assert.Equal(SpoolFailureKind.TruncatedRun, ex.Kind);
    }

    [Fact]
    public void TryRead_WhenFieldLengthOverrunsRecord_ThenThrowsCorruptRun()
    {
        // A well-framed record whose subject string length (1000) overruns the record — a small,
        // safely-identifiable corruption, not a huge allocation.
        var record = new byte[24];
        var span = record.AsSpan();
        var offset = 0;
        RowFraming.WriteInt32(span, ref offset, 20); // recordLength: rank(4)+seq(8)+recordIndex(4)+subjectLen(4)
        RowFraming.WriteInt32(span, ref offset, 0);   // rank
        RowFraming.WriteInt64(span, ref offset, 0);   // seq
        RowFraming.WriteInt32(span, ref offset, 0);   // recordIndex
        RowFraming.WriteInt32(span, ref offset, 1000); // subject length overruns the record

        var ex = Assert.Throws<GroupingStorageException>(() => ReadAll(record));
        Assert.Equal(SpoolFailureKind.CorruptRun, ex.Kind);
    }

    [Fact]
    public void TryRead_WhenRecordLengthBelowHeader_ThenThrowsCorruptRun()
    {
        var record = new byte[sizeof(int) + 4];
        var offset = 0;
        RowFraming.WriteInt32(record, ref offset, 4); // below the 12-byte header minimum

        var ex = Assert.Throws<GroupingStorageException>(() => ReadAll(record));
        Assert.Equal(SpoolFailureKind.CorruptRun, ex.Kind);
    }

    [Fact]
    public void TryRead_WhenStreamThrowsIOException_ThenGroupingStorageMergeRead()
    {
        // A device/read fault after the run opened is an owned storage failure (MergeRead), never a raw
        // exception escaping the seam (F1).
        using var reader = new SpoolRunReader<TripleRow>(
            new ThrowOnReadStream(), TripleRowCodec.Instance, "run", GroupingOperation.MergeRead);

        var ex = Assert.Throws<GroupingStorageException>(() => reader.TryRead(out _));
        Assert.Equal(GroupingOperation.MergeRead, ex.Operation);
    }

    [Fact]
    public void TripleRowCodec_Read_WhenTrailingBytes_ThenThrowsFraming()
    {
        var row = new TripleRow(1, "s", "p", "v");
        var size = (int)TripleRowCodec.Instance.Measure(row);
        var buffer = new byte[size + 4]; // 4 undeclared trailing bytes
        TripleRowCodec.Instance.Write(row, buffer.AsSpan(0, size));

        Assert.Throws<SpoolFramingException>(() => TripleRowCodec.Instance.Read(buffer));
    }

    [Fact]
    public void DedupeRowCodec_Read_WhenTrailingBytes_ThenThrowsFraming()
    {
        var row = DedupeRow.Live(new ObjectRecord("0", new string?[] { "a", null, "c" }), 0);
        var size = (int)DedupeRowCodec.Instance.Measure(row);
        var buffer = new byte[size + 4];
        DedupeRowCodec.Instance.Write(row, buffer.AsSpan(0, size));

        Assert.Throws<SpoolFramingException>(() => DedupeRowCodec.Instance.Read(buffer));
    }

    [Fact]
    public void DedupeRowCodec_RoundTrip_PreservesFieldsIndexNullEmptyLoneSurrogates_AndOmitsName()
    {
        string?[] fields = ["a", "", null, "𝄞", "\uD800"]; // empty vs null distinct; valid pair; lone high surrogate
        var live = DedupeRow.Live(new ObjectRecord("row-42", fields), 42);

        var buffer = new byte[(int)DedupeRowCodec.Instance.Measure(live)];
        DedupeRowCodec.Instance.Write(live, buffer);
        var decoded = DedupeRowCodec.Instance.Read(buffer);

        Assert.Equal(42, decoded.Index);
        Assert.Equal(live.FieldCount, decoded.FieldCount);
        for (var i = 0; i < live.FieldCount; i++)
        {
            Assert.Equal(live.Field(i), decoded.Field(i));
        }

        // No Name bytes: a record with a different Name but identical fields serializes identically.
        var otherName = DedupeRow.Live(new ObjectRecord("a-totally-different-name", fields), 42);
        var otherBuffer = new byte[(int)DedupeRowCodec.Instance.Measure(otherName)];
        DedupeRowCodec.Instance.Write(otherName, otherBuffer);
        Assert.Equal(buffer, otherBuffer);
    }

    private static List<RankedRow<TripleRow>> RoundTrip(IReadOnlyList<RankedRow<TripleRow>> rows) => ReadAll(WriteRun(rows));

    private static byte[] WriteRun(IReadOnlyList<RankedRow<TripleRow>> rows)
    {
        var stream = new MemoryStream();
        using var writer = new SpoolRunWriter<TripleRow>(stream, TripleRowCodec.Instance);
        foreach (var row in rows)
        {
            writer.Write(row);
        }

        return stream.ToArray();
    }

    private static List<RankedRow<TripleRow>> ReadAll(byte[] bytes)
    {
        var result = new List<RankedRow<TripleRow>>();
        using var reader = new SpoolRunReader<TripleRow>(
            new MemoryStream(bytes), TripleRowCodec.Instance, "test-run", GroupingOperation.MergeRead);
        while (reader.TryRead(out var entry))
        {
            result.Add(entry);
        }

        return result;
    }

    // A readable stream with a non-zero length whose Read faults — a device/read error after open.
    private sealed class ThrowOnReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 100;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("simulated read fault");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
