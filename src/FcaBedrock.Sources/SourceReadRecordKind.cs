namespace FcaBedrock.Sources;

/// <summary>Which kind of record a <see cref="SourceReadPosition"/> describes.</summary>
public enum SourceReadRecordKind
{
    /// <summary>The record's kind is not known. Such a position carries no data record index.</summary>
    Unknown = 0,

    /// <summary>The record consumed as the header. It has no data record index (spec §5.1.1).</summary>
    Header,

    /// <summary>A data record, which always has its data record index.</summary>
    Data,
}
