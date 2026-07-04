namespace FcaBedrock.Diagnostics;

/// <summary>
/// Stable identifier for a distinct diagnostic condition. The full registry is
/// this enum (spec §16.4 lists the illustrative initial set). Only codes with a
/// real emit site in the current milestone are present; the enum grows per slice
/// rather than front-loading codes no path produces yet (principle P-3).
/// </summary>
public enum DiagnosticCode
{
    // --- Spec parse ---

    /// <summary>
    /// The document is not valid TOML 1.0 (syntax error, duplicate key, malformed
    /// datetime, …). Fatal: no document is produced. Spec §2 / §16.4 (D-075).
    /// </summary>
    SpecTomlInvalid,

    /// <summary>
    /// A key or table is not part of the v1 spec vocabulary (typo or misplaced
    /// key). The read fails so a write never silently drops authored content.
    /// Spec §16.4 (D-075).
    /// </summary>
    SpecKeyUnrecognized,

    /// <summary>
    /// A known key has the wrong type, shape, or an unrecognized enum/kind
    /// spelling. One code, message variants naming the expected form. Spec §16.4
    /// (D-075; covers the D-070 tier-3 unknown-kind case).
    /// </summary>
    SpecFieldInvalid,

    /// <summary>
    /// A recognized-but-deferred discretizer kind (<c>free_per_value</c>,
    /// <c>equal_width</c>, <c>equal_frequency</c>, <c>value_groups</c>) was
    /// authored; no carrier is built and round-trip is not promised. Spec §11 /
    /// §16.4 (D-070; transitional, removed as each kind lands at M4).
    /// </summary>
    DiscretizerKindNotYetSupported,

    /// <summary>
    /// A recognized v1 surface the reader does not yet model was authored
    /// (<c>[spec].extends</c>, <c>[[template]]</c>/<c>[[matcher]]</c>, attribute
    /// <c>template</c>, <c>display_name</c>, <c>formal_attribute_format</c>,
    /// <c>value_type = "date"</c>). Closed, per-table set — never a fallback for
    /// unknown keys. Transitional intra-M2 scaffolding, retired as slices D–G
    /// land their carriers (D-075).
    /// </summary>
    SpecSurfaceNotYetSupported,

    // --- Spec resolve ---

    /// <summary>
    /// The document has no <c>[spec]</c>/<c>version</c>, or declares a version other
    /// than <c>1</c>; the spec must be refused. Fatal. Spec §2/§3 (D-067).
    /// </summary>
    SpecVersionUnsupported,

    /// <summary>The document has no <c>[binding]</c> or no <c>shape</c>. Spec §5.1 (D-067).</summary>
    BindingShapeMissing,

    /// <summary>
    /// <c>binding.locale</c> is neither <c>"invariant"</c> nor a resolvable culture
    /// name. Spec §5.1 / §17 (D-067).
    /// </summary>
    BindingLocaleInvalid,

    /// <summary>
    /// A wide column source is unresolvable: both or neither of <c>index</c>/<c>name</c>;
    /// <c>name</c> with <c>has_header = false</c>, with no header schema supplied, or not
    /// found in the header; an <c>index</c> that is negative or out of the supplied
    /// schema's range. One code, message variants. Spec §10.2 (D-066/D-067).
    /// </summary>
    SourceBindingInvalid,

    /// <summary>An <c>[[attribute]]</c> has a null or empty <c>name</c>. Spec §10.1 (D-067).</summary>
    AttributeNameMissing,

    /// <summary>
    /// An included attribute is missing its <c>discretizer</c> or <c>scale</c> (§10.9),
    /// or a <c>dichotomic</c> scale has no <c>true_value</c> (§12.2). Message variants (D-067).
    /// </summary>
    AttributeScalingMissing,

    /// <summary>
    /// A column object key's <c>column</c> ref is missing or unresolvable (by index or
    /// header name). Spec §5.4 (D-064/D-067).
    /// </summary>
    ObjectKeyBindingInvalid,

    // --- Spec validation ---

    /// <summary>Two attributes declare the same <c>name</c>. Spec §10.2.</summary>
    AttributeNameDuplicate,

    /// <summary>A <c>value_labels</c> key is not in the declared domain. Spec §10.8.</summary>
    ValueLabelKeyNotInDomain,

    /// <summary><c>manual_cuts</c>/<c>ordered_cuts</c> cuts are not strictly ascending. Spec §11.2 / §11.8.</summary>
    DiscretizerCutsNotAscending,

    /// <summary>A cut discretizer was given fewer than one cut. Spec §11.2 / §11.8.</summary>
    DiscretizerCutsTooFew,

    /// <summary><c>ends = "closed"</c> requires at least two cuts; fewer were given. Spec §11.2 / §11.8.</summary>
    DiscretizerEndsClosedTooFewCuts,

    /// <summary><c>ordered_cuts.order</c> has duplicate or empty entries. Spec §11.8.</summary>
    OrderDomainInvalid,

    /// <summary>An <c>ordered_cuts</c> cut is not a member of <c>order</c>. Spec §11.8.</summary>
    OrderedCutsCutNotInDomain,

    /// <summary><c>ordered_cuts</c> cuts are not strictly ascending by order position. Spec §11.8.</summary>
    OrderedCutsNotAscending,

    // --- Planning ---

    /// <summary>
    /// Two configurations resolve to the same canonical formal-attribute identity.
    /// Spec §10.2 / §14.
    /// </summary>
    FormalAttributeCollision,

    /// <summary>
    /// Two distinct canonical identities render to the same <c>.cxt</c> name.
    /// Spec §10.2 / §14.
    /// </summary>
    FormalAttributeNameCollision,

    /// <summary>
    /// The spec binds a triple source, whose conversion is not implemented in this
    /// milestone; the planner rejects it before any planning. Spec §5.1 (D-072;
    /// transitional, removed at M3).
    /// </summary>
    TripleSourceNotImplementedV1,

    /// <summary>
    /// An attribute uses a modelled-but-deferred scale (<c>interordinal</c>,
    /// <c>biordinal</c>, <c>contranominal</c>); v1 planning rejects it. Fatal.
    /// Spec §12.4 / §16.4 / §20 (D-010; permanent v1 reservation).
    /// </summary>
    ScaleNotImplementedV1,

    // --- Emit ---

    /// <summary>
    /// A raw value outside the declared domain was observed. Severity follows
    /// <c>unknown_value_policy</c> (default warn). Aggregated per attribute. Spec §10.6 / §16.4.
    /// </summary>
    UnknownValueObserved,

    /// <summary>
    /// A present-but-unparseable numeric value (parse failure, NaN, or ±∞) was observed:
    /// the object is kept, no cross is emitted. Severity follows <c>unknown_value_policy</c>
    /// (<c>skip</c> silent). Aggregated per attribute. Spec §10.6 / §11.5 / §16.4 (D-050).
    /// </summary>
    SourceValueUnparseable,
}
