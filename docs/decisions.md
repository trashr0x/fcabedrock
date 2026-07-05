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
- D-063 — `restrict_to`: M2 validates shape, M4 executes; diagnostic ownership
- D-064 — Wide column object keys deferred to M3; object-key diagnostic taxonomy
- D-065 — Calibration/vocabulary over the input universe, before `restrict_to`

### Tier 2 register (pre-M2)

- D-066 — Parsed spec document model vs. resolved Core `BedrockSpec`
- D-067 — Resolve/validate seam and diagnostic phase ownership
- D-068 — `missing_policy = "as_attribute"` scheduled into M2 *(migrator branch realized by D-079)*
- D-069 — Canonical fingerprint encoding, pinned (appendix to D-053)
- D-070 — Minimal M2 discretizer-carrier scope; three-tier kind response
- D-071 — Absent/empty `declared_domain`: M2 interim reject until calibrate
- D-072 — Basic triple TOML carrier in M2; conversion deferred to M3

### M2 implementation (slices)

- D-074 — `as_attribute` missing-column position uniform across scale kinds (appendix to D-068)
- D-075 — Slice C TOML reader/writer contract: strictness, parse codes, canonical form
- D-076 — Slice D seam/plan validation contract details (appends D-067)
- D-077 — Slice E fingerprint encoding/verification contract details (appends D-069)
- D-078 — Slice F composition/carrier contract details (realizes D-027/D-052; refines D-067/D-075)
- D-079 — Slice G `.bed` migrator contract: document-model target, Diagnosed surfaces (realizes D-009/D-049/D-057/D-068)
- D-080 — `AttributeNameDuplicate` / `ValueLabelKeyNotInDomain` re-homed to the resolve seam (realizes D-067; supersedes its "not re-homed" parenthetical)
- D-081 — Value-bin ordinal path (Slice H): identity + explicit order (realizes the D-047-deferred path; refines D-060)

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
  P-12 ("Core is pure: no `System.IO`") once Core has code at M1. The cross-package
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
  span-first row/col API suits the emit hot path (P-17). Wrapping rather than
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
  only, never the schema (§8/§14, D-011/D-035). Canonical identity + late render keeps writers dumb (P-14) and lets
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
- **Why:** keeps numeric and ordered cuts symmetric and DRY (P-16); the shared
  label helper guarantees they never drift on bin labels or `--v2-compat` rendering.
- **Rejected:** modelling `n` with `value_groups` (loses order and threshold
  semantics); a mode-switched single cut discretizer (god type, P-16).
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
  BedrockDiagnostic>` instead of throwing (P-13), so excluded-config recovery can
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
  per-element emit diagnostics must aggregate (P-19) or they flood at 73M rows.
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
  could not be surfaced without re-deriving it in the emitter (P-16); and per-row diagnostics
  do not scale to the v1 data target (~73M records, D-007). One result type plus one
  aggregation pattern keeps future discretizers (`equal_width`/`equal_frequency` at M4,
  `value_groups`) and emit diagnostics consistent (P-5). Also closes the §11.8 gap where an
  `ordered_cuts` value-not-in-order was silently dropped instead of treated as unknown.
- **Rejected:** keeping `string?` and re-deriving unparseable-vs-out-of-range in the emitter
  (duplicates the parse/culture logic out of the discretizer, double-parses the hot path); a
  second `TryDiscretize` out-param method (two ways to spell one decision, P-5); per-row data
  diagnostics (flood at scale, P-19).
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

- **Status:** accepted
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
  `SourceValueTypeInvalid`. One condition → one owning code (P-13).
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
  column keys, so it is the natural home. Distinct conditions get distinct codes (P-13).
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
  (P-10) and Core stays pure (P-12). One model cannot be both. Splitting them
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
  model, aggregating all diagnostics (P-13). Each §16.4 code is **owned by exactly
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
  parse error) so an author can tell which they hit (P-13). Rejecting at read/resolve
  (not silently accepting) avoids the wrong-output hole D-057 closed for `restrict_to`.
- **Rejected:** implementing the M4 discretizers in M2 (scope creep, P-1); building
  full round-trip carriers for the deferred discretizers' parameter shapes in M2
  (speculative surface for kinds M2 cannot execute — P-3/P-6; no v2 type maps to
  them, so migration loses nothing); one code for both not-yet-supported and unknown
  kinds (hides whether the spec is valid, P-13); silently ignoring unimplemented
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
  condition → one owning code, P-13). Cut discretizers ignore `declared_domain`
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
  - **Two-phase aggregation (the P-13 reading):** all TOML syntax errors report
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
  P-13); parsing deferred surface into inert carriers now (pulls Slice F /
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
  D-071 at the seam and planner)
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
    two codes (P-13), not double reporting of one.
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
    diagnostic would be noise (P-13).
- **Why:** the phase/owner assignments were settled (D-060/D-063/D-064/D-067/
  D-071), but the excluded-attribute interactions, the co-fire policy, and the
  blanket's scale coverage were not derivable from any single entry — and each
  reads as a bug (a D-049 violation, a P-13 violation, an over-broad reject)
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
  supersedes the M1 Core-targeting `BedToSpec`)
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
    silent (one condition → one owning code, P-13). The planner still rejects
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
  diagnostic (P-13). It also makes §16.4's "Where" column honest without a spec
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
    code, P-13). `order` lists **raw domain values, never display labels**.
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

## Spec-field defaults

These are recorded in spec §21 ("Decisions log") and not duplicated here:
math bin-label notation; ASCII operators by default (`bin_label_unicode`
opt-in, for ConExp); `unknown_value_policy = "warn"`; `drop_top = false`;
`.dat` 1-based with configurable `base_index`; `.cxt` size advisory at 1 GB;
`.dat` trailing space off by default. See spec §21 D-items 1–11.
