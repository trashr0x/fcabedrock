using FcaBedrock.Sources;

namespace FcaBedrock.Conversion;

/// <summary>
/// The one canonical internal row type for wide <c>dedupe</c> grouping (D-083), used across intake,
/// buffering, key extraction, measurement, (de)serialization, grouping output, and accumulation. A
/// <b>live</b> row wraps the source's <see cref="ObjectRecord"/> with <b>zero copy</b> (no per-row
/// field-array clone; a zero-spill conversion never touches the codec); a <b>decoded</b> row holds a
/// deserialized field array directly. No <see cref="ObjectRecord"/> is ever fabricated and there is no
/// placeholder/sentinel name — the codec serializes fields + index only, via <see cref="Field"/>,
/// identically for both states. Consistently <see langword="readonly"/> with controlled factory
/// construction for the two states.
/// </summary>
internal readonly struct DedupeRow
{
    private readonly ObjectRecord? _record; // live state
    private readonly string?[]? _fields;    // decoded state

    private DedupeRow(ObjectRecord? record, string?[]? fields, int index, int fieldCount)
    {
        _record = record;
        _fields = fields;
        Index = index;
        FieldCount = fieldCount;
    }

    /// <summary>The 0-based source record index (used for first-appearance ordering + error reporting).</summary>
    public int Index { get; }

    /// <summary>The number of fields in this row.</summary>
    public int FieldCount { get; }

    /// <summary>
    /// The retained <see cref="ObjectRecord.Name"/> length — for resident-memory accounting only (a live
    /// row keeps its record's unique row-index name alive); <c>0</c> for a decoded row. Exposes the
    /// length, never the value, so serialization still never touches the name.
    /// </summary>
    public int NameLength => _record?.Name.Length ?? 0;

    /// <summary>Wraps a source record with zero copy (the live state).</summary>
    public static DedupeRow Live(ObjectRecord record, int index)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new DedupeRow(record, null, index, record.FieldCount);
    }

    /// <summary>Holds a deserialized field array (the decoded state).</summary>
    public static DedupeRow Decoded(string?[] fields, int index)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new DedupeRow(null, fields, index, fields.Length);
    }

    /// <summary>
    /// The raw value at a 0-based column, or <see langword="null"/> when the cell is missing or absent
    /// (an <paramref name="index"/> at or beyond <see cref="FieldCount"/> — a ragged short row). Mirrors
    /// <see cref="ObjectRecord.Field"/>: a negative index is a programmer error.
    /// </summary>
    public string? Field(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= FieldCount)
        {
            return null;
        }

        return _record is not null ? _record.Field(index) : _fields![index];
    }
}

/// <summary>
/// The wide <c>dedupe</c> row codec (D-083): serializes a <see cref="DedupeRow"/>'s field count, index,
/// and fields (never a name), via <see cref="DedupeRow.Field"/> — identically for live and decoded
/// rows, so spilling round-trips a row's values exactly (P-7/P-12). A decoded row's field count is
/// validated against the record buffer so a corrupt count cannot force an oversized allocation.
/// </summary>
internal sealed class DedupeRowCodec : IRowCodec<DedupeRow>
{
    public static readonly DedupeRowCodec Instance = new();

    public long Measure(DedupeRow row)
    {
        long size = sizeof(int) + sizeof(int); // field count + index
        for (var i = 0; i < row.FieldCount; i++)
        {
            size += RowFraming.MeasureString(row.Field(i));
        }

        return size;
    }

    // Charges the retained referenced objects of a live row (the RankedRow slot + the buffer's backing
    // array are accounted separately by ResidentModel.BufferBytes): the ObjectRecord and its two reference
    // fields; its Name string; the field array (header + one reference per field, null or not); and each
    // non-null field string. On a decoded row (no record/name) this conservatively over-charges an absent
    // record/name — only live rows are ever budgeted.
    public long MeasureResident(DedupeRow row)
    {
        var resident = ResidentModel.ObjectCost(2 * ResidentModel.Reference);                                // ObjectRecord object + its _fields/Name refs
        resident = ResidentModel.SaturatingAdd(resident, ResidentModel.StringCost(row.NameLength));          // ObjectRecord.Name string
        resident = ResidentModel.SaturatingAdd(resident, ResidentModel.ReferenceArrayBytes(row.FieldCount)); // the string?[] field array

        for (var i = 0; i < row.FieldCount; i++)
        {
            var field = row.Field(i);
            if (field is not null)
            {
                resident = ResidentModel.SaturatingAdd(resident, ResidentModel.StringCost(field.Length));
            }
        }

        return resident;
    }

    public void Write(DedupeRow row, Span<byte> destination)
    {
        var offset = 0;
        RowFraming.WriteInt32(destination, ref offset, row.FieldCount);
        RowFraming.WriteInt32(destination, ref offset, row.Index);
        for (var i = 0; i < row.FieldCount; i++)
        {
            RowFraming.WriteString(destination, ref offset, row.Field(i));
        }
    }

    public DedupeRow Read(ReadOnlySpan<byte> source)
    {
        var offset = 0;
        var fieldCount = RowFraming.ReadInt32(source, ref offset);
        var index = RowFraming.ReadInt32(source, ref offset);

        // Each field is at least 4 bytes (its length prefix), so a valid field count cannot exceed the
        // record size / 4 — bounding the array allocation against a corrupt count.
        if (fieldCount < 0 || 4L * fieldCount > source.Length)
        {
            throw new SpoolFramingException($"A spool record declared an invalid field count ({fieldCount}).");
        }

        var fields = new string?[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            fields[i] = RowFraming.ReadString(source, ref offset);
        }

        RowFraming.EnsureFullyConsumed(source, offset);
        return DedupeRow.Decoded(fields, index);
    }
}
