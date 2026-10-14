namespace FcaBedrock.Sources;

/// <summary>
/// Where a delimited read failed, as far as the reader knows it: the kind of record, its data record
/// index, the field, the physical line the record starts on and the physical line of the defect.
/// Each coordinate is <see langword="null"/> when it is not known, and none is ever estimated.
/// <para>
/// <b>Bases.</b> The data record index and the field index count from 0; the data record index
/// numbers data records in input order, counting neither the header nor blank records (spec
/// §5.1.1). Lines count from 1. They are physical lines of the source: one per record terminator
/// outside quoted content (CR LF, a lone CR or a lone LF), blank lines included, plus every line
/// break inside a quoted field, so two sources whose records are equal can still report different
/// lines. A position holds no path, excerpt or byte offset.
/// </para>
/// <para>
/// <b>Validity.</b> The constructor accepts only consistent positions: a <see cref="SourceReadRecordKind.Data"/>
/// position has a nonnegative data record index, a <see cref="SourceReadRecordKind.Header"/> or
/// <see cref="SourceReadRecordKind.Unknown"/> one has none, and the other coordinates are each
/// optional. The default value is an <see cref="SourceReadRecordKind.Unknown"/> record with no
/// coordinates.
/// </para>
/// </summary>
public readonly record struct SourceReadPosition
{
    /// <summary>Creates a position, refusing an inconsistent one.</summary>
    /// <param name="recordKind">The kind of record.</param>
    /// <param name="dataRecordIndex">
    /// The data record index, counted from 0. Required for a data record, and absent otherwise.
    /// </param>
    /// <param name="fieldIndex">The field index within the record, counted from 0, when known.</param>
    /// <param name="recordStartLine">The physical line the record starts on, counted from 1, when known.</param>
    /// <param name="defectLine">The physical line of the defect, counted from 1, when known.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="recordKind"/> is not a defined kind, an index is negative, or a line is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A data record has no index, another kind has one, or the defect line precedes the start line.
    /// </exception>
    public SourceReadPosition(
        SourceReadRecordKind recordKind,
        int? dataRecordIndex = null,
        int? fieldIndex = null,
        long? recordStartLine = null,
        long? defectLine = null)
    {
        if (!Enum.IsDefined(recordKind))
        {
            throw new ArgumentOutOfRangeException(nameof(recordKind), recordKind, "The record kind is not a defined value.");
        }

        if (dataRecordIndex is { } index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(dataRecordIndex));
        }

        if (recordKind == SourceReadRecordKind.Data && dataRecordIndex is null)
        {
            throw new ArgumentException("A data record position needs its data record index.", nameof(dataRecordIndex));
        }

        if (recordKind != SourceReadRecordKind.Data && dataRecordIndex is not null)
        {
            throw new ArgumentException("Only a data record position has a data record index.", nameof(dataRecordIndex));
        }

        if (fieldIndex is { } field)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(field, nameof(fieldIndex));
        }

        if (recordStartLine is { } start)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(start, nameof(recordStartLine));
        }

        if (defectLine is { } defect)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defect, nameof(defectLine));
            if (recordStartLine is { } first && defect < first)
            {
                throw new ArgumentException("The defect line cannot precede the line the record starts on.", nameof(defectLine));
            }
        }

        RecordKind = recordKind;
        DataRecordIndex = dataRecordIndex;
        FieldIndex = fieldIndex;
        RecordStartLine = recordStartLine;
        DefectLine = defectLine;
    }

    /// <summary>The kind of record.</summary>
    public SourceReadRecordKind RecordKind { get; }

    /// <summary>The data record index, counted from 0; present exactly for a data record.</summary>
    public int? DataRecordIndex { get; }

    /// <summary>The field index within the record, counted from 0, or null when not known.</summary>
    public int? FieldIndex { get; }

    /// <summary>The physical line the record starts on, counted from 1, or null when not known.</summary>
    public long? RecordStartLine { get; }

    /// <summary>The physical line of the defect, counted from 1, or null when not known.</summary>
    public long? DefectLine { get; }
}
