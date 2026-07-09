using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The authored <c>[binding]</c> section (§5). Wide and triple fields coexist
/// here as authored (possibly-invalid states are document territory, D-066);
/// the resolver applies the §5.1 defaults and shape rules.
/// </summary>
/// <param name="Shape">Source shape (§5.1); required — absent is <c>BindingShapeMissing</c>.</param>
/// <param name="Encoding">Source text encoding (§5.1, default <c>"utf-8"</c>); resolved/validated at the seam (D-082).</param>
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
/// Authored triple column positions (§5.3): which column holds each triple role,
/// addressed by 0-based index or header name (one addressing mode across the three
/// roles; §5.3). The resolver validates addressing/distinctness and resolves names
/// to indices (D-082/D-085). A partial role table is a document-representable
/// invalid state (D-066) diagnosed at the seam.
/// </summary>
/// <param name="Subject">Subject column, by index or name.</param>
/// <param name="Predicate">Predicate column, by index or name.</param>
/// <param name="Value">Value column, by index or name.</param>
public sealed record TripleColumnsSection(ColumnRef? Subject, ColumnRef? Predicate, ColumnRef? Value);
