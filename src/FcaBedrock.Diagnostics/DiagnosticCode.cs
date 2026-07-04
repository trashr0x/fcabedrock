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

    /// <summary>
    /// <c>binding.quote_char</c> is authored as something other than the standard
    /// double quote <c>"</c>, the only quote v1 supports; the field is retained so a
    /// later version can lift the restriction without a format change. Spec §5.1
    /// (D-054).
    /// </summary>
    QuoteCharNotSupportedV1,

    /// <summary>
    /// The resolved <c>binding.delimiter</c> equals the resolved
    /// <c>binding.quote_char</c>; the two must differ. Fires independently of
    /// <see cref="QuoteCharNotSupportedV1"/> — both report when both conditions
    /// hold (D-076). Spec §5.1.
    /// </summary>
    BindingDelimiterQuoteConflict,

    /// <summary>
    /// The source's <c>value_type</c> is invalid for the attribute: an authored type
    /// a type-fixing discretizer disallows (<c>identity</c>/<c>ordered_cuts</c> are
    /// string-fixing, <c>manual_cuts</c> number-fixing, D-061), or a string-typed
    /// source whose <c>restrict_to</c> contains a numeric-range entry (the mirror
    /// case is <see cref="RestrictToOnNumericRequiresRange"/>). Spec §10.2 / §10.4
    /// (D-061/D-063).
    /// </summary>
    SourceValueTypeInvalid,

    /// <summary>
    /// A number-typed source (authored <c>value_type = "number"</c> or a numeric-cut
    /// discretizer) has a bare-string <c>restrict_to</c> entry; numeric restriction
    /// uses range entries. This code — not <see cref="SourceValueTypeInvalid"/> —
    /// owns the numeric-source/string-entry mismatch. Spec §10.4 (D-063).
    /// </summary>
    RestrictToOnNumericRequiresRange,

    /// <summary>
    /// A <c>restrict_to</c> string value is absent from the attribute's explicit
    /// non-empty <c>declared_domain</c> — a typo-catcher, Warning only; the resolve
    /// still succeeds. Spec §10.4 (D-063).
    /// </summary>
    RestrictToValueNotInDomain,

    /// <summary>
    /// <c>scale.order</c> is present on an ordinal scale over a cut discretizer,
    /// whose cut geometry is the single source of bin order; <c>order</c> is a
    /// value-bin field only. Spec §12.3 / §17 (D-060).
    /// </summary>
    OrdinalOrderNotAllowedWithCuts,

    /// <summary>
    /// A per-attribute authored <c>boundary</c> requests the straddling combination
    /// (<c>le</c>+inclusive or <c>ge</c>+strict) over cut bins, whose half-open
    /// geometry fixes the operator. An omitted or <c>[defaults]</c>-inherited
    /// <c>boundary</c> is defaulted, not authored, and never trips this. Spec §6 /
    /// §12.3 (D-060).
    /// </summary>
    OrdinalBoundaryIncompatibleWithCuts,

    /// <summary>
    /// The <c>object_key.mode</c> is not valid under the binding's <c>shape</c>
    /// (<c>row_index</c> under <c>shape = "triple"</c>). Spec §5.4 (D-064).
    /// </summary>
    ObjectKeyModeInvalidForShape,

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

    /// <summary>
    /// The spec declares a <c>composite</c> object key, deferred to v1.1; the v1
    /// planner rejects it. Fatal. Spec §5.4 / §20 (D-024/D-064; permanent v1
    /// reservation).
    /// </summary>
    ObjectKeyCompositeNotImplementedV1,

    /// <summary>
    /// The spec declares a wide <c>column</c> object key, whose execution is
    /// sequenced with the triple object-key work; the planner rejects it rather
    /// than silently falling back to row index. Spec §5.4 (D-064; transitional,
    /// removed at M3).
    /// </summary>
    ObjectKeyColumnNotImplementedV1,

    /// <summary>
    /// An attribute carries <c>restrict_to</c>, whose execution is not implemented
    /// in this milestone; the planner rejects it — included or filter-only — rather
    /// than silently emitting unfiltered output. Spec §10.4 (D-057/D-063;
    /// transitional, removed at M4).
    /// </summary>
    RestrictToNotImplementedV1,

    /// <summary>
    /// An included <c>identity</c> attribute has an absent <c>declared_domain</c>
    /// (omitted or authored <c>[]</c>), which needs the observed-domain calibration
    /// the pipeline does not build yet; the planner rejects it rather than silently
    /// emitting an empty or data-order-dependent schema. Spec §10.3 (D-071;
    /// transitional, removed when observed-domain calibration lands — see the
    /// roadmap backlog).
    /// </summary>
    ObservedDomainCalibrationNotImplementedV1,

    // --- Spec load (stored-fingerprint verification, D-051/D-069/D-077) ---

    /// <summary>
    /// The stored <c>schema_fingerprint</c> does not match the fingerprint
    /// recomputed from the resolved plan: the spec's schema changed since it was
    /// frozen. Warning — the run proceeds; recompute or remove the stored value.
    /// Spec §3 / §14 / §16.4.
    /// </summary>
    SchemaFingerprintStale,

    /// <summary>
    /// The stored <c>cxt_output_fingerprint</c> does not match the fingerprint
    /// recomputed from the plan and the spec's <i>native</i> output settings
    /// (CLI overrides never enter, §14). Warning. Spec §3 / §14 / §16.4.
    /// </summary>
    CxtOutputFingerprintStale,

    /// <summary>
    /// The stored <c>dat_output_fingerprint</c> does not match the fingerprint
    /// recomputed from the plan and the spec's <i>native</i> output settings.
    /// Warning. Spec §3 / §14 / §16.4.
    /// </summary>
    DatOutputFingerprintStale,

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
