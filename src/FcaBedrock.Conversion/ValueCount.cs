using System.Buffers.Binary;

namespace FcaBedrock.Conversion;

/// <summary>
/// One distinct finite numeric value and how many observations carried it — the unit of
/// the exact count-sensitive calibration population (§11.5, D-095). A fixed-size row with
/// <b>no reference fields</b>, so its resident cost is the array slot alone and its
/// serialized form is a constant 16 bytes.
/// <para>
/// <see cref="Value"/> is always <b>positive-zero canonicalized</b> before it reaches this
/// type: the accumulator folds ±0 at intake, never merely at cut placement. The two zero
/// spellings do aggregate on their own — <see cref="Dictionary{TKey, TValue}"/>,
/// <see cref="double.Equals(double)"/> and <see cref="double.CompareTo(double)"/> all treat
/// them as one value, so the distinct count is right either way — but <i>which</i> spelling
/// survives into a run, and therefore into the merged row, would otherwise depend on
/// insertion and heap order. Canonicalizing at intake removes that freedom, so the value a
/// cut, label, or hash is derived from is pinned rather than incidental (G-6/D-096).
/// </para>
/// </summary>
internal readonly record struct ValueCount(double Value, long Count);

/// <summary>
/// The <see cref="ValueCount"/> spool codec (D-095): a fixed 16-byte little-endian payload
/// — the <see cref="ValueCount.Value"/> bit pattern then the <see cref="ValueCount.Count"/>
/// — reusing the existing framing, integrity, and failure semantics of the grouping spool
/// stack rather than a second temporary-storage system (P-5). Encoding is deterministic and
/// exactly round-trips the double's bits, so spilling never moves a cut (P-7).
/// </summary>
internal sealed class ValueCountCodec : IRowCodec<ValueCount>
{
    /// <summary>The fixed serialized payload size: one <see cref="double"/> + one <see cref="long"/>.</summary>
    public const int PayloadBytes = sizeof(double) + sizeof(long);

    public static readonly ValueCountCodec Instance = new();

    public long Measure(ValueCount row) => PayloadBytes;

    // No reference fields: the value and count live inline in the RankedRow slot, which the
    // accumulator's own fixed-capacity model charges. Nothing is retained beyond it.
    public long MeasureResident(ValueCount row) => 0;

    public void Write(ValueCount row, Span<byte> destination)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(destination, row.Value);
        BinaryPrimitives.WriteInt64LittleEndian(destination[sizeof(double)..], row.Count);
    }

    public ValueCount Read(ReadOnlySpan<byte> source)
    {
        // The payload is fixed-size, so any other length is safely-identifiable corruption
        // (the reader maps SpoolFramingException to a CorruptRun storage failure).
        if (source.Length != PayloadBytes)
        {
            throw new SpoolFramingException(
                $"A value-count spool record is {source.Length} bytes; the fixed payload is {PayloadBytes}.");
        }

        return new ValueCount(
            BinaryPrimitives.ReadDoubleLittleEndian(source),
            BinaryPrimitives.ReadInt64LittleEndian(source[sizeof(double)..]));
    }
}

/// <summary>
/// Orders <see cref="ValueCount"/> rows by ascending value (§11.5's "sort ascending by
/// IEEE-754 total order"). Every value in the population is finite and zero-canonicalized
/// before it reaches a run, so <see cref="double.CompareTo(double)"/> is a total order over
/// them — culture-independent and machine-stable. The canonicalization is what makes that
/// true rather than the comparer: <c>CompareTo</c> reports the two zero spellings <b>equal</b>
/// (it does not implement IEEE <c>totalOrder</c>, which would rank <c>-0.0</c> below
/// <c>+0.0</c>), so without it a tie between two distinct bit patterns could survive into the
/// output.
/// </summary>
internal sealed class ValueCountComparer : IComparer<ValueCount>
{
    public static readonly ValueCountComparer Instance = new();

    public int Compare(ValueCount x, ValueCount y) => x.Value.CompareTo(y.Value);
}
