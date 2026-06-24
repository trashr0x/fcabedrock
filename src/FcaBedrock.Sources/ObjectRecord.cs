namespace FcaBedrock.Sources;

/// <summary>
/// One object's row of raw field values, as produced by a source. A missing field
/// (empty, or equal to the binding's <c>missing_token</c> after trimming — spec
/// §5.1) is surfaced as <see langword="null"/>, so the source owns missing
/// detection and downstream stages stay free of binding details.
/// </summary>
public sealed class ObjectRecord
{
    private readonly string?[] _fields;

    public ObjectRecord(string name, string?[] fields)
    {
        Name = name;
        _fields = fields;
    }

    /// <summary>The object name (row index, for row-index keys).</summary>
    public string Name { get; }

    /// <summary>The number of fields read for this record.</summary>
    public int FieldCount => _fields.Length;

    /// <summary>The raw value at a 0-based column, or <see langword="null"/> when missing.</summary>
    public string? Field(int index) => _fields[index];
}
