using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace FcaBedrock.Conversion;

/// <summary>
/// Serializes one row type to and from a fixed-size byte buffer for spilling (D-082). Strings are
/// encoded as a length (in UTF-16 code units, <c>-1</c> for null) plus their raw UTF-16 code units, so
/// lone surrogates round-trip exactly — spilling never changes a value's identity or the output bytes
/// (P-7/P-12). <see cref="Measure"/> returns the exact serialized size (checked <see langword="long"/>
/// so it composes across many rows without overflow), and <see cref="Write"/> writes exactly that many
/// bytes. <see cref="Read"/> validates every field against the record buffer bounds, so a
/// safely-identifiable corrupt record surfaces as a storage failure rather than a malformed row.
/// </summary>
internal interface IRowCodec<TRow>
{
    /// <summary>The exact number of bytes <see cref="Write"/> will produce for <paramref name="row"/>.</summary>
    long Measure(TRow row);

    /// <summary>Writes <paramref name="row"/> into <paramref name="destination"/> (exactly <see cref="Measure"/> bytes).</summary>
    void Write(TRow row, Span<byte> destination);

    /// <summary>Decodes a row from a validated record buffer; throws <see cref="SpoolFramingException"/> on inconsistency.</summary>
    TRow Read(ReadOnlySpan<byte> source);

    /// <summary>
    /// A conservative upper bound on the heap bytes of the objects this row <b>references</b> (retained
    /// beyond the array slot) while buffered — used for the resident memory budget, distinct from
    /// <see cref="Measure"/> (the serialized size). The <c>RankedRow</c> slot and the buffer's
    /// <c>List</c>/backing array are <b>not</b> counted here; the grouping loop adds
    /// <see cref="ResidentModel.BufferBytes"/> for those. This charges the retained record/name/field
    /// array, a reference per field, and each non-null string object, with saturating arithmetic so large
    /// field counts/lengths do not overflow (D-082).
    /// </summary>
    long MeasureResident(TRow row);
}

/// <summary>
/// Conservative x64 managed-heap upper-bound constants for the resident buffer accounting (D-082). These
/// are <b>correctness</b> constants for the .NET 10 CoreCLR x64 object layout, padded upward so actual
/// retained live-object bytes ≤ the modeled bytes on that target — <b>not</b> performance knobs (M8 tunes
/// the buffer budget and fan-in, never these; changing a layout constant requires re-validating the
/// object layout). The model has two parts: per-row <b>retained referenced objects</b>
/// (<see cref="IRowCodec{TRow}.MeasureResident"/>) and the <b>buffer</b> itself — the <c>List</c> object
/// plus its backing array (<see cref="BufferBytes"/>). All arithmetic saturates so large field
/// counts/lengths cannot overflow. The bound is over the stable retained graph at a grouping checkpoint;
/// the <c>List</c> resize copy transient (old+new array co-resident) is excluded as transient allocator
/// overhead the GC reclaims.
/// </summary>
internal static class ResidentModel
{
    public const long ObjectHeader = 24; // reference-type object header + method-table pointer, padded
    public const long ArrayHeader = 32;  // array object header + length, padded
    public const long Reference = 8;     // one reference (an array slot or field)

    /// <summary>A reference-type object carrying <paramref name="fieldBytes"/> of instance fields.</summary>
    public static long ObjectCost(long fieldBytes) => SaturatingAdd(ObjectHeader, fieldBytes);

    /// <summary>
    /// A non-null string's retained cost: object header + length field + UTF-16 payload (2 B/char) + the
    /// null terminator, rounded up to 8-byte alignment — conservative over the real
    /// <c>roundUp8(header + 4 + 2·length + 2)</c>.
    /// </summary>
    public static long StringCost(int length) => SaturatingAdd(ObjectHeader, RoundUpTo8((2L * length) + 2));

    /// <summary>A reference array of <paramref name="count"/> slots (e.g. a <c>string?[]</c> field array).</summary>
    public static long ReferenceArrayBytes(long count) => SaturatingAdd(ArrayHeader, SaturatingMul(count, Reference));

    /// <summary>A backing array of <paramref name="capacity"/> slots of <paramref name="elementSize"/> bytes (the exact struct stride).</summary>
    public static long BackingArrayBytes(int capacity, long elementSize) => SaturatingAdd(ArrayHeader, SaturatingMul(capacity, elementSize));

    /// <summary>The <c>List&lt;T&gt;</c> object itself: header + the <c>_items</c> reference + <c>_size</c>/<c>_version</c>, padded.</summary>
    public static long ListObjectBytes => ObjectCost(Reference + (2L * sizeof(int)));

    /// <summary>
    /// The whole <c>List&lt;RankedRow&lt;TRow&gt;&gt;</c> buffer's retained bytes at a checkpoint: the
    /// <c>List</c> object plus its backing array. <paramref name="capacity"/> is the list's real capacity;
    /// <paramref name="elementSize"/> the exact <c>RankedRow</c> stride (<c>Unsafe.SizeOf</c>).
    /// </summary>
    public static long BufferBytes(int capacity, long elementSize) => SaturatingAdd(ListObjectBytes, BackingArrayBytes(capacity, elementSize));

    /// <summary>Rounds <paramref name="n"/> up to a multiple of 8 (saturating; non-positive → 0).</summary>
    public static long RoundUpTo8(long n) => n <= 0 ? 0 : (n > long.MaxValue - 7 ? long.MaxValue : (n + 7) & ~7L);

    /// <summary>Saturating addition (clamps at <see cref="long.MaxValue"/>) so the running total cannot overflow.</summary>
    public static long SaturatingAdd(long a, long b)
    {
        var sum = unchecked(a + b);
        return ((a ^ sum) & (b ^ sum)) < 0 ? long.MaxValue : sum; // overflow → clamp
    }

    /// <summary>Saturating multiply of non-negative magnitudes (clamps at <see cref="long.MaxValue"/>).</summary>
    public static long SaturatingMul(long a, long b)
    {
        if (a <= 0 || b <= 0)
        {
            return 0;
        }

        return a > long.MaxValue / b ? long.MaxValue : a * b;
    }
}

/// <summary>
/// A safely-identifiable intra-record framing inconsistency (a field length that overruns the record
/// buffer). Internal; the run reader maps it to a <see cref="GroupingStorageException"/> with
/// <see cref="SpoolFailureKind.CorruptRun"/>.
/// </summary>
internal sealed class SpoolFramingException(string message) : Exception(message);

/// <summary>
/// Fixed-endianness (little-endian) framing primitives shared by the row codecs. String payloads use
/// raw UTF-16 code units so lone surrogates are preserved exactly; reads validate each field against
/// the remaining buffer, never allocating beyond it.
/// </summary>
internal static class RowFraming
{
    /// <summary>Serialized size of a nullable string: 4 (length) + 2·length payload bytes; null = 4.</summary>
    public static long MeasureString(string? value) => 4L + (value is null ? 0L : 2L * value.Length);

    public static void WriteInt32(Span<byte> destination, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], value);
        offset += sizeof(int);
    }

    public static void WriteInt64(Span<byte> destination, ref int offset, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination[offset..], value);
        offset += sizeof(long);
    }

    public static long ReadInt64(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset + sizeof(long) > source.Length)
        {
            throw new SpoolFramingException("A spool record ended before an expected 8-byte integer.");
        }

        var value = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
        offset += sizeof(long);
        return value;
    }

    public static void WriteString(Span<byte> destination, ref int offset, string? value)
    {
        if (value is null)
        {
            WriteInt32(destination, ref offset, -1);
            return;
        }

        WriteInt32(destination, ref offset, value.Length);
        var payload = MemoryMarshal.AsBytes(value.AsSpan());
        payload.CopyTo(destination[offset..]);
        offset += payload.Length;
    }

    /// <summary>Asserts a record was fully consumed; trailing declared bytes are safely-identifiable corruption.</summary>
    public static void EnsureFullyConsumed(ReadOnlySpan<byte> source, int offset)
    {
        if (offset != source.Length)
        {
            throw new SpoolFramingException($"A spool record has {source.Length - offset} trailing byte(s) after its fields.");
        }
    }

    public static int ReadInt32(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset + sizeof(int) > source.Length)
        {
            throw new SpoolFramingException("A spool record ended before an expected 4-byte integer.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);
        return value;
    }

    public static string? ReadString(ReadOnlySpan<byte> source, ref int offset)
    {
        var length = ReadInt32(source, ref offset);
        if (length == -1)
        {
            return null;
        }

        if (length < 0)
        {
            throw new SpoolFramingException($"A spool record declared a negative string length ({length}).");
        }

        var byteLength = 2L * length;
        if (offset + byteLength > source.Length)
        {
            throw new SpoolFramingException(
                $"A spool record declared a {length}-char string that overruns the {source.Length}-byte record.");
        }

        var payload = source.Slice(offset, (int)byteLength);
        var result = new string(MemoryMarshal.Cast<byte, char>(payload));
        offset += (int)byteLength;
        return result;
    }
}

/// <summary>
/// The wide-source row codec would live here too; C2 ships only <see cref="TripleRowCodec"/> (triple
/// <c>unordered</c>). The wide <c>DedupeRow</c> codec lands with the dedupe emit path (C3).
/// </summary>
internal sealed class TripleRowCodec : IRowCodec<Sources.TripleRow>
{
    public static readonly TripleRowCodec Instance = new();

    public long Measure(Sources.TripleRow row) =>
        sizeof(int) // RecordIndex
        + RowFraming.MeasureString(row.Subject)
        + RowFraming.MeasureString(row.Predicate)
        + RowFraming.MeasureString(row.Value);

    // The 3 string refs live inline in the RankedRow slot (counted by the buffer's backing array); the
    // retained cost adds only each non-null string object.
    public long MeasureResident(Sources.TripleRow row)
    {
        long resident = 0;
        if (row.Subject is not null)
        {
            resident = ResidentModel.SaturatingAdd(resident, ResidentModel.StringCost(row.Subject.Length));
        }

        if (row.Predicate is not null)
        {
            resident = ResidentModel.SaturatingAdd(resident, ResidentModel.StringCost(row.Predicate.Length));
        }

        if (row.Value is not null)
        {
            resident = ResidentModel.SaturatingAdd(resident, ResidentModel.StringCost(row.Value.Length));
        }

        return resident;
    }

    public void Write(Sources.TripleRow row, Span<byte> destination)
    {
        var offset = 0;
        RowFraming.WriteInt32(destination, ref offset, row.RecordIndex);
        RowFraming.WriteString(destination, ref offset, row.Subject);
        RowFraming.WriteString(destination, ref offset, row.Predicate);
        RowFraming.WriteString(destination, ref offset, row.Value);
    }

    public Sources.TripleRow Read(ReadOnlySpan<byte> source)
    {
        var offset = 0;
        var recordIndex = RowFraming.ReadInt32(source, ref offset);
        var subject = RowFraming.ReadString(source, ref offset);
        var predicate = RowFraming.ReadString(source, ref offset);
        var value = RowFraming.ReadString(source, ref offset);
        RowFraming.EnsureFullyConsumed(source, offset);
        return new Sources.TripleRow(recordIndex, subject, predicate, value);
    }
}
