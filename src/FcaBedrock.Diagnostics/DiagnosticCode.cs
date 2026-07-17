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

    // DiscretizerKindNotYetSupported (D-070) retired at M4 Slice E (D-104): every v1
    // discretizer kind is now executable — free_per_value at Slice B (D-101), equal_width at
    // Slice C (D-102), equal_frequency at Slice D (D-103), and value_groups, its last owner,
    // here. An unknown kind spelling stays an ordinary SpecFieldInvalid (D-070 tier 3).

    /// <summary>
    /// A recognized v1 surface the reader does not yet model was authored
    /// (<c>display_name</c>, <c>formal_attribute_format</c>,
    /// <c>value_type = "date"</c>). Closed, per-table set — never a fallback for
    /// unknown keys. Transitional intra-M2 scaffolding, retired as slices land
    /// their carriers (D-075; extends/template/matcher retired by Slice F, D-078).
    /// </summary>
    SpecSurfaceNotYetSupported,

    // --- Spec resolve ---

    /// <summary>
    /// The document has no <c>[spec]</c>/<c>version</c>, or declares a version other
    /// than <c>1</c>; the spec must be refused. Fatal. Emitted by the resolve seam,
    /// and by composition for every spec in an <c>extends</c> chain (root gated
    /// before any base loads; each base at its load) — per flow it fires exactly
    /// once. Spec §2/§3/§13 (D-067/D-078).
    /// </summary>
    SpecVersionUnsupported,

    /// <summary>
    /// An authored <c>[spec].extends</c> reference could not be resolved to a base
    /// spec by the composition source. Fatal — the composed spec cannot be built.
    /// Spec §13 (D-027/D-078).
    /// </summary>
    SpecExtendsNotFound,

    /// <summary>
    /// The <c>extends</c> chain revisits a spec it already contains (including a
    /// spec extending itself); composition must be a finite chain. Fatal.
    /// Spec §13 (D-027/D-078).
    /// </summary>
    SpecExtendsCycle,

    /// <summary>
    /// The document <em>uses</em> templates/matchers — a <c>[[matcher]]</c> entry is
    /// present (one aggregated diagnostic per document), or an attribute references a
    /// <c>template</c> (one per attribute) — and resolution is not implemented; the
    /// resolve seam rejects rather than silently ignoring schema-changing config.
    /// Unreferenced <c>[[template]]</c> blocks are inert and resolve cleanly.
    /// Spec §9 / §16.4 (D-078; transitional, removed at M6).
    /// </summary>
    TemplateMatcherNotImplementedV1,

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

    /// <summary>
    /// A numeric <c>free_per_value</c> <c>declared_domain</c> entry is unparseable,
    /// non-finite (NaN/±∞), or a normalization duplicate of another entry (two
    /// spellings, one numeric identity, e.g. <c>90</c> and <c>90.0</c>) — the §5.1
    /// exception to verbatim strings, parsed under <c>binding.locale</c>. Spec §10.3
    /// / §16.4 (D-096).
    /// </summary>
    DeclaredDomainInvalid,

    /// <summary>
    /// Two numeric <c>free_per_value</c> <c>value_labels</c> keys collapse to one
    /// numeric identity after normalization under <c>binding.locale</c> (e.g. <c>90</c>
    /// and <c>90.0</c>); an out-of-domain key stays <see cref="ValueLabelKeyNotInDomain"/>.
    /// Spec §10.8 / §16.4 (D-096).
    /// </summary>
    ValueLabelKeyDuplicate,

    /// <summary><c>manual_cuts</c>/<c>ordered_cuts</c> cuts are not strictly ascending. Spec §11.2 / §11.8.</summary>
    DiscretizerCutsNotAscending,

    /// <summary>A cut discretizer was given fewer than one cut. Spec §11.2 / §11.8.</summary>
    DiscretizerCutsTooFew,

    /// <summary>
    /// An <c>equal_width</c> <c>range = "manual"</c> span is unusable: a non-finite
    /// <c>vmin</c>/<c>vmax</c>, or <c>vmin >= vmax</c>. Every finite increasing range is
    /// accepted — the sign-aware interpolation cannot overflow one (G-7) — so this owns
    /// only genuinely unusable authored spans. Spec §11.4 / §16.4 (D-089).
    /// </summary>
    EqualWidthRangeInvalid,

    /// <summary>
    /// An <c>equal_width</c> <c>range = "manual"</c> derives cuts that are not finite and
    /// strictly ascending once <c>precision</c> is applied — a <c>round_to</c> collapsing
    /// two cuts onto one value. The spec-validate twin of
    /// <see cref="CalibrationCutsInvalid"/>, which owns the data-derived case. Spec §11.4
    /// / §16.4 (D-089).
    /// </summary>
    EqualWidthCutsCollapsed,

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
    /// source whose <c>restrict_to</c> contains a numeric entry — an exact
    /// <c>{ value = n }</c> or a range (the mirror case is
    /// <see cref="RestrictToNumericEntryRequired"/>). Spec §10.2 / §10.4
    /// (D-061/D-063/D-091).
    /// </summary>
    SourceValueTypeInvalid,

    /// <summary>
    /// A number-typed source (authored <c>value_type = "number"</c> or a numeric-cut
    /// discretizer) has a bare-string <c>restrict_to</c> entry; numeric restriction
    /// uses a <b>numeric entry</b> — an exact <c>{ value = n }</c> or a range. This
    /// code — not <see cref="SourceValueTypeInvalid"/> — owns the
    /// numeric-source/string-entry mismatch. Spec §10.4 (D-063; renamed from
    /// <c>RestrictToOnNumericRequiresRange</c> by D-091 now that the exact numeric
    /// entry exists, so "requires a range" is no longer the whole rule).
    /// </summary>
    RestrictToNumericEntryRequired,

    /// <summary>
    /// A <c>restrict_to</c> numeric entry is invalid: an exact <c>{ value = n }</c>
    /// whose <c>n</c> is non-finite, or a range whose provided bounds are equal,
    /// reversed, or non-finite. The empty range <c>{}</c> — both bounds omitted — is
    /// valid and matches any usable numeric value. Error. Spec §10.4 / §16.4
    /// (D-091).
    /// </summary>
    RestrictToRangeInvalid,

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
    /// A <c>value_groups</c> discretizer declares the same group <c>label</c> twice, or —
    /// under <c>unmatched = "other"</c> — a group whose label collides with the synthetic
    /// <c>Other</c> bin. Ordinal comparison (P-12), so <c>"Other"</c> collides and
    /// <c>"other"</c> does not. Error, one per duplicate. Duplicates never surface as
    /// <c>SpecFieldInvalid</c>; a pass-through value merely <em>observed</em> to equal a label
    /// is data-dependent and belongs to plan (<c>FormalAttributeCollision</c>).
    /// Spec §11.6 / §16.4 (D-090).
    /// </summary>
    ValueGroupsLabelDuplicate,

    /// <summary>
    /// An <c>ordinal</c> scale sits over <c>value_groups</c> with
    /// <c>unmatched = "passthrough"</c>. Ordinal over groups requires an authored
    /// <c>scale.order</c> that is a full permutation of the group labels, which a
    /// data-discovered bin set can never be. Error. Spec §11.6 / §12.3 / §16.4 (D-090).
    /// </summary>
    OrdinalNotAllowedWithValueGroupsPassthrough,

    /// <summary>
    /// An <c>object_key</c> is not valid under the binding's <c>shape</c>: a
    /// <c>row_index</c> key under <c>shape = "triple"</c>, or <em>any</em> authored
    /// <c>[binding.object_key]</c> under triple (triple identity is always the
    /// resolved subject and is not repointable). Spec §5.4 (D-064/D-082).
    /// </summary>
    ObjectKeyModeInvalidForShape,

    /// <summary>
    /// Two triple <c>columns</c> roles (subject/predicate/value) resolve to the same
    /// physical column; the three roles must be distinct. Spec §5.3 / §16.4 (D-085).
    /// </summary>
    TripleColumnsNotDistinct,

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
    /// A value-bin ordinal scale (<c>identity</c> — the only M2 value-bin
    /// discretizer, D-070) needs an explicit <c>scale.order</c> but omits it, or a
    /// <c>declared_domain</c> value has no <c>order</c> entry (every value bin needs
    /// a threshold — <c>order</c> must be a full permutation of the domain). Spec
    /// §12.3 (D-081).
    /// </summary>
    OrdinalOrderMissing,

    /// <summary>
    /// A <c>scale.order</c> entry is not among the attribute's bin labels (its
    /// <c>declared_domain</c> for <c>identity</c>); <c>order</c> lists raw domain
    /// values, never display labels. Spec §12.3 (D-081).
    /// </summary>
    OrdinalOrderHasUnknownValue,

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
    /// A plan produced zero formal attributes — every attribute is excluded or
    /// filter-only. A degenerate but structurally-valid schema; the run
    /// proceeds. Warning. Spec §16.4.
    /// </summary>
    NoFormalAttributes,

    // --- Calibrate (data-dependent schema resolution, D-098/G-1) ---

    /// <summary>
    /// A consuming discretizer (<c>identity</c> / <c>free_per_value</c>) with an
    /// absent <c>declared_domain</c> was calibrated from the observed data (any
    /// observed count, including zero). Warning — the resulting schema depends on
    /// this specific input; declare the domain or freeze it to make the run
    /// input-independent. Spec §7 / §10.3 / §16.4 (D-036; replaces the transitional
    /// <c>ObservedDomainCalibrationNotImplementedV1</c>).
    /// </summary>
    ObservedDomainUsed,

    /// <summary>
    /// <c>unknown_value_policy = "include"</c> calibration extended a consuming
    /// discretizer's domain with the observed unknown values (any addition count,
    /// including zero), making <c>schema_fingerprint</c> data-dependent. Warning.
    /// Spec §7 / §10.6 / §16.4 (D-036).
    /// </summary>
    UnknownValuePolicyInclude,

    /// <summary>
    /// A <c>value_groups</c> discretizer with <c>unmatched = "passthrough"</c> discovered its
    /// bins from the data (any discovered count, including zero), so the column set depends on
    /// this specific input. Warning — fires whenever the mode executes, because the
    /// data-dependence exists regardless of how many bins were found. Spec §7 / §11.6 / §16.4
    /// (D-055/D-090).
    /// </summary>
    ValueGroupsPassthroughDataDependent,

    /// <summary>
    /// A data-derived calibration population cannot bound its discretizer's
    /// configuration: an <c>equal_width</c> data range (<c>min_max</c>) with no usable
    /// spread — no usable numeric values at all, or every value equal, so <c>vmin</c>
    /// would equal <c>vmax</c>. Error, per attribute, in-path (no calibrated result).
    /// The <c>equal_frequency</c> distinct-value guard reuses this code at its slice;
    /// it does <b>not</b> apply to <c>equal_width</c>, whose bins are placed by span,
    /// not by count (D-089). Spec §7 / §11.4 / §16.4 (D-088/D-089).
    /// </summary>
    CalibrationDataInsufficient,

    /// <summary>
    /// Cuts derived from a calibration population are not finite and strictly ascending
    /// after any rounding — the calibrate-phase twin of
    /// <see cref="EqualWidthCutsCollapsed"/>. Error, per attribute (no calibrated
    /// result). Spec §11.4 / §11.5 / §16.4 (D-088/D-089).
    /// </summary>
    CalibrationCutsInvalid,

    /// <summary>
    /// The calibration population is too large to count exactly: a per-value count, the
    /// running total, or a merge sum would overflow <see cref="long"/> (spec §16.4,
    /// D-103/G-13). Error, calibrate, in-path — no calibrated result. A distinct
    /// condition from <see cref="CalibrationDataInsufficient"/> (too little data) and
    /// from a storage failure, so it must not masquerade as either (P-14). A
    /// contract-totality row: unreachable below ~9.2e18 observations.
    /// </summary>
    CalibrationPopulationTooLarge,

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

    // --- v2 .bed migration (D-009 one-way migration; Slice G) ---

    /// <summary>
    /// The v2 <c>.bed</c> structure is unreadable: a missing bracket section, an
    /// entry-count shortfall, or an unparseable attribute count / convert flag.
    /// Fatal: no document is produced (the <c>SpecTomlInvalid</c> of the migration
    /// path). Spec §16.4 (D-009/D-079).
    /// </summary>
    BedStructureInvalid,

    /// <summary>
    /// An included attribute uses the v2 date type <c>d</c>, which is deferred from
    /// v1 (D-038) and has no spec carrier; the migration fails rather than silently
    /// producing a spec missing an included attribute. The recognized-but-deferred
    /// tier — a typo'd type code is <see cref="BedTypeUnrecognized"/>. Spec §16.4 (D-079).
    /// </summary>
    BedDateTypeNotSupported,

    /// <summary>
    /// An included attribute's v2 type code is outside the six-code set
    /// (c/b/o/n/d) — a corrupt or hand-mangled <c>.bed</c>. Spec §16.4 (D-038's
    /// type-code map; D-079).
    /// </summary>
    BedTypeUnrecognized,

    /// <summary>
    /// An included attribute's v2 config cannot be transcribed into the document
    /// model: an unparseable numeric cut token (the carrier stores numbers), or a
    /// <c>dichotomic</c> true value equal to the effective missing token
    /// (contradicts the D-068 domain-exclusion rule). Config the model can carry is
    /// carried instead and validated at the resolve seam. Spec §16.4 (D-079).
    /// </summary>
    BedAttributeConfigInvalid,

    /// <summary>
    /// An excluded attribute's v2 config cannot be transcribed (the
    /// <see cref="BedDateTypeNotSupported"/>/<see cref="BedTypeUnrecognized"/>/
    /// <see cref="BedAttributeConfigInvalid"/> conditions) and was degraded to a
    /// bare excluded attribute — name, source, <c>include = false</c>. Warning:
    /// dormant config never blocks migration (D-049), but it is never dropped
    /// silently either. Spec §16.4 (D-049/D-079).
    /// </summary>
    BedParkedConfigDropped,

    /// <summary>
    /// A v2 missing-token category (D-068) carried a display label, which has no v1
    /// carrier — the missing column is canonically <c>{column}-missing</c>
    /// (§10.5/D-074); the label is dropped with this Warning. Spec §16.4 (D-068/D-079).
    /// </summary>
    BedMissingTokenLabelDropped,

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

    /// <summary>
    /// A triple <c>subject_grouped</c> source is not contiguous: a subject recurs after
    /// an intervening subject (its group already closed). Error — the conversion halts,
    /// the file is usable next call (§16.2); <c>ordering = "unordered"</c> accepts
    /// interleaved input instead. Spec §5.3 / §16.4 (D-082).
    /// </summary>
    TripleSubjectNotContiguous,

    /// <summary>
    /// A data-derived object name (a triple subject or a wide column key) is unusable:
    /// empty, whitespace-only, a <c>missing_token</c>, contains a newline/control
    /// character, or its mapped column is absent from the row (a newline would corrupt
    /// the line-structured <c>.cxt</c>, §18.1). Error, per row. Spec §5.4 / §16.4 (D-085).
    /// </summary>
    ObjectKeyValueInvalid,

    /// <summary>
    /// A wide <c>column</c> object key repeats a cleaned key value, governed by
    /// <c>duplicate_object_policy</c>: <c>fail</c> → <b>Error</b> naming the key + record index, stop;
    /// <c>keep</c> → aggregated <b>Warning</b> (each row stays its own object, later occurrences get a
    /// <c>#record-index</c> suffix); <c>dedupe</c> → aggregated <b>Info</b> (rows sharing a cleaned key
    /// collapse to one object, crosses unioned onto the first, with a bounded source-order sample). Does
    /// not apply to <c>row_index</c> or triple. Spec §5.4 / §6.1 / §16.4 (D-034/D-083/D-085).
    /// </summary>
    DuplicateObjectKey,

    /// <summary>
    /// Under <c>keep</c>, a <c>column</c> object's assigned name needed <c>#N</c> escalation because
    /// its candidate name (the cleaned key, or <c>&lt;key&gt;#&lt;record-index&gt;</c>) was already
    /// assigned — a literal data key colliding with a generated name, or vice versa. Warning-only,
    /// aggregated (count + a bounded <c>key→name</c> sample); distinct from <c>DuplicateObjectKey</c>,
    /// which reports repeated cleaned keys. Spec §6.1 / §16.4 (D-083/D-085).
    /// </summary>
    ObjectKeyNameDisambiguated,

    /// <summary>
    /// A conversion that grouped on the external sort-merge spool path (triple <c>unordered</c> or wide
    /// <c>dedupe</c>) hit a storage failure. Two channels (D-082): an <b>in-path</b> failure — storage
    /// still needed for correct row delivery — is <b>Error</b> and halts this conversion; a
    /// <b>cleanup-class</b> failure — storage that can no longer affect delivered rows (consumed-run
    /// deletes, teardown) — is <b>Warning</b> and the conversion completes. Aggregated to one final
    /// per stable identity <c>(operation, kind)</c> with a combined count and bounded path samples, and
    /// promoted to the worst severity across replay passes. Spec §16.4 (D-082/D-085).
    /// </summary>
    GroupingStorageFailed,

    /// <summary>
    /// The conversion emitted zero objects — an empty input, or <c>restrict_to</c>
    /// excluded every object (§10.4). A degenerate but structurally-valid context is
    /// still written; the run proceeds. Warning, once, on normal completion only — a
    /// structural halt suppresses it, because "no objects" would then describe the
    /// halt rather than the data. The row twin of the plan-phase
    /// <see cref="NoFormalAttributes"/>. Spec §16.4 (D-058: replaces the ambiguous
    /// whole-context <c>EmptyExtent</c>).
    /// </summary>
    NoObjectsEmitted,

    /// <summary>
    /// One or more planned formal attributes were never crossed by any emitted object —
    /// an empty <b>column</b>. Expected after <c>restrict_to</c> filtering, since
    /// calibration and the column vocabulary are computed over the input universe
    /// <em>before</em> objects are filtered (§7), so a surviving population need not
    /// span every bin. Warning, <b>aggregated</b>: one diagnostic carrying the count and
    /// a bounded sample of rendered names in plan order, flushed on normal completion
    /// only. Spec §7 / §16.4 (D-058: replaces <c>EmptyIntent</c>).
    /// </summary>
    AttributeHasNoCrosses,

    /// <summary>
    /// One or more emitted objects carry no crosses at all — an empty <b>row</b>.
    /// Legal (§10.1) and still written. Warning, <b>aggregated</b>: one diagnostic
    /// carrying the count and a bounded sample of object names in emission order,
    /// flushed on normal completion only. Counts <em>emitted</em> objects only — an
    /// object <c>restrict_to</c> excluded never had a row to be empty. Spec §16.4
    /// (D-058).
    /// </summary>
    ObjectHasNoCrosses,
}
