namespace FcaBedrock.Diagnostics;

/// <summary>
/// Stable identifier for a distinct diagnostic condition. The full registry is
/// this enum (spec §16.4 lists the illustrative initial set). Only codes with a
/// real emit site in the current milestone are present; the enum grows per slice
/// rather than front-loading codes no path produces yet (principle P-3).
/// </summary>
public enum DiagnosticCode
{
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
