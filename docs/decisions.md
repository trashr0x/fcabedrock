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

This file expands on spec §21 ("Decisions log") with the broader architectural
decisions, not just the spec-field defaults. Where a decision is purely a
spec-field default, it lives in spec §21 and is only cross-referenced here.

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

- **Status:** accepted
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
  not worth a standalone refactor now.
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

- **Status:** accepted (refines D-021 / D-032; sequences §10.4)
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
  `90` / `90.0` / `9e1` collapse to one bin; `"string"` → verbatim spelling). `identity`
  + `"number"` is `SourceValueTypeInvalid`; numeric distinct-value binning uses
  `free_per_value`.
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

## Spec-field defaults

These are recorded in spec §21 ("Decisions log") and not duplicated here:
math bin-label notation; ASCII operators by default (`bin_label_unicode`
opt-in, for ConExp); `unknown_value_policy = "warn"`; `drop_top = false`;
`.dat` 1-based with configurable `base_index`; `.cxt` size advisory at 1 GB;
`.dat` trailing space off by default. See spec §21 D-items 1–11.
