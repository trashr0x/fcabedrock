# Decisions Log: FcaBedrock vNext

Architectural decisions, with rationale and rejected alternatives. Append-only
in spirit: supersede entries rather than deleting them, and compact an entry in
place only as D-129 allows, so the reasoning trail survives. Newest decisions at
the bottom of each section.

**Entry format:**

```text
### D-NNN: Short title
- **Status:** accepted | superseded by D-MMM | revisited
- **Date:** YYYY-MM-DD
- **Decision:** what we're doing.
- **Why:** the reasoning.
- **Rejected:** alternatives and why not.
- **Affects:** packages / spec sections.
```

Early entries recorded before the **Date** field was introduced are
intentionally undated; new entries include it.

This file expands on spec §21 ("Decisions log") with the broader architectural
decisions, not just the spec-field defaults. Where a decision is purely a
spec-field default, it lives in spec §21 and is only cross-referenced here.

## Index

One line per decision, grouped as the sections below. Read the entries your
task touches (see the `AGENTS.md` workflow, D-073); read the log in full before
proposing architectural changes. An italic note records where a later decision
supersedes, refines, realizes or retires part of an entry, or where the entry
changes an earlier one. A new entry MUST add its line here.

### Architecture

- D-001: Separable binding vs scaling in the spec
- D-002: Orthogonal discretizer × scale model
- D-003: Discovery is a separate operation, not implicit in convert *(refined by D-036)*
- D-004: Determinism rules centralized in the planner
- D-005: Plan/Emit separation, with calibration before planning *(refined by D-036)*
- D-006: Diagnostics package as the shared leaf *(alias dropped by D-042)*
- D-007: Target 10×–100× the v2 EMAGE workload
- D-008: Avalonia for the desktop UI
- D-009: New TOML spec format; one-way `.bed` migration *(migrator realized by D-079)*
- D-010: Modelled-but-rejected scales/features carry forward-compat
- D-011: v2 byte-equality is a CLI flag, not a spec setting
- D-133: A Bedrock spec is a UTF-8 TOML 1.1.0 document; the TOML version is independent of `[spec].version`

### Scope / feature decisions

- D-020: v1 scale and discretizer surface
- D-021: Three filtering levers kept distinct *(refined by D-032)*
- D-022: `value_groups` discretizer (clustering raw values)
- D-023: `value_labels` (raw value → display label)
- D-024: Object grouping by composite key (deferred to v1.1)
- D-025: Post-context reductions live in a sibling tool
- D-026: Reproducibility: provenance block + run manifest *(per-run manifest schema and complete calibration representation refined by D-122)*
- D-027: Spec composition via `extends`
- D-028: Calibration on-the-fly by default; `calibrate` to freeze

### Round 1 spec audit

- D-030: Triple multi-value union; scale decides folding
- D-031: `subject_grouped` requires contiguous subjects
- D-032: Filter-only attributes (`include = false` keeps `restrict_to`)
- D-033: `source` may repeat across attributes
- D-034: Duplicate object keys defined by key mode; `fail` default *(execution → M3, D-064)*
- D-035: Fingerprint scopes: schema = planned columns only *(output split by D-051/D-053)*
- D-036: Four-phase processing model; convert calibrates, never discovers *(NaN/∞-as-missing clause superseded by D-050)*

### Round 2 spec audit

- D-037: Scale-specific default naming; emitted-field discipline *((b) superseded by D-049)*
- D-038: Date support deferred; v2 six-type-code map confirmed

### Process / governance

- D-029: Engineering principles formalized as docs/engineering-principles.md *(owner file renamed and renumbering forbidden by D-129)*
- D-039: Test conventions + ArchUnitNET for architecture tests *(conventions moved to `AGENTS.md` by D-048)*
- D-040: Shared `tests/Directory.Build.props`; MTP-only (no Microsoft.NET.Test.Sdk)
- D-048: `AGENTS.md` canonical; `CLAUDE.md` imports it; no symlink
- D-073: Decision index; sessions read it first, then relevant entries *(in-place summarizing narrowed by D-129)*
- D-129: M8.2 writing governance: in-place decision compaction, `EP-` and `WP-` identifier ownership, the authored inventory, and the mechanical checker boundary *(narrows D-073; rule 5's checker portion superseded by D-130; rule 1's heading-anchor clause superseded by D-131 for its one-time migration)*
- D-130: Authored-text integrity: one mechanical command over raw decoded text, two fixed instruction-entry checks, an include/exclude manifest and a process-level test runner *(supersedes the checker portion of D-129 rule 5)*
- D-131: One-time heading-separator migration in the decision log, roadmap and benchmark record: colons replace em dashes, changed anchors are accepted, and no alias is kept *(supersedes the heading-anchor clause of D-129 rule 1 for this migration only)*
- D-132: No separate public evidence file; the benchmark guide carries only the identities that qualify its current claims
- D-134: Spec passages that contradict their rule's owner are corrected to it, and enforced defaults, bounds and allowed values shown only in examples, and enforced rules stated nowhere, are stated in prose

### M1 (mini-mushroom walking skeleton)

- D-041: Sep as the DSV tokenizer for the wide-CSV source
- D-042: Drop the `BedrockResult<T>` alias; use `Result<T, BedrockDiagnostic>`
- D-043: Two test axes: golden = v2-compat evidence, conformance = native spec
- D-044: Bin-label style is a plan-time render hook, not a writer flag
- D-045: v2 `o` and `n` are both cut-based; discrete→nominal, progressive→ordinal
- D-046: `ordered_cuts` discretizer (spec §11.8); v2 `.bed` section-role asymmetry
- D-047: Open-end ordinal threshold renders `all`, canonical in both paths
- D-049: `include = false` is an authoring toggle; dormant config never blocks

### M2 (TOML spec format + fingerprinting)

- D-050: Malformed numeric values are present-but-invalid, not missing
- D-051: Per-format output fingerprints (cxt + dat) replace the single output_fingerprint
- D-052: `extends` overrides attributes position-preservingly
- D-053: Fingerprints hash a plan-derived canonical JSON structure, not TOML text *(pinned by D-069)*
- D-054: v1 supports only the standard double `quote_char`
- D-055: `value_groups` does not use `declared_domain`
- D-056: Cut validation in M2
- D-057: `restrict_to` round-trips in M2; execution deferred to M4 *(carriage realized by D-079; execution realized by D-105)*
- D-058: Empty-output diagnostics: mechanical names replace EmptyExtent/EmptyIntent

### M1-adjacent conformance pass

- D-059: Discretization outcomes and data-diagnostic aggregation

### Tier 1 spec audit (pre-M2)

- D-060: Ordinal-over-cuts validation contract *((c) authored-boundary provenance extended to template/matcher-supplied fields by D-114)*
- D-061: `value_type` matrix: `free_per_value` flexible, `identity` string-only
- D-062: Cross-attribute restrict not modelled in v1; drop the diagnostic
- D-063: `restrict_to`: M2 validates shape, M4 executes; diagnostic ownership *(exact numeric form added + diagnostic renamed by D-091; execution realized by D-105)*
- D-064: Wide column object keys deferred to M3; object-key diagnostic taxonomy *(execution realized by D-083)*
- D-065: Calibration/vocabulary over the input universe, before `restrict_to`

### Tier 2 register (pre-M2)

- D-066: Parsed spec document model vs. resolved Core `BedrockSpec`
- D-067: Resolve/validate seam and diagnostic phase ownership *(re-homing parenthetical superseded by D-080)*
- D-068: `missing_policy = "as_attribute"` scheduled into M2; effective-`missing_token` migration *(migrator branch realized by D-079)*
- D-069: Canonical fingerprint encoding, pinned (appendix to D-053)
- D-070: Minimal M2 discretizer-carrier scope; three-tier kind response *(transitional kind reject fully retired by D-104)*
- D-071: Absent/empty `declared_domain`: M2 interim reject until calibrate *(authored-empty reading revised by D-122; interim reject retired by D-098)*
- D-072: Basic triple TOML carrier in M2; conversion deferred to M3 *(conversion realized by D-082)*

### M2 implementation (slices)

- D-074: `as_attribute` missing-column position uniform across scale kinds (appendix to D-068) *(default-path rendering; an explicit `formal_attribute_format` renders the missing column too; D-117)*
- D-075: Slice C TOML reader/writer contract: strictness, parse codes, canonical form *(deferred-surface set narrowed by D-078/D-120 to `value_type = "date"`)*
- D-076: Slice D seam/plan validation contract details *(appends D-067; exact numeric form added + diagnostic renamed by D-091)*
- D-077: Slice E fingerprint encoding/verification contract details *(appends D-069)*
- D-078: Slice F composition/carrier contract details *(realizes D-027/D-052; refines D-067/D-075; pattern semantics, template application and duplicate-id validation settled by D-114/D-115/D-116/D-118; extends canonical file identity settled by D-122)*
- D-079: Slice G `.bed` migrator contract: document-model target, Diagnosed surfaces *(realizes D-009/D-049/D-057/D-068; numeric restrict migration refined by D-091)*
- D-080: `AttributeNameDuplicate` / `ValueLabelKeyNotInDomain` re-homed to the resolve seam *(realizes D-067; supersedes its "not re-homed" parenthetical)*
- D-081: Value-bin ordinal path (Slice H): identity + explicit order *(realizes the D-047-deferred path; refines D-060)*

### M3 (triple source + wide column object keys)

- D-082: M3 triple source contract: reader, orderings, object identity, absent-vs-missing *(realizes D-072)*
- D-083: Wide `column` object-key execution + `duplicate_object_policy` *(realizes D-064/D-034; interim key-index phase retired by D-098)*
- D-084: Ordinal string comparison is the project-wide rule *(adds principle EP-12)*
- D-085: M3 diagnostic taxonomy: structural triple/column-key codes *(refines D-067; interim key-index phase retired by D-098; `ObjectKeyValueInvalid` phase widened by D-099/D-111)*
- D-086: Shape-aware `.bed` migration sources *(refines D-079)*
- D-087: Symmetrical final-newline controls and v2 triple `.dat` compatibility

### Tier 1 M4 spec audit (pre-M4)

- D-088: Shared auto-calibration invariants + equal-frequency contract *(restates D-028; quantile formula settled by D-103; freeze generalized to all four calibration outcomes by D-122)*
- D-089: Equal-width range-mode contract
- D-090: `value_groups` execution contract
- D-091: `restrict_to` execution contract *(refines D-063/D-076/D-079; merged-`dedupe` restriction and type-directed migration clarified in place by the Tier 2 audit; `unknown_value_policy` key added in place at Slice F; realized by D-105)*
- D-092: Numeric `free_per_value` rendered labels

### Tier 2 M4 implementation-contract audit (pre-M4)

- D-093: Calibrated-state model: Core-owned resolved calibration outcomes; Plan consumes calibrated state *(refines D-036/D-088; realizes D-028's freeze face; realized by D-098)*
- D-094: M4 canonical fingerprint encodings; effective-configuration hashing rule *(appendix to D-069/D-077)*
- D-095: Bounded-memory calibration; subject-local triple deduplication; `GroupingStorageFailed` calibrate ownership *(refines D-088/D-082; realized by D-103)*
- D-096: Numeric `free_per_value` identity: locale-parsed, zero-canonicalized; normalized domain/label/order keys *(refines D-061/D-081/D-092)*
- D-097: Filter-only restriction diagnostics: unparseable values report under `unknown_value_policy` *(refines D-049/D-076)*

### M4 Slice A (calibration preparation)

- D-098: M4 preparation contract: two-stage source bootstrap, token-paired provenance, calibrated state *(realizes D-093; makes the schema-aware-resolve move D-083 reserved)*
- D-099: Triple structural validity widens to calibrate/emit *(refines D-082/D-085/D-095; probe phase added by D-111)*
- D-100: Per-phase `SourceValueUnparseable` aggregation across calibrate and emit *(refines D-097)*

### M4 Slice B (free_per_value + numeric identity)

- D-101: `free_per_value` executable: string + numeric identity, `CanonicalNumber`, scoped zero canonicalization, seam-normalized numeric keys, natural-order default *(realizes D-061/D-092/D-096)*

### M4 Slice C (equal_width + the shared cut engine)

- D-102: `equal_width` executable: shared `NumericCutBins` engine, sign-aware cut formula, `CutPrecision`, pending→executable substitution, streaming min/max calibration *(realizes D-088/D-089/D-093/D-094)*

### M4 Slice D (equal_frequency + percentile + the bounded quantile engine)

- D-103: `equal_frequency` + percentile executable: exact-rational ranks, feasibility precedence, sign-aware midpoints, the bounded quantile accumulator, subject-local triple dedup *(realizes D-088/D-089/D-093/D-094/D-095; catalogue-at-most-fan-in and merge-all scheduling clauses superseded by D-128)*

### M4 Slice E (value_groups)

- D-104: `value_groups` executable: first-match grouping, authored-presence matchers, raw-order pass-through discovery, ordinal over group labels, and the final deferred-kind retirement *(realizes D-022/D-055/D-090/D-093/D-094/D-095; completes D-070)*

### M4 Slice F (restrict_to execution + emit observability)

- D-105: `restrict_to` executable: exact numeric entries, existential object filtering, sequencing, filter-only diagnostics, emit observability, the policy-bearing `restrictions` container, and caller-discard output *(realizes D-021/D-057/D-063/D-076/D-079/D-091/D-097; amends D-091 in place; completes M4)*

### M5 (discovery / probe) pre-implementation audit

- D-106: Discovery `probe`: caller-selected shape, no inference, universal identity + nominal, one set-based observation pass *(refines D-003/D-036)*
- D-107: Draft naming/binding matrix, validity guarantee, and content inventory; no stored fingerprints/clock/tool version; caller enrichment
- D-108: Retention limit (100,000 default), strictly-greater truncation, prefix + `include` recovery, marker + always-written notes, probe options ownership; legacy retention-cap correction
- D-109: The general unbound streaming source session; adapter/engine split; package dependency direction; required EP-4 review
- D-110: Probe boundedness: per-attribute limit + three aggregate guards, deterministic accounting, hard-failure semantics, no spill machinery, inherited subject-metadata carve-out *(refines D-095)*
- D-111: Probe diagnostic governance: five future codes, two phase widenings, `ProbeSourceReadFailed` scope, cancellation is not a diagnostic, registry 70 → expected 75 *(refines D-067/D-085/D-099)*
- D-112: Probe determinism and cancellation: record-sequence input, no ambient state, byte/diagnostic repeatability, no partial artifact; the M5 verification suite
- D-113: Canonical-writer deterministic multiline wrapping for long top-level `declared_domain` arrays; private byte-pinned cutoff *(refines D-075)*

### M6 (templates + matchers) pre-implementation audit

- D-114: Template/matcher resolution: field-wise layering in declaration order, whole-value compounds, authored provenance, closed flat template surface *(refines D-060(c); realizes D-078's deferred application clauses)*
- D-115: Matcher selector contracts: whole-logical-name regex, resolved zero-based inclusive index range, exactly one selector *(realizes D-078's "M6 owns pattern semantics")*
- D-116: M6 diagnostic matrix: permanent conditions, per-effective-attribute granularity, deterministic order, transitional retirements *(refines D-067; deferred enum names and registry rows settled by D-120/D-121)*
- D-117: Naming: closed placeholder grammar, single-pass rendering, authored and rendered validity *(refines D-037(a)/D-074/D-092)*
- D-118: M6 application site and architecture: resolver-seam application, template/matcher-free Core, planner-owned naming *(makes D-078's Core boundary permanent)*
- D-119: M6 exit restated: one self-contained Ads spec, no attribute synthesis; the verification floor

### M6 Slice A (naming: carriers, grammar, rendering, plan guard)

- D-120: Naming executable: additive carriers, the `NameFormat` grammar owner, `FormalAttributeNameInvalid`, centralized parse ordering, and the `MergeDefaults` extension *(realizes D-116(7)/D-117/D-118; interim template/matcher reject retired by D-121)*

### M6 Slice B (template/matcher application at the resolver seam)

- D-121: Templates and matchers executable: the six resolve codes, single-owner source addressing, the wrapped whole-name regex, the effective-section fold, family assembly, and the last M6 retirement *(realizes D-114/D-115/D-116(1–6)/D-118)*

### M7 (CLI) pre-implementation adjudication

- D-122: M7 CLI contract: commands, process model, publication, manifest, freeze, authored-empty domains, per-command semantics, non-goals, distribution, and the exit floor *(revises D-071's empty-domain reading; refines D-026/D-078/D-088; realizes the CLI faces of D-005/D-028/D-067/D-077; implementation architecture realized by D-123; reach of parts 4–5 qualified by D-125)*

### M7 (CLI) implementation

- D-123: M7 implementation architecture and public surfaces: CLI boundary, parser, presentation, audit argv, filesystem identity, publication transaction, run manifest, freeze pairing, runtime options, and the CXT size advisory (registry 81 → 82) *(realizes D-122's implementation architecture; refines D-085's registry-timing rule; reach of point 7 qualified by D-125)*

### M8 (first scaling / benchmark pass)

- D-124: M8 benchmark architecture: one internal BenchmarkDotNet host, an explicit corpus/oracle layer, tier-gated selection with a pinned opt-in external corpus, two benchmark-only friend grants, and the conforming calibration fix the suite found *(invalidation/recheck rule amended for D-125's correction only, and live status corrected, by D-126; catalogue-at-most-fan-in restatement superseded by D-128)*

### M7 publication ownership (corrected under M8's first native gate)

- D-125: Lifetime-bound publication ownership, the exclusive-first native rename and its guarded fallback, the stated namespace precondition, and the closed package-metadata producer set *(qualifies the reach of D-122 parts 4–5 and D-123 point 7; corrects the defects D-124's first native run exposed; measurement claims corrected by D-126)*

### M8 evidence policy

- D-126: Limitation closure: the publication correction's incremental latency is inconclusive at the 5% bound, and the corrected-build allocation, validation and resource evidence is complete instead *(amends D-124's invalidation/recheck rule for D-125's correction only; corrects the stale live status of D-124 and the measurement claims of D-125)*

### M8.1 (pre-M9 performance pass)

- D-128: `QuantileAccumulator` uses a radix-F generation-tiered spill-run catalogue *(supersedes only D-103's catalogue-at-most-fan-in and merge-all scheduling clauses and D-124's restatement of that catalogue bound)*

### M8.2 spec review

- D-135: A composed spec needs an `[[attribute]]`, `base_index` is 0 or 1, and `size_advisory_bytes` is not negative; the binding resolves on its own for the probe preflight
- D-136: Tooling should store fingerprints only in a fully-frozen spec, and §14 defines one

Spec-field defaults are recorded in spec §21 items 1–11 (see the final section
of this file).

---

## Architecture

### D-001: Separable binding vs scaling in the spec

- **Status:** accepted
- **Decision:** the Bedrock spec splits into a `[binding]` layer (which data
  source, which columns/predicates, header, locale) and per-attribute scaling
  config (discretizer + scale + policies). Re-binding a spec to a new dataset
  variant changes only `[binding]`.
- **Why:** spec-first reuse across "compatible dataset variants" is a stated
  goal. Without the split, every attribute entry duplicates source-binding
  detail and portability is brittle.
- **Rejected:** a flat spec where each attribute carries its own source
  binding: simpler to write, but spec reuse across datasets becomes
  copy-paste-and-drift.
- **Affects:** Spec, Core, spec §5 ("The [binding] block") / §10 ("The [[attribute]] block").

### D-002: Orthogonal discretizer × scale model

- **Status:** accepted
- **Decision:** model conceptual scaling as two independent stages: a
  discretizer (raw value → bin label) and a scale (bin label → formal
  attributes). v2's `c`/`b`/`o` type codes become (discretizer, scale) pairs.
- **Why:** v2 conflated discretization and scaling through a single type code,
  which is why "continuous discrete" vs "continuous progressive" felt like
  special cases. Orthogonality reproduces every v2 mode and yields new
  combinations (e.g., equal-frequency bins + ordinal scaling) at zero extra
  design cost. The thesis already separates these conceptually (Ch 4.7 vs 4.8).
- **Rejected:** preserving v2's unified type code: familiar but blocks the
  combinations and bakes in the conflation.
- **Affects:** Core, Conversion, spec §11 ("Discretizer reference") / §12 ("Scale reference"). This is THE anchoring idea.

### D-003: Discovery is a separate operation, not implicit in convert

- **Status:** accepted; refined by D-036
- **Decision:** `Discover(source) → draft spec` (the `probe` command) is a
  distinct, optional operation. The **Plan** phase consumes a validated,
  *calibrated* spec; a data-reading **Calibrate** phase may precede it (D-036),
  so "fully-resolved spec" means *after* parse/validate/calibrate, not before
  convert starts. Emit is single-pass; Calibrate may add a bounded pre-pass.
  Discovery (draft-spec generation) is never folded into convert.
- **Why:** kills the streaming-vs-multi-pass tension at design time. A truly
  streaming converter needs all attributes known up front; auto-*discovery* at
  emit time would force a hidden pre-pass. Making discovery explicit (and
  distinct from calibration) keeps the converter clean. See D-036 for the full
  phase model.
- **Rejected:** auto-*discovery* folded into convert: convenient but couples
  two concerns and makes the streaming story murky. (Auto-*calibration* IS
  folded in by default (D-005, D-028), but it is an explicit phase, not
  discovery.)
- **Affects:** Discovery, Conversion; spec §7 ("Processing phases").

### D-004: Determinism rules centralized in the planner

- **Status:** accepted
- **Decision:** all ordering rules (attribute order, formal-attribute order,
  object order, ID assignment, locale) are encoded in the `ConversionPlan`,
  one rule per axis, all testable. See spec §17 ("Determinism rules").
- **Why:** "stable item ids" is only meaningful if every ordering axis has a
  single, explicit, tested rule. Scattering these across discretizers, scales,
  and writers guarantees drift and cross-machine non-reproducibility.
- **Affects:** Core (planner), spec §17 ("Determinism rules").

### D-005: Plan/Emit separation, with calibration before planning

- **Status:** accepted; phase order refined by D-036
- **Decision:** the pipeline is Parse/validate → **Calibrate** → **Plan** →
  **Emit** (D-036). `Plan(spec, sourceSchema) → ConversionPlan` is sync and
  pure and consumes an *already-calibrated* spec; the data-reading Calibrate
  step (for `equal_width` / `equal_frequency` cuts, absent domains,
  `unknown_value_policy = "include"`) runs *before* Plan, not after. `Emit(plan,
  source) → IAsyncEnumerable<EmittedObject>` streams; the matrix is never
  materialized in Core.
- **Why:** the plan is independently inspectable (powers `plan` and `validate`
  CLI commands), and the emit stage stays allocation-conscious at 10–100×
  scale. `.dat` is single-pass; `.cxt` is two-pass (needs |G| up front) but
  pass 1 carries only object identity, not the incidence matrix. (The original
  wording put Calibrate "before emit" but after Plan; D-036 fixes the order:
  Plan needs calibrated cuts to assign formal-attribute IDs, so Calibrate must
  precede it.)
- **Affects:** Conversion, Export, Cli; spec §7 ("Processing phases").

### D-006: Diagnostics package as the shared leaf

- **Status:** accepted; `BedrockResult<T>` alias dropped by D-042
- **Decision:** `FcaBedrock.Diagnostics` holds `Result<T, TError>`,
  `BedrockResult<T>` (alias), `Diagnosed<T>`, `BedrockDiagnostic`,
  `DiagnosticCode` (enum), `DiagnosticSeverity`, `DiagnosticLocation`.
  Referenced by every other package. This makes the package count 9
  (the original 8-package sketch + Diagnostics).
- **Why:** a domain-specific instantiation of the generic Result type, shared
  across CLI, future Reduce tool, etc. Aggregating diagnostics (`Diagnosed<T>`)
  lets `validate` surface all errors at once, not just the first.
- **Rejected:** (a) reusing the trading-platform Result library: work code
  stays at work, FcaBedrock is personal; keep them unmixed. (b) a `.Common`
  junk-drawer package: named `.Diagnostics` to resist scope creep.
- **Affects:** all packages, spec §16 ("Diagnostics").

### D-007: Target 10×–100× the v2 EMAGE workload

- **Status:** accepted
- **Decision:** v1 design target is ~7.3M–73M records (EMAGE was ~732k
  triples). Streaming with `System.IO.Pipelines`, span-based parsing,
  `ArrayPool`, `IAsyncEnumerable`, cancellation, and progress reporting are
  v1 concerns.
- **Why:** explicit answer to "how large." At this scale, transient
  per-object allocations and full-matrix materialization are disqualifying.
  Also means `.cxt` and `.dat` are not symmetric: at the top end a `.cxt` is
  tens of GB and ConExp can't open it; `.dat` is the scaling format, `.cxt`
  the human-readable/small-context one. Planner warns over a size threshold.
- **Affects:** Sources, Conversion, Export, spec §8 (`size_advisory_bytes`).

### D-008: Avalonia for the desktop UI

- **Status:** accepted
- **Decision:** the eventual desktop UI is Avalonia.
- **Why:** cross-platform from one codebase (the user wants cross-platform),
  mature MVVM, familiar XAML. WPF is Windows-only; MAUI is mobile-skewed and
  rough on desktop; Uno is heavyweight.
- **Rejected:** WPF (platform lock-in), MAUI/Uno (fit/maturity).
- **Affects:** Desktop only; zero effect on Core. UI is M9.

### D-009: New TOML spec format; one-way .bed migration

- **Status:** accepted (the migrator's document-model realization is D-079)
- **Decision:** the Bedrock spec is a new TOML format, record-per-attribute.
  A v2 `.bed` reader exists for one-way migration (load v2, save as TOML).
  `.bed` writing is not supported.
- **Why:** v2's `.bed` is parallel arrays under bracketed headers:
  index-aligned and brittle (one misaligned line silently corrupts the spec).
  TOML record-per-attribute is hard to misalign and easy to diff. No
  file-format-compat requirement was found.
- **Rejected:** preserving `.bed` format compatibility (no need, and it would
  perpetuate the brittleness); JSON/YAML (TOML reads better for this and
  matches the author's habits; `Tomlyn` is solid on .NET).
- **Affects:** Spec, spec §2+ (file structure).

### D-010: Modelled-but-rejected scales/features carry forward-compat

- **Status:** accepted
- **Decision:** `interordinal`, `biordinal`, `contranominal` scales, and
  `composite` object keys, are parsable in the v1 spec format but the v1
  planner rejects them with a specific `*NotImplementedV1` diagnostic.
- **Why:** lets v1 spec files declare these without a format break when they're
  implemented. Near-zero cost (a parse case + a planner guard). The user
  preferred "model it now."
- **Rejected:** not modelling them (would force a spec-version bump later).
- **Affects:** Spec, Core (planner), spec §12.4 / §5.4 / §20.

### D-011: v2 byte-equality is a CLI flag, not a spec setting

- **Status:** accepted
- **Decision:** `--v2-compat` on the `convert` command overrides `[output]`
  to v2's exact byte conventions (CRLF, `30to<40` bin labels, trailing space
  on `.dat` lines). Not expressible as spec fields.
- **Why:** v2-compat is a one-time migration/testing concern, not a property
  of a spec. Keeps the spec format clean. M1's byte-equality goal uses this
  flag.
- **Rejected:** a `bin_label_style = "math" | "v2"` spec field (originally
  proposed, then pulled): pollutes the spec with a transient concern.
- **Affects:** Cli, Export, spec §8 ("The [output] block") / §18 ("Output formats").

### D-133: A Bedrock spec is a UTF-8 TOML 1.1.0 document; the TOML version is independent of `[spec].version`

- **Status:** accepted
- **Date:** 2026-09-26
- **Decision:** spec §2 declares a Bedrock spec a UTF-8 TOML 1.1.0 document instead of a TOML 1.0
  document. The whole TOML 1.1.0 grammar is the contract, not a subset, and the version is spelled
  `1.1.0` in the spec and in the diagnostic. The Bedrock rules for sections, keys, types and values
  still apply on top of that grammar, unchanged.
  - **What TOML 1.1.0 adds.** The tagged TOML 1.1.0 changelog
    (<https://github.com/toml-lang/toml/blob/1.1.0/CHANGELOG.md>) lists four syntax additions, and
    the specification (<https://toml.io/en/v1.1.0>) and its grammar define them; the changelog's
    other entries are clarifications. Inline tables may contain newlines, comments and a trailing
    comma. Basic strings gain the `\e` and `\xHH` escapes. Seconds become optional in date-times
    and times, and an omitted value means `:00`. Each addition is another spelling of a value that
    TOML 1.0.0 already expresses: the same table on one line, `\u001B` and `\u00HH`, and explicit
    seconds. The canonical writer (D-075, D-113) is unchanged.
  - **No universal compatibility claim.** Neither the specification nor the changelog states that
    every valid TOML 1.0.0 document stays valid, so this decision does not claim it. The evidence
    is representative only: the existing specs in the test suite still read, and the new grammar
    tests read each addition beside a TOML 1.0.0 spelling that gives the same document.
  - **Accepted input does not change.** Since the reader was introduced (D-075) it has parsed with
    Tomlyn 2.10.1, whose package documentation says it targets TOML 1.1.0 only and does not support
    TOML 1.0. That parser call is unchanged, so the reader accepts and rejects exactly what it did
    before.
  - **The diagnostic.** A Fatal `SpecTomlInvalid` syntax-error message now begins
    `Not valid TOML 1.1.0: ` instead of `Not valid TOML 1.0: `. Its code, severity, phase and
    location are unchanged, and the parser's text after the prefix is not a contract.
  - **`[spec].version` stays `1`.** It versions the Bedrock schema, which §3 bumps on incompatible
    schema changes. This declaration changes no section, field, type, default or meaning and
    rejects no spec that the reader accepted, so it is not a schema change.
  - **Parser upgrades.** The contract is the TOML 1.1.0 grammar, not Tomlyn's observed behaviour,
    which the grammar tests record as evidence. Tomlyn stays exact-pinned (D-075), and upgrading it
    is a reviewed change that must keep those tests passing. Input that a parser accepts although
    TOML 1.1.0 forbids it carries no compatibility promise. Adopting a later TOML version needs its
    own decision.
- **Why:** §2 said TOML 1.0 while nine spec examples write inline tables across lines, and the
  reader has parsed TOML 1.1.0 since it was introduced. EP-8 allows either correction. Declaring
  the grammar that the reader and the examples already use changes no accepted input, keeps every
  example byte-identical and needs no added syntax check. The maintainer chose TOML 1.1.0 over the
  options below.
- **Rejected:** keeping TOML 1.0 and rewriting the nine examples, because the reader would still
  accept TOML 1.1.0 and §2 would stay untrue unless an added check rejected syntax that every build
  has accepted; declaring a subset, such as TOML 1.0.0 plus multi-line inline tables, because the
  pinned parser has no such mode and enforcing it needs the same check; and bumping
  `[spec].version`, because the grammar is not the schema and a bump would signal an incompatible
  schema change where none exists.
- **Affects:** spec §2; `FcaBedrock.Spec` (the `SpecTomlInvalid` syntax-error message);
  `FcaBedrock.Diagnostics` (the `SpecTomlInvalid` documentation); comments that quote §2 in
  `FcaBedrock.Cli` and its tests; grammar tests in `FcaBedrock.Spec.Tests`; `docs/roadmap.md` (the
  M8.2 scope). The only change in product behaviour is that message text, which library callers
  receive and the CLI prints on stderr. No public API, CLI grammar, exit code, diagnostic code,
  severity, phase, registry count, schema version, fingerprint, `fp_format`, or `.cxt`, `.dat`,
  canonical-TOML or manifest byte changes.

---

## Scope / feature decisions

### D-020: v1 scale and discretizer surface

- **Status:** accepted
- **Decision:** v1 implements discretizers `identity`, `manual_cuts`,
  `free_per_value`, `equal_width`, `equal_frequency`, `value_groups`; and
  scales `nominal`, `dichotomic`, `ordinal`. `std_dev` binning was considered
  and **removed entirely** (not deferred). Advanced scales are D-010.
- **Why:** this set reproduces the v2 behavior targeted for current
  compatibility / golden tests, plus value grouping and ordinal-over-groups,
  **except std-dev binning**, which is intentionally removed rather than
  deferred because there is no current user need and it is not part of the M1
  compatibility target. (v2's source has a std-dev branch, and SPARQL2FCA had
  std-dev variants, so "all v2 behavior" is qualified here on purpose.) Cut if
  a real need appears later; re-adding is non-breaking.
- **Affects:** Core, spec §11 / §12 / §21.

### D-021: Three filtering levers kept distinct

- **Status:** accepted; lever (1) refined by D-032
- **Decision:** (1) `include = false` drops formal-attribute generation and
  incidence emission, but does **not** suppress the attribute's own
  `restrict_to` (the filter-only pattern; see D-032);
  (2) `declared_domain` limits which raw values become formal attributes;
  (3) `restrict_to` filters objects (OR within an attribute, AND across
  attributes), operating on **raw values before discretization**.
- **Why:** v2 has all three but the UI blurs them. They compose and have
  different semantics: e.g., you can restrict on a raw value range that isn't
  even a bin boundary, and matched objects still cross under their actual bin.
  The restrict-on-raw-value-before-discretization rule is required: the thesis
  EMAGE walkthrough relies on it.
- **Affects:** Core (planner filter stage), spec §10.3 / §10.4.

### D-022: value_groups discretizer (clustering raw values)

- **Status:** accepted
- **Decision:** a `value_groups` discretizer maps many raw values to one bin
  label, via explicit `values`, a regex `pattern`, or both, with an
  `unmatched` policy (`skip` | `other` | `passthrough`). Pairs with nominal
  (mutually-exclusive groups) or ordinal (cumulative, needs `order`).
- **Why:** requested: collapse {Bachelors, Masters, PhD} → "Uni-Degree" to
  reduce clutter. Falls out of the orthogonal model as just another
  discretizer. Regex form is essential for high-cardinality coded data
  (ICD-10, gene IDs). Value aliases (synonyms → one label) are the same
  mechanism, no extra feature.
- **Affects:** Core, spec §11.6 ("value_groups").

### D-023: value_labels (raw value → display label)

- **Status:** accepted
- **Decision:** a per-attribute `value_labels` map renders raw values under
  display names in formal-attribute output (v2's `[Attribute Categories]` vs
  `[Category Values]`). Applies only under `identity` / `free_per_value`.
- **Why:** v2 outputs `gill-size-broad`, not `gill-size-b`. Without it,
  mini-mushroom byte-equality (M1) fails and output names are ugly. Affects
  `output_fingerprint` (now split per-format, D-051), not `schema_fingerprint`.
- **Affects:** Core, Export, spec §10.8 ("value_labels").

### D-024: Object grouping by composite key (deferred to v1.1)

- **Status:** accepted (deferred)
- **Decision:** `object_key.mode = "composite"` (rows sharing a derived key
  merge into one formal object, attribute sets unioned/intersected) is modelled
  in the spec but the v1 planner rejects it.
- **Why:** real EMAGE-style need ("all observations for this gene as one
  object"), but it turns single-pass conversion into a sort-then-group stage
  needing care at 73M rows. Model now, implement once the streaming pipeline
  is proven.
- **Affects:** Sources, Conversion, spec §5.4 ("Object key resolution").

### D-025: Post-context reductions live in a sibling tool

- **Status:** accepted
- **Decision:** clarification, reduction, and minimum-support filtering are
  NOT part of the conversion pipeline. They operate on a produced `.cxt`/`.dat`
  and belong in a separate tool (working name `FcaBedrock.Reduce`).
- **Why:** they share no state with convert and apply equally to contexts from
  other software. Keeps the conversion pipeline lean. Thesis flagged
  minimum-support as future work.
- **Affects:** future sibling tool; keeps Conversion/Export focused.

### D-026: Reproducibility: provenance block + run manifest

- **Status:** accepted *(manifest schema refined by D-122)*
- **Decision:** specs carry an optional `[provenance]` block (author, source
  URL/hash, lineage). **By default, each `convert` run emits** one
  **`BASE.manifest.toml`** sidecar *(refined by D-122: ordered `[[run.outputs]]`
  entries for every committed artifact)*; **`--no-manifest` suppresses it**. It
  carries tool version, spec/input hashes, per-artifact hashes, command line, and
  the **complete retained calibration** *(refined by D-122/§15: ordered
  `[[run.calibrations]]` covering all four outcome kinds; originally recorded
  auto-calibrated cuts only)*.
- **Why:** the thesis frames Bedrock files as a reproducible record of how data
  was appropriated. Citing a manifest is sufficient for a reproducibility
  audit. Auto-discretizer calibration is captured so on-the-fly runs stay
  reproducible without forcing a separate `calibrate` step.
- **Affects:** Spec, Cli, spec §4 ("The [provenance] block") / §15 ("Run manifest"). D-122.

### D-027: Spec composition via `extends`

- **Status:** accepted
- **Decision:** a spec may `extends` a base spec; merge rules are defined
  (binding/defaults per-field override, templates/matchers concatenate,
  attributes override by name, cycles rejected). Fingerprints computed over the
  resolved spec.
- **Why:** the EMAGE workflow is one schema, many analyses (this gene vs that,
  this stage-range vs that). Without composition, analyses are copy-paste and
  drift. Hard to retrofit (changes parser resolution order), so committed early.
- **Affects:** Spec, spec §13 ("Composition: extends").

### D-028: Calibration on-the-fly by default; `calibrate` to freeze

- **Status:** accepted
- **Decision:** auto-discretizers (`equal_width`, `equal_frequency`) calibrate
  from the input at convert time by default, with cuts captured in the run
  manifest. `fcabedrock calibrate` bakes computed cuts into a copy of the spec
  as `manual_cuts` for version-controlled, fully-frozen reproducibility.
- **Why:** convenience (one command) without losing reproducibility (manifest
  captures the cuts). `calibrate` serves the "cuts under git" workflow.
- **Affects:** Conversion, Cli, spec §11.4 ("equal_width") / §15 ("Run manifest").

---

## Round 1 spec audit (post-design review)

These resolve ambiguities and conflicts found auditing `bedrock-spec-v1.md`
against the settled triple/filter/fingerprint assumptions. They refine, not
reverse, the earlier decisions.

### D-030: Triple multi-value union; scale decides folding

- **Status:** accepted
- **Decision:** for triple input, rows sharing an object key accumulate crosses
  by union; duplicate identical triples are idempotent; this is normal input,
  never a duplicate-object condition. Whether several values fold into one
  formal attribute is a property of the chosen *scale* (`dichotomic` /
  `value_groups`), not a source-level toggle.
- **Why:** matches v2 (coalesces by subject, accumulates crosses). A
  source-level collapse toggle would be a second way to express what the scale
  already expresses, violating principles EP-5 ("One project-standard way per
  concern").
- **Rejected:** a configurable collapse flag on the source: declined for the
  one-way reason above.
- **Affects:** Sources, Conversion, spec §5.3.1.

### D-031: `subject_grouped` requires contiguous subjects

- **Status:** accepted
- **Decision:** under `ordering = "subject_grouped"`, all rows for a subject
  MUST be contiguous; a recurring subject after an intervening one is
  `TripleSubjectNotContiguous` (Error), stop. Non-grouped input must declare
  `ordering = "unordered"`.
- **Why:** `subject_grouped` exists precisely to guarantee single-pass,
  zero-buffer streaming (D-007). Silently falling back to buffering would void
  the performance contract the user opted into without telling them.
- **Rejected:** silent buffering on non-contiguous subjects.
- **Affects:** Sources, spec §5.3.1.

### D-032: Filter-only attributes (`include = false` keeps `restrict_to`)

- **Status:** accepted
- **Decision:** `include = false` suppresses formal-attribute emission and
  incidence but does **not** suppress the attribute's own `restrict_to`. This
  is the filter-only pattern: filter objects by a field without emitting it as
  a column. Consistent across wide and triple input.
- **Why:** v2 effectively allowed filter-only restriction for wide input but
  not consistently for 3-column; vNext makes it uniform. Resolves the §10.1/§19.4
  contradiction where the EMAGE example relied on restriction from an attribute
  whose text implied it wasn't emitted.
- **Affects:** Core (planner), spec §10.1 / §10.4 / §19.4.

### D-033: `source` may repeat across attributes

- **Status:** accepted (supersedes the blanket source-duplicate ban; the rule
  now lives in spec §10.2)
- **Decision:** two attributes MAY share one `source`, enabling multiple
  scalings of one field (e.g. `age` nominal bins + `age_ordinal` thresholds, or
  an emitted attribute plus a filter-only attribute on the same field). `name`
  must remain unique; identical resulting formal-attribute identity is rejected
  with `FormalAttributeCollision` (Error).
- **Why:** the thesis's conceptual scaling explicitly allows multiple scales
  over one many-valued attribute (interordinal is literally two ordinal scales
  on one field). The blanket ban would block legitimate, useful contexts.
- **Rejected:** keeping the ban (would forbid multi-scale, a real need).
- **Affects:** Core (planner), spec §10.2; new diagnostic `FormalAttributeCollision`.

### D-034: Duplicate object keys defined by key mode; `fail` default

- **Status:** accepted (supersedes the inconsistent `duplicate_policy` /
  `duplicate_object_policy` wording)
- **Decision:** behavior depends on object-key mode. `row_index` and triple
  input: not applicable (unique by construction / accumulation is normal). Wide
  `column` mode: `duplicate_object_policy` defaults to **`fail`** (`keep` =
  index-suffix + Warning; `dedupe` = union + Info, both opt-in). The stray
  `"merge"` value and the `duplicate_policy` misnomer are removed; cross-row
  merge by a derived key remains the deferred `composite` feature (D-024).
- **Why:** choosing a column as the object key asserts it identifies objects; a
  duplicate means that assertion is false, so surfacing it (fail) beats silently
  inventing `P001#2` (keep) or fusing two rows into an impossible object
  (dedupe). v2 used row index for wide, so there's no v2 precedent to preserve.
- **Rejected:** `keep` as default (surprising silent rename of the key column).
- **Affects:** Core (planner), Sources, spec §5.4 / §6.1; diagnostic
  `DuplicateObjectKey` (severity per policy).

### D-035: Fingerprint scopes: schema = planned columns only

- **Status:** accepted; the single `output_fingerprint` is split into per-format
  `cxt_output_fingerprint` / `dat_output_fingerprint` by D-051 and its plan-derived
  canonical encoding pinned by D-053 (refines D-004-adjacent fingerprint wording)
- **Decision:** `schema_fingerprint` hashes **only the final ordered list of
  planned formal-attribute canonical identities** (logical name + scale +
  canonical bin/threshold key + operator), as produced by Plan. No policy is an
  independent input: `missing_policy = "as_attribute"`,
  `unknown_value_policy = "include"`, `binding.locale`, declared-domain order,
  and cuts all enter *only* through their effect on that resolved list. Two
  plans with the same ordered canonical-identity list fingerprint identically.
  It excludes `duplicate_object_policy`, `restrict_to`, object-key mode, and
  object ordering (those select/merge *rows*). A single `output_fingerprint`
  covered the effective plan and output settings; D-051 replaced it with
  per-format output fingerprints.
- **Canonical identity vs rendered name:** canonical identity (→ `.dat` columns,
  → `schema_fingerprint`) is distinct from the rendered `.cxt` name (produced by
  `formal_attribute_format` / `display_name` / `value_labels`, → only the
  output fingerprint, the `.cxt` one since D-051). Identity collision →
  `FormalAttributeCollision`; rendered-name collision →
  `FormalAttributeNameCollision`; both Error.
- **Why:** `.dat` column identity is the schema; listing policies as *direct*
  inputs double-counted their effect (the extra/included columns are already in
  the planned list). Hashing the planned list alone is exact and simpler.
- **Affects:** Spec (fingerprinting), spec §10.2 / §14 / §17; diagnostics
  `FormalAttributeCollision`, `FormalAttributeNameCollision`.

### D-036: Four-phase processing model; convert calibrates, never discovers

- **Status:** accepted (makes D-003 / D-005 concrete and normative in the spec);
  the "NaN/∞ → missing" clause superseded by D-050
- **Decision:** Parse/validate → Calibrate → Plan → Emit. Calibrate is the only
  data-reading phase that resolves data-dependent schema (absent
  `declared_domain`, auto-discretizer cuts, `unknown_value_policy = "include"`).
  `convert` auto-calibrates by default with cuts captured in the manifest, but
  never *discovers* (draft-spec generation is the separate `probe`). Absent
  `declared_domain` is calibrated with an `ObservedDomainUsed` (Warning).
  Auto-discretizer determinism rules (parse via `binding.locale` (default
  invariant), total-order sort, insufficient-distinct →
  `CalibrationDataInsufficient`, NaN/∞ → missing) are fixed now; exact quantile
  formula and label precision settled at M4.
- **Why:** a coder must know whether `Convert(spec, source)` may run a schema-
  changing pre-pass. Answer: yes (calibrate), but it's an explicit phase, and it
  never silently *discovers*. Aligns the spec with D-003/D-005.
- **Affects:** Conversion, Cli, Discovery, spec §7 / §10.3 / §10.6 / §11.5.

---

## Round 2 spec audit (contract-tightening)

Refinements from the second audit pass.

### D-037: Scale-specific default naming; emitted-field discipline

- **Status:** accepted; (b) superseded by D-049: `EmittedFieldOnExcludedAttribute`
  is removed and `include = false` is an authoring toggle (retained config is
  ignored, not an error). (a) stands.
- **Decision:** (a) default formal-attribute naming is scale-specific:
  nominal `{column}-{value}`, ordinal `{column}-{scale_op}{value}`, dichotomic
  `{column}` alone, `as_attribute` adds `{column}-missing`; an explicit
  `formal_attribute_format` overrides the scale default entirely. (b)
  `discretizer`/`scale` are required only when `include = true`; emitted-only
  fields on an `include = false` attribute were first an Error
  (`EmittedFieldOnExcludedAttribute`), which D-049 removed.
- **Why:** (a) M1 byte-equality requires dichotomic `bruises?` not
  `bruises?-bruises`; the old flat `{column}-{value}` default was wrong for
  dichotomic and contradicted the examples. (b) the old "every attribute needs
  discretizer+scale" rule made the new filter-only examples invalid.
- **Affects:** Core, Export, spec §10.7 / §10.9 / §12.2; diagnostic
  `EmittedFieldOnExcludedAttribute` (removed by D-049).

### D-038: Date support deferred; v2 six-type-code map confirmed

- **Status:** accepted (supersedes the Round 2 "implement date now" direction)
- **Decision:** date-valued scaling is **deferred** from v1. `value_type =
  "date"` is reserved but parsed-then-rejected by the v1 planner with
  `DateValueTypeNotImplementedV1`. v1 treats date-like values as strings unless
  a future date value type/discretizer lands. The `value_type` field survives
  with live values `"string"` / `"number"` (so un-deferring later is a
  non-breaking addition of the `"date"` value, not a new field).
- **Why:** continuous *numeric* support is the v1 priority. Full date support
  pulls in cut syntax, `DateOnly`/`DateTime` semantics, day-space binning, label
  rounding, and date-specific diagnostics: complexity not worth front-loading
  before coding.
- **v2 finding:** verified against `frmFcaBedrock.vb`, v2 has **six** type
  codes: `c` categorical, `b` boolean, `o` continuous-numeric, `d` date, `n`
  ordinal, plus missing. The earlier "dates as continuous" reading was wrong;
  `d` is a distinct type using `DateTime.Parse`. So v1 deferring `d` is a
  *conscious parity deferral*, not an oversight, and D-020's "reproduces v2
  behavior" is correspondingly qualified (date deferred, std-dev removed).
  v2's `n` (Ordinal) type = our `ordinal` scale over an ordered categorical
  discretizer (already covered, no new feature). The 60% date auto-detect
  heuristic is retained only as future `probe` behavior when date lands.
- **Rejected:** implementing date in v1 (Round 2 direction): reversed for
  scope/priority. Fully removing `value_type` until date lands: kept the field
  so re-enabling is non-breaking.
- **Affects:** Sources, Conversion, Core, spec §5.1 / §10.2 / §11.7 / §20;
  lineage.md (v2 type-code map, `d` marked deferred); diagnostics
  `DateValueTypeNotImplementedV1`, `SourceValueTypeInvalid` (replacing the
  Round 2 `DateFormatInvalid` / `DateGranularityNotImplementedV1`).

---

## Process / governance

### D-029: Engineering principles formalized as docs/engineering-principles.md

- **Status:** accepted; owner file renamed and renumbering forbidden by D-129
- **Date:** 2026-06-20
- **Decision:** project-specific engineering principles live in
  `docs/engineering-principles.md` (21 principles across Working discipline + Correctness,
  Architecture, Performance, Testing). Mechanical rules stay in `.editorconfig`
  / analyzers. Changes to the principle set are governance changes and get a
  `decisions.md` entry; internal cross-references use number + short title so
  renumbering stays painless.
- **Why:** gives sessions (especially agents) a checkable invariant set
  distinct from operational guidance (then `CLAUDE.md`, now `AGENTS.md` under
  D-048) and per-decision rationale (this file).
- **Affects:** all packages; governance. See also the mechanical-backstop
  follow-up in `roadmap.md` (M0).

### D-039: Test conventions + ArchUnitNET for architecture tests

- **Status:** accepted; the conventions moved to `AGENTS.md` by D-048
- **Date:** 2026-06-23
- **Decision:** the project test conventions, first recorded in `CLAUDE.md`
  ("Testing conventions"), are: one `FcaBedrock.<Package>.Tests` project per
  package (created when the package gains code), unit test class
  `<ClassUnderTest>Tests` mirroring the production type's folder/namespace,
  unit/behavioural methods named `Subject_When<Condition>_Then<Outcome>`, and
  architecture tests named `Subject_Should<Outcome>`. Architecture/dependency
  tests are written with **ArchUnitNET** (`TngTech.ArchUnitNET.xUnit`),
  replacing the hand-rolled reflection harness.
- **Why:** one documented test style (EP-5), and a fluent arch-test library whose
  type-level dependency analysis can enforce invariants reflection cannot, notably
  EP-13 ("Core is pure: no `System.IO`") once Core has code at M1. The cross-package
  layering and cycle rules read declaratively and extend cleanly as packages land.
- **Rejected:** NetArchTest.Rules: simpler fluent API but less expressive and less
  actively maintained; the hand-rolled reflection harness: zero-dependency but
  assembly-reference granular, so it cannot see type-level dependencies like
  `System.IO.File`. On M0's empty assemblies all three are equally vacuous, so the
  arch suite keeps a non-vacuous "production assemblies were loaded" guard.
- **Affects:** tests (`FcaBedrock.Architecture.Tests`, `*.Tests` naming),
  `CLAUDE.md`, `Directory.Packages.props`.

### D-040: Shared `tests/Directory.Build.props`; MTP-only (no Microsoft.NET.Test.Sdk)

- **Status:** accepted
- **Date:** 2026-06-23
- **Decision:** common test-project configuration is centralized in a single
  `tests/Directory.Build.props` instead of being repeated per `.csproj`:
  `OutputType=Exe`, `IsTestProject=true`, `IsPackable=false`, the zero-tests guard
  (`--minimum-expected-tests 1`), the `xunit.v3` package reference, and the `Xunit`
  global using. Its first line re-imports the repo-root `Directory.Build.props` via
  `$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))`.
  Each `FcaBedrock.<Package>.Tests` project now carries only its own
  references/items. `Microsoft.NET.Test.Sdk` is dropped: xUnit v3 self-hosts
  Microsoft.Testing.Platform, so `OutputType=Exe` + `xunit.v3` is sufficient for
  `dotnet test` (MTP runner per `global.json`); the CLI build and test run, the guard,
  golden fixture copying and Visual Studio 2026 Test Explorer were verified without it.
  `Microsoft.NET.Test.Sdk` is therefore removed from `Directory.Packages.props`
  entirely; if some future tooling needs VSTest, re-add the `PackageReference` to
  the shared props (one line) rather than per project.
- **Why:** the convention is one test project per package (D-039), so the six
  duplicated boilerplate lines would be re-pasted for every future package, and
  the zero-tests guard relied on each session remembering to copy it. Centralizing
  makes the guard automatic and shrinks each `.csproj` to its unique parts.
- **Rejected:** a `tests/Directory.Build.props` *without* the parent import: it
  shadows (does not merge with) the root props, silently dropping
  `TargetFramework`, `Nullable`, analyzers, `TreatWarningsAsErrors`, and
  `RepoRoot` (the last breaks the Golden fixture-copy glob). Putting the shared
  block in the **root** `Directory.Build.props` under
  `Condition="'$(IsTestProject)'=='true'"`: fails, because props are imported
  before the csproj body sets `IsTestProject`; a root `Directory.Build.targets`
  with that condition works but is less discoverable and mixes test config into a
  root file. Keeping `Microsoft.NET.Test.Sdk` unconditionally: unnecessary VSTest
  weight for the MTP/CLI path.
- **Affects:** tests (`tests/Directory.Build.props`, both `*.Tests` csproj),
  `CLAUDE.md`. Refines D-039.

---

### D-048: `AGENTS.md` canonical; `CLAUDE.md` imports it; no symlink

- **Status:** accepted
- **Date:** 2026-06-26
- **Decision:** the shared, tool-agnostic agent guidance formerly in `CLAUDE.md`
  is now canonical in **`AGENTS.md`** (the cross-tool convention Codex and other
  agents read natively). `CLAUDE.md` is reduced to a one-line Claude Code import
  shim, `@AGENTS.md`, which inlines the file into context identically to inline
  content. Any Claude-only instructions go *after* that import line; Codex-only
  runtime config stays in Codex's own config, never the shared file. Historical
  `CLAUDE.md` mentions elsewhere in this log (e.g. D-040 "Affects") are left as-is.
- **Why:** a Claude session and a Codex session working the same repo need one
  source of truth, not two files that silently drift. The import costs Claude
  nothing and Codex reads `AGENTS.md` directly.
- **Rejected:** a `CLAUDE.md → AGENTS.md` **symlink**: this checkout has
  `core.symlinks=false` (the Windows default; `core.autocrlf=true` compounds it),
  so a committed symlink checks out as a one-line text file containing the path,
  i.e. Claude would load the literal string `AGENTS.md` as its entire guidance.
  Duplicating the content across both files: guaranteed drift.
- **Affects:** `AGENTS.md`, `CLAUDE.md` (new import shim). Process/tooling only; no
  code, no output bytes.

### D-073: Decision index; sessions read it first, then relevant entries

- **Status:** accepted; its rejection of summarizing entries in place narrowed by
  D-129 rule (1)
- **Date:** 2026-07-04
- **Decision:** this file opens with a compact **index**: one line per
  decision, grouped by section, status originally annotated only where
  superseded or refined (the index introduction now states the annotation
  rule). The `AGENTS.md` session workflow changes from "read
  `docs/decisions.md`" to "read the index, then the entries the task touches";
  reading the log in full remains the bar before proposing architectural
  changes (the rule at the top of `AGENTS.md`). A new decision entry MUST also
  add its index line. The same documentation pass restores spec §21 to its
  documented scope (this file's preamble): items 1–11 remain the
  spec-field-default record; items 12–25 become one-line cross-references to
  the owning spec sections and decisions, with their item numbers preserved
  (they are referenced by number, e.g. "§21-item-16" in D-051).
- **Why:** the log is append-only and was a mandatory full read for every
  session, an unbounded per-session cost (~90 KB and growing) when most tasks
  touch a handful of entries. An index converts the default read to
  index + relevant entries without touching history or weakening the
  architectural-change bar. Separately, §21 items 12–25 had drifted beyond the
  documented "spec-field defaults" contract, restating decisions in a third
  place that could drift from both the spec body and this log.
- **Rejected:** splitting or archiving old entries (breaks `D-NNN` references
  and hides the reasoning trail the append-only rule exists to preserve);
  keeping the mandatory full read (unbounded); summarizing entries in place
  (rewrites history); renumbering or deleting §21 items (breaks external
  item-number references).
- **Affects:** `docs/decisions.md` (index), `AGENTS.md` (workflow step 1,
  Current-status trim), spec §21 (items 12–25 → cross-references; §21 is
  informative, so no normative change). Process/docs only; no code, no output
  bytes.

### D-129: M8.2 writing governance: in-place decision compaction, `EP-` and `WP-` identifier ownership, the authored inventory, and the mechanical checker boundary

- **Status:** accepted; the checker portion of rule (5) superseded by D-130; the heading-anchor clause of rule (1) superseded by D-131 for the one-time heading-separator migration; rule (2) amended in place on 2026-09-28 for EP-18's hot paths, EP-20's title and the new EP-23
- **Date:** 2026-09-22
- **Decision:** five governance rules for the M8.2 authored-text pass.
  **(1) Compaction.** A decision entry may be shortened *in place*. Its ID, heading anchor, actual
  rule, decisive rationale and tradeoff, consequences and limits, status, meaningful dates,
  supersession links and position in historical order all survive. What may go is chronological run
  narration, retry and custody story, text about obligations that were open at the time, and
  repeated evidence tables. An earlier rule must never be reworded so that it reads as though it
  always contained a later correction.
  **(2) Engineering principles.** `docs/engineering-principles.md` owns the engineering invariants,
  numbered `EP-1` through `EP-23`. Numbers, titles and meanings are fixed, and renumbering is
  forbidden. Every authored citation in this repository uses that family. No alias anchor
  and no compatibility stub carries any earlier spelling, and no earlier spelling or path remains in
  authored repository state. Git is the record of what the names were. Amended in place on
  2026-09-28: EP-18's allocation-audited paths also include the calibration data pass,
  post-intake quantile merge/replay and `probe`'s observation pass. These traverse input records
  or calibration value counts; final cut construction after population traversal stays a cold
  path. EP-20's title says "never run in the normal suite" instead of the figurative "never gate
  the normal suite", and its heading anchor changes with it. EP-23 joins working discipline:
  external library behavior is verified at its source, not recalled, because recalled library
  behavior such as a method's exception contract has proved wrong in practice.
  **(3) Writing policy.** `docs/writing-principles.md` is the single vendor-neutral owner of how
  this repository writes documents and comments, numbered `WP-1` onward. It binds humans and
  assistants. `AGENTS.md` carries a mandatory pointer to it, never a copy, and an instruction shim
  may satisfy that pointer through an explicit mandatory import. No vendor skill is required; an
  adapter, if one is ever added, reads this file rather than restating it.
  **(4) Inventory and owner map.** Every authored document and every comment-bearing source, test,
  benchmark, engineering, workflow, configuration and instruction file is inventoried from
  tracked-file enumeration, with owner, input class, risk, packet, disposition, validation and
  review state recorded. Review state distinguishes foundation routing, a reviewed mechanical
  migration, and completed content review, so a mechanically migrated file is never recorded as
  content-reviewed. The detailed ledger is private working state and is not a repository artifact;
  the public artifact is the owner map in `AGENTS.md`.
  **(5) Checker boundary.** `eng/` carries one small local command that exits non-zero only for
  reliable mechanical failures: invalid required UTF-8 or prohibited control bytes, a resolvably
  broken local file or explicit-anchor reference, a missing mandatory policy pointer, a forbidden
  obsolete canonical path or identifier, and a malformed or stale suppression. Sentence length,
  passive voice, dashes and listed vocabulary are warnings for human review. They never fail the
  command, and the command never rewrites text. Suppressions are per-rule and either next-line or
  one-path narrow, carry a reason, and fail when malformed or stale. The command adds no dependency
  beyond tools the repository already requires and does not duplicate the existing source-hygiene
  ownership.
  M8.2 closes finitely: a private acceptance receipt may record integration, so no self-referential
  public documentation commit is required.
- **Why:** D-073 made the index the routine read and explicitly rejected "summarizing entries in
  place (rewrites history)". That rejection was aimed at losing the reasoning trail, not at removing
  campaign narration that never carried reasoning. Rule (1) narrows it to exactly that: the
  reasoning trail is enumerated and protected, and only material with no lasting reader value may
  go. Rules (2) and (3) follow one policy, that an identifier prefix names its owning file; with a
  second principles document in the repository, a prefix that does not name its file stops being
  self-describing. The two-letter forms were chosen over single letters because this repository's
  canonical number formatter emits uppercase exponent literals such as `1E-05` and `5E-324`, and a
  single-letter `E-` prefix would sit beside them in the fingerprinting and planner code that cites
  principles most. Rule (4) exists because a text search cannot prove coverage of a repository, and
  because the per-file detail that makes the pass executable is private working state with no
  lasting reader value. Rule (5) keeps the checker small: usefulness, repetition and semantic
  preservation are review judgments, and a tool that failed on them would either be wrong or grow
  into a prose-analysis platform.
- **Rejected:** a principle prefix that does not name its owning file, which stops being
  self-describing once a second principles document exists. Permanent alias anchors and an old-path
  stub, which keep an obsolete spelling as a migration diary when Git already holds it. Renumbering
  principles, which no rename requires. An in-tree archive of
  pre-compaction prose, which duplicates what Git already stores. A checker that fails on style,
  which cannot be made reliable and would invite automatic rewriting of exact contract language. A
  second owner for the source-hygiene encoding rules, which `tests/FcaBedrock.Architecture.Tests`
  already holds for `.cs` under `src/` and `tests/`.
- **Affects:** `docs/engineering-principles.md` (the canonical engineering-principle owner),
  `docs/writing-principles.md` (new), `AGENTS.md` (the policy pointer, the prefix table and the
  owner map), `eng/` (the checker and its manifest), `docs/decisions.md` (this entry, its index
  line, and D-029's title and body, which now name the canonical path), and every authored file that
  cites an engineering principle. Narrows D-073. Renames the owner D-029 established. Process and
  documentation only: no code, no diagnostics, no output bytes.

### D-130: Authored-text integrity: one mechanical command over raw decoded text, two fixed instruction-entry checks, an include/exclude manifest and a process-level test runner

- **Status:** accepted
- **Date:** 2026-09-23
- **Decision:** M8.2 works in nine numbered packets; this entry names four of them: Packet 2, the
  first to rewrite documentation; Packet 3, which edits the decision log, roadmap and benchmark
  record; Packet 8, which edits tests, benchmarks, engineering files and workflows; and Packet 9,
  the closing integration audit. `eng/check-authored-text.ps1`, its manifest
  `eng/authored-files.txt` and its test runner `eng/check-authored-text.tests.ps1` ship together
  before Packet 2, a deliberate move of this tooling ahead of Packet 8, which owes no checker design
  and no first tests. The command is one PowerShell 7 script with no module and no dependency. It is
  read-only and offline, takes `-RepoRoot` (default: the parent of `eng/`) and `-Path`, and checks a
  stable, trusted checkout: an edit during a run invalidates the result, and it is not a security
  boundary.
  **(1) Selection.** The root is normalized lexically and must be an existing ordinary directory;
  containment is by path segment. The command walks one directory at a time with its entries in
  ordinal order, skips `.git`, never enters a directory that an exclude of the form `<prefix>/**`
  matches, and refuses every other reparse point at its segment without resolving, following or
  naming its target. The manifest accepts only blank lines, `#` comments, exactly one
  `min-files <n>`, `+ <glob>` and `- <glob>`. Globs are repository-relative and case-sensitive, `*`
  stays within a segment and `**` spans segments; a leading slash, a drive letter, a backslash, a
  `.` or `..` segment and any other line, including the retired `!`, `~` and `@` forms, make the
  manifest malformed. Excludes win. Every include must match a walked file, the resolved set must
  reach `min-files` on every invocation, and it must keep `AGENTS.md`, `CLAUDE.md`,
  `docs/writing-principles.md` and the manifest; an exclude that removes one of them is malformed.
  `-Path` validates the whole manifest first. Each operand must be contained, spelled exactly as
  walked and select at least one file of the manifest set (a file itself, a directory its selected
  descendants, `.` all of it), or the run is refused naming that operand. Every comparison and every
  ordering is ordinal.
  **(2) Checks.** Every selected file is read and decoded as strict UTF-8, and a byte order mark is
  accepted. For `.cs` under `src/` and `tests/`, whose bytes
  `tests/FcaBedrock.Architecture.Tests/SourceHygieneTests.cs` owns, no control-character check runs
  and a decode failure is a file-level refusal. For any other file, a decode failure is
  `encoding-invalid-utf8` and ends that file's checks, and a C0 character other than TAB, LF and CR,
  or DEL, is `encoding-control-byte`. Over the whole decoded text of every file, with no syntax
  masked, three case-sensitive .NET regular expressions with Unicode `\b` and `\d` report one
  finding per match: `\bP-\d+\b` is `identifier-obsolete`, `(?<![\w-])principles\.md` is
  `path-obsolete` and also applies to each selected path, and `\bP[0-3]\b` is
  `stage-label-obsolete`. On every invocation, targeted or not: `AGENTS.md`, `CLAUDE.md` and
  `docs/writing-principles.md` must exist as regular files (`required-file-missing`); `AGENTS.md`,
  with each whitespace run collapsed to one space, must contain the sentence
  ``Read `docs/writing-principles.md` before authoring or editing any prose or comment in this repository.``
  (`policy-pointer-missing`), which fixes that sentence and not its paragraph; and the first
  nonblank line of `CLAUDE.md`, with trailing whitespace trimmed, must be exactly `@AGENTS.md`
  (`instruction-import-invalid`), while later lines stay free as D-048 allows.
  **(3) Refusals, output and exits.** A run-level refusal stops the assessment and keeps every
  finding already collected: a missing or malformed manifest, an invalid root, a reparse point, an
  unsafe or empty operand, a selected path that contains a tab, CR or LF, and an unexpected
  failure. A file-level refusal (an unreadable file, or an undecodable `.cs` under `src/` or
  `tests/`) is reported and the scan continues. Each output line has five tab-separated fields:
  rule; repository-relative path or `-`; line, counting LF and ignoring CR, or `0`; severity
  `fatal`, `error` or `info`; and message. Fatal lines come first, then errors, each ordered by path,
  line, rule and message. The last two lines are `checker-scope`, which states that local links,
  fragments and writing style are not assessed and that `SourceHygieneTests` owns the bytes of
  `.cs` under `src/` and `tests/`, and `checker-summary`, which counts the selected, decoded,
  owned-elsewhere and refused files and the errors. The complete rule set is the eight content
  errors named in (2); the refusals `checker-manifest-missing`, `checker-manifest-malformed`,
  `checker-scan-empty`, `checker-path-escape`, `checker-file-unreadable`,
  `checker-file-undecodable` and `checker-internal-failure`; and those two information lines. Exit 2
  means a refusal occurred and takes precedence over exit 1, which means a content error. Exit 0
  means only that the manifest and selection were valid, every selected file was decoded or
  accounted, the fixed checks passed and no owned violation was found; it is never a statement about
  links, style or the `SourceHygieneTests` result.
  **(4) Removed from automation.** Link, fragment, anchor and slug checks, because an open Markdown
  grammar cannot yield reliable errors. Style warnings and every comment or prose extractor, because
  the warnings were not a usable work list and a one-line search replaces the useful part.
  Suppressions and preservation or instruction routes, because none had a caller and an identifier
  suppression contradicts D-129 rule (2). Import graphs, because D-048 fixes exactly two entry
  points. Reparse following and physical-alias identity, because no caller needs them and they
  conflict with logical selection. The coverage channel, because it claimed a completeness it could
  not prove.
  **(5) Human link review.** A change that adds or edits a local link, renames or moves its target,
  changes a target heading or explicit anchor, or changes structure that could alter whether a link
  or heading renders requires review. The packet that makes such a change keeps a private
  navigation table with one row per affected occurrence (source path, line, destination, target
  heading before and after, and the reviewer's mark), and the reviewer checks every row against the
  candidate's actual headings. The baseline at this decision is 47 local links: 46 same-file
  fragment links to 17 distinct destinations, in `docs/benchmarks.md` (40),
  `src/FcaBedrock.Cli/README.md` (5) and `tests/FcaBedrock.Benchmarks/README.md` (1), and one
  cross-file link from `README.md`. Packet 3's table also carries the decision-identifier and
  contractual-heading inventory that D-129 rule (1) requires, whether or not any link points at
  them, and Packet 9 reconciles the union of all packet tables.
  **(6) A legitimate future spelling.** No suppression exists. Authored code and tests assemble a
  needed obsolete spelling from fragments. Required verbatim third-party text that contains one is
  excluded by a literal `- <path>` line whose comment names the reason and the authorizing decision,
  and review then preserves that text as it preserves `LICENSE`. `stage-label-obsolete` may be
  retired by a later decision if it collides with a legitimate identifier; the other two predicates
  stand while the `EP-` family and its owner exist.
  **(7) Tests.** `eng/check-authored-text.tests.ps1` builds each case from inline content in the
  session temporary directory, runs the command only as a child process, asserts the exit code, the
  complete standard output and an empty standard error, and hashes every case directory before and
  after the run. It reports a case the host cannot set up as skipped with its reason, fails when no
  case runs, and first proves that its comparator rejects wrong expectations. It is a named, narrow
  exception to the one-xUnit-project-per-package convention of D-039 and D-040, because it tests a
  standalone PowerShell command that must run without a restore or a build, and it establishes no
  general test framework.
  **(8) Supersession.** This supersedes the checker portion of D-129 rule (5): its link and
  explicit-anchor failures, its style warnings and its suppression rules. Rule (5) keeps one small
  local command, no new dependency, no rewriting of text and a single source-hygiene owner. D-129
  rules (1) to (4), the writing policy and D-048's allowance for later guidance are unchanged. No CI
  job, blocking or not, is licensed; that remains a separate decision.
- **Why:** the earlier draft decided its errors through Markdown and source-language recognizers
  that each review could defeat with new valid input, and its proof lived only in private scratch
  cases. Deciding every error on raw decoded text, a path or a fixed predicate removes the syntax
  dependency, so an exit status means the same on any input. D-129 rule (2) already makes any
  occurrence of an earlier spelling an error, including inside literals, so the raw predicates are
  exact rather than approximate. The permanent runner makes the behaviour's proof part of the
  repository. The rewrite packets are the command's first users, so it ships before them.
- **Rejected:** a bounded Markdown grammar with advisory style warnings and a 1,200-line ceiling,
  because its recognizer cannot prove it saw every relevant construct, the warnings were not a work
  list, and the ceiling was an unexplained number. A whole-file safe-text envelope for automatic link
  checks, which admitted none of the 16 Markdown files. Deferring the command and its tests to
  Packet 8, which inverts the value: the packets before it are the callers. Repairing the earlier
  draft one counterexample at a time, an open grammar with no completion criterion and no permanent
  tests. Keeping the earlier name `check-writing.ps1`, which claims a writing assessment the command
  no longer makes. Hosting the runner in an xUnit project, which needs a built test host for a check
  meant to run without one. An opt-in style mode, aggregate counters or a separate style command,
  which keep extraction machinery for output a one-line search gives. A suppression grammar kept for
  later, which has no caller, contradicts D-129 rule (2) and is covered by the two dispositions in
  (6). Hard-coded pruning of `bin`, `obj` and `artifacts`, a second owner for a fact the manifest
  states. Stopping the whole run at the first undecodable `.cs`, which hides other files' findings
  when a file-level refusal is as honest and clearer. A physical line cap as an acceptance measure;
  the structural limits above decide scope.
- **Affects:** `eng/check-authored-text.ps1` and `eng/check-authored-text.tests.ps1` (new),
  `eng/authored-files.txt`, `docs/decisions.md` (this entry, its index line, and D-129's status line
  and index annotation), one sentence each in `AGENTS.md` and `docs/writing-principles.md`, and
  `eng/README.md`; the uncommitted earlier draft `eng/check-writing.ps1` was removed. Supersedes the
  checker portion of D-129 rule (5). Process and tooling only: no code, diagnostics, spec, fixture,
  output bytes or roadmap change.

### D-131: One-time heading-separator migration in the decision log, roadmap and benchmark record: colons replace em dashes, changed anchors are accepted, and no alias is kept

- **Status:** accepted
- **Date:** 2026-09-24
- **Decision:** the em-dash separators in the headings of `docs/decisions.md`, `docs/roadmap.md` and
  `docs/benchmarks.md` become colons. Each heading changes once, in the change that edits its
  section, never in a separate heading-only sweep. A decision heading keeps its words apart from the
  separator, except that it may drop an internal process label that means nothing outside the
  project's working process; the words around a dropped label may be repunctuated, or joined by a
  conjunction, so that the heading still reads as one title. A roadmap or benchmark heading may
  also be retitled, merged or removed when that change restructures its section on its own merits.
  The index lines and the entry-format template, which mirror the decision headings, take the same
  form.
  The anchors those headings generate change with them:
  - **External fragments may break.** A link from outside the repository to a changed anchor stops
    resolving, and that is accepted. No compatibility alias, explicit anchor or old-path stub keeps
    an earlier anchor.
  - **Every in-repository local link is repaired and recorded.** The D-130 human link review applies:
    the change that converts a heading repairs every local link in the repository that targets it,
    and its private navigation table records each old and new anchor.
  - **The rest of D-129 rule (1) still holds.** A compacted entry keeps its ID, actual rule, decisive
    rationale and tradeoff, consequences and limits, status, meaningful dates, supersession links and
    position in historical order. Decision identifiers and file paths do not change, so every
    citation by `D-NNN` or by path still resolves.
  This supersedes the heading-anchor clause of D-129 rule (1) for this migration only.
- **Why:** WP-6 forbids the em dash as punctuation, and a heading separator is punctuation. Keeping
  every heading byte-identical to preserve its anchor would have kept the separator in every heading
  of the three documents that used one, plus the entry-format template, permanently. The maintainer
  explicitly accepted that external links to the changed headings may break. No local link in the
  repository targets a decision or roadmap heading, and every local link that targets a changed
  heading is a same-file link in `docs/benchmarks.md` that the D-130 review already covers, so the
  cost falls only on external fragment links. An internal process label, such as a policy letter,
  tells a reader outside the project nothing, so a decision heading may lose one; D-126's heading
  lost one.
- **Rejected:** keeping every heading byte-identical, the D-129 default, which preserves each anchor by
  keeping its dash; alias anchors or stub headings for the old anchors, which D-129 already rejects as
  a migration diary that Git holds; and one repository-wide heading sweep apart from the content
  changes, which would review each heading away from its section and twice.
- **Affects:** `docs/decisions.md` (this entry, its index line, D-129's status line and index
  annotation, and each decision heading, index line and the entry-format template as the change that
  edits each section converts it), `docs/roadmap.md` and `docs/benchmarks.md` (their headings and the
  in-file fragment links that target them). Public documentation anchors change. No product code,
  public API, CLI contract, diagnostic, spec text, fingerprint or output byte changes. Supersedes the
  heading-anchor clause of D-129 rule (1) for this one-time migration; the rest of D-129 is unchanged.

### D-132: No separate public evidence file; the benchmark guide carries only the identities that qualify its current claims

- **Status:** accepted
- **Date:** 2026-09-24
- **Decision:** `docs/benchmarks.md` is the only public home for benchmark evidence identity, and it
  carries only the revisions and digests that qualify its current claims: the corpus identities,
  the revision each result set describes, and the source identity of the M8.1 measurement. No
  separate evidence file, directory, stub or digest ledger exists. Seals, archive and trace
  digests, pre-registered criteria and deviation records are not repository content: the
  maintainer's private evidence holds them, and Git history holds the earlier benchmark narrative.
  The owner map in `AGENTS.md` has no evidence row.
- **Why:** a public ledger of seals and run identities restates the chronology the benchmark guide
  drops, and a digest of unpublished evidence identifies bytes without making them available. Each
  claim stays qualified where it is made. The maintainer chose removal over a smaller receipt.
- **Rejected:** a public receipt of up to 10 KiB listing revisions, archive and trace digests and
  evidence seals; a stub, alias or renamed file; moving the seals into the benchmark guide, a
  decision entry or the roadmap.
- **Affects:** `AGENTS.md` (the owner map), `docs/benchmarks.md`, `docs/decisions.md` (this entry
  and its index line). No product code, public API, CLI contract, diagnostic, spec text,
  fingerprint or output byte changes.

### D-134: Spec passages that contradict their rule's owner are corrected to it, and enforced defaults, bounds and allowed values shown only in examples, and enforced rules stated nowhere, are stated in prose

- **Status:** accepted; amended in place on 2026-09-27 to add the third kind
- **Date:** 2026-09-26
- **Decision:** M8.2's review of the Bedrock spec makes three kinds of normative text change
  without a decision of its own for each. (1) A passage that contradicts the section or decision
  owning its rule, where the implementation already follows that owner, is corrected to the owner.
  (2) A default, bound or allowed value that the spec showed only in an example value or comment,
  which §1 makes informative, and that the reader or resolver already enforces, is stated in prose
  at the section that owns the field. (3) A rule that the reader or resolver already enforces and
  tests pin, but that the spec stated nowhere, is stated in prose at the section that owns the
  field. None of the three kinds changes accepted input, a diagnostic's code, severity or
  phase, a fingerprint or an output byte. A correction that would change behavior, or that must
  choose between two owners that disagree, still needs its own decision. Each correction is still
  reviewed before it lands, and this entry accepts no later change by itself. First applications:
  §7's parse/validate list no longer names formal-attribute identity collisions, which §10.2, §16.4
  and D-067 assign to plan, and its Plan phase names that check, including a collision that only
  calibration reveals (§11.6, D-090, D-104); §7's fully-declared conditions list `ordered_cuts`,
  whose order and cuts are authored (§11.8, D-046) and whose kind alone does not trigger
  §14's data-calibration exclusion, and limit the `include` condition to `identity` and
  `free_per_value` attributes, as
  D-104 (for `value_groups`) and the implementation already do; §5.4 states the allowed values of
  the deferred composite key's `columns` and `aggregate`, which the reader enforces.
  Kind (3)'s first applications are in §11.4 and §11.5: `vmin` and `vmax` are rejected under a
  data-derived `equal_width` range, `round_to` must be finite and greater than zero, and `bins` is
  at most 2,147,483,647. This entry also covers §8's exact `.cxt` size-advisory default,
  1,073,741,824 bytes: its example showed the value and its prose said only 1 GB, and the CLI, not
  the reader or resolver, applies it.
- **Why:** EP-8 and the spec's status line require a reviewed, recorded change whenever spec text is
  corrected to match the implementation. These corrections change no behavior, so one rule records
  them instead of one entry each. Both §7 passages date from the spec's first version: the
  fully-declared list was written before `ordered_cuts` existed, and the phase list has disagreed
  with the §16.4 registry since both were first written.
- **Rejected:** one entry per correction (the same rationale repeated); deferring the corrections
  past M8.2 (the closing audit could not certify a spec known to contradict itself); changing the
  implementation to match the contradicting passages (it would move `FormalAttributeCollision` to a
  phase that cannot see a data-dependent collision, and force a calibration pass on specs every
  build has treated as fully declared).
- **Affects:** spec §3, §5.4, §7, §8, §9.2, §10.4, §10.5, §11.2, §11.4, §11.5, §11.6, §12.3,
  §14, §16.2 and §16.4; `docs/roadmap.md` (the M8.2 paragraph). No code, test, public API,
  diagnostic, fingerprint or output byte changes.

---

## M1 (mini-mushroom walking skeleton)

### D-041: Sep as the DSV tokenizer for the wide-CSV source

- **Status:** accepted
- **Date:** 2026-06-24
- **Decision:** `FcaBedrock.Sources.WideCsvSource` delegates raw DSV tokenization
  (field splitting, RFC 4180 quoting, custom separators) to **Sep**
  (`nietras/Sep`, MIT). `WideCsvSource` wraps it and owns the FCA semantics the
  tokenizer knows nothing about: the declared `binding.delimiter`, `has_header`,
  missing detection (`missing_token`/empty → `null`), row-index object naming, and
  re-readability (a `Func<Stream>` so the `.cxt` two-pass can replay). Sep is the
  only new runtime dependency; it is one `PackageVersion` in
  `Directory.Packages.props` referenced only by `Sources`.
- **Why:** hand-rolling correct RFC 4180 quoting (escaped quotes, embedded
  delimiters/newlines) is a classic bug farm, and the v1 scale target (D-007, up
  to ~73M records) wants a parser already hardened and benchmarked for span-based,
  zero-allocation streaming. Sep was then the fastest .NET CSV parser and its
  span-first row/col API suits the emit hot path (EP-18). Wrapping rather than
  exposing it keeps Sep out of the public surface, so it can be swapped without a
  contract change (EP-4).
- **Rejected:** (a) hand-rolling a span DSV parser: reinvents the wheel we
  explicitly chose not to, and shifts the hardening/benchmark burden onto us; (b)
  `Sylvan.Data.Csv`: also fast and mature, but exposes an ADO.NET
  `DbDataReader` shape with slightly more per-field overhead and a less span-native
  API than Sep.
- **Affects:** Sources, `Directory.Packages.props`; spec §5.1/§5.2.

### D-042: Drop the `BedrockResult<T>` alias; use `Result<T, BedrockDiagnostic>`

- **Status:** accepted (refines D-006)
- **Date:** 2026-06-24
- **Decision:** `Result<T, TError>` is a `readonly struct` whose `Ok`/`Err`
  factories allocate nothing, so the `BedrockResult<T>` "alias" D-006 named would
  be pure typing-sugar with no runtime benefit, and C# cannot alias a
  partly-closed generic anyway. It is **not** introduced; call sites use
  `Result<T, BedrockDiagnostic>` directly. `Diagnosed<T>` (the aggregating carrier)
  is unaffected and remains the standard for validate/plan.
- **Why:** one result type, no indirection, no second way to spell the same thing
  (EP-5). Naming the alias only to never realize it would have been a phantom in the
  surface.
- **Rejected:** a `BedrockResult` static factory class over the closed generic
  (still indirection for zero benefit); a distinct wrapper type (a second result
  type to learn, against EP-5).
- **Affects:** Diagnostics; refines D-006 (alias struck from the type list).

### D-043: Two test axes: golden = v2-compat evidence, conformance = native spec

- **Status:** accepted
- **Date:** 2026-06-24
- **Decision:** output behavior is proven along two distinct axes. (1) **Golden
  tests** (`FcaBedrock.Golden.Tests`) run the pipeline under
  `WriterOptions.V2Compat` and assert byte-identity against the `fixtures/v2/`
  goldens: *compatibility evidence* (EP-9). (2) **Spec-conformance tests** run the
  **native** (non-v2) path and assert documented behavior with spec-section
  citations (e.g. native `.cxt` is LF + trailing newline §18.1; native `.dat` has
  no trailing space §18.2/§21.4; dichotomic name is `{column}` alone §10.7/§12.2;
  `value_labels` change names not order/count §10.8). Per-fixture binding (which
  the v2 `.bed` never recorded: delimiter/header/shape) lives in a typed
  `FixtureCase` table, as the fixtures README sanctions.
- **Why:** the v2 goldens are deliberately *not* the spec's native output (v2-isms
  are quarantined behind `--v2-compat`, D-011), so byte-equality to v2 alone would
  leave the native contract unproven. The conformance axis is how code is held to
  the spec, not merely to v2 (EP-8); the golden axis is how v2 compatibility stays
  evidenced (EP-9).
- **Rejected:** a single golden axis (would silently let the native default drift
  from the spec); asserting native output against checked-in native golden files
  (premature before the native format stabilizes, and the spec text is the
  authority at this stage).
- **Affects:** tests (`FcaBedrock.Golden.Tests`: `GoldenFixtureTests`,
  `SpecConformanceTests`, `FixtureCase`); spec §18.

---

### D-044: Bin-label style is a plan-time render hook, not a writer flag

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** the v2 `30to<40` vs native `[30, 40)` cut-bin label difference is
  applied once, at name render, via a `LabelStyle { Native, V2Compat }` input to
  `ConversionPlanner.Plan` and an `internal virtual Discretizer.RenderBinLabel`
  hook (default identity; cut discretizers transform only the interior `[a, b)`
  form). Discretizers emit **one canonical label** (`<c0`, `[a, b)`, `>=cn`) used
  as the bin key / `BinKey` / `CrossesByBin` key; the style touches only the
  rendered name. `WriterOptions` gains no label flag.
- **Why:** label style affects `output_fingerprint` (now split per-format, D-051)
  only, never the schema (§8/§14, D-011/D-035). Canonical identity + late render keeps writers dumb (EP-15) and lets
  one planner path serve both styles.
- **Affects:** `FcaBedrock.Core` (`Discretizer.RenderBinLabel`, `LabelStyle`,
  `ConversionPlanner`); the golden harness derives the style from the v2-compat
  line ending.

---

### D-045: v2 `o` and `n` are both cut-based; discrete→nominal, progressive→ordinal

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** v2's continuous (`o`) and ordinal (`n`) types are both cut
  discretizers; the discrete/progressive toggle is the **scale** choice, not a
  discretizer mode. `o` → `manual_cuts`, `n` → `ordered_cuts` (D-046); discrete →
  `nominal`, progressive → `OrdinalScale(direction = le)`. The mode is **not in the
  `.bed`** (the two ordinal `.bed`s are byte-identical); it is supplied
  **out-of-band** (a `ScalingMode` argument to `BedToSpec.ToSpec`, like `Binding`),
  defaulting to discrete. **No TOML reader is needed** to reproduce the progressive
  golden. `d` (date) stays rejected (D-038).
- **Why:** matches the orthogonal model (D-002) and the verified v2 output (`n`
  produces mutually-exclusive `<Managerial`/`>=Managerial`). Treating the mode as
  caller metadata mirrors how delimiter/header already are.
- **Rejected:** mapping `n` to a cumulative ordinal by default (refuted by the
  discrete `n` golden); deferring the progressive golden to the M2 TOML reader.
- **Affects:** `FcaBedrock.Spec` (`BedToSpec`, `ScalingMode`); the golden harness
  (`FixtureCase.ScalingMode`).

---

### D-046: `ordered_cuts` discretizer (spec §11.8); v2 `.bed` section-role asymmetry

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** add an `ordered_cuts` discretizer (cuts over a declared category
  order) as the categorical sibling of `manual_cuts`, sharing one `CutBinLabels`
  label/identity helper. Documented in spec §11.8 **before/with** the code (EP-8).
  For v2 `n`, `[Attribute Categories]` carries the ordered domain and
  `[Category Values]` carries the cut (`<,Managerial,>`); for `o`, both carry the
  numeric cut spec.
- **Why:** keeps numeric and ordered cuts symmetric and DRY (EP-17); the shared
  label helper guarantees they never drift on bin labels or `--v2-compat` rendering.
- **Rejected:** modelling `n` with `value_groups` (loses order and threshold
  semantics); a mode-switched single cut discretizer (god type, EP-17).
- **Affects:** `FcaBedrock.Core` (`OrderedCutsDiscretizer`, `ManualCutsDiscretizer`,
  `CutBinLabels`, `BinEnds`); spec §11.8, §19.2.

---

### D-047: Open-end ordinal threshold renders `all`, canonical in both paths

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** for an `ordinal` scale over an **open-ended** cut discretizer, the
  tautological threshold at the open end has no finite edge and renders **`all`**
  (v2's `age-all`). It is **canonical** (emitted natively too), so `--v2-compat`
  never changes the schema (D-035); `drop_top` suppresses it. Over half-open
  `[lo, hi)` cut bins only the geometry-aligned boundary is well-defined (`le`+`<`,
  `ge`+`>=`); the straddling combinations and the independent `boundary` knob await
  the M2 value-bin path (no M1 producer, EP-3). Spec §12.3 amended.
- **Why:** makes "N bins → N formal attributes" exact and reproduces v2's
  progressive column count; an `all`-only-under-`--v2-compat` rule would add a
  column under compat, violating D-035.
- **Rejected:** a math-y native `<∞` with `all` only under `--v2-compat`.
- **Affects:** `FcaBedrock.Core` (`OrdinalScale`, `BinScheme`, `OrdinalDirection`);
  spec §12.3.

---

### D-049: `include = false` is an authoring toggle; dormant config never blocks

- **Status:** accepted
- **Date:** 2026-06-27
- **Decision:** `include = false` is a pure on/off **authoring toggle**. An
  excluded attribute MAY retain any emitted-shaping config (`discretizer`,
  `scale`, `value_labels`, `declared_domain`, `formal_attribute_format`,
  `display_name`, `missing_policy`, `unknown_value_policy`); the planner ignores
  it while excluded, and it is **never** an error. This reverses D-037(b)'s
  `EmittedFieldOnExcludedAttribute`, which is **removed** (diagnostic + enum
  member). `restrict_to` still applies (D-021/D-032). Consequences: (a) the v2
  `.bed` migrator now **preserves** an excluded attribute's derived
  discretizer/scale/domain, best-effort: an unsupported type or config that fails
  to parse degrades to a bare excluded attribute, never failing the migration; (b)
  `value_labels` under a discretizer that does not
  consult it is **dormant**: ignored by both validation *and* name rendering, so
  it cannot change output even when a key matches a cut-bin label. `value_labels`
  applies only when the discretizer consults it (`identity`/`free_per_value`); the
  single authority is `Discretizer.ConsultsValueLabels`. So `ValueLabelsNotApplicable`
  (spec-text only) and the enforced `ValueLabelKeyNotInDomain` fire only for live
  labels; `ValueLabelKeyNotInDomain` stays the typo-catcher there.
- **Why:** the D-037(b) rule made toggling an attribute off destructive (strip all
  config) and a TOML round-trip of a parked attribute lossy, which was
  authoring-hostile ahead of the M2 reader/writer. The principle: inactive/dormant
  config must not block authoring; active attributes are still validated normally.
  Aligns excluded attributes and dormant `value_labels` with how `declared_domain`
  is already ignored for cut-based discretizers (§10.3).
- **Deferred at the time:** round-trip presence tracking for
  `missing_policy`/`unknown_value_policy`/`display_name`/`formal_attribute_format`
  (settled by D-066 and D-075), the default-`boundary` round-trip trap over cut
  bins (§12.3, settled by D-060) and the `value_type`-vs-discretizer rule (§10.2,
  settled by D-061) went to the M2 writer. Replacing the migrator's broad recovery
  `catch (Exception)` with diagnostics (EP-14) landed with D-079.
- **Affects:** Core (planner `ValidateValueLabels` + `RenderName`; new
  `Discretizer.ConsultsValueLabels`; `AttributeSpec` doc), Diagnostics (enum:
  `EmittedFieldOnExcludedAttribute` removed), Spec (`BedToSpec` migrator), spec
  §7 / §10.8 / §10.9 / §16.4 / §17. Supersedes D-037(b); refines D-021 / D-032.

---

## M2 (TOML spec format + fingerprinting)

These finalize the M2 contract before the TOML reader/writer, `.bed` migrator,
fingerprints, manifests, and `extends` are implemented.

### D-050: Malformed numeric values are present-but-invalid, not missing

- **Status:** accepted (supersedes the §11.5 "parse failure → missing" wording and
  the "NaN/∞ → missing" clause of D-036)
- **Date:** 2026-06-28
- **Decision:** for a numeric attribute, a value that is present but not a usable
  finite number (fails to parse under `binding.locale`, or parses to NaN/±∞) is
  **invalid**, not missing. The object is kept, no cross is emitted, the value is
  excluded from calibration, and `SourceValueUnparseable` is reported at the
  severity `unknown_value_policy` selects (`skip` → silent; `warn` → Warning;
  `fail` → Error/abort; `include` → Warning, since an unparseable token cannot be
  added to a numeric domain). Only empty cells and explicit `missing_token`
  matches are *missing* and follow `missing_policy`.
- **Why:** "missing" and "malformed" diverge under `missing_policy = "as_attribute"`
  (missing crosses the `-missing` column; malformed must not) and for diagnostics
  (malformed data deserves a signal). Reusing `unknown_value_policy` for severity
  avoids a second strictness knob (EP-5); for numeric attributes that policy was
  otherwise inert (cut discretizers ignore `declared_domain`, §10.3). NaN/±∞ are
  folded in with parse-failure rather than split into a third behavior.
- **Rejected:** keeping parse-failure as missing (conflates two conditions, hides
  bad data); a fixed Warning severity (a `fail` pipeline expects malformed data to
  abort); a dedicated malformed-value policy knob (EP-6, redundant with
  `unknown_value_policy`).
- **Affects:** Core (planner/emit), Conversion; spec §10.6 / §11.5 / §16.4;
  diagnostic `SourceValueUnparseable`. Byte-neutral on M1 (still "keep object, no
  cross"; only adds a diagnostic).

### D-051: Per-format output fingerprints (cxt + dat) replace the single output_fingerprint

- **Status:** accepted (supersedes the single-`output_fingerprint` model of D-035;
  `schema_fingerprint` unchanged)
- **Date:** 2026-06-28
- **Decision:** the `[spec]` block stores `schema_fingerprint`,
  `cxt_output_fingerprint`, and `dat_output_fingerprint`. Both output fingerprints
  build on `schema_fingerprint` and add the **shared** byte-affecting inputs that
  schema omits: `duplicate_object_policy`, object-ordering policy, `restrict_to`
  (once executable), and conversion-affecting binding/source settings. `.cxt` then
  adds rendered names + bin-label style + `.cxt` writer settings; `.dat` adds only
  `.dat` writer settings (`base_index`, line endings, trailing space). Rendered
  names never affect `.dat`.
- **Why:** a single output fingerprint conflated `.cxt`-only and `.dat`-only
  settings, so a `.cxt`-only change perturbed the `.dat` hash. The split is exact
  per format. The row-shaping policies (`duplicate_object_policy`, ordering) sit in
  *both* output fingerprints, not `schema_fingerprint`, because they change rows,
  not columns (D-035).
- **Rejected:** one `output_fingerprint` (imprecise); folding output identity into
  `schema_fingerprint` (would make `.dat` column identity depend on formatting).
- **Affects:** Spec, Core; spec §3 / §14 / §15 / §21-item-16; diagnostics
  `CxtOutputFingerprintStale`, `DatOutputFingerprintStale`.

### D-052: extends overrides attributes position-preservingly

- **Status:** accepted (supersedes §13 rule 5 "concatenate; current wins"; refines
  D-027)
- **Date:** 2026-06-28
- **Decision:** under `extends`, base attributes keep their original positions; a
  derived attribute with the same `name` replaces the base attribute **in place**
  (whole-attribute replacement: inherited fields, including `restrict_to`, are
  dropped unless repeated); a derived attribute with a new `name` is appended after
  all inherited attributes; the merge is applied at each step of a multi-level
  chain, base-most first. Suppress an inherited attribute by overriding it with
  `include = false`. `[output]` / `[output.cxt]` / `[output.dat]` merge per leaf
  field. Base-stored fingerprints are ignored and recomputed for the resolved spec.
- **Why:** attribute order is column order (§17 rule 1) and feeds
  `schema_fingerprint`, so plain concatenation would reorder columns whenever a
  derived spec re-tuned an inherited attribute: surprising and fingerprint-
  changing. Position-preserving override keeps column order stable across re-tunes.
  `[output]` merge was previously unspecified.
- **Rejected:** concatenation with append-on-override (reorders columns);
  field-level merge of same-name attributes (error-prone, already rejected by D-027).
- **Affects:** Spec; spec §13.

### D-053: Fingerprints hash a plan-derived canonical JSON structure, not TOML text

- **Status:** accepted (supersedes the §2/§3 "canonical TOML projection" framing;
  refines D-035's plan-based hashing)
- **Date:** 2026-06-28
- **Decision:** all three fingerprints hash a fixed UTF-8 canonical JSON structure
  generated from the resolved/calibrated **plan**, never the spec's TOML text. The
  structure carries a format-version tag; arrays stay in planned order; map keys
  are sorted; strings use one documented JSON escaping rule; numbers are the parsed
  numeric value reformatted with invariant, shortest round-trippable .NET
  formatting (so `30`, `30.0`, `3e1` collapse and no machine-dependent float drift);
  cut-bin open ends are structural flags, not `∞` strings.
- **Why:** §2/§3 still described a "canonical TOML projection," which D-035 had
  already obsoleted by defining `schema_fingerprint` over the planned list. Hashing
  the plan is the single source of truth. Pinning the numeric format and a version
  tag makes stored fingerprints portable and the encoding evolvable, both durable
  contracts (EP-11), so they must be fixed before any spec ships with a stored hash.
- **Rejected:** hashing TOML text (formatting-sensitive, and `30` vs `30.0` would
  differ); `"R"`/`"G17"` float formatting (17 digits defeats the `30.0`/`30`
  collapse).
- **Affects:** Spec, Core (fingerprint encoder); spec §2 / §3 / §14.

### D-054: v1 supports only the standard double quote_char

- **Status:** accepted (refines D-041 / §5.1)
- **Date:** 2026-06-28
- **Decision:** v1 accepts only `quote_char = "\""`; a custom `quote_char` parses
  but is rejected with `QuoteCharNotSupportedV1`. `delimiter` is a single
  non-newline character and MUST differ from `quote_char`
  (`BindingDelimiterQuoteConflict`). The `quote_char` field is retained so a later
  version can lift the restriction without a format change (the D-010 pattern).
- **Why:** the Sep tokenizer (D-041) is exercised and golden-tested only with the
  standard quote; promising arbitrary quote chars in v1 would be an unverified
  contract. Narrowing now, with a reserved field, keeps the door open.
- **Rejected:** silently honoring a custom `quote_char` (unverified); removing the
  field (would force a format change to re-add).
- **Affects:** Sources, Spec; spec §5.1 / §16.4; diagnostics
  `QuoteCharNotSupportedV1`, `BindingDelimiterQuoteConflict`.

### D-055: value_groups does not use declared_domain

- **Status:** accepted (refines D-022)
- **Date:** 2026-06-28
- **Decision:** `declared_domain` is not meaningful for `value_groups` and is
  removed from §10.3's applicability list (now `identity` / `free_per_value` only).
  For `value_groups`, the `groups` plus the `unmatched` policy define recognition:
  a value matches a group → its label; otherwise `unmatched` decides (`skip` defers
  to `unknown_value_policy`, `other` → synthetic `Other` after declared groups,
  `passthrough` → its own raw label). `passthrough` discovers columns from data, so
  it triggers Calibrate, emits `ValueGroupsPassthroughDataDependent` (Warning), and
  omits stored fingerprints unless frozen.
- **Why:** `declared_domain` + `value_groups` created two overlapping
  "recognized-value" checks (EP-5), and since unmatched-`skip` already defers to
  `unknown_value_policy`, the domain added nothing but precedence ambiguity (it also
  made regex groups, the high-cardinality case D-022 targets, useless). Dropping it
  dissolves the ambiguity.
- **Rejected:** a domain-vs-group precedence rule (made regex groups pointless);
  rejecting `passthrough` + `declared_domain` only (still leaves the overlap).
- **Affects:** Core, Spec; spec §7 / §10.3 / §11.6 / §17;
  diagnostic `ValueGroupsPassthroughDataDependent`.

### D-056: Cut validation in M2

- **Status:** accepted (refines D-046)
- **Date:** 2026-06-28
- **Decision:** M2 validates hand-authored TOML cuts, since hand-written specs first
  become possible at M2. For `manual_cuts`: strictly ascending
  (`DiscretizerCutsNotAscending`), length ≥ 1 (`DiscretizerCutsTooFew`), and
  `ends = "closed"` requires ≥ 2 cuts (`DiscretizerEndsClosedTooFewCuts`). For
  `ordered_cuts`: `order` entries distinct and non-empty (`OrderDomainInvalid`),
  cuts ∈ `order` (`OrderedCutsCutNotInDomain`), cuts strictly ascending by order
  position (`OrderedCutsNotAscending`), plus the closed-ends rule.
- **Why:** v2 `.bed` migration produced cuts mechanically, but a hand-authored TOML
  spec can easily express invalid cuts; these need clear diagnostics at the
  validate phase rather than surfacing as confusing downstream behavior.
- **Affects:** Core (cut validation), Spec; spec §11.2 / §11.8 / §16.4; diagnostics
  `DiscretizerEndsClosedTooFewCuts`, `OrderDomainInvalid`, `OrderedCutsCutNotInDomain`,
  `OrderedCutsNotAscending` (and existing `DiscretizerCutsNotAscending` / `DiscretizerCutsTooFew`).

### D-057: restrict_to round-trips in M2; execution deferred to M4

- **Status:** accepted (refines D-021 / D-032; sequences §10.4; migrator carriage realized by D-079;
  execution realized by D-105)
- **Date:** 2026-06-28
- **Decision:** M2 parses, preserves, and round-trips every `restrict_to` form
  (string list, open- and closed-range, mixed). Until execution landed at M4,
  planning rejected any `restrict_to` with the transitional
  `RestrictToNotImplementedV1` rather than silently ignoring it; `restrict_to`
  stayed out of the output fingerprints, and a restricting spec was not fully
  frozen. D-105 made `restrict_to` execute and retired all three limits.
- **Why:** round-trip and migration need the carrier in M2. Silently ignoring an
  authored filter would produce unfiltered output mismatching the spec's intent,
  and a stored output fingerprint without `restrict_to` would have gone stale
  when execution landed. The parse-but-reject pattern closed both holes.
- **Rejected:** silently ignoring `restrict_to` in M2 (latent correctness bug);
  pulling wide-CSV `restrict_to` execution forward into M2 (expands M2 scope; M4 is
  the restriction milestone).
- **Affects:** Core (planner guard), Spec; spec §10.4 / §14 / §16.4; diagnostic
  `RestrictToNotImplementedV1` (transitional, retired by D-105).

### D-058: Empty-output diagnostics: mechanical names replace EmptyExtent/EmptyIntent

- **Status:** accepted (supersedes the §16.4 `EmptyExtent` / `EmptyIntent` rows)
- **Date:** 2026-06-28
- **Decision:** the overloaded FCA-concept names `EmptyExtent` / `EmptyIntent` are
  removed in favor of four mechanical, correctly-phased diagnostics:
  `NoFormalAttributes` (zero columns, **plan**), `NoObjectsEmitted` (zero rows after
  filtering, **emit**), `AttributeHasNoCrosses` (an empty column, **emit**,
  aggregated), `ObjectHasNoCrosses` (an empty row, **emit**, aggregated). All four
  warn and still write a structurally-valid (if degenerate) output rather than
  failing; zero-column output is allowed because §10.1 already blesses inert
  attributes during staged editing.
- **Why:** `EmptyExtent` was listed at the plan phase, which is impossible for a
  per-attribute "no crosses" meaning (it needs emit-time data); and "extent/intent"
  are concept-level FCA terms, confusing when applied per-attribute/per-object. The
  per-element emit diagnostics must aggregate or they flood at 73M rows (D-007).
- **Rejected:** keeping `EmptyExtent` / `EmptyIntent` (wrong phase, overloaded
  names); failing on zero columns (blocks the staged-editing workflow §10.1 allows).
- **Affects:** Core (planner/emit), Diagnostics, Spec; spec §16.2 / §16.4.

---

## M1-adjacent conformance pass

Landed when shipped M1 code was reconciled with the merged M2 wording, before M2
proper. Mostly realizes earlier decisions (D-050 malformed-numeric, D-056 cut
validation); the one new architectural decision is below.

### D-059: Discretization outcomes and data-diagnostic aggregation

- **Status:** accepted
- **Date:** 2026-06-28
- **Decision:** discretizers return a structured `BinResult` distinguishing a recognized
  **bin**, **no-bin** (out-of-range), an **unknown** value, and an **unparseable** numeric,
  replacing the old `string?` that collapsed all four into "label or null". Emit-phase data
  diagnostics that can occur per row (`UnknownValueObserved`, `SourceValueUnparseable`) are
  **aggregated per attribute** with a count and a bounded sample, flushed in plan order after
  the stream (deterministic, EP-7), never one diagnostic per row.
- **Why:** `string?` conflated the silent no-cross cases (out-of-range, §11.2) with the
  diagnosable ones (unparseable §11.5/D-050; unknown §10.6/§11.8), so malformed/unknown data
  could not be surfaced without re-deriving it in the emitter (EP-17); and per-row diagnostics
  do not scale to the v1 data target (~73M records, D-007). One result type plus one
  aggregation pattern keeps future discretizers (`equal_width`/`equal_frequency` at M4,
  `value_groups`) and emit diagnostics consistent (EP-5). Also closes the §11.8 gap where an
  `ordered_cuts` value-not-in-order was silently dropped instead of treated as unknown.
- **Rejected:** keeping `string?` and re-deriving unparseable-vs-out-of-range in the emitter
  (duplicates the parse/culture logic out of the discretizer, double-parses the hot path); a
  second `TryDiscretize` out-param method (two ways to spell one decision, EP-5); per-row data
  diagnostics (flood at scale, D-007).
- **Affects:** Core (discretizers, `BinResult`), Conversion (emit aggregation), Diagnostics
  (`SourceValueUnparseable`); spec §10.6 / §11.5 / §11.8 / §16.4.

---

## Tier 1 spec audit (pre-M2)

A consolidated internal-consistency audit of `bedrock-spec-v1.md` before M2
implementation. Most findings were doc fixes needing no decision; the six below change a
validation/output contract or sequence a feature, so they are recorded here. They refine,
not reverse, earlier decisions.

### D-060: Ordinal-over-cuts validation contract

- **Status:** accepted; (c)'s authored-vs-defaulted boundary provenance is
  extended by D-114: a `boundary` arriving as a **winning field from an applied
  template or matcher** counts as explicitly authored, exactly as if it had been
  written on the attribute. A `[defaults]`-inherited boundary remains defaulted,
  as recorded here.
- **Date:** 2026-06-30
- **Decision:** three linked rules for ordinal scales over cut discretizers.
  (a) `scale.order` is a **value-bin** field only (`identity` / `free_per_value`):
  required for non-numeric value bins, optional for numeric. It MUST NOT appear with
  a **cut** discretizer (`manual_cuts`, `ordered_cuts`, `equal_width`,
  `equal_frequency`); presence is `OrdinalOrderNotAllowedWithCuts` (Error). The cut
  discretizer is the single source of order, and `ordered_cuts` bin order is added to
  §17 rule 3 (ascending by cut position). (b) Both ordinal-over-cuts compatibility
  checks run at **spec validate**: `OrdinalOrderNotAllowedWithCuts` and (re-phased
  from plan) `OrdinalBoundaryIncompatibleWithCuts`; both depend only on authored
  spec shape (discretizer kind, presence of `scale.order`, authored-vs-default
  `boundary`), never on data or calibrated cut values. (c) `[defaults]
  .ordinal_direction` / `ordinal_boundary` fill a missing `scale.direction` /
  `scale.boundary`; a per-attribute field wins. A boundary arriving via the default
  is **defaulted, not authored**: over cut bins it never overrides the geometry
  operator and never trips `OrdinalBoundaryIncompatibleWithCuts`. The reader/writer
  preserves authored-vs-default provenance.
- **Why:** the spec required `order` for any non-numeric bin labels, contradicting the
  §19.2 progressive golden (ordinal `le` over `ordered_cuts` with no `scale.order`)
  and the cut-geometry rule; §17 also omitted `ordered_cuts` entirely. Hard-rejecting
  `scale.order` over cuts (not silently ignoring it) keeps one visible source of order
  on an active attribute and matches the sibling `OrdinalBoundaryIncompatibleWithCuts`
  treatment. Validate phase is the earliest point these static errors can be caught
  and matches the other cut validations (D-056) and the M2 validation freeze.
- **Rejected:** ignoring `scale.order` over cuts as dormant (the
  `declared_domain`-over-cuts / D-049 pattern): on an *active* attribute it leaves two
  visible order declarations and can silently void the authored one; the local
  ordinal-over-cuts contract (boundary already rejects) is the tighter consistency
  axis. Placing the checks at plan: they need no data, so validate is earlier.
- **Affects:** Core (validate, `OrdinalScale`), Spec, Diagnostics
  (`OrdinalOrderNotAllowedWithCuts` new; `OrdinalBoundaryIncompatibleWithCuts`
  re-phased plan→validate); spec §6 / §12.3 / §16.4 / §17. Refines D-044 / D-046 /
  D-047 / D-049.

### D-061: value_type matrix: free_per_value flexible, identity string-only

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** each discretizer either **fixes** the value type or is **flexible**.
  String-fixing: `identity`, `value_groups`, `ordered_cuts` (only `"string"`).
  Number-fixing: `manual_cuts`, `equal_width`, `equal_frequency` (only `"number"`).
  Flexible: `free_per_value` (either: `"number"` → parsed-numeric bin identity, so
  `90` / `90.0` / `9e1` collapse to one bin; `"string"` → verbatim spelling).
  `identity` + `"number"` is `SourceValueTypeInvalid`; numeric distinct-value binning
  uses `free_per_value`.
- **Why:** D-049 flagged the §10.2 `value_type`-vs-discretizer rule as a *live
  conflict*: it spoke of one "discretizer-implied type," which mis-described
  `free_per_value` (legitimately both) and could reject its headline numeric use
  (D-022). Naming type-fixing vs flexible makes the four real conflict cases exact and
  keeps one numeric distinct-binner (EP-5).
- **Rejected:** making `identity` also flexible (a second way to spell numeric distinct
  binning, EP-5); leaving the rule vague (the original live conflict).
- **Affects:** Core (validate), Spec; spec §10.2 / §11.3; diagnostic
  `SourceValueTypeInvalid` (scope clarified). Resolves the D-049-deferred item; refines
  D-022.

### D-062: Cross-attribute restrict not modelled in v1; drop the diagnostic

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** cross-attribute restrict ("include attr A only when attr B = X") has no
  reserved carrier syntax in v1 and is **not modelled**. The named
  `RestrictCrossAttributeNotImplementedV1` diagnostic is **removed** from §20; it was
  unreachable, since no v1 syntax could trigger it. It remains prose-only future work.
- **Why:** a `*NotImplementedV1` reservation is only meaningful when a parseable
  carrier lets a v1 spec express the feature and be cleanly rejected (the `composite` /
  `date` / scale pattern). With no carrier the code was dead. Not every future idea
  needs reserved syntax (EP-3).
- **Rejected:** inventing a carrier now (EP-3: no current need); keeping the dead
  diagnostic (misleads readers into thinking the feature is expressible).
- **Affects:** Spec; spec §20; diagnostic `RestrictCrossAttributeNotImplementedV1`
  removed.

### D-063: restrict_to: M2 validates shape, M4 executes; diagnostic ownership

- **Status:** accepted *(refined by D-091: exact numeric restrict entries added; `RestrictToOnNumericRequiresRange` renamed `RestrictToNumericEntryRequired` in live text; execution realized by D-105)*
- **Date:** 2026-06-30
- **Decision:** M2 validated the *shape* of `restrict_to` at parse/validate even though
  execution was deferred to M4 (D-057). In M2, `RestrictToOnNumericRequiresRange`
  (Error) owned the numeric-source + non-range-entry mismatch, and conversion
  rejected all `restrict_to` with the transitional `RestrictToNotImplementedV1`.
  D-091 added exact numeric entries and renamed the code
  `RestrictToNumericEntryRequired`, which now owns a bare string on a numeric source;
  D-105 landed the rename with no alias, made `restrict_to` execute and retired the
  reject. `RestrictToValueNotInDomain` (Warning) is the explicit-domain typo-catcher.
  `SourceValueTypeInvalid` (§10.2) does **not** duplicate the numeric/string-entry
  case. Mixed string/range `restrict_to` lists round-trip (D-057) but, under the
  single-`value_type` matrix (D-061), each entry must match the attribute's type, so a
  genuinely mixed list is rejected at validation (§10.4).
- **Why:** D-057 established round-trip + execution-deferral, but §10.4 prose never
  documented the static checks the §16.4 table already listed, nor their precedence vs
  `SourceValueTypeInvalid`. One condition → one owning code (EP-14).
- **Rejected:** deferring shape validation to M4 with execution (an authoring error
  would surface late); letting both codes fire on the same mismatch (ambiguous, EP-5).
- **Affects:** Core (validate), Spec; spec §10.4 / §10.2; diagnostics
  `RestrictToOnNumericRequiresRange` (renamed by D-091/D-105),
  `RestrictToValueNotInDomain`. Refines D-057.

### D-064: Wide column object keys deferred to M3; object-key diagnostic taxonomy

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** wide `object_key.mode = "column"` (and the `duplicate_object_policy`
  machinery it governs) is parsed and round-tripped from M2, but its **execution was
  deferred to M3**, alongside the triple subject-derived column key; until then
  conversion rejected a wide column object key with the transitional
  `ObjectKeyColumnNotImplementedV1`. D-083 made wide column object keys execute and
  retired that code. Object-key validation gains a taxonomy: `ObjectKeyBindingInvalid`
  (malformed, e.g. column mode without a resolvable `column`),
  `ObjectKeyModeInvalidForShape` (e.g. `row_index` under triple);
  `ObjectKeyCompositeNotImplementedV1` is unchanged.
- **Why:** D-034 fully specified wide column-key behavior, but no milestone implemented
  it and no guard existed: a silent partial implementation (carrier parses, execution
  missing), the hole D-057 closed for `restrict_to`. M3 was already planned to build
  subject-derived column keys, so it was the natural home. Distinct conditions get
  distinct codes (EP-14).
- **Rejected:** implementing wide column keys in M2 (expands M2 scope; M3 is the
  object-key milestone); leaving execution unguarded (latent wrong output: a silent
  `row_index` fallback).
- **Affects:** Core (validate/plan), Sources, Spec; spec §5.4 / §6.1 / §16.4;
  diagnostics `ObjectKeyColumnNotImplementedV1` (transitional, removed at M3),
  `ObjectKeyBindingInvalid`, `ObjectKeyModeInvalidForShape`. Refines D-034.

### D-065: Calibration/vocabulary over the input universe, before restrict_to

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** the formal-attribute **vocabulary** and any auto-discretizer
  **calibration** are computed over the **input universe**, *before* `restrict_to`
  (§10.4) selects emitted objects. `restrict_to` filters emitted objects, never the
  calibration population or the column set. Post-filter empty columns are allowed
  (`AttributeHasNoCrosses`). Population-relative calibration (quantiles over only the
  surviving objects) is recorded as a **future option**, not a v1 setting.
- **Why:** the §7 phase order (Calibrate before Emit-time restriction, D-036) already
  implied this, but §19.4 (`equal_frequency` + `restrict_to` on TheilerStage) made the
  consequence non-obvious and the spec never stated it. "Define vocabulary first, then
  select objects" is the FCA-coherent model and keeps the schema stable under
  object-filter changes (reproducibility). Documenting it as a deliberate choice, with
  the §19.4 consequence visible, prevents it being read as a bug.
- **Rejected:** population-relative calibration as the v1 default/option (EP-3: no
  current need; a larger M4 design); leaving the interaction unspecified (a
  determinism and expectation gap).
- **Affects:** Conversion (calibrate/emit ordering), Spec; spec §7 / §19.4; `roadmap.md`
  (population-relative noted as future). Refines D-021 / D-036.

---

## Tier 2 register (pre-M2): model boundary + carrier scope

A pass fixing the M2 *implementation* contracts the earlier M2/Tier-1 decisions
left open: the in-memory two-model split, the resolve/validate seam, the
exact fingerprint encoding, and the carrier-vs-execution scope for discretizers,
absent domains, triple input, and `missing_policy`. These pin cross-package
contracts (EP-4) before the TOML reader/writer, migrator, and fingerprints are
built; they refine, not reverse, D-009 / D-049 / D-050…D-065.

### D-066: Parsed spec document model vs. resolved Core `BedrockSpec`

- **Status:** accepted
- **Date:** 2026-07-03
- **Decision:** M2 splits the spec into **two models**. The Spec layer owns a
  faithful, presence-tracked **document model** (`SpecDocument` and siblings under
  `src/FcaBedrock.Spec/Toml/`) mirroring the authored TOML: every section
  (`[spec]`, `[provenance]`, `[binding]`, `[defaults]`, `[output]`, `[[attribute]]`,
  `[[template]]`, `[[matcher]]`), optional/nullable fields tracking **presence**,
  authored-vs-default **provenance** for the round-trippable defaults (`boundary`,
  `direction`, `missing_policy`, `unknown_value_policy`, `display_name`,
  `formal_attribute_format`), a column bound **by name or by index**, and
  possibly-invalid parsed states. It **resolves+validates into** Core's
  `BedrockSpec`: the resolved model with defaults merged, `extends` applied,
  name→index resolved, illegal states unrepresentable (EP-10). A bind-by-header
  `NamedColumnSource` is therefore a **document-model state only**, never a
  resolved Core source; the Core wide binding carries a resolved column **index**
  plus a `value_type`. In M2 a triple `predicate` **attribute source** was also
  document-only; the one exception was the *basic triple binding shape*
  (`shape = "triple"`), which resolved into a **minimal Core carrier**, just enough
  for `ConversionPlanner` to reject a triple spec with `TripleSourceNotImplementedV1`
  (D-072, D-067). D-082 made triple conversion execute, and the triple binding and
  its `predicate` sources now resolve into Core.
- **Why:** round-trip fidelity (D-049 presence tracking, D-057 `restrict_to`
  preservation, D-052 `extends` merge) needs a model that holds exactly what was
  authored (provenance, unresolved references, an authored `[]`), while the
  planner's determinism guarantees need a model where bad states cannot occur
  (EP-10) and Core stays pure (EP-13). One model cannot be both. Splitting them
  confines Tomlyn and every authoring concession to Spec and hands the planner a
  clean resolved input.
- **Rejected:** a single model for both parse and plan: it either admits invalid
  states (defeating EP-10, scattering guards through the planner) or rejects at
  parse and loses the authored form a faithful round-trip needs (breaking
  D-049/D-057); resolving name→index inside Core: pulls header/schema knowledge
  into a pure package and lets a resolved source hold an unresolved reference.
- **Affects:** Spec (new `Toml/` document model), Core (`BedrockSpec`,
  `AttributeSpec`, wide binding + a minimal triple binding carrier (D-072),
  `OrdinalScale`, `ObjectKey` grow *resolved* fields), Diagnostics; spec §2 / §3 /
  §5.4 / §10.2. Defines public Core/Spec surface (EP-4). Realizes D-009; refines D-049.

### D-067: Resolve/validate seam and diagnostic phase ownership

- **Status:** accepted; the "invoked from the seam, not re-homed" parenthetical
  superseded by D-080
- **Date:** 2026-07-03
- **Decision:** the document→Core transformation is a **single
  `SpecResolver.Resolve(document) → Diagnosed<BedrockSpec>`** step that resolves and
  validates **together** (not two sequential passes): defaults merge, `extends`
  resolves, name→index resolves, and the static checks run against the resolving
  model, aggregating all diagnostics (EP-14). Each §16.4 code is **owned by exactly
  one phase**: construction-time invariants stay in Core smart factories
  (`CutValidation`, D-056; the `OrdinalScale`/`ObjectKey` factories, EP-10);
  resolution-time *static* checks (source-binding shape, `value_type` matrix,
  ordinal-over-cuts, `restrict_to` shape, object-key taxonomy, and the existing
  Core duplicate-name / `value_labels` checks, invoked from the seam, not
  re-homed) live in the seam; plan-time checks (`FormalAttributeCollision`,
  `ScaleNotImplementedV1`) stay in `ConversionPlanner`; data-phase codes fire in
  Calibrate/Emit. In M2 the planner also owned the transitional `*NotImplementedV1`
  rejects whose carriers resolved into Core, such as `RestrictToNotImplementedV1`
  (retired by D-105) and `TripleSourceNotImplementedV1` (retired by D-082). A
  read/resolve-owned transitional reject with no carrier built, such as
  `DiscretizerKindNotYetSupported` (D-070; retired by D-104), fired in the seam.
  This seam is the real M7 `validate` caller, so it is built where it is used, not
  speculatively (EP-3). One condition → one owning code.
- **Why:** M2 is the first point hand-authored TOML can express invalid specs, so
  the ~20 static diagnostics need a definite home and a definite phase. A combined
  resolve+validate avoids a half-resolved intermediate a separate validate pass
  would re-derive; single-owner phasing keeps §16.4's "Where" column honest and
  stops two codes firing on one mistake (EP-5). Reusing the existing Core static
  checks from the seam (not big-bang-refactoring them) keeps the diff surgical
  (EP-1).
- **Rejected:** sequential resolve-then-validate (a throwaway half-resolved model,
  and cross-field checks want the merged view); scattering static checks across
  reader, Core, and planner (drifts from the §16.4 phase column, risks double
  reporting).
- **Affects:** Spec (`SpecResolver`), Core (factories, planner guards),
  Diagnostics; spec §7 / §16.4. Realizes the §16.4 "Where" column; pairs with D-066.

### D-068: `missing_policy = "as_attribute"` scheduled into M2; effective-`missing_token` migration

- **Status:** accepted (schedules the previously-unscheduled §10.5 branch; migrator branch realized by D-079)
- **Date:** 2026-07-03
- **Decision:** `missing_policy = "as_attribute"` (§10.5), omitted from the M1
  pipeline, is **implemented in M2**. Plan appends a `{column}-missing`
  `FormalAttribute` at the correct ordinal position (after the value columns for
  nominal; the second column for dichotomic) with its own canonical identity; Emit
  crosses it when a value is *missing* (empty cell or explicit `missing_token`
  match, never a merely unparseable numeric, D-050) instead of the M1
  unconditional skip. The `.bed` migrator detects it structurally: a
  `[Category Values]` entry equal to the **effective `binding.missing_token`** (the
  resolved token, *not* a hardcoded `?`) becomes `missing_policy = "as_attribute"`
  and is excluded from `declared_domain`.
- **Why:** the column set `as_attribute` produces is deterministic and enters
  `schema_fingerprint` through the planned list (§14), so leaving it unimplemented
  while M2 ships fingerprints would freeze an incomplete schema; and the migrator
  must recognize a non-`?` missing token or it silently drops a real v2 attribute.
  Anchoring the ordinal-position rule and the effective-token rule keeps the
  planner/emitter/migrator (three packages) consistent (EP-4).
- **Rejected:** deferring `as_attribute` past M2 (its column is a fingerprint
  input, §14: would ship a knowingly-incomplete schema hash); hardcoding `?` in
  the migrator (misreads any spec with a custom `missing_token`).
- **Affects:** Core (`ConversionPlanner`, `PlannedAttribute`), Conversion
  (`Emitter`), Spec (`BedToSpec`); spec §10.5 / §14 / §17. Byte-neutral on the M1
  goldens (no fixture uses `as_attribute`).

### D-069: Canonical fingerprint encoding, pinned (appendix to D-053)

- **Status:** accepted (appends D-053; pins the exact structure before any stored hash ships)
- **Date:** 2026-07-03
- **Decision:** the plan-derived canonical structure D-053 mandated is pinned
  concretely, so a stored hash is portable and version-tagged before Slice E ships
  one (EP-11). It is **UTF-8, no BOM**, with sorted object keys, arrays in planned
  order, and this fixed shape:
  - a root object carrying `"fp_format": 1` (the encoding **version literal**;
    bumped only on an incompatible encoding change) and a `"kind"` of `"schema"` /
    `"cxt_output"` / `"dat_output"`;
  - **schema** content = `"attributes"`, an **ordered array** (plan/column order)
    of canonical-identity objects, each carrying the §14 four-tuple under fixed keys
    `"name"`, `"scale"`, `"op"`, `"bin"`, where `"bin"` encodes a cut bin as
    `{"lo":…, "hi":…, "lo_open":<bool>, "hi_open":<bool>}` with **open ends as JSON
    booleans, never `∞`/`"all"` strings**, a value bin as its string label, and a
    threshold by its canonical key;
  - **cxt_output / dat_output** = an object nesting the schema array under
    `"schema"`, the D-051 **shared** row/binding inputs under `"shared"`, and the
    per-format inputs under `"cxt"` / `"dat"` (rendered names + label style +
    `bin_label_unicode` + `.cxt` writer settings for cxt; `base_index` + line
    endings + trailing-space for dat), never mixed;
  - **numbers** are the *parsed* numeric value reformatted with invariant, shortest
    round-trippable .NET formatting, so `30`, `30.0`, `3e1` collapse and no machine
    float drift occurs (EP-11);
  - **strings** use one JSON escaping rule (minimal `\"`, `\\`, control escapes,
    UTF-8 passthrough otherwise).

  Slice E realizes exactly this and ships the canonical-stability golden that locks it.
- **Why:** D-053 fixed the *properties* (version tag, sorted keys, shortest
  numbers, structural open ends) but not a concrete shape, and a durable hash
  contract (EP-11) cannot ship half-specified: the first stored fingerprint
  fossilizes whatever the encoder emits. Pinning the root shape, field names, and
  version literal now makes Slice E mechanical and the golden a genuine lock.
- **Rejected:** hashing TOML text (D-053, formatting-sensitive); leaving field
  names to the encoder (the first stored hash fossilizes an unreviewed shape);
  `∞`/`"all"` sentinels for open ends (string-fragile; booleans are exact);
  `"R"`/`"G17"` floats (17 digits defeat the `30.0`≡`30` collapse, D-053).
- **Affects:** Spec, Core (fingerprint encoder); spec §3 / §14. Appends D-053;
  supports D-051.

### D-070: Minimal M2 discretizer-carrier scope; three-tier kind response

- **Status:** accepted; the transitional `DiscretizerKindNotYetSupported` fully retired by D-104
- **Date:** 2026-07-03
- **Decision:** M2 answered a discretizer `kind` in **three tiers**. The M1
  kinds (`identity`, `manual_cuts`, `ordered_cuts`) parsed, resolved and
  converted. The four kinds planned for M4 (`free_per_value`, `equal_width`,
  `equal_frequency`, `value_groups`) were recognized by **kind name only** and
  rejected at **read/resolve** with the transitional
  `DiscretizerKindNotYetSupported`, with no parameter carrier and no round-trip
  promise. Any other `kind` was a generic unrecognized-kind **parse** error. Each
  kind left the transitional set as it landed, and D-104 retired the code with
  the last one; an unrecognized kind is now an ordinary `SpecFieldInvalid`
  (spec §16.4).
- **Why:** M2's job was the format, migrator, fingerprints, and `extends`, not new
  discretizers (M4 is the discretizer milestone). Carriers for parameter shapes M2
  could not execute would have been speculative surface (EP-3/EP-6), and no v2
  `.bed` type maps to these vNext-native discretizers, so migration lost nothing.
  Recognizing the *name* separated "valid v1 feature, later milestone" from a
  typo, so an author could tell which they hit (EP-14). Rejecting rather than
  silently accepting avoided the wrong-output hole D-057 closed for `restrict_to`.
- **Rejected:** implementing the M4 discretizers in M2 (scope creep, EP-1); building
  full round-trip carriers for their parameter shapes in M2 (speculative surface);
  one code for both not-yet-supported and unknown kinds (hides whether the spec is
  valid, EP-14); silently ignoring unimplemented kinds (latent wrong output).
- **Affects:** Spec (reader/resolver), Diagnostics; spec §11 / §16.4; diagnostic
  `DiscretizerKindNotYetSupported` (transitional, retired by D-104). Refines
  D-020 / D-061.

### D-071: Absent/empty `declared_domain`: M2 interim reject until calibrate

- **Status:** accepted (sequences §10.3 for M2; refines D-036; refined by D-076:
  retirement is "when observed-domain calibration lands", not a fixed milestone;
  retired by D-098)
  *(authored-empty reading revised by D-122: an authored `[]` is a complete fixed
  empty domain (zero declared value bins), not absent; only omission requests
  calibration)*
- **Date:** 2026-07-03
- **Decision:** in M2, omitted `declared_domain` **and** an explicit empty `[]`
  both resolved as **absent** (§10.3 as then written), and the reader/writer
  **round-trips the authored form verbatim** (omitted stays omitted, `[]` stays
  `[]`). Observed-domain calibration was not built in M2, so an M2 conversion of
  `identity` (the only M2 value-bin discretizer) with an absent domain was
  **rejected** with the transitional `ObservedDomainCalibrationNotImplementedV1`,
  never silently emitting an empty or data-order-dependent schema. D-098 retired
  the reject when the Calibrate phase began filling an omitted domain from the
  data.
- **Why:** the authored `[]`-vs-omitted distinction must survive round-trip
  (D-049) even though both then meant "absent," so provenance is preserved
  without inventing a third state. An absent value-bin domain has no columns
  until calibration observes the data; converting it without Calibrate would
  have emitted zero columns or depended on input order, against the spec's
  reproducibility intent.
- **Rejected:** treating `[]` as "zero columns" (contradicted §10.3 as then
  written); silently calibrating in M2 (Calibrate was not built: a latent,
  undocumented data-dependence); collapsing omitted and `[]` at read time (loses
  authored provenance, D-049).
- **D-122 revision (2026-07-22):** the `[]`-resolves-as-absent half of this
  decision is revised: an authored `[]` is complete (a fixed empty domain with
  zero declared value bins; `unknown_value_policy = "include"` may still extend it
  and `missing_policy = "as_attribute"` may still add its missing column). The
  verbatim round-trip rule still stands.
- **Affects:** Spec (reader/writer), Core (planner guard), Diagnostics; spec §7 /
  §10.3; diagnostic `ObservedDomainCalibrationNotImplementedV1` (transitional,
  retired by D-098). Refines D-036; pairs with D-070. Revised by D-122.

### D-072: Basic triple TOML carrier in M2; conversion deferred to M3

- **Status:** accepted (sequences §5.3 for M2; refines D-009)
- **Date:** 2026-07-03
- **Decision:** M2 carries and **round-trips** the *basic* triple binding:
  `shape = "triple"`, `ordering`, `columns` (`subject`/`predicate`/`value`), and
  `{ kind = "predicate", name = … }` attribute sources. In M2, **conversion
  rejected** any triple spec with the transitional `TripleSourceNotImplementedV1`;
  that reject was **required** so a triple spec was not silently mis-converted as
  wide. The **advanced** triple surface (triple header rows, binding `columns` by
  header **name**, object/subject-name filtering) was left to **M3** and neither
  parsed nor relied on in M2 (§5.3), and the triple *execution* diagnostics
  (`TripleSubjectNotContiguous`, …) belonged to M3, not M2. D-082 made triple
  conversion execute, retired the reject and settled the advanced surface.
- **Why:** round-trip and migration needed the basic triple carrier in M2 (the three
  v2 `mini-*_triples` examples must survive read→write→read as documents), but the
  triple *reader/streaming* was the M3 milestone. Parsing-but-rejecting closed the
  silent-mis-conversion hole (D-057 pattern) while keeping M2 scoped to the format.
  Deferring the advanced surface matched the §5.3 M3 finalization note.
- **Rejected:** pulling triple conversion into M2 (that was the whole M3 milestone:
  scope creep, EP-1); omitting the triple carrier from M2 (migration of the
  `mini-*_triples` specs would drop config, D-049); parsing the advanced surface in
  M2 (no M2 consumer, EP-3).
- **Affects:** Spec (reader/writer), Core/Conversion (planner guard), Diagnostics;
  spec §5.3 / §16.4; diagnostic `TripleSourceNotImplementedV1` (transitional,
  removed at M3). Refines D-009; the M3 triple audit (D-082) settled the advanced
  surface.

---

## M2 implementation (slices)

### D-074: `as_attribute` missing-column position uniform across scale kinds (appendix to D-068)

- **Status:** accepted (appends D-068; lands with M2 Slice B); the rendered-name
  clause describes the **default** naming path: under an explicit
  `formal_attribute_format` the missing column renders through that format like
  every other formal attribute the logical attribute emits (D-117). Its
  **position** and canonical identity are unchanged.
- **Date:** 2026-07-04
- **Decision:** the `{column}-missing` column appends **after the scale's columns
  for every scale kind**. D-068 named nominal (after the value bins) and
  dichotomic (the second column); ordinal, reachable in M2 via the string
  value-bin path (D-047/D-070), follows the same rule: after the threshold
  columns. Canonical identity is `(name, scale_kind, "missing", "")` (§14); the
  rendered name is the literal `{column}-missing`, bypassing `value_labels` and
  label style ("missing" is not a raw value). Emit-time contract:
  `PlannedAttribute` carries the resolved missing formal-attribute id (null =
  missing values skip) instead of the policy enum, so the emitter never
  reinterprets policy; `AttributeSpec.MissingPolicy` remains the policy source
  for planning and for the Slice E output-fingerprint shared inputs.
- **Why:** the planner's append branch is scale-agnostic, so ordinal support is
  free; excluding it would cost a scale-kind special case and leave
  `ordinal + as_attribute` silently behaving as skip (the exact gap D-068
  closes), which Slice E would then freeze incorrectly into `schema_fingerprint`.
- **Rejected:** rejecting `as_attribute` on ordinal scales (extra code plus a
  transitional diagnostic for a combination that works uniformly); keeping
  `MissingPolicy` on `PlannedAttribute` alongside the id (two fields with an
  invariant to keep in sync).
- **Affects:** Core (`ConversionPlanner`, `PlannedAttribute`), Conversion
  (`Emitter`); spec §10.5 (ordinal clause). Appends D-068.

### D-075: Slice C TOML reader/writer contract: strictness, parse codes, canonical form

- **Status:** accepted; the deferred-surface set narrowed by D-078/D-120 to
  `value_type = "date"`
- **Date:** 2026-07-04
- **Decision:** the M2 Slice C reader/writer fixes the contracts the spec left
  open:
  - **Library:** Tomlyn (D-009's lean), exact-pinned via central package
    management and confined to `FcaBedrock.Spec` (D-066); the reader walks the
    CST (`SyntaxParser.Parse` → `DocumentSyntax`) for node spans, and no Tomlyn
    type appears on any public signature.
  - **Reader strictness:** unknown keys/tables are **Errors**
    (`SpecKeyUnrecognized`): the spec is silent on unknown-key policy, and a
    reader that accepted what the writer would drop makes read→write silently
    lossy. Known keys with the wrong type/shape/spelling are `SpecFieldInvalid`
    (one code, message names the expected form; also the D-070 tier-3
    unknown-kind case). TOML-level errors are `SpecTomlInvalid` (Fatal; Tomlyn
    parser warnings surface under the same code at Warning).
  - **Two-phase aggregation (the EP-14 reading):** all TOML syntax errors report
    together and are terminal (a broken tree would cascade garbage); on clean
    syntax, one whole-document semantic pass aggregates every diagnostic.
  - **Deferred-surface scaffolding:** at Slice C, recognized-but-unmodelled v1
    surface (`[spec].extends`, `[[template]]`/`[[matcher]]`, attribute
    `template` / `display_name` / `formal_attribute_format`, `[defaults]`
    `formal_attribute_format`, `value_type = "date"`) failed the read with the
    transitional `SpecSurfaceNotYetSupported` (Error) so nothing known was
    silently dropped. The set was **closed and per-owning-table**, never a
    fallback: near-miss keys and a listed name in the wrong table got
    `SpecKeyUnrecognized`. Entries retire as slices D–G land their carriers;
    the `date` entry retires when the D-038 carrier lands (the v1 end-state is
    the plan-phase `DateValueTypeNotImplementedV1`).
    D-078 retired the `extends`, template and matcher entries, and D-120
    retired the naming keys. `SpecSurfaceNotYetSupported` now covers only the
    value-level `value_type = "date"` reject (spec §16.4).
  - **Canonical writer:** hand-rolled emission (not Tomlyn serialization: the
    canonical form is owned here and cannot drift with a library upgrade):
    authored-only fields (presence tracking survives verbatim, D-049/D-071),
    fixed section/key order (spec presentation order), inline tables for the
    nested groups, LF-only/no-BOM, invariant shortest numbers (integral doubles
    as bare integers, matching §11.2's own examples), RFC 3339 date-times, and
    `value_labels` in authored order (never sorted; label order is inert). The
    round-trip contract is **document-model fidelity, not byte fidelity** of
    authored files (§2 makes formatting informative; fingerprints hash the
    plan, D-053); the test oracle is canonical-text idempotence.
  - **`created_at`:** offset date-times verbatim; local forms coerce to a
    zero-offset `DateTimeOffset` (deterministic across machines; the field is
    inert provenance, §4).
  - **API:** `SpecReader.Read(string toml, string? filePath = null)` →
    `Diagnosed<SpecDocument>` and `SpecWriter.Write(SpecDocument)` → `string`:
    string-only, mirroring `BedReader`; file I/O belongs to a host slice.

  The slice also realizes D-010 for scales: `interordinal`/`biordinal`/
  `contranominal` parse into a kind-only `DeferredScaleSection`, resolve into
  the Core `UnimplementedScale` reject-carrier (the D-072 pattern), and fail at
  **plan** with `ScaleNotImplementedV1` (Fatal): parse-but-fail-to-plan.
- **Why:** round-trip fidelity is a headline property of the format, so reader
  strictness and writer canonicalization must be decided together: the reader
  must reject exactly what the writer cannot re-emit. Distinct codes keep
  typo-vs-valid-feature actionable (the D-070 rationale); the closed
  deferred-surface set keeps the transitional code from becoming a catch-all.
  Hashing is already formatting-immune (D-053), so one canonical written form
  costs nothing and buys deterministic output and a trivial round-trip oracle.
- **Rejected:** warning-and-drop for unknown keys (silent loss on write-after-
  read); one code for all parse problems (hides whether the spec is valid v1,
  EP-14); parsing deferred surface into inert carriers now (pulls Slice F /
  naming-slice semantics forward, and an authored-but-ignored
  `formal_attribute_format` would silently change intended output); Tomlyn's
  serializer for writing (its formatting choices can drift across versions);
  byte-preserving round-trip (would require a lossless CST document model for
  no consumer; §2 makes formatting informative).
- **Affects:** Spec (`SpecReader`, `SpecWriter`, `DeferredScaleSection`, resolver
  mapping), Core (`UnimplementedScale`, planner guard), Diagnostics (`SpecTomlInvalid`,
  `SpecKeyUnrecognized`, `SpecFieldInvalid`, `DiscretizerKindNotYetSupported`,
  `SpecSurfaceNotYetSupported`, `ScaleNotImplementedV1`), spec §16.4,
  `Directory.Packages.props`. Realizes D-009/D-066/D-070/D-071/D-072 read/write
  faces; realizes D-010 for scales.

### D-076: Slice D seam/plan validation contract details

- **Status:** accepted (appends D-067; realizes D-054/D-060/D-061/D-063/D-064/
  D-071 at the seam and planner) *(refined by D-091: exact numeric restrict entries added; `RestrictToOnNumericRequiresRange` renamed `RestrictToNumericEntryRequired` in live text)*
- **Date:** 2026-07-05
- **Decision:** Slice D activates the static validation the earlier decisions
  assigned but left operationally open; the details settled here:
  - **`include = false` interaction matrix.** (a) The `restrict_to` **shape**
    checks (`RestrictToNumericEntryRequired`; the range-entry-on-string-source
    case of `SourceValueTypeInvalid`) run on **excluded/filter-only attributes
    too**: `restrict_to` is *live* config, not parked; §10.1/§10.4 apply it
    whether or not the attribute is included (the §19.4 filter-only pattern), so
    its static shape is validated on the same terms. (b)
    `RestrictToValueNotInDomain` does **not** run on excluded attributes:
    `declared_domain` is emitted-shaping config, parked under D-049; a live
    check must not warn against a dormant list. (c) A **parked numeric-cut
    discretizer still types a live `restrict_to`** (an excluded attribute
    retaining `manual_cuts` resolves `value_type = "number"`, so a bare-string
    entry rejects). Deliberate, not a D-049 violation: `value_type` is a
    source-level property whose D-061 derivation is include-independent, and
    §10.4 itself defines a numeric source as "`value_type = "number"`, *or a
    numeric-cut discretizer*" (recorded so the combination is not later "fixed"
    into a silent skip). The remaining Slice D checks (the `value_type` matrix,
    ordinal-over-cuts) are include-gated per D-049/D-060 ("active attribute").
  - **Quote/delimiter co-fire.** `QuoteCharNotSupportedV1` (authored quote ≠
    `"`) and `BindingDelimiterQuoteConflict` (resolved delimiter = resolved
    quote) are distinct §5.1 conditions and report independently; both fire
    when both hold (e.g. delimiter and quote both authored `|`). Two conditions,
    two codes (EP-14), not double reporting of one.
  - **`ObservedDomainCalibrationNotImplementedV1`** (retired by D-098) was an
    **Error at plan** and **blanket across scales** for an included `identity`
    attribute with an absent domain: dichotomic included, because with no domain
    every observed value is "unknown" and the single column never crosses (the
    same silent-wrong-output D-071 closed). The blanket covered the value-bin
    ordinal path too: `scale.order` orders bins, but the domain remains the bin
    *source* for `identity`, so an ordinal `order` never substitutes for a
    domain. Retirement was phrased "when observed-domain calibration lands" (the
    categorical case was then unassigned in the roadmap backlog), not a bare "M4".
  - **Domain typo-catcher condition.** `RestrictToValueNotInDomain` also
    requires a resolved **string** `value_type`: on a mis-typed numeric
    `identity` source the same entries are already owned by
    `SourceValueTypeInvalid` / `RestrictToNumericEntryRequired`, and a third
    diagnostic would be noise (EP-14).
- **Why:** the phase/owner assignments were settled (D-060/D-063/D-064/D-067/
  D-071), but the excluded-attribute interactions, the co-fire policy, and the
  blanket's scale coverage were not derivable from any single entry, and each
  reads as a bug (a D-049 violation, a EP-14 violation, an over-broad reject)
  unless the rationale is on record.
- **Rejected:** skipping restrict_to shape checks on excluded attributes (a
  filter-only attribute's restrict_to changes output at M4, so its shape errors
  must surface at authoring time); warning against a parked domain (turns
  toggling an attribute off into new warnings: the authoring-hostility D-049
  removed); suppressing the delimiter conflict when the quote is unsupported
  (hides an independent, independently-fixable mistake).
- **Affects:** Spec (`SpecResolver` seam checks), Core (`ConversionPlanner`
  guards), Diagnostics (the twelve Slice D codes); spec §10.3 / §16.4. Each
  matrix point is locked by a dedicated test in `SpecResolverTests` /
  `ConversionPlannerTests`.

### D-077: Slice E fingerprint encoding/verification contract details

- **Status:** accepted (appends D-069; realizes D-035/D-051/D-053/D-069 in code)
- **Date:** 2026-07-05
- **Decision:** Slice E ships the fingerprint encoder
  (`FcaBedrock.Core.Fingerprinting`), the plan-side structural bin, and
  stored-fingerprint verification (`SpecFingerprints`); the residual details
  D-069 left open are pinned here:
  - **Hash string:** `"sha256:" + 64 lowercase hex chars` over the canonical
    UTF-8 (no BOM) bytes. Verification compares the full stored string
    ordinally; a malformed stored value simply reads as stale (no parse-time
    format validation).
  - **Byte rules:** compact JSON (no insignificant whitespace); one escaping
    rule (`\"`, `\\`, the `\b \t \n \f \r` shorthands, remaining C0 controls as
    lowercase `\u00xx`, raw UTF-8 otherwise); doubles in invariant shortest
    round-trippable form, integers invariant decimal; the canonical writer is
    hand-rolled (`CanonicalJson`), never a JSON library, so the form cannot
    drift with a library upgrade (the D-075 rationale on a hash contract).
  - **Cut bins:** always the fixed four-key object
    `{"hi":…,"hi_open":…,"lo":…,"lo_open":…}`; an unbounded end is `null` plus
    its `*_open: true` flag. **`lo_open`/`hi_open` mean *unbounded end* (the
    bin runs to ±∞ on that side), never interval inclusivity**: every bounded
    cut bin is uniformly half-open `[lo, hi)` (§11.2), so `<30` carries
    `hi_open: false`. Bounds are JSON numbers for `manual_cuts`, JSON strings
    for `ordered_cuts`. Non-interval bins stay plain strings: value bins,
    ordinal thresholds (incl. `all`, D-047), the dichotomic empty key, the
    missing column's `missing`.
  - **The plan carries the structure:** `FormalAttribute` gains a public
    `CanonicalBin` (`ValueBin` / `NumericCutBin` / `TextCutBin`; `null` bound =
    unbounded), produced by the same planner walk as the identity's `BinKey`:
    single producer, no string parse-back (EP-4 surface, byte-neutral on the M1
    goldens).
  - **Vocabulary:** JSON enum spellings are the spec's TOML spellings; label
    style spells `"native"`/`"v2-compat"`; line endings hash as the
    `"lf"`/`"crlf"` tokens, never raw control characters. All object keys are
    fixed ASCII, sorted ordinal.
  - **`shared` content (M2):** binding = `{delimiter, encoding, has_header,
    locale, missing_token, object_key, quote_char, shape}`, with `encoding`
    the constant `"utf-8"` until M3 models encoding in Core (UTF-8 specs keep
    their hash when it lands); attributes = **included attributes only**, spec
    order, each `{declared_domain, discretizer, missing_policy, name, scale,
    source, unknown_value_policy}`. `declared_domain` is the **effective**
    domain (`Discretizer.ConsumesDeclaredDomain`): cut discretizers hash `[]`,
    so an inert authored domain never moves an output fingerprint (§10.3, the
    D-049 parked-config discipline). `manual_cuts.Culture` is not encoded: it
    *is* `binding.locale`, and double-counting is the mistake D-035 removed.
    `restrict_to` is absent until executable (§14); M4 adds it
    present-only-when-non-empty, so restrict_to-free specs keep their hashes,
    and filter-only attributes join through that same route.
  - **`cxt`/`dat` content:** per D-069 (`bin_label_unicode` + `label_style` +
    `line_endings` + `rendered_names` + `trailing_newline`; `base_index` +
    `empty_line_trailing_space` + `line_endings` +
    `nonempty_line_trailing_space`). **`size_advisory_bytes` is not an input**:
    it changes a warning, never output bytes; §3's byte-affecting definition
    wins over §8's blanket sentence (clarified there). `bin_label_unicode`
    hashes the resolved flag although Unicode rendering is not built yet: when
    rendering lands, rendered names change and the stale warning fires, the
    mechanism working as designed.
  - **Pairing precondition:** `ComputeCxtOutputFingerprint`'s `LabelStyle`
    input must be the style the plan was produced with (rendered names bake it
    in); `SpecFingerprints.ComputeNative` guarantees the Native/Native pairing
    (v2-compat is a CLI override, D-011); the M7 effective path pairs
    V2Compat/V2Compat.
  - **Verification:** defined over a successful plan only (a failed
    resolve/plan already fails the run). Absent stored field → silent (§3
    optional); match → silent (no "verified" noise, EP-3); mismatch → its own
    Warning (`SchemaFingerprintStale` / `CxtOutputFingerprintStale` /
    `DatOutputFingerprintStale`, phase "spec load"); all stale fields co-fire
    (the D-076 stance). Slice E added no production call site (the D-067
    pattern); the M7 CLI became its caller and enforces the §14 fully-frozen
    gate when writing stored fingerprints (D-122/D-123).
- **Why:** the first stored hash fossilizes the encoder's bytes, so every
  residual freedom (compactness, escaping, the unbounded-end encoding, the key
  vocabulary, the exact `shared` key set) had to be pinned and golden-locked
  before any spec ships with a stored value (EP-11, D-069). The locks: the
  hand-authored canonical-JSON goldens plus a hard SHA-256 vector
  (`FingerprintCalculatorTests`), the roadmap 30/30.0/3e1 TOML golden, and the
  §19.1/§19.2 end-to-end baselines (`SpecFingerprintsTests`).
- **Rejected:** renaming `lo_open`/`hi_open` to `*_unbounded` (amends D-069's
  pinned field names for a readability gain the definition here provides);
  omitting the `lo`/`hi` key on unbounded ends (a conditional shape; the fixed
  four-key object is the literal D-069 reading); hashing the authored
  `declared_domain` under cut discretizers (an inert edit would move output
  fingerprints); a `System.Text.Json` writer (library-version drift on a durable
  hash contract); raw line-ending characters in the canonical bytes (escape
  noise; the token names are the spec's own vocabulary).
- **Affects:** Core (`CanonicalBin` and its leaves, `FormalAttribute.Bin`, the new
  `Fingerprinting` namespace: `FingerprintCalculator`, `CanonicalJson`), Spec
  (`SpecFingerprints`), Diagnostics (the three stale codes); spec §3 / §8 /
  §14. Appends D-069; byte-neutral on the M1 goldens.

### D-078: Slice F composition/carrier contract details

- **Status:** accepted (realizes D-027/D-052; refines D-067/D-075); the clauses
  this entry deferred to M6 (pattern semantics and validation: arity, regex
  syntax; template resolution precedence; within-file duplicate ids; and the
  shared-config-record question) are settled by D-114/D-115/D-116/D-118. Its
  carrier, composition, and "templates never resolve into Core" contracts stand.
- **Date:** 2026-07-05
- **Decision:** Slice F ships §13 `extends` composition and the
  template/matcher document carriers; the contracts the earlier decisions left
  operationally open are pinned here:
  - **Composition is a document→document step preceding the seam.**
    `SpecComposer.Compose(document, documentKey, source) → Diagnosed<SpecDocument>`
    walks the chain and folds the §13 merge base-most first, producing a flat
    document (`extends` consumed) that the unchanged
    `SpecResolver.Resolve(document, schema)` consumes. The §13 merge rules are
    defined over *authored* surface, so the merge must run on the
    presence-tracked model (`null` never overrides an authored value; `derived
    ?? base` is the only override operator). For §16.4 phase ownership,
    composition is part of the **spec-resolve phase**: the
    `SpecExtendsNotFound`/`SpecExtendsCycle` "Where = spec resolve" rows stand
    unchanged. This refines D-067's "extends resolves in the seam": the seam
    stays a single loader-free resolve+validate step; composition precedes it.
  - **Loading seam; no file I/O in Spec.** `ISpecTextSource.Load(reference,
    referrerKey) → SpecSourceText(CanonicalKey, Toml)?` supplies base text; the
    source owns reference→canonical-key resolution (relative paths, case
    rules), so path/OS determinism hazards never enter Spec logic, and the
    composer compares canonical keys ordinally (cycles = a revisited key,
    including self-extends). Slice F's production API is deliberately
    **string/text-source only**: file loading is **M7 host work, not missing
    Slice F work** (D-075 "file I/O belongs to a host slice", EP-3); tests
    compose through an in-memory source.
  - **Resolve-without-compose is a call-contract violation.** `Resolve` on a
    document with an authored `extends` throws `ArgumentException`: the
    document is not bad; the *call* skipped composition. Resolve never throws
    for valid inputs under its contract; an uncomposed extends document is
    invalid input to Resolve. Extends can therefore never be silently ignored,
    and no diagnostic code is spent on a host-sequencing error.
  - **Version gating.** Every spec in a chain must itself declare
    `version = 1`: the composer gates the **root before any source
    consultation** (an unversioned root must not drive v1 extends semantics or
    surface a missing-base/cycle diagnostic first) and **each base at its
    load** (Fatal `SpecVersionUnsupported`, base-file location). The composed
    document's version (the derived file's) is re-checked only by the seam;
    per flow the condition fires exactly once.
  - **Merge details** (beyond §13's own rules): the composed `[spec]` is
    derived-only (version, description, stored fingerprints; base-stored
    fingerprints never merge); the nested `[binding]` tables (`columns`,
    `object_key`) override as **whole values** (per-leaf mixing would compose
    an incoherent key-mode hybrid or a partial triple remap with silently
    duplicated role indices, unvalidated until M3), unlike `[output]`'s
    per-leaf merge; attribute/template replacement searches only the **base
    region** of the working list and marks each name/id overridden at most
    once, so authoring duplicates are preserved into the composed document for
    the flat-file diagnostics (`AttributeNameDuplicate`) rather than silently
    collapsed.
  - **Template/matcher carriers.** `TemplateSection` mirrors the
    `AttributeSection` config fields (minus `name`/`source`/`description`,
    §9.1) as a deliberately **flat** record; a shared config record earns its
    keep when M6 applies templates. `MatcherSection`/`MatchSection` carry
    `match` at authored shape (`source_index_range` at authored arity; M6 owns
    pattern semantics). Template bodies parse with attribute strictness:
    per-attribute identity fields fall to `SpecKeyUnrecognized` (the D-075
    wrong-table stance); at Slice F the naming-deferred keys stayed
    `SpecSurfaceNotYetSupported` and deferred discretizer kinds stayed
    `DiscretizerKindNotYetSupported` (D-070). Same-`id` template merge
    (replace in place, first base-region match) is **carrier composition
    only**, not resolution precedence; within-file duplicate ids were
    unvalidated until M6 (D-116 rejects them).
  - **Use-reject ownership.** Until D-121 retired it,
    `TemplateMatcherNotImplementedV1` fired at the **seam**, not the planner:
    templates/matchers never resolve into Core, so by D-067's own criterion
    (plan-time rejects are for carriers that resolve into Core) a Core
    reject-carrier would have been speculative surface removed at M6.
  - **Deferred-surface retirement.** `extends`, `[[template]]`/`[[matcher]]`,
    and attribute `template` leave the D-075 `SpecSurfaceNotYetSupported` set
    (their sets/branches deleted); the remaining entries
    (`display_name`/`formal_attribute_format`, `value_type = "date"`) were
    left to the naming-fidelity slice (D-120) and D-038.
- **Why:** D-027/D-052 fixed the merge semantics but not the API shape, phase
  ownership, version gating, nested-table granularity, duplicate handling, or
  carrier scope, and each unpinned point reads as a bug or an invitation to
  scope creep without the rationale on record. The fingerprint invariant (§13:
  composed ≡ flat, all three fingerprints) is locked by tests over the
  unchanged Slice E machinery; composition needed no encoder change, which is
  itself evidence the document→document design is at the right altitude.
- **Rejected:** merging inside `Resolve` (a loader on the seam signature and a
  half-composed intermediate, the shape D-067 already rejected); a
  resolve-side "not found" diagnostic for the uncomposed case (synthesizes a
  fake condition for a host bug and dilutes `SpecExtendsNotFound`); per-leaf
  merge of `columns`/`object_key` (incoherent hybrids); collapsing same-name
  derived duplicates (masks `AttributeNameDuplicate`); a shared
  attribute/template config record now (churns every construction site for a
  duplication M6 may reshape anyway); a compose-then-resolve convenience
  overload with no caller before M7 (EP-3).
- **Affects:** Spec (`SpecComposer`, `ISpecTextSource`/`SpecSourceText`, the
  template/matcher carriers, `SpecResolver`), Diagnostics (`SpecExtendsNotFound`,
  `SpecExtendsCycle`, `TemplateMatcherNotImplementedV1`); spec §9 / §13 / §16.4.
  Realizes D-027/D-052; refines D-067/D-075; byte-neutral on the M1 goldens and
  the Slice E fingerprint baselines (Core untouched).

### D-079: Slice G `.bed` migrator contract: document-model target, Diagnosed surfaces

- **Status:** accepted (realizes D-009's save-as-TOML face, the D-049
  migrator-hygiene item, D-068's migrator branch, and D-057's carriage;
  supersedes the M1 Core-targeting `BedToSpec`) *(numeric restrict migration refined by D-091: a parseable v2 numeric token migrates to an exact `{ value = n }` entry)*
- **Date:** 2026-07-05
- **Decision:** the one-way v2 migrator targets the **document model**:
  `BedMigrator.Migrate(BedDocument, BindingSection, ScalingMode, derivedFrom?) →
  Diagnosed<SpecDocument>` replaces `BedToSpec` (Core-targeting, throwing), and
  `BedReader.Read(text, filePath?)` returns `Diagnosed<BedDocument>` (structural
  problems (missing section, entry-count shortfall, unparseable count/convert
  flag) are `BedStructureInvalid`, Fatal, aggregated). A migrated spec is
  written by `SpecWriter`, resolved through the one seam (D-066/D-067), and
  fingerprinted like any authored spec; the golden harness runs this
  migrate→resolve route. Contract points:
  - **Carry what the carrier represents; validation stays at the seam.** The
    migrator transcribes; representable-but-invalid config (e.g. non-ascending
    cuts) carries verbatim and fails at resolve under its owning code
    (D-056/D-067); an *included* malformed-cut `.bed` therefore now fails at
    resolve, not migrate. Only transcription failures diagnose at migrate: an
    unparseable numeric cut token (the carrier stores numbers) or a dichotomic
    true value equal to the effective missing token (contradicts the D-068
    domain exclusion; no seam check would catch it) → `BedAttributeConfigInvalid`;
    type `d` on an included attribute → `BedDateTypeNotSupported` (D-038 parity
    deferral: failing is honest, a spec silently missing an included attribute
    changes the analysis; retires if the date carrier lands); an unknown type
    code → `BedTypeUnrecognized` (the D-070 typo-vs-deferred tiering).
  - **Parked config (D-049):** an excluded attribute parks its **full** config,
    including representable-but-invalid config, which the seam skips while
    parked, so flipping `include = true` is what surfaces validation (better
    than M1, which degraded it). Untranscribable parked config degrades to bare
    excluded (`name`, `source`, `include = false`, plus the live `restrict_to`)
    with `BedParkedConfigDropped` (Warning), replacing M1's silent broad
    `catch (Exception)`. Excluded attributes hence resolve parked-with-nulls in
    Core rather than M1's carried discretizer/scale: planner- and
    fingerprint-inert (both skip excluded attributes).
  - **restrict_to (D-057):** a non-empty `[Restrict To Values]` line migrates to
    authored `restrict_to` entries without per-token trim (v2 restrict is
    raw-value equality, OR within an attribute), include-independent
    (§10.1/D-076); a blank line stays unauthored. At M2 every token became a
    verbatim string `RestrictToValue`, numeric attributes included (a range
    cannot express v2's exact match: [x, x) is empty under
    lo-inclusive/hi-exclusive). D-091 made migration type-directed and D-105
    implemented it: on a type-`o` (numeric) attribute, a finite token that
    parses under the effective `binding.locale` becomes an exact
    `{ value = n }` entry; a token on any other v2 type, or a type-`o` token
    that is not a finite number, stays a verbatim string (under an invalid
    locale no token is parsed; resolution owns the locale error). The seam's
    `RestrictToNumericEntryRequired` owns a string entry on a numeric source;
    the migrator stays silent (one condition → one owning code, EP-14). At M2
    the planner rejected any carried `restrict_to` with
    `RestrictToNotImplementedV1`; D-105 made it execute.
  - **Missing token (D-068):** for the domain-list types (`c`, `b`) a
    `[Category Values]` entry equal to the **effective** token (null when
    `missing_token` is authored `""` (§5.1: detection disabled), else the
    authored value or the default `?`) selects `missing_policy = "as_attribute"`
    and leaves `declared_domain`/`value_labels`; a display label on the token
    has no v1 carrier (`{column}-missing` is canonical, D-074) →
    `BedMissingTokenLabelDropped` (Warning). Cut types (`o`, `n`) get no
    detection: their `[Category Values]` is a cut spec, not a domain.
  - **Authoring rules:** `ends` is always authored (the resolver defaults an
    absent `ends` to open, but a sentinel-less v2 cut spec means closed;
    omission would silently flip it); the progressive ordinal authors
    `direction = "le"` **only** (an authored `boundary`/`order` over cuts is a
    D-060 validation error; the resolver defaults reproduce v2's rendering);
    everything the resolver already defaults correctly stays unauthored
    (`value_type`, quote/locale/missing token, object key, the policies,
    `include = true`, `[defaults]`, `[output]`). Dichotomic display labels now
    carry as dormant `value_labels` (byte-/fingerprint-inert; dichotomic renders
    the column name alone) instead of dropping silently. `[spec]` holds
    `version = 1` only: no stored fingerprints (a migrated spec is unfrozen;
    D-057) and no `created_at` (no clock in pure code, EP-7); `derived_from`
    lands in `[provenance]` when the caller supplies it.
- **Why:** D-009 promised "load v2, save as TOML", but the M1 migrator could
  only produce a Core spec (unwritable, unfingerprintable, restrict-dropping),
  and D-049 deferred the hygiene ("no silently-dropped parked config") to this
  rework. Targeting the document model gets the writer, seam validation, and
  fingerprints for free and keeps every static check single-homed (D-067);
  the migrated mini-mushroom reproducing the pinned Slice E baselines is the
  proof the `.bed` path and the §19.1 TOML path are one spec.
- **Rejected:** keeping a Core-targeting migrator alongside the document path
  (two producers to hold semantically aligned forever); wrapping the M1
  exceptions in `Diagnosed` at the edges (keeps the broad catch and the silent
  drops); converting v2 numeric restrict tokens to ranges (changes semantics;
  see above); parking an included `d` attribute as excluded so migration
  "succeeds" (silently changes the analysis, the exact hazard D-068 names);
  hardcoding `?` in missing-token detection (already rejected by D-068).
- **Affects:** Spec (`BedReader`, `BedMigrator` replacing `BedToSpec`), Diagnostics
  (`BedStructureInvalid`, `BedDateTypeNotSupported`, `BedTypeUnrecognized`,
  `BedAttributeConfigInvalid`, `BedParkedConfigDropped`, `BedMissingTokenLabelDropped`),
  the golden harness (the migrate→resolve route); spec §16.4 (the `migrate (v2)`
  phase rows). M1 goldens byte-identical; Slice E fingerprint baselines unchanged.

### D-080: `AttributeNameDuplicate` / `ValueLabelKeyNotInDomain` re-homed to the resolve seam

- **Status:** accepted (the M2-exit standalone cleanup; supersedes D-067's
  "invoked from the seam, not re-homed" parenthetical for these two codes)
- **Date:** 2026-07-05
- **Decision:** the two remaining Core-emitted spec-validate checks,
  `AttributeNameDuplicate` and `ValueLabelKeyNotInDomain`, are **physically
  moved** into `SpecResolver` as private static checks over the **document
  model**, and deleted from `ConversionPlanner`. Both are §16.4 *spec validate*
  codes (their "Where" cell already read `spec validate`); the planner emitting
  them was a phase drift. The re-home matches the D-076 seam style: the dup-name
  check runs in the wide-shape attribute loop (after the triple early-return, so
  triple documents are unaffected, as the planner's triple guard already did),
  skips null/empty names (`AttributeNameMissing` owns those), and reports **one
  diagnostic per extra occurrence**, message verbatim. The `value_labels` check
  runs in the `include`-gated `ValidateAttributeConstraints` block, keyed on
  `section.Discretizer is IdentityDiscretizerSection` (≡
  `Discretizer.ConsultsValueLabels` in M2: `free_per_value` is the only other
  consulting kind and is read-rejected before the seam, D-070; it joins the check
  at M4), checking keys against `declared_domain ?? []`. `FormalAttributeCollision`
  / `FormalAttributeNameCollision` stay at **plan** as the output-integrity
  backstop (colliding *rendered/canonical* identities, not authored names).
- **Why:** D-067 assigned these to the seam but, to keep that slice surgical,
  left them "invoked from the seam, not re-homed": a parenthetical the code
  never realized (they stayed in the planner). Realizing the physical re-home
  over the **document model** is not cosmetic: `ResolveAttribute` returns `null`
  for an attribute whose source/discretizer/scale fails to resolve, so a
  Core-model check would *lose* a duplicate whose sibling field is broken; the
  document-model check catches it and aggregates with that sibling's own
  diagnostic (EP-14). It also makes §16.4's "Where" column honest without a spec
  edit.
- **Consequences:** (i) a Core-only caller hand-building a `BedrockSpec` and
  planning it directly no longer gets these two checks: the resolve seam is the
  enforced entry point for spec validation (every §16.4 spec-validate code is
  already seam-owned; the planner keeps only its plan-phase checks). (ii) These
  checks aggregate within the resolve pass rather than the plan pass, the
  cross-phase split every D-067 seam check already has. Neither is a public API
  change: no new public Core surface, no production `InternalsVisibleTo`, and the
  diagnostic codes/severities/messages are unchanged. **Byte- and
  fingerprint-neutral** on every golden and pinned baseline (validation-only; no
  planned column, name, or hash input moves).
- **Rejected:** a Core *helper* over the resolved model invoked from the seam
  (D-067's literal parenthetical): semantically weaker (drops duplicates whose
  sibling resolution fails) and still splits the check across packages; moving
  the §16.4 "Where" cell to `plan` instead of moving the code (records the drift
  as intended rather than fixing it, and orphans the checks from the seam that
  owns every other spec-validate code); leaving the drift (a standing
  code-vs-spec disagreement, EP-8).
- **Affects:** Spec (`SpecResolver`), Core (`ConversionPlanner`; `ValidateValueLabels`
  removed), tests (planner tests relocated to `SpecResolverTests`). Diagnostics
  unchanged. Realizes the D-067 seam assignment for these two codes; pairs with
  D-076. Spec §10.2 / §10.8 / §16.4 (no text change: the cells already read
  `spec validate`).

### D-081: Value-bin ordinal path (Slice H): identity + explicit order

- **Status:** accepted (M2 Slice H; realizes the D-047-deferred value-bin path
  and closes the F1 silent-output hole; refines D-060)
- **Date:** 2026-07-05
- **Decision:** the value-bin ordinal path (§12.3) is implemented, so an
  `ordinal` scale over a **value-bin** discretizer thresholds on the explicit
  `scale.order` instead of the (unread) cut geometry. Scope and semantics:
  - **Kind scope.** In M2 the only value-bin discretizer was `identity`
    (`free_per_value` was read-rejected by D-070), so this path was
    `identity` + a **string** `order` only (D-061 string-fixing), and `order`
    was **always required**. §12.3's "optional for numeric" branch belongs to
    the numeric value-bin (`free_per_value`) case, which D-101 activated at M4
    with D-096's natural numeric ascending default.
  - **Permutation rule.** `order` must be a **full permutation** of the
    `declared_domain`: every domain value gets a threshold. At **plan** an
    omitted `order` **or** a domain value missing from `order` is
    `OrdinalOrderMissing`; an `order` entry outside the domain is
    `OrdinalOrderHasUnknownValue` (one per stray entry). These two codes were
    already §16.4-registered at `plan`; Slice H adds their enum members and emit
    sites. In M2 the membership check was suppressed on an absent domain,
    which D-071's transitional reject owned (one condition → one code, EP-14);
    since D-098, Calibrate fills an omitted domain before Plan, so the check
    always has a domain. `order` lists **raw domain values, never display
    labels**.
  - **Order list shape** (distinct, non-empty entries) is validated at the
    **resolve seam** by broadening `OrderDomainInvalid` (its existing §16.4
    `spec validate` cell) from `ordered_cuts.order` to *any* authored order over
    a non-cut discretizer; include-gated (parked orders never block, D-049). A
    cut discretizer's order stays `OrdinalOrderNotAllowedWithCuts` (D-060); the
    two are mutually exclusive by discretizer kind, no double-report.
  - **Threshold semantics.** For order position *i* (value `order[i]`),
    `direction × boundary` pick the operator and crossings: `ge`+inclusive →
    `>=`, crosses `order[i..]`; `ge`+strict → `>`, crosses `order[i+1..]`;
    `le`+inclusive → `<=`, crosses `order[..i+1]`; `le`+strict → `<`, crosses
    `order[..i]`. Enumeration is by **ascending order position** in both
    directions. Value schemes have **no open end**, so there is no `all`
    threshold (unlike the cut path, D-047); *N* bins give *N* shapes (before
    `drop_top`).
  - **`drop_top`.** Suppresses the **inclusive tautological** threshold (`ge` →
    the first, `le` → the last). Under a **strict** boundary it is a **no-op**:
    there is no tautological threshold; the statically-empty end (`> highest` /
    `< lowest`) is **kept and simply never crosses** (the closed-ends cut
    precedent: an empty column is legal, §10.1). An empty column that never
    crosses is left to the **emit-phase** `AttributeHasNoCrosses` (D-058),
    whose emit site D-105 added.
  - **Naming / identity.** Names render through the existing `RenderName`
    (`{attr}-{op}{display}`, `value_labels` applied since `identity` consults
    them); the canonical identity's `BinKey` is the **raw** order value, so it
    is style-independent (the `BinKey == ValueLabel` check). The fingerprint
    **encoder is untouched**: `AppendScale` already encoded ordinal
    `boundary`/`direction`/`drop_top`/`order`, and each threshold column encodes
    as `{"bin":"<raw value>","op":"<op>","scale":"ordinal"}`. Two order
    permutations of one domain change the column identities/sequence, so the
    schema fingerprint moves; Slice H is byte- and fingerprint-neutral on every
    existing golden and pinned baseline (no cut spec takes this path).
- **Why:** the path was committed M2 scope in three places (roadmap M2 body, the
  Tier-2 net-scope line, spec §12.3 "implemented at M2") but never landed:
  `OrdinalScale.BuildShapes` read only the cut geometry, and an `identity` +
  `ordinal` spec resolved, planned, and emitted output that **silently ignored**
  the authored `order`/`boundary`, the exact wrong-output hazard D-057/D-070/
  D-071 close elsewhere, and a path that could mint a wrong schema fingerprint.
  A `BinScheme.CutBins` discriminator selects the path with one bit, keeping the
  cut geometry (and its `Boundary`-is-unread rule, D-060) exactly as it was.
- **Rejected:** *drop_top drops the statically-empty end under strict*: that
  end is not tautological, and dropping it would make `drop_top` mean two
  different things by boundary; keeping it matches the closed-ends cut
  precedent and an empty column is already legal (§10.1). *A new §16.4 code for
  "order omits a domain value"*: it is the same defect as an omitted `order` (a
  bin with no threshold), so a message variant of `OrdinalOrderMissing` keeps
  the registry closed. *Plan-phase duplicate/empty order validation*: that would
  split `OrderDomainInvalid`'s phase ownership across packages; the seam already
  owns the identical `ordered_cuts.order` shape check. *Building the numeric
  value-bin ordinal now*: that is `free_per_value`, an M4 discretizer (D-070);
  M2 is string-only.
- **Affects:** Core (`OrdinalScale.BuildValueThresholds`, `BinScheme.CutBins`,
  the `ConversionPlanner` order-permutation guard), Spec (`SpecResolver` broadened
  `OrderDomainInvalid`), Diagnostics (`OrdinalOrderMissing`,
  `OrdinalOrderHasUnknownValue` enum members added). Spec §12.3 (the `drop_top`
  value-bin and strict-no-op wording, the full-permutation clause). Byte- and
  fingerprint-neutral. Refines D-047/D-060; pairs with D-070 (kind scope) and
  D-071 (absent-domain precedence).

---

## M3 (triple source + wide column object keys)

These entries record the M3 triple-source audit, a review of the finalized triple and
wide-column-key surface like the Tier 1 and Tier 2 passes before M2, and the decisions the M3
implementation added.

### D-082: M3 triple source contract: reader, orderings, object identity, absent-vs-missing

- **Status:** accepted (M3 triple-source audit; realizes D-072)
- **Date:** 2026-07-07
- **Decision:** M3 implements the triple source under this audit-settled contract:
  - **Reader + orderings.** A triple `IRecordSource` streams subject/predicate/value
    rows. `ordering = "subject_grouped"` is the single-pass fast path: rows for a
    subject MUST be contiguous; a recurrence after an intervening subject is
    `TripleSubjectNotContiguous` (Error, stop; D-031). `ordering = "unordered"`
    groups non-contiguous (interleaved) subjects via external sort-merge/spool,
    never a full matrix (EP-16), and emits objects in **first-appearance order of
    each cleaned subject**: the same output order as `subject_grouped`, differing
    only in the contiguity requirement.
  - **Object identity = the resolved subject**, always (§5.4 default `column` =
    subject). An authored `[binding.object_key]` under `shape = "triple"` is
    rejected (`ObjectKeyModeInvalidForShape`, D-085): identity is not repointable.
    Every valid subject row establishes/keeps an object, **including rows whose
    predicate matches no attribute** (an object with no crosses is legal, §10.1).
    Contiguity and first-appearance order are judged over **every valid subject
    row**, ignored-predicate and no-cross rows included.
  - **Absent vs missing.** An **absent** predicate (no triple for that
    subject+predicate) is **no observation**: no cross, and never crosses
    `-missing`. `missing_policy` fires **only** when a matching-predicate row
    *exists* and its value is empty / `missing_token` / absent-because-short. An
    unknown or empty predicate keeps the subject and sets no crosses. Predicate
    matching is exact ordinal (EP-12).
  - **Cleanup scope.** The §5.1 quote-aware trim applies to the subject (→ object
    name) and predicate (→ selector), not only value-side matching; the cleaned
    subject is both the object name and the grouping/sort key.
  - **`has_header`** default is **shape-specific**: wide `true`, triple `false`
    (triple data is typically headerless, as the v2 goldens are; a `true` default
    would silently consume the first triple as a header). Once resolved, behaviour
    is identical, no heuristics. `columns` is **optional** (omit → `subject = 0,
    predicate = 1, value = 2`) and uses **one addressing mode**: omitted, all-index,
    or all-name; no mixing; all-name requires `has_header = true`.
  - **Structural-error severity.** Missing/invalid subject, a row too short for the
    subject role, non-distinct roles, and invalid `columns` shape are **Error**
    (halt this conversion: the §16.2 usable-next-call reading), matching
    `TripleSubjectNotContiguous` / `DuplicateObjectKey`; value-level problems
    aggregate (§16.4). Codes are D-085.
  - **Determinism / fingerprints.** Predicate arrival order never affects
    formal-attribute order or `.dat` item order (§17 rules 1–3, 8). Output
    fingerprints encode the **resolved** role→column-index map (so name-bound ≡
    index-bound roles hash identically), and `binding.encoding` becomes a real Core
    input (was the constant `"utf-8"`, D-077; UTF-8 specs keep their hash). The
    triple `ordering` field is **not** a fingerprint input: `subject_grouped` and
    `unordered` emit identical first-appearance bytes, so it is an acceptance/
    streaming property, not a byte one. The subject key is a resolved `column` key
    whose `duplicate_object_policy` is inapplicable (§6.1); the resolver pins it to
    the inert `fail` so `defaults.duplicate_object_policy` never reaches the triple
    key or perturbs its output fingerprint. Otherwise two specs with identical
    output bytes would hash differently (§14/D-077).
  - Conversion no longer rejects triple: `TripleSourceNotImplementedV1` retires.
- **Why:** D-072 carried the basic triple binding but rejected conversion; M3 is the
  triple milestone. The audit surfaced three silent-wrong-output hazards left
  implicit by the pre-M3 spec: triple breaks wide's "every object has a cell per
  column" invariant (absent ≠ missing), structural errors had no severity/home, and
  determinism needs the resolved-index fingerprint plus ordinal collation (D-084).
- **Rejected:** absent predicate = missing (would cross `-missing` for every
  unobserved predicate: schema-inflating and surprising); triple `has_header`
  default `true` (silently eats row 1 of headerless data); mixed index/name
  `columns` (ambiguous addressing, §5.3); per-row structural-error aggregation on the
  streaming path (cannot aggregate without buffering; abort is honest).
- **Affects:** Sources (new triple `IRecordSource`), Core (`Binding` triple +
  encoding carrier, `SpecResolver`, planner guard removal), Conversion
  (grouping/sort-merge + emit), Spec (writer already round-trips), Diagnostics
  (D-085); spec §5.1 / §5.3 / §5.3.1 / §5.4 / §10.2 / §10.5 / §14 / §17. Realizes
  D-072; pairs with D-083 / D-084 / D-085.
- **Slice F implementation, the bounded shared grouping backend (durable invariants):**
  the slow path (triple `unordered`, wide `dedupe`) runs on one internal backend:
  `FirstAppearanceGrouping` orchestrating a rank map (the **single** cleaned-key
  structure), spill runs, and a bounded-fan-in multi-stage `RunMerger`. Invariants:
  - **Byte-neutral spilling.** The row codec round-trips values exactly (strings as
    length + raw UTF-16 code units, lone surrogates preserved), so spilling never
    changes output bytes (EP-7); a zero-spill enumeration stays fully in memory.
  - **Lazy, owned, confidential workspace.** The spool directory is created only on
    the first required spill (zero-spill needs no disk, even under an unusable temp
    root); it is a uniquely-named, create-new per-enumeration directory, and only that
    recorded path is ever deleted. Because it holds raw source data it is created
    owner-restricted (Unix `700`; Windows an explicit owner-only, inheritance-disabled
    DACL; run handles `FileShare.None` + non-inheritable). If the restrictive ACL
    cannot be established, workspace creation **fails** rather than proceeding
    unprotected.
  - **Bounded resources: two accounting rules.** The intake buffer spills on a
    **deterministic modeled resident accounting** = the `List` buffer object + its backing
    array (`capacity × Unsafe.SizeOf<RankedRow<TRow>>()`: the list's *real* capacity × the
    exact element stride) + each row's **retained referenced objects** (its record + the
    record's two reference fields, its name, its field array + one reference per field, and each
    non-null string with header/length/terminator/8-byte alignment). This is a **two-tier**
    guarantee: the spill decision is always the modeled value, and on **.NET 10 CoreCLR x64**
    the padded layout constants make **actual retained live-object bytes ≤ modeled**, a
    numerical bound over the **stable retained graph at the post-`Add` checkpoint** (the `List`
    resize copy transient, GC commitment, fragmentation, and allocator bookkeeping are
    **excluded**). On other architectures/runtimes the model still bounds buffering
    operationally, but the byte guarantee is not asserted until that layout is validated. The
    layout constants are **correctness** constants (their code XML docs are authoritative):
    **M8 tunes the buffer budget and fan-in, never these**; changing one requires re-validating
    the object layout. Separately, the initial run's on-disk size is the **exact serialized
    accounting**: the sum of each row's serialized size plus **all run framing**. Intermediate
    runs may exceed the budget; peak temp disk is pinned to **`3T`** (`T` = the intake-final
    initial spill payload, captured once) by a pre-batch **degraded-cleanup escalation** that
    halts before any merge batch whose projected live bytes would exceed `3T`. Failed
    consumed-run deletions are **retried at each batch boundary** (each failed attempt
    aggregates) so a transient failure does not force the escalation.
  - **Validated framing (narrowed).** Run reads validate field counts and string
    lengths against the record buffer with checked/saturating `long` arithmetic, so a
    safely-identifiable truncated/corrupt record becomes an owned storage-failure
    outcome, never a malformed row or silent corruption. It does **not** claim
    OOM-immunity for payload-consistent huge lengths (documented residual).
  - **Two-channel storage failures, one ordered ledger.** Stable logical identity is
    `(Operation, Kind)`, both **application-defined** (mapped from runtime exceptions; identity
    never includes the random path or severity). Both channels share **one per-enumeration
    insertion-ordered ledger**: **in-path** failures are recorded as **Error** by the site that
    detects them, at their first-logical-occurrence position (before the cleanup their
    unwinding triggers), and also throw an internal `GroupingStorageException` from
    advancement only (never disposal) so the emitter halts (it catches only to stop, not to
    record); **cleanup-class** failures (consumed-run deletes, workspace teardown, and **reader
    close / a failed reader construction after a successful open**, which is `CleanupClose`)
    are recorded as **Warning** as they occur while enumeration continues, so
    disposal/open-cleanup never throws a storage exception and never masks the primary result,
    cancellation, or in-path failure. The emitter renders the ledger after the stream in
    **first-occurrence order**, one aggregate per identity at worst severity (count + ≤3
    first-occurrence samples); the replay session re-aggregates across passes, preserving each
    pass's positions.
  - **Replay session + `.cxt` invariant.** The public `EmitReplaySession`
    (`EmitReplay.Begin`) brackets one conversion attempt: data diagnostics collect
    once (first pass), storage failures are intercepted on every pass and the final
    aggregates append at **disposal** (in first-logical-occurrence order, the D-059
    rule), so a pass-2-only storage failure is never lost. The `.cxt` writer enforces
    the **object-name sequence invariant** (§18.1): a replay that diverges in name
    count/order fails the write.
  - **Plan carries `SourceExecution`.** The plan gains a `SourceExecution`
    (`WideExecution` / `TripleExecution(ordering)`), a pure value and **not** a
    fingerprint input; `EmitTripleAsync` owns ordering selection from it (the external
    `TripleRowSources` selector retired), and the emit entrypoints reject a mismatched
    variant.
  - **Runtime knobs.** `GroupingOptions` (budget, fan-in, temp root) is internal and never
    a spec/TOML/fingerprint input (the storage strategy never changes bytes). Since M7 the
    temp root is reachable through the public `ConversionRuntimeOptions` (`--temp-dir`,
    D-123 point 11); budget and fan-in stay internal.
  - **Rejected:** an external sort library (ExternalSort: Parquet baggage; SQLite:
    disproportionate); a record-and-rethrow failure model (EP-14: storage failures
    cross the seam as diagnostics, not exceptions); warnings-as-exceptions (a faulted
    async iterator cannot resume); deriving failure identity from the runtime exception
    type (it must neither split one condition nor merge unrelated ones).

### D-083: Wide `column` object-key execution + `duplicate_object_policy`

- **Status:** accepted (M3; realizes D-064 / D-034); its interim key-index phase retired by
  D-098, which moved the range check to spec-validate
- **Date:** 2026-07-07
- **Decision:** wide `object_key.mode = "column"` executes at M3 with
  `duplicate_object_policy` over the **cleaned** key value:
  - **`fail`** (default): a repeated key → `DuplicateObjectKey` (Error), stop.
  - **`keep`**: each row is its own object. Name assignment is a **conversion**
    concern (the object-key resolver, **not** the writer; EP-15): names are assigned
    in object emission order (§17 rule 4) and are **unique by construction**. The
    first occurrence of a cleaned key takes the key itself, a later occurrence takes
    `<key>#<record-index>` (0-based). If any candidate is already assigned (a literal
    data key or an earlier generated name), the resolver **escalates** by appending
    `#1`, `#2`, … (ascending integers from 1) and taking the first unused; all
    comparisons are ordinal (EP-12). The assigned-name set is bounded object-name
    metadata (EP-16); `.cxt` serializes these names and `.dat` ignores them, so the
    guarantee is observable only in `.cxt`. A repeated cleaned key aggregates to one
    `DuplicateObjectKey` (Warning); a candidate-name collision that forces the `#N`
    escalation aggregates to a separate `ObjectKeyNameDisambiguated` (Warning): one
    condition, one code (D-085). Both flush once after the object stream and are
    suppressed on a structural halt (an invalid key).
  - **`dedupe`**: rows sharing a key collapse to one object, later crosses union onto
    the first; `DuplicateObjectKey` (Info). Non-contiguous keys cannot stream in one
    pass without holding all crosses (EP-16), so `dedupe` is built on the **shared
    external grouping/sort-merge/spool path**, the same infrastructure as triple
    `unordered` (D-082).
  - **Output order.** Wide `column` object order = **order of first
    occurrence of each cleaned key** (§17 rule 4). This generalizes row order:
    `row_index` / `keep` / `fail` / all-unique reduce to row order; `dedupe` merges
    onto the first and adds no new position. Triple `unordered` (and
    `subject_grouped`) emit the same first-appearance order of each cleaned subject,
    so wide `column` and triple share **one** first-occurrence principle; the shared
    grouping infrastructure must not sort object output. The two triple orderings
    differ only in the contiguity requirement (`unordered` accepts interleaved input,
    `subject_grouped` requires contiguity), not in output order.
  - The key column is **not implicit** as an attribute but **may** be explicitly
    bound by an `[[attribute]]` source (§5.4 "excluded from conversion" → "not
    implicit"; D-033 source-repeat).
  - The source stays key-agnostic (row-index names); the emitter derives column-key
    names + policy from the plan.
  - **Key-index phase (interim; retired by D-098).** M3 checked the key index's upper bound
    at **plan** as `ObjectKeyBindingInvalid`, a binding error distinct from an absent per-row
    key cell (`ObjectKeyValueInvalid`), and reserved a schema-aware move to spec-validate;
    D-098 (2026-07-15) made that move with the same code.
- **Why:** D-064 deferred wide column-key execution to M3 alongside the triple
  subject-derived key: one column-object-key machine. The audit found `dedupe` is
  the sole policy incompatible with single-pass streaming; its first-occurrence
  output order matches triple `unordered`'s first-appearance order on the shared
  grouping infra (neither sorts object output). `keep` uniqueness was tightened from a
  "documented residual" to a guarantee once it was clear the **converter** can hold
  the assigned-name set as bounded metadata (EP-16), so uniqueness is affordable and
  stays upstream of the dumb writer (EP-15) rather than being decided in the `.cxt`
  writer.
- **Rejected:** silent `row_index` fallback (D-064's latent-wrong-output hole);
  duplicate output object names under `keep` as a documented residual (names are
  already held, so uniqueness is affordable); sorting `dedupe` output (contradicts
  §6.1 "union onto the first"); buffering all crosses for `dedupe` (violates EP-16).
- **Affects:** Core (`ColumnObjectKey` execution, planner guard narrowed to `dedupe`
  + key-index range-check), Sources (row-index-agnostic `WideCsvSource`, ragged
  tolerance), Conversion (`keep` name-uniqueness via `ColumnKeyNamer`; grouping/
  sort-merge/spool shared with D-082 for `dedupe`), Diagnostics (`DuplicateObjectKey`,
  `ObjectKeyNameDisambiguated`, D-085); spec §5.4 / §6.1 / §16.4 / §17 rule 4. Realizes
  D-064 / D-034; pairs with D-082.
- **Slice F implementation, Info aggregation is normative:** `dedupe` emits on the
  shared grouping backend (D-082). It reports **one aggregated** `DuplicateObjectKey`
  (Info): a count of the merged (duplicate) rows with a bounded **source-order** sample
  (the duplicate keys in the order they recurred, detected via the grouper's intake hook
  so no second seen-set is needed). It is **silent** when every key is unique, and it is
  flushed only on normal completion (suppressed on a structural halt). The object name is
  the cleaned key; the wide key column may double as an `[[attribute]]` source (D-033), and
  its crosses union with the other attributes' onto the one object.

### D-084: Ordinal string comparison is the project-wide rule

- **Status:** accepted (M3 audit; adds principle EP-12)
- **Date:** 2026-07-07
- **Decision:** all string identity, equality, matching, deduplication, grouping,
  source binding, and deterministic string **ordering** use ordinal comparison
  (`StringComparer.Ordinal`, a UTF-16 code-unit compare), never culture-aware
  collation; `binding.locale` (§5.1) governs numeric/date **parsing** only, never
  string collation. Recorded as new principle **EP-12** ("Strings compare and sort
  ordinally…"), the string-side companion to EP-11 (numeric/locale parsing).
- **Why:** the audit found "invariant culture" for the unordered subject
  grouping a determinism hazard: `InvariantCulture` string collation is ICU/NLS-version
  dependent and can mis-group or reorder across machines/runtimes, drifting output bytes
  on a golden/fingerprint path (EP-7); ordinal is byte-stable. (Slice F confirms this: the
  grouping backend keys strictly by `StringComparer.Ordinal` and emits in first-appearance
  order; there is no culture-sensitive sort.) The code already uses
  `StringComparer.Ordinal` for name/identity dedup, so this codifies practice across
  every string-keyed surface (predicate matching, key dedup, name uniqueness). A
  dedicated principle (not an EP-11 extension) has its own check moment (comparing or
  sorting strings), distinct from EP-11's numeric-parsing moment.
- **Rejected:** `InvariantCulture` collation (ICU-version-dependent: the hazard);
  extending EP-11 instead of a sibling principle (different check moment; leaves a
  correctness rule structurally implicit); "byte-value" wording
  (`Ordinal` compares UTF-16 code units, not bytes; precision matters).
- **Affects:** `engineering-principles.md` (new **EP-12**; old EP-12…EP-21 renumbered EP-13…EP-22,
  cross-references updated in `decisions.md` / `AGENTS.md`);
  Core / Conversion / Sources (ordinal collation on all string keys). Byte-neutral:
  `unordered` emits first-appearance order (no object string sort), so ordinal collation
  governs matching/dedup/grouping only, not object order.

### D-085: M3 diagnostic taxonomy: structural triple/column-key codes

- **Status:** accepted (M3; refines D-067); its interim key-index phase retired by D-098,
  which moved the range check to spec-validate; the `ObjectKeyValueInvalid` phase widened to
  calibrate by D-099 and to probe by D-111
- **Date:** 2026-07-07
- **Decision:** M3 gives triple/column-key **structural** conditions real §16.4
  codes rather than misusing value-level codes or `DuplicateObjectKey`:
  - **New codes.** `ObjectKeyValueInvalid` (Error, emit): a data-derived object name
    (triple subject or wide column key) is empty, whitespace-only, contains
    newline/control characters, or its mapped column is absent from the row (an
    unusable object name; a newline would corrupt the line-structured `.cxt`,
    §18.1). `TripleColumnsNotDistinct` (Error, spec validate): the **resolved** case
    where two logical roles (subject/predicate/value) point to the same physical
    column. `ObjectKeyNameDisambiguated` (Warning, aggregated, emit): under wide
    `keep`, an object's assigned name needed `#N` escalation because its candidate
    collided with an already-assigned name (a literal key vs a generated name);
    distinct from `DuplicateObjectKey` (repeated cleaned keys): one condition, one code.
    `GroupingStorageFailed` (Error in-path/escalated, or Warning cleanup-only; emit): a
    spool-storage failure on the shared grouping backend (triple `unordered` / wide
    `dedupe`), carrying the two-channel model, stable `(Operation, Kind)` identity,
    bounded aggregation, and severity promotion of D-082 (Slice F).
  - **Extend existing (no new code).** `SourceBindingInvalid` (Error, spec validate)
    also owns invalid triple `columns` **shape/addressing** (missing/partial
    role table, mixed index/name addressing, all-name without `has_header = true`,
    header-name no-match, duplicate matching header) **plus** the source
    `kind`↔`shape` mismatch (`column`↔wide, `predicate`↔triple). This parallels its
    existing wide meaning (name binding needs `has_header`; a reference must resolve
    to **exactly one** column, so both a no-matching-header and a duplicate matching
    header are invalid for wide sources and column object keys too, §10.2);
    its message/Context disambiguates the attribute `source` from the `columns`
    table. `ObjectKeyModeInvalidForShape` also owns any authored
    `[binding.object_key]` under triple.
  - **Severity.** Structural row/source errors are **Error** (halt this conversion,
    file usable next call; §16.2), matching `TripleSubjectNotContiguous` /
    `DuplicateObjectKey`; never `Fatal` (reserved for a corrupt/unrecoverable spec).
  - **Interim phase (wide column-key index; retired by D-098).** M3 checked the wide `column`
    key index's upper bound at **plan** as `ObjectKeyBindingInvalid` (D-083), a binding error
    distinct from an absent per-row key cell (`ObjectKeyValueInvalid`); D-098 (2026-07-15)
    moved the check to spec-validate with the same code.
  - **Enum timing.** The §16.4 **table** is the documentation home; each
    `DiagnosticCode` **enum member** lands with its **emit site** at the implementing
    slice ("grows per slice", EP-3): the new codes above
    (`ObjectKeyValueInvalid`, `TripleColumnsNotDistinct`, `ObjectKeyNameDisambiguated`)
    and the already-spec'd-but-unimplemented `DuplicateObjectKey`,
    `TripleSubjectNotContiguous`, `AttributeHasNoCrosses`, `ObjectHasNoCrosses`,
    `NoObjectsEmitted`, `NoFormalAttributes`.
  - **Retirements.** `TripleSourceNotImplementedV1` (D-082) was removed with its guard
    when the triple source landed (Slice C). `ObjectKeyColumnNotImplementedV1` (D-083)
    narrowed to wide `dedupe` when `fail`/`keep` landed (Slice E) and **retired fully
    when `dedupe` landed (Slice F)**: the enum member and its planner guard are gone, so
    wide `column` object keys now execute for every `duplicate_object_policy`.
- **Why:** the M3 audit found that the triple/column-key structural-error
  class had no diagnostic home, and that reusing value-level (`UnknownValueObserved`)
  or duplicate-key codes would break one-condition → one-owning-code (EP-14).
  Assigning owners/severities now, and consolidating shape/addressing under the
  existing `SourceBindingInvalid` rather than proliferating codes, keeps §16.4's
  phase-ownership honest (D-067).
- **Rejected:** reusing `UnknownValueObserved` / `DuplicateObjectKey` for structural
  conditions (mis-phased, mis-severity, EP-14); a dedicated code per shape/addressing
  variant (`SourceBindingInvalid` already means "binding shape invalid":
  proliferation for no gain); `Fatal` for row-level structural errors (the file is
  usable next call); adding enum members before their emit sites existed (would strand
  members no emit site raises, EP-3).
- **Affects:** Diagnostics (`ObjectKeyValueInvalid`, `TripleColumnsNotDistinct`, and
  the six deferred codes, at their emit sites), Core / Conversion / Sources (emit
  sites), Spec (`SpecResolver` shape checks); spec §16.4. Refines D-067; pairs with
  D-082 / D-083.

---

### D-086: Shape-aware `.bed` migration sources

- **Status:** accepted (M3, Slice G; refines D-079)
- **Date:** 2026-07-11
- **Decision:** the one-way v2 migrator (`BedMigrator`) authors an attribute
  `source` per the binding **shape**: a wide (or shape-absent) binding keeps the
  positional `ColumnSourceSection(Index: i)` byte-for-byte, while a **triple**
  binding authors `PredicateSourceSection(Name: <v2 attribute name>)`: the
  attribute's own name is the predicate it binds (spec §19.3). The shape is
  threaded through the private `MigrateAttribute → MapConfig → Map* → Bare`
  chain from `binding.Shape`; `Migrate`'s public signature is unchanged and no
  diagnostic is added or renamed. This closes the only gap that kept the
  sanctioned migrate→resolve route from expressing the three v2 triple goldens:
  a migrated triple spec now resolves to a Core `PredicateSource` instead of
  dead-ending at `SourceBindingInvalid` (a column source under a triple binding,
  §10.2). Value types stay unauthored (defaulted per discretizer at resolve,
  D-061), exactly as on the wide path.
- **Why:** D-079 moved the migrator onto the document model but `Bare`
  hard-coded a positional column source; triple-golden activation (Slice G)
  needs the migrator to speak both shapes. Deriving the predicate name from the
  v2 attribute name matches v2's own 3-column loader (lineage.md) and spec §19.3
  (`name = "age"` → `source = { kind = "predicate", name = "age" }`).
- **Rejected:** a second migrator overload or a public shape parameter (the
  binding already carries the shape; thread it privately); authoring the
  predicate `value_type` (would create a place for it to disagree with the
  discretizer default: the same reasoning as the wide positional source, D-061).
- **Affects:** Spec (`BedMigrator` private chain + type XML doc). No public
  signature, diagnostic, fingerprint, or output-byte change. Refines D-079;
  enables the Slice G triple goldens.

---

### D-087: Symmetrical final-newline controls and v2 triple `.dat` compatibility

- **Status:** accepted (M3, Slice G)
- **Date:** 2026-07-11
- **Decision:** three settled parts.
  - **Native control.** `[output.dat] trailing_newline` (default `true`) is the
    symmetrical twin of `[output.cxt] trailing_newline`: an additive
    `{ get; init; }` property on `DatOutputSection` / `DatFingerprintInputs`
    (positional constructors and deconstruction unchanged, so every existing
    caller and record deconstruction keeps compiling; a `[output.dat]` document
    that previously rejected the key now accepts it). `DatWriter` already honored
    `WriterOptions.TrailingNewline`; the knob simply reaches it from the spec.
  - **Backward-compatible fingerprint encoding.** `trailing_newline` is a
    `dat_output_fingerprint` input (§14), encoded into the canonical `.dat` JSON
    (alphabetically last of the dat keys) **only when disabled** (`false`).
    Omitting the key at the historical default (`true`) keeps every pre-D-087
    stored `.dat` hash byte-identical; a `false` value produces a distinct hash.
    This deliberately diverges from the `.cxt` twin, which has always emitted
    `trailing_newline` unconditionally; the divergence is exactly what preserves
    the existing dat pins.
  - **Shape-derived v2-compat override.** v2's `.dat` final newline is
    shape-dependent: v2's wide converter wrote a final line terminator, its
    triple converter did **not** (the three `mini-*_triples.dat` goldens end
    without CRLF). Under `--v2-compat` the conversion orchestrator therefore
    suppresses the final `.dat` newline for a **triple** source and keeps it for
    a **wide** source; the `V2Compat` preset stays the common baseline (it leaves
    `TrailingNewline` at its default) and the exception is applied per resolved
    shape, not baked into the preset. Native output ignores shape and honors the
    authored/default `trailing_newline`. Full v2-compat matrix: wide `.cxt`/`.dat`
    and triple `.cxt` → final CRLF; triple `.dat` → no final CRLF.
- **Why:** activating the triple `.dat` goldens (Slice G) surfaced that they end
  without a trailing newline while every wide `.dat` and every `.cxt` ends with
  one. Rather than a per-fixture knob or an M7 deferral, the byte fact is v2's
  shape-dependent behavior; modelling it as a symmetrical native control plus a
  shape-derived compat override keeps the vNext default clean and the v2-ism
  behind the one flag (§8), with no lost fidelity.
- **Rejected:** a per-fixture newline boolean in the golden harness (the rule is
  shape-derived, not fixture-specific); baking the triple exception into the
  `V2Compat` preset (it is a shape decision the orchestrator owns, not a writer
  byte-convention); emitting `trailing_newline` unconditionally in the dat
  fingerprint like the cxt twin (would re-pin every stored `.dat` hash for no
  gain); deferring the `.dat` final-newline handling to M7 (a byte-equality fact
  needed now, not CLI work).
- **Affects:** Spec (`DatOutputSection`, `ReadOutputDat`, `SpecWriter`,
  `MergeDat`, `SpecFingerprints`), Core (`DatFingerprintInputs`,
  `FingerprintCalculator.BuildDatOutputJson`), Export (`WriterOptions` doc only),
  the golden orchestrator (`DatOptionsFor`); spec §8 / §14 / §18.2 / §21. One new
  fingerprint input with backward-compatible encoding; no new diagnostic; wide
  output bytes unchanged.

---

## Tier 1 M4 spec audit (pre-M4)

### D-088: Shared auto-calibration invariants + equal-frequency contract

- **Status:** accepted (pre-M4 Tier 1 audit; restates D-028); its deferred quantile formula
  settled by D-103; the freeze generalized to all four calibration outcomes by D-122
- **Date:** 2026-07-12
- **Decision:** four settled invariants ahead of the M4 calibration work.
  - **Auto/frozen byte-equivalence (both auto discretizers).** For `equal_width`
    and `equal_frequency`, converting on the fly and converting from the
    `calibrate`-frozen spec MUST produce **byte-identical** `.cxt`/`.dat` on the
    calibration dataset; only **audit metadata** (the run manifest, recorded
    command line, spec-file hashes, and calibration diagnostics) is exempt. This
    restates D-028 at the output-byte level: freezing changes *when*
    cuts resolve, never *which* cuts.
  - **General calibration population.** Each record contributes its
    non-missing, usable value; a numeric value contributes only when it parses to
    a finite number under `binding.locale`; for triple input each distinct cleaned
    `(subject, predicate, value)` observation contributes once (§5.3.1); wide rows
    are independent observations.
  - **`tie_policy` is calibration-time.** It assigns an **entire
    tied-value group** to one side of a candidate boundary and never splits a
    group; Emit does no tie handling of its own: it applies ordinary §11.2
    half-open `[lo, hi)` geometry to the resolved cuts.
  - **Formula-stage distinct-gap obligation + phase-split cut validity.** When
    distinct ≥ `bins`, quantile/tie placement selects `bins - 1`
    distinct, strictly-ascending cut gaps and never drops a bin because several
    target boundaries fall in one tied group; the exact quantile formula stays
    deferred (D-103 settled it), bounded by this obligation and the auto/frozen
    equivalence. Spec-determined cuts validate at spec-validate; **data-calibrated**
    cuts that are non-finite or not strictly ascending are `CalibrationCutsInvalid`
    (Error, calibrate).
- **Why:** the tie/emit boundary, the calibration population, and the
  frozen-equivalence guarantee were implied but never normative; the M4
  implementation needs them pinned before cuts are computed, and golden pins can
  only be set once the byte-equivalence is a stated contract.
- **Rejected:** emit-time tie handling (splits tied groups, contradicts half-open
  geometry); dropping bins on tied-boundary collisions (silently fewer bins);
  freezing the exact quantile-index formula now (a tuning choice, not a
  determinism guarantee, §11.5).
- **Affects:** spec §5.3.1 / §7 / §11.4 / §11.5; new diagnostic
  `CalibrationCutsInvalid`. Restates D-028.

---

### D-089: Equal-width range-mode contract

- **Status:** accepted (pre-M4 Tier 1 audit)
- **Date:** 2026-07-12
- **Decision:** `equal_width`'s `range` mode decides its phase and fingerprint
  eligibility.
  - **`range = "manual"` is spec-determined.** `vmin`/`vmax` fix the
    span, the `bins - 1` cuts come from the spec alone, no data calibration runs,
    it skips Calibrate (§7), and it is **eligible for stored fingerprints** like
    any fully-declared spec (§14). Missing `vmin`/`vmax` → `SpecFieldInvalid`
    (parse); a non-finite or non-increasing authored range → `EqualWidthRangeInvalid`
    (Error, spec validate); a `precision`/`round_to` that collapses the derived
    cuts → `EqualWidthCutsCollapsed` (Error, spec validate).
  - **Data-derived range** (`min_max` / `percentile_p1_p99`). No usable spread →
    `CalibrationDataInsufficient` (calibrate); final cuts non-finite or not
    strictly ascending, including rounding collapse → `CalibrationCutsInvalid`
    (calibrate, shared with D-088).
  - **Distinct-value guard scoping.** The ≥`bins`-distinct guard is
    **`equal_frequency`-only**: `equal_width` never applies it, because
    equal-width bins are placed by span, not by count.
- **Why:** the manual-vs-data-range split governs which phase runs and whether a
  spec can carry stored fingerprints; the distinct-value guard was mistakenly
  assumed to cover both auto discretizers.
- **Rejected:** applying the distinct-value guard to `equal_width` (span-based,
  tolerates sparse data); treating a manual range as data-dependent (it is fully
  spec-determined and fully-frozen-eligible).
- **Affects:** spec §3 / §7 / §11.4 / §14; new diagnostics `EqualWidthRangeInvalid`,
  `EqualWidthCutsCollapsed`.

---

### D-090: value_groups execution contract

- **Status:** accepted (pre-M4 Tier 1 audit)
- **Date:** 2026-07-12
- **Decision:** the `value_groups` grouping discretizer, pinned for M4.
  - **Regex semantics.** `pattern` is a .NET regex matched
    culture-invariant, case-sensitive, and partial (unanchored `IsMatch`); authors
    anchor (`^…$`) for full-string matching, and inline options such as `(?i)` are
    honored as part of the pattern.
  - **Validity, one condition → one code.** `values` and `pattern`
    are independently optional and may be combined. `SpecFieldInvalid` (parse)
    owns a missing/empty label, an empty or invalid `pattern`, an empty explicit
    value, or a group with neither matcher; there is no dedicated regex-error code.
  - **Unique labels.** Authored labels must be distinct, and an
    authored label colliding with the synthetic `Other` (`unmatched = "other"`) is
    a duplicate → `ValueGroupsLabelDuplicate` (Error, spec validate); duplicates
    never surface as `SpecFieldInvalid`. A pass-through value merely *observed* to
    equal an authored label is data-dependent → plan-phase
    `FormalAttributeCollision`; repeated pass-through observations are idempotent.
  - **Ordinal over groups.** Ordinal `value_groups` (with `unmatched`
    `skip`/`other`) requires an explicit `scale.order` that is a full permutation
    of the group labels, including `Other` when applicable; `ordinal` +
    `unmatched = "passthrough"` is `OrdinalNotAllowedWithValueGroupsPassthrough`
    (Error, spec validate). §12.3's order-consuming family and §17 rule 2 extend to
    `value_groups` skip/other, and `OrdinalOrderMissing` /
    `OrdinalOrderHasUnknownValue` now cover group-label permutations.
  - **`include` behaves as warn** under `unmatched = "skip"`: an unmatched value is
    not a domain gap (`value_groups` ignores `declared_domain`, D-055), so
    `unknown_value_policy = "include"` yields no bin, `UnknownValueObserved`
    (Warning), and no `UnknownValuePolicyInclude` (no schema extension).
- **Why:** `value_groups` is the M4 grouping discretizer; its regex behavior,
  validity codes, and ordinal/passthrough/include interactions were under-specified
  and each would otherwise read as an implementation choice rather than a contract.
- **Rejected:** a dedicated regex-error diagnostic (`SpecFieldInvalid` already owns
  malformed fields, EP-14); ordinal over `passthrough` (its data-discovered bins
  cannot be a full authored permutation); routing duplicate labels through
  `SpecFieldInvalid` (duplicates own `ValueGroupsLabelDuplicate`).
- **Affects:** spec §11.6 / §12.3 / §16.4 / §17; new diagnostics
  `ValueGroupsLabelDuplicate`, `OrdinalNotAllowedWithValueGroupsPassthrough`.

---

### D-091: restrict_to execution contract

- **Status:** accepted (pre-M4 Tier 1 audit; refines D-063/D-076/D-079).
  Merged-`dedupe` restriction and type-directed migration **clarified in place** by
  the pre-M4 Tier 2 audit (2026-07-13). The restriction-object encoding gained its
  **`unknown_value_policy` key** in place at M4 Slice F (2026-07-17, the G-9
  governance item; see D-105). Both amendments were made in place because the D-091
  contract was still unimplemented. **Realized by D-105.**
- **Date:** 2026-07-12
- **Decision:** the M4 execution semantics, numeric surface, migration mapping, and
  fingerprint encoding for `restrict_to`.
  - **Existential matching.** An object passes an attribute's
    `restrict_to` when at least one observed value matches at least one entry (OR
    within an attribute); an absent triple predicate and a missing value match
    nothing; failing any attribute's restriction excludes the object (AND across
    attributes, §10.1). Under wide `dedupe` all rows for a key are grouped **first**
    and the restriction is evaluated existentially over the **merged** object: if
    any of the merged observations matches, the complete object survives with **all**
    its observations and crosses (single-row behaviour is `keep`/`fail` or upstream
    conflict resolution, not `dedupe`). Wide `row_index`/`fail`/`keep`, where an
    object carries one observation per source, is the one-observation special case.
  - **Exact numeric entries.** `{ value = n }` matches by
    parsed numeric identity (`30`, `30.0`, `3e1`), with finite `n`, and coexists
    with ranges; `{}` is the full usable-numeric range. Bounded ranges require
    finite `from < to`; equal, reversed, or non-finite provided bounds, and a
    non-finite exact value, are `RestrictToRangeInvalid` (Error, spec validate). A
    bare string on a numeric source is `RestrictToNumericEntryRequired`, which
    **renames** `RestrictToOnNumericRequiresRange` (the enum rename landed with the M4
    check site, D-105).
  - **Type-directed migration** (refines D-079). Migration is directed by the v2
    attribute **type**, not by whether a token looks numeric: only a parseable
    **finite** restrict token on a type-`o` (numeric) attribute migrates to an exact
    `{ value = n }` entry (parsed under the effective `binding.locale`, then written
    as the numeric exact entry), replacing D-079's keep-as-string carriage now that
    an exact numeric form exists. A numeric-**looking** token on a categorical
    attribute stays a **verbatim string**; and an **unparseable** token on a type-`o`
    attribute also stays a string, then fails later at the resolve seam with
    `RestrictToNumericEntryRequired`. Migration never parses a categorical token or
    silently drops an unparseable numeric one.
  - **Canonical `restrictions` encoding.** `restrict_to` is encoded
    as a `restrictions` array in the **shared** portion of the canonical structure
    (§14). It enters **both** output fingerprints, is excluded from
    `schema_fingerprint`, is present only when non-empty and, as an explicit
    exception to the planned-order rule, is **canonically sorted**. Each restriction
    object is `{"entries":[…],"source":{…},"unknown_value_policy":<string>}` (keys
    sorted `entries` < `source` < `unknown_value_policy`), reusing the D-077 source
    encoding (`{"predicate":<name>,"value_type":<type>}` or
    `{"column":<index>,"value_type":<type>}`); entries are `{"value":<string>}`,
    `{"value":<number>}`, or `{"from":<number|null>,"to":<number|null>}` (both range
    keys always present). Entries sort by complete canonical JSON (ordinal) with
    canonically-identical entries deduplicated (`30`/`30.0` collapse) and
    overlapping-but-non-identical ranges **not** merged; restriction objects (AND)
    sort likewise with exact duplicates removed; filter-only attributes contribute
    their object here, not through the included-attribute column encoding.
    - *In-place amendment (M4 Slice F, G-9).* The `unknown_value_policy` key was
      **added** to the restriction object after this entry first landed. Reason: D-097
      makes the policy **live, byte/abort-affecting** configuration on a filter-only
      attribute (a `fail` spec aborts a run its otherwise-identical `warn` twin
      completes), and a filter-only attribute never enters `shared.attributes`, so
      the original two-key object would have hashed two behaviourally different specs
      identically. It is encoded on **every** restriction object for one uniform
      shape; for an included-and-restricted attribute the value therefore also appears
      in `shared.attributes`, which is deliberate encoding redundancy (a value
      repeated), **not** the D-035 double-*counting* (nothing is summed).
- **Why:** `restrict_to` was carried and shape-validated (D-057/D-063/D-076/D-079)
  but its execution, numeric surface, and fingerprint encoding were open; M4 needs
  all three pinned before rows are filtered, and reusing the D-077 source encoding
  mints no new fingerprint vocabulary.
- **Rejected:** universal (non-existential) matching (contradicts the FCA
  object-filter intent, §7); a bespoke source vocabulary for restrictions (reuse
  D-077); keeping `restrict_to` in planned order in the fingerprint (order is
  semantically immaterial, so canonical sort); merging overlapping ranges (loses
  authored intent and is unneeded for determinism); keeping the v2 numeric-token
  keep-as-string migration (an exact numeric entry now expresses it faithfully).
- **Affects:** spec §10.2 / §10.4 / §14 / §16.4 / §19.4; renames
  `RestrictToOnNumericRequiresRange` → `RestrictToNumericEntryRequired` and adds
  `RestrictToRangeInvalid`; refines D-063/D-076/D-079.

---

### D-092: Numeric free_per_value rendered labels

- **Status:** accepted (pre-M4 Tier 1 audit)
- **Date:** 2026-07-12
- **Decision:** a numeric `free_per_value` bin's **rendered label** (and any
  `{value}` in `formal_attribute_format`) is the parsed numeric value formatted
  with the §14 invariant, shortest round-trippable rule, so `90`, `90.0`, and `9e1`
  share one bin rendered `90`. This carries the bin-identity collapse
  already specified (§11.3) through to the rendered name, reusing the fingerprint
  number formatter (§14).
- **Why:** §11.3 pinned bin *identity* but not the rendered *label*; without this,
  the same numeric bin could render `90.0` on one path and `90` on another,
  breaking `.cxt` determinism and the `cxt_output_fingerprint`.
- **Rejected:** rendering the first observed spelling (data-order-dependent); a
  separate label formatter (reuse the one canonical §14 number formatting).
- **Affects:** spec §10.7 / §11.3. No new diagnostic.

---

## Tier 2 M4 implementation-contract audit (pre-M4)

A second pre-M4 review, after the Tier 1 spec audit (D-088…D-092), of the M4
**implementation contract**: the calibrated-state boundary between Calibrate and Plan, the
exact canonical fingerprint encodings for the M4 discretizers, the bounded-memory
calibration obligation, numeric `free_per_value` identity, and the filter-only restriction
diagnostics.

### D-093: Calibrated-state model: Core-owned resolved calibration outcomes; Plan consumes calibrated state

- **Status:** accepted (pre-M4 Tier 2 audit; refines D-036/D-088); realized by D-098, which
  names the types this entry left open
- **Date:** 2026-07-13
- **Decision:** the boundary between the Calibrate and Plan phases (§7) is a
  single, resolved **calibrated-state** value, contracted as follows:
  - **Core-owned, immutable, resolved.** The resolved calibration outcome is an
    immutable value owned by `Core` that carries everything a data-reading pass
    discovers: resolved auto cuts, observed domains, `unknown_value_policy =
    "include"` additions, and `value_groups` `unmatched = "passthrough"` bins. It is
    **produced by the Conversion calibrator** and returned through `Diagnosed<…>`
    (EP-14, alongside `ObservedDomainUsed` / `CalibrationDataInsufficient` / …), and
    it is what **Plan consumes**. The freeze path (`calibrate`) and later manifest
    serialization read this **same** retained outcome; none re-derives M4 semantics
    from raw data (the retention boundary, D-028/D-036; §15).
  - **One Plan input contract.** A fully declared / spec-determined configuration
    satisfies the **same** calibrated-state contract **without** a data-reading
    calibration pass: it is already calibration-ready and enters the identical Plan
    input. There is one Plan input shape, not an "auto" and a "declared" one.
  - **Plan rejects unresolved state.** Plan must not accept calibration-dependent
    state that has not been resolved. This is a **call-contract / package-boundary**
    obligation, enforced structurally (the D-078 posture: a mis-sequenced internal
    call is a programmer error), **not** a permanent runtime diagnostic; no code is
    minted for the bypass.
  - **Structural auto/frozen equivalence.** Resolved auto cuts reuse the manual-cut
    geometry, label, and identity machinery (`CutBinLabels` / `NumericCutBin`);
    sharing that machinery is what makes the D-088 auto/frozen byte-equivalence
    **structural** rather than a property two code paths must independently maintain.
  - **API shape left open.** Exact type names and member layouts are **not** fixed
    here; this entry pins the contract and its retention boundary, not the surface.
- **Why:** M4 introduces the first calibration that reads data. Without a single
  owned, retained outcome, planning, freezing, and manifest serialization could each
  re-derive cuts/domains: three chances to diverge and break determinism (EP-7).
  Making the resolved state Core-owned and immutable, and routing declared specs
  through the same path, collapses those to one contract and makes the D-088
  byte-equivalence fall out of shared machinery.
- **Rejected:** letting Plan re-run calibration or read raw data (re-derivation;
  breaks the retention boundary); a separate Plan input for declared vs auto specs
  (two contracts to keep in sync); a permanent runtime diagnostic for feeding Plan
  unresolved state (a mis-sequenced internal call is programmer error, D-078);
  fixing the concrete types now (premature; layout was left to implementation).
- **Affects:** Core (owns the calibrated-state value), Conversion (calibrator
  produces it), Spec/manifest (consume the retained outcome); spec §7 / §15. Refines
  D-036/D-088; realizes D-028's freeze face at the contract level.

---

### D-094: M4 canonical fingerprint encodings; effective-configuration hashing rule

- **Status:** accepted (pre-M4 Tier 2 audit; appendix to D-069/D-077)
- **Date:** 2026-07-13
- **Decision:** pin the canonical JSON encoding of every M4 discretizer in the
  `shared.attributes[].discretizer` sub-object (the D-077 shared per-attribute
  encoding), and the rule for what an M4 fingerprint hashes. All the D-069/D-077
  conventions carry over unchanged: UTF-8 no BOM, compact JSON, **object keys
  sorted ordinal ascending**, `kind` as a key, TOML enum spellings, and the §14
  invariant shortest round-trippable number formatter (so `1.0` encodes `1`,
  `90.0`/`9e1` encode `90`). The per-kind shapes:
  - **`free_per_value`**: no config beyond the kind (its numeric-vs-string identity
    rides on `source.value_type`, already in `source`; its bins/domain are effective,
    below):

    ```json
    {"kind":"free_per_value"}
    ```
  - **`equal_width`**: authored `bins`, `range`, `precision`, and, **only** when
    `range = "manual"`, `vmin`/`vmax`. `precision` mirrors its two TOML forms: the
    string `"exact"` or the object `{"round_to":<number>}`. Data-derived range, then
    manual range:

    ```json
    {"bins":4,"kind":"equal_width","precision":"exact","range":"min_max"}
    {"bins":4,"kind":"equal_width","precision":{"round_to":1},"range":"manual","vmax":100,"vmin":0}
    ```
  - **`equal_frequency`**: authored `bins`, `tie_policy`, `cut_placement`
    (resolved defaults spelled: `"left"`, `"right_value"`):

    ```json
    {"bins":4,"cut_placement":"right_value","kind":"equal_frequency","tie_policy":"left"}
    ```
  - **`value_groups`**: `groups` in **declaration order** (order is significant:
    first-match wins, §11.6, so it is a planned-order array, **not** sorted),
    `unmatched` (spelled `"skip"`/`"other"`/`"passthrough"`), and each group object
    with keys sorted `label`/`pattern`/`values`, where `values` and `pattern` are
    present **only when authored** and the inner `values` array preserves **authored
    order with duplicates retained** (it is authored configuration, not a
    canonicalized set: the §14 arrays-in-planned-order default; only `restrictions`
    sort):

    ```json
    {"groups":[{"label":"School","values":["11th","HS-grad"]},{"label":"ICD-Cardiac","pattern":"^I[0-9]{2}"}],"kind":"value_groups","unmatched":"skip"}
    ```
  - **Effective vs authored (the hashing rule).** Fingerprints hash the
    **effective** planned bins/cuts/columns/order (the resolved `bin` objects in the
    `schema` array, D-069) and the **effective** (calibrated/extended) domains (the
    `Discretizer.ConsumesDeclaredDomain` domain, D-077). A data-calibrated
    discretizer's **resolved cuts are not re-encoded** in its `discretizer`
    sub-object: they already appear as `bin` objects in the `schema` array, so
    duplicating them would be redundant. What the `discretizer` sub-object carries is
    the **authored** kind and its authored configuration (the shapes above). Because
    that sub-object feeds the **output** fingerprints (`shared`) but **not**
    `schema_fingerprint` (which hashes the `schema` columns only), an **auto**
    (`equal_frequency`) spec and its **frozen** form (`manual_cuts` with explicit
    cuts) share the same `schema_fingerprint` and emit **byte-identical** contexts
    (D-088), yet may carry **different** `cxt`/`dat` output fingerprints. That is a
    one-directional guarantee (same output fingerprint ⇒ same bytes; not the
    converse, §14), so this is sound.
  - **Golden-lock before first use.** As D-069 was locked by the Slice-E
    canonical-stability golden before any stored hash shipped, the M4 per-kind
    canonical **bytes and SHA-256 vectors MUST be golden-locked before the first M4
    fingerprint is produced**. The first stored/compared M4 hash fossilizes these
    bytes.
- **Why:** the M4 discretizers had no pinned canonical encoding (they reject at
  read pre-M4, D-070), so the first M4 fingerprint would fossilize whatever the
  encoder happened to emit (EP-11, the D-069 rationale). Pinning the shapes, the
  omission rules, and the effective/authored split now makes the M4 encoder
  mechanical and its goldens a genuine lock, and states plainly why auto and frozen
  output fingerprints may differ despite identical bytes.
- **Rejected:** re-encoding resolved auto cuts in the `discretizer` sub-object
  (redundant with the `schema` bins; invites a two-source-of-truth drift); sorting
  the `groups` array (declaration order is semantically significant, §11.6);
  always-present `vmin`/`vmax` (they are not authored under a data-derived range);
  a bespoke number format for cuts (reuse the one §14 formatter, D-069); deferring
  the shapes to the M4 encoder unreviewed (the first stored hash fossilizes them).
- **Affects:** Core (M4 fingerprint encoder: the `AppendDiscretizer` cases for the
  four kinds), Spec; spec §14. Appends D-069/D-077.

---

### D-095: Bounded-memory calibration; subject-local triple deduplication; `GroupingStorageFailed` calibrate ownership

- **Status:** accepted (pre-M4 Tier 2 audit; refines D-088/D-082); realized by D-103
- **Date:** 2026-07-13
- **Decision:**
  - **Bounded-memory calibration is an M4 obligation.** Equal-frequency and
    percentile-range (`percentile_p1_p99`) calibration MUST be **exact,
    deterministic, and bounded-memory in M4**, not a later performance retrofit.
    When the calibration population exceeds the working-memory budget, the
    implementation spills/sorts/aggregates (or uses an equivalent exact method); the
    spill and non-spill paths MUST produce **byte-identical** cuts and output.
    **Approximate quantiles are prohibited.** M8 may tune budgets and benchmark
    algorithms (the D-082 split), but it never *establishes* boundedness; that
    exists at M4.
  - **Subject-local triple deduplication.** The §5.3.1 "each distinct cleaned
    `(subject, predicate, value)` contributes once" rule is realized with a
    **subject-local** deduplication of `(predicate, value)` within the current
    subject, **never** a dataset-wide seen set. `subject_grouped` deduplicates the
    contiguous run; `unordered` may group/spool first, then apply the **same**
    subject-local rule. Discovery order remains **raw input order** (§17 rule 3):
    the deduplication changes how repeats are collapsed, not the first-appearance
    order.
  - **Budgets are internal.** Memory budgets, fan-in, spill thresholds, and similar
    controls are **implementation internals**, never TOML surface and never
    fingerprint inputs (the D-082 `GroupingOptions` precedent: a knob that cannot
    change output bytes is not a spec field). Tuning them is byte-neutral by
    construction.
  - **`GroupingStorageFailed` extends to calibrate.** The existing
    `GroupingStorageFailed` code now owns storage failures in the **calibrate** phase
    as well as emit, with unchanged identity/aggregation/severity: an **in-path**
    failure is Error with **no** calibrated result; a **cleanup-only** failure is
    Warning. No new diagnostic code.
- **Why:** M4 calibrates over the v1 target population (7.3M–73M records, D-007);
  a calibration that must hold every value in memory would break at target scale, and
  a calibration that goes approximate under pressure would break determinism (EP-7)
  and the D-088 auto/frozen byte-equivalence. Fixing "exact + bounded" as an M4
  property, with spill/non-spill byte-identity, closes both. Subject-local
  deduplication keeps the triple path bounded without a dataset-wide set (which would
  defeat the point). Keeping budgets internal preserves the D-082 rule that tuning cannot move
  bytes.
- **Rejected:** deferring boundedness to M8 (a data structure that only becomes
  bounded after a perf pass is a scale bug shipped early); approximate quantiles
  under memory pressure (breaks determinism and D-088); a dataset-wide deduplication
  set for triples (unbounded: the very thing to avoid); exposing budgets as TOML/fingerprint
  inputs (a byte-neutral knob has no place in either, D-082); a new storage-failure
  code for calibrate (the aggregation/severity semantics are identical to emit's).
- **Affects:** Conversion (calibrator: bounded quantile/percentile pass, spill
  path), Sources (subject-local triple deduplication), Diagnostics (`GroupingStorageFailed`
  phase widens to calibrate/emit; registry cell only); spec §7 / §11.4 / §11.5 /
  §16.4; roadmap M4/M8. Refines D-088/D-082.

---

### D-096: Numeric `free_per_value` identity: locale-parsed, zero-canonicalized; normalized domain/label/order keys

- **Status:** accepted (pre-M4 Tier 2 audit; refines D-061/D-081/D-092)
- **Date:** 2026-07-13
- **Decision:**
  - **Identity.** A numeric `free_per_value` bin's identity is **finite parsed
    numeric equality** under `binding.locale`: `90`, `90.0`, and `9e1` are one bin
    (extending D-092's rendered-label rule to the spec-side keys that name the same
    bin). **All zero spellings** (`0`, `0.0`, `-0`, `+0`, `0e0`) canonicalize to
    **positive zero** and render `0`. (The Tier 1 claim that .NET equality
    distinguishes signed zero was incorrect; the canonicalization is nonetheless
    pinned so `-0` never leaks into a key, label, or hash.)
  - **`declared_domain`.** A numeric `free_per_value` `declared_domain` entry parses
    under `binding.locale`, a **stated exception** to the "spec strings are
    verbatim" rule (§5.1), on par with numeric `restrict_to` entries. An **invalid**
    (unparseable), **non-finite** (NaN/±∞), or **normalization-duplicate** (two
    spellings, one numeric identity) authored entry is the new **`DeclaredDomainInvalid`**
    (Error, spec validate).
  - **`value_labels` / `scale.order`.** Both use the **same** normalized numeric
    identity. A `value_labels` key that is invalid or out-of-domain stays
    `ValueLabelKeyNotInDomain`; two keys collapsing to one numeric identity is the
    new **`ValueLabelKeyDuplicate`** (Error, spec validate). An invalid or
    normalization-duplicate numeric `scale.order` entry reuses `OrderDomainInvalid`;
    a valid-but-out-of-domain `order` entry stays `OrdinalOrderHasUnknownValue`; an
    **absent** numeric `order` uses **natural numeric ascending** order (§12.3).
  - **Authored spellings survive; Core keys are canonical.** The document model
    round-trips the authored spellings verbatim (round-trip fidelity, D-081); the
    resolved keys the Core planner and fingerprint see are the canonical numeric
    identities.
- **Why:** D-092 pinned how a numeric `free_per_value` bin *renders*; it did not pin
  how spec-side **keys** (`declared_domain`, `value_labels`, `scale.order`) that
  refer to those bins are identified. Without normalized identity, `90` and `90.0`
  in a domain would be two bins on one path and one on another, and a `value_labels`
  entry keyed `90.0` would silently miss a `90` bin, breaking determinism and the
  fingerprint. Two new spec-validate codes give an author a precise error instead of
  a silent miss.
- **Rejected:** string-identical domain/label/order keys for numeric
  `free_per_value` (contradicts the bin-identity collapse, §11.3/D-092); preserving
  signed zero (`-0` leaking into a key/label/hash is a determinism hazard for no
  gain); overloading `ValueLabelKeyNotInDomain` for the duplicate case (a distinct
  condition deserves its own code, EP-14); a new code for duplicate numeric `order`
  (reuse `OrderDomainInvalid`, which already owns duplicate/invalid domain entries).
- **Affects:** Core (numeric `free_per_value` identity, signed-zero canonicalization),
  Spec (document model round-trip; the two new validate checks), Diagnostics (new
  `DeclaredDomainInvalid`, `ValueLabelKeyDuplicate`); spec §5.1 / §10.3 / §10.8 /
  §11.3 / §12.3 / §16.4 / §17. Refines D-061/D-081/D-092.

---

### D-097: Filter-only restriction diagnostics: unparseable values report under `unknown_value_policy`

- **Status:** accepted (pre-M4 Tier 2 audit; refines D-049/D-076)
- **Date:** 2026-07-13
- **Decision:** a narrow refinement of D-049/D-076 for what a **filter-only**
  attribute (`include = false` + `restrict_to`) reports while evaluating its
  restriction (it interacts with D-091's execution and D-050's present-but-invalid
  rule):
  - On a **filter-only numeric** restriction, a **valid non-match** and a **missing**
    value are **silent** (a non-match is the restriction working, not an anomaly).
    An **unparseable / non-finite** input is a **non-match** (it can match no numeric
    entry) **plus** an aggregated `SourceValueUnparseable` at the severity
    `unknown_value_policy` selects: `skip` silent, `warn` Warning, `fail`
    Error/abort, `include` Warning (an unparseable token cannot join a numeric domain,
    so `include` behaves as `warn`, §10.6).
  - An **included-and-restricted** attribute (not filter-only) keeps its **ordinary**
    malformed/unknown-value diagnostics even when the restriction excludes the object:
    restrictions **filter objects, not observations**. Each **raw observation is
    diagnosed at most once**: the restriction pass and the discretization pass do not
    each report the same unparseable cell twice.
- **Why:** D-050/§10.6 pinned unparseable-value reporting for *discretized*
  attributes; a **filter-only** attribute is discarded before discretization, so
  without this its unparseable inputs would report **nothing**, a silent data-quality
  hole exactly where a filter is meant to be trustworthy. Routing the filter-only
  restriction path through the same `unknown_value_policy` severity closes the hole
  without inventing a code, and the "filter objects, not observations" rule keeps an
  included attribute's diagnostics intact and single-counted.
- **Rejected:** silence on filter-only unparseable inputs (the data-quality hole);
  a new diagnostic code for the restriction path (reuse `SourceValueUnparseable` at
  the policy severity); suppressing an included-and-restricted attribute's ordinary
  diagnostics when its object is filtered out (restrictions filter objects, not
  observations); double-counting an observation across the restriction and
  discretization passes.
- **Affects:** Conversion (restriction evaluation reports `SourceValueUnparseable`),
  Diagnostics (no new code: existing `SourceValueUnparseable`); spec §10.4 / §10.6.
  Refines D-049/D-076; interacts with D-091.

---

## M4 Slice A (calibration preparation)

M4 Slices A to F (D-098 to D-105) implement the contracts of the two pre-M4 audits
(D-088 to D-097). The labels G-1 to G-13 (with G-8b) number the governance items of the M4
implementation plan; each slice entry's Status names the items it settles, and source comments
cite them.

### D-098: M4 preparation contract: two-stage source bootstrap, token-paired provenance, calibrated state

- **Status:** accepted (M4 Slice A; realizes D-093; the D-083-reserved schema-aware-resolve move;
  the G-1 governance item)
- **Date:** 2026-07-15
- **Decision:** M4's Calibrate phase needs one preparation identity threaded from resolve
  through emit, and a source cannot exist before resolution (a source constructor needs the
  resolved `Binding`, and name-bound resolution needs the schema). Preparation is therefore a
  **two-stage bootstrap** over an opaque, validated, recursively-immutable token chain:
  1. **Read settings first** (schema-independent): `SpecResolver.ResolveReadSettings(document)
     → Diagnosed<SourceReadSettings>` resolves only the §5.1 scalars (shape, encoding,
     delimiter, quote, has_header, missing_token, triple ordering) via helpers shared with full
     resolve (no condition gains a second owner). Prefix gates match full resolve: an authored
     `extends` throws `ArgumentException` (uncomposed, D-078); a missing/unsupported version is
     `SpecVersionUnsupported` (Fatal) with no settings, so the bootstrap never opens a source for
     a document whose semantics are unknown. `SourceReadSettings.Create` is the EP-10 backstop
     with an exact exception contract (empty `missingToken` valid; UTF-8 spellings normalize;
     `delimiter == quoteChar`/non-`"` quote/inconsistent shape-ordering throw).
  2. **Session** (reads schema, holds no resolved indexes): `WideCsvSession` /
     `TripleCsvSession(openStream, settings)` expose `GetSchemaAsync` with a pinned lifecycle:
     the first successful read caches an immutable snapshot; a canceled/failed read (factory
     exceptions included) caches nothing and the next call retries; `Bind` before a successful
     read throws; repeated `Bind` is allowed and re-validates.
  3. **Full resolve against the schema**: `SpecResolver.Resolve(document, schema?) →
     Diagnosed<ResolvedDocument>` pairs a Spec-owned `ResolvedDocument` (internal constructor, an
     immutable document snapshot) over the Core-owned **`ResolvedSpec`**, a sealed non-positional
     class produced only by the validating factory `ResolvedSpec.Create`. `Create` deep-snapshots
     the spec graph into recursively-immutable storage (`ImmutableArray`/`FrozenDictionary`/
     `FrozenSet`, no castable mutable backing array on any public property), and is the
     **validate-once trust boundary** (EP-10): it exhaustively re-checks every structural invariant
     downstream phases trust (schema structure, settings↔binding consistency, locale, source-kind
     ↔shape, binding-union coherence (the subject-pinned triple key, distinct in-range roles,
     null triple fields under wide), included discretizer/scale presence, defined enum members,
     known restriction variants, and each site-typed `ResolvedNameBinding` against both the header
     and its resolved member), throwing `ArgumentException` on any violation. So the planner's
     residual range/coherence checks become unreachable-by-construction, and all binding range
     checks return to their §16.4 spec-validate home (the D-083 "interim at plan" parentheticals
     retire). Strict factories run only behind the resolver's success gate: on any Error/Fatal the
     result is `Diagnosed.Failed` with no factory called, so aggregation never throws.
  4. **Bind**: `session.Bind(resolvedDocument.Resolved)` validates settings + schema value
     equality and returns the concrete source **carrying the token**. Source provenance is a
     mechanically-closed three-state Core union on both source interfaces
     (`SourceProvenance Provenance { get; }`): `TokenProvenance` for bound sources;
     `DescriptorProvenance(settings, roles)` for direct-constructed production sources (derived
     from the `Binding` they hold); and the explicit `SourceProvenance.Unvalidated` opt-out.
     Weaker validation is *named*, never silent.
  5. **Calibrate/Emit pair by provenance before reading any row**: token → `ReferenceEquals` with
     the resolution (full pairing); descriptor → settings value-equality + role-map equality +
     ordinal schema-value equality; unvalidated → schema-value only. Mismatch (or an unknown
     variant) throws `InvalidOperationException` (the D-082 call-contract posture).

  The **Calibrate phase** (`Calibrator.CalibrateAsync`/`CalibrateTripleAsync`, Conversion)
  produces the Core-owned, immutable **`CalibratedSpec`**: the single Plan input carrying the
  effective spec, the schema snapshot, and the retained outcomes (D-093). `CalibratedSpec.Create`
  returns `Diagnosed` (EP-14) and enforces per-mode **completeness**: an absent-domain consuming
  attribute requires exactly one `ObservedDomain` outcome; an explicit-domain consuming attribute
  under `unknown_value_policy = "include"` requires exactly one `IncludeAdditions` marker (an
  empty list is the zero-additions marker); a leftover/duplicate/kind-mismatched/unexpected outcome
  or a null schema is a programmer error (`ArgumentException`). `ConversionPlanner.Plan` re-signs to
  `Plan(CalibratedSpec, LabelStyle)`; `ConversionPlan` becomes a sealed class with a planner-owned
  internal constructor carrying its `CalibratedSpec` and `LabelStyle`; the output-fingerprint API
  reads `plan.Calibrated.Spec` and validates the plan↔spec/style pairing;
  `SpecFingerprints.ComputeNative(ResolvedDocument, ConversionPlan)` validates
  `ReferenceEquals(resolved.Resolved, plan.Calibrated.Resolution)` and reads output settings from
  the document snapshot. Construction authority: public validating Core factories +
  reference-token pairing, so a forger can only build a parallel honest chain, not a mixed one;
  **no production `InternalsVisibleTo`**.
- **Why:** without one owned, retained, validated preparation, resolve/calibrate/plan/emit/
  fingerprint could each re-derive or mispair state, breaking determinism (EP-7) and the D-093
  retention boundary. The two-stage bootstrap is the only compilable ordering (sources cannot
  precede resolution); the opaque token makes pairing structural rather than a header heuristic.
- **Rejected:** a one-stage bootstrap (sources cannot be built before resolution); header-only
  pairing (same-header/different-settings mispairing); a mutable calibrated state (castable arrays
  defeat immutability); production IVT to forge tokens (a mixed pipeline); re-deriving calibration
  downstream of the calibrator (D-093).
- **Affects:** Core (`SourceReadSettings`, `ResolvedNameBinding`, `SourceProvenance`,
  `ResolvedSpec`, `CalibratedSpec`, `ConversionPlan`/`ConversionPlanner`), Sources (sessions and
  provenance), Spec (`ResolvedDocument`, `Resolve`/`ResolveReadSettings`), Conversion
  (`Calibrator`, emit pairing guard), Diagnostics (`ObservedDomainUsed`,
  `UnknownValuePolicyInclude`, `NoFormalAttributes`; removes
  `ObservedDomainCalibrationNotImplementedV1`); spec §7 / §16.4. Realizes D-093; the D-083 move.

### D-099: Triple structural validity widens to calibrate/emit

- **Status:** accepted (M4 Slice A; refines D-082/D-085/D-095; the G-3 governance item); the
  probe phase added to both codes by D-111
- **Date:** 2026-07-15
- **Decision:** a triple **calibration** read enforces the same structural validity as emit: a
  `subject_grouped` read halts on non-contiguity (`TripleSubjectNotContiguous`), and **any**
  triple calibration read halts on a structurally unusable subject (`ObjectKeyValueInvalid`),
  with the same codes, severities, and identities as emit: an in-path Error yielding no
  calibrated result (the D-095 `GroupingStorageFailed` precedent). Otherwise a
  calibrate-only/freeze run would retain outcomes from input the conversion rejects. Deliberate
  asymmetry, recorded: **wide** calibration does not read or validate the object-key column. The
  §7 wide population is row-scoped and key validity does not shape it, whereas the triple
  subject *is* the observation's identity (it scopes the §5.3.1 dedup), so its validity decides
  calibration correctness.
- **Why:** the pre-M4 spec phased `TripleSubjectNotContiguous`/`ObjectKeyValueInvalid` at emit
  only; a calibration pass that read the same rows without the same guards could silently retain
  cuts/domains from structurally-invalid input.
- **Rejected:** validating triple structure only at emit (a calibrate-only run would retain
  invalid outcomes); validating the wide object-key column during calibration (the wide population
  is key-independent: no correctness gain).
- **Affects:** Conversion (`Calibrator` triple pass), Diagnostics (registry cells only:
  `TripleSubjectNotContiguous`/`ObjectKeyValueInvalid` phase widens to `calibrate/emit`); spec
  §16.4. Refines D-082/D-085/D-095.

### D-100: Per-phase `SourceValueUnparseable` aggregation across calibrate and emit

- **Status:** accepted (M4 Slice A; refines D-097; the G-4 governance item)
- **Date:** 2026-07-15
- **Decision:** Calibrate and Emit are independent data-reading passes; each reports its **own**
  aggregated `SourceValueUnparseable` per attribute (calibrate reports only for the attributes it
  reads). Under `fail` the calibrate-phase Error aborts before emit, so no double report; under
  `warn`/`include` a value unparseable in both passes yields one aggregate per phase. D-097's
  at-most-once rule is per-pass (restriction vs discretization within emit), unchanged.
- **Why:** a value unparseable during both calibration and emit reports honestly once per pass
  rather than being deduplicated across phases, which would hide a calibrate-phase data-quality
  signal.
- **Rejected:** a single cross-phase at-most-once rule (would suppress a legitimate calibrate-phase
  aggregate); a new diagnostic code for the calibrate phase (the aggregation/severity semantics
  are identical to emit's; reuse `SourceValueUnparseable`, §16.4 already phases it
  `calibrate/emit`).
- **Affects:** Conversion (calibrator aggregation), Diagnostics (no new code); spec §16.4 (no text
  change: the code is already phased `calibrate/emit`). Refines D-097.

---

## M4 Slice B (free_per_value + numeric identity)

### D-101: `free_per_value` executable: string + numeric identity, `CanonicalNumber`, scoped zero canonicalization, seam-normalized numeric keys, natural-order default

- **Status:** accepted (M4 Slice B; realizes D-061/D-092/D-096; the G-6/G-8 governance items)
- **Date:** 2026-07-16
- **Decision:** the `free_per_value` discretizer (§11.3) becomes executable and leaves the
  transitional read-reject set (G-8). It is **type-flexible** (D-061): with
  `value_type = "string"` (the default) each raw spelling is its own bin, verbatim; with
  `value_type = "number"` the bin identity is the **parsed numeric value**, parsed with
  `binding.locale` (never ambient; EP-11), **finite-only**, and rendered by the one canonical §14
  number rule, so `90`/`90.0`/`9e1` collapse to one bin labelled `90` and every zero spelling
  (`-0` included) collapses to `0` (D-092/D-096). A present-but-unparseable/non-finite numeric value
  is `BinResult.Unparseable` (§11.5). `identity` + `value_type = "number"` stays invalid: the numeric
  distinct binner is `free_per_value` (D-061); there is no second numeric distinct-value spelling.
  - **`CanonicalNumber` (public, `Core.Fingerprinting`) + scoped zero canonicalization (G-6).** The
    one §14 number-identity rule is made public: `Format(double)` reproduces the existing invariant
    shortest encoder **byte-for-byte** (`-0.0` still formats `"-0"`, so the `fp_format = 1` encoder
    and every stored hash are untouched), `CanonicalizeZero(double)` maps both signed zeros to positive
    zero, and `TryParse(text, culture, out value)` parses `NumberStyles.Float`, finite-only, without
    canonicalizing (the caller applies `CanonicalizeZero`). `CanonicalJson.AppendNumber` is left
    unchanged. Every **new** M4 numeric identity is canonicalized via the type-correct chains
    (text-sourced `TryParse → CanonicalizeZero → Format`; already-numeric `CanonicalizeZero → Format`);
    Slice B uses the text-sourced chain for numeric `free_per_value` domain/label/order keys at the
    seam and observed/emitted values. Authored manual-cut bytes keep their standing `-0` exception
    (a golden pins it).
  - **Seam-normalized numeric keys; authored spellings survive in the document (D-096).** A numeric
    `free_per_value` `declared_domain`, `value_labels` key, and `scale.order` entry is the §5.1
    exception to verbatim strings: the resolve seam parses each to its canonical numeric identity,
    and the resolved Core graph carries canonical keys while the **document model round-trips the
    authored spellings verbatim** (`90.0` stays `90.0` in TOML). New spec-validate diagnostics:
    `DeclaredDomainInvalid` (a domain entry unparseable, non-finite, or a normalization duplicate) and
    `ValueLabelKeyDuplicate` (two label keys collapsing to one numeric identity); an out-of-domain
    label key stays `ValueLabelKeyNotInDomain`, a normalization-duplicate/invalid `order` entry reuses
    `OrderDomainInvalid`. String `free_per_value` consults `value_labels` verbatim, exactly like
    `identity`. The seam checks aggregate independently (EP-14).
  - **Calibration (extends Slice A's discovery-class path).** Absent-domain numeric `free_per_value`
    calibrates its **observed domain** from canonical numeric identities (first-observation order after
    collapse; `ObservedDomainUsed`), and `unknown_value_policy = "include"` appends novel canonical
    identities (`UnknownValuePolicyInclude`); the mode-triggered warnings fire at zero discoveries. A
    present-but-unparseable numeric value read during calibration is excluded from the population and
    reported as this phase's own aggregated `SourceValueUnparseable` at the severity
    `unknown_value_policy` selects (D-100/G-4); under `fail` the calibrate Error aborts with no result.
  - **Numeric value-bin ordinal natural-order default (§12.3/D-096).** A numeric `free_per_value`
    ordinal with **no authored `scale.order`** derives natural numeric **ascending** order from its
    (canonical) domain at plan, the one value-bin case exempt from `OrdinalOrderMissing`; every other
    value-bin ordinal (string `free_per_value`, or a numeric one with an authored order) still requires
    an explicit full-permutation order. All four `direction × boundary` combinations are well-defined
    over value bins.
  - **Fingerprint (D-094 golden-lock).** The discretizer encodes as `{"kind":"free_per_value"}`: its
    numeric-vs-string identity rides on `source.value_type` and its bins are the effective (canonical)
    domain. Hand-authored canonical bytes and a pinned SHA-256 vector lock it, as D-094 requires. All
    pre-Slice-B canonical bytes, SHA vectors, and the nine golden fixtures are unchanged.
- **Why:** M4's first executable discretizer beyond manual cuts needs its numeric identity, seam
  normalization, calibration, ordinal, and fingerprint contracts pinned so `90`/`90.0` are one bin on
  every path (determinism, EP-7) and a signed zero never leaks into a key, label, or hash, without
  moving the `fp_format = 1` bytes (G-6).
- **Rejected:** making `identity` also numeric-flexible (a second numeric distinct-value spelling,
  EP-5/D-061); canonicalizing zero inside `Format` or changing `CanonicalJson.AppendNumber` (would move
  the standing `-0` bytes, G-6); normalizing the document model's authored spellings (breaks round-trip
  fidelity, D-081); requiring an explicit order for numeric value bins (natural numeric order is
  unambiguous, D-096).
- **Architecture-test correction.** Adding `free_per_value` made
  `FreePerValueDiscretizer` (`Core.Discretization`) reference `CanonicalNumber` (`Core.Fingerprinting`)
  and `SourceValueType` (`Core.Spec`), both **inside `FcaBedrock.Core`**: no package cycle, but the
  `Packages_ShouldBeFreeOfCycles` arch test sliced by full namespace via `Slices().Matching("FcaBedrock.(*)")`,
  accidentally treating `Core`'s sub-namespaces as separate packages. The documented invariant
  (AGENTS.md, D-039) is **package/assembly** acyclicity, so the test is corrected to assign each type to
  its production **assembly** (an explicit `SliceAssignment`), with a non-vacuity assertion that the
  slice names equal the loaded production package names. A correction of a latent architecture-test
  semantic bug, not a weakening of the invariant (no type moved, no numeric logic duplicated).
- **Affects:** Core (`FreePerValueDiscretizer`, `CanonicalNumber`, `FingerprintCalculator`,
  `ResolvedSpec`, `ConversionPlanner`), Spec (`FreePerValueDiscretizerSection`, reader/writer,
  `SpecResolver`), Conversion (`Calibrator`), Diagnostics (`DeclaredDomainInvalid`,
  `ValueLabelKeyDuplicate`), `FcaBedrock.Architecture.Tests`; spec §5.1 / §7 / §10.3 / §10.7 /
  §10.8 / §11.3 / §12.3 / §16.4 / §17. Realizes D-061/D-092/D-096.

---

## M4 Slice C (equal_width + the shared cut engine)

### D-102: `equal_width` executable: shared `NumericCutBins` engine, sign-aware cut formula, `CutPrecision`, pending→executable substitution, streaming min/max calibration

- **Status:** accepted (M4 Slice C; realizes D-088/D-089/D-093/D-094; the G-5/G-7/G-8 governance items)
- **Date:** 2026-07-16
- **Decision:** the `equal_width` discretizer (§11.4) becomes executable in both Slice C range
  modes and leaves the transitional read-reject set (G-8). It is **number-fixing** (D-061):
  an absent `value_type` resolves to `number` and an authored `value_type = "string"` is
  `SourceValueTypeInvalid`. Its bins are always **open-ended** (§11.4), so data outside the
  calibration span still falls in the first/last bin.
  - **The shared `NumericCutBins` engine (realizes D-093).** The numeric-cut execution machinery
    (parsing, finite-only classification, cut membership, bin/cut labels, structural interval
    bins, and native/v2-compat rendering) is extracted into one internal Core engine that
    `ManualCutsDiscretizer` and `EqualWidthDiscretizer` both compose (EP-17). This is what makes
    the **D-088 auto/frozen byte-equivalence structural** rather than a property two code paths
    must independently maintain: the same effective cuts through one engine give the same bins,
    labels, canonical identities, and crosses. The extraction is **byte- and behaviour-neutral**
    for `manual_cuts`: the nine golden fixtures, every pinned canonical-byte baseline, every SHA
    vector, and the authored `-0.0` manual-cut encoding are all unchanged (EP-1's stated exception:
    touching M1 code is justified as D-093-mandated shared machinery, with the goldens as proof).
  - **Range mode decides the phase (D-089).** `range = "manual"` is spec-determined: the seam
    calls the strict factory, the cuts come from `vmin`/`vmax` alone, no calibration runs, and the
    spec stays **fully-frozen-eligible** (§14). A data-derived range resolves to the
    `CalibrationPending` carrier (D-093) that Calibrate replaces; there is no parallel
    unresolved-carrier shape. `range = "percentile_p1_p99"` is modelled in the Core enum; it
    stayed an unrecognized range (`SpecFieldInvalid`, the D-070 tier-3 posture) until its
    calibration landed (D-103, G-8b), and it was never silently mapped to `min_max`.
  - **The cut formula, pinned (G-5/G-7).** For `i = 1 .. bins-1` with `t = (double)i / bins`, the
    interpolation is **sign-aware**: a same-sign span (or one with a zero bound) uses
    `vmin + (vmax - vmin) * t`; a span crossing zero uses the convex combination
    `vmin * (1 - t) + vmax * t`, whose terms are each bounded by their own operand. So **every
    finite increasing range derives finite cuts** (`[-1.7e308, 1.7e308]` included) where the
    naive always-`vmax - vmin` form would overflow it, and no range is rejected merely for being
    wide (G-7). `precision` then rounds (`MidpointRounding.ToEven`, pinned), and every computed cut
    is **positive-zero canonicalized** (G-6) so a computed `-0` never reaches a bin identity,
    label, or hash. Derived-cut validity is the backstop on the *result* (a `round_to` collapsing
    two cuts or, at the extreme margin of the double range, a span too narrow to hold `bins - 1`
    distinct representable cuts), never a second range gate, so its message names the outcome
    rather than assuming rounding caused it. The formula lives in **one place** and behind **one
    boundary**: the internal `DeriveCuts` is reached only through `CreateManual`, and the
    Conversion calibrator obtains its data-range cuts by invoking that same public factory over the
    **observed** span (keeping only its cuts; `CalibratedSpec.Create` then builds the real
    discretizer, preserving the authored data-derived range and its absent `vmin`/`vmax`). So auto
    and frozen cuts are the same numbers by construction with **no second copy of the formula and
    no public surface beyond the approved inventory** (EP-4).
  - **Failure ownership.** Authored: a non-finite or non-increasing manual range is
    `EqualWidthRangeInvalid`, and cuts that are not finite and strictly ascending after
    `precision` are `EqualWidthCutsCollapsed` (both Error, spec validate). Data-derived: a
    population with no usable spread (no usable finite value, or `min == max`) is
    `CalibrationDataInsufficient`, and invalid calibrated cuts are `CalibrationCutsInvalid` (both
    Error, calibrate, in-path with no calibrated result). There is deliberately **no
    distinct-value guard**: equal-width bins are placed by span, not count, so fewer distinct
    values than `bins` is valid whenever `min < max` (D-089).
  - **The calibrated-state boundary is total, so the transitional line holds at every seam.**
    Two conditions are **calibrator-contract violations** rather than data errors, and therefore
    throw (the D-093 programmer-error posture, EP-10): a `CalibratedCuts` outcome whose length is
    not `bins - 1` (it would build a discretizer whose `Bins` contradicts its own geometry, and
    the fingerprint would encode `"bins":4` beside a schema array of another width), and a pending
    `equal_width` whose range is not `min_max`. The second held the transitional line: until
    percentile had its calibration, hand-supplied cuts must not make it plannable, emittable, or
    fingerprintable ahead of its slice (the reader rejected the spelling, and this closed the
    programmatic route, G-8). D-103 narrowed it to ranges other than `min_max` and
    `percentile_p1_p99`. `CalibrationCutsInvalid` stays for correctly-sized cuts the *data* could
    not make ascending.
  - **Calibration (extends the existing `Calibrator`; no second engine).** `min_max` reads the
    §7 population (each non-missing value that parses **finite** under `binding.locale`), tracks a
    **streaming minimum and maximum**, and retains two doubles: never the population, no sort, no
    spill, no distinct tracking. Because min/max is **order- and count-insensitive**, the triple
    path needs **no subject-local deduplication** and no grouped second pass: a repeated
    `(subject, predicate, value)` cannot move a min or a max (this is why D-095's count-sensitive
    machinery belongs to `equal_frequency`'s slice, not here). Unparseable values are excluded and
    reported as this phase's own aggregated `SourceValueUnparseable` at the `unknown_value_policy`
    severity (D-100). Cuts are derived only after the pass completes; `CalibratedSpec.Create`
    substitutes the executable discretizer and **retains** the `CalibratedCuts` outcome in
    spec-attribute order (the D-093 retention boundary: the freeze path and the §15 manifest read
    it rather than re-deriving).
  - **Fingerprint (D-094 golden-lock).** The discretizer encodes its **authored** configuration,
    `{"bins":4,"kind":"equal_width","precision":"exact","range":"min_max"}`, adding
    `"vmax"`/`"vmin"` **only** under `range = "manual"`; `precision` is `"exact"` or
    `{"round_to":<number>}`. The **resolved cuts are not re-encoded**: they already ride as `bin`
    objects in the `schema` array, so an auto spec and its frozen `manual_cuts` twin may carry
    different output fingerprints (D-094's one-directional guarantee). Hand-authored canonical
    bytes and independently-computed SHA-256 vectors lock both forms.
- **Why:** `equal_width` is M4's first data-calibrated discretizer, so the boundary it establishes
  (one cut engine, one formula, one retained outcome) is what makes the auto/frozen guarantee a
  structural property instead of a coincidence maintained by hand. Pinning the sign-aware formula
  and the positive-zero canonicalization now keeps cuts identical across machines (EP-7/EP-11)
  before any stored hash fossilizes them.
- **Rejected:** duplicating the interpolation in the calibrator (the exact drift D-093's shared
  machinery exists to prevent); making the derivation a **public** Core helper so the calibrator
  could call it directly (it would widen the approved public surface for no capability; reusing
  the `CreateManual` boundary over the observed span centralizes the formula just as well, EP-4);
  letting `FromCalibratedCuts` accept any ascending cut list (a wrong-sized one silently desyncs
  `bins` from the geometry); allowing percentile to substitute from hand-supplied cuts (it would
  have made a Slice D mode executable through the public calibrated-state API while the reader
  still rejected its spelling; one seam disagreeing with another is how transitional lines rot);
  the naive `vmin + (vmax - vmin) * t` for every span (overflows a wide
  opposite-sign range that is perfectly valid; G-7); rejecting extreme ranges instead
  (a finite increasing range is usable by construction); a distinct-value guard for equal_width
  (span-based binning tolerates sparse data, D-089); re-encoding resolved cuts in the discretizer
  sub-object (redundant with the schema bins, D-094); subject-local dedup or a grouped second pass
  for the triple min/max read (count-insensitive: it would buy nothing and cost a pass);
  canonicalizing zero inside `Format` (would move the standing authored `-0` bytes, G-6);
  accepting `percentile_p1_p99` before its calibration existed (a silent map to `min_max` would
  convert a spelling error into wrong output).
- **Affects:** Core (`NumericCutBins`, `EqualWidthDiscretizer`, `EqualWidthRange`,
  `CutPrecision`, `ManualCutsDiscretizer`, `PendingEqualWidth`, `CalibratedSpec`,
  `FingerprintCalculator`), Spec (`EqualWidthDiscretizerSection`, reader/writer, `SpecResolver`),
  Conversion (`Calibrator` streaming min/max), Diagnostics (`EqualWidthRangeInvalid`,
  `EqualWidthCutsCollapsed`, `CalibrationDataInsufficient`, `CalibrationCutsInvalid`); spec §7 /
  §10.2 / §11.4 / §12.3 / §16.4. Realizes D-088/D-089/D-093/D-094.

---

## M4 Slice D (equal_frequency + percentile + the bounded quantile engine)

### D-103: `equal_frequency` + percentile executable: exact-rational ranks, feasibility precedence, sign-aware midpoints, the bounded quantile accumulator, subject-local triple dedup

- **Status:** accepted (M4 Slice D; realizes D-088/D-089/D-093/D-094/D-095; the G-5/G-6/G-8/G-13
  governance items); its per-accumulator catalogue-at-most-fan-in and merge-all scheduling clauses
  superseded by D-128 (2026-09-21)
- **Date:** 2026-07-17
- **Decision:** the `equal_frequency` discretizer (§11.5) and `equal_width`
  `range = "percentile_p1_p99"` (§11.4) become executable: M4's **count-sensitive** calibration.
  `equal_frequency` leaves the transitional read-reject set (G-8), and percentile becomes an
  accepted spelling now that its calibration exists (D-102/G-8b). `equal_frequency` is
  **number-fixing** (D-061) and, unlike `equal_width`, has **no spec-determined mode**: every
  configuration draws its cuts from the population, so it always resolves to the
  `CalibrationPending` carrier (D-093). Its bins are always **open-ended**, and it composes the
  shared `NumericCutBins` engine, so it needs no ordinal path of its own (cut geometry is the
  ordering authority, §12.3) and no second cut/render implementation (D-102).
  - **Exact rank selection, separate from binary64 placement (G-5).** The rank target `N·k/bins` is
    **never materialized** in floating point: both sides are cross-multiplied into `UInt128`
    (`C_i·bins` vs `N·k`, each below 2^94, so exact by construction). This is not pedantry: above
    2^53 a `double` rank target rounds onto a cumulative count and selects the group one too early,
    which at the v1 target population (7.3M–73M records, D-007) is reachable data, not a corner
    case. Cut *placement* stays binary64, because a cut is a data value. An exact **group edge**
    (`N·k = C_i·bins`) separates whole groups, so `tie_policy` does not apply there; only a
    boundary landing strictly inside a run of equal values has a tie to resolve.
  - **Feasibility prevails over tie-side preference, a normative §11.5 amendment (G-5).** Honoring
    `tie_policy` and producing `bins - 1` distinct ascending gaps can be mutually unsatisfiable,
    not only on collision/saturation but at **both domain edges** (`"right"` on the first group
    prefers a gap below the domain; `"left"` on the last prefers one above it). §11.5 now pins the
    precedence: boundaries are allocated in ascending order, each taking the nearest feasible gap
    within the window `[p+1, m-1-(bins-1-k)]`, which may place a tied group on the **opposite** side
    of its preference. A clamp is normal resolution and is **never** diagnosed as invalid cuts.
    Because the window's lower bound forces ascent and its upper bound reserves a gap per later
    boundary, the §11.5 distinct-gap obligation becomes **total** and cuts are strictly ascending
    **by construction**; the post-hoc validity check is defense in depth, not the guarantee. Three
    normative examples land with the amendment (collision; last-group edge; first-group edge).
  - **Sign-aware midpoint placement (G-5/G-6).** `cut_placement = "midpoint"` uses the same
    sign-aware split as §11.4's interpolation (`a + (b-a)/2` for a same-sign gap, `(a+b)/2` for one
    crossing zero) because **neither form alone is safe**: the subtraction overflows an
    opposite-sign extreme gap and the sum overflows a same-sign one. The two branches must not be
    folded or reordered. A midpoint that cannot land strictly above `v_g` (adjacent representable
    doubles) falls back to `v_{g+1}`: membership-identical under half-open geometry, and it keeps
    the cut inside its own gap so the list's strict ascent survives. Every computed cut is
    positive-zero canonicalized (G-6); authored existing-kind bytes and `fp_format = 1` are
    untouched.
  - **Percentile is exact order statistics (D-089).** `p1`/`p99` are selected by the same `UInt128`
    comparisons over the same aggregated population, never interpolated between neighbours, never
    from a sketch, never from a machine-dependent library percentile, any of which would break the
    D-088 auto/frozen byte-equivalence. `p1 == p99` (or an empty population) is
    `CalibrationDataInsufficient`. The selected span feeds the **existing Slice C**
    `CreateManual` derivation over the observed span, so precision applies after span selection and
    there is no second copy of the interpolation formula (D-102's boundary, reused not widened).
  - **Exact, bounded-memory accumulation (D-095).** The `QuantileAccumulator` is a
    **fixed-capacity fill-and-spill** dictionary plus a preallocated sort buffer, both retained,
    so both charged: `Modeled(capacity) = 384 + capacity·44` on x64. The slot is dictionary entry
    24 + bucket 4 + sort-buffer element 16, and the fixed part covers the accumulator object (144),
    the `Dictionary` object (80), and three array headers (3 × 24), each padded upward.
    **`FixedBytes` is 384, not the planned 264** (see the correction below); both are correctness
    constants like `ResidentModel`'s, not perf knobs. The
    capacity comes from `EnsureCapacity`'s **accepted** (prime-rounded) value, not the request; an
    overshoot reduces multiplicatively and retries (decrementing would re-round to the same prime
    and crawl one integer at a time: ~130k probes of multi-megabyte dictionaries at a 64 MiB
    share). ±0 is folded at **intake**, not merely at placement: the dictionary, `Equals`, and
    `CompareTo` all treat the two zero spellings as one value, so they aggregate either way and the
    distinct count is right either way, but *which* spelling survives into the key, the spilled
    run, and the merged row would otherwise depend on arrival order. Canonicalizing at intake pins
    the value a cut, label, or hash is derived from. All count arithmetic is `checked`; overflow is
    the new `CalibrationPopulationTooLarge` (G-13).
  - **The honest two-tier resource contract.** **Tier 1** (byte-exact): the retained accumulator
    graph obeys `Σ Modeled(capacity_i) ≤ max(budget, A·FloorBytes)`; the floor arm is stated, not
    hidden, because a share below one entry cannot be honored. Sizing-probe transients are excluded
    (the D-082 precedent: the guarantee is over the stable post-sizing graph). **Tier 2**
    (structurally bounded, *not* byte-modeled): ≤ fan-in readers, ≤ 1 writer, ≤ 1 replay reader, a
    `PriorityQueue` ≤ fan-in, each accumulator's run catalog (≤ fan-in until 2026-09-21, then
    D-128's fixed ceiling), and pending deletions ≤ a fixed cap. Catalog entries are closed
    `(path, size)` values, not open handles. Tier 2 is deliberately **not** given a byte constant:
    a `FileStream`'s internal strategy/handle graph is runtime-owned, and an "≈ 8 KiB" claim would
    be unvalidatable.
  - **Bounded run catalog and bookkeeping.** Until 2026-09-21 a spill that would exceed the fan-in
    first consolidated the whole catalog online into one aggregated run; D-128 replaced that
    merge-all schedule with generation-tiered carries. `T_so_far` (the 3T baseline) counts **raw
    spill payload only**; merge output never inflates it. Failed deletions are bounded by a fixed
    `MaxPendingDeletions = 4 × fan-in`, opt-in to the calibration workspace: the 3T byte rule alone
    does **not** bound them, because repeated-key runs let live bytes and `T` grow together and
    never trip the escalation while pending entries grow without bound. Exceeding the cap is an
    in-path Error: persistently failing storage is broken storage. The emit-path backend keeps its
    existing uncapped semantics (EP-1).
  - **Release before merge; the consolidated-run two-pass replay.** At intake end a spilled
    accumulator flushes and **releases both buffers** before any post-intake merge, which then runs
    sequentially per attribute (D-128's in-intake carries are the sole exception, and tier 2
    accounts for that co-residence). The final merge yields one consolidated ascending
    count-aggregated run that is walked **twice**: pass 1 for the distinct count `m` and each
    boundary's preference, pass 2 for the values adjoining the selected gaps. Two passes are
    structural, not incidental: the feasibility window needs the **global** `m` before the first
    allocation and the gap-adjacent values after it, neither knowable from one forward walk. The
    zero-spill path runs the **same** walk over its in-memory buffer, so spill/non-spill identity is
    structural rather than two algorithms agreeing.
  - **Calibration population (§7/§5.3.1).** Wide rows stay independent observations: no dedup, no
    `duplicate_object_policy`, no object-key read (D-099). For triple, `equal_frequency`/percentile
    are **count-sensitive**, so each distinct cleaned `(subject, predicate, value)` contributes once
    per subject: `subject_grouped` dedups inline on the raw pass; `unordered` adds a **grouped
    second pass** (`FirstAppearanceGrouping`), but only when a count-sensitive need exists, since
    discovery-class and min/max needs are set-/count-insensitive and would pay a whole read for
    nothing. Never a third pass, and never a dataset-wide seen set (the very thing D-095 forbids).
    The dedup key is the **raw cleaned spelling**, not parsed numeric identity: `"90"` and `"90.0"`
    are distinct observations that both count, and only then aggregate onto the same value.
    Discovery-class observation stays on the raw stream (§17 rule 3's first-appearance order is raw
    input order, which grouping would reorder). **Exactly one pass feeds a given observer**: the raw
    pass owns discovery-class observers always, and count-sensitive observers only under
    `subject_grouped`, where the inline dedup applies; under `unordered` the grouped pass owns them
    alone. Feeding them from both would add every raw row's multiplicity on top of the deduped
    contribution, changing the cuts and double-reporting unparseable values in one phase.
  - **Fingerprint (D-094 golden-lock).** `equal_frequency` encodes its **authored** configuration
    with the §11.5 defaults spelled,
    `{"bins":4,"cut_placement":"right_value","kind":"equal_frequency","tie_policy":"left"}`, and
    percentile activates
    `{"bins":4,"kind":"equal_width","precision":"exact","range":"percentile_p1_p99"}`. Calibrated
    cuts are **not** re-encoded: they ride as `bin` objects in the `schema` array (D-094's
    one-directional guarantee). Hand-authored canonical bytes and independently-computed SHA-256
    vectors lock both forms.
- **`FixedBytes` correction.** The planned constant, 264 bytes, budgeted about 88 bytes for the
  accumulator object. The implemented object is **144 bytes** (header 16 + 128 of fields: nine
  references, a `CancellationToken`, two ints, two longs, and a `SpoolRunHandle?`), so the real
  fixed retained part is 80 + 3×24 + 144 = **296 > 264**. A model that under-charges states a
  bound it does not hold, and D-082 makes `actual retained ≤ modeled` a **correctness** property,
  so the constant is **384** (each component padded upward: 160 + 96 + 96 = 352 → 384). Raising it
  only tightens every bound assertion, and `Σ Modeled ≤ max(budget, A·FloorBytes)` still holds by
  construction (both sides move together). The tier-1 test found the under-charge by deriving the
  accumulator object independently rather than only its arrays.
- **Why:** equal-frequency is the milestone's first calibration whose result depends on **how many**
  observations carry a value, not merely which occur, which is what forces exact counting, the
  bounded accumulator, and the triple dedup all at once. Pinning exact-rational ranks and the
  sign-aware midpoint now keeps cuts identical across machines (EP-7/EP-11) before any stored hash
  fossilizes them, and stating the feasibility precedence normatively turns a case where the spec
  asked for two incompatible things into one defined answer.
- **Rejected:** a `double` (or `decimal`) rank target (silently selects the wrong order statistic
  near 2^53, reachable at v1 scale); applying `tie_policy` at an exact group edge (there is no tie
  to resolve, and it produces a worse split: `[1,2,3,4]`, `bins = 2`, `"right"` would cut at 2
  rather than the even 3); a greedy gap allocator without the reservation term (strands later
  boundaries and drops bins, which §11.5 forbids); diagnosing a feasibility clamp as
  `CalibrationCutsInvalid` (it is normal resolution: the cuts are valid); one folded midpoint
  formula (each form overflows exactly the case the other handles); replacing a midpoint that lands
  *on* `v_{g+1}` (already membership-correct); canonicalizing ±0 only at placement (the merge sorts
  by total order, so the two spellings would never aggregate and `m` would be wrong); a distinct
  dictionary per spill (the capacity **is** the budget here, so replacing it buys no bound and costs
  a re-size); decrement-by-one sizing reduction (re-rounds to the same prime and crawls); an
  unbounded spill-run catalog (memory proportional to the population: the very thing the model
  forbids); bounding failed deletions by the 3T byte rule alone (repeated-key runs are the
  counterexample); a byte constant for tier 2 (unvalidatable: `FileStream` internals are
  runtime-owned); a grouped second pass for count-insensitive triple needs (a whole read for
  nothing); a dataset-wide dedup set (unbounded, D-095); deduplicating by parsed numeric identity
  (§5.3.1 counts distinct cleaned *observations*); a new code for percentile's collapsed span or
  equal-frequency's distinct-value guard (both are "the data cannot bound these cuts", which
  `CalibrationDataInsufficient` already owns, D-067); reusing `CalibrationDataInsufficient` for
  count overflow (its exact opposite: too much data, not too little); letting `OverflowException`
  cross the calibrator seam (EP-14); re-encoding calibrated cuts in the discretizer sub-object
  (redundant with the schema bins, D-094); emitting the resolved `tie_policy`/`cut_placement`
  defaults into authored TOML (D-049 presence tracking; the fingerprint is where the resolved
  values matter).
- **Affects:** Core (`TiePolicy`, `CutPlacement`, `EqualFrequencyDiscretizer`,
  `PendingEqualFrequency`, `CalibratedSpec`, `ResolvedSpec`, `FingerprintCalculator`), Spec
  (`EqualFrequencyDiscretizerSection`, reader/writer, `SpecResolver`), Conversion
  (`QuantileSelection`, `QuantileAccumulator`, `CalibrationBudget`, `ValueCountMerger`,
  `ICalibrationObserver`, `Calibrator`, `SpoolWorkspace`), Diagnostics
  (`CalibrationPopulationTooLarge`); spec §7 / §11.4 / §11.5 / §16.4. Realizes
  D-088/D-089/D-093/D-094/D-095.

---

## M4 Slice E (value_groups)

### D-104: `value_groups` executable: first-match grouping, authored-presence matchers, raw-order pass-through discovery, ordinal over group labels, and the final deferred-kind retirement

- **Status:** accepted (M4 Slice E; realizes D-022/D-055/D-090/D-093/D-094/D-095; the G-8/G-11
  governance items; completes D-070's retirement)
- **Date:** 2026-07-17
- **Decision:** the `value_groups` discretizer (§11.6) becomes executable under **all three**
  `unmatched` policies, and with it **every v1 discretizer kind is executable**, so D-070's
  transitional read-reject set empties and `DiscretizerKindNotYetSupported` retires with its last
  owner (G-8).
  - **Discretization, not domain membership.** `value_groups` is a **value-bin** discretizer whose
    bin universe is its *groups*, not a `declared_domain` (D-055), so `declared_domain` and
    `value_labels` are both **dormant** under it (§10.3/§10.8/D-049): authored, round-tripped,
    ignored by validation, naming, and the effective-domain fingerprint check, never an error. Its
    geometry is ordinary value bins (`CutBins = false`), never cut geometry, and it is
    **string-fixing** (D-061): an authored `value_type = "number"` is `SourceValueTypeInvalid`.
  - **Matching, pinned exactly.** A group matches when an explicit value matches **or** its regex
    matches (OR within a group); groups are walked in **declaration order** and the **first match
    wins**, which is why the `groups` array is planned-order everywhere: semantic, not
    presentational. Explicit values compare with **ordinal** equality (EP-12). The regex is compiled
    **once** per group with `RegexOptions.CultureInvariant`, default backtracking, and
    `Regex.InfiniteMatchTimeout` passed **explicitly**. The constructor overloads that omit a
    timeout silently inherit the host's ambient `REGEX_DEFAULT_MATCH_TIMEOUT`, which would let the
    same spec over the same input complete on one machine and throw `RegexMatchTimeoutException` on
    another (EP-7), on the exception channel mid-calibrate/emit rather than the diagnostic one
    (EP-14); "no timeout" is therefore a property the construction must *state*, not one it can
    inherit. Matching is **partial** (unanchored `IsMatch`) and **case-sensitive** unless the author
    writes an inline option such as `(?i)`. Nothing is trimmed, case-folded, anchored, or
    culture-normalized.
  - **Authored presence survives into Core (G-11).** `ValueGroup.Values` is `null` when `values`
    was omitted and a list (**possibly empty**) when it was authored, because §14 encodes
    `values` only when authored and the two must be byte-distinct. Authored order and duplicates
    are retained: this is authored configuration, not a canonicalized set. The matcher-validity
    predicate is exactly *at least one non-empty explicit value **or** a non-empty pattern*, so
    `values = []` alone is invalid while `values = []` alongside a pattern is valid.
  - **One matcher, two phases.** Calibration discovery and emit classification route through the
    **same** Core matcher (the calibrator classifies through a `ValueGroupsDiscretizer` built over
    the authored groups under `skip`, whose `Unknown` outcome *is* "no group claimed it"), so the
    two phases cannot disagree about what "unmatched" means, and no pattern is recompiled per
    observed value. (The throwaway-probe shape mirrors the calibrator's existing
    `CreateManual`-for-its-cuts use, D-102.)
  - **Pass-through is data-dependent and discovery-class.** `passthrough` cannot be constructed
    directly (its public factory rejects the policy): it resolves to the `CalibrationPending`
    carrier `PendingValueGroupsPassthrough`, the Calibrate phase discovers one bin per **distinct
    observed ungrouped raw value in first-observation order** (§17 rule 3), and
    `CalibratedSpec.Create` substitutes the executable form over the retained `PassthroughBins`
    (D-093). Discovery is **set-based and idempotent**, so, unlike the count-sensitive kinds, it
    needs no `QuantileAccumulator`, value counts, spill runs, merge/replay, or §5.3.1 subject-local
    deduplication, and it does not enter the count-sensitive budget divisor: its bound is the
    attribute vocabulary, the documented schema-scale carve-out (EP-16/D-095). It therefore reads
    the **raw** pass only (for both triple orderings) and alone never triggers the grouped second
    pass; when a count-sensitive attribute coexists and forces that pass, pass-through is fed
    **only** from the raw one (D-103's one-pass-per-observer rule), never both, never a third.
  - **Mode-triggered warning, retained empty outcome.** `ValueGroupsPassthroughDataDependent`
    (Warning, calibrate) fires whenever the mode executes, **zero discoveries included** (the
    column set depends on this input either way), and an **empty** `PassthroughBins` is retained as
    the legitimate zero-discovery completeness marker, never dropped (a dropped one would read as a
    skipped calibration). There is deliberately **no** data-insufficiency guard: discovering no
    ungrouped value means every value matched a group, which is a good outcome, not a failure.
  - **Ordinal over group labels.** Ordinal `value_groups` (with `unmatched` `skip`/`other`)
    requires an explicit `scale.order` that is a **full permutation of the group labels**,
    including the synthetic `Other`: never `declared_domain`, and never the raw values the groups
    match. Group labels are strings, so unlike numeric `free_per_value` there is **no** natural
    order to derive. The universe comes from the discretizer's own `BinLabels`, so one permutation
    algorithm serves every value-bin kind (EP-5). All four `direction × boundary` combinations are
    live (value bins have no half-open geometry) and `drop_top` keeps its value-bin semantics.
    `ordinal` + `passthrough` is `OrdinalNotAllowedWithValueGroupsPassthrough` (Error, spec
    validate): a data-discovered bin set can never be a full authored permutation.
  - **Diagnostics.** Added: `ValueGroupsLabelDuplicate` (Error, spec validate: a duplicate
    authored label, or one colliding with the synthetic `Other` under `unmatched = "other"`;
    ordinal, so `"Other"` collides and `"other"` does not),
    `OrdinalNotAllowedWithValueGroupsPassthrough` (Error, spec validate),
    `ValueGroupsPassthroughDataDependent` (Warning, calibrate). Removed:
    `DiscretizerKindNotYetSupported`. Net **65 + 3 − 1 = 67**. Reused rather than duplicated
    (D-067): a malformed group/regex/`unmatched` field is `SpecFieldInvalid` (there is deliberately
    **no** dedicated regex-error code: an uncompilable pattern is one malformed field, checked at
    parse); an observed pass-through bin colliding with an authored label is the ordinary
    plan-phase `FormalAttributeCollision` (data-dependent, so not the static duplicate code); an
    unmatched value under `skip` is `UnknownValueObserved`. Ownership is split so one condition
    yields one code: the **reader** owns each group's own validity, the **seam** owns the
    cross-group rules, and resolution declines to build a discretizer over a label conflict
    *without* reporting it a second time.
  - **`include` behaves as `warn` under `skip`** (§11.6): an unmatched value is not a domain gap
    (there is no domain; D-055), so `include` has nothing to extend. This falls out of the
    `Unknown` outcome plus `ConsumesDeclaredDomain = false` rather than being special-cased: the
    spec stays fully-declared (no calibration pass), the emitter warns, and **no**
    `UnknownValuePolicyInclude` and no schema extension occur.
  - **Fingerprints (§14/D-094).** The authored config only:
    `{"groups":[…],"kind":"value_groups","unmatched":"skip"}`, with `groups` in declaration order
    (never sorted), each group's keys sorted `label`/`pattern`/`values`, `pattern`/`values` present
    **only when authored**, inner values in authored order with duplicates retained, and the
    **resolved** `unmatched` always spelled. **Discovered pass-through bins are not encoded here**:
    they are effective, not authored, and already ride as `bin` objects in the `schema` array, so
    re-encoding them would be the second source of truth D-094 forbids; what *is* encoded is
    `"unmatched":"passthrough"`, the authored config that made the schema data-dependent. Two specs
    differing only in omitted-vs-authored-empty `values` therefore share a `schema_fingerprint` but
    carry different **output** fingerprints (D-094's one-directional guarantee). Independently
    computed SHA-256 vectors lock the encoding.
- **Why:** `value_groups` is the M4 grouping discretizer (D-022) and the last deferred kind. Its
  semantics are almost entirely about *precedence and presence* (which group claims a value, which
  bin order results, and which authored spellings survive into the fingerprint), so leaving any of
  them to the implementation would have fossilized an accident: the first stored hash pins the
  encoding (D-094), and first-match order is observable in output bytes. Pinning the matcher in one
  place, and routing calibration through it, is what makes discovery and emit provably agree rather
  than coincidentally agree. Keeping pass-through discovery on the discovery-class path (rather
  than reusing the quantile engine because both "read data") is what keeps it bounded by the
  schema, not the population.
- **Rejected:** a dedicated regex-error diagnostic (`SpecFieldInvalid` already owns malformed
  fields; D-090/EP-14); a public `ValueGroup.Matches` or a production `InternalsVisibleTo` so the
  calibrator could match directly (the approved public surface is the pinned one, and the
  discretizer already answers the question through supported API); normalizing an authored
  `values = []` to null (it is authored state the §14 bytes distinguish; G-11); sorting the
  `groups` array or the inner `values` (both are semantically ordered authored config); a
  non-empty-groups requirement (D-090/G-11 make each *group* the unit of validity, and an empty
  group list is coherent under `other`); rejecting a discovered bin that equals an authored label
  at construction (it is data-dependent: plan owns it as `FormalAttributeCollision`); routing
  pass-through through the count-sensitive machinery (its bound is the vocabulary, not the
  population; D-095); a natural order for ordinal group labels (they are strings; only numeric
  `free_per_value` has one to derive); reporting both `ValueGroupsLabelDuplicate` and
  `AttributeScalingMissing` for one duplicate (one condition → one code, D-067); a regex timeout
  knob, and equally the timeout-omitting `Regex` overloads that quietly inherit one from the host
  (both make matching machine-dependent; EP-7).
- **Affects:** Core (`ValueGroup`, `ValueGroupsDiscretizer`, `ValueGroupsUnmatched`,
  `PendingValueGroupsPassthrough`, `CalibratedSpec`, `ResolvedSpec`, `ConversionPlanner`,
  `FingerprintCalculator`), Conversion (`Calibrator` pass-through observer), Spec
  (`ValueGroupsDiscretizerSection` / `ValueGroupSection`, reader/writer, seam resolution),
  Diagnostics (+3, −1); spec §7 / §11.6 / §12.3 / §16.4 / §19.4. Realizes
  D-022/D-055/D-090/D-093/D-094/D-095; completes D-070.

---

## M4 Slice F (restrict_to execution + emit observability)

### D-105: `restrict_to` executable: exact numeric entries, existential object filtering, sequencing, filter-only diagnostics, emit observability, the policy-bearing `restrictions` container, and caller-discard output

- **Status:** accepted (M4 Slice F; realizes D-091/D-097; the G-2/G-6/G-9/G-10/G-12
  governance items; **completes M4**)
- **Date:** 2026-07-17
- **Decision:** `restrict_to` executes.
  - **Exact numeric entries.** The public Core carrier is
    `RestrictToNumber(double Value)`. It carries **parsed numeric identity**, not
    string-spelling equality and not a single-point range: `30`, `30.0`, and `3e1` are
    one entry, and matching has **no tolerance**. It is deliberately a positional record
    able to hold a non-finite value, because the Spec document model reuses the Core
    union (D-057), so the carrier must represent an authored `{ value = nan }` long enough for the
    **resolve seam** to diagnose it as `RestrictToRangeInvalid` on the user channel; a
    throwing factory would put an authoring error on the exception channel (EP-14). The
    boundary is layered instead: the seam validates and zero-canonicalizes, and
    `ResolvedSpec.Create` (plus the calibrated-state factories) **throw** for anything
    non-finite that survives past it, which only a hand-built graph can produce. The
    union now has exactly three recognized variants; an unknown one is rejected at the
    trust boundary rather than silently ignored.
  - **Zero canonicalization is scoped (G-6).** Restriction exact values and provided
    range bounds are zero-canonicalized **at the seam** (the already-numeric arm of the
    D-096 chain) and migrated tokens at the migrator (the text-sourced arm), so an
    authored `-0` resolves, matches, plans, and hashes identically to `0`.
    `CanonicalJson.AppendNumber` is **untouched** and still formats `-0.0` as `-0`,
    which is precisely what keeps every authored manual-cut byte and every stored
    `fp_format = 1` hash unmoved. Canonicalization at the seam is what makes the
    untouched encoder safe.
  - **Matching is existential, over formed objects.** One shared matcher serves all four
    emit paths, so wide streaming, wide `dedupe`, and both triple orderings cannot drift
    about what a restriction means: an object passes a restriction when **at least one**
    of its observations for that source matches **at least one** entry (OR across both),
    and is emitted only when **every** restriction passes (AND). Missing matches nothing;
    an absent triple predicate supplies no observation and fails. Strings compare
    **ordinally** (EP-12); numbers parse under `binding.locale` (derived **once per emit**,
    never ambient), then zero-canonicalize; unparseable/non-finite input is a non-match;
    ranges are half-open `[from, to)` and `{}` matches any usable numeric value.
    Restrictions read **cleaned raw values before discretization** and never consult bins.
  - **Restrictions filter objects, not observations.** A surviving object keeps **all**
    its crosses, not only the matching ones, so classification runs for every formed
    object, filtered or not.
  - **Sequencing vs the object-key policies (G-2).** Restriction is evaluated on each
    formed object's **complete** observation set; object formation order is unchanged.
    Wide single-pass: classify → filter → (survivors only) key validity, duplicate
    policy, name assignment. A non-surviving row **is not an object**, so it trips no
    `fail` duplicate check and consumes no `keep` assigned name (which are assigned in
    *emission* order, §6.1). **`row_index` names are input positions and filtering never
    renumbers them**: if row 0 is filtered and row 1 survives, the survivor is still `1`.
    Wide `dedupe`: grouping strictly **precedes** filtering. One restriction is evaluated
    existentially over all merged observations, one match preserves the whole object with
    every cross, and a non-surviving group is dropped only after grouping and
    classification; the aggregated `DuplicateObjectKey` (Info) is therefore **pre-filter**
    (the intake hook observes the raw stream), which is right: it reports what the input
    contained. Triple: the subject's complete group is the formed object, so emission is
    decided at group close; contiguity and subject validity are structural, precede
    filtering, and are independent of it.
  - **Filter-only diagnostic ownership (D-097).** A filter-only attribute has no
    discretization pass, so the restriction path is its **only** diagnostic owner: an
    unparseable/non-finite numeric observation is a non-match **plus** one aggregated
    `SourceValueUnparseable` at the severity `unknown_value_policy` selects (`skip`
    silent, `warn` Warning, `fail` Error, `include` Warning). A valid non-match and a
    missing value are **silent**: a non-match is the filter working. For an
    **included**-and-restricted attribute the classification pass already owns that
    diagnostic, so the restriction path stays silent for it: at-most-once is **per raw
    observation per attribute per pass**. (Two attributes bound to one column each report
    once: tallies are attribute-owned, which is not double-counting.)
  - **Emit observability.** The three §16.4 warnings registered since M2 gain their sites
    (D-085 enum timing): `NoObjectsEmitted` (zero rows), `ObjectHasNoCrosses` (empty
    rows), and `AttributeHasNoCrosses` (empty columns). The latter two are **aggregated**
    (count + ≤3 samples, in emission and plan order respectively), tracked over a bounded
    `bool[]` and a tally, never the matrix (EP-16). Only **emitted** objects count. They
    flush in the pinned order whole-context → rows → columns, and are suppressed on **any
    invalid run**, the same predicate G-12 uses for the artifact itself: any Error/Fatal
    among the emit diagnostics. That covers two cases, which differ in whether the stream
    stops:
    - a **structural halt or storage failure** stops the object stream, so the aggregates
      would describe a truncated read (the established §16.4 rule, unchanged);
    - an **`unknown_value_policy = "fail"` abort** (a filter-only restriction's or an
      included attribute's) does **not** stop the stream (the aggregated per-attribute
      diagnostic requires reading the whole population, D-050/D-059, so enumeration
      completes and the Error flushes at the end), but the run is invalid, so describing
      the shape of a context the caller must discard (G-12) is noise. Literal truncation
      would contradict the aggregation contract and change the pre-Slice-F
      included-attribute `fail` behaviour, so "abort" here means §16.2's
      Error/operation-failed semantics, not "stop reading rows". The §18.1 and §16.2
      spec text carry this distinction normatively.
    The warnings route through the ordinary data sink, so the `.cxt` replay session's
    first-pass claim single-counts them. Empty columns are **expected** after filtering,
    not a fault: §7 fixes the vocabulary over the input universe before objects are
    selected.
  - **The `restrictions` fingerprint container (G-9/G-10).** Present in `shared` only when
    some attribute restricts (so every restriction-free spec keeps its exact prior bytes
    and hashes), after `attributes` and `binding`. Each object carries `entries`, the
    D-077 `source` encoding verbatim, and the resolved `unknown_value_policy` (G-9; see
    the D-091 in-place amendment for why). Entries and objects are sorted by their
    complete canonical JSON with **`StringComparer.Ordinal`** (a **UTF-16 code-unit**
    compare over the JSON strings, applied **before** UTF-8 encoding, G-10), and exact
    canonical duplicates removed; overlapping-but-distinct ranges are never merged. This
    sorting/deduplication is a **fingerprint projection only**: the document, the resolved
    authored list, the plan, and emit all keep authored order and duplicates.
  - **Type-directed `.bed` migration (D-091).** Only a restrict token on v2 type `o`
    migrates numerically, parsed under `binding.locale ?? "invariant"` with the same
    predefined-only rule as resolution. An unparseable/non-finite token on `o`, and every
    token on a categorical type, stays a **verbatim string**: v2 restricted those by raw
    value, so reinterpreting `007` as `7` would silently change which objects survive. An
    **invalid locale** reinterprets nothing, does not fall back to invariant, does not
    throw, and mints no migrate-phase diagnostic: full resolution owns
    `BindingLocaleInvalid` (D-067: one condition, one owner).
  - **Caller-discard output contract (G-12), normative.** Writers serialize their inputs
    to **caller-owned sinks** and remain semantically dumb (EP-15); they cannot retract
    bytes. A run's artifact is valid **only if** the run's collected diagnostics (for
    `.cxt`, inspected **after `EmitReplaySession` disposal**, which is when the final
    cross-pass aggregates land) contain no Error/Fatal; otherwise **the caller must
    discard it**. This is not hypothetical: a *deterministic* halt truncates both `.cxt`
    passes **identically**, so the object-name-sequence invariant cannot catch it and a
    structurally well-formed but truncated file can exist alongside an Error; and `.dat`
    streams rows immediately, so bytes precede any later Error. Transactional publication
    is M7's conversion-run abstraction (D-122, D-123).
  - **Diagnostics.** Adds `RestrictToRangeInvalid` (Error, spec validate),
    `NoObjectsEmitted`, `AttributeHasNoCrosses`, `ObjectHasNoCrosses` (Warning, emit);
    **renames** `RestrictToOnNumericRequiresRange` → `RestrictToNumericEntryRequired`
    with its check site, **with no alias** (the old name stopped being true once an exact
    entry existed; keeping both would give one condition two names, D-067); **removes**
    `RestrictToNotImplementedV1`. Registry: 67 + 4 − 1 = **70** (the rename is
    count-neutral).
  - **M4 is complete.** Every v1 discretizer kind executes, every `restrict_to` form
    executes, and no M4 transitional diagnostic or guard remains. Later milestones'
    transitional codes were left in place: `TemplateMatcherNotImplementedV1` (retired at M6
    by D-121), `SpecSurfaceNotYetSupported` (its naming half retired by D-120; it now covers
    only `value_type = "date"`, D-038), and the permanent v1 reservations.
- **Why:** `restrict_to` was carried, shape-validated, and fingerprint-specified across
  D-057/D-063/D-076/D-079/D-091/D-097, but never executed: a spec could express a filter
  the converter rejected. Executing it closes v1's last silent-output gap in the M4 scope
  and is what makes the filter-only pattern (§10.4's "keep only objects whose Gene is
  Bmp5" without a Gene column) real. The sequencing rule (G-2) had to be pinned because
  §6.1/§10.4 were silent on it and the two orders are observably different (a filtered
  row tripping `fail`, or consuming a `keep` name, changes the output). The policy key
  (G-9) had to be added because D-097 turned `unknown_value_policy` into live
  configuration on attributes that have no other fingerprint carrier. The caller-discard
  rule (G-12) had to become normative because §18.1's "the partial output is discarded"
  described something no writer can do.
- **Rejected:** filtering rows before grouping under `dedupe` (contradicts D-091's
  group-first clause and would drop an object whose match arrives on a later row);
  renumbering `row_index` over survivors (§5.4 names are input positions; renumbering
  would make an object's name depend on the filter); letting a filtered row trip `fail` or
  consume a `keep` name (it is not an object); making the restriction path report an
  included attribute's unparseable values (double-counting the same cell, D-097);
  `UnknownValueObserved` for a valid non-match (a non-match is the filter working, not an
  anomaly); per-object/per-column observability diagnostics (a storm at target scale;
  aggregated with bounded samples, §16.4); emitting the observability warnings after a
  halt (they would describe the halt); a throwing `RestrictToNumber` factory (wrong
  channel for an authoring error, EP-14; the boundary is layered instead); a
  `RestrictToOnNumericRequiresRange` alias (one condition, one name); sorting the
  canonical restriction JSON by UTF-8 bytes (diverges from UTF-16 ordinal above U+E000;
  EP-12 defines ordinal as a code-unit compare); merging overlapping ranges (loses authored
  intent, no determinism gain); sorting or deduplicating the authored document/plan to
  match the fingerprint (the projection is the fingerprint's, not the author's);
  reinterpreting numeric-looking tokens on categorical `.bed` attributes (changes which
  objects survive); falling back to invariant on an invalid migrate locale (parses under a
  locale the author never asked for); making the writers transactional (EP-15; that is
  M7's run abstraction).
- **Affects:** Core (`RestrictToNumber`, the `ResolvedSpec`/`CalibratedSpec` numeric boundary,
  `ConversionPlanner`, the `FingerprintCalculator` restrictions container), Conversion
  (`RestrictionFilter`, `EmitObservability`, `Emitter`), Spec (reader/writer/resolver
  exact-entry surface, `BedMigrator`), Diagnostics (+4, −1, 1 rename → **70**); spec §6.1 / §7 /
  §10.4 / §14 / §16.2 / §16.4 / §18.1 / §19.4; roadmap M4 exit. Realizes
  D-021/D-057/D-063/D-076/D-079/D-091/D-097; amends D-091 in place (G-9). **Completes M4.**

---

## M5 (discovery / probe) pre-implementation audit

These entries set the M5 Discovery (`probe`) contract: an optional draft-spec generator outside
the convert pipeline. Its normative text is spec §7.1, with clarifications in §5.1, §14, §16.4
and §17.

### D-106: Discovery `probe`: caller-selected shape, no inference, universal identity + nominal, one set-based observation pass

- **Status:** accepted (M5 pre-implementation audit; refines D-003/D-036)
- **Date:** 2026-07-18
- **Decision:** `probe` is an **optional draft-generation operation outside** the
  Parse/validate → Calibrate → Plan → Emit pipeline (D-003/D-036; §7's "convert calibrates
  but never discovers" stands unchanged). The **caller always selects the shape**
  (`wide` | `triple`); probe performs **no structural inference of any kind**: no delimiter
  sniffing, header detection, shape detection, or type inference. Every read setting has a
  caller-overridable default. Most are the §5.1 **common** binding defaults, shared by **both**
  shapes: delimiter `","`, quote `"` (the only supported quote, D-054), encoding `utf-8`,
  `missing_token = "?"` (empty token disables token matching; empty cells are always missing),
  and locale `invariant` (inert: probe parses no numbers). Only the **shape-specific**
  defaults differ: `has_header` follows §5.1's shape default (`true` wide, `false` triple), and
  **triple** also defaults `ordering = "unordered"` and roles
  `subject = 0, predicate = 1, value = 2` (a complete role map supplyable in one addressing
  mode, §5.3; wide has no ordering/role settings). **`subject_grouped` is explicit-only**:
  never a default, never inferred. Every discovered attribute is authored as **string-valued `identity` +
  `nominal`**; no numeric/boolean/ordinal/date inference at M5. Probe observes the source's
  **cleaned records exactly once, in input order**: one data-record pass ("single pass"
  means one *record* pass, not structural inference), per-attribute **set-based and
  idempotent** over cleaned values, with **ordinal** identity (EP-12) and **first-observation**
  domain order (§17 rule 3's principle). Missing values are **not observations** (empty
  field, or a field equal to the effective `missing_token`). **Triple** probe observes raw
  `(predicate, value)` pairs; predicates are discovered in **first-appearance order**; an
  empty/missing predicate is ignored; **no grouped/count-sensitive pass, ever**, and no
  contiguity requirement under `unordered`. For **structural symmetry with conversion**, a
  triple probe validates **subject usability under both orderings** (an empty,
  whitespace-only, control-character, or absent subject halts the probe; otherwise the
  draft would violate the same-source conversion guarantee, D-107), and an explicitly
  selected `subject_grouped` probe also validates **contiguity**; **wide** probe is
  row-index based and does **no** object-key validation. Cross-checks: a probed attribute's
  domain equals Calibrate's observed domain (same values, same order) for the same input,
  and probe's grouped-input rejection is symmetric with conversion's.
- **Why:** legacy FcaBedrock already required choosing CSV/TSV, wide/triple, and header
  handling before autodetection: it was never a zero-configuration sniffer, so requiring
  the caller to select the shape is continuity, not a new constraint. A conservative base
  that infers nothing keeps drafts predictable and defers legacy guided detection ("this
  looks continuous; add ranges?") to future Discovery UX. Pinning "one pass = one record
  pass" over set-based ordinal observation is what makes probe's and Calibrate's observed
  domains provably identical rather than coincidentally so.
- **Rejected:** structural/type inference at M5 (useful later as guided UX, not the
  conservative base; EP-3); numeric/date/boolean typing now; a grouped second pass or a
  contiguity requirement under `unordered` (set-based observation needs neither); skipping
  subject-usability validation (would let a probe author a draft the same-source conversion
  rejects, D-107).
- **Affects:** Discovery (the engine), Spec (`SpecWriter` draft authoring); spec §5.1 /
  §7 (new §7.1) / §17. Refines D-003/D-036; cross-references D-038 (no date inference), D-054
  (single supported quote), D-061 (`identity` is string-only, so `nominal` over `identity`
  is the universal draft cell), D-082 (triple structural validity), D-099 (calibrate/probe
  structural symmetry), D-104 (first-observation-order discovery precedent).

### D-107: Draft naming/binding matrix, validity guarantee, and content inventory; no stored fingerprints/clock/tool version; caller enrichment

- **Status:** accepted (M5 pre-implementation audit)
- **Date:** 2026-07-18
- **Decision:** a **successful** probe produces a draft that (1) contains **at least one
  attribute**; (2) **rereads** under the strict `SpecReader`; (3) **resolves** against the
  probed source's schema with no Error/Fatal; (4) **converts the same source under the same
  effective settings** with no Error/Fatal (warnings and structurally valid degenerate
  contexts allowed: probe does not itself run conversion, and tests enforce this). Probe returns
  **no draft** (diagnostics only) when no valid draft exists: zero-column wide input, triple
  input with no usable predicates, structural invalidity (unusable subjects under either
  ordering; non-contiguous input under explicit `subject_grouped`), or impossible
  binding/naming. **All-missing columns and header-only sources succeed** (attributes
  authored, domains omitted; their data-side emptiness surfaces at convert as the existing
  degenerate-context warnings, §16.4).
  - **Naming/binding matrix.** **Wide:** a **unique, usable** header cell binds **by name**
    (preserving reorder protection); a **duplicate, blank, unusable, or headerless** column
    binds **by physical index**. Fallback names (headerless or unusable header) are
    `column_<zero-based-index>`. Duplicate names disambiguate deterministically with
    `#<source-index>`, escalating ordinally `#1`, `#2`, … to the first unused. **Triple:** a
    usable predicate string is **both** the source selector and the attribute name; an
    **unusable** predicate (invalid as a §10.1 name) keeps its **exact** source selector but
    receives the logical name `predicate_<zero-based-first-appearance-ordinal>`;
    empty/missing predicates are ignored. **No source selector is ever silently changed.**
    All names satisfy §10.1 validity and §10.2 uniqueness. Every adjustment/disambiguation is
    diagnosed (aggregated Warning, D-111); plain headerless `column_N` synthesis alone is
    **not** a warning.
  - **Content inventory.** The draft contains exactly: `[spec]` (`version = 1` + a
    deterministic probe `description`); `[binding]` with **every effective read setting
    authored explicitly even when defaulted** (a self-documenting draft, including the
    complete triple ordering/role mappings); `[provenance]` with only the deterministic notes
    of D-108; and per attribute `name`, `source` (with **explicit** `value_type = "string"`),
    `discretizer = { kind = "identity" }`, `scale = { kind = "nominal" }`, plus the domain
    per D-108. **Nothing else:** **no stored fingerprints** (a probe draft is never a frozen
    artifact, even when its explicit domains would make it fully-declared, §14), **no
    clock/timestamps** (`created_at` never stamped: the D-079 no-clock rule), no tool
    version, no `[defaults]`, `[output]`, templates, matchers, or object-key section (the
    defaults are correct: wide `row_index`, triple subject-pinned). The initial Discovery API
    accepts **no speculative provenance parameters** (EP-6); the caller may enrich the returned
    `SpecDocument` afterwards (it is a public record).
- **Why:** the four-part guarantee makes "a probe draft is immediately usable" a *tested*
  property, not a hope: the reread/resolve/convert chain is exactly what a user does next.
  Binding a unique header by name preserves the reorder protection wide binding already gives;
  falling back to index everywhere else keeps a draft that resolves. A duplicate or blank
  header name does not resolve to exactly one column (`SourceBindingInvalid`, §10.2), so
  binding it by name would make the draft fail its own reread/resolve/convert guarantee. A
  probe draft is a starting point, so freezing it (fingerprints) or stamping it (clock) would
  fossilize provenance the user has not yet reviewed.
- **Rejected:** binding an ambiguous/blank/duplicate header by name (a duplicate or blank name
  fails resolution (`SourceBindingInvalid`, §10.2), so the draft would not satisfy its
  validity guarantee; index binding is the resolvable fallback);
  renaming a source selector to make it a valid attribute name (would change which
  predicate/column the draft reads); storing fingerprints or a timestamp in a draft (it is
  not a frozen artifact: a stored hash would fossilize an unreviewed schema, and a clock
  read violates D-079/EP-13); a no-draft outcome for all-missing/header-only input (the
  attributes are real; their emptiness is a convert-time signal); speculative provenance
  parameters on the first API (EP-6: the caller enriches the returned record).
- **Affects:** Discovery (the engine), Spec (`SpecWriter`, `SpecDocument`); spec §5.2 /
  §5.3 / §7.1 / §10.1 / §10.2 / §14. Cross-references D-049 (`include`/dormant-config
  authoring hygiene), D-066 (document model the draft targets), D-071 (absent-domain
  handling), D-075 (canonical writer / strict reader), D-079 (no-clock rule), D-083 (wide
  object-key defaults).

### D-108: Retention limit (100,000 default), strictly-greater truncation, prefix + `include` recovery, marker + always-written notes, probe options ownership; legacy retention-cap correction

- **Status:** accepted (M5 pre-implementation audit)
- **Date:** 2026-07-18
- **Decision:** probe retains, **per attribute**, at most the first `limit` **distinct
  cleaned non-missing** values in first-observation order, allocated as observed (no
  legacy-style preallocation). The **default limit is 100,000**. **Truncation is strictly
  greater-than:** an attribute is truncated only when **more than** `limit` distinct values
  exist; probe then knows only that **at least one more distinct value exists**, never an
  exact over-limit count (counting distinct requires retaining). A **truncated** attribute
  authors its retained **prefix** as `declared_domain` (first-observation order) **plus
  `unknown_value_policy = "include"`**, so converting the draft over the probed source
  **recovers the complete schema** via existing §10.6 include calibration (include-appended
  values follow the declared prefix in first-observation order per §17 rule 3, making the
  resulting column set and order identical to an untruncated probe's). An **untruncated**
  attribute authors its complete non-empty domain; an **all-missing** attribute authors **no
  domain** (omitted). Each truncated attribute also carries a **deterministic
  human-readable marker in its `description`**. `[provenance].notes` **always** records the
  effective per-attribute limit and the number of truncated attributes, **including zero**,
  so an untruncated draft still explains which limit produced it. Description and notes are
  **fingerprint-inert** (neither is a fingerprint input, §14). The limit is a **probe option**
  (M7 exposes it as `probe --limit`), never a conversion `[binding]` field and never a
  fingerprint input; the user may re-probe with a higher limit to inspect more values.
  - **Legacy correction.** The roadmap's prior "v2's 100-distinct-value cap" claim is
    **incorrect**: verified legacy source shows `MAX_CATS = 100000` was the backend
    array-allocation/retention bound, while `maxCatsDisplayed = 100` was **only a UI display
    limit**; all discovered values were retained and usable (Adult 21,648 distinct; Internet
    Ads up to 781; Mushroom 12; EMAGE gene values ≈ 6,800). **M5 has no presentation/display
    limit.** The correction is recorded in `roadmap.md` M5 and `lineage.md` §1.
- **Why:** the strictly-greater rule plus prefix + `include` is what lets a *truncated* draft
  still round-trip to the complete schema: the include pass re-appends the tail deterministically,
  so a 100k-limit draft and an untruncated one converge on the same columns. Writing the notes
  even at zero truncations means every draft carries its own provenance for which limit produced
  it. Retaining ≤ `limit` distinct strings per attribute is the honest bound: probe cannot report
  an exact over-limit count without doing the very retention the limit caps.
- **Rejected:** a "greater-than-or-equal" boundary (would truncate an at-limit attribute that
  fits exactly); reporting an exact over-limit distinct count (requires unbounded retention:
  the thing the limit prevents); omitting the notes when nothing truncates (a draft would not
  explain its own limit); a machine-readable v1 truncation key (the strict reader rejects
  unknown keys, D-075) or a writer comment (the canonical writer emits none, D-075); a
  presentation/display cap like the legacy UI's first-100 limit (a UI concern, not a retention
  one); making the limit a `[binding]`/fingerprint input (it shapes a draft, not a
  conversion's identity).
- **Affects:** Discovery (the engine, probe options), Spec (`SpecWriter`); spec §4 / §7.1 /
  §10.1 / §10.3 / §10.6 / §17; roadmap M5; lineage §1. Cross-references D-068 (`include`
  precedent), D-071 (absent/empty `declared_domain`), D-098 (observed-domain + `include`
  calibration the recovery relies on); spec §10.6 include behavior.

### D-109: The general unbound streaming source session; adapter/engine split; package dependency direction; required EP-4 review

- **Status:** accepted (M5 pre-implementation audit)
- **Date:** 2026-07-18
- **Decision:** Discovery consumes a **general unbound streaming source session** whose stable
  boundary is **ordered schema + streamed cleaned/normalized records + source-shape
  information + cancellation + source diagnostics**, *not* a stream/file abstraction. The CSV
  wide/triple **adapters** (built from stream factories + read settings) implement that
  boundary **outside** the Discovery engine; future SQL/SPARQL sources could implement the
  same boundary without refactoring, but they are **examples of future adapters, not M5
  promises** (no connection/query semantics are designed now, none scheduled). Discovery
  **does not** tokenize, fabricate conversion bindings, open file paths, or reference
  `FcaBedrock.Conversion`. Allowed references: **Sources, Spec, Core, Diagnostics**,
  cycle-free under the current graph (Spec references neither Sources nor Discovery). Probe
  returns `Diagnosed<SpecDocument>`; the **caller** serializes via `SpecWriter` and owns all
  file output. Exact interface/member names were left to the implementation-time
  **public-API (EP-4) review**; the docs record the seam's shape and dependency direction, not
  signatures.
- **Why:** an abstraction over *schema + normalized records* (rather than over a byte
  stream) is what lets an in-memory or future SQL/SPARQL source feed Discovery with no CSV
  coupling; the determinism guarantee (D-112) must hold over a record sequence, not a file.
  Keeping Discovery out of `Conversion` and off the filesystem preserves the dependency graph
  and EP-13 purity boundaries.
- **Rejected:** coupling Discovery to a stream/file type (would block non-file sources and
  make determinism a file-byte property); letting Discovery reference `Conversion` or open
  files (breaks the seam and EP-13); designing SQL/SPARQL connection/query semantics now (EP-3:
  future adapters, unscheduled); pinning interface signatures in docs (reserved for the EP-4
  review).
- **Affects:** Discovery (engine + adapters), Sources (adapter boundary), Spec
  (`SpecWriter`); spec §7.1; roadmap M5/M7. Cross-references D-075/D-078 (the string-only
  `ISpecTextSource` host-seam precedent), D-098 (two-stage source bootstrap this reuses).

### D-110: Probe boundedness: per-attribute limit + three aggregate guards, deterministic accounting, hard-failure semantics, no spill machinery, inherited subject-metadata carve-out

- **Status:** accepted (M5 pre-implementation audit; refines D-095)
- **Date:** 2026-07-18
- **Decision:** per-attribute retention is bounded by the D-108 limit. Also, three
  deterministic **aggregate guards**, exposed as advanced probe options: (1) maximum
  discovered attributes; (2) maximum total retained distinct values; (3) maximum total
  retained value text. Their **defaults and accounting constants were chosen and pinned during
  the implementation review** against representative workloads (the motivating arithmetic:
  1,554 attributes × 100,000 values permits a theoretical 155.4M retained strings), not
  invented here. Guards use **deterministic logical accounting, never available machine
  memory** (EP-7/EP-11). An **aggregate breach** emits `ProbeLimitExceeded` (D-111) and **no
  draft**; aggregate pressure **never silently truncates** further attributes: only the
  per-attribute limit produces a usable, marked, truncated draft (D-108). Probe uses **no
  spill / count-sensitive calibration machinery**: it is set-based and idempotent (D-106).
  The seen-subject set that triple contiguity validation needs is the **inherited EP-16
  bounded-metadata carve-out** (the object-names class the converter already retains); probe
  invents **no fourth aggregate guard** for it.
- **Why:** probe reads data at the v1 target scale (D-007), so it needs the same bounded-memory
  discipline as calibration, but because its observation is set-based it needs none of the
  spill/merge machinery (D-095) that count-sensitive calibration does. Distinguishing a
  usable, marked truncation (per-attribute) from a hard failure (aggregate breach) keeps a
  runaway vocabulary from silently producing a partial draft that reads as complete.
- **Rejected:** memory-figure-based guards (non-deterministic; EP-7/EP-11); silently truncating
  attributes under aggregate pressure (a partial draft that reads as complete); reusing the
  quantile-accumulator/spill machinery (probe is set-based: it would buy nothing, D-095); a
  fourth aggregate guard for the seen-subject set (already covered by the EP-16 carve-out).
- **Affects:** Discovery (the engine, probe options); spec §7.1 / §16.4; roadmap M5/M8.
  Refines D-095; cross-references D-007 (target scale), EP-16 (bounded-metadata carve-out).

### D-111: Probe diagnostic governance: five future codes, two phase widenings, `ProbeSourceReadFailed` scope, cancellation is not a diagnostic, registry 70 → expected 75

- **Status:** accepted (M5 pre-implementation audit; refines D-067/D-085/D-099)
- **Date:** 2026-07-18
- **Decision:** M5 implementation is **expected** to add **five** diagnostic codes (recorded
  before implementation, while the registry was **70**):
  `ProbeSourceReadFailed` (Error: a stream/read failure; it **must not absorb structural
  subject errors**), `ProbeNoAttributesDiscovered` (Error: the D-107 no-valid-draft outcome
  for empty vocabularies), `ProbeAttributeNameAdjusted` (Warning, aggregated: the D-107
  naming adjustments; plain `column_N` synthesis alone is not a warning),
  `ProbeDomainTruncated` (Warning, aggregated: **more than** the per-attribute limit distinct
  values observed, the strictly-greater-than boundary of D-108; equality never truncates), and
  `ProbeLimitExceeded` (Error: any aggregate guard breached, no draft, D-110). Also,
  **two existing codes widen their phase cell** to add `probe` (both were `calibrate/emit`
  per D-099; the widening adds `probe`, it does not restate an emit-only baseline):
  `TripleSubjectNotContiguous | Error | probe/calibrate/emit` and
  `ObjectKeyValueInvalid | Error | probe/calibrate/emit`. Probe-phase `ObjectKeyValueInvalid`
  applies to **triple subjects** (both orderings); wide probe gains no object-key validation
  (D-106). Reusing the existing codes preserves one-condition/one-code ownership (D-067). The
  widenings add **no** enum member; with the five additions the registry moves **70 → expected
  75** when M5 implementation lands (M5 landed with 75). **Cancellation is not a diagnostic**
  (D-112).
- **Why:** the five codes cover exactly the outcomes probe can produce that a caller must
  distinguish (read failure, empty vocabulary, name adjustment, per-attribute truncation,
  aggregate breach); pinning their severities/aggregation before the sites land keeps M5 from
  inventing an ad-hoc taxonomy. Reusing `TripleSubjectNotContiguous`/`ObjectKeyValueInvalid`
  rather than minting probe-specific twins keeps one structural condition owned by one code
  across all three phases (D-067).
- **Rejected:** probe-specific twins of the two structural codes (one condition, two names;
  D-067); folding a structural subject error into `ProbeSourceReadFailed` (would hide the
  distinction between broken storage and invalid data); warning on plain `column_N` synthesis
  (routine, not an adjustment); a diagnostic for cancellation (D-112: cancellation leaves no
  artifact and no diagnostic); editing the enum or `DiagnosticCodeRegistryTests` before the
  sites existed (they landed with M5).
- **Affects:** Diagnostics (the five codes; two registry cells widen at M5), Discovery
  (the engine); spec §16.4. Refines D-067/D-085/D-099.

### D-112: Probe determinism and cancellation: record-sequence input, no ambient state, byte/diagnostic repeatability, no partial artifact; the M5 verification suite

- **Status:** accepted (M5 pre-implementation audit)
- **Date:** 2026-07-18
- **Decision:** probe's input is defined **generically**: the same ordered normalized record
  sequence + ordered schema + effective source settings + probe options ⇒ an **identical**
  `SpecDocument`, identical canonical TOML bytes, identical diagnostics, diagnostic
  **ordering** (first-occurrence), and bounded samples. This is **record-sequence**
  determinism, not file-byte determinism: it must hold equally for future non-file adapters
  (D-109). Probe reads **no** clock, ambient culture, environment variable, current directory,
  random source, or available-memory figure, and writes **no** tool version or timestamp
  (D-107). **Cancellation propagates with no diagnostic and no partial document, ever.** EP-7
  applies verbatim: the repeatability test ships with the path, in the same commit. The
  settled M5 test categories (verbatim or a faithful grouped equivalent): (1) repeatable bytes
  and diagnostics; (2) strict reread, resolve, and same-source conversion; (3)
  below/equal/above-limit: prefix/include/markers/notes; (4) default/custom/disabled missing
  tokens; (5) probe-vs-Calibrate ordered-domain cross-check; (6) wide/triple symmetry and
  grouped validation; (7) headerless/duplicate/blank/unusable naming and binding; (8)
  empty/header-only/all-missing/no-predicate cases plus unusable-subject rejection under both
  orderings; (9) every aggregate guard at and beyond its boundary; (10) one cleaned data pass
  and no grouped/count-sensitive pass; (11) an in-memory non-file session proving no CSV
  coupling; (12) cancellation/read failure with no partial document; (13)
  short/wrap-boundary/large/idempotent canonical-writer tests (D-113); (14) a compact
  ordered-distinct/truncation property test.
- **Why:** because Discovery's boundary is a record sequence (D-109), determinism must be
  defined over that sequence, not over file bytes; otherwise a future in-memory or SQL source
  could not inherit the guarantee. Enumerating the test categories now fixes M5's acceptance
  contract so the repeatability and no-partial-artifact properties ship with the code (EP-7),
  not after.
- **Rejected:** file-byte determinism (would not transfer to non-file adapters; D-109);
  reading any ambient state (a determinism hazard; EP-11/D-079); leaving a partial document on
  cancellation (a half-authored draft is worse than none); deferring the repeatability test
  (EP-7 forbids it for output-producing paths).
- **Affects:** Discovery (engine and tests); spec §7.1 / §17. Cross-references EP-7, D-004
  (planner determinism rules), D-079 (no-clock), D-082 (guarantee-over-stable-graph
  precedent), D-095 (bounded-memory determinism).

### D-113: Canonical-writer deterministic multiline wrapping for long top-level `declared_domain` arrays; private byte-pinned cutoff

- **Status:** accepted (M5 pre-implementation audit; **refines D-075**)
- **Date:** 2026-07-18
- **Decision:** `SpecWriter` remains the **sole** canonical serialization path; there is no
  probe-specific writer. Long **top-level `declared_domain`** arrays must not render as one
  unbounded line (the writer joined arrays inline at the time; a 16-value education domain
  yielded a 201-character line, and 100,000-value probe domains would be pathological). A
  top-level `declared_domain` array beyond a fixed cutoff renders **deterministically
  multiline, one escaped value per line**; **no other array wraps**: cut lists, `scale.order`,
  `value_groups`, `restrict_to`, and every nested or inline array keep their existing inline
  rendering. The cutoff is a **private canonical-writer formatting constant**, selected during the implementation
  review from representative output and then **byte-pinned** in code/tests, **not** a spec
  field, probe options entry, CLI/UI setting, fingerprint input, or `.editorconfig` concern,
  and **no numeric value is invented here**. Formatting never alters document semantics or
  fingerprints (hashes read the plan, never the TOML text, §14). The wrapping cutoff is
  distinct from the retention limits of D-108/D-110.
- **Why:** a probe draft is meant to be read and diffed in a text editor; a single
  hundred-thousand-value line defeats that. Wrapping is a pure serialization concern, so it
  belongs in the one canonical writer and must be byte-deterministic like everything else it
  emits, but the cutoff is a formatting constant, not a semantic knob, so it stays private
  and is pinned by a byte test rather than exposed.
- **Rejected:** a probe-specific writer (two serialization paths; EP-5); a configurable
  wrapping width (a speculative knob; EP-6); inventing the numeric cutoff in the docs
  (reserved for the implementation review, byte-pinned then); treating wrapping as
  fingerprint-affecting (hashes read the plan, not the text; §14); conflating the cutoff with
  the retention limits (unrelated concerns).
- **Affects:** Spec (`SpecWriter`); spec §2 / §14; roadmap M5. **Refines D-075.**

---

## M6 (templates + matchers) pre-implementation audit

These entries set the M6 contract for templates and matchers: selectors, naming, diagnostics and
architecture. Its normative text is spec §9, with clarifications in §6, §10.1, §10.7, §12.3, §13
and §16.4; D-120 and D-121 record the implemented naming, template and matcher surface.

### D-114: Template/matcher resolution: field-wise layering in declaration order, whole-value compounds, authored provenance, closed flat template surface

- **Status:** accepted (M6 pre-implementation audit; refines D-060(c); realizes
  D-078's deferred application clauses)
- **Date:** 2026-07-19
- **Decision:** applying a template is **deterministic syntactic sugar for
  ordinary per-attribute configuration**: it must behave exactly as though the
  same fields had been written on every affected attribute. The algebra:
  - **Precedence is §9.2's five tiers, unchanged:** built-ins < `[defaults]` <
    matching templates in declaration order < the attribute's directly named
    `template` < explicit attribute fields. This was already normative; the
    roadmap's four-term shorthand ("defaults < templates < matchers <
    per-attribute overrides") read a directly named template as *below* matchers
    and is corrected to cross-reference §9.2. **No normative change**: a direct
    reference is more specific than a pattern, so it wins.
  - **Every matching matcher participates, and templates layer field-wise.**
    All matchers whose selector matches an attribute take part, in declaration
    order; for a field authored by more than one matching template, the **last
    matching template that authors that field wins**. A later matching template
    that **omits** a field never erases a value an earlier one supplied.
  - **Presence, not value, drives the override.** Omission inherits; an explicit
    `false`, an authored empty collection, or a value equal to its own default
    still overrides a lower tier (the presence-tracked model of D-049/D-071).
  - **Compound fields are whole values.** `discretizer`, `scale`,
    `declared_domain`, `restrict_to`, `value_labels`, and the naming fields
    `display_name` / `formal_attribute_format` are replaced **entire** across
    precedence tiers, never deep-merged per leaf or per map key. This follows
    §13 rule 1's whole-value nested `[binding]` tables (D-078), not `[output]`'s
    per-leaf merge: a half-merged `scale` is exactly the incoherent hybrid that
    reasoning rejected.
  - **A winning template/matcher field counts as explicitly authored** for
    validation and provenance. Templates cannot make otherwise-invalid
    configuration valid, nor suppress its established diagnostic. Consequently a
    template-supplied `boundary = "strict"` with `direction = "ge"` over manual
    cut bins is invalid and reports `OrdinalBoundaryIncompatibleWithCuts`, just
    as the equivalent explicit attribute would, extending D-060(c)'s
    "explicitly authored, **per-attribute**" wording to any tier.
  - **Ordinal defaults fill last and stay defaulted.**
    `[defaults].ordinal_direction` / `ordinal_boundary` fill an omitted
    `direction` / `boundary` **only after** the winning effective scale is
    selected, retaining **defaulted**, not authored, provenance, so a defaulted
    boundary still never selects the operator over cut bins and never trips that
    check (D-060(c) unchanged for that path).
  - **A replaced value is semantically irrelevant**: it contributes neither
    behaviour nor validation.
  - **Application covers every attribute, including `include = false`.** D-049
    dormancy is preserved: emitted shaping stays dormant while excluded, while
    live restriction/filter configuration still operates (§10.4/D-076).
  - **The effective attribute is then validated exactly like its equivalent flat
    declaration**, by the existing condition owners; no §16.4 code changes phase
    and no condition gains a second owner.
  - **Closed, flat v1 template surface.** A `[[template]]` body may carry exactly
    `include`, `discretizer`, `scale`, `declared_domain`, `restrict_to`,
    `value_labels`, `missing_policy`, `unknown_value_policy`, `display_name`, and
    `formal_attribute_format`. `name`, `source`, `description`, and `template`
    are **not** legal there; a `template` key inside `[[template]]` remains the
    existing wrong-table-key error (`SpecKeyUnrecognized`, D-075/D-078). **v1
    templates cannot reference or inherit from another template**: no template
    graph, parent lookup, chain precedence, or cycle diagnostics. Template
    inheritance may be reconsidered after v1 only if a concrete caller justifies
    reopening the grammar and a graph-resolution contract (EP-3).
  - **Equivalence.** Semantically equivalent flat, materialized,
    template/matcher-authored, and `extends`-composed specs resolve to the same
    effective attributes, the same plan, the same three fingerprints, and
    byte-identical output.
- **Why:** §9.2 said "Matchers run in declaration order; the last match wins for
  any given attribute" beside a tier list reading "Matcher templates (in
  declaration order)". The first sentence reads as whole-template *selection*,
  the second as *layering*, and the two diverge observably: a partial
  `missing_policy`-only template applied over a scaling template either composes
  a valid two-column context (layering) or fails `AttributeScalingMissing`
  (selection). Two competent implementers would have shipped incompatible
  resolvers. Layering harmonizes both sentences, matches the tier model, and is
  what makes partial templates composable, the property that makes bulk editing
  useful. The rest of the algebra (granularity, presence, provenance, dormancy)
  was equally unstated and equally observable in columns, diagnostics, and
  fingerprints; the governing principle that settles every case at once is that
  configuration arriving through a template behaves as though written on the
  attribute.
- **Rejected:** whole-template selection (v2's Repeat-To copied whole
  configurations, which argues for it, but it forecloses partial-template
  composition and would force the tier list's plural into a singular); per-leaf
  merging inside `scale`/`discretizer`/`value_labels` (composes incoherent
  hybrids; the D-078 argument); letting omission clear an earlier value (no
  presence state can express "erase", and it would make declaration order
  destructive); treating template-supplied config as *defaulted* (a
  template-authored `strict` boundary would be silently discarded and the
  geometry would render a different operator: a silent wrong-output path);
  nested templates (`extends` already composes specs and matchers already share
  one template; no v1 caller, EP-3); skipping excluded attributes (a
  template-supplied `restrict_to` is live on a filter-only attribute, §10.4/
  D-076).
- **Affects:** spec §6 / §9.1 / §9.2 / §12.3 / §13; Spec (`SpecResolver`
  application step at M6), Core (effective per-attribute configuration only).
  Refines D-060(c); realizes D-078's deferred application clauses; pairs with
  D-115…D-119.

### D-115: Matcher selector contracts: whole-logical-name regex, resolved zero-based inclusive index range, exactly one selector

- **Status:** accepted (M6 pre-implementation audit; realizes D-078's "M6 owns
  pattern semantics and validation")
- **Date:** 2026-07-19
- **Decision:** a matcher **configures already-declared logical attributes**; it
  never synthesizes attributes, discovers source data, or inspects rows (D-119).
  Its two selectors:
  - **`name_regex` targets the complete logical `attribute.name`**, never a
    source header, predicate text, `display_name`, or a rendered formal name.
  - **Matching is whole-name:** the pattern must match the entire logical name.
    Explicit anchors stay legal but are redundant when they express the same
    boundary. This **deliberately diverges** from `value_groups.pattern`
    (§11.6/D-090/D-104), which is partial/unanchored: a selector is an identity
    test over a configuration-bounded name set, whereas a value pattern is a
    content search over data. The divergence is recorded here so the two regex
    surfaces are not "harmonized" later by accident.
  - **Engine:** .NET regex with `RegexOptions.CultureInvariant`, **case-sensitive
    by default**, authored inline options (e.g. `(?i)`) honored, and
    `Regex.InfiniteMatchTimeout` passed **explicitly**. The constructor
    overloads that omit a timeout inherit the host's ambient
    `REGEX_DEFAULT_MATCH_TIMEOUT`, which would make the same spec host-dependent
    on the exception channel (the identical D-104 reasoning, EP-7/EP-14). Each
    pattern is **compiled once** and reused across attribute names.
  - **Pattern validity:** non-empty and compilable. An empty or uncompilable
    pattern is `SpecFieldInvalid` at spec parse; there is **no dedicated
    regex-error code** (the §11.6 stance).
  - **`source_index_range = [lo, hi]` is inclusive and zero-based**, over the
    **resolved physical wide-source index**, the same index space as
    `attribute.source.index` (§10.2).
  - It is evaluated **after ordinary header/schema binding**, so a name-bound
    source participates normally. Failing to supply a schema for a name-bound
    source remains the existing source-binding failure (`SourceBindingInvalid`);
    **no matcher-specific duplicate condition is introduced.** There is no
    circularity, because `source` can never arrive from a template (the closed
    field list, D-114): binding always precedes application.
  - **Every declared logical attribute bound to an in-range physical index
    matches**, including several logical attributes bound to one physical column
    (D-033's source repeat). A range never synthesizes attributes.
  - **Shape:** exactly two TOML integers satisfying `0 ≤ lo ≤ hi`. Wrong arity,
    a non-integer, a negative endpoint, or reversed endpoints are
    `SpecFieldInvalid` at parse.
  - **Over-coverage is legal.** An endpoint beyond the available source width
    simply has no attribute to match: the Internet-Ads idiom of writing a
    generous range stays valid. A wholly unmatched matcher warns (D-116); it does
    not error.
  - **A range is incompatible with `shape = "triple"`** (predicate sources have
    no column index) and is an **Error at spec resolve, one per incompatible
    matcher** (D-116 owns the condition and its name).
  - **Exactly one selector per matcher.** A `match` table authors `name_regex`
    **or** `source_index_range`; **both or neither** is `SpecFieldInvalid` at
    parse. A matcher must also reference a template; a missing `template` key is
    `SpecFieldInvalid` at parse.
  - **Cost:** approximately attributes × matchers, two integer comparisons per
    attribute for a range. Selector evaluation is a one-time
    configuration/schema-bounded resolution step; **no data row is read** to
    evaluate a selector, and no matcher state enters Core, calibration, or
    per-row emission (D-118).
- **Why:** §9.2 offered one hand-anchored example and an "inclusive" comment,
  and §10.1 promised "full regex matching", which reads either as whole-string
  matching or as full regex *syntax*. Under the partial reading, `feature_\d+`
  matches `xfeature_12x` as well as `feature_12`, so the template silently
  configures an attribute the author never named. The matched string is
  schema-affecting configuration, so its match rule is normative surface, not
  implementation freedom. `source_index_range` was undefined beyond the happy
  path on every axis that changes which attributes get configured: index space,
  name-bound sources, triple, endpoint validation, authored arity, and whether
  two selectors mean AND, OR, or an error.
- **Rejected:** partial/substring selector matching (§11.6 consistency argues for
  it, but accidental-substring application is the costlier silent error, and
  §10.1 already promised whole-name); culture-sensitive matching (EP-12); a finite
  wall-clock match timeout (machine-speed dependent: a worse determinism hazard
  than a slow pattern, and matching runs over a config-bounded name set);
  `RegexOptions.NonBacktracking` (silently narrows the regex language relative to
  `value_groups`); one-based or half-open ranges; clamping a range to the source
  width; erroring on over-coverage (breaks the generous-range idiom);
  authored-`source.index`-only matching (would silently never match a name-bound
  column); a matcher-specific schemaless failure condition (name binding already
  needs the schema; one condition, one owner); AND or OR semantics for two
  authored selectors (ambiguous; exactly one is the clear contract).
- **Affects:** spec §9.2 / §10.1; Spec (`SpecResolver` selector evaluation at
  M6), Diagnostics (existing `SpecFieldInvalid` sites; the triple-incompatibility
  condition per D-116). Realizes D-078's deferred pattern semantics; pairs with
  D-114/D-116/D-118.

### D-116: M6 diagnostic matrix: permanent conditions, per-effective-attribute granularity, deterministic order, transitional retirements

- **Status:** accepted (M6 pre-implementation audit; refines D-067); its deferred enum names and
  registry rows settled by D-120 and D-121
- **Date:** 2026-07-19
- **Decision:** every new invalid state M6 introduces gets an owner (phase,
  severity, and granularity) before any site exists.
  - **Static authored shape stays parse-owned under the existing
    `SpecFieldInvalid`; no new parse code is minted.** That set is: an invalid
    template-`id` grammar (§10.1's `[A-Za-z_][A-Za-z0-9_-]*`), a matcher with no
    `template` reference, an empty or uncompilable `name_regex`, a malformed
    `source_index_range` (arity, non-integer, negative, reversed), both-or-neither
    selector, and the D-117 naming-shape rejections (format-grammar violations,
    the empty format, and CR/LF or emptiness where D-117 forbids it).
  - **Seven new permanent conditions**, recorded by condition/phase/severity/
    granularity. **Their public enum names and their final §16.4 registry rows
    are deliberately deferred to the M6 implementation-surface review (EP-4)**
    (D-120 and D-121 later minted the names and rows):
    1. a `[[template]]` without `id`: **spec resolve**, Error, one per template;
    2. a **duplicate** template `id` in the composed document: **spec resolve**,
       Error, one per extra declaration in composed template order (the
       `AttributeNameDuplicate` granularity, D-080). This closes D-078's
       "within-file duplicate ids … unvalidated until M6";
    3. an **unknown template reference**: **spec resolve**, Error, one per
       referencing matcher or attribute site; an attribute site carries the
       `AttributeName` location, and a matcher/template diagnostic identifies its
       declaration and id/reference deterministically;
    4. `source_index_range` under `shape = "triple"`: **spec resolve**, Error,
       one per incompatible matcher (D-115);
    5. a matcher selecting **zero attributes**: **spec resolve**, Warning, one
       per matcher (the `RestrictToValueNotInDomain` typo-catcher pattern);
    6. a **fully-shadowed** matcher: **spec resolve**, Warning, one per matcher;
    7. an **invalid rendered formal-attribute name** (empty, or containing CR or
       LF): **plan**, Error, per affected logical attribute, alongside
       `FormalAttributeNameCollision` (D-117).
  - **"Fully shadowed" is a merge-level determination** (defined normatively in
    §9.2): a matcher that selects at least one attribute but, on **every**
    selected attribute, has **every field its template authors** overridden by a
    higher-precedence source (a later matching template authoring the same
    field, the attribute's directly named template, or an explicit attribute
    field). It is **independent of `include = false` dormancy (D-049)**: a matcher
    whose fields *win* on an excluded attribute is **not** fully shadowed, even
    though the winning configuration is dormant while the attribute is excluded.
  - **Applied templates are validated through their effective attributes**, under
    the established owners and codes of the equivalent flat configuration.
    **Structured validation diagnostics are one per affected effective attribute,
    in attribute declaration order, never a per-template aggregate**: an invalid
    effective attribute can be assembled from several matching templates plus
    higher tiers, so the attribute is the only sound owner. A message **may**
    identify all contributing template/matcher sites, and a future CLI/UI may
    group identical diagnostics for presentation without changing the structured
    diagnostic contract.
  - **An unused template is semantically dormant.** Parse-level shape and
    format-grammar checks still fire on its authored body, but effective
    combinations such as missing scaling or an incomplete scale are validated
    **only if the template applies**. §9.2's inertness sentence becomes end-state
    normative text rather than M2-transitional framing.
  - **Deterministic ordering.** Resolve-family order is: template identity →
    matcher reference/shape compatibility → attribute references →
    effective-attribute validation → zero-match/fully-shadowed warnings, with
    declaration or logical-attribute order preserved **within** each family.
    Parse diagnostics retain reader source-position order.
  - **Retirement schedule.** `TemplateMatcherNotImplementedV1` is **removed
    entirely** when the application path lands. The **naming portion** of
    `SpecSurfaceNotYetSupported` retires when the `display_name` /
    `formal_attribute_format` carriers land; that code then carries **only**
    `value_type = "date"` until the D-038 carrier. Both §16.4 rows and both enum
    members stayed live until then (D-120 and D-121 retired them).
- **Why:** D-067's regime (each code owned by exactly one phase, one condition
  one code, aggregating seams) is the project's diagnostic contract, and
  retrofitting owners after sites exist is precisely the drift D-080 had to clean
  up. Every row above is a state a hand-authored M6 spec can reach, and several
  (a fully shadowed matcher, an unused template's invalid body) would otherwise
  be silent. The registry count and its lock test also move at M6, so the
  schedule has to be settled before the slice is planned.
- **Rejected:** per-`(template, condition)` aggregation with a bounded attribute
  sample (attractive at Internet-Ads scale, but unsound in general: an invalid
  effective attribute can draw from several templates plus higher tiers, so no
  single template owns it; presentation-layer grouping solves the noise without
  weakening the structured contract); silence for a fully-shadowed matcher (the
  natural probe-draft-plus-matcher journey would then produce byte-identical
  output with no signal at all); an Error for a zero-match matcher (a pattern may
  legitimately over-cover, D-115); validating an unused template as a
  hypothetical attribute (it has no `source` and no attribute context;
  dormancy is the D-049 precedent); minting enum names or final registry rows
  before their sites existed (they would strand members no site raises; EP-3 and
  D-085's enum-timing rule); a dedicated regex-error or format-error code
  (`SpecFieldInvalid` already owns malformed fields; D-090/EP-14).
- **Affects:** spec §9.2 / §16.4; Diagnostics (seven conditions at M6, names at
  the EP-4 review; two retirements at M6). Refines D-067; closes D-078's deferred
  duplicate-id validation; pairs with D-114/D-115/D-117.

### D-117: Naming: closed placeholder grammar, single-pass rendering, authored and rendered validity

- **Status:** accepted (M6 pre-implementation audit; refines D-037(a)/D-074/D-092)
- **Date:** 2026-07-19
- **Decision:** `formal_attribute_format` gets a grammar, and rendered names get
  a validity rule.
  - **Closed, case-sensitive placeholder set, exactly §10.7's documented five:**
    `{name}`, `{column}` (the documented **alias** of `{name}`),
    `{display_name}`, `{value}`, and `{scale_op}`. No other placeholder exists;
    in particular there is **no `{scale}`**.
  - **Escaping and substitution.** `{{` renders a literal `{` and `}}` renders a
    literal `}`. Parsing and substitution are a **single left-to-right pass**:
    text substituted from `name`, `display_name`, a value, or a label is **never
    rescanned** as format syntax or brace escaping. This is required for
    determinism, because §10.1 explicitly allows a `name` to contain brace-like
    text.
  - **Three distinct validity rules**, deliberately separate because
    conflating them would reject valid formats:
    1. an authored **`display_name`** must be **non-empty** and contain **neither
       CR nor LF**;
    2. the **`formal_attribute_format` string as a whole** must be **non-empty**;
       the empty format, an unknown placeholder, an empty placeholder, and an
       unmatched or malformed brace are each `SpecFieldInvalid` at spec parse;
    3. **literal text within a format** must contain **neither CR nor LF**, but
       **may be empty**, so `"{name}"`, `"{value}"`, and `"{name}{value}"`,
       whose literal spans are empty, are **valid**. No non-empty requirement
       attaches to a literal segment.

    No dedicated format diagnostic is introduced; all of the above are
    `SpecFieldInvalid`.
  - **Checked wherever the format is authored** (`[defaults]`, `[[template]]`,
    and `[[attribute]]`), **including unused templates and excluded attributes**:
    shape is parse's concern and dormancy is semantic (the D-049 split, exactly
    as `value_groups` patterns already behave).
  - **An explicit format is a total override** for **every** formal attribute the
    logical attribute emits, **including its missing-value column**, which
    therefore renders through the format, with `{value}` resolving to the literal
    `missing` per §10.7's table, instead of the default literal
    `{column}-missing`. This extends D-074, whose rendered-name clause describes
    the **default** path; the missing column's **position** and canonical
    identity are unchanged. Without an explicit format the scale-specific
    defaults of §10.7/D-037(a) continue to apply.
  - **`{value}` uses `value_labels` whenever labels are live**
    (`Discretizer.ConsultsValueLabels`, D-049), including, for a **dichotomic**
    scale, the **labelled** `true_value` rather than the raw authored value (so
    `true_value = "t"` with label `bruised` renders `{column}-{value}` as
    `bruises?-bruised`). Under a discretizer that does not consult labels the raw
    bin label falls out correctly.
  - **Rendered-name validity: a plan-time backstop.** After final substitution
    every rendered formal-attribute name must be **non-empty** and contain
    **neither CR nor LF**. Violation is a permanent **plan Error** alongside
    `FormalAttributeNameCollision`, reported per affected logical attribute
    (D-116). The backstop matters beyond the M6 surface: it also covers
    CR/LF arriving from **raw values, calibrated domains, and `value_labels`**,
    a route reachable before M6. **The shared plan fails**, deliberately blocking
    `.dat` emission as well as `.cxt`, consistent with existing formal-name
    collision behaviour even though `.dat` serializes no names.
  - **Exporters never sanitize**, replace, escape, or independently validate a
    name (EP-15).
  - **No broader character policy.** Only *empty*, *CR*, and *LF* are settled for
    v1; no wider C0/Unicode-control prohibition is adopted by this entry.
  - **Fingerprint reach.** A naming change moves rendered `.cxt` names, `.cxt`
    bytes, and `cxt_output_fingerprint` **only**; canonical schema identity,
    `.dat` bytes, and `dat_output_fingerprint` never move on naming alone
    (§14/D-035/D-051). A naming setting that does not change a rendered name is
    byte- and hash-neutral.
- **Why:** §10.7 listed five placeholders and resolved `{value}` per
  formal-attribute kind, but never defined the *grammar* (whether the set is
  closed, whether an unknown or case-variant placeholder is an error or literal
  text, how to write a literal brace, whether substitution recurses, whether an
  empty format is legal), and every one of those answers changes `.cxt` bytes and
  `cxt_output_fingerprint`. Separately, nothing constrained `display_name` or the
  *rendered* name at all: `.cxt` is a line-oriented format (§18.1) whose writer is
  dumb by law (EP-15), so a rendered name containing a newline adds a phantom line
  (the attribute count and the name block disagree and every downstream consumer
  misparses) with no diagnostic, since the canonical fingerprint encoding escapes
  control characters happily (D-077). M6 is what first lets author-controlled text
  reach rendered names, so the fence belongs here.
- **Rejected:** treating an unknown placeholder as literal text (turns every typo
  into a silent rendered-name change or a confusing collision, where a closed set
  fails fast at parse); a case-insensitive set; backslash escaping (`{{`/`}}` is
  the conventional choice that keeps every literal writable); recursive or
  multi-pass substitution (a brace-bearing `name` would re-expand: a determinism
  hole §10.1 makes reachable); **requiring literal segments to be non-empty**
  (that would reject `"{name}"` and every adjacent-placeholder format; the
  non-empty rule belongs to the whole format string and to `display_name`, not to
  a literal span); the raw, unlabelled dichotomic `{value}` (§10.8's whole purpose
  is that labels are how raw values appear in formal-attribute names); constraining
  only the inputs (raw values can inject CR/LF (an RFC 4180 quoted field may
  legally contain a line break), so an input-only rule is insufficient);
  constraining only the rendered name (an authored multiline `display_name`
  deserves a parse-time error, not a late plan failure); letting the exporter
  sanitize (EP-15); adding `{scale}` or dropping `{name}` (neither is defined by
  §10.7: an undefined-but-legal placeholder would let two conforming
  implementations emit different bytes, and dropping a documented one would reject
  valid specs).
- **Affects:** spec §10.1 / §10.7 (its conditions register through D-116); Spec
  (the naming carriers at M6), Core (`ConversionPlanner` rendering and the
  validity guard), Export (unchanged; EP-15). Refines D-037(a)/D-074/D-092;
  pairs with D-116/D-118.

### D-118: M6 application site and architecture: resolver-seam application, template/matcher-free Core, planner-owned naming

- **Status:** accepted (M6 pre-implementation audit; makes D-078's "templates
  never resolve into Core" permanent)
- **Date:** 2026-07-19
- **Decision:** where M6 runs, and what crosses each package boundary.
  - **Application site.** Template/matcher application is a step **inside
    `SpecResolver.Resolve`**, after `extends` composition and after the source
    binding a selector needs, and **before** effective-attribute validation and
    Core construction. The resolver applies the D-114/D-115 precedence and
    provenance rules, constructs the equivalent flat effective attributes, and
    invokes the **same** validation owners as explicit configuration, so no §16.4
    code moves phase and no condition gains a second owner.
  - **Composition is unchanged.** `SpecComposer` remains an **authored
    document→document** transformation (D-078): it does not materialize matcher
    results and does not change the canonical round-trip surface. Same-`id`
    template replacement under `extends` is **late-bound**: an inherited base
    matcher resolves against the **composed winning** template; base and derived
    matchers otherwise retain composed order (§13 rules 3–4). This documents what
    compose-then-resolve already produces; no merge rule changes.
  - **Core never learns templates.** `BedrockSpec` / `ResolvedSpec` /
    `AttributeSpec` carry only effective per-attribute configuration and resolved
    naming inputs. Template ids, matcher selectors, compiled regexes, ranges, and
    precedence syntax **never enter Core**, and `ConversionPlanner` needs no new
    guard.
  - **New public document surface** (the EP-4 review list; internal layouts stay
    free): `display_name` and `formal_attribute_format` on `AttributeSection` and
    `TemplateSection`, plus `formal_attribute_format` on `DefaultsSection`, with
    the corresponding `SpecSurfaceNotYetSupported` retirements (D-116) and
    canonical-writer emission in spec presentation order (D-075). Core's public
    naming addition is limited to the resolved values `ConversionPlanner`
    consumes.
  - **The planner owns final name rendering and the validity guard**, including
    applying an explicit total format to the missing-value formal attribute
    (D-117). **Exporters remain decision-free serializers** (EP-15).
  - **No fingerprint-encoder change and no `fp_format` bump.** Template/matcher
    syntax is never a fingerprint input; fingerprints consume only the resolved
    plan, and naming reaches only the existing `rendered_names` array in the
    `cxt` block (D-069/D-077). Resolved-equivalent inline, materialized,
    matcher-driven, flat, and composed specs therefore hash identically.
  - **Boundedness.** Matcher evaluation uses configuration and schema metadata
    only, costs approximately attributes × matchers, compiles each regex once,
    and **never opens or scans source rows** (§7 phase 1). Resolve stays callable
    with no source open, and no matcher work occurs during Calibrate or per-row
    emission.
- **Why:** D-078 deliberately deferred the shared config record to "when M6
  applies templates", D-067/D-080 made the seam the enforced validation entry
  point, and EP-4 requires the public shape first. Left unpinned, the obvious
  failure modes are template state leaking into `ResolvedSpec`, validation running
  *before* application (checking authored rather than effective configuration),
  and matching acquiring a data dependency, each of which would quietly undo an
  accepted decision rather than announce itself.
- **Rejected:** applying templates during **composition** (composition runs per
  `extends` step and yields an *authored-surface* document the writer
  round-trips; an applied document is no longer authored surface (it would change
  what `SpecWriter` emits and what the flat-file `AttributeNameDuplicate`-style
  diagnostics see), and application needs the *composed* matcher list anyway, so
  it is inherently resolve-time); a Core reject-carrier or any template/matcher
  type in Core (speculative surface removed again at M6; EP-3); validating
  authored rather than effective configuration; reading data rows to evaluate a
  selector (§7 phase 1); a fingerprint-encoder change for template syntax (only
  resolved semantics are inputs; §9.2/§14).
- **Affects:** Spec (`SpecResolver` application step, document naming carriers),
  Core (resolved naming inputs, `ConversionPlanner` rendering + guard), Export
  (unchanged); spec §7 / §9.2 / §13. Makes D-078's Core boundary permanent;
  realizes D-066/D-067 at the M6 seam; pairs with D-114…D-117.

### D-119: M6 exit restated: one self-contained Ads spec, no attribute synthesis; the verification floor

- **Status:** accepted (M6 pre-implementation audit)
- **Date:** 2026-07-19
- **Decision:**
  - **The exit is restated; the "<50 lines of TOML" target is withdrawn.** §2
    requires at least one `[[attribute]]` with `name` + `source` per logical
    attribute, and matchers **configure** rather than create, so roughly 1,500
    boolean columns cost roughly 4,700 lines of declaration under *any*
    conforming spec. The old target could not be met by anything the format allows,
    by two orders of magnitude.
  - **The accepted exit artifact is one self-contained spec** carrying the
    complete generated attribute inventory plus a template/matcher expressing the
    repeated boolean scaling policy **once** over the already-declared
    attributes, converting end-to-end. **The exit measures elimination of
    repetitive per-attribute manual curation, not total file length.**
  - **Matchers configure declared attributes only**: they never synthesize
    attributes, never perform implicit discovery, and never inspect data rows.
    **Attribute synthesis from a source schema is rejected:** it would violate
    §2's cardinality rule, make the column set depend on the source header/width
    (breaking §14's "fully determined by its own text" and the spec-first
    workflow), and blur D-003's discovery boundary; its only proposed caller
    was this exit criterion (EP-3).
  - **Curation from an M5 probe draft.** A draft authors `discretizer` and
    `scale` explicitly on **every** attribute (D-107), and explicit per-attribute
    fields are tier 5 (above matcher templates at tier 3), so a matcher laid
    over an uncurated draft is fully shadowed and changes nothing. A curation
    tool must therefore either **remove or replace** the shadowing explicit
    fields, or **materialize** the boolean configuration into every selected
    attribute as v2's "Repeat To" did. Both are valid one-file representations,
    and the fully-shadowed Warning (D-116) makes the uncurated case visible
    rather than silent.
  - **Declarative ≡ materialized.** Equivalent declarative and materialized specs
    must resolve to the same plan, fingerprints, and output, so a future editor
    may materialize a bulk edit rather than preserve matcher syntax.
  - **Multi-file `extends` composition remains optional**, not the default
    workflow, and **no minimal/bare probe mode is required**; D-107's
    self-documenting draft is unchanged.
  - **v2's index-range "Repeat To" is authoring precedent, not a ruling on
    selector semantics**: it is no argument against persistent `name_regex`
    selection; D-115 settles both selectors on their own merits.
  - **The verification floor.** It is the M6 completion condition, not a
    prescribed test-file layout, and each behaviour ships with its verification in
    the same slice (EP-7):
    1. every precedence tier pair; multiple matching templates; authored presence
       (explicit `false`, authored `[]`, authored-equals-default); whole-value
       compound replacement; boundary and ordinal-defaults provenance, including
       the template-supplied straddling-boundary Error; excluded attributes
       (dormancy plus live `restrict_to`); and the directly-named-template versus
       matcher-template tier scenario;
    2. inline ≡ materialized ≡ template/matcher ≡ flat ≡ composed equivalence
       (identical resolved attributes, plans, all three fingerprints, and bytes),
       including base/derived matcher order and same-`id` late retargeting;
    3. regex whole-name targeting, case/culture/inline-option behaviour, escaping,
       and invalid patterns; inclusive range endpoints, repeated source bindings,
       name-bound binding, legal over-coverage, malformed ranges, zero matches,
       and triple rejection;
    4. missing, invalid, and duplicate template ids; unknown references;
       unused-template dormancy; per-effective-attribute granularity, location,
       and severity; the deterministic family ordering including both warnings;
       and diagnostic determinism (same spec ⇒ same ordered diagnostics);
    5. exact rendered names for nominal, dichotomic, ordinal, and missing columns
       across `[defaults]`, template, and explicit sources; live `value_labels`
       including the labelled dichotomic `{value}`; numeric rendering (D-092);
       **all five placeholders, `{name}` included and `{scale}` absent**; brace
       escaping; total overrides; malformed formats; invalid final names; and
       collisions;
    6. the fingerprint/byte neutrality-and-change matrix below, including an
       **effect-changing** `[defaults].formal_attribute_format` case;
    7. round-trip of every new authored carrier and presence state through
       canonical write→read→write, including authored-equals-default values and
       template naming keys;
    8. all existing golden, canonical-fingerprint, native-conformance, and
       diagnostic-registry locks green, with the transitional retirements
       asserted;
    9. the accepted one-file Internet-Ads workflow end-to-end, without repetitive
       manual edits;
    10. the document→resolver→Core boundary: template/matcher-free Core,
        planner-owned naming, dumb exporters, schema-only bounded matcher
        resolution, and no source-row inspection.
  - **The neutrality/change matrix.** Adding a valid **unused** `[[template]]`, a
    matcher that **matches nothing**, or a `display_name` **no format
    references** leaves all three fingerprints and both outputs unchanged (the
    unmatched matcher adds only its Warning). Inline configuration and the same
    configuration via template+matcher are identical, as are a flat spec and its
    equivalent `extends` split. An **effect-changing** `formal_attribute_format`
    (per-attribute or from `[defaults]`) changes `cxt_output_fingerprint` and
    `.cxt` bytes **only**. A template-supplied **semantic** change that adds a
    formal column (e.g. `missing_policy = "as_attribute"`) moves all three
    fingerprints and both outputs, through the resolved plan, exactly as the
    equivalent flat edit would. Existing pre-M6 canonical JSON/SHA pins, native
    conformance outputs, and v2 golden bytes are unaffected, because no existing
    fixture uses the M6 surface.
- **Why:** this is the milestone's own exit condition, and as written it could
  not be honestly met, which also made the largest scope fork in M6 (bulk-edit
  configuration versus schema-driven attribute creation) look open when it is
  not. Restating the exit as a curation-delta claim keeps the honest promise
  (roughly 1,500 attributes configured without roughly 1,500 manual edits) while
  leaving §2, §14, and D-003 intact. Agreeing the verification floor now prevents
  the exit being declared on untested precedence (EP-7/EP-21).
- **Rejected:** keeping the "<50 lines" target (unmeetable: the arithmetic is
  exact); matchers synthesizing attributes from the source schema (§2/§14/D-003,
  and EP-3: this exit was its only proposed caller); adding a probe "bare/minimal
  draft" mode to feed the demonstration (reopens D-107 and the public Discovery
  contract, and is unnecessary once curation may materialize or de-shadow);
  treating the exit as satisfied only by a two-file `extends` split (composition
  is optional; the accepted artifact is one self-contained spec).
- **Affects:** `roadmap.md` M6 (exit wording + the §9.2 precedence
  cross-reference), spec §9.2; the M6 implementation slices' test plan.
  Pairs with D-114…D-118.

## M6 Slice A (naming: carriers, grammar, rendering, plan guard)

### D-120: Naming executable: additive carriers, the `NameFormat` grammar owner, `FormalAttributeNameInvalid`, centralized parse ordering, and the `MergeDefaults` extension

- **Status:** accepted (M6 Slice A implementation; realizes D-116(7)/D-117/D-118); the interim
  `TemplateMatcherNotImplementedV1` reject it left in place retired by D-121
- **Date:** 2026-07-20
- **Decision:** `display_name` and `formal_attribute_format` become **executable**
  on attributes and `[defaults]`, and are carried and parse-validated on
  templates. The implementation-surface choices D-116/D-117 deferred to this slice:
  - **Additive, non-positional carriers.** `AttributeSection` and
    `TemplateSection` gain `DisplayName` / `FormalAttributeFormat`, and
    `DefaultsSection` gains `FormalAttributeFormat`, all as
    `public string? { get; init; }`, the D-087 precedent. Every existing
    positional constructor, construction site, and record deconstruction is
    untouched, and the canonical key order stays owned by `SpecWriter` rather
    than by record field order. Presence is the existing convention: null =
    omitted, non-null = authored; empty display names and empty formats do not
    survive parse, so no new presence machinery exists.
  - **One grammar owner:** the new public `FcaBedrock.Core.Spec.NameFormat`
    (`Text`, `static TryCreate`, no public constructor; token model and
    rendering internal to Core). The Spec reader validates every authored format
    through it and the planner renders through it, so the two cannot disagree
    about which formats are legal or what they produce (EP-5). Parsing is one
    left-to-right pass into literal/placeholder tokens and rendering walks those
    tokens, which makes "substituted text is never rescanned" **structural**
    rather than a property two code paths maintain.
  - **Core's naming surface** is exactly two additive `init` properties on
    `AttributeSpec`: `DisplayName` (defaulting to `Name`, so every pre-M6
    construction site stays valid unchanged) and `NameFormat?` (null = the
    scale-specific defaults). Nothing else crosses into Core: no template ids,
    selectors, ranges, or precedence syntax (D-118). `ResolvedSpec.Create`
    backstops `DisplayName` non-null/non-empty for hand-built graphs; the format
    needs no re-check, because `NameFormat` cannot be constructed except through
    its validating factory (EP-10).
  - **One new diagnostic: `FormalAttributeNameInvalid`** (Error, plan): the
    seventh D-116 condition, the only one whose site exists at Slice A. Its
    representation is **pinned**: one aggregated diagnostic per affected logical
    attribute in plan order with `AttributeName` location, carrying the offending
    count and at most three samples **in render order**, each wrapped in double
    quotes with exactly four escapes (`\`→`\\`, `"`→`\"`, CR→`\r`, LF→`\n`) and a
    `(+N more)` tail only when truncated. Byte-identical messages across runs and
    machines are the point (EP-7). Invalid names still register in the id maps, so
    ids and collision reporting stay exactly what a valid run would produce, and
    the shared plan fails, blocking `.dat` as well as `.cxt` (§10.7).
  - **Centralized parse ordering.** D-116's "parse diagnostics retain reader
    source-position order" is pinned as **one policy at the `SpecReader`
    boundary**: the collected semantic diagnostics are ordered once by
    `(Line, Column, emission ordinal)`. Including the ordinal in the comparison
    makes the order **total**, so equal-position diagnostics keep their relative
    order independently of sort stability, and span-less document-level
    diagnostics sort first in emission order. Individual readers own no ordering
    and future readers inherit the policy automatically. Phase separation is
    untouched: TOML syntax errors stay terminal, and phase-1 parser warnings stay
    ahead of every semantic diagnostic. The helper is **`internal` rather than
    private, as a deliberate test seam** (EP-6): the span-less branch is defensive
    (`TomlReadContext` always attaches a span, so no authored document reaches it
    through `Read`), and a two-element equal-position case survives even an
    unstable sort, so both guarantees are exercised directly with constructed
    diagnostics rather than by a test that cannot fail. Production behaviour is
    unchanged; `Read` remains the only caller.
  - **`SpecComposer.MergeDefaults` gains the format explicitly.**
    `[[attribute]]`/`[[template]]` composition is whole-section replacement
    (§13 rules 3/5), so the new init properties ride along with no composer
    change; `[defaults]` composes **per field** (rule 2), so a base-supplied
    format would silently vanish the moment a derived `[defaults]` authored
    anything else. The `derived ?? base` line is the D-087 `MergeDat` precedent.
  - **Retirement.** The closed D-075 deferred-**key** sets
    (`DefaultsDeferredKeys` / `AttributeDeferredKeys`) are **deleted**, along with
    `TomlTableCursor.Finish`'s now-unreachable deferred-key parameter and branch:
    an empty set is dead scaffolding whose dispatch can never fire, exactly the
    reasoning that removed the D-070 deferred-kind set at M4 Slice E.
    `SpecSurfaceNotYetSupported` stays live with its one remaining owner, the
    **value-level** `value_type = "date"` reject. `TemplateMatcherNotImplementedV1`
    stayed **untouched**: template and matcher *use* remained rejected until Slice B
    (retired by D-121). Registry **75 → 76**.
  - **Fingerprint boundary unchanged.** Naming reaches identity only through the
    existing planned `rendered_names` input, so `CanonicalJson`,
    `FingerprintCalculator`'s field shapes, and `fp_format = 1` are untouched
    (D-118). An effect-changing format moves `cxt_output_fingerprint` and `.cxt`
    bytes only; a `display_name` no format references, and a format that renders
    identically, are fully neutral.
- **Why:** D-116/D-117 settled the *semantics* and deliberately minted no enum
  name, no public shape, and no registry row; those are implementation-surface
  choices EP-4 requires be designed before code, and this entry records the ones
  taken. Two matter beyond their local scope. The parse-ordering
  policy is centralized because ordering that emerges from traversal is not a
  contract: it changes silently whenever a reader's field order is edited, and
  D-116 promised source-position order to users. The `MergeDefaults` line is
  called out because it is the one place the new surface does **not** come for
  free: attribute and template composition carries init properties
  automatically, which makes the `[defaults]` exception easy to miss and
  invisible until a composed spec quietly renders different names.
- **Rejected:** positional record parameters for the new carriers (would break
  every existing construction site and deconstruction for no gain; D-087
  settled this pattern); a dedicated format or display-name diagnostic code
  (§10.7/D-116 assign every naming-shape failure to `SpecFieldInvalid`; a new
  code would give one condition two owners); a resolve-phase diagnostic for an
  invalid *effective* format (every authored format is parse-validated, so a
  failure there is a hand-built document, a programmer error, and reusing
  `SpecFieldInvalid` would give it a second phase against D-067); value equality
  on `NameFormat` (unlisted public behaviour beyond the approved surface, and
  reference equality is unobservable while every pre-M6 spec resolves to null);
  keeping the deferred-key sets empty "for symmetry" (dead dispatch, M4 Slice E's
  precedent); sanitizing an invalid rendered name in the exporter (EP-15: writers
  serialize an already-decided result); sorting the invalid-name samples (render
  order is the order the planner emits those columns, so it is the order an author
  can act on).
- **Affects:** Diagnostics (`FormalAttributeNameInvalid`; registry 76); Core
  (`NameFormat`, `AttributeSpec` naming properties, `ResolvedSpec` backstop,
  `ConversionPlanner` rendering + guard); Spec (the three section carriers,
  `SpecReader` ordering, `SpecWriter`, `SpecComposer.MergeDefaults`, `SpecResolver`);
  spec §10.1 / §10.7 / §16.4. Realizes D-116(7)/D-117/D-118. Export unchanged (EP-15); no
  fingerprint-encoder or `fp_format` change; no production project reference changed.

## M6 Slice B (template/matcher application at the resolver seam)

### D-121: Templates and matchers executable: the six resolve codes, single-owner source addressing, the wrapped whole-name regex, the effective-section fold, family assembly, and the last M6 retirement

- **Status:** accepted (M6 Slice B implementation; realizes D-114/D-115/D-116(1–6)/D-118)
- **Date:** 2026-07-20
- **Decision:** templates and matchers **execute**. The implementation-surface
  choices D-114…D-118 deferred to this slice:
  - **Six permanent `DiagnosticCode` members**, all spec-resolve, adjacent to the
    other spec-resolve members: `TemplateIdMissing`, `TemplateIdDuplicate`,
    `TemplateReferenceUnknown`, `MatcherSelectorInvalidForShape`,
    `MatcherSelectsNoAttributes`, `MatcherFullyShadowed`. One code covers **both**
    unknown-reference sites (matcher and attribute) because it is one condition with
    two locations, not two conditions (D-067). Every static authored-shape failure
    (an invalid template `id`, a missing matcher `template`, both-or-neither selector,
    an empty or uncompilable `name_regex`, a malformed `source_index_range`) reuses
    the parse-phase `SpecFieldInvalid` and mints nothing (D-116). Registry
    76 + 6 − 1 = **81**.
  - **One authoritative source-addressing pass.** A `source_index_range` selects on
    the **resolved physical column index**, so addressing must precede application,
    but the resulting `SourceBindingInvalid` must still be reported **exactly once**,
    in the attribute's ordinary validation slot. The new internal `SourceAddressing`
    runs once per resolve, after binding resolution and before matcher application,
    and yields per attribute either an addressed source or the single diagnostic
    explaining why not. `ResolveAttribute` **consumes** that result: it no longer
    resolves a source and never emits a second binding diagnostic, so a name-bound
    source with no schema plus an index-range matcher yields one diagnostic, not two.
    Splitting "address the source" from "report and consume it" is what makes both
    obligations true at once, and the refactor is behaviour-neutral for every
    template-free spec (message text, location, order, resolved name bindings, plans,
    fingerprints, and bytes all unchanged).
  - **Value typing stays post-application.** The addressing pass captures only the
    **authored** `source.value_type`; the D-061 derivation (authored ?? the type the
    discretizer fixes) runs in `ResolveAttribute` over the **effective** section,
    because the effective discretizer can arrive from a template. Addressing is
    source-only and pre-merge; typing is discretizer-dependent and post-merge: two
    steps, one owner each. Concretely, a template-supplied `manual_cuts` types a
    bare-string `restrict_to` and trips `RestrictToNumericEntryRequired`, exactly as
    the flat equivalent does.
  - **One wrapped whole-name regex construction**, in the internal `MatcherSelectors`,
    shared by the parse gate and selector evaluation so a pattern cannot parse
    successfully and then fail, or match differently, when evaluated (EP-5):
    `new Regex(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant,
    Regex.InfiniteMatchTimeout)`. The **non-capturing** group is required, not
    cosmetic: bare anchors around an alternation would bind as `\Aa|b\z`
    ("starts with a, or ends with b") instead of whole-name "a or b", and a
    non-capturing group shifts no backreference number. `CultureInvariant` (EP-12) and
    the **explicit** `InfiniteMatchTimeout` follow D-104's `value_groups` precedent;
    `NonBacktracking` stays unadopted (D-115).
  - **Application is a document→document fold** inside `SpecResolver.Resolve`,
    producing an effective `AttributeSection` (the same type a flat spec produces),
    over which every existing validation owner then runs unchanged. Provenance is
    therefore **automatic** rather than a parallel model: a template-won `boundary`
    simply *is* a non-null `Scale.Boundary`, which is already what "authored" means to
    `ValidateOrdinalOverCuts`. Tiers 1–2 deliberately stay **below** the fold, where
    the resolver already applies them: materializing `[defaults]` into the effective
    section would turn defaulted values into authored ones and silently convert a
    legal defaulted `ordinal_boundary` into an `OrdinalBoundaryIncompatibleWithCuts`
    Error (§6/§12.3).
  - **Five-family diagnostic assembly.** Each family is collected into its own list
    and concatenated at the end, so the deterministic order is a property of
    `Resolve` rather than of where each helper happens to append: template identity →
    the established binding-section prefix → matcher references/shape compatibility →
    attribute template references → effective-attribute validation (including the
    once-emitted addressing diagnostics) → matcher warnings. Family 1 precedes the
    shape gate, so a shape-less document still reports template identity, the
    aggregation the retired transitional reject used to provide.
  - **One matcher-order warning traversal.** Family 5 is a single pass over matchers
    in declaration order, so the two warning kinds **interleave by matcher** rather
    than grouping by code, and a matcher qualifies for at most one. Per-field winners
    are recorded during the merge walk itself, at the moment the winner is chosen,
    so the shadow map cannot drift from the merge it describes. A matcher with an
    Error (unknown reference, or a range under `shape = "triple"`) **still** reports
    `MatcherSelectsNoAttributes` when its selector chose nothing: both warnings are
    selector- and merge-level facts, D-116 makes the zero-match warning explicitly
    independent of whether the template resolves, and suppressing it would be a
    special case in what is otherwise one uniform traversal.
  - **Core boundary and retirement.** Core gains nothing; the new
    `Core_ShouldRemainTemplateAndMatcherFree` architecture rule (ArchUnitNET) asserts
    no Core type is *named* for a template or a matcher, deliberately a name rule,
    because the reference rules already forbid Core→Spec and would stay green while
    someone re-implemented a template table inside Core.
    `TemplateMatcherNotImplementedV1` is removed entirely (member, both emit sites,
    and §16.4 row), so **no M6 transitional remains**;
    `SpecSurfaceNotYetSupported` stays live with its one non-M6 owner,
    `value_type = "date"`.
- **Why:** D-114…D-118 settled the *semantics* and deliberately minted no enum name
  and no public shape; those are the implementation-surface choices EP-4 requires be
  designed before code, and this entry records the ones taken. Three matter beyond
  their local scope. The addressing pass is called out because
  the naive implementation (resolving the source once for the matcher and again for
  the attribute) is invisible until it produces a duplicate diagnostic on exactly
  the spec D-115 discusses. The post-application typing split is called out because
  deriving the value type where it *used* to be derived silently ignores a
  template-supplied discretizer, producing a spec that validates differently from its
  own flat equivalent. And the fold's decision to leave `[defaults]` below it is what
  keeps the authored-vs-defaulted boundary provenance D-060(c) depends on.
- **Rejected:** a matcher-specific schemaless-binding condition (D-115: name binding
  already needs the schema; one condition, one owner); separate enum members for the
  matcher and attribute unknown-reference sites (one condition, two locations);
  minting a parse-phase code for matcher/template shape (D-116 assigns all of it to
  `SpecFieldInvalid`); materializing `[defaults]` into the effective section (would
  destroy defaulted provenance, above); deriving the effective value type in the
  addressing pass (would ignore a template-supplied discretizer); a second pass to
  compute shadowing from the finished effective sections (the merge already knows the
  winner, and re-deriving it invites drift); suppressing `MatcherSelectsNoAttributes`
  on a matcher that also errored (D-116 makes the warning independent of template
  resolution, and a special case would make the single traversal conditional);
  reporting arity and per-selector validity together for a both/neither `match` table
  (one authoring mistake, one diagnostic); applying templates during composition
  (D-118: composition yields authored surface the writer round-trips);
  `RegexOptions.NonBacktracking` or a finite match timeout (D-115).
- **Affects:** Diagnostics (six members added, `TemplateMatcherNotImplementedV1`
  removed; registry 81); Spec (`AttributeReader`, `SpecSectionReaders`, the new
  internal `MatcherSelectors` / `SourceAddressing` / `TemplateApplication`,
  `SpecResolver`); Architecture tests (the Core name rule); spec §9.1 / §9.2 / §16.4.
  Realizes D-114/D-115/D-116(1–6)/D-118. Core, Conversion, Export, `CanonicalJson`,
  `FingerprintCalculator`, and `fp_format` unchanged; no production project reference
  changed; every v2 fixture, golden, canonical byte, and SHA pin unchanged.

---

## M7 (CLI) pre-implementation adjudication

### D-122: M7 CLI contract: commands, process model, publication, manifest, freeze, authored-empty domains, per-command semantics, non-goals, distribution, and the exit floor

- **Status:** accepted (M7 pre-implementation contract; **revises D-071**;
  refines D-026/D-078/D-088; realizes the CLI faces of D-005/D-028/D-067/D-077); its
  implementation architecture realized by D-123; the reach of parts 4–5 qualified by D-125
  (2026-09-09)
- **Date:** 2026-07-22
- **Decision:** the complete M7 CLI contract is settled **before any M7 code**, in
  fifteen parts.

  **1. Command inventory and shared grammar.** M7 ships **eight** commands:
  `convert`, `validate`, `plan`, `stats`, `calibrate`, `probe`, `migrate`,
  `fingerprint`. SPEC and DATA are **positional operands**; every other choice is a
  **named option**. `--help` and `--version` write **stdout** and exit **0**; the
  version text is the **same string** the manifest records as `tool_version` (§15).
  `plan --stats` does not exist: `stats` is a standalone command (part 10), never a
  plan flag.

  **2. Process contract.** Exit codes are fixed: **0** success (warnings and Info
  included; they never move it off 0), **1** any Error/Fatal diagnostic or a
  host/runtime/publication failure (a missing input file is 1, not 2), **2** usage,
  **3** cooperative cancellation, **4** unexpected internal fault. Ordinary
  host/environment failures (a missing or unreadable input, permissions, an output
  or publication failure) are **CLI-owned, code-less** errors on stderr with exit 1;
  they never grow `DiagnosticCode`. Existing phase-owned conditions such as
  `SpecExtendsNotFound` remain registry diagnostics (§16.4). **Signals:** the first
  Ctrl+C/SIGINT (and SIGTERM where the platform supports it) requests **cooperative,
  diagnostic-free** cancellation: cleanup runs, no committed run results, exit 3. A
  **repeated** interrupt restores immediate platform termination; only uncommitted
  residue may remain.

  **3. Rendering.** Each diagnostic renders as **one deterministic stderr line** in
  the **sparse labelled** form:

  ```text
  file="adult.csv" attribute="education" record=3: warning UnknownValueObserved: escaped-message
  warning NoObjectsEmitted: escaped-message
  error: escaped-host-message
  ```

  Only **populated** location fields appear, in the fixed order `file`, `line`,
  `column`, `attribute`, `record`; string-valued fields render as **JSON string
  literals**, integer fields as **invariant** integers; severity is **lowercase**;
  all control characters and literal backslashes in messages and string locations use
  **JSON string escaping** (messages carry no surrounding quotes). When **no** field
  is populated the location prefix is omitted entirely (including the leading `: `),
  so the line begins at the severity. Code-less host errors render `error: <escaped
  message>`. Diagnostics go to **stderr**, primary results to **stdout**; the
  library's diagnostic order is preserved and **M7 performs no additional grouping**
  (§16.4's permission to group stands; M7 declines it). Output is **plain and
  terminal-independent**: no color, no progress meter, no machine-readable mode.
  **Color and progress are committed follow-ups and machine diagnostics an
  anticipated later requirement**, so M7 MUST **centralize** diagnostic presentation
  and progress observation such that adding them later needs no run-orchestration
  refactoring. M7 defines no flag or schema for any of them.

  **4. Input/output policy.** Neither SPEC nor DATA accepts `-`: there is **no stdin
  source** and no implicit spooling or synthetic composition base. Report commands
  write their primary result to **stdout**. `probe`, `migrate`, `calibrate`, and
  `fingerprint --write` **require** `--out VALUE`, where VALUE is a **distinct file
  path or the literal `-`**; `convert` never publishes an artifact to stdout.
  **In-place writing is forbidden.** Writing commands **refuse an existing target by
  default**; `--force` authorizes replacing an **existing distinct destination
  only**. Any canonical-identity **input/output or output/output collision fails even
  with `--force`**, and the complete target set is preflighted **before** any staging.

  **5. Convert publication and input stability.** `convert` requires
  `--out BASE --format cxt|dat|both`; the ruled extensions are appended to BASE, with
  **no default and no inference** from an extension. Publication stages every artifact
  on the **destination filesystem**, validates/flushes/hashes, then commits files
  **atomically one by one** with **best-effort rollback**. For a **manifest-bearing**
  run the manifest publishes **last** and is the run's **public commit marker**; under
  **`--no-manifest`**, implementation-private transaction state marks the run
  incomplete until the complete requested artifact set commits and disappears **only**
  on success, so incomplete-run detection is identical either way. Failed, cancelled,
  or invalid runs **leave no committed run**: a failure before the commit phase exposes
  nothing new, and a commit-phase failure rolls back best-effort; any surviving
  residue remains **uncommitted** and is detected, reported, and safely cleaned on a
  later collision. The contract claims **no cross-file atomicity**. **Input
  stability:** every complete data pass hashes the **raw bytes it consumes inline**
  (never a separate hash-only read); a replay pass whose hash differs from the first
  fails the run **before any commit**, as a **code-less host error** that publishes
  nothing. A genuinely single-pass run records that pass's hash under an explicit
  **stable-input precondition**. The policy is centralized and applies equally to
  **multi-pass report commands** (`stats`), where a mismatch fails the report
  identically even though no commit was attempted. M7 exposes **no toggle**; M8
  measured the cost; any later opt-out needs its own explicit ruling and **never**
  activates by file size.

  **6. Manifest.** **By default, each `convert` run emits exactly one
  `BASE.manifest.toml`; `--no-manifest` suppresses that audit sidecar only** (never
  two manifests, never a manifest per artifact). Section order is `[run]` →
  `[[run.outputs]]` → conditional `[[run.spec_files]]` (root-to-base) → ordered
  `[[run.calibrations]]`. `[[run.outputs]]` entries appear in canonical **format
  order** (cxt before dat) carrying `format`, `path`, `hash`. `[[run.spec_files]]`
  entries exist **only** for an `extends` chain and carry their fields in the fixed
  order **`path`, then `hash`**. `[[run.calibrations]]` entries carry `attribute`,
  `kind` ∈ {`cuts`, `observed_domain`, `include_additions`, `passthrough_bins`}, and
  that kind's variant-specific fields (**one entry per calibrated attribute, which
  retains exactly one outcome**: the closed `AttributeCalibration` union), in
  **spec-attribute order**, with legitimate zero-discovery outcomes as **explicit
  empty arrays**. **Array wrapping: only long non-cut calibration `values` arrays use
  the D-113 deterministic wrapping; `command_line`, `cuts`, and every other array
  remain inline.** Manifest bytes are canonical (§15): UTF-8 without BOM, LF, the
  fixed order above, the **same canonical TOML literal conventions as the spec
  writer** (D-075/D-113), and **no comments emitted**. `timestamp` is whole-second
  RFC 3339 UTC from an **injected clock**; `command_line` is the argv array verbatim;
  `tool_version` is the shared version string. Only `timestamp` and `command_line`
  are audit-variable.

  **7. Size advisory.** `OutputCxtSizeAdvisory` is the **exact final serialized
  `.cxt` size in UTF-8 bytes**, projected **after** the writer's object-name/count
  pass and **before any output bytes** (§8). It is **CXT-only** (a `.dat`-only run
  emits none), `0` disables it, and it stays an **export**-phase diagnostic. It joins
  the enum **at its real M7 emit site** (81 → 82) and, changing a warning and never
  bytes, remains a non-input to all three fingerprints (D-077).

  **8. Runtime options.** `--temp-dir PATH` is exposed on the **grouping-capable**
  commands, **`convert`, `plan`, `stats`, `calibrate`, `fingerprint`** (each may
  calibrate and/or emit); `probe` has no spill machinery (D-110) and
  `validate`/`migrate` never read data rows. It is **runtime-only**, byte- and
  fingerprint-neutral, and needs only a **minimal public byte-neutral Conversion
  capability**. Memory budget and merge fan-in stay **internal**; M8 measured them and
  kept them unchanged (D-124).

  **9. Ownership.** The run/publication coordinator is **CLI-internal** for M7; **no
  production package references `Cli`** (an architecture lock). An **EP-4
  public-surface extraction review** is required before any M9 reuse.
  `EmitReplaySession` remains public and supported; nothing is deprecated before a
  proven replacement exists.

  **10. Per-command semantics.**
  - **`validate SPEC [DATA]`**: without DATA, the existing no-schema resolution
    behavior applies **verbatim**; with DATA it reads **only enough for authoritative
    schema acquisition** (the header, or the first record when headerless). It does
    not plan, does not verify stored fingerprints, and writes nothing.
  - **`plan SPEC DATA`**: DATA is **required for every plan**; rows are read only
    when calibration requires them, and "dry run" means **no artifact publication**,
    not no source access. Stdout is **detailed fixed plain text in plan order**: index,
    canonical identity, rendered name, source/scale/bin detail, calibration and
    restriction summaries, then the **three native fingerprints**. No artifact, no
    manifest, no machine document; stale-fingerprint warnings stay stderr diagnostics.
  - **`stats SPEC DATA`**: DATA required; the full pipeline plus **one bounded
    emit-counting pass**; exactly six fields: `objects`, `formal_attributes`,
    `crosses`, `density` (an exact ratio with fixed six-decimal invariant rendering),
    `crossless_objects`, `empty_attributes`. When `objects × formal_attributes` is
    zero it prints exactly **`density = n/a (0 cells)`** and retains the other five
    fields. It writes no files and no manifest.
  - **`calibrate`**: freezes **every** data-dependent outcome by its kind's mapping:
    **automatic cuts → `manual_cuts`**; **observed domain → an explicit
    `declared_domain`**, an **empty** outcome freezing as **`declared_domain = []`**;
    **`unknown_value_policy = "include"` additions → folded into the
    `declared_domain`** (appended after the declared values) with the policy rewritten
    to fixed **`"warn"`**; **`value_groups` `unmatched = "passthrough"` bins → ordered
    singleton groups** (`{ label = <value>, values = [<value>] }`) with `unmatched`
    rewritten to fixed **`"skip"`**. Declaration/first-observation order is preserved
    (§17 rule 3) and numeric entries use the **D-096/D-101 canonical numeric
    spellings**. The result is **fully frozen** with all **three native stored
    fingerprints** written, is **byte-idempotent** when rerun on its own output, and
    warns on stale input hashes while correcting them in the output. An `extends`
    chain is **flattened** to one standalone frozen spec. Reconverting the calibration
    input from the frozen spec is **byte-identical in both native and `--v2-compat`**
    modes.
  - **`fingerprint SPEC DATA`**: DATA always required; reports the three computed
    **native** hashes plus each stored field's `match`/`stale`/`absent` state.
    `--write --out NEW_SPEC` writes a corrected canonical copy **only** for a spec
    meeting the fully-frozen gate; it **preserves the root's `extends`** and changes
    only the stored fingerprint fields semantically. It takes **no `--v2-compat`**
    (D-011 keeps that flag convert-only) and never stores effective override hashes.
  - **`probe`**: `fcabedrock probe DATA --shape wide|triple --out PATH|-` plus
    `[--delimiter CHAR] [--header true|false] [--missing-token TOKEN]
    [--locale TAG|invariant] [--limit N]`, and triple-only
    `[--ordering subject_grouped|unordered]
    [--subject N|NAME --predicate N|NAME --value N|NAME]`. The three role options are
    supplied **together**, in **one addressing mode** (all zero-based indices or all
    header names); name addressing requires `--header true`. Defaults mirror §5.1/§5.3;
    `--limit` defaults to 100,000. The **three aggregate guards remain pinned
    public-API defaults with no CLI flags** (§7.1).
  - **`migrate`**: `fcabedrock migrate BED --out PATH|-` plus
    `[--shape wide|triple]` (default wide), `[--delimiter CHAR] [--header true|false]
    [--locale TAG|invariant] [--missing-token TOKEN]
    [--scaling discrete|progressive]` (default **discrete**, D-045); wide-only
    `[--object-key row_index|column]` with `--object-key-column N|NAME` **required
    exactly when** `--object-key column` and invalid otherwise; triple-only role
    options **identical to probe's** (together, one mode, names need `--header true`,
    indices zero-based). The invoked `.bed` path is recorded automatically as
    `derived_from`. Fixed one-value v1 facts (encoding, quote character) get no flags
    (EP-6).

  **11. Extends canonical identity.** The file-backed host resolves **actual
  filesystem identity** where available, which unifies supported symlink/hardlink
  aliases as well as `.`/`..` and case spellings of one file. Otherwise it falls back
  to comparing **normalized full paths** under the actual volume/platform comparison
  behavior; the fallback makes **no link-alias guarantee** beyond what the host
  filesystem exposes. Authored references stay **referrer-relative and relative-only**;
  `..` is legal, and an absolute reference keeps the existing not-found outcome.
  Identity keys are internal: they never affect output bytes, fingerprints, or the
  manifest.

  **12. Non-goals.** M7 ships **no sampling, no compressed artifacts, and no
  arbitrary output-setting overrides**; unknown flags are **usage errors** (exit 2).
  Each excluded feature may return only through its own contract decision.
  `--v2-compat` remains the **sole settled conversion override** and stays
  convert-only (D-011). No color, progress, or machine-mode flag exists in M7.

  **13. Distribution.** M7 ships as a **.NET global tool**, validated on **x64**, with
  a documented Windows sequence (`winget install Microsoft.DotNet.SDK.10`, then
  `dotnet tool install --global FcaBedrock.Cli`) plus equivalent guidance for the
  other supported platforms. This is **explicitly temporary technical-preview
  distribution**. The later public release MUST provide a **standalone/self-contained
  route requiring no prior .NET knowledge**; the global tool may coexist. Broader
  platform/architecture validation belonged to M8 (D-124). **Documentation:** user
  documentation MUST document the exact global-tool installation route, and the later
  public-release documentation MUST lead with the standalone route.

  **14. Exit floor.** M7 closes only when the full **argv-boundary** floor is green:
  every command exercised through argv; **all nine active goldens** reproduced through
  the real CLI path; manifest/exit/diagnostic **byte locks**, including manifest byte
  locks covering **all four `[[run.calibrations]]` kinds**, empty arrays,
  Unicode/control escaping, long values and D-113 wrapping, argv, timestamps, **and
  both no-chain and multilevel-chain `[[run.spec_files]]` paths**, plus
  rendering-grammar locks for **every location-field combination**;
  publication/rollback/overwrite/collision cases; calibrate/fingerprint freeze and
  idempotence covering **every freeze mapping, the empty-outcome `[]` freeze, and the
  authored-empty `[]` combinations with `unknown_value_policy = "include"` and
  `missing_policy = "as_attribute"`**; probe/migrate cases covering **triple role
  index-mode and name-mode successes plus mixed-mode and partial-role usage failures,
  and the wide object-key conditional grammar** (`--object-key-column` present exactly
  with `--object-key column`); advisory threshold cases; extends identity and cycle
  cases; signal cases; input-stability mismatch cases; a **global-tool
  pack/install/uninstall/`--version` smoke in an isolated tool path**; and
  **representative excluded flags** (`--sample`, `--gzip`, machine/color/progress
  spellings) returning **usage exit 2**. Every existing golden, canonical-TOML, SHA,
  registry, and architecture lock stays green; the normal suite stays fast; large
  benchmarks and release validation were not part of this floor.

  **15. Authored-empty domain semantics (revises D-071).** An **omitted**
  `declared_domain` requests observed-domain calibration. **Any authored domain is
  complete, including `declared_domain = []`**, which denotes a **fixed empty
  domain**: **zero declared value bins**. An authored `[]` suppresses observed-domain
  discovery, but `unknown_value_policy = "include"` may still extend the domain with
  observed values and `missing_policy = "as_attribute"` may still add the missing
  column, so `[]` **does not by itself guarantee zero formal attributes**. This holds
  through every resolution tier: a template-supplied `[]` is likewise
  authored-complete (D-114 whole-value layering). `probe` **omits** `declared_domain`
  for an attribute whose observations were all missing; it never authors `[]`;
  `calibrate` freezes an empty observed outcome **as `[]`**. The fully-frozen gate
  (§3/§14) therefore reads "**omitted** `declared_domain` under a consuming
  discretizer", so an authored `[]` no longer disqualifies. The code gap this
  opened, an authored `[]` read as absent (D-098), was closed at M7 S1 (D-123
  point 10).
- **Why:** this is the pre-implementation contract for M7, settled with explicit
  rulings by Constantinos Orphanides on 2026-07-22, including authored-empty domains,
  the exact stderr grammar and the `[[run.calibrations]]` manifest shape. Settling it
  **before** any M7 code prevents implementation-driven contract drift on surfaces
  that are observable and byte-tested from their first commit: exit codes, diagnostic
  bytes, manifest bytes, publication guarantees, freeze output. It follows the
  contract-first precedent of D-088…D-092 and D-114…D-119. Concrete parser,
  DI/logging/hosting, serializer, terminal, and transaction-mechanism choices stay
  implementation-plan work (D-123); nothing here selects a library.
- **Rejected:** inferring the output format from an `--out` extension (an
  extensionless base would silently mean two artifacts); one manifest per artifact
  (two divergent run records for one run); inline calibration maps keyed by
  attribute name (dynamic attribute names as schema keys, and unbounded single lines
  for large domains); registry codes for ordinary host failures (the registry is phase-owned
  and grows only with real emit sites); stdin SPEC/DATA with implicit spooling (a
  huge private copy, and no stable referrer base for `extends`); in-place spec
  rewriting even with `--force` (canonical serialization drops comments); a
  set-atomic rename promise for sibling files (not achievable with ordinary renames);
  optional DATA for `plan` (the settled `CalibratedSpec` seam rejects a schema-less
  resolution); a `plan --stats` duplicate of the `stats` command; `--v2-compat` on
  `fingerprint` (D-011 keeps it convert-only); a POSIX-style two-code exit model
  (cancellation and internal faults become indistinguishable); immediate termination
  on the first signal (no cleanup, committed-run ambiguity); snapshot- or lock-based
  input stability (host-specific and unenforceable); a standalone-only M7
  distribution (premature for a dogfood milestone); the thin roadmap exit; retaining
  `[]`-as-absent (D-071's reading, which leaves an empty observed domain with no
  frozen representation); and a weaker `--no-manifest` crash guarantee.
- **Affects:** `Cli` (the new package); `Conversion` (a byte-neutral runtime-options
  capability); `Export` (the advisory site); `Spec` (freeze/write checks,
  empty-domain resolution); `Core` (Calibrate-phase empty-domain semantics, freeze
  outcomes); `Diagnostics` (registry 81 → 82 at the advisory's emit site); spec §§3,
  7, 7.1, 8, 10.3, 10.6, 11.6, 13, 14, 15, 16, 17, 18.1; D-026 and D-071 refinement
  notes; roadmap M7/M8/M9 and the deferred backlog; `AGENTS.md` status.

---

## M7 (CLI) implementation

### D-123: M7 implementation architecture and public surfaces: CLI boundary, parser, presentation, audit argv, filesystem identity, publication transaction, run manifest, freeze pairing, runtime options, and the CXT size advisory (registry 81 → 82)

- **Status:** accepted (M7 implementation; realizes D-122's implementation architecture;
  refines D-085's registry-timing rule). Slices S1 to S11 all landed by M7 Slice K, merged
  2026-08-08; the roadmap's M7 exit block is the landed record. The reach of point 7
  qualified by D-125 (2026-09-09).
- **Date:** 2026-07-24
- **Decision:** M7's implementation architecture and every new public surface are
  settled here. It records the concrete parser, serializer and transaction-mechanism
  ownership D-122 left to the implementation plan; every point landed across slices
  S1 to S11 (see Status).

  **1. Package boundary and dependency direction.** M7 ships one new package,
  `FcaBedrock.Cli`, a .NET global tool (`PackAsTool`, `ToolCommandName = fcabedrock`,
  `PackageId = FcaBedrock.Cli`) that references all seven production packages. **No
  production package references `Cli`**: a one-way dependency enforced by an architecture
  lock (point 13). Every CLI component is `internal`; an EP-4 public-surface extraction
  review is required before any M9 reuse; `EmitReplaySession` stays public.

  **2. Coordination and parsing.** The run/publication coordinator is plain static
  CLI-internal composition (no DI container, no hosting, no logging framework) and
  **adds no new parser/hosting/logging dependency**: a hand-rolled declarative parser (a
  data-driven command table of positional operands + `--name VALUE`) owns the byte-locked
  usage text. `System.CommandLine` was rejected because its help/usage/implicit bytes sit
  inside M7's byte-locked stdout/stderr and would drift with upgrades (the D-075 hazard).

  **3. Centralized presentation.** All diagnostics and progress flow through one
  CLI-internal renderer (the D-122 part 3 grammar, byte-exact) plus a no-op progress
  observer, so the committed color/progress/machine-mode follow-ups become later renderer
  implementations rather than run-orchestration refactors.

  **4. Audit argv.** The manifest `command_line` is the **complete process command-line
  array including its actual argv[0]** (`Environment.GetCommandLineArgs()`), captured
  **separately** from the parser's ordinary arguments and written **verbatim**.
  A non-`fcabedrock` argv[0] (e.g. a full host executable path) is
  preserved unchanged; parsing continues over the ordinary args. `command_line` and
  `timestamp` are the only audit-variable manifest fields.

  **5. Input-open seam.** A CLI-internal input-opening seam (production opens real files;
  argv tests inject stable or pass-specific streams) makes input-stability behavior
  deterministically testable without timing races. It is no public API and no
  user option.

  **6. Filesystem identity.** One shared CLI-internal identity service resolves **actual
  OS filesystem identity** for existing files where available (Windows `FILE_ID_INFO`;
  Unix `dev`/`ino`), unifying supported symlink **and hardlink** aliases; normalized full
  paths are the **fallback only**. It is consumed by both the file-backed `extends` host
  and publication collision checks, adds **no dependency**, and its identity keys never
  reach output bytes, fingerprints, or the manifest.

  **7. Publication transaction.** Writing is a staged transaction: a
  complete **preflight** resolves the target set and fails any input/output or
  output/output identity collision even with `--force`; a unique **create-new transaction
  record** (`<BASE>.fcabedrock-transaction-<token>.toml`, with `fcabedrock` spelled in
  full, never `fb`) owns and lists the run's private stage/backup siblings; artifacts stage
  and flush on the destination filesystem; forced replacement first **renames each
  existing target aside** to a backup (a cheap same-directory, same-filesystem metadata
  rename, never a copy or rehash); commit is per-file atomic rename in canonical format
  order with the **manifest last** as the public commit marker (under `--no-manifest`, the
  equivalent commit point is the complete requested artifact set), and backups plus the
  record are removed only at that commit point. Failure/cancellation restores
  renamed-aside targets, deletes stages, and best-effort-removes commits; **validated
  recovery** cleans only files a transaction record lists and **never deletes unknown
  lookalikes**; those are reported as a collision. No cross-file atomicity is claimed. The
  single-file writers (`probe`/`migrate`/`calibrate`/`fingerprint --write`) reuse the same
  preflight+stage+backup+atomic-rename matrix, refuse an existing target without
  `--force`, preserve the old target on failure/cancellation, and treat **`--force --out
  -` as usage exit 2**; `--out -` bypasses publication.

  **8. Run manifest.** `FcaBedrock.Spec` owns the public `RunManifest` model
  (`RunSection`, `RunOutput`, `SpecFileEntry`, and calibration entries built directly from
  the retained `AttributeCalibration` union: no re-derivation, D-093) and
  `RunManifestWriter.Write(RunManifest) → string`, implemented on the **same
  Spec-internal canonical TOML literal/wrapping machinery as the spec writer** (D-075/
  D-113), so no second canonical-TOML emitter exists (EP-5). The CLI composes the model and
  never formats TOML itself.

  **9. Freeze pairing.** The public **`SpecFreezer.Freeze(ResolvedDocument resolved,
  CalibratedSpec calibrated) → SpecDocument`** is the **paired** carrier: it
  verifies `ReferenceEquals(resolved.Resolved, calibrated.Resolution)` and throws
  `ArgumentException` on mismatch (the `SpecFingerprints.ComputeNative` posture, EP-14), so
  template/matcher-won values invisible in the composed document are read from the
  effective resolved state before calibration additions are appended.

  **10. Presence carrier.** `AttributeSpec.DeclaredDomain` is the nullable presence
  carrier: `null` = omitted (requests observed-domain calibration); any non-null list
  including `[]` = authored-complete.

  **11. Conversion runtime options.** A public `ConversionRuntimeOptions`
  (single member `TempDirectory`) plus public `Calibrator`/`Emitter` overloads map to the
  internal `GroupingOptions(tempDirectory:)`, giving `--temp-dir` a minimal, byte- and
  fingerprint-neutral public capability; memory budget and merge fan-in stay internal
  (EP-6; M8 kept them unchanged, D-124).

  **12. CXT size advisory.** `CxtWriter.WriteAsync` gains the
  additive public overload `(…, ICollection<BedrockDiagnostic> diagnostics, long
  sizeAdvisoryBytes)`; the existing overload delegates with the advisory disabled and stays
  byte-identical. After the pass-1 name/count collection and **before any output byte**,
  the writer projects the **exact** final serialized `.cxt` size in UTF-8 bytes (encoded
  bytes, not characters, under checked 64-bit arithmetic) and appends `OutputCxtSizeAdvisory`
  (Warning, export) when it is **at or above** a positive threshold; exactly `0` disables,
  and a negative threshold throws `ArgumentOutOfRangeException`. The advisory changes a
  warning only: never output bytes, never a fingerprint (D-077), and `size_advisory_bytes`
  is not a fingerprint input. `OutputCxtSizeAdvisory` is M7's **only** new diagnostic code
  (registry **81 → 82**); ordinary host/publication failures remain CLI-owned, code-less
  errors (D-122 part 2), so the registry does not grow for them.

  **13. Architecture lock.** `FcaBedrock.Architecture.Tests` gains a **test-only**
  `ProjectReference` to `FcaBedrock.Cli` so the assembly loader sees the Cli assembly; the
  non-vacuity assertion requires all **eight** production assemblies loaded, and no
  production dependency on Cli is created.

  **14. Ruled command behaviors.** Triple `migrate` always authors
  `ordering = "unordered"` (no `--ordering` flag); `fingerprint --write
  --out -` writes **only** the corrected canonical spec on stdout and suppresses the normal
  report, while file targets keep report-on-stdout plus spec-in-file.

- **Why:** EP-4 requires public shapes to be designed before implementation, and the
  registry lock's convention requires the 81 → 82 count change to land **with** its
  decision entry. D-122 left concrete parser, serializer, and transaction-mechanism
  ownership to the implementation plan; one entry records all of it where M7 adds its
  largest host surface, so a later reader sees what was chosen and why without
  reconstructing it.
- **Rejected:** `System.CommandLine` (library-owned help/usage bytes inside a byte-locked
  surface, drifting with upgrades; the D-075 hazard); a second canonical-TOML emitter in
  the CLI (EP-5: the one emitter stays in Spec); a raw-document-plus-unpaired-outcome
  freezer (loses template/matcher-won values and permits cross-run mixing);
  path/symlink-only identity (misses the hardlink aliases normative §13 names);
  overwrite-then-delete forced replacement (a false commit marker and a destroyed prior run);
  fixed-suffix residue recognition without ownership proof; a
  synthesized constant `fcabedrock` argv[0] (not the verbatim audit value);
  exposing grouping budget/fan-in as a test seam (EP-6); and several narrow decision entries
  in place of one (a fragmented audit trail).
- **Affects:** `Diagnostics` (registry 81 → 82); `Export` (the `CxtWriter` advisory
  overload); `Core` (nullable `AttributeSpec.DeclaredDomain`); `Conversion`
  (`ConversionRuntimeOptions` and overloads); `Spec` (`SpecFreezer`,
  `RunManifest`/`RunManifestWriter`, file-backed `ISpecTextSource`); `Cli` (the new package);
  `tests/FcaBedrock.Architecture.Tests` (test-only Cli reference); `docs/roadmap.md`,
  `AGENTS.md`, README and user docs (status and install route). This entry does not reopen
  D-122 or any of its rulings.

---

## M8 (first scaling / benchmark pass)

### D-124: M8 benchmark architecture: one internal BenchmarkDotNet host, an explicit corpus/oracle layer, tier-gated selection with a pinned opt-in external corpus, two benchmark-only friend grants, and the conforming calibration fix the suite found

- **Status:** accepted, with M8 Slices S1 to S5 landed: both friend grants and the conforming
  multi-attribute calibration correction with its replacement evidence. *Corrected 2026-09-09 by
  D-126:* the remaining-work status first recorded here was stale, and this entry's
  invalidation/recheck rule is amended for D-125's correction only. *Superseded 2026-09-21 by D-128*
  for the restated per-accumulator catalogue-at-most-fan-in bound only. M8's evidence closed at
  `3b2e4a80` (D-126).
- **Date:** 2026-09-06
- **Decision:** M8 is measured by **one internal BenchmarkDotNet executable**
  (`tests/FcaBedrock.Benchmarks`) plus a small tested corpus/oracle layer
  (`tests/FcaBedrock.Benchmarks.Tests`) and a documented evidence pack. It is an
  internal suite, not a benchmarking product: there is no plugin system, no
  scenario language, no process supervisor, and no public benchmark surface.
  - **BenchmarkDotNet owns everything standard.** Discovery, filters, jobs,
    warmup and measurement, the out-of-process worker lifecycle, iteration
    statistics, managed-allocation and GC reporting, profiling integration, logs,
    and exporters are BenchmarkDotNet's, pinned centrally at **0.15.8** rather
    than floating, because the measurement semantics every M8 comparison is
    stated in must not move underneath the comparison. Custom code requires both
    an actual FcaBedrock need **and** a verified gap. The accepted custom scope
    is exactly: deterministic streaming corpus preparation and cataloguing;
    independent domain oracles and post-iteration validation; derived domain
    denominators in the report; the existing internal grouping/calibration
    observers and layout tests; ordinary packaging scripts; and the existing
    bounded test subprocess helper for finite package smokes.
  - **Benchmarked surfaces are the real production paths**: source-session drain
    for wide and triple CSV; calibration, pure planning, emit drain, real `.dat`
    export and bounded small `.cxt` coverage; probe/discovery including bounded,
    truncated, and guard outcomes; in-process `CliHost` conversion through the
    real input/publication/hash paths, labelled **CLI-host throughput** and never
    installed-command latency; genuine input/output hashing wrapper pairs and
    manifest-sidecar comparisons; and separate real self-contained-command
    resource traces and packaging smokes **outside** BenchmarkDotNet throughput
    measurement. A nested CLI child is deliberately **not** benchmarked.
  - **The measured interval is a contract.** Every timed file operation starts
    from prepared but **unopened** inputs and outputs and includes opening, the
    full awaited production operation, complete consumption, the final flush and
    disposal, and the normal production diagnostics. Data generation, output
    reset, independent validation, harness hashing, and artifact retention stay
    outside it unless the production operation itself includes them. No scale
    dataset and no full context matrix is ever materialized (EP-16).
  - **Validation is per-iteration and outside timing.** Each completed measured
    iteration is validated after its producer sessions are disposed and its final
    diagnostics are available; a failure throws, so the case has no throughput
    result. The only correctness check is never deferred to global cleanup, where
    one late failure would silently cover every iteration before it. The launcher
    reads BenchmarkDotNet's own report/validation contract (build failures,
    execution failures, critical validation errors) rather than inventing a
    parallel result protocol, and also refuses to call a run successful
    when it measured nothing.
  - **The families, and what each one is for.** W16 (sixteen wide columns, both
    missing forms, the RFC 4180 quoting path) and T10 (ten triple rows per subject,
    in two physical layouts of the same observations) carry the tier ladder.
    **Keyed W16** repeats a non-contiguous object key so wide `dedupe` exercises the
    grouping backend from the other side; **Ads-width** is 1,559 columns of sparse
    flags, so width pressure is separable from length; **long text** has few, large
    values, which is the only shape that reaches probe's text guard before its value
    guard; **UCI Adult** is real, externally acquired data whose distributions nobody
    here chose. Adult and the v2 minis are the two external checks on a suite whose
    other expectations are authored in this repository. Adult alone is *acquired*
    rather than committed, which is why it is pinned and opt-in below.
  - **Corpora are explicit, deterministic, hashed, and out of Git.** Generated
    and downloaded data, produced outputs, and raw results are bulk evidence
    under an ignored artifacts root (redirectable with `FCABEDROCK_BENCH_ROOT`);
    only generator definitions, specs, attribution, the metadata contract, and
    tiny independent expectations are committed. Generation uses **defined
    integer arithmetic**, never an unversioned runtime pseudo-random source, so a
    corpus regenerates byte-for-byte anywhere. Each prepared case records its
    generator revision, geometry, exact byte length, and SHA-256; anything that no
    longer matches is **refused**, and the catalog entry is written last so an
    interrupted preparation is never reused. Preparation is a separate `prepare`
    verb: no run may start generating a 73M-record file as a side effect of a
    broad filter. No network access occurs during ordinary tests or benchmark
    measurement.
  - **The one acquired corpus is pinned by its consumed bytes, and kept out of
    routine CI.** UCI Adult is the only input this repository cannot generate, and
    it is treated accordingly.
    - **Pinned to the entry, not the archive.** The accepted `adult.data` is
      exactly **3,974,305 bytes**, SHA-256
      `5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d`, the file
      every M8 Adult figure is stated against. It is enforced on a fresh acquisition
      *before* a catalog entry is written, and again on reuse **independently of the
      catalog's own recorded digest**: the catalog records whatever arrived, so a
      changed upstream file and a catalog rewritten beside it agree with each other
      perfectly, and that is precisely the failure a self-recorded receipt cannot
      catch. A mismatch is refused and removed, never adopted. The pin is on the
      consumed entry because a zip can be repacked without changing a byte of what
      is measured. It establishes **byte identity**, not publisher authenticity; no
      attestation exists to claim more. **Changing the accepted identity bumps
      `AcquisitionRevision` with it**, so previously prepared corpora are refused
      rather than silently reinterpreted.
    - **Routine CI prepares `micro small` and runs `--anyCategories Small --filter
      '*' --job dry`.** No required or optional native job prepares or selects
      Adult. Every other build, test, accounting, package, and archive check is
      unchanged. A green CI run therefore proves the **selected synthetic and
      fixture** contracts on that platform and claims nothing about Adult, on that
      candidate or on any non-Windows target; the workflow and both READMEs say so
      in those words.
    - **The real-data evidence is required of the candidate instead.** Before whole-M8
      acceptance, and for each later release candidate, all three External cases must
      run successfully on the final **Windows x64** candidate with the verified
      corpus, and the result is retained as durable evidence. It is a **blocking**
      manual obligation (routine CI may be green while it is outstanding), enforced
      the same way M8's other primary evidence is: the closure checklist, the
      independent review, and operator acceptance. It is not a GitHub status check
      and introduces no release framework. An earlier candidate's result carries
      forward only under the existing explicit evidence-reuse justification, and a
      retained corpus is reusable only when its identity, spec digest, and
      acquisition revision all verify.
    - **Three failure modes, kept apart.** An unreachable host is an
      **evidence-availability** failure: the obligation stays outstanding, and no
      routine job fails for it. A length or digest mismatch is an **input-identity**
      failure to investigate, never an outage to retry into acceptance. A failure or
      oracle mismatch on verified bytes is a **correctness finding**. None of the
      three can become a pass, and none has a valid performance number.
  - **Oracles are independent.** Expected output is derived from the corpus
    definition plus the documented spec semantics, never by invoking the
    conversion algorithm under test, and large expectations are hashed while
    streamed rather than retained. The immutable v2 minis also serve as
    **external** byte oracles: they were produced by a different program, so they
    catch a pipeline that would still satisfy a model authored in this repository.
  - **Selection: Small by default; `Working`, `Scale`, and `External` opt-in by
    category.** Tier categories (`Small`/`Working`/`Scale`/`External`) and surface
    categories are orthogonal. With no category named the selection is Small; **the
    three opt-in tiers are reachable only by naming their category**: neither a
    broad name filter such as `--filter *`, nor a case's own name, nor a surface
    category reaches one. This is the one selection rule added on top of
    BenchmarkDotNet's filtering, and it is a pure function of the command line and a
    case's categories, so it is tested directly. A tier is a corpus: `Micro` (1,000
    records, for the wide-geometry family), `Small` (10,000), `Working` (730,000),
    the two target scales, and `External` for an acquired corpus whose size is
    measured at preparation rather than chosen. Micro is Small-category work.
    - **Why each tier is opt-in differs, and the difference is the point.**
      `Working` and `Scale` are gated by **cost**: those cases read 730,000, 7.3M,
      and 73M records, so an accidental run costs minutes to hours.
      `External` is gated by **dependency**: its cases are quick, but its corpus is
      acquired from a third-party host rather than generated here, so requiring it
      would make an unrelated outage fail whatever run happened to select it. The
      shape of the guarantee is identical either way, which is why one mechanism
      serves both. `External` keeps the fresh-iteration job: it is opt-in, not
      long-running.
    - **Opting in is not skipping.** A *selected* case whose corpus is absent,
      incomplete, or no longer matches its recorded identity is a hard failure
      carrying the exact `prepare` command, and the launcher also exits
      non-zero when a run measured nothing. An External case that quietly did not
      run is the one outcome this category must never produce.
  - **Two job shapes, chosen by the selection.** Small runs BenchmarkDotNet's
    Throughput strategy with one invocation per iteration and unroll factor one;
    Working and Scale run **Monitoring** with one launch, two warmups, and five
    measured iterations, because an operation that already takes seconds gives
    Throughput's pilot stage nothing to find. The 73M tier uses that same job with
    BenchmarkDotNet's own `--warmupCount 1 --iterationCount 3` on the command line
    rather than a third declared job: a run costing hours is a deliberate choice
    made where the run is started.
  - **Three memory meanings stay distinct**, and none is ever read off another:
    (1) BenchmarkDotNet's process-wide **managed allocated bytes** and GC counts,
    reported beside the recorded input-record and input-byte denominators so a
    rate can be derived and rechecked; (2) **modelled retained-resource**
    guarantees from the existing observers and independently validated layout
    witnesses; (3) separately sampled **whole-command working set** and GC traces,
    where a sampled maximum is a lower bound on the true peak and no sample is not
    zero.
  - **Build integration.** The benchmark host carries its own
    `Directory.Build.props` that imports the repository root props directly,
    deliberately shadowing `tests/Directory.Build.props`: inheriting the shared
    test defaults (`IsTestProject`, the xUnit reference, the
    Microsoft.Testing.Platform zero-tests guard) would make `dotnet test` run
    multi-minute benchmarks, which **EP-20** forbids. It keeps every root setting,
    warnings-as-errors included. `FcaBedrock.Benchmarks.Tests` is an ordinary
    xUnit v3 project and inherits the shared test props unchanged; the shared
    zero-tests guard is not weakened.
  - **Two benchmark-only friend grants, and no other access change.**
    `FcaBedrock.Conversion` and `FcaBedrock.Cli` each grant
    `InternalsVisibleTo` to `FcaBedrock.Benchmarks` so the suite can measure the
    existing internal grouping/host/hash seams rather than a copy of them. The CLI
    grant therefore changes from its own test assembly alone to **exactly**
    `FcaBedrock.Cli.Tests` and `FcaBedrock.Benchmarks`, once each in the project
    and in the compiled assembly, asserted independently of declaration order by
    `tests/FcaBedrock.Cli.Tests/CliProjectContractTests.cs`.
    `FcaBedrock.Benchmarks.Tests` gets **no** CLI grant; the contract test asserts
    the absence of its *grant* rather than of its name, because the project comment
    names it precisely to record that it is excluded. Both grantees are non-product
    assemblies: this changes the internal access contract, never the public product
    API. It preserves D-098's ban on production-to-production
    internals access and D-122/D-123's CLI-internal boundary: no production
    package references the benchmark assembly, CLI types stay internal, and no
    benchmark code ships in any product artifact. M9 reuse still needs its own EP-4
    review.
  - **The suite found a production defect, and it is fixed here as a conforming
    correction.** A spec with several `equal_frequency` (or `percentile_p1_p99`)
    attributes could fail to calibrate under the shipped budget, reporting
    `GroupingStorageFailed (CleanupDelete/DeleteFailed)` and producing **no
    result**, on entirely healthy storage. One `CalibrationRun` owns one
    `SpoolWorkspace<ValueCount>` that every count-sensitive accumulator spills
    into, but each accumulator passed **its own** cumulative spill payload as the
    `baselineT` of D-082's `L + P ≤ 3T` merge allowance, while the retained bytes
    it was compared against were the **whole workspace's**. The two sides
    described different sets, so the allowance shrank as attributes were added:
    with `A` comparable accumulators the left side grows with `A` and the right
    side does not, and the guard fires for `A > 2`.
    - **`T` is the workspace's, not one attribute's.** `T_i` remains an
      attribute's cumulative exact serialized bytes of successful **original**
      spills, framing included, never consolidation output and never reduced by
      deletion, consolidation, replay or finalization; `T` is their sum across
      every accumulator sharing that workspace, taken at the boundary in
      question, so online consolidation uses the workspace's `T_so_far` and a
      later merge is never measured against a smaller allowance than an earlier
      one. `L` is every retained run byte of that same workspace, `P` the
      conservative sum of the next merge's input sizes, and the check still
      retries pending deletions and permits equality. That is **one consistently
      scoped `≤3T` guarantee**: not `3A`, not a larger per-attribute allowance,
      and not a process-RSS ceiling. `FirstAppearanceGrouping` never had the
      mismatch (its workspace and its summed baseline describe the same grouping
      call) and is unchanged.
    - **The correction removes only false failures.** It changes no diagnostic,
      severity, registry entry, public API, TOML field, fingerprint rule, output
      byte, ordering rule, resource bound, or default. A genuine global
      byte-bound or pending-cap breach is still an `Error` with no calibrated
      result; cleanup-only trouble is still a `Warning`; `Σ Modeled(capacity_i) ≤
      max(budget, A · FloorBytes)`, the per-accumulator catalog ≤ fan-in, the
      single-workspace `4 × fan-in` pending-deletion cap, sequential merges,
      release-before-post-intake-merge, lazy zero-spill operation, confidential
      ownership, cancellation and non-masking cleanup all stand. Successful
      outputs keep their bytes; the cases that previously aborted now agree with
      their independent expectations and with their in-memory and frozen
      equivalents. It promises nothing about faulty storage.
    - **Ownership is narrow:** `QuantileAccumulator.cs`, which already owns
      `CalibrationBudget` and therefore the registry of accumulators sharing the
      run's workspace, plus the `baselineT` contract on `ValueCountMerger`. No
      new workspace, storage or accounting framework, no population-sized
      bookkeeping, no public seam, and no change to `SpoolWorkspace`,
      `FirstAppearanceGrouping` or `RunMerger`. Splitting the grouping and
      calibration budgets remains the separate recorded follow-up below; the
      defect is not an argument for it.
  - **No other product change is approved by this entry.** Beyond the conforming
    calibration correction above, no public product API,
    product-to-product internals access, diagnostic, ordering rule, determinism
    contract, spec clause, or output byte changes for M8. The M1–M7 invariants
    stand as correctness inputs M8 may measure and validate but never establish:
    pure Core, orthogonal discretization and scaling, planner-owned semantics,
    dumb exporters, streaming with no full matrix, **D-082** resource accounting
    (whose layout constants are correctness constants, never performance knobs),
    **D-095** exact bounded calibration, **D-110** probe accounting, and
    **D-122/D-123** input-stability, publication, and manifest semantics.
  - **What may change, and only after evidence.** The internal grouping and
    calibration memory budget and merge fan-in are the tunable policy (EP-19,
    D-082/D-095: byte-neutral by construction, never a spec or fingerprint input),
    and may move only after the approved evidence rule is met: a repeatable
    improvement on identified controlled hardware, reproduced in two independent
    comparison sessions and confirmed at both target sizes. **Probe-default adoption
    is not included**: it changes draft bytes, warnings, or success-versus-guard-failure,
    so it is a separate observable semantic decision requiring its own approval and
    spec §7.1/D-110 reconciliation. Target-specific resident safety constants are
    **correctness** changes requiring executed native proof and a narrow review, not
    tuning. A measured hotspot may receive one narrow optimization inside the
    accepted scope; no speculative optimization is preselected.
  - **The tuning rule was applied, and it was not met. Nothing changed.** A 256 MiB
    grouping budget is reproducibly faster than the shipped 64 MiB at 730,000 and
    7.3M records, and byte-neutral on every iteration. At **73M**, the larger of the
    two target sizes the rule requires confirmation at, the elapsed gain falls to
    6.9% and 4.0% in the two sessions, the allocation gain disappears entirely
    (+0.05%), and the same case measured 99.9 s and 125.1 s between the two
    launches: a 25% between-session drift, larger than the effect it would have to
    prove. The budget is also shared with the count-sensitive calibration
    accumulator, which is sized from it before any record is read, so a raise would
    multiply a fixed per-calibration cost at every input size in exchange for a gain
    that is largest where it matters least. **`DefaultMaxBufferedBytes` stays at 64
    MiB and `DefaultMaxMergeFanIn` stays at 16** (fan-in 32 moved elapsed time by
    +0.1% and -1.9% in the two sessions, noise in both directions; fan-in 4 costs
    40%). The measurements are in `docs/benchmarks.md`. That the two knobs' two
    consumers want different values is a real finding and is recorded there as a
    question for a later decision, not answered here.
- **Why:** M8's job is to find out how FcaBedrock behaves at 7.3M and 73M
  records, and a benchmark suite is only worth its cost if its numbers can be
  believed. Almost every clause above exists to close a specific way a number can
  be wrong rather than merely imprecise: a corpus that changed between two runs, a
  timed interval that excluded the flush, an oracle that agreed with the code
  because it *was* the code, a validation deferred until after the numbers were
  printed, an accidental 73M-record run from a broad filter, an allocation figure
  quoted as if it were peak memory, a Debug build, or an exit code of 0 over a run
  that measured nothing. Pinning BenchmarkDotNet's version and delegating every
  standard responsibility to it is the same discipline applied to the harness
  itself: the previous M8 attempts failed by growing process-containment,
  scheduling, and telemetry subsystems disproportionate to an internal benchmark,
  so the scope rule is that custom machinery must name both a real FcaBedrock need
  and a verified gap.
- **Rejected:** a floating BenchmarkDotNet version (the measurement semantics of a
  milestone's comparisons would drift with an upgrade: the D-075 hazard applied
  to numbers); a custom scheduler, process supervisor, polling/telemetry framework,
  scenario-plugin system, public benchmark framework, or threat model (no verified
  gap, and the failure mode of the abandoned attempts); benchmarking a nested CLI
  child process (it measures process startup and a containment problem, not
  FcaBedrock); a null sink presented as export throughput (reports a rate no user
  can obtain); an unversioned runtime pseudo-random corpus (`System.Random` may
  change algorithm between .NET versions, so a corpus could not be regenerated);
  implicit corpus generation inside a benchmark run (silently writes very large
  files and blurs the preparation boundary); keeping Adult in the required native
  jobs (repeating three cases on five runners adds platform coverage of a
  *data-dependent* path, not five independent datasets, and buys it by letting an
  outage at a research-data host block package delivery; the platform-specific
  risks are already covered by the layout witnesses, the ordinary suite, the Small
  smoke, and the package smokes); an `actions/cache` entry for the download (a cold
  or evicted cache still needs the host, and it widens the exact action allowlist
  for a partial mitigation; GitHub evicts after seven days without access);
  committing the dataset (CC BY 4.0 would allow it, but it overrides the approved
  no-large-data-in-Git rule and the documented promise that it never enters Git, to
  solve a dependency that simply need not be in routine CI); a scheduled or separate
  non-required real-data job (ongoing hosted evidence with a cadence and a
  maintenance cost, and no checking value that the explicit candidate run does not
  already provide; recorded as a follow-up if the manual run proves insufficient);
  pinning the zip rather than the consumed entry (a repack that changes no measured
  byte would be refused); and adopting a changed download's digest (it would
  silently re-base every figure recorded against the old bytes); deferring the only correctness check
  to global cleanup (one late failure covers every earlier iteration); deriving
  expected output by calling the pipeline (an oracle that cannot disagree proves
  nothing); a serializer-produced catalog (the metadata contract decides whether
  cached inputs are believed, so its bytes must not drift with a library:
  D-075's reasoning); making the benchmark host a test project or letting it
  inherit the shared test props (EP-20); granting `FcaBedrock.Benchmarks.Tests`
  CLI access (it needs none); switching the CLI benchmarks to the public
  executable (that changes the accepted measured boundary from host throughput to
  process latency) or copying the host's behaviour into the suite (weakens
  production-path validity); and exposing the grouping budget or fan-in as a
  public tuning flag (D-082/D-095/EP-6: a knob that cannot change output bytes
  belongs in neither the spec nor the public surface).
- **Affects:** `tests/FcaBedrock.Benchmarks` and `tests/FcaBedrock.Benchmarks.Tests` (new);
  `Directory.Packages.props` (the BenchmarkDotNet pin) and `FcaBedrock.slnx`;
  `src/FcaBedrock.Conversion` and `src/FcaBedrock.Cli` (benchmark-only `InternalsVisibleTo`, one
  comment each);
  `tests/FcaBedrock.Cli.Tests` (the exact-friend-set assertion, the gated self-contained publish
  smoke, `ConvertMultiCalibrationTests`) and `tests/FcaBedrock.Conversion.Tests` (runtime-observed
  resident-layout witnesses, `MultiAttributeCalibrationTests`); `eng/` (the publish/archive command
  and its README), `.github/workflows/ci.yml`, `docs/roadmap.md`, `docs/benchmarks.md`, `README.md`
  and a stale comment in `Directory.Build.props`. The **one** production behaviour change is the
  conforming calibration correction in `src/FcaBedrock.Conversion/QuantileAccumulator.cs` (the
  workspace-wide `CalibrationBudget.SpilledBytes` baseline, used by both consolidation entry points),
  with the `baselineT` contract documented on `ValueCountMerger.cs`; everything else under `src/` is
  the two friend grants and their comments. **No diagnostic, fingerprint, ordering, or output-byte
  change**: the registry stays **82**, and every golden, canonical-byte, and SHA pin is untouched.
- **Evidence replacement.** The correction invalidates the measurements that
  consumed the broken path, and they were replaced rather than reinterpreted.
  `ManyQuantileCalibrateWorking` and `ManyQuantileCalibrateScale7M` published
  **NA** in the baseline measurements (2026-09-06) because they failed; those reports are retained
  under their original identity as recorded failures, and both cases now carry real
  measurements. The count-sensitive calibration series
  (`CalibrateWide{Working,Scale7M,Scale73M}`,
  `CalibrateTriple{GroupedWorking,UnorderedWorking,UnorderedScale7M}`) and the
  auto-calibrated command series (`CliHostConvertAuto{Working,Scale7M,Scale73M}`)
  were re-measured against the corrected build. Fixed-plan emit, declared CLI,
  hash-wrapper, manifest, grouping-budget/fan-in, source-drain, planning,
  discovery and probe evidence is **not** invalidated: the diff reaches only the
  count-sensitive calibration merge baseline, which is absent from those timed
  paths, and that dependency is recorded in `docs/benchmarks.md` rather than
  assumed. The 64 MiB / fan-in-16 retention decision is unaffected: those cases
  run the declared T10 plan through `FirstAppearanceGrouping` and `RunMerger`,
  neither of which changed.
- **What the first native gate found (2026-09-08; see D-125).** The first five-target workflow run,
  at `a09e302`, **failed**, produced no artifact, and remains failed evidence at that revision. Both
  defects it exposed were pre-existing M7 release blockers that had never executed off Windows x64,
  and both are corrected under **D-125**; **no M8 benchmark, corpus, oracle, selection or identity
  test failed on any target.** That correction reaches the measured CLI-host interval, so this
  entry's CLI-host `Convert` measurements are superseded there rather than here; they keep their
  original provenance as the measurements of the revision that produced them.
- **Correction (2026-09-09): two of this entry's rules were stated correctly and implemented
  incompletely.** Neither changes what this entry decided; both bring the code to it, and the
  decisions above stand exactly as written.
  1. **The tier opt-in reached two of its three tiers.** "The three opt-in tiers are reachable only
     by naming their category" was implemented with an explicit branch for `Scale` and `External` and
     none for `Working`, so any other named category, such as the surface category `Source`, admitted
     every `Working` case; and because the command line had not named `Working`, those 730,000-record
     cases would have run under a throughput job's pilot stage instead of the bounded monitoring job.
     `Working` now has the same explicit branch, and all three tiers are swept for bare, broad-name,
     case-name and surface-only selection against the categories BenchmarkDotNet reads, with the
     resulting job asserted through the real configuration.
  2. **The delivery archive recorded the Unix apphost as non-executable.** `Compress-Archive`
     records `0100644` for every entry, so the Linux and macOS archives the successful `4216610b` and
     `03352da7` runs uploaded unzip a non-executable `FcaBedrock.Cli`, and the documented
     `./FcaBedrock.Cli` exits 126, `Permission denied`. **Neither run is relabelled**: workflow
     success is not the same claim as archive usability. The smoke had inspected a zip it created
     itself, not the one the workflow uploaded. The script now writes the archive entry by entry and
     records the apphost `0100755` with every other entry left `0100644`; the smoke drives that
     script, inspects the archive it produced, extracts **that exact archive** and makes every
     behavioural check against the extracted apphost; and the workflow uploads the file the smoke
     verified, so **the archive tested is the archive delivered**. The five-target matrix, the
     read-only permissions, the required/optional split, the RID and bundled-runtime checks and the
     path-safety rules are unchanged, and no product output byte, diagnostic or manifest is touched.
- **Correction (2026-09-10): that packaging fix stated only half of what the writer decides.** It
  assigned `ExternalAttributes` only inside the Unix branch, so a `win-*` entry kept whatever
  `ZipArchive.CreateEntry` defaults to on the *creating host*: zero on Windows, `0100644` on Linux and
  macOS. The native run at `163f1c49` found it as deterministic host-dependent metadata, not a flake
  or a runner issue. **The writer is corrected, not the test.** Relaxing the assertion or
  conditioning it on the creating host would preserve host-dependent metadata and contradict the
  archive contract, which is a claim about the **target**: a Linux or macOS target
  records the apphost `0100755` and every other entry `0100644`; a Windows target claims no Unix mode,
  so every entry's high mode field is explicitly zero; and neither answer varies with the machine that
  built the zip. Every entry is now assigned from the target's RID, and the shared archive validator
  requires that zero on **every** entry of a Windows distribution rather than skipping the check. The
  reach is exactly that field on `win-*` archives written by a non-Windows host: native Windows
  delivery is semantically unchanged, archiving one fixed folder with the old and the new writer on
  Windows yields byte-identical zips, and every Unix mode is unchanged. This is not a claim that a ZIP
  is byte-reproducible across hosts or rebuilds. No public API, CLI contract, diagnostic, spec text,
  output byte, manifest schema or runtime product code changes, and the registry stays **82**.
- **Outcomes (2026-09-10 to 2026-09-12).** The corrected writer's five-target run at `c4ceb8e2`
  passed on all five native targets with every `DistributionArchiveTests` case executed on every
  target, and its three archives carry exactly the modes the 2026-09-10 correction requires. The
  blocking real-data obligation was met at `82e2ffea` by an offline replacement run, after a first
  attempt at the same commit crossed an explicit no-network rule and did not close it; the pin, the
  acquisition revision, the routine-CI exclusion and the three-failure-mode rule did not change. After
  the 2026-09-11 correction below, the probe validation, the Adult acceptance and the native/archive
  gate were met again at `3b2e4a80`, where M8's evidence closes (D-126). Each result closes its
  obligation at its own revision only: the real-data obligation re-attaches at every later release
  candidate and is not a standing exemption.
- **Correction (2026-09-11): three oracle and validator defects, found by an exhaustive review of the
  whole branch.** None changes what this entry decided; all three bring the code to it, and the
  decisions above stand exactly as written.
  1. **The Adult plan-shape oracle proves its stated guarantee per planned attribute.**
     `AdultOracle.RequirePlanShape` had asserted 14 planned attributes plus an aggregate
     `FormalAttributes.Count >= 14`. Adult's nominal and manual-cut attributes each produce several
     columns, so one attribute could carry an empty `CrossesByBin` and no `MissingFormalAttributeId`
     while the aggregate stayed far above 14: the silently empty attribute the helper claimed to catch,
     and a self-confirming check of the kind this entry's oracle-independence rule forbids. It now
     proves, **per planned attribute**, that the union of its `CrossesByBin` ids with its
     `MissingFormalAttributeId` is non-empty, that every claimed id resolves in the plan's schema, that
     the resolved column's `Identity.AttributeName` equals that attribute's own name, and that no
     column is claimed by two attributes; the aggregate count survives only as an explicitly labelled
     secondary guard. A recognized bin that crosses nothing, a dichotomic false pole, remains
     legitimate, because the rule is over each attribute's **union**, never over each bin.
  2. **The probe outcome oracle is per attribute and exact.** D-108 is per attribute: an untruncated
     non-all-missing attribute authors its complete non-empty domain, and a truncated one authors its
     retained prefix plus `unknown_value_policy = "include"`. The oracle now requires every
     attribute's complete non-empty domain, subject only to an explicitly supplied all-missing
     exception set; requires the observed truncated set to **equal** the expected one, with every
     member carrying its recovery and no unlisted attribute carrying one; and requires a guard breach
     to be **exactly one** `ProbeLimitExceeded` at the production `Error` severity with no draft. The
     expected truncated sets are derived from the **frozen corpus generator** at D-108's
     strictly-greater boundary, never from the prober under measurement, and every check still runs in
     `[IterationCleanup]`, outside the measured interval.
  3. **The Windows archive validator compares the raw field before any shift.**
     `DistributionArchive.AssertValid` had shifted `ExternalAttributes` right by 16 before requiring
     zero, so a raw `0x00000001`, the DOS read-only bit, passed although the 2026-09-10 correction
     states the contract as **exact raw zero on every entry of a Windows distribution**. The `win-*`
     branch now compares the **raw** field before any shift and renders the offending value as
     `0x{…:X8}`; the shifted mode and symbolic-link handling stay on the Unix path, which is unchanged.
     Archive *production* was already correct, writing literal `0` for every non-Unix entry, so no
     delivered archive was ever affected, and the retained `c4ceb8e2` artifacts independently satisfy
     the corrected rule.

  The correction is committed as signed `3b2e4a80` (sole parent `d4b310ad`), entirely within
  `tests/FcaBedrock.Benchmarks`, `tests/FcaBedrock.Benchmarks.Tests` and `tests/FcaBedrock.Cli.Tests`.
  **No production source, spec text, diagnostic, severity, registry entry, public API, benchmark
  meaning, measured interval, job, category, corpus byte, packaging script, workflow or output byte
  changes**; the registry stays **82**, and no earlier measurement is invalidated: strengthening a
  validator changes what a run *proves*, not what it *does*. Because no earlier benchmark, Adult or
  native run executed the corrected checks, the probe validation, the Adult acceptance and the
  native/archive gate were rerun at `3b2e4a80`.

---

## M7 publication ownership (corrected under M8's first native gate)

### D-125: Lifetime-bound publication ownership, the exclusive-first native rename and its guarded fallback, the stated namespace precondition, and the closed package-metadata producer set

- **Status:** accepted (semantic amendments A and B approved by Constantinos Orphanides on
  2026-09-08, after an independent bounded review). It **qualifies the reach** of D-122 parts 4–5 and
  D-123 point 7 rather than restating them, and corrects the two defects M8's first native run
  exposed. D-122/D-123 are not rewritten: they said what they said, and this entry records what
  holds. *Corrected 2026-09-09 by D-126:* its measurement claims (see *Evidence*).
- **Date:** 2026-09-09
- **Decision:** publication owns objects by **lifetime**, not by remembered
  identifier, and renames through the platform's **exclusive** primitive with one
  narrowly guarded fallback. In seven parts.

  **1. The defect this corrects.** Publication captured an object's OS identity
  (Unix `dev`/`ino`, Windows volume plus `FILE_ID_INFO`), **closed the handle**, and
  re-checked that identity later, by path, to authorize a commit, a backup rename, a
  restore or a removal. An identifier is unique only among objects that exist **at the
  same time**: unlink a file's last name on ext4 and its inode is immediately free, so
  the next creation can be handed it. A substitute therefore compared **equal** to the
  object the transaction had staged. The consequences were not theoretical: all three
  were reproduced on ext4 with recorded identifiers. A substituted manifest was
  published at exit **0** as the run's public commit marker; a substituted stage was
  published **with a manifest certifying its hash**; and a foreign file was **deleted**
  by a recovery whose proof the recycled identifier satisfied.

  **2. A: the supported namespace, and the limits that remain (approved).**
  FcaBedrock requires its output participants, their containing-path resolution and
  its private recovery state to be **exclusively managed by the invocation and left
  undisturbed after an interruption until recovery**. Other programs may read
  completed outputs; they must not rewrite this namespace during publication or
  recovery. **`--force` does not waive it.** Within an invocation, live references
  detect distinct observable substitutions and stop identifier reuse from defeating
  the proof. They cannot say what happened while a crashed invocation held no
  reference: two histories (an untouched interrupted file, and a deletion followed by
  a same-identifier replacement, byte-identical or not) present identical durable
  evidence, and no hash, length, timestamp or process-local lease recovers the missing
  history. Arbitrary competing writes in the intervals named in part 5, and arbitrary
  namespace replacement across a crash, are **outside** the guarantee. Every
  observable mismatch still refuses mutation and preserves unknown objects under the
  existing classification and recovery rules.

  **3. Live references.** Every object whose identity authorizes a later mutation is
  **held open** from the moment that identity is captured until its last authorized
  use. A stage's reference is acquired while its creation handle is still open and
  **proved equal to it**; an existing participant's is acquired through the open that
  approves it; recovery acquires every reference its decision pass needs **before** it
  mutates any earlier participant. **An identity is reported only when it is
  anchored**: where a reference cannot be taken or proved, no identity is reported at
  all and the existing fail-closed path refuses the run, exactly as a host with no
  identity capability is refused. Acquisition or proof failure grants no mutation
  authority. A reference is never released and re-acquired by name: a verified rename
  **re-keys** it. These are **object-lifetime references, not writer streams or reader
  locks**; the writer's flush-and-close precommit boundary is exactly where it was.
  - **Windows** uses a non-inheritable, attribute-only open (`FILE_READ_ATTRIBUTES`,
    sharing read/write/delete). An open requesting none of read-data, write-data or
    delete access does not take part in share-access checking, which is what lets it
    coexist with the `FileShare.None` creation handle, the later data-read
    re-observations, and the share-mode-zero handle the removal deletes through.
  - **Unix** uses a raw close-on-exec `open(O_RDONLY)`. A managed open is
    specifically excluded: .NET emulates `FileShare` with advisory locks, so it would
    take `LOCK_SH` and make `RemoveUnix`'s own `LOCK_EX` fail.
  - **Release sequencing is part of the contract.** The Windows disposition names the
    **object** and takes effect when its last handle closes, so a retained reference
    keeps a completed deletion pending and the name occupied. The reference overlaps
    the removal handle for the whole of its life (that is what transfers the proof)
    and is released the instant the removal reports success, which completes the
    deletion and frees the name for a restore that may follow. Because the disposition
    is bound to that object, releasing can delete nothing else. No sleeps, forced
    collections, reference gaps, test-only handles or new deletion modes are used.
  - **The invariant is uniform on Windows and Unix.** NTFS and ReFS ids carry a
    sequence component, but the filesystem contract does not promise an identifier is
    never reissued, and one lifetime-based rule is worth more than a
    filesystem-specific exception. The cost is explicit: Windows retention reaches the
    measured CLI-host interval, so the reachable CLI-host measurements are
    **invalidated and rerun** (see *Evidence*).

  **4. B: exclusive-first native rename, and the exact fallback (approved).** Every
  commit, backup, restore, compensation and private publication rename is a
  **same-directory, same-filesystem native metadata rename**, never a content copy, a
  clone, a link/unlink pair, a copy/delete pair, a destination pre-delete, or a managed
  `File.Move` (whose Unix non-overwrite path performs its own check-then-rename and can
  end in `link`+`unlink` or copy+delete). The same-directory precondition is enforced
  structurally: a cross-directory request is a contract fault and never reaches a
  native call.
  - **Linux** calls `renameat2` with exactly `RENAME_NOREPLACE`. `EEXIST` is a
    collision and **never** falls back. `EINVAL` is the capability answer: the VFS
    contract requires a filesystem to return it for a flag it does not support, and the
    invocation is valid by construction (one fixed flag, a correctly bound entry point,
    two distinct sibling leaves in an already-resolved and guarded parent).
    `ENOTSUP`/`EOPNOTSUPP` (one errno, 95) and a directly reported `ENOSYS` also permit
    fallback; a libc wrapper may fold kernel `ENOSYS` into `EINVAL`, which the row
    above already accepts.
  - **macOS** calls `renamex_np` with exactly `RENAME_EXCL`. `EEXIST` is a collision.
    **Only the documented `ENOTSUP` (45)** permits fallback: Apple documents `EINVAL`
    as an invalid flag rather than an unsupported one, and neither Darwin's `ENOSYS`
    (78) nor its distinct modern `EOPNOTSUPP` (102) is documented as this API's
    capability answer. Linux's readings are **not** copied onto Darwin.
  - **Every other result** (access, permission, I/O, read-only, cross-device, missing
    source, invalid source or type, interruption, stale, quota or space, invalid
    handle, import or ABI) is an operation failure and **never** permission to switch
    primitives. In particular `EXDEV` never permits a copy. A missing or misbound
    import is a contract fault, never a fabricated `ENOSYS`. Errno is captured
    immediately from the indicated call and never inferred from an exception message,
    an operating-system or filesystem name, a failed existence check, or a later
    operation. Permission is **local to the attempted operation**: never cached, and
    never disabling the exclusive primitive for another volume or a later one.
  - **Windows** keeps native no-replace semantics through `MoveFileExW` with **no
    flags at all**: neither `MOVEFILE_REPLACE_EXISTING` nor `MOVEFILE_COPY_ALLOWED`.
    There is no Unix-style fallback, because there is no capability gap: no-replace
    *is* the primitive. Existing-destination and every other failure keep their current
    handling, and the extended-path prefix is applied exactly as the managed wrapper
    applied it, so no path's reach changes.
  - **After exactly one permitted capability result**, the guarded fallback keeps the
    authorizing references and: (1) revalidates the source against the live reference,
    because a failed exclusive attempt is not evidence the namespace stayed still;
    (2) establishes destination **entry** absence immediately before the rename with
    native `lstat`, where **only** a missing destination leaf establishes absence and
    **any** entry (an ordinary file, a directory, or a dangling symbolic link) is a
    collision, while any other lookup result refuses the operation (`File.Exists ==
    false` is insufficient, and cannot see a dangling link at all); (3) invokes
    **exactly one** classic flagless `rename`, with no retry, no second fallback, no
    placeholder and no pre-delete; (4) verifies the destination against the original
    retained reference, preserving the existing input checks, commit order,
    compensation decisions, diagnostics and recovery direction. Reverse compensation
    runs under these same rules, preserving a substitute rather than adopting or
    deleting it; where compensation cannot safely restore state it keeps classification
    authority and unresolved objects and reports failure.

  **5. The three limits, stated rather than repaired.** (a) The fallback's
  absence-check-to-rename interval is **not** atomic no-replace: an actor violating
  part 2 can create the destination inside it, and the classic rename will replace that
  entry. This applies at **every destination role** (a public artifact or manifest, a
  backup or private control name, and a source name used as a compensation
  destination), and the overwritten entry may have no surviving link and no transaction
  backup. A post-move identity match proves **which source object arrived**; it does
  not prove the destination stayed absent, restore an overwritten foreign entry, or
  certify that nothing was transiently exposed. (b) Unix has no portable
  compare-and-delete by descriptor (no `funlink`, and `unlinkat` still takes a name),
  so the analogous proof-to-unlink interval remains. (c) Neither reaches across a
  crash, per part 2. Windows has none of the three: its rename is no-replace and its
  deletion is handle-bound.

  **6. Preserved unchanged.** Manifest-last commit order and the manifest as the public
  commit marker; canonical CXT/DAT order; no-manifest parity; previous-set restoration
  where possible; rollback direction; code-less host failure; cancellation and fault
  classification; confidential stages; input, hardlink and symlink collision
  protection; unknown-object preservation; retry convergence; and post-commit cleanup
  semantics. Repeated retries still refuse without consuming unknown residue. **No
  product output byte, diagnostic code or message, exit meaning, manifest schema or
  ordering, fingerprint, record vocabulary, residue name, target-safety constant,
  supported-RID policy, benchmark meaning, corpus byte or tuning default changes.** The
  registry stays **82**. No public API is added: the new types are `internal` to
  `FcaBedrock.Cli`. No cross-file atomicity, and no protection for an unobserved object
  introduced during an excluded race, is newly claimed.

  **7. The package metadata's closed producer set.** `ToolPackTests` pinned the
  core-properties leaf to a lowercase 32-hexadecimal GUID-N stem. That was one
  producer's choice, not a package contract: NuGet through SDK 10.0.302 emits a GUID,
  and from SDK 10.0.400 it hard-codes `nuget.psmdcp` (NuGet.Client change `5834c6b9`,
  which fixed a deterministic-pack file-handle leak). The accepted part is now the
  **closed two-producer set** (the exact existing directory, one leaf with no nested
  component, a stem that is either a lowercase GUID-N or the ordinal literal `nuget`,
  and a lowercase `.psmdcp`) and **not** `*.psmdcp`, which would admit an arbitrary
  metadata part, `CON.psmdcp` among them. Because a canonical name is not
  discoverability, the OPC wiring is validated too, from **bounded** reads with **DTD
  processing prohibited and no resolver**: exactly one package core-properties
  relationship whose internal target resolves to that exact part, the NuGet manifest
  relationship targeting `FcaBedrock.Cli.nuspec`, duplicate identifiers and
  relationships, external targets, traversal and encoded aliases and dangling targets
  all refused, the effective content type
  `application/vnd.openxmlformats-package.core-properties+xml` required with
  conflicting declarations refused, and an OPC `coreProperties` root whose identifier
  and version agree with the nuspec. Nothing pins relationship identifiers, the producer
  version, timestamps, archive order, metadata bytes or whole-package reproducibility.
  Package producer inputs are unchanged and **no SDK is pinned**. This changes no
  shipped payload, extraction-safety guarantee, public API, product diagnostic or
  product output.
- **Why:** the exact-object promise D-122 and D-123 make ("never deletes unknown
  lookalikes", a manifest that certifies the bytes this run wrote) was resting on a
  value the operating system is free to reissue. M8's first native run was the first
  time any of it executed off Windows x64, and it found exactly that. The correction is
  deliberately about **lifetime rather than comparison**: holding the object open makes
  every proof already in the code sound, without changing what any of them compare. The
  namespace precondition and the fallback are recorded as approved semantic amendments
  rather than filed as conforming maintenance, because both genuinely narrow what an
  unqualified reading of D-122/D-123 promised: the first by excluding a hostile
  writer, the second by admitting a non-atomic interval on filesystems with no
  exclusive rename. Representative, version-qualified environments that may take the
  fallback include Linux NFS and 9p implementations that reject rename flags (both
  return `EINVAL` at the top of their rename functions in v6.12), FUSE configurations
  lacking the flagged operation, and macOS volumes without `VOL_CAP_INT_RENAME_EXCL`.
  Windows-backed WSL mounts must be assessed from actual operation results; **path
  spelling is not a capability test**. These are examples, not an allowlist, a
  denylist, a dispatch table, or a promise that every mount succeeds.
- **Rejected:** *blanket refusal* on any filesystem without an exclusive rename
  (an earlier draft clause, withdrawn): it would refuse to publish at all on mounts
  that publish correctly today, to guard against an actor part 2 already excludes, and
  it is the same category of race the accepted unlink interval already admits; a
  **managed Unix `File.Move` fallback**, whose non-overwrite path is a
  check-then-rename that can end in `link`+`unlink` or copy+delete, so the seam's
  "never a copy" promise would stay untrue; retaining the .NET Windows wrapper's
  `MOVEFILE_COPY_ALLOWED` (defensible, since a same-directory rename cannot cross
  volumes, but it leaves the no-copy claim resting on an argument rather than on the
  call); **more evidence instead of lifetime** (content hashes, lengths,
  `mtime`/`ctime`/birth-time heuristics, extra `stat` calls, sleeps or a process-local
  lease), none of which can distinguish a byte-identical replacement; **ext4 inode
  generation**, which is filesystem-specific and fails closed on tmpfs and network
  mounts; a **durable private-link ownership protocol**, which would need its own
  residue role, validation and namespace contract and fails closed where hard links are
  unavailable; **fail-closed recovery** that never restores or removes a final;
  exempting Windows from retention (a filesystem-specific exception in place of one
  invariant, and NTFS makes no never-reissued promise either); a **general filesystem or
  transaction framework**, a public API, a new record role, residue name, sidecar or
  package dependency; **test-only lifetime protection**, which would manufacture a green
  Linux result while leaving production defective; accepting `*.psmdcp`; and **pinning
  an older SDK** to keep the GUID leaf.
- **Affects:** `src/FcaBedrock.Cli/Publication/` (new `PublicationNative` and
  `PublicationObjectReference`: the native primitives behind one injectable seam, the lifetime
  reference and its per-transaction registry; the seam and the `IDisposable` transaction acquire,
  transfer and release references), `src/FcaBedrock.Cli/Commands/` (deterministic disposal on every
  exit), `FileIdentity.cs` and `StageEvidence.cs` (identity comments), and
  `tests/FcaBedrock.Cli.Tests` (new rename, reference and package-OPC tests; the tool-pack, harness
  and publication tests). Docs:
  spec §§15 and 16.2, `src/FcaBedrock.Cli/README.md`, `docs/benchmarks.md`, `docs/roadmap.md`, and
  D-124's evidence note.
- **Evidence.** Required, and none of it inferred from an outcome alone.
  **Before-fix corroboration** on WSL2 Ubuntu 24.04.4 ext4 (.NET SDK 10.0.401) recorded the actual
  `dev`/`ino` values around each replacement, with controls proving that a **closed** original's
  identifier is reused and a **held** original's is not; against the corrected build the identifier
  is no longer reused, the run exits 1 where it had exited 0, no public marker appears, and the
  foreign object is preserved. **Focused proof** covers the capability table for
  both platforms as a pure rule; `EEXIST` and ordinary failures never falling back;
  exactly *exclusive → lookup → classic* after a permitted result, with no pre-delete,
  placeholder or retry; entry-aware collisions including a directory and a dangling
  symbolic link; a collision on a compensation destination; the excluded destination
  race recorded as the disclosed limit it is; the no-copy identity proof against a
  still-open original with a byte-identical copy as the negative; the full reference
  lifecycle through create, acquire-while-open, flush/close, re-observe, rename,
  proof-bound removal and **removed-name reuse**; and a byte-identical stage
  substitution refused. Substitution tests assert the deterministic hook **fired**,
  because before the correction the same case could pass or fail on identical code
  depending on the allocator's history. **Native fast-path witnesses** record the test
  volume and the primitive that ran, never inferring it from success.

  **Measurements (corrected 2026-09-09 by D-126).** Uniform Windows retention reaches the measured
  CLI-host interval, so the CLI-host `Convert` Small, Working, 7.3M and 73M cases, both sides of the
  sidecar comparisons, and all six actual-command traces stop describing the shipped code. The
  originals keep their provenance and are not overwritten; component measurements that do not reach
  `PublicationTransaction` keep theirs; and the native run at `a09e302` remains **failed** evidence,
  which nothing here relabels. As first written, this paragraph drew three conclusions from one
  machine state and one rounded reading, and D-126 corrected all three. Only the measurement claims
  moved: the publication semantics of parts 1–7, the approved amendments A and B, the namespace
  precondition, the capability matrix and its guarded fallback, the three stated limits, the package
  producer set, and every D-122/D-123 contract this entry qualifies are **unaffected**.
  1. **Allocation is not byte-identical on both builds.** That reading came from the reports' rounded
     `1.04 GB` column and is withdrawn, with its inference that a native handle allocates nothing
     managed: the lifetime reference, its per-transaction registry and the path bookkeeping around
     them are ordinary managed allocations. Within one machine state the four Working publication
     forms allocate **+2,880 to +9,416 bytes** more per published run than their paired unchanged
     control, on operations of 1.04 GB to 3.23 GB in BenchmarkDotNet's binary units, apart from one
     preserved anomalous contrast of **-99,080 bytes**, which that block's abnormally high *control*
     reading produces and which is **not** a candidate saving. Across sessions the fifteen
     corrected-build rows differ from their historical totals by **-98,936 to +17,984 bytes**; the two
     negative 73M values are descriptive cross-session measurement observations, **not savings**. No
     allocation investigation trigger fired on any of the fifteen rows. Allocation is
     near-deterministic but **not machine-state independent**, so it is reported as exact integers and
     ranges with their denominators, never as a single value and never inferred from a rounded unit.
  2. **The "about 10% slower with three times the deviation" explanation describes one machine
     state.** It was true of the machine on 2026-09-09 around 01:00 and stands as that dated
     observation. The same *unchanged* pre-fix control later measured **5.3–8.5% faster** than its
     own baseline rows at *tighter* dispersion, and inside the pre-registered paired comparison it
     **failed to reproduce itself within 5% across its own six launches** of
     `CliHostConvertBothWorking` (control spread 1.071382). The control therefore disqualified the
     machine in **both** directions, which supports, more strongly than either reading alone, the
     original conclusion that the machine was not in the controlled state those rows were taken in,
     so an absolute number taken there would measure the state rather than the code. None of this is a
     statement about the correction's cost.
  3. **"Invalidated and rerun" (part 3) is half true, and permanently so.** The reachable CLI-host
     measurements were invalidated; **no controlled absolute replacement was ever taken**, and under
     D-126 none is owed. The pre-registered paired comparison that would have licensed a
     performance-continuity claim ran in full and **failed its collective test**
     (`CliHostConvertWideWorking` one-sided 95% upper limit 5.6809% > 5%; `CliHostConvertBothWorking`
     control stability 1.071382 > 1.05), so the correction's incremental elapsed-time effect is
     **inconclusive at the 5% bound**: never neutral, never a non-regression, never "probably below
     5%", and never a speedup. What exists for the corrected build instead is **allocation and
     per-iteration output validation for all fifteen CLI-host cases**, including all three 73M cases,
     and **six corrected-command resource traces**. The CLI-host rows of the baseline measurements and
     the re-measurement after the calibration fix, and the six historical traces, keep their original
     provenance as observations of the revisions that produced them and are never attributed to
     `4216610b`.
- **Correction (2026-09-09): part 3's acquire-before-mutate rule was implemented for the running
  transaction and not for a resumed one.** The rule itself is unchanged and correct as written; what
  follows is the code being brought to it.

  A resumed recovery pass called the reference registry's `Ensure` for each participant and then
  **ignored what it answered**: a null answer, which means the caller must not act on that identity,
  was followed by removals, restores and re-renames anyway. A transient capability, permission or
  sharing failure, the one case the rule exists for, therefore left the pass acting on an identifier
  the host is free to have reissued: exactly the recycled-identifier class part 1 records.

  What holds it now is an anchor check in front of each pass, plus a requirement each mutating
  primitive states for itself. **Intent-only cleanup** anchors its pending record *and* its
  descriptor before either removal. **Resumed `Preparing` cleanup** anchors every stage and backup
  entry the record lists before its data cleanup removes any of them. **Forward and rollback
  recovery** anchors every final, backup and stage participant that its aggregate direction question
  and its target-decision pass require, before that direction is decided and before any target is
  mutated. Both orderings are required: the ownership question is what authorizes forward cleanup
  over rollback, and anchoring per target as the pass reached it would already have removed the first
  target's final before discovering it could not hold the second target's backup. A participant that
  is present and cannot be held ends the pass with the location exactly as it was found. Absence stays
  absence, so the ordinary idempotent case is unchanged, and an unanswerable existence question reads
  as present and so fails closed too.

  **Every destructive removal, including the ordered marker, evidence and record cleanup,
  independently refuses to act unless it holds the object at its own mutation boundary**, so a later
  call site cannot reintroduce the gap by forgetting a check; and the restoring rename **separately
  requires its source anchor and proves that anchor is still at the name it is moving**, rather than
  re-acquiring the destination by name. **Control cleanup keeps its existing durable, incremental
  order** (most advanced phase first, stopping at the first deletion that fails) and is deliberately
  *not* pre-anchored as a set: each control object is held at the removal that acts on it.

  A refused pass reports the existing code-less "cannot clean up an incomplete fcabedrock run" host
  error at exit 1, preserves the residue byte for byte, and answers the same way on every retry; no
  diagnostic, exit meaning, record or residue vocabulary, ordering, manifest byte, output byte or
  target-safety constant changes, and no public surface is added. The proof is deterministic and per
  participant (intent-only cleanup, resumed `Preparing` cleanup, forward cleanup after commit
  evidence, rollback with restore, and a two-target case in which the *later* target's reference
  cannot be taken and the earlier one is untouched): each suppresses one participant's acquisition,
  asserts the suppression fired, and ends by showing the same state converging under an ordinary
  retry.

---

## M8 evidence policy

### D-126: Limitation closure: the publication correction's incremental latency is inconclusive at the 5% bound, and the corrected-build allocation, validation and resource evidence is complete instead

- **Status:** accepted (an evidence-policy amendment, independently reviewed, then approved by
  Constantinos Orphanides on 2026-09-09). It **amends D-124's mandatory invalidation/recheck rule for
  D-125's already-implemented correction only**, and corrects the stale live status of D-124 and the
  measurement claims of D-125. It reopens no publication, benchmark, spec or packaging decision, and
  waives no other M8 obligation. Conditions (c) and (d) were met at `50f6aa62` on 2026-09-10, the
  fresh implementation review at `c4ceb8e2`, and M8's evidence closed at `3b2e4a80`, whose probe,
  Adult and native runs took place on 2026-09-11 and 2026-09-12.
- **Date:** 2026-09-09
- **Decision:** M8 may close with an **explicit, permanent measurement limitation** in
  place of a quantified incremental-latency bound for D-125's publication correction.

  **1. Why the roadmap permits this.** M8's exit is "documented throughput/memory at
  target scale; no full-matrix materialization", and its M7-cost clause says M8
  **measures** inline input-stability and manifest hashing. Neither names a latency
  threshold. The 5% figure enters only through the base plan's **optimization-adoption**
  rule (a decision aid for whether to *change* a default, which M8 ran and did not meet;
  D-124) and through the later comparison's own pre-registered bound, which existed to
  earn a performance-continuity claim rather than to satisfy the roadmap. **EP-19** governs
  claimed *improvements*; D-125 is a correctness correction, governed by EP-7/EP-11. The
  failed comparison therefore forfeits that continuity claim; it does not expose a
  previously unstated roadmap requirement that every correction prove a sub-5% cost.

  **2. What was attempted, and exactly how it failed.** A four-form paired campaign was
  **pre-registered before any measurement**, its criterion frozen and hashed before the first
  invocation and re-hashed identical afterwards, then executed exactly as specified: twelve
  interleaved invocations of the unchanged pre-fix control and the corrected candidate in a fixed
  six-block order, forty-eight case reports agreeing on job, runtime, SDK, BenchmarkDotNet version, OS
  and instrumentation, every iteration validated after disposal. The test was **collective** (part
  3's "collective gate"): all four forms had to pass every applicable check. Two failed. For Wide,
  the one-sided 95% upper limit `U` was **5.6809%** against the 5% bound; for Both, the unchanged
  control's `max/min` was **1.071382** against 1.05. No-manifest and CXT passed every check. All four
  rows are published together in `docs/benchmarks.md`.

  The two failures have different characters and neither implicates a code path: Wide's `U` is
  carried over 5% by a single iteration in one candidate launch that ran ~200 ms above its four
  siblings **at identical GC counts and an allocation total inside the same 1,592-byte band as its
  five siblings**, and Both's failure is a **control-arm** stability failure, the *unchanged* pre-fix
  build having moved 7.1% across its own six launches. **No block was discarded, no alternative bound
  computed, no margin widened and no sample added**; the statistic is reported exactly as it fell. The
  small point estimates are **not** offered as a bound or as evidence of neutrality.

  **3. Disposition: complete, failed, inconclusive, no retry.** The experiment's status is
  **"attempt complete; collective gate failed; incremental elapsed effect inconclusive at
  the 5% bound; no retry is required for M8 under limitation closure."** Not "bridge
  passed", not "bridge pending" (which would imply an owed retry), and not
  "measurement-neutral". The criterion and result are preserved unedited and are never
  retroactively passed; any later attempt would be a new pre-registration, not a resumed
  or enlarged campaign.

  **4. Claims surrendered, permanently.** No equality, byte identity, neutrality,
  non-regression, speedup, "probably below 5%", exact final-build publication or sidecar
  overhead, new absolute CLI baseline, all-tier equivalence, or reached-path
  performance-continuity inference. The historical CLI-host rows (the baseline measurements and the
  re-measurement after the calibration fix) and the six historical traces describe **their measured
  revisions only**. No corrected-build elapsed figure may be published as a result table or
  converted into records/s, MiB/s, a scaling curve, an overhead percentage, sidecar or
  auto-versus-declared timing arithmetic, or any old/new subtraction; corrected-build elapsed output
  is retained as **contextual raw data only**.
  Trace wall time is instrumented command duration in its provenance record, never
  BenchmarkDotNet performance evidence.

  **5. The five conditions, and their state.** Limitation closure is available only when
  all five hold:
  - **(a) fixed-participant, unreachable premise: holds.** The `a09e302..4216610b`
    correction inventory is exactly 29 paths (23 modified, 6 added), confined to
    `src/FcaBedrock.Cli/`, `tests/FcaBedrock.Cli.Tests/` and four docs. **Nothing** under
    Sources, Conversion, Export, Core, Spec, Discovery or `tests/FcaBedrock.Benchmarks`
    changed, so the correction is unreachable from the Sources, calibration,
    grouping/fan-in, planning, emit/export, probe, hash-wrapper and tuning measurements.
    Those keep their original provenance and their conclusions stand at their
    original scope. This is **not** a claim that all prior evidence was invalidated, nor
    that all current performance was remeasured.
  - **(b) the failed comparison preserved and prominently reported: satisfied.** All four
    rows are published together as one failed collective test in `docs/benchmarks.md`,
    with both failed checks named by value and no selected passing-case conclusion.
  - **(c) all fifteen corrected CLI-host cases carrying original-job allocation and
    per-iteration validation, including all three 73M cases: met at `50f6aa62`.** All fifteen
    were reacquired **together** on 2026-09-10 at the corrected candidate: one ordinary invocation
    apiece, in the required order, each archived immutably before the next began; every completed
    measured iteration passed its existing post-disposal validator; **no allocation investigation
    trigger fired** on any row; and all three 73M rows ran their real 1-warmup/3-measured policy. The
    earlier reading of this condition, from the first corrected-build checks at
    `4216610b`/`03352da7`, remains valid historical evidence of those revisions, neither withdrawn
    nor relabelled, but it is **not** what satisfies (c) here; part 9 records why the reacquisition
    was required.
  - **(d) all six corrected actual-command traces complete with their real
    output/manifest validation and resource provenance: met at `50f6aa62`.** All six were
    reacquired at the corrected candidate on 2026-09-10. The earlier traces at
    `4216610b`/`03352da7` likewise remain valid historical evidence at their own revisions and are
    **not** what satisfies (d) here.
  - **(e) every claim naming its session, revision, validation strength and measurement
    meaning: satisfied**, and enforced by the wording rules in part 4.

  No unresolved product, allocation or resource investigation is waived by any of this: an
  open trigger would still stop closure.

  **6. A disclosed acquisition-boundary change.** When an evidence run must change how it acquires
  data, for example by moving the grouping spool to another volume because the system volume lacks
  space, the change is disclosed with the evidence rather than hidden or worked around. It is allowed
  here only because this policy has already surrendered cross-session latency comparability: such a
  run never supports a timing comparison, the other volume is never described as faster or slower,
  and a control and its comparison must share one acquisition boundary. The corrected build's 73M
  triple traces were taken this way; their outputs reproduced the earlier retained outputs byte for
  byte, and the product removed its own spool.

  **7. Oracle boundaries, stated exactly.** The declared, CXT and both-format cases carry
  **independent byte expectations** derived from the corpus definition plus documented
  spec semantics. The auto cases carry only the narrower checks (expected subject count,
  the `ObservedDomainUsed`-only diagnostic policy, manifest **presence**, and intra-run
  byte determinism), plus external digest continuity with the re-measurement after the
  calibration fix, supplied by the actual-command traces. That continuity is **regression
  evidence, not an independent quantile-semantic oracle**. BenchmarkDotNet's manifest check is
  **presence/absence only**; full manifest/input/output hash consistency is carried by the traces,
  which recompute it independently. No validator was weakened, redesigned or filtered anywhere.

  **8. What resource evidence means.** Every reported working-set, GC-heap and
  GC-committed figure is a **sampled maximum at a one-second interval**, with its sample
  count and coverage disclosed and `dotnet-counters` profiling overhead present. A sampled
  maximum is a **lower bound** on the true peak (never an exact peak and never a portable
  ceiling), and within-session size ratios are descriptive observations, not scaling laws.
  Exit zero, a flat resident set, a BenchmarkDotNet allocation total, and the wide
  investigation trigger's failure to fire each prove **no streaming guarantee** on their
  own; the algorithmic bound remains carried by the independent observer and
  retained-layout witnesses, which this evidence reconciles with rather than replaces.

  **9. Evidence carriage and admissibility.** *(As first recorded on 2026-09-09, this part listed the
  obligations that then remained; the Status line records how they were met. The rules below were
  settled between 2026-09-09 and 2026-09-12, as the evidence was carried to later revisions.)*
  - **Carriage is a finding about one exact diff.** Evidence recorded at one revision carries to a
    later one only after that change's exact diff is shown not to reach the measured path. That is a
    bounded reachability conclusion about that diff: never a standing exemption, never binary
    equality, and never carried to a further change without its own check.
  - **The recovery correction was not failure-only.** `Commit` ends by calling
    `Finish(forward: true)` inside `CliHost.RunAsync("convert", …)`, the interval the CLI-host cases
    measure, and the correction adds one `File.Exists` for each absent stage and a held-reference
    lookup at each successful post-commit removal there. Bounded and small is not zero, so the rows
    and traces taken at `4216610b`/`03352da7` stayed valid evidence of those revisions but did not
    satisfy (c) or (d) for the corrected candidate. All fifteen rows were reacquired **together**,
    because (c) is one coupled allocation-plus-per-iteration-validation proof of the actual
    candidate, at the same oracle strengths, and all six traces with them, at `50f6aa62`. The
    component measurements, the **64 MiB / fan-in-16** tuning conclusion and the Windows x64
    External/Adult acceptance carried across that correction, because publication is unreachable from
    the component paths and the Adult cases run `ConversionRun` rather than `CliHost`.
  - **The later packaging correction** reached only the packaging script, two test-support files and
    three advisory documents, none of which the CLI-host cases, the traces or the Adult cases execute,
    so (c) and (d) stayed met at `50f6aa62`. The native archive gate did not carry, because the
    packaging writer is exactly what changed.
  - **A documentation-only commit** whose exact diff is `docs/decisions.md`, `docs/benchmarks.md` and
    `docs/roadmap.md`, none of which any `.csproj`, `.props`, `.targets`, `.slnx`, `.nuspec`, `eng/`
    script or workflow consumes as a build, test, workflow, benchmark, corpus, spec, configuration or
    packaging input, carries the native, archive, Adult and `50f6aa62` evidence across it. A run's
    result belongs to its own revision, and the commit that records it is never relabelled as the run
    head.
  - **Admissibility is not exact protocol compliance.** The `50f6aa62` reacquisition is admissible
    evidence with two protocol deviations and one tooling correction disclosed: before its criterion
    was frozen, a restore's vulnerability audit contacted NuGet's public endpoints without obtaining
    or changing a package, and extra identical builds ran; a correction to its own read-only validator
    re-read retained output and reran no trace. None of these invalidates the measurements or
    requires a rerun. The first `82e2ffea` Adult attempt, which crossed an explicit no-network rule
    and did not close that obligation, and the first `3b2e4a80` probe attempt, which is
    non-admissible, are recorded as that, never as failures or superseded evidence.
  - **Limitation closure does not reopen.** No reacquisition or later run licenses a latency,
    neutrality, equality, non-regression, speedup, corrected-build elapsed, rate or overhead claim,
    and no retry is owed; any elapsed values they incidentally produced are contextual raw data only.
- **Why:** the comparison was the right experiment and it was run honestly, in full, under
  a criterion fixed before any data was seen, and it did not pass. The two available
  alternatives were both worse than recording that plainly. Making a sub-5%
  incremental-latency bound a *new* M8 exit condition would invent a release obligation
  the roadmap never stated, on a **correctness** correction, and would hold the milestone
  open behind a machine that has now been shown, by its own unchanged control in both
  directions, not to hold still enough to prove it. Re-basing the whole scale matrix onto
  a new session would spend hours of measurement to support exactly the cross-session
  comparison that limitation closure forgoes, and would discard provenance that is
  currently exact. What closure needs is that the corrected build is **correct**
  and **bounded** (which is what the fifteen validated allocation rows and the six
  validated resource traces establish), plus an honest statement of the one thing that
  could not be measured. Recording it as an accepted evidence policy, rather than as a
  quiet omission, is what keeps the next reader from mistaking "inconclusive" for "fine".
- **Rejected:** treating the failed comparison as a pass, or reporting only its three
  passing forms (the test is collective, and a selected passing-case conclusion is the
  laundering this entry exists to forbid); paraphrasing "inconclusive" as
  *measurement-neutral*, *no measurable regression*, *probably below 5%*, or
  *non-regression* (each asserts the very thing that was not established, and the small
  point estimates cannot carry them); an automatic retry series (the criterion forbids
  reusing or extending that campaign, and a retry series is an unbounded correction loop);
  making the sub-5% bound a mandatory M8 exit condition (a new release obligation the roadmap
  never stated); re-basing the scale matrix onto a fresh session to support a comparison
  closure forgoes (expensive, provenance-destroying, and unnecessary); rerunning valid
  evidence only because a later run saw a different machine state (machine-state difference
  alone is expected and changes nothing that evidence claims); substituting ten times a 7.3M
  job, a `Dry` smoke, or a smaller tier for a real 73M case (the 73M obligation is the 73M
  job); publishing the corrected-build elapsed numbers as a results table or deriving any rate,
  curve or overhead from them (they are contextual raw data, and a table would rebuild the
  comparison by another route); changing an established trace command for no measurement gain,
  such as giving a command that spools nothing a `--temp-dir`; changing the environment under
  measurement, such as redirecting process-global `TEMP`/`TMP` or freeing space on the measured
  machine's system volume, to keep an acquisition boundary instead of disclosing the change; and
  inferring byte identity from a rounded `GB` column, extrapolating one row's allocation delta to
  another, calling allocation machine-state independent, or converting allocated throughput into
  resident memory (the four readings that produced D-125's retracted sentence).
- **Affects:** `docs/decisions.md` (this entry, the D-124 status correction and the D-125
  measurement corrections), `docs/benchmarks.md` (the failed comparison and its limitation,
  the fifteen-row allocation/validation ledger, the corrected-command trace and resource
  records, the native-delivery record, and the session qualifiers on every historical
  headline), `docs/roadmap.md` (current position and the M8 block). **No production code,
  test, benchmark definition, job, corpus, spec, oracle, workflow, package input, public
  API, diagnostic, ordering rule, determinism contract or output byte changes**; the
  registry stays **82**, and every golden, canonical-byte and SHA pin is untouched. Amends
  D-124's invalidation/recheck rule for D-125's correction only; corrects D-124's live
  status and D-125's *Superseded measurements* paragraph; reopens neither D-122/D-123 nor
  D-125's approved amendments A and B.

---

## M8.1 (pre-M9 performance pass)

### D-128: `QuantileAccumulator` uses a radix-F generation-tiered spill-run catalogue

- **Status:** accepted
- **Date:** 2026-09-21
- **Decision:** replace `QuantileAccumulator`'s repeated whole-catalogue consolidation with an
  internal radix-`F` generation-tiered catalogue. Original spills enter generation zero. When a
  generation holds exactly `F` runs, those runs are merged oldest-first into one run of the next
  generation; promotion is by original-spill count, never byte size. A generation-`g` run therefore
  represents exactly `F^g` original spills, and finalization visits generations ascending and runs
  in creation order.
  - **Bounds.** Let `L = 1 + floor(log_F(long.MaxValue))`, computed by repeated integer division,
    and `K = (F - 1) * L`. A quiescent catalogue contains the sum of the base-`F` digits of the
    successful spill count and is bounded by `K` (`F=16`: `L=16`, `K=240`; `F=2`: `L=63`,
    `K=63`). During insertion/carry it is bounded by `K+1`; a carry requiring generation `L` fails
    as an internal invariant violation rather than growing. The open-reader bound remains `F`, with
    at most one writer and the existing `4F` pending-deletion cap. Catalogue entries remain closed
    `(path, size)` values, not open operating-system handles.
  - **Resource accounting.** `T_so_far` still counts original-spill payload only; carry and final
    outputs do not inflate it. The existing workspace-wide `<=3T` rule, tier-1 model
    `384 + 44*capacity`, sequential merge ownership, release-before-post-intake-merge rule and lazy
    zero-spill path are unchanged. The workspace file-count bound becomes `A*K + 4F + 2`; this is a
    fixed structural bound, never population-sized memory.
  - **Failure semantics.** A carry commits its catalogue transition only after the merge succeeds.
    Storage failures, cancellation and cleanup therefore keep their existing diagnostics and
    primary-result precedence. The physical run schedule, deletion order, first encountered storage
    fault and cancellation-observation instant may differ; no contract promises identical fault
    traces across two different merge schedules. Healthy storage still leaves no residue.
  - **Output contract.** Cuts, frozen specs, fingerprints, diagnostics, public API and emitted bytes
    are unchanged. The change adds one internal type, changes `QuantileAccumulator`, and changes
    only the XML wording of `ICalibrationObserver.RunCatalog`. `ValueCountMerger` and every public
    surface remain untouched.
  - **Permanent proof.** The Conversion suite includes an independent model of both the
    generation-tiered schedule and the superseded whole-catalogue schedule (the latter as a rejected
    negative control), real accumulator/workspace observations through the `F=16`, 807-spill
    transition, exact base-`F` occupancy and run-byte checks, fixed/logarithmic bounds, and
    storage/cancellation/overflow cases. The three old catalogue-`<=F` assertions are replaced,
    not removed: they require both the independent logarithmic bound and `<=K`, while continuing to
    require open readers `<=F` and the existing cleanup/accounting bounds.
  - **Measured evidence.** Eight fresh ordinary BenchmarkDotNet host launches used the existing
    `Monitoring` job (two warmups, five actual iterations, one launch) and balanced the primary
    endpoint as `AB`, then `BA`. The 7.3M sixteen-attribute many-quantile
    (`ManyQuantileCalibrateScale7M`) ratios were `0.746878` and `0.752516`; geometric mean
    `0.749692` (about 25% less elapsed time). Baseline spread was 2.80% and order-ratio difference
    0.75%. Combining the retained forward-order guard observations with fresh reverse-order
    observations gave `0.991274` for `ManyQuantileCalibrateWorking` and `0.994901` for
    `CalibrateWideScale7M`. Allocation deltas were `+2,816`, `+424` and `+16,512` B/op for
    `ManyQuantileCalibrateWorking`, `CalibrateWideScale7M` and `ManyQuantileCalibrateScale7M`, all far
    inside the existing allowance. All eight launches exited zero and their per-iteration
    `CalibrationOracle`/`OutputValidation` checks passed. The earlier one-pair
    `ManyQuantileCalibrateScale7M` screen (`0.760480`) is context, not silently pooled.
  - **Evidence limit.** This was a direct BenchmarkDotNet engineering assessment of eight launches,
    with two order-balanced pairs on the targeted endpoint: evidence of the observed improvement, not
    a statistical bound on drift or order effects, and not a formal admission. It makes no
    per-launch digest or surviving-worker claim, and no 73M run was performed. Those limits do not
    weaken the correctness suite or the direct, replicated BenchmarkDotNet comparison on the
    targeted 7.3M shape. *Noted 2026-09-24:* the measured candidate's `QuantileRunCatalog.cs`
    differs from the committed file only in one XML documentation comment, whose evaluation label
    became the citation `(D-128)`; `QuantileAccumulator.cs` and `ICalibrationObserver.cs` are
    byte-identical. No build of the committed revision was measured, and the assessment records no
    build-to-commit tie.
- **Why:** the old schedule repeatedly rewrote the growing consolidated run after the fan-in was
  reached, giving quadratic spill I/O amplification at fixed capacity. Tiering rewrites each value
  at most once per generation. On the workload this change targets it reduced conservative written
  bytes from `9,017,744,128` to `5,571,308,288` (`0.617816`) and reproducibly reduced elapsed time
  by about 25%, without a detected correctness, guard-endpoint or meaningful allocation regression.
  EP-19's requirement for measured evidence is therefore met; retaining the simpler old schedule
  would keep a demonstrated large target-scale cost.
- **Rejected:** keeping merge-all (simpler, but retains the measured quadratic amplification); an
  unbounded catalogue followed by one final merge (population-sized bookkeeping, which D-103
  rejects); promotion by byte size (data-dependent schedule and no fixed digit-sum bound); changing
  the fan-in, memory budget, `ValueCountMerger`, public API or spec (none is needed); and landing the
  evaluation's custom runner, scorer, observer verb, digest helper or custody infrastructure
  (measurement machinery, not product or permanent regression coverage).
- **Affects:** `FcaBedrock.Conversion` (`QuantileRunCatalog`, `QuantileAccumulator`, observer XML)
  and its tests; `docs/benchmarks.md`; `docs/roadmap.md`. Supersedes only D-103's
  per-accumulator catalogue-`<=F` and merge-all scheduling clauses and D-124's restatement of that
  catalogue bound. Every other D-103/D-124 clause stands; D-126 is unchanged. No public API, spec,
  diagnostic, registry, output-byte, benchmark-definition, job, corpus, packaging or workflow
  change.

---

## M8.2 spec review

M8.2's review of the Bedrock spec corrects passages to their owners under D-134. A correction that
changes accepted input, or that must choose between two owners that disagree, needs a decision of
its own, and this section records those.

### D-135: A composed spec needs an `[[attribute]]`, `base_index` is 0 or 1, and `size_advisory_bytes` is not negative; the binding resolves on its own for the probe preflight

- **Status:** accepted
- **Date:** 2026-09-27
- **Decision:** three inputs that the spec never allowed, but that the implementation accepted, are
  rejected. The maintainer placed the fixes inside M8.2 rather than after it.
  1. **A composed spec with no `[[attribute]]`.** §2 has required at least one `[[attribute]]`
     since the spec's first version, but neither the reader nor the resolver checked it, so such
     a spec validated and converted to a context with no columns. The rule applies to the composed spec (§13): a base
     file in an `extends` chain may declare none if the composed spec gains one.
     `SpecResolver.Resolve` owns the check, because only resolve sees the composed document; the
     reader sees one file. The new code `AttributesMissing` (Error, spec resolve) reports it once
     per resolve, with no location.
     - **Its slot.** It is the first entry of §16.4's resolve family 4 (effective-attribute
       validation): after family 1, the binding section and families 2 and 3, before any
       per-attribute diagnostic, and before the family 5 matcher warnings. It aggregates with
       every other resolve diagnostic; it does not stop resolution.
     - **The earlier checks.** As before, the version check runs ahead of every family and the
       shape check ahead of families 2 to 5. A document without `version = 1` reports that check
       alone; one without a `[binding]` or its `shape` reports family 1 and `BindingShapeMissing`.
       Neither is judged against the minimum.
     - **Not `NoFormalAttributes`.** Attributes that are all excluded or filter-only meet the
       minimum; their empty context keeps D-058's plan-phase `NoFormalAttributes` Warning.
  2. **`base_index` other than 0 or 1.** The §8 example (`1 | 0`), §18.2 and the writer
     documentation have only ever described these two values, but the reader accepted any 32-bit
     integer and the `.dat` writer wrote IDs from it. `SpecSectionReaders.ReadOutputDat` now
     reports any other authored value as `SpecFieldInvalid` (Error, spec parse), at the value. An
     omitted field still means 1.
  3. **A negative `size_advisory_bytes`.** §8 gives the field two readings: `0` disables the
     advisory and a positive value is the threshold. The reader accepted any 64-bit integer; only
     `convert` refused a negative one, with a code-less host error, because the writer throws on
     it (D-123 point 12), while `validate`, `plan`, `stats`, `calibrate` and `fingerprint` accepted
     it. `SpecSectionReaders.ReadOutputCxt` now reports a negative authored value as
     `SpecFieldInvalid` (Error, spec parse), so every command refuses it the same way, and
     `convert`'s refusal and its message are removed. The writer's `ArgumentOutOfRangeException`
     stays as the backstop for callers that use the writer directly.
  - **Why the reader owns rules 2 and 3.** Each judges one file's authored value, and the composer
    reads each file of an `extends` chain on its own, so a base with an out-of-range value fails at
    its load even when the root overrides the field. The type checks are unchanged: a value that
    is not an integer, or for `base_index` not a 32-bit one, keeps its existing message and is not
    also reported as out of range.
  - **The binding-only resolver stage.** The triple `probe` checks its role map before it reads a
    row, by resolving a document that holds only the draft's `[binding]`; the draft's attributes
    exist only after the pass. Rule 1 rejects that document, so a new public method,
    `SpecResolver.ResolveBinding(SpecDocument, SourceSchema?)` returning `Diagnosed<Binding>`,
    resolves the `[binding]` alone. It applies the same `extends`, version and shape checks as
    `Resolve` and runs the private helpers that `Resolve` runs for the binding section, so it
    reports the same diagnostics in the same order and no condition gains a second owner. It reads
    no template, matcher or attribute, and it fails on any Error or Fatal. `Prober` calls it
    instead of `Resolve`. It is a second partial stage beside `ResolveReadSettings` (D-098). The
    maintainer approved this public method for M8.2.
  - **Registry and bytes.** The registry moves from 82 to 83 with `AttributesMissing`; rules 2 and
    3 reuse `SpecFieldInvalid`. Each rejection exits 1, like any other Error, so no exit-code
    meaning changes. A spec that is still accepted keeps its fingerprints and its canonical TOML,
    `.cxt`, `.dat` and manifest bytes.
- **Why:** each rule already had an owner in the spec, and the gaps let a spec that §2 forbids
  validate and convert, let `.dat` IDs start from a base that §8 and §18.2 never offer, and gave
  one spec different answers from different commands. Checking the §2 minimum at resolve, and the
  §8 values as each file is read, puts each check where the fact it judges is visible. The binding
  stage keeps the probe's role-map check in the resolver, which owns §5.3, without asking a
  binding-only document to meet a rule about attributes.
- **Rejected:** checking the attribute minimum in the reader (it would reject a base file that
  legitimately declares no attribute); checking it in the composer (a caller that resolves an
  `extends`-free document without composing it would never meet it); reusing `NoFormalAttributes`
  (that Warning describes attributes that exist but plan to no column, while a spec with no
  attribute is an authoring error); allowing any `base_index` the writer can represent (the spec
  never offered one, and a large base overflows the writer's `int` IDs); keeping the refusal in
  `convert` alone (the other commands accepted the value); reading a negative threshold as
  "disabled" (a third reading that §8 does not give); an excluded placeholder attribute in the
  probe's preflight document (an invented attribute that exists only to pass a whole-spec rule);
  and deferring rule 1 until the preflight changed.
- **Affects:** spec §2, §8 and §16.4; `FcaBedrock.Diagnostics` (`AttributesMissing`; registry 83);
  `FcaBedrock.Spec` (`SpecResolver.Resolve`, the new public `SpecResolver.ResolveBinding`,
  `SpecSectionReaders`); `FcaBedrock.Discovery` (the triple preflight in `Prober`);
  `FcaBedrock.Cli` (`convert` loses its negative-threshold refusal); tests in the Spec, Cli,
  Discovery, Diagnostics and Golden projects; `docs/roadmap.md` (the M8.2 scope and M7's registry
  line); `AGENTS.md` (the registry count). No Core type, exporter, fingerprint encoding, golden
  fixture or CLI grammar changes.

### D-136: Tooling should store fingerprints only in a fully-frozen spec, and §14 defines one

- **Status:** accepted
- **Date:** 2026-09-27
- **Decision:** §3's RFC 2119 SHOULD sets the strength of the stored-fingerprint limit: tooling
  SHOULD write the three stored fingerprints only for a fully-frozen spec. §14 defines a
  fully-frozen spec by the conditions under which §7 lets a spec skip Calibrate, and records that
  the `fcabedrock` writing commands never store the fields in any other spec: `calibrate` writes
  only frozen output, and `fingerprint --write` refuses a data-dependent spec with a code-less
  error and exit 1. The reader still accepts the stored fields in any spec and verifies them where
  a plan is computable, so no accepted input, diagnostic, fingerprint or output byte changes.
- **Why:** §3 stated the limit as a SHOULD, while §14 stated it as a plain sentence about tooling,
  which the spec otherwise reads as a requirement. D-134 leaves a choice between two owners that
  disagree to a decision of its own. SHOULD was the only strength the spec stated in RFC 2119
  terms, the reader never enforced a stronger rule, and the product's own writers already meet
  the stricter behavior.
- **Rejected:** a MUST NOT for all tooling (a new obligation on other writers that the reader would
  not enforce); rejecting stored fields in a data-dependent spec at read or resolve (it would
  change accepted input); leaving the two passages to disagree (the closing audit could not
  certify the spec).
- **Affects:** spec §3 and §14; `docs/roadmap.md` (the M8.2 paragraph). No code, test, public API,
  diagnostic, fingerprint or output byte changes.

---

## Spec-field defaults

These are recorded in spec §21 ("Decisions log") and not duplicated here:
math bin-label notation; ASCII operators by default (`bin_label_unicode`
opt-in, for ConExp); `unknown_value_policy = "warn"`; `drop_top = false`;
`.dat` 1-based with configurable `base_index`; `.cxt` size advisory at 1 GB;
`.dat` trailing space off by default. See spec §21 D-items 1–11.
