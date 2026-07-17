using FcaBedrock.Core.Discretization;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// An authored attribute <c>discretizer</c> (§11). Models every v1 kind —
/// <c>identity</c>, <c>manual_cuts</c>, <c>ordered_cuts</c> (D-070 tier 1),
/// <c>free_per_value</c> (M4 Slice B, D-101), <c>equal_width</c> (M4 Slice C, D-102),
/// <c>equal_frequency</c> (M4 Slice D, D-103), and <c>value_groups</c> (M4 Slice E,
/// D-104). With <c>value_groups</c> carried the D-070 deferred-kind tier is empty and
/// retired; an unknown kind spelling is an ordinary <c>SpecFieldInvalid</c> (tier 3).
/// </summary>
public abstract record DiscretizerSection;

/// <summary>The <c>identity</c> discretizer (§11.1): each raw value is its own bin.</summary>
public sealed record IdentityDiscretizerSection : DiscretizerSection;

/// <summary>
/// The <c>free_per_value</c> discretizer (§11.3): one bin per distinct value. No
/// authored parameters — its numeric-vs-string identity rides on the source
/// <c>value_type</c> (§10.2, D-061), and any numeric <c>declared_domain</c> /
/// <c>value_labels</c> / <c>scale.order</c> keys stay authored verbatim in the
/// document, normalized to canonical numeric identities only in the resolved Core
/// graph (D-096).
/// </summary>
public sealed record FreePerValueDiscretizerSection : DiscretizerSection;

/// <summary>The <c>manual_cuts</c> discretizer (§11.2): numeric bins between authored cuts.</summary>
/// <param name="Cuts">Ascending cut points; null when not authored (diagnosed via the D-056 factory).</param>
/// <param name="Ends">Whether the outer bins are open or closed (§11.2).</param>
public sealed record ManualCutsDiscretizerSection(IReadOnlyList<double>? Cuts, BinEnds? Ends) : DiscretizerSection;

/// <summary>
/// The <c>equal_width</c> discretizer (§11.4): <c>bins</c> equal-width bins over the
/// span its <c>range</c> mode supplies. <c>range = "manual"</c> is spec-determined and
/// authors <c>vmin</c>/<c>vmax</c>; a data-derived range draws the span from the
/// calibration population and must not author them (D-089). Every field is
/// presence-tracked, so an omitted <c>range</c> (default <c>min_max</c>) or
/// <c>precision</c> (default <c>"exact"</c>) round-trips as omitted (D-049).
/// </summary>
/// <param name="Bins">The authored bin count; carried as <c>long?</c> and range-checked to 2..<see cref="int.MaxValue"/> before it becomes an <c>int</c>.</param>
/// <param name="Range">The authored range mode; null when not authored. Slice C recognizes <c>"min_max"</c> and <c>"manual"</c> only.</param>
/// <param name="VMin">The authored span minimum; required under <c>range = "manual"</c>, forbidden otherwise.</param>
/// <param name="VMax">The authored span maximum; required under <c>range = "manual"</c>, forbidden otherwise.</param>
/// <param name="Precision">The authored cut rounding (<c>"exact"</c> or <c>{ round_to = r }</c>); null when not authored.</param>
public sealed record EqualWidthDiscretizerSection(
    long? Bins,
    EqualWidthRange? Range,
    double? VMin,
    double? VMax,
    CutPrecision? Precision) : DiscretizerSection;

/// <summary>
/// The <c>equal_frequency</c> discretizer (§11.5): <c>bins</c> bins whose cuts are placed
/// for approximately equal counts. Always data-calibrated — it authors no span, so there
/// is no spec-determined mode (§7). Every field is presence-tracked, so an omitted
/// <c>tie_policy</c> (default <c>"left"</c>) or <c>cut_placement</c> (default
/// <c>"right_value"</c>) round-trips as omitted (D-049).
/// </summary>
/// <param name="Bins">The authored bin count; carried as <c>long?</c> and range-checked to 2..<see cref="int.MaxValue"/> before it becomes an <c>int</c>.</param>
/// <param name="TiePolicy">The authored side a tied group falls to; null when not authored.</param>
/// <param name="CutPlacement">The authored placement within the selected gap; null when not authored.</param>
public sealed record EqualFrequencyDiscretizerSection(
    long? Bins,
    TiePolicy? TiePolicy,
    CutPlacement? CutPlacement) : DiscretizerSection;

/// <summary>
/// The <c>value_groups</c> discretizer (§11.6, M4 Slice E / D-090): many-to-one value
/// grouping in declaration order, first match wins. Both fields are presence-tracked, so an
/// omitted <c>unmatched</c> (default <c>"skip"</c>) round-trips as omitted (D-049) — the
/// default resolves at the seam, never in the document.
/// </summary>
/// <param name="Groups">The authored groups in declaration order (order is semantic); null when not authored.</param>
/// <param name="Unmatched">The authored policy for values matching no group; null when not authored.</param>
public sealed record ValueGroupsDiscretizerSection(
    IReadOnlyList<ValueGroupSection>? Groups,
    ValueGroupsUnmatched? Unmatched) : DiscretizerSection;

/// <summary>
/// One authored <c>value_groups</c> group (§11.6). Every field is an authored-presence
/// carrier and is <b>not</b> normalized in the document model: <see cref="Values"/> is null
/// when <c>values</c> was omitted and a list — possibly empty — when authored, which the §14
/// encoding and the round-trip both depend on (G-11/D-094). Authored value order and
/// duplicates are preserved verbatim.
/// </summary>
/// <param name="Label">The authored group label; null when not authored (diagnosed at parse).</param>
/// <param name="Values">The authored explicit values; null when omitted, possibly empty when authored.</param>
/// <param name="Pattern">The authored regex verbatim; null when omitted.</param>
public sealed record ValueGroupSection(
    string? Label,
    IReadOnlyList<string>? Values,
    string? Pattern);

/// <summary>The <c>ordered_cuts</c> discretizer (§11.8): categorical bins cut over an ordered domain.</summary>
/// <param name="Order">The ordered domain; null when not authored.</param>
/// <param name="Cuts">Cut members of <paramref name="Order"/>; null when not authored.</param>
/// <param name="Ends">Whether the outer bins are open or closed (§11.8).</param>
public sealed record OrderedCutsDiscretizerSection(
    IReadOnlyList<string>? Order,
    IReadOnlyList<string>? Cuts,
    BinEnds? Ends) : DiscretizerSection;
