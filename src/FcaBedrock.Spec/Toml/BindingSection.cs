using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[binding]</c> section (§5). Wide and triple fields coexist
/// here as authored (possibly-invalid states are document territory, D-066);
/// the resolver applies the §5.1 defaults and shape rules.
/// <c>encoding</c> is document-only: Core carries no encoding field (Sources
/// is UTF-8-only until M3).
/// </summary>
/// <param name="Shape">Source shape (§5.1); required — absent is <c>BindingShapeMissing</c>.</param>
/// <param name="Encoding">Source text encoding (§5.1, default <c>"utf-8"</c>); document-only.</param>
/// <param name="Delimiter">Field delimiter (§5.1, default <c>','</c>).</param>
/// <param name="QuoteChar">Quote character (§5.1, default <c>'"'</c>).</param>
/// <param name="HasHeader">Whether the first row is a header (§5.1, default true).</param>
/// <param name="Locale">Locale for data parsing (§5.1, default <c>"invariant"</c>).</param>
/// <param name="MissingToken">Token marking a missing value (§5.1, default <c>"?"</c>).</param>
/// <param name="Ordering">Triple row ordering (§5.3); triple shape only.</param>
/// <param name="Columns">Triple column positions (§5.3); triple shape only.</param>
/// <param name="ObjectKey">The <c>[binding.object_key]</c> block (§5.4); null means the per-shape default.</param>
public sealed record BindingSection(
    SourceShape? Shape,
    string? Encoding,
    char? Delimiter,
    char? QuoteChar,
    bool? HasHeader,
    string? Locale,
    string? MissingToken,
    TripleOrdering? Ordering,
    TripleColumnsSection? Columns,
    ObjectKeySection? ObjectKey);

/// <summary>
/// Authored triple column positions (§5.3): which 0-based column holds each
/// triple role. Carried for the M2 triple reject-carrier (D-072); execution is M3.
/// </summary>
/// <param name="Subject">0-based subject column.</param>
/// <param name="Predicate">0-based predicate column.</param>
/// <param name="Value">0-based value column.</param>
public sealed record TripleColumnsSection(int? Subject, int? Predicate, int? Value);

/// <summary>
/// How triple rows are ordered in the source (§5.3). Carried for the M2 triple
/// reject-carrier (D-072); execution semantics are M3.
/// </summary>
public enum TripleOrdering
{
    /// <summary>Rows for one subject are contiguous.</summary>
    SubjectGrouped,

    /// <summary>No ordering guarantee.</summary>
    Unordered,
}
