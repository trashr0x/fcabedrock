namespace FcaBedrock.Core.Spec;

/// <summary>
/// How triple rows are ordered in the source (spec §5.3). Governs streaming
/// acceptance, not output bytes: <see cref="SubjectGrouped"/> is the single-pass
/// fast path (subjects MUST be contiguous), <see cref="Unordered"/> permits
/// interleaved subjects; both emit objects in first-appearance order of each
/// cleaned subject (D-082, Slice A), so ordering is deliberately <em>not</em> a
/// fingerprint input. The document layer reuses this enum directly (as it does
/// <see cref="SourceShape"/> / <see cref="SourceValueType"/>). Both orderings execute
/// at emit (emitter-owned, D-082): <see cref="SubjectGrouped"/> as the single-pass fast
/// path, <see cref="Unordered"/> through first-appearance grouping.
/// </summary>
public enum TripleOrdering
{
    /// <summary>Rows for one subject are contiguous — single-pass streaming.</summary>
    SubjectGrouped,

    /// <summary>Subjects may be interleaved — grouped via external sort-merge/spool.</summary>
    Unordered,
}
