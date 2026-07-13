# Decisions Log — FcaBedrock vNext

Architectural decisions, with rationale and rejected alternatives. Append-only
in spirit: supersede entries rather than deleting them, so the reasoning trail
survives. Newest decisions at the bottom of each section.

**Entry format:**

```text
### D-NNN — Short title
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
proposing architectural changes. Status is annotated only where an entry is
superseded or refined. A new entry MUST add its line here.

### Architecture

- D-001 — Separable binding vs scaling in the spec
- D-002 — Orthogonal discretizer × scale model (the anchoring idea)
- D-003 — Discovery is a separate operation, not implicit in convert *(refined by D-036)*
- D-004 — Determinism rules centralized in the planner
- D-005 — Plan/Emit separation, with calibration before planning *(refined by D-036)*
- D-006 — Diagnostics package as the shared leaf *(alias dropped by D-042)*
- D-007 — Target 10×–100× the v2 EMAGE workload
- D-008 — Avalonia for the desktop UI
- D-009 — New TOML spec format; one-way `.bed` migration *(migrator realized by D-079)*
- D-010 — Modelled-but-rejected scales/features carry forward-compat
- D-011 — v2 byte-equality is a CLI flag, not a spec setting

### Scope / feature decisions

- D-020 — v1 scale and discretizer surface
- D-021 — Three filtering levers kept distinct *(refined by D-032)*
- D-022 — `value_groups` discretizer (clustering raw values)
- D-023 — `value_labels` (raw value → display label)
- D-024 — Object grouping by composite key (deferred to v1.1)
- D-025 — Post-context reductions live in a sibling tool
- D-026 — Reproducibility: provenance block + run manifest
- D-027 — Spec composition via `extends`
- D-028 — Calibration on-the-fly by default; `calibrate` to freeze

### Round 1 spec audit

- D-030 — Triple multi-value union; scale decides folding
- D-031 — `subject_grouped` requires contiguous subjects
- D-032 — Filter-only attributes (`include = false` keeps `restrict_to`)
- D-033 — `source` may repeat across attributes
- D-034 — Duplicate object keys defined by key mode; `fail` default *(execution → M3, D-064)*
- D-035 — Fingerprint scopes: schema = planned columns only *(output split by D-051/D-053)*
- D-036 — Four-phase processing model; convert calibrates, never discovers

### Round 2 spec audit

- D-037 — Scale-specific default naming; emitted-field discipline *((b) superseded by D-049)*
- D-038 — Date support deferred; v2 six-type-code map confirmed

### Process / governance

- D-029 — Engineering principles formalized as docs/principles.md
- D-039 — Test conventions + ArchUnitNET for architecture tests
- D-040 — Shared `tests/Directory.Build.props`; MTP-only
- D-048 — `AGENTS.md` canonical; `CLAUDE.md` imports it; no symlink
- D-073 — Decision index; sessions read it first, then relevant entries

### M1 (mini-mushroom walking skeleton)

- D-041 — Sep as the DSV tokenizer for the wide-CSV source
- D-042 — Drop the `BedrockResult<T>` alias; use `Result<T, BedrockDiagnostic>`
- D-043 — Two test axes: golden = v2-compat evidence, conformance = native spec
- D-044 — Bin-label style is a plan-time render hook, not a writer flag
- D-045 — v2 `o` and `n` are both cut-based; discrete→nominal, progressive→ordinal
- D-046 — `ordered_cuts` discretizer; v2 `.bed` section-role asymmetry
- D-047 — Open-end ordinal threshold renders `all`, canonical in both paths
- D-049 — `include = false` is an authoring toggle; dormant config never blocks

### M2 (TOML spec format + fingerprinting)

- D-050 — Malformed numeric values are present-but-invalid, not missing
- D-051 — Per-format output fingerprints (cxt + dat)
- D-052 — `extends` overrides attributes position-preservingly
- D-053 — Fingerprints hash a plan-derived canonical JSON structure *(pinned by D-069)*
- D-054 — v1 supports only the standard double `quote_char`
- D-055 — `value_groups` does not use `declared_domain`
- D-056 — Cut validation in M2
- D-057 — `restrict_to` round-trips in M2; execution deferred to M4 *(carriage realized by D-079)*
- D-058 — Empty-output diagnostics: mechanical names replace EmptyExtent/EmptyIntent

### M1-adjacent conformance pass

- D-059 — Discretization outcomes and data-diagnostic aggregation

### Tier 1 spec audit (pre-M2)

- D-060 — Ordinal-over-cuts validation contract
- D-061 — `value_type` matrix: `free_per_value` flexible, `identity` string-only
- D-062 — Cross-attribute restrict not modelled in v1; drop the diagnostic
- D-063 — `restrict_to`: M2 validates shape, M4 executes; diagnostic ownership *(exact numeric form added + diagnostic renamed by D-091)*
- D-064 — Wide column object keys deferred to M3; object-key diagnostic taxonomy *(execution realized by D-083)*
- D-065 — Calibration/vocabulary over the input universe, before `restrict_to`

### Tier 2 register (pre-M2)

- D-066 — Parsed spec document model vs. resolved Core `BedrockSpec`
- D-067 — Resolve/validate seam and diagnostic phase ownership
- D-068 — `missing_policy = "as_attribute"` scheduled into M2 *(migrator branch realized by D-079)*
- D-069 — Canonical fingerprint encoding, pinned (appendix to D-053)
- D-070 — Minimal M2 discretizer-carrier scope; three-tier kind response
- D-071 — Absent/empty `declared_domain`: M2 interim reject until calibrate
- D-072 — Basic triple TOML carrier in M2; conversion deferred to M3 *(conversion realized by D-082)*

### M2 implementation (slices)

- D-074 — `as_attribute` missing-column position uniform across scale kinds (appendix to D-068)
- D-075 — Slice C TOML reader/writer contract: strictness, parse codes, canonical form
- D-076 — Slice D seam/plan validation contract details (appends D-067) *(exact numeric form added + diagnostic renamed by D-091)*
- D-077 — Slice E fingerprint encoding/verification contract details (appends D-069)
- D-078 — Slice F composition/carrier contract details (realizes D-027/D-052; refines D-067/D-075)
- D-079 — Slice G `.bed` migrator contract: document-model target, Diagnosed surfaces (realizes D-009/D-049/D-057/D-068) *(numeric restrict migration refined by D-091)*
- D-080 — `AttributeNameDuplicate` / `ValueLabelKeyNotInDomain` re-homed to the resolve seam (realizes D-067; supersedes its "not re-homed" parenthetical)
- D-081 — Value-bin ordinal path (Slice H): identity + explicit order (realizes the D-047-deferred path; refines D-060)

### M3 (triple source + wide column object keys)

- D-082 — M3 triple source contract: reader, orderings, object identity, absent-vs-missing semantics, structural-error severity (realizes D-072; retires `TripleSourceNotImplementedV1`)
- D-083 — Wide `column` object-key execution + `duplicate_object_policy`; `dedupe` on the shared sort-merge path (realizes D-064; retires `ObjectKeyColumnNotImplementedV1`)
- D-084 — Ordinal string comparison is the project-wide rule (adds principle P-12)
- D-085 — M3 diagnostic taxonomy: structural triple/column-key codes, severities, retirements (refines D-067)
- D-086 — Shape-aware `.bed` migration sources: wide → column, triple → predicate by attribute name (refines D-079)
- D-087 — Symmetrical `[output.dat].trailing_newline` + shape-derived v2 triple `.dat` final-newline compat (new fingerprint input, backward-compatible encoding)

### Tier 1 M4 spec audit (pre-M4)

- D-088 — Shared auto-calibration invariants + equal-frequency contract (restates D-028)
- D-089 — Equal-width range-mode contract
- D-090 — `value_groups` execution contract
- D-091 — `restrict_to` execution contract: existential matching, exact numeric entries, canonical `restrictions` encoding (refines D-063/D-076/D-079) *(merged-`dedupe` restriction + type-directed migration clarified in place — Tier 2 audit)*
- D-092 — Numeric `free_per_value` rendered labels

### Tier 2 M4 implementation-contract audit (pre-M4)

- D-093 — Calibrated-state model: Core-owned resolved calibration outcomes consumed by Plan (refines D-036/D-088; realizes D-028's freeze face)
- D-094 — M4 canonical fingerprint encodings + effective-configuration hashing rule (appendix to D-069/D-077)
- D-095 — Bounded-memory calibration + subject-local triple deduplication + `GroupingStorageFailed` calibrate ownership (refines D-088/D-082)
- D-096 — Numeric `free_per_value` identity: locale-parsed, zero-canonicalized; normalized domain/label/order keys (refines D-061/D-081/D-092)
- D-097 — Filter-only restriction diagnostics report unparseable values under `unknown_value_policy` (refines D-049/D-076)

Spec-field defaults are recorded in spec §21 items 1–11 (see the final section
of this file).

---

## Architecture

### D-001 — Separable binding vs scaling in the spec

- **Status:** accepted
- **Decision:** the Bedrock spec splits into a `[binding]` layer (which data
  source, which columns/predicates, header, locale) and per-attribute scaling
  config (discretizer + scale + policies). Re-binding a spec to a new dataset
  variant changes only `[binding]`.
- **Why:** spec-first reuse across "compatible dataset variants" is a stated
  goal. Without the split, every attribute entry duplicates source-binding
  detail and portability is brittle.
- **Rejected:** a flat spec where each attribute carries its own source
  binding — simpler to write, but spec reuse across datasets becomes
  copy-paste-and-drift.
- **Affects:** Spec, Core, spec §5 ("The [binding] block") / §10 ("The [[attribute]] block").

### D-002 — Orthogonal discretizer × scale model

- **Status:** accepted
- **Decision:** model conceptual scaling as two independent stages — a
  discretizer (raw value → bin label) and a scale (bin label → formal
  attributes). v2's `c`/`b`/`o` type codes become (discretizer, scale) pairs.
- **Why:** v2 conflated discretization and scaling through a single type code,
  which is why "continuous discrete" vs "continuous progressive" felt like
  special cases. Orthogonality reproduces every v2 mode and yields new
  combinations (e.g., equal-frequency bins + ordinal scaling) at zero extra
  design cost. The thesis already separates these conceptually (Ch 4.7 vs 4.8).
- **Rejected:** preserving v2's unified type code — familiar but blocks the
  combinations and bakes in the conflation.
- **Affects:** Core, Conversion, spec §11 ("Discretizer reference") / §12 ("Scale reference"). This is THE anchoring idea.

### D-003 — Discovery is a separate operation, not implicit in convert

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
- **Rejected:** auto-*discovery* folded into convert — convenient but couples
  two concerns and makes the streaming story murky. (Auto-*calibration* IS
  folded in by default — D-005, D-028 — but it is an explicit phase, not
  discovery.)
- **Affects:** Discovery, Conversion; spec §7 ("Processing phases").

### D-004 — Determinism rules centralized in the planner

- **Status:** accepted
- **Decision:** all ordering rules (attribute order, formal-attribute order,
  object order, ID assignment, locale) are encoded in the `ConversionPlan`,
  one rule per axis, all testable. See spec §17 ("Determinism rules").
- **Why:** "stable item ids" is only meaningful if every ordering axis has a
  single, explicit, tested rule. Scattering these across discretizers, scales,
  and writers guarantees drift and cross-machine non-reproducibility.
- **Affects:** Core (planner), spec §17 ("Determinism rules").

### D-005 — Plan/Emit separation, with calibration before planning

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
  wording put Calibrate "before emit" but after Plan; D-036 fixes the order —
  Plan needs calibrated cuts to assign formal-attribute IDs, so Calibrate must
  precede it.)
- **Affects:** Conversion, Export, Cli; spec §7 ("Processing phases").

### D-006 — Diagnostics package as the shared leaf

- **Status:** accepted; `BedrockResult<T>` alias dropped by D-042
- **Decision:** `FcaBedrock.Diagnostics` holds `Result<T, TError>`,
  `BedrockResult<T>` (alias), `Diagnosed<T>`, `BedrockDiagnostic`,
  `DiagnosticCode` (enum), `DiagnosticSeverity`, `DiagnosticLocation`.
  Referenced by every other package. This makes the package count 9
  (the original 8-package sketch + Diagnostics).
- **Why:** a domain-specific instantiation of the generic Result type, shared
  across CLI, future Reduce tool, etc. Aggregating diagnostics (`Diagnosed<T>`)
  lets `validate` surface all errors at once, not just the first.
- **Rejected:** (a) reusing the trading-platform Result library — work code
  stays at work, FcaBedrock is personal; keep them unmixed. (b) a `.Common`
  junk-drawer package — named `.Diagnostics` to resist scope creep.
- **Affects:** all packages, spec §16 ("Diagnostics").

### D-007 — Target 10×–100× the v2 EMAGE workload

- **Status:** accepted
- **Decision:** v1 design target is ~7.3M–73M records (EMAGE was ~732k
  triples). Streaming with `System.IO.Pipelines`, span-based parsing,
  `ArrayPool`, `IAsyncEnumerable`, cancellation, and progress reporting are
  v1 concerns.
- **Why:** explicit answer to "how large." At this scale, transient
  per-object allocations and full-matrix materialization are disqualifying.
  Also means `.cxt` and `.dat` are not symmetric: at the top end a `.cxt` is
  tens of GB and ConExp can't open it — `.dat` is the scaling format, `.cxt`
  the human-readable/small-context one. Planner warns over a size threshold.
- **Affects:** Sources, Conversion, Export, spec §8 (`size_advisory_bytes`).

### D-008 — Avalonia for the desktop UI

- **Status:** accepted
- **Decision:** the eventual desktop UI is Avalonia.
- **Why:** cross-platform from one codebase (the user wants cross-platform),
  mature MVVM, familiar XAML. WPF is Windows-only; MAUI is mobile-skewed and
  rough on desktop; Uno is heavyweight.
- **Rejected:** WPF (platform lock-in), MAUI/Uno (fit/maturity).
- **Affects:** Desktop only; zero effect on Core. UI is M9.

### D-009 — New TOML spec format; one-way .bed migration

- **Status:** accepted (the migrator's document-model realization is D-079)
- **Decision:** the Bedrock spec is a new TOML format, record-per-attribute.
  A v2 `.bed` reader exists for one-way migration (load v2, save as TOML).
  `.bed` writing is not supported.
- **Why:** v2's `.bed` is parallel arrays under bracketed headers —
  index-aligned and brittle (one misaligned line silently corrupts the spec).
  TOML record-per-attribute is robust and diffable. No file-format-compat
  requirement was found.
- **Rejected:** preserving `.bed` format compatibility (no need, and it would
  perpetuate the brittleness); JSON/YAML (TOML reads better for this and
  matches the author's habits; `Tomlyn` is solid on .NET).
- **Affects:** Spec, spec §2+ (file structure).

### D-010 — Modelled-but-rejected scales/features carry forward-compat

- **Status:** accepted
- **Decision:** `interordinal`, `biordinal`, `contranominal` scales, and
  `composite` object keys, are parsable in the v1 spec format but the v1
  planner rejects them with a specific `*NotImplementedV1` diagnostic.
- **Why:** lets v1 spec files declare these without a format break when they're
  implemented. Near-zero cost (a parse case + a planner guard). The user
  preferred "model it now."
- **Rejected:** not modelling them — would force a spec-version bump later.
- **Affects:** Spec, Core (planner), spec §12.4 / §5.4 / §20.

### D-011 — v2 byte-equality is a CLI flag, not a spec setting

- **Status:** accepted
- **Decision:** `--v2-compat` on the `convert` command overrides `[output]`
  to v2's exact byte conventions (CRLF, `30to<40` bin labels, trailing space
  on `.dat` lines). Not expressible as spec fields.
- **Why:** v2-compat is a one-time migration/testing concern, not a property
  of a spec. Keeps the spec format clean. M1's byte-equality goal uses this
  flag.
- **Rejected:** a `bin_label_style = "math" | "v2"` spec field (originally
  proposed, then pulled) — pollutes the spec with a transient concern.
- **Affects:** Cli, Export, spec §8 ("The [output] block") / §18 ("Output formats").

---

## Scope / feature decisions

### D-020 — v1 scale and discretizer surface

- **Status:** accepted
- **Decision:** v1 implements discretizers `identity`, `manual_cuts`,
  `free_per_value`, `equal_width`, `equal_frequency`, `value_groups`; and
  scales `nominal`, `dichotomic`, `ordinal`. `std_dev` binning was considered
  and **removed entirely** (not deferred). Advanced scales are D-010.
- **Why:** this set reproduces the v2 behavior targeted for current
  compatibility / golden tests, plus value grouping and ordinal-over-groups —
  **except std-dev binning**, which is intentionally removed rather than
  deferred because there is no current user need and it is not part of the M1
  compatibility target. (v2's source has a std-dev branch, and SPARQL2FCA had
  std-dev variants, so "all v2 behavior" is qualified here on purpose.) Cut if
  a real need appears later; re-adding is non-breaking.
- **Affects:** Core, spec §11 / §12 / §21.

### D-021 — Three filtering levers kept distinct

- **Status:** accepted; lever (1) refined by D-032
- **Decision:** (1) `include = false` drops formal-attribute generation and
  incidence emission, but does **not** suppress the attribute's own
  `restrict_to` (the filter-only pattern — see D-032);
  (2) `declared_domain` limits which raw values become formal attributes;
  (3) `restrict_to` filters objects (OR within an attribute, AND across
  attributes), operating on **raw values before discretization**.
- **Why:** v2 has all three but the UI blurs them. They compose and have
  different semantics — e.g., you can restrict on a raw value range that isn't
  even a bin boundary, and matched objects still cross under their actual bin.
  The restrict-on-raw-value-before-discretization rule is load-bearing (thesis
  EMAGE walkthrough relies on it).
- **Affects:** Core (planner filter stage), spec §10.3 / §10.4.

### D-022 — value_groups discretizer (clustering raw values)

- **Status:** accepted
- **Decision:** a `value_groups` discretizer maps many raw values to one bin
  label, via explicit `values`, a regex `pattern`, or both, with an
  `unmatched` policy (`skip` | `other` | `passthrough`). Pairs with nominal
  (mutually-exclusive groups) or ordinal (cumulative, needs `order`).
- **Why:** requested — collapse {Bachelors, Masters, PhD} → "Uni-Degree" to
  reduce clutter. Falls out of the orthogonal model as just another
  discretizer. Regex form is essential for high-cardinality coded data
  (ICD-10, gene IDs). Value aliases (synonyms → one label) are the same
  mechanism, no extra feature.
- **Affects:** Core, spec §11.6 ("value_groups").

### D-023 — value_labels (raw value → display label)

- **Status:** accepted
- **Decision:** a per-attribute `value_labels` map renders raw values under
  display names in formal-attribute output (v2's `[Attribute Categories]` vs
  `[Category Values]`). Applies only under `identity` / `free_per_value`.
- **Why:** caught during spec review — v2 outputs `gill-size-broad`, not
  `gill-size-b`. Without it, mini-mushroom byte-equality (M1) fails and output
  names are ugly. Affects `output_fingerprint` (now split per-format, D-051), not `schema_fingerprint`.
- **Affects:** Core, Export, spec §10.8 ("value_labels").

### D-024 — Object grouping by composite key (deferred to v1.1)

- **Status:** accepted (deferred)
- **Decision:** `object_key.mode = "composite"` (rows sharing a derived key
  merge into one formal object, attribute sets unioned/intersected) is modelled
  in the spec but the v1 planner rejects it.
- **Why:** real EMAGE-style need ("all observations for this gene as one
  object"), but it turns single-pass conversion into a sort-then-group stage
  needing care at 73M rows. Model now, implement once the streaming pipeline
  is proven.
- **Affects:** Sources, Conversion, spec §5.4 ("Object key resolution").

### D-025 — Post-context reductions live in a sibling tool

- **Status:** accepted
- **Decision:** clarification, reduction, and minimum-support filtering are
  NOT part of the conversion pipeline. They operate on a produced `.cxt`/`.dat`
  and belong in a separate tool (working name `FcaBedrock.Reduce`).
- **Why:** they share no state with convert and apply equally to contexts from
  other software. Keeps the conversion pipeline lean. Thesis flagged
  minimum-support as future work.
- **Affects:** future sibling tool; keeps Conversion/Export focused.

### D-026 — Reproducibility: provenance block + run manifest

- **Status:** accepted
- **Decision:** specs carry an optional `[provenance]` block (author, source
  URL/hash, lineage). Each `convert` emits a `<output>.manifest.toml` sidecar
  with tool version, spec/input/output hashes, command line, and any
  on-the-fly-calibrated cuts.
- **Why:** the thesis frames Bedrock files as a reproducible record of how data
  was appropriated. Citing a manifest is sufficient for a reproducibility
  audit. Auto-discretizer calibration is captured so on-the-fly runs stay
  reproducible without forcing a separate `calibrate` step.
- **Affects:** Spec, Cli, spec §4 ("The [provenance] block") / §15 ("Run manifest").

### D-027 — Spec composition via `extends`

- **Status:** accepted
- **Decision:** a spec may `extends` a base spec; merge rules are defined
  (binding/defaults per-field override, templates/matchers concatenate,
  attributes override by name, cycles rejected). Fingerprints computed over the
  resolved spec.
- **Why:** the EMAGE workflow is one schema, many analyses (this gene vs that,
  this stage-range vs that). Without composition, analyses are copy-paste and
  drift. Hard to retrofit (changes parser resolution order), so committed early.
- **Affects:** Spec, spec §13 ("Composition: extends").

### D-028 — Calibration on-the-fly by default; `calibrate` to freeze

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

### D-030 — Triple multi-value union; scale decides folding

- **Status:** accepted
- **Decision:** for triple input, rows sharing an object key accumulate crosses
  by union; duplicate identical triples are idempotent; this is normal input,
  never a duplicate-object condition. Whether several values fold into one
  formal attribute is a property of the chosen *scale* (`dichotomic` /
  `value_groups`), not a source-level toggle.
- **Why:** matches v2 (coalesces by subject, accumulates crosses). A
  source-level collapse toggle would be a second way to express what the scale
  already expresses, violating principles P-5 ("One project-standard way per
  concern").
- **Rejected:** a configurable collapse flag on the source (the "(maybe
  configurable?)" question) — declined for the one-way reason above.
- **Affects:** Sources, Conversion, spec §5.3.1.

### D-031 — `subject_grouped` requires contiguous subjects

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

### D-032 — Filter-only attributes (`include = false` keeps `restrict_to`)

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

### D-033 — `source` may repeat across attributes

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

### D-034 — Duplicate object keys defined by key mode; `fail` default

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

### D-035 — Fingerprint scopes: schema = planned columns only

- **Status:** accepted; the single `output_fingerprint` is split into per-format
  `cxt_output_fingerprint` / `dat_output_fingerprint` by D-051 and its plan-derived
  canonical encoding pinned by D-053 (refines D-004-adjacent fingerprint wording;
  tightened in Round 4)
- **Decision:** `schema_fingerprint` hashes **only the final ordered list of
  planned formal-attribute canonical identities** (logical name + scale +
  canonical bin/threshold key + operator), as produced by Plan. No policy is an
  independent input: `missing_policy = "as_attribute"`,
  `unknown_value_policy = "include"`, `binding.locale`, declared-domain order,
  and cuts all enter *only* through their effect on that resolved list. Two
  plans with the same ordered canonical-identity list fingerprint identically.
  It excludes `duplicate_object_policy`, `restrict_to`, object-key mode, and
  object ordering (those select/merge *rows*). `output_fingerprint` =
  effective conversion plan + effective output settings (binding mappings,
  delimiter/quote/missing token, value types, policies, locale, object
  ordering, rendered names, writer settings incl. `trailing_newline`).
- **Canonical identity vs rendered name:** canonical identity (→ `.dat` columns,
  → `schema_fingerprint`) is distinct from the rendered `.cxt` name (produced by
  `formal_attribute_format` / `display_name` / `value_labels`, → only
  `output_fingerprint`). Identity collision → `FormalAttributeCollision`;
  rendered-name collision → `FormalAttributeNameCollision`; both Error.
- **Why:** `.dat` column identity is the schema; listing policies as *direct*
  inputs double-counted their effect (the extra/included columns are already in
  the planned list). Hashing the planned list alone is exact and simpler. The
  original §13 wrongly put `duplicate_object_policy` in the schema fingerprint;
  Round 3 removed locale; Round 4 removed the residual policy inputs and split
  identity from rendered name.
- **Affects:** Spec (fingerprinting), spec §10.2 / §14 / §17; diagnostics
  `FormalAttributeCollision`, `FormalAttributeNameCollision`.

### D-036 — Four-phase processing model; convert calibrates, never discovers

- **Status:** accepted (makes D-003 / D-005 concrete and normative in the spec)
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

Refinements from the second audit pass. Several existing decisions gained
refinement markers (D-003, D-005, D-021); the entries below are new.

### D-037 — Scale-specific default naming; emitted-field discipline

- **Status:** accepted; (b) superseded by D-049 — `EmittedFieldOnExcludedAttribute`
  is removed and `include = false` is an authoring toggle (retained config is
  ignored, not an error). (a) stands.
- **Decision:** (a) default formal-attribute naming is scale-specific —
  nominal `{column}-{value}`, ordinal `{column}-{scale_op}{value}`, dichotomic
  `{column}` alone, `as_attribute` adds `{column}-missing`; an explicit
  `formal_attribute_format` overrides the scale default entirely. (b)
  `discretizer`/`scale` are required only when `include = true`; emitted-only
  fields (`discretizer`, `scale`, `value_labels`, `declared_domain`,
  `formal_attribute_format`, `display_name`, `missing_policy`,
  `unknown_value_policy`) on an `include = false` attribute are
  `EmittedFieldOnExcludedAttribute` (Error).
- **Why:** (a) M1 byte-equality requires dichotomic `bruises?` not
  `bruises?-bruises`; the old flat `{column}-{value}` default was wrong for
  dichotomic and contradicted the examples. (b) the old "every attribute needs
  discretizer+scale" rule made the new filter-only examples invalid.
- **Affects:** Core, Export, spec §10.7 / §10.9 / §12.2; new diagnostic
  `EmittedFieldOnExcludedAttribute`.

### D-038 — Date support deferred; v2 six-type-code map confirmed

- **Status:** accepted (supersedes the Round 2 "implement date now" direction)
- **Decision:** date-valued scaling is **deferred** from v1. `value_type =
  "date"` is reserved but parsed-then-rejected by the v1 planner with
  `DateValueTypeNotImplementedV1`. v1 treats date-like values as strings unless
  a future date value type/discretizer lands. The `value_type` field survives
  with live values `"string"` / `"number"` (so un-deferring later is a
  non-breaking addition of the `"date"` value, not a new field).
- **Why:** continuous *numeric* support is the v1 priority. Full date support
  pulls in cut syntax, `DateOnly`/`DateTime` semantics, day-space binning, label
  rounding, and date-specific diagnostics — complexity not worth front-loading
  before coding. This reverses the Round 2 decision to implement date now.
- **v2 finding (retained, accurate):** verified against `frmFcaBedrock.vb`, v2
  has **six** type codes — `c` categorical, `b` boolean, `o` continuous-numeric,
  `d` date, `n` ordinal, plus missing. The earlier "dates as continuous" reading
  was wrong; `d` is a distinct type using `DateTime.Parse`. So v1 deferring `d`
  is a *conscious parity deferral*, not an oversight — and D-020's "reproduces
  v2 behavior" is correspondingly qualified (date deferred, std-dev removed).
  v2's `n` (Ordinal) type = our `ordinal` scale over an ordered categorical
  discretizer (already covered, no new feature). The 60% date auto-detect
  heuristic is retained only as future `probe` behavior when date lands.
- **Rejected:** implementing date in v1 (Round 2 direction) — reversed for
  scope/priority. Fully removing `value_type` until date lands — kept the field
  so re-enabling is non-breaking.
- **Affects:** Sources, Conversion, Core, spec §5.1 / §10.2 / §11.7 / §20;
  lineage.md (v2 type-code map, `d` marked deferred); diagnostics
  `DateValueTypeNotImplementedV1`, `SourceValueTypeInvalid` (replacing the
  Round 2 `DateFormatInvalid` / `DateGranularityNotImplementedV1`).

---

## Process / governance

### D-029 — Engineering principles formalized as docs/principles.md

- **Status:** accepted
- **Date:** 2026-06-20
- **Decision:** project-specific engineering principles live in
  `docs/principles.md` (21 principles across Working discipline + Correctness,
  Architecture, Performance, Testing). Mechanical rules stay in `.editorconfig`
  / analyzers. Changes to the principle set are governance changes and get a
  `decisions.md` entry; internal cross-references use number + short title so
  renumbering stays painless.
- **Why:** gives sessions (especially agents) a checkable invariant set
  distinct from operational guidance (`CLAUDE.md`) and per-decision rationale
  (this file).
- **Affects:** all packages; governance. See also the mechanical-backstop
  follow-up in `roadmap.md` (M0).

### D-039 — Test conventions + ArchUnitNET for architecture tests

- **Status:** accepted
- **Date:** 2026-06-23
- **Decision:** project test conventions live in `CLAUDE.md` ("Testing
  conventions"): one `FcaBedrock.<Package>.Tests` project per package (created when
  the package gains code), unit test class `<ClassUnderTest>Tests` mirroring the
  production type's folder/namespace, unit/behavioural methods named
  `Subject_When<Condition>_Then<Outcome>`, and architecture tests named
  `Subject_Should<Outcome>`. Architecture/dependency tests are written with
  **ArchUnitNET** (`TngTech.ArchUnitNET.xUnit`), replacing the hand-rolled
  reflection harness.
- **Why:** one documented test style (P-5), and a fluent arch-test library whose
  type-level dependency analysis can enforce invariants reflection cannot — notably
  P-13 ("Core is pure: no `System.IO`") once Core has code at M1. The cross-package
  layering and cycle rules read declaratively and extend cleanly as packages land.
- **Rejected:** NetArchTest.Rules — simpler fluent API but less expressive and less
  actively maintained; the hand-rolled reflection harness — zero-dependency but
  assembly-reference granular, so it cannot see type-level dependencies like
  `System.IO.File`. On M0's empty assemblies all three are equally vacuous, so the
  arch suite keeps a non-vacuous "production assemblies were loaded" guard.
- **Affects:** tests (`FcaBedrock.Architecture.Tests`, `*.Tests` naming),
  `CLAUDE.md`, `Directory.Packages.props`.

### D-040 — Shared `tests/Directory.Build.props`; MTP-only (no Microsoft.NET.Test.Sdk)

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
  `dotnet test` (MTP runner per `global.json`); CLI build/test, the guard, and
  golden fixture copying were all verified green without it, and Visual Studio
  2026 Test Explorer was confirmed to discover and run both projects without it.
  `Microsoft.NET.Test.Sdk` is therefore removed from `Directory.Packages.props`
  entirely; if some future tooling needs VSTest, re-add the `PackageReference` to
  the shared props (one line) rather than per project.
- **Why:** the convention is one test project per package (D-039), so the six
  duplicated boilerplate lines would be re-pasted for every future package, and
  the zero-tests guard relied on each session remembering to copy it. Centralizing
  makes the guard automatic and shrinks each `.csproj` to its unique parts.
- **Rejected:** a `tests/Directory.Build.props` *without* the parent import — it
  shadows (does not merge with) the root props, silently dropping
  `TargetFramework`, `Nullable`, analyzers, `TreatWarningsAsErrors`, and
  `RepoRoot` (the last breaks the Golden fixture-copy glob). Putting the shared
  block in the **root** `Directory.Build.props` under
  `Condition="'$(IsTestProject)'=='true'"` — fails, because props are imported
  before the csproj body sets `IsTestProject`; a root `Directory.Build.targets`
  with that condition works but is less discoverable and mixes test config into a
  root file. Keeping `Microsoft.NET.Test.Sdk` unconditionally — unnecessary VSTest
  weight for the MTP/CLI path.
- **Affects:** tests (`tests/Directory.Build.props`, both `*.Tests` csproj),
  `CLAUDE.md`. Refines D-039.

---

### D-048 — `AGENTS.md` canonical; `CLAUDE.md` imports it; no symlink

- **Status:** accepted
- **Date:** 2026-06-26
- **Decision:** the shared, tool-agnostic agent guidance formerly in `CLAUDE.md`
  is now canonical in **`AGENTS.md`** (the cross-tool convention Codex and other
  agents read natively). `CLAUDE.md` is reduced to a one-line Claude Code import
  shim — `@AGENTS.md` — which inlines the file into context identically to inline
  content. Any Claude-only instructions go *after* that import line; Codex-only
  runtime config stays in Codex's own config, never the shared file. The two
  self-references in the migrated file (the `# AGENTS.md` title and the
  `/AGENTS.md # this file` layout-block line) were repointed; historical
  `CLAUDE.md` mentions elsewhere in this log (e.g. D-040 "Affects") are left as-is.
- **Why:** a Claude session and a Codex session working the same repo need one
  source of truth, not two files that silently drift. The import costs Claude
  nothing and Codex reads `AGENTS.md` directly.
- **Rejected:** a `CLAUDE.md → AGENTS.md` **symlink** — this checkout has
  `core.symlinks=false` (the Windows default; `core.autocrlf=true` compounds it),
  so a committed symlink checks out as a one-line text file containing the path,
  i.e. Claude would load the literal string `AGENTS.md` as its entire guidance.
  Duplicating the content across both files — guaranteed drift.
- **Affects:** `AGENTS.md` (renamed from `CLAUDE.md`, history preserved via
  `git mv`), `CLAUDE.md` (new import shim). Process/tooling only; no code, no
  output bytes.

### D-073 — Decision index; sessions read it first, then relevant entries

- **Status:** accepted
- **Date:** 2026-07-04
- **Decision:** this file opens with a compact **index** — one line per
  decision, grouped by section, status annotated only where superseded or
  refined. The `AGENTS.md` session workflow changes from "read
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

---

## M1 (mini-mushroom walking skeleton)

### D-041 — Sep as the DSV tokenizer for the wide-CSV source

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
  zero-allocation streaming. Sep is currently the fastest .NET CSV parser and its
  span-first row/col API suits the emit hot path (P-18). Wrapping rather than
  exposing it keeps Sep out of the public surface, so it can be swapped without a
  contract change (P-4).
- **Rejected:** (a) hand-rolling a span DSV parser — reinvents the wheel we
  explicitly chose not to, and shifts the hardening/benchmark burden onto us; (b)
  `Sylvan.Data.Csv` — also fast and mature, but exposes an ADO.NET
  `DbDataReader` shape with slightly more per-field overhead and a less span-native
  API than Sep.
- **Affects:** Sources, `Directory.Packages.props`; spec §5.1/§5.2.

### D-042 — Drop the `BedrockResult<T>` alias; use `Result<T, BedrockDiagnostic>`

- **Status:** accepted (refines D-006)
- **Date:** 2026-06-24
- **Decision:** `Result<T, TError>` is a `readonly struct` whose `Ok`/`Err`
  factories allocate nothing, so the `BedrockResult<T>` "alias" D-006 named would
  be pure typing-sugar with no runtime benefit — and C# cannot alias a
  partly-closed generic anyway. It is **not** introduced; call sites use
  `Result<T, BedrockDiagnostic>` directly. `Diagnosed<T>` (the aggregating carrier)
  is unaffected and remains the standard for validate/plan.
- **Why:** one result type, no indirection, no second way to spell the same thing
  (P-5). Naming the alias only to never realize it would have been a phantom in the
  surface.
- **Rejected:** a `BedrockResult` static factory class over the closed generic —
  still indirection for zero benefit; a distinct wrapper type — a second result
  type to learn, against P-5.
- **Affects:** Diagnostics; refines D-006 (alias struck from the type list).

### D-043 — Two test axes: golden = v2-compat evidence, conformance = native spec

- **Status:** accepted
- **Date:** 2026-06-24
- **Decision:** output behavior is proven along two distinct axes. (1) **Golden
  tests** (`FcaBedrock.Golden.Tests`) run the pipeline under
  `WriterOptions.V2Compat` and assert byte-identity against the `fixtures/v2/`
  goldens — *compatibility evidence* (P-9). (2) **Spec-conformance tests** run the
  **native** (non-v2) path and assert documented behavior with spec-section
  citations (e.g. native `.cxt` is LF + trailing newline §18.1; native `.dat` has
  no trailing space §18.2/§21.4; dichotomic name is `{column}` alone §10.7/§12.2;
  `value_labels` change names not order/count §10.8). Per-fixture binding (which
  the v2 `.bed` never recorded — delimiter/header/shape) lives in a typed
  `FixtureCase` table, as the fixtures README sanctions.
- **Why:** the v2 goldens are deliberately *not* the spec's native output (v2-isms
  are quarantined behind `--v2-compat`, D-011), so byte-equality to v2 alone would
  leave the native contract unproven. The conformance axis is how code is held to
  the spec, not merely to v2 (P-8); the golden axis is how v2 compatibility stays
  evidenced (P-9).
- **Rejected:** a single golden axis — would silently let the native default drift
  from the spec; asserting native output against checked-in native golden files —
  premature before the native format stabilizes, and the spec text is the
  authority at this stage.
- **Affects:** tests (`FcaBedrock.Golden.Tests`: `GoldenFixtureTests`,
  `SpecConformanceTests`, `FixtureCase`); spec §18.

---

### D-044 — Bin-label style is a plan-time render hook, not a writer flag

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
  only, never the schema (§8/§14, D-011/D-035). Canonical identity + late render keeps writers dumb (P-15) and lets
  one planner path serve both styles.
- **Affects:** `FcaBedrock.Core` (`Discretizer.RenderBinLabel`, `LabelStyle`,
  `ConversionPlanner`); the golden harness derives the style from the v2-compat
  line ending.

---

### D-045 — v2 `o` and `n` are both cut-based; discrete→nominal, progressive→ordinal

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** v2's continuous (`o`) and ordinal (`n`) types are both cut
  discretizers; the discrete/progressive toggle is the **scale** choice, not a
  discretizer mode. `o` → `manual_cuts`, `n` → `ordered_cuts` (D-046); discrete →
  `nominal`, progressive → `OrdinalScale(direction = le)`. The mode is **not in the
  `.bed`** (the two ordinal `.bed`s are byte-identical) — it is supplied
  **out-of-band** (a `ScalingMode` argument to `BedToSpec.ToSpec`, like `Binding`),
  defaulting to discrete. **No TOML reader is needed** to reproduce the progressive
  golden. `d` (date) stays rejected (D-038).
- **Why:** matches the orthogonal model (D-002) and the verified v2 output (`n`
  produces mutually-exclusive `<Managerial`/`>=Managerial`). Treating the mode as
  caller metadata mirrors how delimiter/header already are.
- **Rejected:** mapping `n` to a cumulative ordinal by default (refuted by the
  discrete `n` golden); gating the progressive golden behind the M2 TOML reader.
- **Affects:** `FcaBedrock.Spec` (`BedToSpec`, `ScalingMode`); the golden harness
  (`FixtureCase.ScalingMode`).

---

### D-046 — `ordered_cuts` discretizer (spec §11.8); v2 `.bed` section-role asymmetry

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** add an `ordered_cuts` discretizer — cuts over a declared category
  order — as the categorical sibling of `manual_cuts`, sharing one `CutBinLabels`
  label/identity helper. Documented in spec §11.8 **before/with** the code (P-8).
  For v2 `n`, `[Attribute Categories]` carries the ordered domain and
  `[Category Values]` carries the cut (`<,Managerial,>`); for `o`, both carry the
  numeric cut spec.
- **Why:** keeps numeric and ordered cuts symmetric and DRY (P-17); the shared
  label helper guarantees they never drift on bin labels or `--v2-compat` rendering.
- **Rejected:** modelling `n` with `value_groups` (loses order and threshold
  semantics); a mode-switched single cut discretizer (god type, P-17).
- **Affects:** `FcaBedrock.Core` (`OrderedCutsDiscretizer`, `ManualCutsDiscretizer`,
  `CutBinLabels`, `BinEnds`); spec §11.8, §19.2.

---

### D-047 — Open-end ordinal threshold renders `all`, canonical in both paths

- **Status:** accepted
- **Date:** 2026-06-25
- **Decision:** for an `ordinal` scale over an **open-ended** cut discretizer, the
  tautological threshold at the open end has no finite edge and renders **`all`**
  (v2's `age-all`). It is **canonical** — emitted natively too — so `--v2-compat`
  never changes the schema (D-035); `drop_top` suppresses it. Over half-open
  `[lo, hi)` cut bins only the geometry-aligned boundary is well-defined (`le`+`<`,
  `ge`+`>=`); the straddling combinations and the independent `boundary` knob await
  the M2 value-bin path (no M1 producer, P-3). Spec §12.3 amended.
- **Why:** makes "N bins → N formal attributes" exact and reproduces v2's
  progressive column count; an `all`-only-under-`--v2-compat` rule would add a
  column under compat, violating D-035.
- **Rejected:** a math-y native `<∞` with `all` only under `--v2-compat`.
- **Affects:** `FcaBedrock.Core` (`OrdinalScale`, `BinScheme`, `OrdinalDirection`);
  spec §12.3.

---

### D-049 — `include = false` is an authoring toggle; dormant config never blocks

- **Status:** accepted
- **Date:** 2026-06-27
- **Decision:** `include = false` is a pure on/off **authoring toggle**. An
  excluded attribute MAY retain any emitted-shaping config (`discretizer`,
  `scale`, `value_labels`, `declared_domain`, `formal_attribute_format`,
  `display_name`, `missing_policy`, `unknown_value_policy`); the planner ignores
  it while excluded — it is **never** an error. This reverses D-037(b)'s
  `EmittedFieldOnExcludedAttribute`, which is **removed** (diagnostic + enum
  member). `restrict_to` still applies (D-021/D-032). Consequences: (a) the v2
  `.bed` migrator now **preserves** an excluded attribute's derived
  discretizer/scale/domain — best-effort: an unsupported type or config that fails
  to parse degrades to a bare excluded attribute, never failing the migration; (b)
  `value_labels` under a discretizer that does not
  consult it is **dormant** — ignored by both validation *and* name rendering, so
  it cannot change output even when a key matches a cut-bin label. `value_labels`
  applies only when the discretizer consults it (`identity`/`free_per_value`); the
  single authority is `Discretizer.ConsultsValueLabels`. So `ValueLabelsNotApplicable`
  (spec-text only) and the enforced `ValueLabelKeyNotInDomain` fire only for live
  labels — `ValueLabelKeyNotInDomain` stays the typo-catcher there.
- **Why:** the D-037(b) rule made toggling an attribute off destructive (strip all
  config) and a TOML round-trip of a parked attribute lossy — authoring-hostile
  ahead of the M2 reader/writer. The principle: inactive/dormant config must not
  block authoring; active attributes are still validated normally. Aligns excluded
  attributes and dormant `value_labels` with how `declared_domain` is already
  ignored for cut-based discretizers (§10.3).
- **Deferred (M2 writer):** authored-vs-default presence tracking for
  `missing_policy`/`unknown_value_policy`/`display_name`/`formal_attribute_format`
  (round-trip fidelity); the default-`boundary` round-trip trap over cut bins
  (§12.3); revisiting the `value_type`-vs-discretizer rule (§10.2 — a live
  conflict, left validating). See `docs/roadmap.md`.
- **Deferred (migrator hygiene):** make `BedToSpec` return `Result<T,
  BedrockDiagnostic>` instead of throwing (P-14), so excluded-config recovery can
  emit a *diagnostic* rather than relying on the broad recovery `catch (Exception)`
  in `MapAttribute`. Natural to fold in when M2 reworks the `.bed` → TOML migrator;
  not worth a standalone refactor now. *(Landed at M2 Slice G, D-079 —
  `Diagnosed<SpecDocument>` over the document model.)*
- **Affects:** Core (planner `ValidateValueLabels` + `RenderName`; new
  `Discretizer.ConsultsValueLabels`; `AttributeSpec` doc), Diagnostics (enum:
  `EmittedFieldOnExcludedAttribute` removed), Spec (`BedToSpec` migrator), spec
  §7 / §10.8 / §10.9 / §16.4 / §17. Supersedes D-037(b); refines D-021 / D-032.

---

## M2 (TOML spec format + fingerprinting)

These finalize the M2 contract before the TOML reader/writer, `.bed` migrator,
fingerprints, manifests, and `extends` are implemented. They were settled over
four review passes; the spec text (`bedrock-spec-v1.md`) is updated to match in
the same documentation pass. Implementation follows a separate M1-adjacent
conformance pass (`roadmap.md`).

### D-050 — Malformed numeric values are present-but-invalid, not missing

- **Status:** accepted (supersedes the §11.5 "parse failure → missing" wording and
  the "NaN/∞ → missing" clause of D-036)
- **Date:** 2026-06-28
- **Decision:** for a numeric attribute, a value that is present but not a usable
  finite number — fails to parse under `binding.locale`, or parses to NaN/±∞ — is
  **invalid**, not missing. The object is kept, no cross is emitted, the value is
  excluded from calibration, and `SourceValueUnparseable` is reported at the
  severity `unknown_value_policy` selects (`skip` → silent; `warn` → Warning;
  `fail` → Error/abort; `include` → Warning, since an unparseable token cannot be
  added to a numeric domain). Only empty cells and explicit `missing_token`
  matches are *missing* and follow `missing_policy`.
- **Why:** "missing" and "malformed" diverge under `missing_policy = "as_attribute"`
  (missing crosses the `-missing` column; malformed must not) and for diagnostics
  (malformed data deserves a signal). Reusing `unknown_value_policy` for severity
  avoids a second strictness knob (P-5); for numeric attributes that policy was
  otherwise inert (cut discretizers ignore `declared_domain`, §10.3). NaN/±∞ are
  folded in with parse-failure rather than split into a third behavior.
- **Rejected:** keeping parse-failure as missing (conflates two conditions, hides
  bad data); a fixed Warning severity (a `fail` pipeline expects malformed data to
  abort); a dedicated malformed-value policy knob (P-6, redundant with
  `unknown_value_policy`).
- **Affects:** Core (planner/emit), Conversion; spec §10.6 / §11.5 / §16.4;
  diagnostic `SourceValueUnparseable`. Byte-neutral on M1 (still "keep object, no
  cross"; only adds a diagnostic).

### D-051 — Per-format output fingerprints (cxt + dat) replace the single output_fingerprint

- **Status:** accepted (supersedes the single-`output_fingerprint` model of D-035;
  `schema_fingerprint` unchanged)
- **Date:** 2026-06-28
- **Decision:** the `[spec]` block stores `schema_fingerprint`,
  `cxt_output_fingerprint`, and `dat_output_fingerprint`. Both output fingerprints
  build on `schema_fingerprint` and add the **shared** byte-affecting inputs that
  schema omits — `duplicate_object_policy`, object-ordering policy, `restrict_to`
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

### D-052 — extends overrides attributes position-preservingly

- **Status:** accepted (supersedes §13 rule 5 "concatenate; current wins"; refines
  D-027)
- **Date:** 2026-06-28
- **Decision:** under `extends`, base attributes keep their original positions; a
  derived attribute with the same `name` replaces the base attribute **in place**
  (whole-attribute replacement — inherited fields, including `restrict_to`, are
  dropped unless repeated); a derived attribute with a new `name` is appended after
  all inherited attributes; the merge is applied at each step of a multi-level
  chain, base-most first. Suppress an inherited attribute by overriding it with
  `include = false`. `[output]` / `[output.cxt]` / `[output.dat]` merge per leaf
  field. Base-stored fingerprints are ignored and recomputed for the resolved spec.
- **Why:** attribute order is column order (§17 rule 1) and feeds
  `schema_fingerprint`, so plain concatenation would reorder columns whenever a
  derived spec re-tuned an inherited attribute — surprising and fingerprint-
  changing. Position-preserving override keeps column order stable across re-tunes.
  `[output]` merge was previously unspecified.
- **Rejected:** concatenation with append-on-override (reorders columns);
  field-level merge of same-name attributes (error-prone, already rejected by D-027).
- **Affects:** Spec; spec §13.

### D-053 — Fingerprints hash a plan-derived canonical JSON structure, not TOML text

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
  tag makes stored fingerprints portable and the encoding evolvable — both durable
  contracts (P-11), so they must be fixed before any spec ships with a stored hash.
- **Rejected:** hashing TOML text (formatting-sensitive, and `30` vs `30.0` would
  differ); `"R"`/`"G17"` float formatting (17 digits defeats the `30.0`/`30`
  collapse).
- **Affects:** Spec, Core (fingerprint encoder); spec §2 / §3 / §14. Needs a
  canonical-encoding stability golden (numeric cuts) at implementation.

### D-054 — v1 supports only the standard double quote_char

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

### D-055 — value_groups does not use declared_domain

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
  "recognized-value" gates (P-5) — and since unmatched-`skip` already defers to
  `unknown_value_policy`, the domain added nothing but precedence ambiguity (it also
  made regex groups, the high-cardinality case D-022 targets, useless). Dropping it
  dissolves the ambiguity.
- **Rejected:** a domain-vs-group precedence rule (made regex groups pointless);
  rejecting `passthrough` + `declared_domain` only (still leaves the overlap).
- **Affects:** Core, Spec; spec §7 / §10.3 / §11.6 / §17;
  diagnostic `ValueGroupsPassthroughDataDependent`.

### D-056 — Cut validation in M2

- **Status:** accepted (formalizes the 2026-06-26 review item; refines D-046)
- **Date:** 2026-06-28
- **Decision:** M2 validates hand-authored TOML cuts, since hand-written specs first
  become possible at M2: `manual_cuts` — strictly ascending (`DiscretizerCutsNotAscending`),
  length ≥ 1 (`DiscretizerCutsTooFew`), and `ends = "closed"` requires ≥ 2 cuts
  (`DiscretizerEndsClosedTooFewCuts`); `ordered_cuts` — `order` entries distinct and
  non-empty (`OrderDomainInvalid`), cuts ∈ `order` (`OrderedCutsCutNotInDomain`),
  cuts strictly ascending by order position (`OrderedCutsNotAscending`), plus the
  closed-ends rule.
- **Why:** v2 `.bed` migration produced cuts mechanically, but a hand-authored TOML
  spec can easily express invalid cuts; these need clear diagnostics at the
  validate phase rather than surfacing as confusing downstream behavior. Recorded
  here because the 2026-06-26 review agreed the item without a decision entry.
- **Affects:** Core (cut validation), Spec; spec §11.2 / §11.8 / §16.4; diagnostics
  `DiscretizerEndsClosedTooFewCuts`, `OrderDomainInvalid`, `OrderedCutsCutNotInDomain`,
  `OrderedCutsNotAscending` (and existing `DiscretizerCutsNotAscending` / `DiscretizerCutsTooFew`).

### D-057 — restrict_to round-trips in M2; execution deferred to M4

- **Status:** accepted (refines D-021 / D-032; sequences §10.4; migrator carriage realized by D-079)
- **Date:** 2026-06-28
- **Decision:** M2 parses, preserves, and round-trips every `restrict_to` form
  (string list, open- and closed-range, mixed), but planning/conversion **rejects**
  any `restrict_to` with `RestrictToNotImplementedV1` until execution lands at M4 —
  it is never silently ignored. While unimplemented, `restrict_to` does not enter
  the output fingerprints, and a spec containing any `restrict_to` is not
  fully-frozen, so tooling stores no fingerprints for it.
- **Why:** the M2 carrier is needed for round-trip and migration, and `restrict_to`
  is an output-fingerprint input — but its execution is roadmap M4. Silently
  ignoring it would produce unfiltered output mismatching the spec's intent; storing
  an M2 output fingerprint that excludes `restrict_to` would go stale when M4 lands.
  The parse-but-reject pattern (as for templates/matchers) closes both holes.
- **Rejected:** silently ignoring `restrict_to` in M2 (latent correctness bug);
  pulling wide-CSV `restrict_to` execution forward into M2 (expands M2 scope; M4 is
  the restriction milestone).
- **Affects:** Core (planner guard), Spec; spec §10.4 / §14 / §16.4; diagnostic
  `RestrictToNotImplementedV1` (transitional — removed at M4).

### D-058 — Empty-output diagnostics: mechanical names replace EmptyExtent/EmptyIntent

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
  per-element emit diagnostics must aggregate (P-20) or they flood at 73M rows.
- **Rejected:** keeping `EmptyExtent` / `EmptyIntent` (wrong phase, overloaded
  names); failing on zero columns (blocks the staged-editing workflow §10.1 allows).
- **Affects:** Core (planner/emit), Diagnostics, Spec; spec §16.2 / §16.4.

---

## M1-adjacent conformance pass

Landed when shipped M1 code was reconciled with the merged M2 wording (the "Before
implementation" pass in `roadmap.md`), before M2 proper. Mostly realizes earlier
decisions (D-050 malformed-numeric, D-056 cut validation); the one new architectural
decision is below.

### D-059 — Discretization outcomes and data-diagnostic aggregation

- **Status:** accepted
- **Date:** 2026-06-28
- **Decision:** discretizers return a structured `BinResult` distinguishing a recognized
  **bin**, **no-bin** (out-of-range), an **unknown** value, and an **unparseable** numeric —
  replacing the old `string?` that collapsed all four into "label or null". Emit-phase data
  diagnostics that can occur per row (`UnknownValueObserved`, `SourceValueUnparseable`) are
  **aggregated per attribute** with a count and a bounded sample, flushed in plan order after
  the stream (deterministic — P-7), never one diagnostic per row.
- **Why:** `string?` conflated the silent no-cross cases (out-of-range, §11.2) with the
  diagnosable ones (unparseable §11.5/D-050; unknown §10.6/§11.8), so malformed/unknown data
  could not be surfaced without re-deriving it in the emitter (P-17); and per-row diagnostics
  do not scale to the v1 data target (~73M records, D-007). One result type plus one
  aggregation pattern keeps future discretizers (`equal_width`/`equal_frequency` at M4,
  `value_groups`) and emit diagnostics consistent (P-5). Also closes the §11.8 gap where an
  `ordered_cuts` value-not-in-order was silently dropped instead of treated as unknown.
- **Rejected:** keeping `string?` and re-deriving unparseable-vs-out-of-range in the emitter
  (duplicates the parse/culture logic out of the discretizer, double-parses the hot path); a
  second `TryDiscretize` out-param method (two ways to spell one decision, P-5); per-row data
  diagnostics (flood at scale, P-20).
- **Affects:** Core (discretizers, `BinResult`), Conversion (emit aggregation), Diagnostics
  (`SourceValueUnparseable`); spec §10.6 / §11.5 / §11.8 / §16.4. The §5.1 whitespace
  reconciliation in the same pass needed no decision entry — `SepTrim.Outer` is an
  implementation detail inside the existing Sep integration (D-041); its conformance tests
  are the record.

---

## Tier 1 spec audit (pre-M2)

A consolidated internal-consistency audit of `bedrock-spec-v1.md`, reconciled over
two external review rounds, before M2 implementation. Most findings were doc fixes
needing no decision; the six below change a validation/output contract or sequence a
feature, so they are recorded here. They refine, not reverse, earlier decisions.

### D-060 — Ordinal-over-cuts validation contract

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** three linked rules for ordinal scales over cut discretizers.
  (a) `scale.order` is a **value-bin** field only (`identity` / `free_per_value`):
  required for non-numeric value bins, optional for numeric. It MUST NOT appear with
  a **cut** discretizer (`manual_cuts`, `ordered_cuts`, `equal_width`,
  `equal_frequency`); presence is `OrdinalOrderNotAllowedWithCuts` (Error). The cut
  discretizer is the single source of order, and `ordered_cuts` bin order is added to
  §17 rule 3 (ascending by cut position). (b) Both ordinal-over-cuts compatibility
  checks run at **spec validate**: `OrdinalOrderNotAllowedWithCuts` and — re-phased
  from plan — `OrdinalBoundaryIncompatibleWithCuts`; both depend only on authored
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
  `declared_domain`-over-cuts / D-049 pattern) — on an *active* attribute it leaves two
  visible order declarations and can silently void the authored one; the local
  ordinal-over-cuts contract (boundary already rejects) is the tighter consistency
  axis. Placing the checks at plan — they need no data, so validate is earlier.
- **Affects:** Core (validate, `OrdinalScale`), Spec, Diagnostics
  (`OrdinalOrderNotAllowedWithCuts` new; `OrdinalBoundaryIncompatibleWithCuts`
  re-phased plan→validate); spec §6 / §12.3 / §16.4 / §17. Refines D-044 / D-046 /
  D-047 / D-049.

### D-061 — value_type matrix: free_per_value flexible, identity string-only

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** each discretizer either **fixes** the value type or is **flexible**.
  String-fixing — `identity`, `value_groups`, `ordered_cuts` (only `"string"`).
  Number-fixing — `manual_cuts`, `equal_width`, `equal_frequency` (only `"number"`).
  Flexible — `free_per_value` (either: `"number"` → parsed-numeric bin identity, so
  `90` / `90.0` / `9e1` collapse to one bin; `"string"` → verbatim spelling).
  `identity` + `"number"` is `SourceValueTypeInvalid`; numeric distinct-value binning
  uses `free_per_value`.
- **Why:** D-049 flagged the §10.2 `value_type`-vs-discretizer rule as a *live
  conflict* — it spoke of one "discretizer-implied type," which mis-described
  `free_per_value` (legitimately both) and could reject its headline numeric use
  (D-022). Naming type-fixing vs flexible makes the four real conflict cases exact and
  keeps one numeric distinct-binner (P-5).
- **Rejected:** making `identity` also flexible (a second way to spell numeric distinct
  binning, P-5); leaving the rule vague (the original live conflict).
- **Affects:** Core (validate), Spec; spec §10.2 / §11.3; diagnostic
  `SourceValueTypeInvalid` (scope clarified). Resolves the D-049-deferred item; refines
  D-022.

### D-062 — Cross-attribute restrict not modelled in v1; drop the diagnostic

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** cross-attribute restrict ("include attr A only when attr B = X") has no
  reserved carrier syntax in v1 and is **not modelled**. The named
  `RestrictCrossAttributeNotImplementedV1` diagnostic is **removed** from §20 — it was
  unreachable, since no v1 syntax could trigger it. It remains prose-only future work.
- **Why:** a `*NotImplementedV1` reservation is only meaningful when a parseable
  carrier lets a v1 spec express the feature and be cleanly rejected (the `composite` /
  `date` / scale pattern). With no carrier the code was dead. Not every future idea
  needs reserved syntax (P-3).
- **Rejected:** inventing a carrier now (P-3 — no current need); keeping the dead
  diagnostic (misleads readers into thinking the feature is expressible).
- **Affects:** Spec; spec §20; `roadmap.md` (stale "§19" → "§20" reference fixed);
  diagnostic `RestrictCrossAttributeNotImplementedV1` removed.

### D-063 — restrict_to: M2 validates shape, M4 executes; diagnostic ownership

- **Status:** accepted *(refined by D-091: exact numeric restrict entries added; `RestrictToOnNumericRequiresRange` renamed `RestrictToNumericEntryRequired` in live text)*
- **Date:** 2026-06-30
- **Decision:** M2 validates the *shape* of `restrict_to` at parse/validate even though
  execution is deferred to M4 (D-057): `RestrictToOnNumericRequiresRange` (Error) owns
  the numeric-source + non-range-entry mismatch; `RestrictToValueNotInDomain` (Warning)
  is the explicit-domain typo-catcher. `SourceValueTypeInvalid` (§10.2) does **not**
  duplicate the numeric/string-entry case. Conversion still rejects all `restrict_to`
  with `RestrictToNotImplementedV1` until M4. Mixed string/range `restrict_to` lists
  round-trip (D-057) but, under the single-`value_type` matrix (D-061), each entry must
  match the attribute's type, so a genuinely mixed list is rejected at validation (§10.4).
- **Why:** D-057 established round-trip + execution-deferral, but §10.4 prose never
  documented the static checks the §16.4 table already listed, nor their precedence vs
  `SourceValueTypeInvalid`. One condition → one owning code (P-14).
- **Rejected:** deferring shape validation to M4 with execution (an authoring error
  would surface late); letting both codes fire on the same mismatch (ambiguous, P-5).
- **Affects:** Core (validate), Spec; spec §10.4 / §10.2; diagnostics
  `RestrictToOnNumericRequiresRange`, `RestrictToValueNotInDomain`. Refines D-057.

### D-064 — Wide column object keys deferred to M3; object-key diagnostic taxonomy

- **Status:** accepted
- **Date:** 2026-06-30
- **Decision:** wide `object_key.mode = "column"` (and the `duplicate_object_policy`
  machinery it gates) is parsed and round-tripped from M2, but its **execution is
  deferred to M3**, alongside the triple subject-derived column key; until then
  conversion rejects a wide column object key with the transitional
  `ObjectKeyColumnNotImplementedV1`. Object-key validation gains a taxonomy:
  `ObjectKeyBindingInvalid` (malformed — e.g. column mode without a resolvable
  `column`), `ObjectKeyModeInvalidForShape` (e.g. `row_index` under triple);
  `ObjectKeyCompositeNotImplementedV1` is unchanged.
- **Why:** D-034 fully specified wide column-key behavior, but no milestone implemented
  it and no guard existed — a silent partial implementation (carrier parses, execution
  missing), the hole D-057 closed for `restrict_to`. M3 already builds subject-derived
  column keys, so it is the natural home. Distinct conditions get distinct codes (P-14).
- **Rejected:** implementing wide column keys in M2 (expands M2 scope; M3 is the
  object-key milestone); leaving execution unguarded (latent wrong output — a silent
  `row_index` fallback).
- **Affects:** Core (validate/plan), Sources, Spec; spec §5.4 / §6.1 / §16.4;
  diagnostics `ObjectKeyColumnNotImplementedV1` (transitional, removed at M3),
  `ObjectKeyBindingInvalid`, `ObjectKeyModeInvalidForShape`. Refines D-034.

### D-065 — Calibration/vocabulary over the input universe, before restrict_to

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
  object-filter changes (reproducibility). Documenting it as a deliberate choice — with
  the §19.4 consequence visible — prevents it being read as a bug.
- **Rejected:** population-relative calibration as the v1 default/option (P-3 — no
  current need; a larger M4 design); leaving the interaction unspecified (the audit's
  F14 — a determinism/expectation gap).
- **Affects:** Conversion (calibrate/emit ordering), Spec; spec §7 / §19.4; `roadmap.md`
  (population-relative noted as future). Refines D-021 / D-036.

---

## Tier 2 register (pre-M2): model boundary + carrier scope

A reconciled pass fixing the M2 *implementation* contracts the earlier M2/Tier-1
decisions left open: the in-memory two-model split, the resolve/validate seam, the
exact fingerprint encoding, and the carrier-vs-execution scope for discretizers,
absent domains, triple input, and `missing_policy`. These pin cross-package
contracts (P-4) before the TOML reader/writer, migrator, and fingerprints are
built; they refine, not reverse, D-009 / D-049 / D-050…D-065.

### D-066 — Parsed spec document model vs. resolved Core `BedrockSpec`

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
  name→index resolved, illegal states unrepresentable (P-10). A bind-by-header
  `NamedColumnSource` and a triple `predicate` **attribute source** are therefore
  **document-model states only**, never resolved Core sources; the Core wide
  binding carries a resolved column **index** plus a `value_type`. The one
  exception is the *basic triple binding shape* (`shape = "triple"`), which
  **does** resolve into a **minimal Core carrier** — just enough for
  `ConversionPlanner` to reject a triple spec with `TripleSourceNotImplementedV1`
  (D-072, D-067) — while triple predicate-source *execution* and the advanced
  triple surface remain document-layer / M3 concerns.
- **Why:** round-trip fidelity (D-049 presence tracking, D-057 `restrict_to`
  preservation, D-052 `extends` merge) needs a model that holds exactly what was
  authored — provenance, unresolved references, an authored `[]` — while the
  planner's determinism guarantees need a model where bad states cannot occur
  (P-10) and Core stays pure (P-13). One model cannot be both. Splitting them
  confines Tomlyn and every authoring concession to Spec and hands the planner a
  clean resolved input.
- **Rejected:** a single model for both parse and plan — it either admits invalid
  states (defeating P-10, scattering guards through the planner) or rejects at
  parse and loses the authored form a faithful round-trip needs (breaking
  D-049/D-057); resolving name→index inside Core — pulls header/schema knowledge
  into a pure package and lets a resolved source hold an unresolved reference.
- **Affects:** Spec (new `Toml/` document model), Core (`BedrockSpec`,
  `AttributeSpec`, wide binding + a minimal triple binding carrier (D-072),
  `OrdinalScale`, `ObjectKey` grow *resolved* fields), Diagnostics; spec §2 / §3 /
  §5.4 / §10.2. Defines public Core/Spec surface (P-4). Realizes D-009; refines D-049.

### D-067 — Resolve/validate seam and diagnostic phase ownership

- **Status:** accepted
- **Date:** 2026-07-03
- **Decision:** the document→Core transformation is a **single
  `SpecResolver.Resolve(document) → Diagnosed<BedrockSpec>`** step that resolves and
  validates **together** (not two sequential passes): defaults merge, `extends`
  resolves, name→index resolves, and the static checks run against the resolving
  model, aggregating all diagnostics (P-14). Each §16.4 code is **owned by exactly
  one phase**: construction-time invariants stay in Core smart factories
  (`CutValidation`, D-056; the `OrdinalScale`/`ObjectKey` factories — P-10);
  resolution-time *static* checks (source-binding shape, `value_type` matrix,
  ordinal-over-cuts, `restrict_to` shape, object-key taxonomy, and the existing
  Core duplicate-name / `value_labels` checks — invoked from the seam, not
  re-homed) live in the seam; plan-time checks (`FormalAttributeCollision`,
  `ScaleNotImplementedV1`, and the transitional `*NotImplementedV1` rejects whose
  carriers resolve into Core — e.g. `RestrictToNotImplementedV1`,
  `TripleSourceNotImplementedV1`) stay in `ConversionPlanner`, while a
  read/resolve-owned transitional reject such as `DiscretizerKindNotYetSupported`
  (D-070, no carrier built) fires in the seam; data-phase codes fire in
  Calibrate/Emit. This seam is the
  real M7 `validate` caller, so it is built where it is used, not speculatively
  (P-3). One condition → one owning code.
- **Why:** M2 is the first point hand-authored TOML can express invalid specs, so
  the ~20 static diagnostics need a definite home and a definite phase. A combined
  resolve+validate avoids a half-resolved intermediate a separate validate pass
  would re-derive; single-owner phasing keeps §16.4's "Where" column honest and
  stops two codes firing on one mistake (P-5). Reusing the existing Core static
  checks from the seam (not big-bang-refactoring them) keeps the diff surgical
  (P-1).
- **Rejected:** sequential resolve-then-validate (a throwaway half-resolved model,
  and cross-field checks want the merged view); scattering static checks across
  reader, Core, and planner (drifts from the §16.4 phase column, risks double
  reporting).
- **Affects:** Spec (`SpecResolver`), Core (factories, planner guards),
  Diagnostics; spec §7 / §16.4. Realizes the §16.4 "Where" column; pairs with D-066.

### D-068 — `missing_policy = "as_attribute"` scheduled into M2; effective-`missing_token` migration

- **Status:** accepted (schedules the previously-unscheduled §10.5 branch; migrator branch realized by D-079)
- **Date:** 2026-07-03
- **Decision:** `missing_policy = "as_attribute"` (§10.5) — omitted from the M1
  pipeline — is **implemented in M2**. Plan appends a `{column}-missing`
  `FormalAttribute` at the correct ordinal position (after the value columns for
  nominal; the second column for dichotomic) with its own canonical identity; Emit
  crosses it when a value is *missing* (empty cell or explicit `missing_token`
  match — never a merely unparseable numeric, D-050) instead of the M1
  unconditional skip. The `.bed` migrator detects it structurally: a
  `[Category Values]` entry equal to the **effective `binding.missing_token`** (the
  resolved token, *not* a hardcoded `?`) becomes `missing_policy = "as_attribute"`
  and is excluded from `declared_domain`.
- **Why:** the column set `as_attribute` produces is deterministic and enters
  `schema_fingerprint` through the planned list (§14), so leaving it unimplemented
  while M2 ships fingerprints would freeze an incomplete schema; and the migrator
  must recognize a non-`?` missing token or it silently drops a real v2 attribute.
  Anchoring the ordinal-position rule and the effective-token rule keeps the
  planner/emitter/migrator (three packages) consistent (P-4).
- **Rejected:** deferring `as_attribute` past M2 (its column is a fingerprint
  input, §14 — would ship a knowingly-incomplete schema hash); hardcoding `?` in
  the migrator (misreads any spec with a custom `missing_token`).
- **Affects:** Core (`ConversionPlanner`, `PlannedAttribute`), Conversion
  (`Emitter`), Spec (`BedToSpec`); spec §10.5 / §14 / §17. Byte-neutral on the M1
  goldens (no fixture uses `as_attribute`).

### D-069 — Canonical fingerprint encoding, pinned (appendix to D-053)

- **Status:** accepted (appends D-053; pins the exact structure before any stored hash ships)
- **Date:** 2026-07-03
- **Decision:** the plan-derived canonical structure D-053 mandated is pinned
  concretely, so a stored hash is portable and version-tagged before Slice E ships
  one (P-11). It is **UTF-8, no BOM**, with sorted object keys, arrays in planned
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
    endings + trailing-space for dat) — never mixed;
  - **numbers** are the *parsed* numeric value reformatted with invariant, shortest
    round-trippable .NET formatting, so `30`, `30.0`, `3e1` collapse and no machine
    float drift occurs (P-11);
  - **strings** use one JSON escaping rule (minimal `\"`, `\\`, control escapes,
    UTF-8 passthrough otherwise).

  Slice E realizes exactly this and ships the canonical-stability golden that locks it.
- **Why:** D-053 fixed the *properties* (version tag, sorted keys, shortest
  numbers, structural open ends) but not a concrete shape, and a durable hash
  contract (P-11) cannot ship half-specified — the first stored fingerprint
  fossilizes whatever the encoder emits. Pinning the root shape, field names, and
  version literal now makes Slice E mechanical and the golden a genuine lock.
- **Rejected:** hashing TOML text (D-053, formatting-sensitive); leaving field
  names to the encoder (the first stored hash fossilizes an unreviewed shape);
  `∞`/`"all"` sentinels for open ends (string-fragile; booleans are exact);
  `"R"`/`"G17"` floats (17 digits defeat the `30.0`≡`30` collapse, D-053).
- **Affects:** Spec, Core (fingerprint encoder); spec §3 / §14. Needs the
  canonical-stability golden at Slice E. Appends D-053; supports D-051.

### D-070 — Minimal M2 discretizer-carrier scope; three-tier kind response

- **Status:** accepted
- **Date:** 2026-07-03
- **Decision:** M2 executes only the three M1 discretizers; the rest are
  **recognized by kind name and rejected**, in **three tiers**:
  - **executable** — `identity`, `manual_cuts`, `ordered_cuts` (M1) parse, resolve,
    and convert;
  - **known-but-not-yet-supported** — `free_per_value`, `equal_width`,
    `equal_frequency`, `value_groups` are recognized by **kind name only** and
    rejected at **read/resolve** with a **transitional** `DiscretizerKindNotYetSupported`.
    M2 does **not** build full document/Core carriers for their parameter shapes and
    does **not** promise round-trip for them (minimal carrier);
  - **unrecognized** — any other `kind` is a generic unrecognized-kind **parse** error.

  The distinction that survives is the *code*: a recognized deferred kind gets an
  actionable "valid v1 feature, later milestone" diagnostic; a typo gets a generic
  unknown-kind error. `free_per_value`, `equal_width`, `equal_frequency`, and
  `value_groups` all execute at **M4** (roadmap.md). The M2-executable value-bin
  ordinal is therefore `identity` + an explicit **string** `order` only (D-061
  string-fixing); numeric value-bin ordinal (`free_per_value` + `order`) is rejected
  in M2 by the `free_per_value` read/resolve reject above, never silently accepted.
- **Why:** M2's job is the format, migrator, fingerprints, and `extends` — not new
  discretizers (M4 is the discretizer milestone). Building full document/Core
  carriers and round-trip for parameter shapes M2 cannot execute is speculative
  surface for kinds no M2 workflow reaches (P-3/P-6) — and no v2 `.bed` type maps to
  these vNext-native discretizers, so migration loses nothing by not carrying them.
  Recognizing the *name* is enough to separate "valid v1 feature, later milestone"
  (a transitional, actionable diagnostic) from "typo / unknown kind" (a generic
  parse error) so an author can tell which they hit (P-14). Rejecting at read/resolve
  (not silently accepting) avoids the wrong-output hole D-057 closed for `restrict_to`.
- **Rejected:** implementing the M4 discretizers in M2 (scope creep, P-1); building
  full round-trip carriers for the deferred discretizers' parameter shapes in M2
  (speculative surface for kinds M2 cannot execute — P-3/P-6; no v2 type maps to
  them, so migration loses nothing); one code for both not-yet-supported and unknown
  kinds (hides whether the spec is valid, P-14); silently ignoring unimplemented
  kinds (latent wrong output).
- **Affects:** Spec (reader/resolver), Diagnostics; spec §11 / §16.4; diagnostic
  `DiscretizerKindNotYetSupported` (transitional, **read/resolve**, removed as each
  kind lands). Refines D-020 / D-061; roadmap.md assigns `value_groups` → M4.

### D-071 — Absent/empty `declared_domain`: M2 interim reject until calibrate

- **Status:** accepted (sequences §10.3 for M2; refines D-036; refined by D-076 —
  retirement is "when observed-domain calibration lands", not a fixed milestone)
- **Date:** 2026-07-03
- **Decision:** omitted `declared_domain` **and** an explicit empty `[]` both
  resolve as **absent** (§10.3), and the reader/writer **round-trips the authored
  form verbatim** (omitted stays omitted, `[]` stays `[]`). The §10.3 end-state —
  Calibrate fills an absent domain from observed data with `ObservedDomainUsed` — is
  **not yet built in M2** (Calibrate is M4-ward), so an M2 conversion of the
  **M2-supported value-bin discretizer** (`identity`) with an absent domain is
  **rejected** with a transitional `ObservedDomainCalibrationNotImplementedV1`,
  never silently emitting an empty or data-order-dependent schema. **Precedence:**
  `free_per_value` is a *deferred* discretizer (D-070), already rejected earlier at
  read/resolve with `DiscretizerKindNotYetSupported` — that code owns the
  `free_per_value` case, and the absent-domain code never fires for it in M2 (one
  condition → one owning code, P-14). Cut discretizers ignore `declared_domain`
  (§10.3) and are unaffected.
- **Why:** the authored `[]`-vs-omitted distinction must survive round-trip (D-049)
  even though both mean "absent," so provenance is preserved without inventing a
  third state. An absent value-bin domain has no columns until calibration observes
  the data; converting it in M2 without Calibrate would emit zero columns or
  silently depend on input order — both violate the spec's reproducibility intent. A
  transitional reject makes the gap explicit and actionable (the D-057 / D-070
  pattern) until observed-domain calibration lands (numeric auto-binning
  calibration is M4; the categorical case is backlog-unassigned — D-076).
- **Rejected:** treating `[]` as "zero columns" (contradicts §10.3); silently
  calibrating in M2 (Calibrate is not built — a latent, undocumented
  data-dependence); collapsing omitted and `[]` at read time (loses authored
  provenance, D-049).
- **Affects:** Spec (reader/writer), Core (planner guard), Diagnostics; spec §7 /
  §10.3; diagnostic `ObservedDomainCalibrationNotImplementedV1` (transitional,
  removed when observed-domain calibration lands — see the roadmap backlog and
  D-076). Refines D-036; pairs with D-070.

### D-072 — Basic triple TOML carrier in M2; conversion deferred to M3

- **Status:** accepted (sequences §5.3 for M2; refines D-009)
- **Date:** 2026-07-03
- **Decision:** M2 carries and **round-trips** the *basic* triple binding —
  `shape = "triple"`, `ordering`, `columns` (`subject`/`predicate`/`value`), and
  `{ kind = "predicate", name = … }` attribute sources — but **conversion rejects**
  any triple spec with a transitional `TripleSourceNotImplementedV1` until the
  triple source lands at M3. This M2 reject is **required** so a triple spec is not
  silently mis-converted as wide. The **advanced** triple surface (triple header
  rows, binding `columns` by header **name**, object/subject-name filtering) stays
  **M3** and is neither parsed nor relied on in M2 (§5.3). The triple *execution*
  diagnostics (`TripleSubjectNotContiguous`, …) are M3, not M2.
- **Why:** round-trip and migration need the basic triple carrier now (the three v2
  `mini-*_triples` examples must survive read→write→read as documents), but the
  triple *reader/streaming* is the M3 milestone. Parsing-but-rejecting closes the
  silent-mis-conversion hole (D-057 pattern) while keeping M2 scoped to the format.
  Deferring the advanced surface matches the §5.3 M3 finalization note.
- **Rejected:** pulling triple conversion into M2 (that is the whole M3 milestone —
  scope creep, P-1); omitting the triple carrier from M2 (migration of the
  `mini-*_triples` specs would drop config, D-049); parsing the advanced surface now
  (no M2 consumer, P-3).
- **Affects:** Spec (reader/writer), Core/Conversion (planner guard), Diagnostics;
  spec §5.3 / §16.4; diagnostic `TripleSourceNotImplementedV1` (transitional,
  removed at M3). Refines D-009; the M3 triple audit owns the advanced surface.

### D-074 — `as_attribute` missing-column position uniform across scale kinds (appendix to D-068)

- **Status:** accepted (appends D-068; lands with M2 Slice B)
- **Date:** 2026-07-04
- **Decision:** the `{column}-missing` column appends **after the scale's columns
  for every scale kind** — D-068 named nominal (after the value bins) and
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
  `ordinal + as_attribute` silently behaving as skip — the exact gap D-068
  closes — which Slice E would then freeze incorrectly into `schema_fingerprint`.
- **Rejected:** rejecting `as_attribute` on ordinal scales (extra code plus a
  transitional diagnostic for a combination that works uniformly); keeping
  `MissingPolicy` on `PlannedAttribute` alongside the id (two fields with an
  invariant to keep in sync).
- **Affects:** Core (`ConversionPlanner`, `PlannedAttribute`), Conversion
  (`Emitter`); spec §10.5 (ordinal clause). Appends D-068.

### D-075 — Slice C TOML reader/writer contract: strictness, parse codes, canonical form

- **Status:** accepted
- **Date:** 2026-07-04
- **Decision:** the M2 Slice C reader/writer fixes the contracts the spec left
  open:
  - **Library:** Tomlyn (D-009's lean), exact-pinned via central package
    management and confined to `FcaBedrock.Spec` (D-066); the reader walks the
    CST (`SyntaxParser.Parse` → `DocumentSyntax`) for node spans, and no Tomlyn
    type appears on any public signature.
  - **Reader strictness:** unknown keys/tables are **Errors**
    (`SpecKeyUnrecognized`) — the spec is silent on unknown-key policy, and a
    reader that accepted what the writer would drop makes read→write silently
    lossy. Known keys with the wrong type/shape/spelling are `SpecFieldInvalid`
    (one code, message names the expected form; also the D-070 tier-3
    unknown-kind case). TOML-level errors are `SpecTomlInvalid` (Fatal; Tomlyn
    parser warnings surface under the same code at Warning).
  - **Two-phase aggregation (the P-14 reading):** all TOML syntax errors report
    together and are terminal (a broken tree would cascade garbage); on clean
    syntax, one whole-document semantic pass aggregates every diagnostic.
  - **Deferred-surface scaffolding:** recognized-but-unmodelled v1 surface —
    `[spec].extends`, `[[template]]`/`[[matcher]]`, attribute `template` /
    `display_name` / `formal_attribute_format`, `[defaults]`
    `formal_attribute_format`, `value_type = "date"` — fails the read with the
    transitional `SpecSurfaceNotYetSupported` (Error) so nothing known is
    silently dropped. The set is **closed and per-owning-table**, never a
    fallback: near-miss keys and a listed name in the wrong table get
    `SpecKeyUnrecognized`. Entries retire as slices D–G land their carriers;
    the `date` entry retires when the D-038 carrier lands (the v1 end-state is
    the plan-phase `DateValueTypeNotImplementedV1`).
  - **Canonical writer:** hand-rolled emission (not Tomlyn serialization — the
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
    `Diagnosed<SpecDocument>` and `SpecWriter.Write(SpecDocument)` → `string` —
    string-only, mirroring `BedReader`; file I/O belongs to a host slice.

  The slice also realizes D-010 for scales: `interordinal`/`biordinal`/
  `contranominal` parse into a kind-only `DeferredScaleSection`, resolve into
  the Core `UnimplementedScale` reject-carrier (the D-072 pattern), and fail at
  **plan** with `ScaleNotImplementedV1` (Fatal) — parse-but-fail-to-plan, the
  roadmap-M2 "parsable types the planner rejects" item.
- **Why:** round-trip fidelity is a headline property of the format, so reader
  strictness and writer canonicalization must be decided together — the reader
  must reject exactly what the writer cannot re-emit. Distinct codes keep
  typo-vs-valid-feature actionable (the D-070 rationale); the closed
  deferred-surface set keeps the transitional code from becoming a catch-all.
  Hashing is already formatting-immune (D-053), so one canonical written form
  costs nothing and buys deterministic output and a trivial round-trip oracle.
- **Rejected:** warning-and-drop for unknown keys (silent loss on write-after-
  read); one code for all parse problems (hides whether the spec is valid v1 —
  P-14); parsing deferred surface into inert carriers now (pulls Slice F /
  naming-slice semantics forward, and an authored-but-ignored
  `formal_attribute_format` would silently change intended output); Tomlyn's
  serializer for writing (its formatting choices can drift across versions);
  byte-preserving round-trip (would require a lossless CST document model for
  no consumer — §2 makes formatting informative).
- **Affects:** Spec (`SpecReader`, `SpecWriter`, `TomlSpellings`, `TomlLiteral`,
  read infra; `DeferredScaleSection`; resolver mapping), Core
  (`UnimplementedScale`, planner guard), Diagnostics (`SpecTomlInvalid`,
  `SpecKeyUnrecognized`, `SpecFieldInvalid`, `DiscretizerKindNotYetSupported`,
  `SpecSurfaceNotYetSupported`, `ScaleNotImplementedV1`), spec §16.4,
  `Directory.Packages.props`. Realizes D-009/D-066/D-070/D-071/D-072 read/write
  faces; realizes D-010 for scales.

### D-076 — Slice D seam/plan validation contract details

- **Status:** accepted (appends D-067; realizes D-054/D-060/D-061/D-063/D-064/
  D-071 at the seam and planner) *(refined by D-091: exact numeric restrict entries added; `RestrictToOnNumericRequiresRange` renamed `RestrictToNumericEntryRequired` in live text)*
- **Date:** 2026-07-05
- **Decision:** Slice D activates the static validation the earlier decisions
  assigned but left operationally open; the details settled here:
  - **`include = false` interaction matrix.** (a) The `restrict_to` **shape**
    checks (`RestrictToOnNumericRequiresRange`; the range-entry-on-string-source
    case of `SourceValueTypeInvalid`) run on **excluded/filter-only attributes
    too**: `restrict_to` is *live* config, not parked — §10.1/§10.4 apply it
    whether or not the attribute is included (the §19.4 filter-only pattern), so
    its static shape is validated on the same terms. (b)
    `RestrictToValueNotInDomain` does **not** run on excluded attributes:
    `declared_domain` is emitted-shaping config, parked under D-049 — a live
    check must not warn against a dormant list. (c) A **parked numeric-cut
    discretizer still types a live `restrict_to`** (an excluded attribute
    retaining `manual_cuts` resolves `value_type = "number"`, so a bare-string
    entry rejects). Deliberate, not a D-049 violation: `value_type` is a
    source-level property whose D-061 derivation is include-independent, and
    §10.4 itself defines a numeric source as "`value_type = "number"`, *or a
    numeric-cut discretizer*" — recorded so the combination is not later "fixed"
    into a silent skip. The remaining Slice D checks (the `value_type` matrix,
    ordinal-over-cuts) are include-gated per D-049/D-060 ("active attribute").
  - **Quote/delimiter co-fire.** `QuoteCharNotSupportedV1` (authored quote ≠
    `"`) and `BindingDelimiterQuoteConflict` (resolved delimiter = resolved
    quote) are distinct §5.1 conditions and report independently — both fire
    when both hold (e.g. delimiter and quote both authored `|`). Two conditions,
    two codes (P-14), not double reporting of one.
  - **`ObservedDomainCalibrationNotImplementedV1`** is an **Error at plan** and
    **blanket across scales** for an included `identity` attribute with an
    absent domain — dichotomic included, because with no domain every observed
    value is "unknown" and the single column never crosses (the same
    silent-wrong-output D-071 closes). The blanket survives the future value-bin
    ordinal slice: `scale.order` orders bins, but the domain remains the bin
    *source* for `identity`, so an ordinal `order` never substitutes for a
    domain. Retirement is phrased "when observed-domain calibration lands" — the
    categorical case is unassigned in the roadmap backlog — not a bare "M4".
  - **Domain typo-catcher gate.** `RestrictToValueNotInDomain` additionally
    requires a resolved **string** `value_type`: on a mis-typed numeric
    `identity` source the same entries are already owned by
    `SourceValueTypeInvalid` / `RestrictToOnNumericRequiresRange`, and a third
    diagnostic would be noise (P-14).
- **Why:** the phase/owner assignments were settled (D-060/D-063/D-064/D-067/
  D-071), but the excluded-attribute interactions, the co-fire policy, and the
  blanket's scale coverage were not derivable from any single entry — and each
  reads as a bug (a D-049 violation, a P-14 violation, an over-broad reject)
  unless the rationale is on record.
- **Rejected:** skipping restrict_to shape checks on excluded attributes (a
  filter-only attribute's restrict_to changes output at M4, so its shape errors
  must surface at authoring time); warning against a parked domain (turns
  toggling an attribute off into new warnings — the authoring-hostility D-049
  removed); suppressing the delimiter conflict when the quote is unsupported
  (hides an independent, independently-fixable mistake).
- **Affects:** Spec (`SpecResolver` seam checks), Core (`ConversionPlanner`
  guards), Diagnostics (the twelve Slice D codes); spec §10.3 / §16.4. Each
  matrix point is locked by a dedicated test in `SpecResolverTests` /
  `ConversionPlannerTests`.

### D-077 — Slice E fingerprint encoding/verification contract details

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
    bin runs to ±∞ on that side), never interval inclusivity** — every bounded
    cut bin is uniformly half-open `[lo, hi)` (§11.2), so `<30` carries
    `hi_open: false`. Bounds are JSON numbers for `manual_cuts`, JSON strings
    for `ordered_cuts`. Non-interval bins stay plain strings: value bins,
    ordinal thresholds (incl. `all`, D-047), the dichotomic empty key, the
    missing column's `missing`.
  - **The plan carries the structure:** `FormalAttribute` gains a public
    `CanonicalBin` (`ValueBin` / `NumericCutBin` / `TextCutBin`; `null` bound =
    unbounded), produced by the same planner walk as the identity's `BinKey` —
    single producer, no string parse-back (P-4 surface, byte-neutral on the M1
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
    D-049 parked-config discipline). `manual_cuts.Culture` is not encoded — it
    *is* `binding.locale`, and double-counting is the mistake D-035 removed.
    `restrict_to` is absent until executable (§14); M4 adds it
    present-only-when-non-empty, so restrict_to-free specs keep their hashes,
    and filter-only attributes join through that same route.
  - **`cxt`/`dat` content:** per D-069 (`bin_label_unicode` + `label_style` +
    `line_endings` + `rendered_names` + `trailing_newline`; `base_index` +
    `empty_line_trailing_space` + `line_endings` +
    `nonempty_line_trailing_space`). **`size_advisory_bytes` is not an input**
    — it changes a warning, never output bytes; §3's byte-affecting definition
    wins over §8's blanket sentence (clarified there). `bin_label_unicode`
    hashes the resolved flag although Unicode rendering is not built yet: when
    rendering lands, rendered names change and the stale warning fires — the
    mechanism working as designed.
  - **Pairing precondition:** `ComputeCxtOutputFingerprint`'s `LabelStyle`
    input must be the style the plan was produced with (rendered names bake it
    in); `SpecFingerprints.ComputeNative` guarantees the Native/Native pairing
    (v2-compat is a CLI override, D-011); the M7 effective path pairs
    V2Compat/V2Compat.
  - **Verification:** defined over a successful plan only (a failed
    resolve/plan already fails the run). Absent stored field → silent (§3
    optional); match → silent (no "verified" noise, P-3); mismatch → its own
    Warning (`SchemaFingerprintStale` / `CxtOutputFingerprintStale` /
    `DatOutputFingerprintStale`, phase "spec load"); all stale fields co-fire
    (the D-076 stance). No production call site is added — tests call it now
    and M7's CLI is the real caller (the D-067 pattern). *Writing* stored
    fingerprints and enforcing the §14 fully-frozen gate are M7 tooling.
- **Why:** the first stored hash fossilizes the encoder's bytes, so every
  residual freedom — compactness, escaping, the unbounded-end encoding, the key
  vocabulary, the exact `shared` key set — had to be pinned and golden-locked
  before any spec ships with a stored value (P-11, D-069). The locks: the
  hand-authored canonical-JSON goldens plus a hard SHA-256 vector
  (`FingerprintCalculatorTests`), the roadmap 30/30.0/3e1 TOML golden, and the
  §19.1/§19.2 end-to-end baselines (`SpecFingerprintsTests`).
- **Rejected:** renaming `lo_open`/`hi_open` to `*_unbounded` (amends D-069's
  pinned field names for a readability gain the definition here provides —
  review-settled); omitting the `lo`/`hi` key on unbounded ends (a conditional
  shape; the fixed four-key object is the literal D-069 reading); hashing the
  authored `declared_domain` under cut discretizers (an inert edit would move
  output fingerprints); a `System.Text.Json` writer (library-version drift on
  a durable hash contract); raw line-ending characters in the canonical bytes
  (escape noise; the token names are the spec's own vocabulary).
- **Affects:** Core (`CanonicalBin` + leaves, `FormalAttribute.Bin`,
  `BinScheme.Bins`, `FormalAttributeShape.Bin`, discretizer
  `DescribeBins`/`ConsumesDeclaredDomain`, new `Fingerprinting` namespace:
  `FingerprintCalculator`, `CanonicalJson`, `CxtFingerprintInputs`,
  `DatFingerprintInputs`, `LineEnding`), Spec (`SpecFingerprints`,
  `ComputedFingerprints`), Diagnostics (the three stale codes); spec §3 / §8 /
  §14. Appends D-069; byte-neutral on the M1 goldens.

### D-078 — Slice F composition/carrier contract details

- **Status:** accepted (realizes D-027/D-052; refines D-067/D-075)
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
    composition is part of the **spec-resolve phase** — the
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
    Slice F work** (D-075 "file I/O belongs to a host slice", P-3); tests
    compose through an in-memory source.
  - **Resolve-without-compose is a call-contract violation.** `Resolve` on a
    document with an authored `extends` throws `ArgumentException` — the
    document is not bad; the *call* skipped composition. Resolve never throws
    for valid inputs under its contract; an uncomposed extends document is
    invalid input to Resolve. Extends can therefore never be silently ignored,
    and no diagnostic code is spent on a host-sequencing error.
  - **Version gating.** Every spec in a chain must itself declare
    `version = 1`: the composer gates the **root before any source
    consultation** (an unversioned root must not drive v1 extends semantics or
    surface a missing-base/cycle diagnostic first) and **each base at its
    load** (Fatal `SpecVersionUnsupported`, base-file location). The composed
    document's version (the derived file's) is re-checked only by the seam —
    per flow the condition fires exactly once.
  - **Merge details** (beyond §13's own rules): the composed `[spec]` is
    derived-only (version, description, stored fingerprints; base-stored
    fingerprints never merge); the nested `[binding]` tables (`columns`,
    `object_key`) override as **whole values** — per-leaf mixing would compose
    an incoherent key-mode hybrid or a partial triple remap with silently
    duplicated role indices (unvalidated until M3) — unlike `[output]`'s
    per-leaf merge; attribute/template replacement searches only the **base
    region** of the working list and marks each name/id overridden at most
    once, so authoring duplicates are preserved into the composed document for
    the flat-file diagnostics (`AttributeNameDuplicate`) rather than silently
    collapsed.
  - **Template/matcher carriers.** `TemplateSection` mirrors the
    `AttributeSection` config fields (minus `name`/`source`/`description`,
    §9.1) as a deliberately **flat** record — a shared config record earns its
    keep when M6 applies templates; `MatcherSection`/`MatchSection` carry
    `match` at authored shape (`source_index_range` at authored arity; M6 owns
    pattern semantics). Template bodies parse with attribute strictness:
    per-attribute identity fields fall to `SpecKeyUnrecognized` (the D-075
    wrong-table stance), the naming-deferred keys stay
    `SpecSurfaceNotYetSupported`, deferred discretizer kinds stay
    `DiscretizerKindNotYetSupported` (D-070). Same-`id` template merge
    (replace in place, first base-region match) is **carrier composition
    only**, not resolution precedence; within-file duplicate ids are
    unvalidated until M6.
  - **Use-reject ownership.** `TemplateMatcherNotImplementedV1` fires at the
    **seam**, not the planner — templates/matchers never resolve into Core, so
    by D-067's own criterion (plan-time rejects are for carriers that resolve
    into Core) a Core reject-carrier would be speculative surface removed at
    M6. §16.4's Where cell moves from "plan (transitional)" to "spec resolve
    (transitional)" accordingly. Granularity: one aggregated Error per document
    for `[[matcher]]` presence (anonymous and span-less at document level;
    count in the message) plus one Error per attribute with an authored
    `template` reference (AttributeName location); emitted before the
    binding-shape gate so they aggregate on shape-less and triple documents.
    An unreferenced `[[template]]` is inert and resolves/converts cleanly.
  - **Deferred-surface retirement.** `extends`, `[[template]]`/`[[matcher]]`,
    and attribute `template` leave the D-075 `SpecSurfaceNotYetSupported` set
    (their sets/branches deleted); the remaining entries
    (`display_name`/`formal_attribute_format`, `value_type = "date"`) belong
    to the naming-fidelity slice and D-038.
- **Why:** D-027/D-052 fixed the merge semantics but not the API shape, phase
  ownership, version gating, nested-table granularity, duplicate handling, or
  carrier scope — and each unpinned point reads as a bug or an invitation to
  scope creep without the rationale on record. The fingerprint invariant (§13:
  composed ≡ flat, all three fingerprints) is locked by tests over the
  unchanged Slice E machinery — composition needed no encoder change, which is
  itself evidence the document→document design is at the right altitude.
- **Rejected:** merging inside `Resolve` (a loader on the seam signature and a
  half-composed intermediate — the shape D-067 already rejected); a
  resolve-side "not found" diagnostic for the uncomposed case (synthesizes a
  fake condition for a host bug and dilutes `SpecExtendsNotFound`); per-leaf
  merge of `columns`/`object_key` (incoherent hybrids); collapsing same-name
  derived duplicates (masks `AttributeNameDuplicate`); a shared
  attribute/template config record now (churns every construction site for a
  duplication M6 may reshape anyway); a compose-then-resolve convenience
  overload before M7 gives it a real caller (P-3).
- **Affects:** Spec (`SpecComposer`, `ISpecTextSource`/`SpecSourceText`,
  `SpecSection.Extends`, `AttributeSection.Template`,
  `TemplateSection`/`MatcherSection`/`MatchSection`, reader/writer carriers,
  `TomlSpellings` set retirements, `SpecResolver` guard + rejects),
  Diagnostics (`SpecExtendsNotFound`, `SpecExtendsCycle`,
  `TemplateMatcherNotImplementedV1`); spec §9 / §13 / §16.4. Realizes
  D-027/D-052; refines D-067/D-075; byte-neutral on the M1 goldens and the
  Slice E fingerprint baselines (Core untouched).

### D-079 — Slice G `.bed` migrator contract: document-model target, Diagnosed surfaces

- **Status:** accepted (realizes D-009's save-as-TOML face, the D-049
  migrator-hygiene item, D-068's migrator branch, and D-057's carriage;
  supersedes the M1 Core-targeting `BedToSpec`) *(numeric restrict migration refined by D-091: a parseable v2 numeric token migrates to an exact `{ value = n }` entry)*
- **Date:** 2026-07-05
- **Decision:** the one-way v2 migrator targets the **document model**:
  `BedMigrator.Migrate(BedDocument, BindingSection, ScalingMode, derivedFrom?) →
  Diagnosed<SpecDocument>` replaces `BedToSpec` (Core-targeting, throwing), and
  `BedReader.Read(text, filePath?)` returns `Diagnosed<BedDocument>` (structural
  problems — missing section, entry-count shortfall, unparseable count/convert
  flag — are `BedStructureInvalid`, Fatal, aggregated). A migrated spec is
  written by `SpecWriter`, resolved through the one seam (D-066/D-067), and
  fingerprinted like any authored spec; the golden harness runs this
  migrate→resolve route. Contract points:
  - **Carry what the carrier represents; validation stays at the seam.** The
    migrator transcribes; representable-but-invalid config (e.g. non-ascending
    cuts) carries verbatim and fails at resolve under its owning code
    (D-056/D-067) — an *included* malformed-cut `.bed` therefore now fails at
    resolve, not migrate. Only transcription failures diagnose at migrate: an
    unparseable numeric cut token (the carrier stores numbers) or a dichotomic
    true value equal to the effective missing token (contradicts the D-068
    domain exclusion; no seam check would catch it) → `BedAttributeConfigInvalid`;
    type `d` on an included attribute → `BedDateTypeNotSupported` (D-038 parity
    deferral — failing is honest, a spec silently missing an included attribute
    changes the analysis; retires if the date carrier lands); an unknown type
    code → `BedTypeUnrecognized` (the D-070 typo-vs-deferred tiering).
  - **Parked config (D-049):** an excluded attribute parks its **full** config —
    including representable-but-invalid config, which the seam skips while
    parked, so flipping `include = true` is what surfaces validation (better
    than M1, which degraded it). Untranscribable parked config degrades to bare
    excluded (`name`, `source`, `include = false`, plus the live `restrict_to`)
    with `BedParkedConfigDropped` (Warning) — replacing M1's silent broad
    `catch (Exception)`. Excluded attributes hence resolve parked-with-nulls in
    Core rather than M1's carried discretizer/scale — planner- and
    fingerprint-inert (both skip excluded attributes).
  - **restrict_to (D-057):** a non-empty `[Restrict To Values]` line migrates to
    authored string `RestrictToValue` entries, tokens verbatim (no per-token
    trim — v2 restrict is raw-value equality, OR within an attribute),
    include-independent (§10.1/D-076); a blank line stays unauthored. Numeric
    attributes keep string entries too — a range cannot express v2's exact
    match ([x, x) is empty under lo-inclusive/hi-exclusive) — and the seam's
    `RestrictToOnNumericRequiresRange` owns the mismatch; the migrator stays
    silent (one condition → one owning code, P-14). The planner still rejects
    any carried `restrict_to` with `RestrictToNotImplementedV1` until M4.
  - **Missing token (D-068):** for the domain-list types (`c`, `b`) a
    `[Category Values]` entry equal to the **effective** token — null when
    `missing_token` is authored `""` (§5.1: detection disabled), else the
    authored value or the default `?` — selects `missing_policy = "as_attribute"`
    and leaves `declared_domain`/`value_labels`; a display label on the token
    has no v1 carrier (`{column}-missing` is canonical, D-074) →
    `BedMissingTokenLabelDropped` (Warning). Cut types (`o`, `n`) get no
    detection — their `[Category Values]` is a cut spec, not a domain.
  - **Authoring rules:** `ends` is always authored (the resolver defaults an
    absent `ends` to open, but a sentinel-less v2 cut spec means closed —
    omission would silently flip it); the progressive ordinal authors
    `direction = "le"` **only** (an authored `boundary`/`order` over cuts is a
    D-060 validation error; the resolver defaults reproduce v2's rendering);
    everything the resolver already defaults correctly stays unauthored
    (`value_type`, quote/locale/missing token, object key, the policies,
    `include = true`, `[defaults]`, `[output]`). Dichotomic display labels now
    carry as dormant `value_labels` (byte-/fingerprint-inert; dichotomic renders
    the column name alone) instead of dropping silently. `[spec]` holds
    `version = 1` only — no stored fingerprints (a migrated spec is unfrozen;
    D-057) and no `created_at` (no clock in pure code — P-7); `derived_from`
    lands in `[provenance]` when the caller supplies it.
- **Why:** D-009 promised "load v2, save as TOML", but the M1 migrator could
  only produce a Core spec — unwritable, unfingerprintable, restrict-dropping —
  and D-049 deferred the hygiene ("no silently-dropped parked config") to this
  rework. Targeting the document model gets the writer, seam validation, and
  fingerprints for free and keeps every static check single-homed (D-067);
  the migrated mini-mushroom reproducing the pinned Slice E baselines is the
  proof the `.bed` path and the §19.1 TOML path are one spec.
- **Rejected:** keeping a Core-targeting migrator alongside the document path
  (two producers to hold semantically aligned forever); wrapping the M1
  exceptions in `Diagnosed` at the edges (keeps the broad catch and the silent
  drops); converting v2 numeric restrict tokens to ranges (changes semantics —
  see above); parking an included `d` attribute as excluded so migration
  "succeeds" (silently changes the analysis — the exact hazard D-068 names);
  hardcoding `?` in missing-token detection (already rejected by D-068).
- **Affects:** Spec (`BedReader`, `BedMigrator` replacing `BedToSpec`,
  `BedDocument`/`ScalingMode` docs), Diagnostics (`BedStructureInvalid`,
  `BedDateTypeNotSupported`, `BedTypeUnrecognized`, `BedAttributeConfigInvalid`,
  `BedParkedConfigDropped`, `BedMissingTokenLabelDropped`), golden harness
  (migrate→resolve route; `FixtureCase` supplies a `BindingSection`), spec
  §16.4 (the `migrate (v2)` phase rows). M1 goldens byte-identical; Slice E
  fingerprint baselines unchanged.

### D-080 — `AttributeNameDuplicate` / `ValueLabelKeyNotInDomain` re-homed to the resolve seam

- **Status:** accepted (the M2-exit standalone cleanup; supersedes D-067's
  "invoked from the seam, not re-homed" parenthetical for these two codes)
- **Date:** 2026-07-05
- **Decision:** the two remaining Core-emitted spec-validate checks —
  `AttributeNameDuplicate` and `ValueLabelKeyNotInDomain` — are **physically
  moved** into `SpecResolver` as private static checks over the **document
  model**, and deleted from `ConversionPlanner`. Both are §16.4 *spec validate*
  codes (their "Where" cell already read `spec validate`); the planner emitting
  them was the phase drift flagged at the Slice F review. The re-home matches
  the D-076 seam style: the dup-name check runs in the wide-shape attribute loop
  (after the triple early-return, so triple documents are unaffected — as the
  planner's triple guard already did), skips null/empty names
  (`AttributeNameMissing` owns those), and reports **one diagnostic per extra
  occurrence**, message verbatim. The `value_labels` check runs in the
  `include`-gated `ValidateAttributeConstraints` block, keyed on
  `section.Discretizer is IdentityDiscretizerSection` (≡
  `Discretizer.ConsultsValueLabels` in M2 — `free_per_value` is the only other
  consulting kind and is read-rejected before the seam, D-070; it joins the gate
  at M4), checking keys against `declared_domain ?? []`. `FormalAttributeCollision`
  / `FormalAttributeNameCollision` stay at **plan** as the output-integrity
  backstop (colliding *rendered/canonical* identities, not authored names).
- **Why:** D-067 assigned these to the seam but, to keep that slice surgical,
  left them "invoked from the seam, not re-homed" — a parenthetical the code
  never realized (they stayed in the planner). Realizing the physical re-home
  over the **document model** is not cosmetic: `ResolveAttribute` returns `null`
  for an attribute whose source/discretizer/scale fails to resolve, so a
  Core-model check would *lose* a duplicate whose sibling field is broken — the
  document-model check catches it and aggregates with that sibling's own
  diagnostic (P-14). It also makes §16.4's "Where" column honest without a spec
  edit.
- **Consequences:** (i) a Core-only caller hand-building a `BedrockSpec` and
  planning it directly no longer gets these two checks — the resolve seam is the
  enforced entry point for spec validation (every §16.4 spec-validate code is
  already seam-owned; the planner keeps only its plan-phase checks). (ii) These
  checks aggregate within the resolve pass rather than the plan pass — the
  cross-phase split every D-067 seam check already has. Neither is a public API
  change: no new public Core surface, no production `InternalsVisibleTo`, and the
  diagnostic codes/severities/messages are unchanged. **Byte- and
  fingerprint-neutral** on every golden and pinned baseline (validation-only; no
  planned column, name, or hash input moves).
- **Rejected:** a Core *helper* over the resolved model invoked from the seam
  (D-067's literal parenthetical) — semantically weaker (drops duplicates whose
  sibling resolution fails) and still splits the check across packages; moving
  the §16.4 "Where" cell to `plan` instead of moving the code (records the drift
  as intended rather than fixing it, and orphans the checks from the seam that
  owns every other spec-validate code); leaving the drift (a standing
  code-vs-spec disagreement, P-8).
- **Affects:** Spec (`SpecResolver` two new private checks; `SpecComposer`
  doc-comment), Core (`ConversionPlanner` two checks + `ValidateValueLabels`
  removed), tests (planner tests relocated to `SpecResolverTests`). Diagnostics
  unchanged. Realizes the D-067 seam assignment for these two codes; pairs with
  D-076. Spec §10.2 / §10.8 / §16.4 (no text change — the cells already read
  `spec validate`).

### D-081 — Value-bin ordinal path (Slice H): identity + explicit order

- **Status:** accepted (M2 Slice H; realizes the D-047-deferred value-bin path
  and closes the F1 silent-output hole; refines D-060)
- **Date:** 2026-07-05
- **Decision:** the value-bin ordinal path (§12.3) is implemented, so an
  `ordinal` scale over a **value-bin** discretizer thresholds on the explicit
  `scale.order` instead of the (unread) cut geometry. Scope and semantics:
  - **Kind gate.** In M2 the only value-bin discretizer is `identity`
    (`free_per_value` → M4, read-rejected by D-070), so this path is
    `identity` + a **string** `order` only (D-061 string-fixing). `order` is
    **always required** in M2; §12.3's "optional for numeric" branch belongs to
    the numeric value-bin (`free_per_value`) case and activates at M4.
  - **Permutation rule.** `order` must be a **full permutation** of the
    `declared_domain`: every domain value gets a threshold. At **plan** an
    omitted `order` **or** a domain value missing from `order` is
    `OrdinalOrderMissing`; an `order` entry outside the domain is
    `OrdinalOrderHasUnknownValue` (one per stray entry). These two codes were
    already §16.4-registered at `plan`; Slice H adds their enum members and emit
    sites. The membership check is suppressed on an absent domain — D-071's
    `ObservedDomainCalibrationNotImplementedV1` owns that (one condition → one
    code, P-14). `order` lists **raw domain values, never display labels**.
  - **Order list shape** (distinct, non-empty entries) is validated at the
    **resolve seam** by broadening `OrderDomainInvalid` (its existing §16.4
    `spec validate` cell) from `ordered_cuts.order` to *any* authored order over
    a non-cut discretizer; include-gated (parked orders never block, D-049). A
    cut discretizer's order stays `OrdinalOrderNotAllowedWithCuts` (D-060) — the
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
    precedent — an empty column is legal, §10.1). *(Open question 1 →
    recommended option.)* An empty column that never crosses is a future
    **emit-phase** `AttributeHasNoCrosses` concern (D-058); Slice H makes no
    claim it surfaces now — that code has no emit site yet.
  - **Naming / identity.** Names render through the existing `RenderName`
    (`{attr}-{op}{display}`, `value_labels` applied since `identity` consults
    them); the canonical identity's `BinKey` is the **raw** order value, so it
    is style-independent (the roadmap gate `BinKey == ValueLabel`). The
    fingerprint **encoder is untouched**: `AppendScale` already encoded ordinal
    `boundary`/`direction`/`drop_top`/`order`, and each threshold column encodes
    as `{"bin":"<raw value>","op":"<op>","scale":"ordinal"}`. Two order
    permutations of one domain change the column identities/sequence, so the
    schema fingerprint moves — byte- and fingerprint-neutral on every existing
    golden and pinned baseline (no cut spec takes this path).
- **Why:** the path was committed M2 scope in three places (roadmap M2 body, the
  Tier-2 net-scope line, spec §12.3 "implemented at M2") but never landed:
  `OrdinalScale.BuildShapes` read only the cut geometry, and an `identity` +
  `ordinal` spec resolved, planned, and emitted output that **silently ignored**
  the authored `order`/`boundary` — the exact wrong-output hazard D-057/D-070/
  D-071 close elsewhere, and a path that could mint a wrong schema fingerprint.
  A `BinScheme.CutBins` discriminator selects the path with one bit, keeping the
  cut geometry (and its `Boundary`-is-unread rule, D-060) exactly as it was.
- **Rejected:** *drop_top drops the statically-empty end under strict* (open
  question 1 alt) — that end is not tautological, and dropping it would make
  `drop_top` mean two different things by boundary; keeping it matches the
  closed-ends cut precedent and an empty column is already legal (§10.1). *A new
  §16.4 code for "order omits a domain value"* (open question 2 alt) — it is the
  same defect as an omitted `order` (a bin with no threshold), so a message
  variant of `OrdinalOrderMissing` keeps the registry closed. *Plan-phase
  duplicate/empty order validation* (open question 3 alt) — that would split
  `OrderDomainInvalid`'s phase ownership across packages; the seam already owns
  the identical `ordered_cuts.order` shape check. *Building the numeric value-bin
  ordinal now* — that is `free_per_value`, an M4 discretizer (D-070); M2 is
  string-only.
- **Affects:** Core (`OrdinalScale.BuildValueThresholds`, `BinScheme.CutBins`
  and its three construction sites), Core (`ConversionPlanner` order-permutation
  guard), Spec (`SpecResolver` broadened `OrderDomainInvalid`), Diagnostics
  (`OrdinalOrderMissing`, `OrdinalOrderHasUnknownValue` enum members added). Spec
  §12.3 (surgical: `drop_top` value-bin + strict-no-op wording, the
  full-permutation clause). Byte- and fingerprint-neutral. Refines D-047/D-060;
  pairs with D-070 (kind scope) and D-071 (absent-domain precedence).

---

## M3 (triple source + wide column object keys)

These land the M3 triple-source audit (a review pass over the finalized triple and
wide-column-key surface, analogous to the Tier-1/Tier-2 M2 passes). They are the
**spec/decisions** landing; the triple reader, column-key execution, `DiagnosticCode`
enum members, and golden activation are the M3 *implementation* that follows.

### D-082 — M3 triple source contract: reader, orderings, object identity, absent-vs-missing

- **Status:** accepted (M3; realizes D-072; the M3 triple-source audit)
- **Date:** 2026-07-07
- **Decision:** M3 implements the triple source; the audit-settled contract:
  - **Reader + orderings.** A triple `IRecordSource` streams subject/predicate/value
    rows. `ordering = "subject_grouped"` is the single-pass fast path — rows for a
    subject MUST be contiguous; a recurrence after an intervening subject is
    `TripleSubjectNotContiguous` (Error, stop — D-031). `ordering = "unordered"`
    groups non-contiguous (interleaved) subjects via external sort-merge/spool,
    never a full matrix (P-16), and emits objects in **first-appearance order of
    each cleaned subject** — the same output order as `subject_grouped`, differing
    only in the contiguity requirement. Both are required for M3 completion.
  - **Object identity = the resolved subject**, always (§5.4 default `column` =
    subject). An authored `[binding.object_key]` under `shape = "triple"` is
    rejected (`ObjectKeyModeInvalidForShape`, D-085) — identity is not repointable.
    Every valid subject row establishes/keeps an object, **including rows whose
    predicate matches no attribute** (an object with no crosses is legal, §10.1).
    Contiguity and first-appearance order are judged over **every valid subject
    row**, ignored-predicate and no-cross rows included.
  - **Absent vs missing (audit NF-2).** An **absent** predicate (no triple for that
    subject+predicate) is **no observation** — no cross, and never crosses
    `-missing`. `missing_policy` fires **only** when a matching-predicate row
    *exists* and its value is empty / `missing_token` / absent-because-short. An
    unknown or empty predicate keeps the subject and sets no crosses. Predicate
    matching is exact ordinal (P-12).
  - **Cleanup scope.** The §5.1 quote-aware trim applies to the subject (→ object
    name) and predicate (→ selector), not only value-side matching; the cleaned
    subject is both the object name and the grouping/sort key.
  - **`has_header`** default is **shape-specific**: wide `true`, triple `false`
    (triple data is typically headerless — the v2 goldens are; a `true` default
    would silently consume the first triple as a header). Once resolved, behaviour
    is identical, no heuristics. `columns` is **optional** (omit → `subject = 0,
    predicate = 1, value = 2`) and uses **one addressing mode** — omitted, all-index,
    or all-name; no mixing; all-name requires `has_header = true`.
  - **Structural-error severity.** Missing/invalid subject, a row too short for the
    subject role, non-distinct roles, and invalid `columns` shape are **Error**
    (halt this conversion — the §16.2 usable-next-call reading), matching
    `TripleSubjectNotContiguous` / `DuplicateObjectKey`; value-level problems
    aggregate (§16.4). Codes are D-085.
  - **Determinism / fingerprints.** Predicate arrival order never affects
    formal-attribute order or `.dat` item order (§17 rules 1–3, 8). Output
    fingerprints encode the **resolved** role→column-index map (so name-bound ≡
    index-bound roles hash identically), and `binding.encoding` becomes a real Core
    input (was the constant `"utf-8"`, D-077; UTF-8 specs keep their hash). The
    triple `ordering` field is **not** a fingerprint input — `subject_grouped` and
    `unordered` emit identical first-appearance bytes, so it is an acceptance/
    streaming property, not a byte one. The subject key is a resolved `column` key
    whose `duplicate_object_policy` is inapplicable (§6.1); the resolver pins it to
    the inert `fail` so `defaults.duplicate_object_policy` never reaches the triple
    key or perturbs its output fingerprint — otherwise two specs with identical
    output bytes would hash differently (§14/D-077).
  - Conversion no longer rejects triple: `TripleSourceNotImplementedV1` retires.
- **Why:** D-072 carried the basic triple binding but rejected conversion; M3 is the
  triple milestone. The audit surfaced three silent-wrong-output hazards left
  implicit by the pre-M3 spec: triple breaks wide's "every object has a cell per
  column" invariant (absent ≠ missing), structural errors had no severity/home, and
  determinism needs the resolved-index fingerprint plus ordinal collation (D-084).
- **Rejected:** absent predicate = missing (would cross `-missing` for every
  unobserved predicate — schema-inflating and surprising); triple `has_header`
  default `true` (silently eats row 1 of headerless data); mixed index/name
  `columns` (ambiguous addressing, §5.3); per-row structural-error aggregation on the
  streaming path (cannot aggregate without buffering — abort is honest).
- **Affects:** Sources (new triple `IRecordSource`), Core (`Binding` triple +
  encoding carrier, `SpecResolver`, planner guard removal), Conversion
  (grouping/sort-merge + emit), Spec (writer already round-trips), Diagnostics
  (D-085); spec §5.1 / §5.3 / §5.3.1 / §5.4 / §10.2 / §10.5 / §14 / §17. Realizes
  D-072; pairs with D-083 / D-084 / D-085.
- **Slice F implementation — the bounded shared grouping backend (durable invariants):**
  the slow path (triple `unordered`, wide `dedupe`) runs on one internal backend —
  `FirstAppearanceGrouping` orchestrating a rank map (the **single** cleaned-key
  structure), spill runs, and a bounded-fan-in multi-stage `RunMerger`. Invariants:
  - **Byte-neutral spilling.** The row codec round-trips values exactly (strings as
    length + raw UTF-16 code units, lone surrogates preserved), so spilling never
    changes output bytes (P-7); a zero-spill enumeration stays fully in memory.
  - **Lazy, owned, confidential workspace.** The spool directory is created only on
    the first required spill (zero-spill needs no disk, even under an unusable temp
    root); it is a uniquely-named, create-new per-enumeration directory, and only that
    recorded path is ever deleted. Because it holds raw source data it is created
    owner-restricted (Unix `700`; Windows an explicit owner-only, inheritance-disabled
    DACL; run handles `FileShare.None` + non-inheritable) — if the restrictive ACL
    cannot be established, workspace creation **fails** rather than proceeding
    unprotected.
  - **Bounded resources — two accounting rules.** The intake buffer spills on a
    **deterministic modeled resident accounting** = the `List` buffer object + its backing
    array (`capacity × Unsafe.SizeOf<RankedRow<TRow>>()` — the list's *real* capacity × the
    exact element stride) + each row's **retained referenced objects** (its record + the
    record's two reference fields, its name, its field array + one reference per field, and each
    non-null string with header/length/terminator/8-byte alignment). This is a **two-tier**
    guarantee: the spill decision is always the modeled value, and on **.NET 10 CoreCLR x64**
    the padded layout constants make **actual retained live-object bytes ≤ modeled** — a
    numerical bound over the **stable retained graph at the post-`Add` checkpoint** (the `List`
    resize copy transient, GC commitment, fragmentation, and allocator bookkeeping are
    **excluded**). On other architectures/runtimes the model still bounds buffering
    operationally, but the byte guarantee is not asserted until that layout is validated. The
    layout constants are **correctness** constants (their code XML docs are authoritative):
    **M8 tunes the buffer budget and fan-in, never these** — changing one requires re-validating
    the object layout. Separately, the initial run's on-disk size is the **exact serialized
    accounting** — the sum of each row's serialized size plus **all run framing**. Intermediate
    runs may exceed the budget; peak temp disk is pinned to **`3T`** (`T` = the intake-final
    initial spill payload, captured once) by a pre-batch **degraded-cleanup escalation** that
    halts before any merge batch whose projected live bytes would exceed `3T` — failed
    consumed-run deletions are **retried at each batch boundary** (each failed attempt
    aggregates) so a transient failure does not force the escalation.
  - **Validated framing (narrowed).** Run reads validate field counts and string
    lengths against the record buffer with checked/saturating `long` arithmetic, so a
    safely-identifiable truncated/corrupt record becomes an owned storage-failure
    outcome — never a malformed row or silent corruption. It does **not** claim
    OOM-immunity for payload-consistent huge lengths (documented residual).
  - **Two-channel storage failures, one ordered ledger.** Stable logical identity is
    `(Operation, Kind)`, both **application-defined** (mapped from runtime exceptions; identity
    never includes the random path or severity). Both channels share **one per-enumeration
    insertion-ordered ledger**: **in-path** failures are recorded as **Error** by the site that
    detects them, at their first-logical-occurrence position — before the cleanup their
    unwinding triggers — and additionally throw an internal `GroupingStorageException` from
    advancement only (never disposal) so the emitter halts (it catches only to stop, not to
    record); **cleanup-class** failures (consumed-run deletes, workspace teardown, and **reader
    close / a failed reader construction after a successful open** — `CleanupClose`) are recorded
    as **Warning** as they occur while enumeration continues, so disposal/open-cleanup never
    throws a storage exception and never masks the primary result, cancellation, or in-path
    failure. The emitter renders the ledger after the stream in **first-occurrence order**, one
    aggregate per identity at worst severity (count + ≤3 first-occurrence samples); the replay
    session re-aggregates across passes, preserving each pass's positions.
  - **Replay session + `.cxt` invariant.** The public `EmitReplaySession`
    (`EmitReplay.Begin`) brackets one conversion attempt: data diagnostics collect
    once (first pass), storage failures are intercepted on every pass and the final
    aggregates append at **disposal** (in first-logical-occurrence order — the D-059
    rule), so a pass-2-only storage failure is never lost. The `.cxt` writer enforces
    the **object-name sequence invariant** (§18.1): a replay that diverges in name
    count/order fails the write.
  - **Plan carries `SourceExecution`.** The plan gains a `SourceExecution`
    (`WideExecution` / `TripleExecution(ordering)`) — a pure value, **not** a
    fingerprint input; `EmitTripleAsync` owns ordering selection from it (the external
    `TripleRowSources` selector retired), and the emit entrypoints reject a mismatched
    variant.
  - **Runtime knobs internal until M7.** `GroupingOptions` (budget, fan-in, temp root)
    is internal — never a spec/TOML/fingerprint input (the storage strategy never
    changes bytes).
  - **Rejected:** an external sort library (ExternalSort — Parquet baggage; SQLite —
    disproportionate); a record-and-rethrow failure model (P-14 — storage failures
    cross the seam as diagnostics, not exceptions); warnings-as-exceptions (a faulted
    async iterator cannot resume); deriving failure identity from the runtime exception
    type (it must neither split one condition nor merge unrelated ones).

### D-083 — Wide `column` object-key execution + `duplicate_object_policy`

- **Status:** accepted (M3; realizes D-064 / D-034)
- **Date:** 2026-07-07
- **Decision:** wide `object_key.mode = "column"` executes at M3 with
  `duplicate_object_policy` over the **cleaned** key value:
  - **`fail`** (default): a repeated key → `DuplicateObjectKey` (Error), stop.
  - **`keep`**: each row is its own object. Name assignment is a **conversion**
    concern (the object-key resolver, **not** the writer — P-15): names are assigned
    in object emission order (§17 rule 4) and are **unique by construction** — the
    first occurrence of a cleaned key takes the key itself, a later occurrence takes
    `<key>#<record-index>` (0-based). If any candidate is already assigned (a literal
    data key or an earlier generated name), the resolver **escalates** by appending
    `#1`, `#2`, … (ascending integers from 1) and taking the first unused; all
    comparisons are ordinal (P-12). The assigned-name set is bounded object-name
    metadata (P-16); `.cxt` serializes these names and `.dat` ignores them, so the
    guarantee is observable only in `.cxt`. A repeated cleaned key aggregates to one
    `DuplicateObjectKey` (Warning); a candidate-name collision that forces the `#N`
    escalation aggregates to a separate `ObjectKeyNameDisambiguated` (Warning) — one
    condition, one code (D-085). Both flush once after the object stream and are
    suppressed on a structural halt (an invalid key).
  - **`dedupe`**: rows sharing a key collapse to one object, later crosses union onto
    the first; `DuplicateObjectKey` (Info). Non-contiguous keys cannot stream in one
    pass without holding all crosses (P-16), so `dedupe` is built on the **shared
    external grouping/sort-merge/spool path** — the same infrastructure as triple
    `unordered` (D-082).
  - **Output order (audit NF-7).** Wide `column` object order = **order of first
    occurrence of each cleaned key** (§17 rule 4). This generalizes row order:
    `row_index` / `keep` / `fail` / all-unique reduce to row order; `dedupe` merges
    onto the first and adds no new position. Triple `unordered` (and
    `subject_grouped`) emit the same first-appearance order of each cleaned subject,
    so wide `column` and triple share **one** first-occurrence principle; the shared
    grouping infrastructure must not sort object output. The two triple orderings
    differ only in the contiguity requirement — `unordered` accepts interleaved input,
    `subject_grouped` requires contiguity — not in output order.
  - The key column is **not implicit** as an attribute but **may** be explicitly
    bound by an `[[attribute]]` source (§5.4 "excluded from conversion" → "not
    implicit"; D-033 source-repeat).
  - **Slice split.** Wide `fail`/`keep` execute at **Slice E**; `dedupe` (the shared
    spool path) at **Slice F**. `ObjectKeyColumnNotImplementedV1` narrows to wide
    `dedupe` at Slice E and retires fully at Slice F. The source stays key-agnostic
    (row-index names); the emitter derives column-key names + policy from the plan.
  - **Key-index phase (interim).** The resolved key index is range-checked at **plan**
    (`ObjectKeyBindingInvalid`, upper bound) because the conversion pipeline resolves
    schema-less, so the schema is first available at plan; negatives are already
    rejected at resolve. This is a binding error, distinct from an absent data cell at
    emit (`ObjectKeyValueInvalid`). A future schema-aware resolve/validate pass may move
    the check back to spec-validate without changing the code.
- **Why:** D-064 deferred wide column-key execution to M3 alongside the triple
  subject-derived key — one column-object-key machine. The audit found `dedupe` is
  the sole policy incompatible with single-pass streaming; its first-occurrence
  output order matches triple `unordered`'s first-appearance order on the shared
  grouping infra (neither sorts object output). `keep` uniqueness was tightened from a
  "documented residual" to a guarantee once it was clear the **converter** can hold
  the assigned-name set as bounded metadata (P-16), so uniqueness is affordable and
  stays upstream of the dumb writer (P-15) rather than being decided in the `.cxt`
  writer.
- **Rejected:** silent `row_index` fallback (D-064's latent-wrong-output hole);
  duplicate output object names under `keep` as a documented residual (rejected in
  audit Round 2 — names are already held, uniqueness is affordable); sorting
  `dedupe` output (contradicts §6.1 "union onto the first");
  buffering all crosses for `dedupe` (violates P-16).
- **Affects:** Core (`ColumnObjectKey` execution, planner guard narrowed to `dedupe`
  + key-index range-check), Sources (row-index-agnostic `WideCsvSource`, ragged
  tolerance), Conversion (`keep` name-uniqueness via `ColumnKeyNamer`; grouping/
  sort-merge/spool shared with D-082 for `dedupe`), Diagnostics (`DuplicateObjectKey`,
  `ObjectKeyNameDisambiguated`, D-085); spec §5.4 / §6.1 / §16.4 / §17 rule 4. Realizes
  D-064 / D-034; pairs with D-082.
- **Slice F implementation — Info aggregation is normative:** `dedupe` emits on the
  shared grouping backend (D-082). It reports **one aggregated** `DuplicateObjectKey`
  (Info) — a count of the merged (duplicate) rows with a bounded **source-order** sample
  (the duplicate keys in the order they recurred, detected via the grouper's intake hook
  so no second seen-set is needed) — and is **silent** when every key is unique; flushed
  only on normal completion (suppressed on a structural halt). The object name is the
  cleaned key; the wide key column may double as an `[[attribute]]` source (D-033), and
  its crosses union with the other attributes' onto the one object.

### D-084 — Ordinal string comparison is the project-wide rule

- **Status:** accepted (M3 audit; adds principle P-12)
- **Date:** 2026-07-07
- **Decision:** all string identity, equality, matching, deduplication, grouping,
  source binding, and deterministic string **ordering** use ordinal comparison
  (`StringComparer.Ordinal`, a UTF-16 code-unit compare), never culture-aware
  collation; `binding.locale` (§5.1) governs numeric/date **parsing** only, never
  string collation. Recorded as new principle **P-12** ("Strings compare and sort
  ordinally…"), the string-side companion to P-11 (numeric/locale parsing).
- **Why:** the audit (F-054) found "invariant culture" for the unordered subject
  grouping a determinism hazard — `InvariantCulture` string collation is ICU/NLS-version
  dependent and can mis-group or reorder across machines/runtimes, drifting output bytes
  on a golden/fingerprint path (P-7); ordinal is byte-stable. (Slice F confirms this: the
  grouping backend keys strictly by `StringComparer.Ordinal` and emits in first-appearance
  order — there is no culture-sensitive sort.) The code already uses
  `StringComparer.Ordinal` for name/identity dedup, so this codifies practice across
  every string-keyed surface (predicate matching, key dedup, name uniqueness). A
  dedicated principle (not a P-11 extension) has its own check moment — comparing or
  sorting strings — distinct from P-11's numeric-parsing moment.
- **Rejected:** `InvariantCulture` collation (ICU-version-dependent — the hazard);
  extending P-11 instead of a sibling principle (different check moment; leaves a
  correctness rule structurally implicit — Codex's Option B); "byte-value" wording
  (`Ordinal` compares UTF-16 code units, not bytes — precision matters).
- **Affects:** `principles.md` (new **P-12**; old P-12…P-21 renumbered P-13…P-22,
  cross-references updated in `decisions.md` / `AGENTS.md`);
  Core / Conversion / Sources (ordinal collation on all string keys) at
  implementation. Byte-neutral: `unordered` emits first-appearance order (no object
  string sort), so ordinal collation governs matching/dedup/grouping only, not
  object order.

### D-085 — M3 diagnostic taxonomy: structural triple/column-key codes

- **Status:** accepted (M3; refines D-067; the audit's NF-1)
- **Date:** 2026-07-07
- **Decision:** M3 gives triple/column-key **structural** conditions real §16.4
  codes rather than misusing value-level codes or `DuplicateObjectKey`:
  - **New codes.** `ObjectKeyValueInvalid` (Error, emit) — a data-derived object name
    (triple subject or wide column key) is empty, whitespace-only, contains
    newline/control characters, or its mapped column is absent from the row (an
    unusable object name; a newline would corrupt the line-structured `.cxt`,
    §18.1). `TripleColumnsNotDistinct` (Error, spec validate) — the **resolved** case
    where two logical roles (subject/predicate/value) point to the same physical
    column. `ObjectKeyNameDisambiguated` (Warning, aggregated, emit) — under wide
    `keep`, an object's assigned name needed `#N` escalation because its candidate
    collided with an already-assigned name (a literal key vs a generated name);
    distinct from `DuplicateObjectKey` (repeated cleaned keys) — one condition, one code.
    `GroupingStorageFailed` (Error in-path/escalated, or Warning cleanup-only; emit) — a
    spool-storage failure on the shared grouping backend (triple `unordered` / wide
    `dedupe`), carrying the two-channel model, stable `(Operation, Kind)` identity,
    bounded aggregation, and severity promotion of D-082 (Slice F).
  - **Extend existing (no new code).** `SourceBindingInvalid` (Error, spec validate)
    additionally owns invalid triple `columns` **shape/addressing** — missing/partial
    role table, mixed index/name addressing, all-name without `has_header = true`,
    header-name no-match, duplicate matching header — **plus** the source
    `kind`↔`shape` mismatch (`column`↔wide, `predicate`↔triple). This parallels its
    existing wide meaning (name binding needs `has_header`; a reference must resolve
    to **exactly one** column — both a no-matching-header and a duplicate matching
    header are invalid for wide sources and column object keys too, §10.2);
    its message/Context disambiguates the attribute `source` from the `columns`
    table. `ObjectKeyModeInvalidForShape` additionally owns any authored
    `[binding.object_key]` under triple.
  - **Severity.** Structural row/source errors are **Error** (halt this conversion,
    file usable next call — §16.2), matching `TripleSubjectNotContiguous` /
    `DuplicateObjectKey`; never `Fatal` (reserved for a corrupt/unrecoverable spec).
  - **Interim phase (wide column-key index).** An out-of-range wide `column` key index
    is a binding error (`ObjectKeyBindingInvalid`) but is checked at **plan** — the
    conversion pipeline resolves schema-less, so the schema is first available there
    (D-083). It stays distinct from a per-row absent key cell (`ObjectKeyValueInvalid`,
    emit); a future schema-aware resolve pass may move it back to spec-validate, same code.
  - **Enum timing.** The §16.4 **table** is the documentation home; each
    `DiagnosticCode` **enum member** lands with its **emit site** at the implementing
    slice ("grows per slice", P-3) — the new codes above
    (`ObjectKeyValueInvalid`, `TripleColumnsNotDistinct`, `ObjectKeyNameDisambiguated`)
    and the already-spec'd-but-unimplemented `DuplicateObjectKey`,
    `TripleSubjectNotContiguous`, `AttributeHasNoCrosses`, `ObjectHasNoCrosses`,
    `NoObjectsEmitted`, `NoFormalAttributes`.
  - **Retirements.** `TripleSourceNotImplementedV1` (D-082) is removed with its guard
    when the triple source lands (Slice C). `ObjectKeyColumnNotImplementedV1` (D-083)
    narrowed to wide `dedupe` when `fail`/`keep` landed (Slice E) and **retired fully
    when `dedupe` landed (Slice F)** — the enum member and its planner guard are gone, so
    wide `column` object keys now execute for every `duplicate_object_policy`.
- **Why:** the audit (F-081 / NF-1) found the triple/column-key structural-error
  class had no diagnostic home, and that reusing value-level (`UnknownValueObserved`)
  or duplicate-key codes would break one-condition → one-owning-code (P-14).
  Assigning owners/severities now — and consolidating shape/addressing under the
  existing `SourceBindingInvalid` rather than proliferating codes — keeps §16.4's
  phase-ownership honest (D-067).
- **Rejected:** reusing `UnknownValueObserved` / `DuplicateObjectKey` for structural
  conditions (mis-phased, mis-severity, P-14); a dedicated code per shape/addressing
  variant (`SourceBindingInvalid` already means "binding shape invalid" —
  proliferation for no gain); `Fatal` for row-level structural errors (the file is
  usable next call); adding enum members in this docs landing (would strand members
  no emit site raises, P-3).
- **Affects:** Diagnostics (`ObjectKeyValueInvalid`, `TripleColumnsNotDistinct`, and
  the six deferred codes, at their emit sites), Core / Conversion / Sources (emit
  sites), Spec (`SpecResolver` shape checks); spec §16.4. Refines D-067; pairs with
  D-082 / D-083.

---

### D-086 — Shape-aware `.bed` migration sources

- **Status:** accepted (M3, Slice G; refines D-079)
- **Date:** 2026-07-11
- **Decision:** the one-way v2 migrator (`BedMigrator`) authors an attribute
  `source` per the binding **shape**: a wide (or shape-absent) binding keeps the
  positional `ColumnSourceSection(Index: i)` byte-for-byte, while a **triple**
  binding authors `PredicateSourceSection(Name: <v2 attribute name>)` — the
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
  binding already carries the shape — thread it privately); authoring the
  predicate `value_type` (would create a place for it to disagree with the
  discretizer default — the same reasoning as the wide positional source, D-061).
- **Affects:** Spec (`BedMigrator` private chain + type XML doc). No public
  signature, diagnostic, fingerprint, or output-byte change. Refines D-079;
  enables the Slice G triple goldens.

---

### D-087 — Symmetrical final-newline controls and v2 triple `.dat` compatibility

- **Status:** accepted (M3, Slice G)
- **Date:** 2026-07-11
- **Decision:** three settled parts.
  - **Native control.** `[output.dat] trailing_newline` (default `true`) is the
    symmetrical twin of `[output.cxt] trailing_newline` — an additive
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
    `trailing_newline` unconditionally — the divergence is exactly what preserves
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

### D-088 — Shared auto-calibration invariants + equal-frequency contract

- **Status:** accepted (pre-M4 Tier 1 audit; docs-only)
- **Date:** 2026-07-12
- **Decision:** four settled invariants ahead of the M4 calibration work.
  - **Auto/frozen byte-equivalence (both auto discretizers).** For `equal_width`
    and `equal_frequency`, converting on the fly and converting from the
    `calibrate`-frozen spec MUST produce **byte-identical** `.cxt`/`.dat` on the
    calibration dataset; only **audit metadata** — the run manifest, recorded
    command line, spec-file hashes, and calibration diagnostics — is exempt. This
    restates D-028 at the output-byte level: freezing changes *when*
    cuts resolve, never *which* cuts.
  - **General calibration population.** Each record contributes its
    non-missing, usable value; a numeric value contributes only when it parses to
    a finite number under `binding.locale`; for triple input each distinct cleaned
    `(subject, predicate, value)` observation contributes once (§5.3.1); wide rows
    are independent observations.
  - **`tie_policy` is calibration-time.** It assigns an **entire
    tied-value group** to one side of a candidate boundary and never splits a
    group; Emit does no tie handling of its own — it applies ordinary §11.2
    half-open `[lo, hi)` geometry to the resolved cuts.
  - **Formula-stage distinct-gap obligation + phase-split cut validity.** When
    distinct ≥ `bins`, quantile/tie placement selects `bins - 1`
    distinct, strictly-ascending cut gaps and never drops a bin because several
    target boundaries fall in one tied group; the exact quantile formula stays
    deferred, bounded by this obligation and the auto/frozen equivalence.
    Spec-determined cuts validate at spec-validate; **data-calibrated** cuts that
    are non-finite or not strictly ascending are `CalibrationCutsInvalid` (Error,
    calibrate).
- **Why:** the tie/emit boundary, the calibration population, and the
  frozen-equivalence guarantee were implied but never normative; the M4
  implementation needs them pinned before cuts are computed, and golden pins can
  only be set once the byte-equivalence is a stated contract.
- **Rejected:** emit-time tie handling (splits tied groups, contradicts half-open
  geometry); dropping bins on tied-boundary collisions (silently fewer bins);
  freezing the exact quantile-index formula now (a tuning choice, not a
  determinism guarantee, §11.5).
- **Affects:** spec §5.3.1 / §7 / §11.4 / §11.5; new diagnostic
  `CalibrationCutsInvalid`. Restates D-028. Docs-only landing (no enum/code change
  here; codes land with their M4 emit sites, §16.4).

---

### D-089 — Equal-width range-mode contract

- **Status:** accepted (pre-M4 Tier 1 audit; docs-only)
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
    **`equal_frequency`-only** — `equal_width` never applies it, because
    equal-width bins are placed by span, not by count.
- **Why:** the manual-vs-data-range split governs which phase runs and whether a
  spec can carry stored fingerprints; the distinct-value guard was mistakenly
  assumed to cover both auto discretizers.
- **Rejected:** applying the distinct-value guard to `equal_width` (span-based,
  tolerates sparse data); treating a manual range as data-dependent (it is fully
  spec-determined and fully-frozen-eligible).
- **Affects:** spec §3 / §7 / §11.4 / §14; new diagnostics `EqualWidthRangeInvalid`,
  `EqualWidthCutsCollapsed`. Docs-only landing.

---

### D-090 — value_groups execution contract

- **Status:** accepted (pre-M4 Tier 1 audit; docs-only)
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
  malformed fields, P-14); ordinal over `passthrough` (its data-discovered bins
  cannot be a full authored permutation); routing duplicate labels through
  `SpecFieldInvalid` (duplicates own `ValueGroupsLabelDuplicate`).
- **Affects:** spec §11.6 / §12.3 / §16.4 / §17; new diagnostics
  `ValueGroupsLabelDuplicate`, `OrdinalNotAllowedWithValueGroupsPassthrough`.
  Docs-only landing.

---

### D-091 — restrict_to execution contract

- **Status:** accepted (pre-M4 Tier 1 audit; docs-only; refines D-063/D-076/D-079).
  Merged-`dedupe` restriction and type-directed migration **clarified in place** by
  the pre-M4 Tier 2 audit (2026-07-13) — no superseding entry, the D-091 contract is
  still unimplemented (the correcting-fresh-decision convention).
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
    conflict resolution, not `dedupe`). Wide `row_index`/`fail`/`keep` — where an
    object carries one observation per source — is the one-observation special case.
  - **Exact numeric entries.** `{ value = n }` matches by
    parsed numeric identity (`30`, `30.0`, `3e1`), with finite `n`, and coexists
    with ranges; `{}` is the full usable-numeric range. Bounded ranges require
    finite `from < to`; equal, reversed, or non-finite provided bounds, and a
    non-finite exact value, are `RestrictToRangeInvalid` (Error, spec validate). A
    bare string on a numeric source is `RestrictToNumericEntryRequired` — which
    **renames** `RestrictToOnNumericRequiresRange` in live text (the enum rename
    lands with the M4 check site).
  - **Type-directed migration** (refines D-079). Migration is directed by the v2
    attribute **type**, not by whether a token looks numeric: only a parseable
    **finite** restrict token on a type-`o` (numeric) attribute migrates to an exact
    `{ value = n }` entry — parsed under the effective `binding.locale`, then written
    as the numeric exact entry — replacing D-079's keep-as-string carriage now that
    an exact numeric form exists. A numeric-**looking** token on a categorical
    attribute stays a **verbatim string**; and an **unparseable** token on a type-`o`
    attribute also stays a string, then fails later at the resolve seam with
    `RestrictToNumericEntryRequired`. Migration never parses a categorical token or
    silently drops an unparseable numeric one.
  - **Canonical `restrictions` encoding.** `restrict_to` is encoded
    as a `restrictions` array in the **shared** portion of the canonical structure
    (§14) — entering **both** output fingerprints, excluded from
    `schema_fingerprint` — present only when non-empty and, as an explicit
    exception to the planned-order rule, **canonically sorted**. Each restriction
    object is `{"entries":[…],"source":{…}}` reusing the D-077 source encoding
    (`{"predicate":<name>,"value_type":<type>}` or
    `{"column":<index>,"value_type":<type>}`); entries are `{"value":<string>}`,
    `{"value":<number>}`, or `{"from":<number|null>,"to":<number|null>}` (both range
    keys always present). Entries sort by complete canonical JSON (ordinal) with
    canonically-identical entries deduplicated (`30`/`30.0` collapse) and
    overlapping-but-non-identical ranges **not** merged; restriction objects (AND)
    sort likewise with exact duplicates removed; filter-only attributes contribute
    their object here, not through the included-attribute column encoding.
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
  `RestrictToRangeInvalid` in live text; refines D-063/D-076/D-079. Docs-only
  landing (enum rename/addition land with the M4 check sites).

---

### D-092 — Numeric free_per_value rendered labels

- **Status:** accepted (pre-M4 Tier 1 audit; docs-only)
- **Date:** 2026-07-12
- **Decision:** a numeric `free_per_value` bin's **rendered label** — and any
  `{value}` in `formal_attribute_format` — is the parsed numeric value formatted
  with the §14 invariant, shortest round-trippable rule, so `90`, `90.0`, and `9e1`
  share one bin rendered `90`. This carries the bin-identity collapse
  already specified (§11.3) through to the rendered name, reusing the fingerprint
  number formatter (§14).
- **Why:** §11.3 pinned bin *identity* but not the rendered *label*; without this,
  the same numeric bin could render `90.0` on one path and `90` on another,
  breaking `.cxt` determinism and the `cxt_output_fingerprint`.
- **Rejected:** rendering the first observed spelling (data-order-dependent); a
  separate label formatter (reuse the one canonical §14 number formatting).
- **Affects:** spec §10.7 / §11.3. No new diagnostic. Docs-only landing.

---

## Tier 2 M4 implementation-contract audit (pre-M4)

A second pre-M4 review, after the Tier 1 spec audit (D-088…D-092), that audited
the M4 **implementation contract** — the calibrated-state boundary between
Calibrate and Plan, the exact canonical fingerprint encodings for the M4
discretizers, the bounded-memory calibration obligation, numeric
`free_per_value` identity, and the filter-only restriction diagnostics. The
findings were adjudicated with the operator and Codex; these entries land the
accepted outcomes. Like the Tier 1 landing this is **docs-only** — no production
code, tests, enum members, fixtures, or output/fingerprint bytes change; the new
diagnostic enum members land with their M4 validation/check sites (the D-088/D-091
pattern).

### D-093 — Calibrated-state model: Core-owned resolved calibration outcomes; Plan consumes calibrated state

- **Status:** accepted (pre-M4 Tier 2 audit; docs-only; refines D-036/D-088)
- **Date:** 2026-07-13
- **Decision:** the boundary between the Calibrate and Plan phases (§7) is a
  single, resolved **calibrated-state** value, contracted as follows:
  - **Core-owned, immutable, resolved.** The resolved calibration outcome is an
    immutable value owned by `Core` that carries everything a data-reading pass
    discovers — resolved auto cuts, observed domains, `unknown_value_policy =
    "include"` additions, and `value_groups` `unmatched = "passthrough"` bins. It is
    **produced by the Conversion calibrator** and returned through `Diagnosed<…>`
    (P-14, alongside `ObservedDomainUsed` / `CalibrationDataInsufficient` / …), and
    it is what **Plan consumes**. The freeze path (`calibrate`) and later manifest
    serialization read this **same** retained outcome — none re-derives M4 semantics
    from raw data (the retention boundary, D-028/D-036; §15).
  - **One Plan input contract.** A fully declared / spec-determined configuration
    satisfies the **same** calibrated-state contract **without** a data-reading
    calibration pass: it is already calibration-ready and enters the identical Plan
    input. There is one Plan input shape, not an "auto" and a "declared" one.
  - **Plan rejects unresolved state.** Plan must not accept calibration-dependent
    state that has not been resolved. This is a **call-contract / package-boundary**
    obligation, enforced structurally (the D-078 posture — a mis-sequenced internal
    call is a programmer error), **not** a permanent runtime diagnostic; no code is
    minted for the bypass.
  - **Structural auto/frozen equivalence.** Resolved auto cuts reuse the manual-cut
    geometry, label, and identity machinery (`CutBinLabels` / `NumericCutBin`);
    sharing that machinery is what makes the D-088 auto/frozen byte-equivalence
    **structural** rather than a property two code paths must independently maintain.
  - **API shape left open.** Exact type names and member layouts are **not** fixed
    here (per the register); this entry pins the contract and its retention boundary,
    not the surface.
- **Why:** M4 introduces the first calibration that reads data. Without a single
  owned, retained outcome, planning, freezing, and manifest serialization could each
  re-derive cuts/domains — three chances to diverge and break determinism (P-7).
  Making the resolved state Core-owned and immutable, and routing declared specs
  through the same path, collapses those to one contract and makes the D-088
  byte-equivalence fall out of shared machinery.
- **Rejected:** letting Plan re-run calibration or read raw data (re-derivation;
  breaks the retention boundary); a separate Plan input for declared vs auto specs
  (two contracts to keep in sync); a permanent runtime diagnostic for feeding Plan
  unresolved state (a mis-sequenced internal call is programmer error, D-078);
  fixing the concrete types now (premature — the register leaves layout to
  implementation).
- **Affects:** Core (owns the calibrated-state value), Conversion (calibrator
  produces it), Spec/manifest (consume the retained outcome); spec §7 / §15. Refines
  D-036/D-088; realizes D-028's freeze face at the contract level. Docs-only landing
  (types land with the M4 calibrator).

---

### D-094 — M4 canonical fingerprint encodings; effective-configuration hashing rule

- **Status:** accepted (pre-M4 Tier 2 audit; docs-only; appendix to D-069/D-077)
- **Date:** 2026-07-13
- **Decision:** pin the canonical JSON encoding of every M4 discretizer in the
  `shared.attributes[].discretizer` sub-object (the D-077 shared per-attribute
  encoding), and the rule for what an M4 fingerprint hashes. All the D-069/D-077
  conventions carry over unchanged — UTF-8 no BOM, compact JSON, **object keys
  sorted ordinal ascending**, `kind` as a key, TOML enum spellings, and the §14
  invariant shortest round-trippable number formatter (so `1.0` encodes `1`,
  `90.0`/`9e1` encode `90`). The per-kind shapes:
  - **`free_per_value`** — no config beyond the kind (its numeric-vs-string identity
    rides on `source.value_type`, already in `source`; its bins/domain are effective,
    below):

    ```json
    {"kind":"free_per_value"}
    ```
  - **`equal_width`** — authored `bins`, `range`, `precision`, and — **only** when
    `range = "manual"` — `vmin`/`vmax`. `precision` mirrors its two TOML forms: the
    string `"exact"` or the object `{"round_to":<number>}`. Data-derived range, then
    manual range:

    ```json
    {"bins":4,"kind":"equal_width","precision":"exact","range":"min_max"}
    {"bins":4,"kind":"equal_width","precision":{"round_to":1},"range":"manual","vmax":100,"vmin":0}
    ```
  - **`equal_frequency`** — authored `bins`, `tie_policy`, `cut_placement`
    (resolved defaults spelled: `"left"`, `"right_value"`):

    ```json
    {"bins":4,"cut_placement":"right_value","kind":"equal_frequency","tie_policy":"left"}
    ```
  - **`value_groups`** — `groups` in **declaration order** (order is significant —
    first-match wins, §11.6 — so it is a planned-order array, **not** sorted),
    `unmatched` (spelled `"skip"`/`"other"`/`"passthrough"`), and each group object
    with keys sorted `label`/`pattern`/`values`, where `values` and `pattern` are
    present **only when authored** and the inner `values` array preserves **authored
    order with duplicates retained** (it is authored configuration, not a
    canonicalized set — the §14 arrays-in-planned-order default; only `restrictions`
    sort):

    ```json
    {"groups":[{"label":"School","values":["11th","HS-grad"]},{"label":"ICD-Cardiac","pattern":"^I[0-9]{2}"}],"kind":"value_groups","unmatched":"skip"}
    ```
  - **Effective vs authored (the hashing rule).** Fingerprints hash the
    **effective** planned bins/cuts/columns/order (the resolved `bin` objects in the
    `schema` array, D-069) and the **effective** (calibrated/extended) domains (the
    `Discretizer.ConsumesDeclaredDomain` domain, D-077). A data-calibrated
    discretizer's **resolved cuts are not re-encoded** in its `discretizer`
    sub-object — they already appear as `bin` objects in the `schema` array, so
    duplicating them would be redundant. What the `discretizer` sub-object carries is
    the **authored** kind and its authored configuration (the shapes above). Because
    that sub-object feeds the **output** fingerprints (`shared`) but **not**
    `schema_fingerprint` (which hashes the `schema` columns only), an **auto**
    (`equal_frequency`) spec and its **frozen** form (`manual_cuts` with explicit
    cuts) share the same `schema_fingerprint` and emit **byte-identical** contexts
    (D-088), yet may carry **different** `cxt`/`dat` output fingerprints — a
    one-directional guarantee (same output fingerprint ⇒ same bytes; not the
    converse, §14), so this is sound.
  - **Golden-lock before first use.** As D-069 was locked by the Slice-E
    canonical-stability golden before any stored hash shipped, the M4 per-kind
    canonical **bytes and SHA-256 vectors MUST be golden-locked before the first M4
    fingerprint is produced**. The first stored/compared M4 hash fossilizes these
    bytes.
- **Why:** the M4 discretizers had no pinned canonical encoding (they reject at
  read pre-M4, D-070), so the first M4 fingerprint would fossilize whatever the
  encoder happened to emit (P-11, the D-069 rationale). Pinning the shapes, the
  omission rules, and the effective/authored split now makes the M4 encoder
  mechanical and its goldens a genuine lock, and states plainly why auto and frozen
  output fingerprints may differ despite identical bytes.
- **Rejected:** re-encoding resolved auto cuts in the `discretizer` sub-object
  (redundant with the `schema` bins; invites a two-source-of-truth drift); sorting
  the `groups` array (declaration order is semantically significant, §11.6);
  always-present `vmin`/`vmax` (they are not authored under a data-derived range);
  a bespoke number format for cuts (reuse the one §14 formatter, D-069); deferring
  the shapes to the M4 encoder unreviewed (the first stored hash fossilizes them).
- **Affects:** Core (M4 fingerprint encoder — the `AppendDiscretizer` cases for the
  four kinds), Spec; spec §14. Appends D-069/D-077. Needs the M4 canonical-byte /
  hash goldens before the first M4 fingerprint. Docs-only landing.

---

### D-095 — Bounded-memory calibration; subject-local triple deduplication; `GroupingStorageFailed` calibrate ownership

- **Status:** accepted (pre-M4 Tier 2 audit; docs-only; refines D-088/D-082)
- **Date:** 2026-07-13
- **Decision:**
  - **Bounded-memory calibration is an M4 obligation.** Equal-frequency and
    percentile-range (`percentile_p1_p99`) calibration MUST be **exact,
    deterministic, and bounded-memory in M4** — not a later performance retrofit.
    When the calibration population exceeds the working-memory budget, the
    implementation spills/sorts/aggregates (or uses an equivalent exact method); the
    spill and non-spill paths MUST produce **byte-identical** cuts and output.
    **Approximate quantiles are prohibited.** M8 may tune budgets and benchmark
    algorithms (the D-082 split), but it never *establishes* boundedness — that
    exists at M4.
  - **Subject-local triple deduplication.** The §5.3.1 "each distinct cleaned
    `(subject, predicate, value)` contributes once" rule is realized with a
    **subject-local** deduplication of `(predicate, value)` within the current
    subject — **never** a dataset-wide seen set. `subject_grouped` deduplicates the
    contiguous run; `unordered` may group/spool first, then apply the **same**
    subject-local rule. Discovery order remains **raw input order** (§17 rule 3) —
    the deduplication changes how repeats are collapsed, not the first-appearance
    order.
  - **Budgets are internal.** Memory budgets, fan-in, spill thresholds, and similar
    controls are **implementation internals** — never TOML surface and never
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
  a calibration that goes approximate under pressure would break determinism (P-7)
  and the D-088 auto/frozen byte-equivalence. Fixing "exact + bounded" as an M4
  property — with spill/non-spill byte-identity — closes both. Subject-local
  deduplication keeps the triple path bounded without a dataset-wide set (which would
  defeat the point). Keeping budgets internal preserves the D-082 rule that tuning cannot move
  bytes.
- **Rejected:** deferring boundedness to M8 (a data structure that only becomes
  bounded after a perf pass is a scale bug shipped early); approximate quantiles
  under memory pressure (breaks determinism and D-088); a dataset-wide deduplication
  set for triples (unbounded — the very thing to avoid); exposing budgets as TOML/fingerprint
  inputs (a byte-neutral knob has no place in either, D-082); a new storage-failure
  code for calibrate (the aggregation/severity semantics are identical to emit's).
- **Affects:** Conversion (calibrator: bounded quantile/percentile pass, spill
  path), Sources (subject-local triple deduplication), Diagnostics (`GroupingStorageFailed`
  phase widens to calibrate/emit — registry cell only); spec §7 / §11.4 / §11.5 /
  §16.4; roadmap M4/M8. Refines D-088/D-082. Docs-only landing.

---

### D-096 — Numeric `free_per_value` identity: locale-parsed, zero-canonicalized; normalized domain/label/order keys

- **Status:** accepted (pre-M4 Tier 2 audit; docs-only; refines D-061/D-081/D-092)
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
    under `binding.locale` — a **stated exception** to the "spec strings are
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
  entry keyed `90.0` would silently miss a `90` bin — breaking determinism and the
  fingerprint. Two new spec-validate codes give an author a precise error instead of
  a silent miss.
- **Rejected:** string-identical domain/label/order keys for numeric
  `free_per_value` (contradicts the bin-identity collapse, §11.3/D-092); preserving
  signed zero (`-0` leaking into a key/label/hash is a determinism hazard for no
  gain); overloading `ValueLabelKeyNotInDomain` for the duplicate case (a distinct
  condition deserves its own code, P-14); a new code for duplicate numeric `order`
  (reuse `OrderDomainInvalid`, which already owns duplicate/invalid domain entries).
- **Affects:** Core (numeric `free_per_value` identity, signed-zero canonicalization),
  Spec (document model round-trip; the two new validate checks), Diagnostics (new
  `DeclaredDomainInvalid`, `ValueLabelKeyDuplicate`); spec §5.1 / §10.3 / §10.8 /
  §11.3 / §12.3 / §16.4 / §17. Refines D-061/D-081/D-092. Docs-only landing (enum
  members land with the M4 validate sites).

---

### D-097 — Filter-only restriction diagnostics: unparseable values report under `unknown_value_policy`

- **Status:** accepted (pre-M4 Tier 2 audit; docs-only; refines D-049/D-076)
- **Date:** 2026-07-13
- **Decision:** a narrow refinement of D-049/D-076 for what a **filter-only**
  attribute (`include = false` + `restrict_to`) reports while evaluating its
  restriction (it interacts with D-091's execution and D-050's present-but-invalid
  rule):
  - On a **filter-only numeric** restriction, a **valid non-match** and a **missing**
    value are **silent** (a non-match is the restriction working, not an anomaly).
    An **unparseable / non-finite** input is a **non-match** (it can match no numeric
    entry) **plus** an aggregated `SourceValueUnparseable` at the severity
    `unknown_value_policy` selects — `skip` silent, `warn` Warning, `fail`
    Error/abort, `include` Warning (an unparseable token cannot join a numeric domain,
    so `include` behaves as `warn`, §10.6).
  - An **included-and-restricted** attribute (not filter-only) keeps its **ordinary**
    malformed/unknown-value diagnostics even when the restriction excludes the object:
    restrictions **filter objects, not observations**. Each **raw observation is
    diagnosed at most once** — the restriction pass and the discretization pass do not
    each report the same unparseable cell twice.
- **Why:** D-050/§10.6 pinned unparseable-value reporting for *discretized*
  attributes; a **filter-only** attribute is discarded before discretization, so
  without this its unparseable inputs would report **nothing** — a silent data-quality
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
  Diagnostics (no new code — existing `SourceValueUnparseable`); spec §10.4 / §10.6.
  Refines D-049/D-076; interacts with D-091. Docs-only landing.

---

## Spec-field defaults

These are recorded in spec §21 ("Decisions log") and not duplicated here:
math bin-label notation; ASCII operators by default (`bin_label_unicode`
opt-in, for ConExp); `unknown_value_policy = "warn"`; `drop_top = false`;
`.dat` 1-based with configurable `base_index`; `.cxt` size advisory at 1 GB;
`.dat` trailing space off by default. See spec §21 D-items 1–11.
