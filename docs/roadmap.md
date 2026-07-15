# Roadmap — FcaBedrock vNext

Milestones, current position, and the deferred-items backlog. Update the
"current position" marker as work progresses. Milestones are incremental
vertical slices, not waterfall phases — each should leave the system working.

## Current position

> **M1 complete — mini-mushroom + mini-adult reproduced byte-for-byte.** The whole
> pipeline runs end-to-end and matches v2 on both families: `.bed` reader
> (`FcaBedrock.Spec`), wide-CSV source over Sep (`FcaBedrock.Sources`), planner
> (`FcaBedrock.Core`), streaming emitter (`FcaBedrock.Conversion`), and
> `.cxt`/`.dat` writers with a `--v2-compat` preset (`FcaBedrock.Export`). Output
> is proven on two axes (D-043: golden v2-compat byte-equality + native spec
> conformance); the M1 decisions are D-041…D-049.
>
> The **M1-adjacent conformance pass** (the M2 "Before implementation"
> reconciliation) has landed (D-050 malformed-numeric, D-056 cut validation,
> D-059 `BinResult` + diagnostic aggregation, §5.1 whitespace, plus conformance
> tests), byte-neutral on the goldens. `dotnet test` is green (178 tests).
>
> **M2 — the TOML spec format + fingerprinting — is complete.** The M2 contract
> was settled across D-050…D-058, the Tier 1 spec audit (D-060…D-065), and the
> Tier 2 register (D-066…D-072); it landed in slices A–G (model split →
> `as_attribute` → reader/writer → validation → fingerprints → `extends`/triple →
> migrator) plus the exit-review Slice H and cleanup below.
>
> **Slices A–G have landed:** the presence-tracked document model +
> resolve/validate seam (D-066/D-067), `missing_policy = "as_attribute"`
> (D-068/D-074), the TOML reader/writer (D-075) — strict CST reader with
> the D-070 kind gates and the closed deferred-surface set, canonical writer,
> D-010 deferred scales rejecting at plan, round-trip + §19 read→resolve
> parity tests — Slice D validation (D-076): the §16.4 spec-validate
> checks now emit at the seam (quote/delimiter D-054, the `value_type` matrix
> D-061, `restrict_to` shape D-063, ordinal-over-cuts D-060, object-key
> mode-vs-shape D-064) and the transitional plan guards fail closed
> (`restrict_to` D-057, wide `column`/`composite` object keys D-064,
> absent-domain `identity` D-071) — and Slice E fingerprints (D-069/D-077):
> the D-069 canonical JSON encoder + SHA-256 in Core (structural cut bins on
> the plan, byte-neutral on the M1 goldens), native-settings computation +
> stored-fingerprint verification in Spec (`SchemaFingerprintStale` /
> `CxtOutputFingerprintStale` / `DatOutputFingerprintStale` at spec load), the
> canonical-stability goldens, and the 30/30.0/3e1 numeric golden —
> and Slice F composition (D-078): §13 `extends` via `SpecComposer` over the
> string-only `ISpecTextSource` seam (root/base version gates, base-most-first
> fold, `SpecExtendsNotFound`/`SpecExtendsCycle`, position-preserving D-052
> attribute override, per-leaf `[output]` merge, whole-value nested binding
> tables), the `[[template]]`/`[[matcher]]` carriers merged-but-rejected-on-use
> at the seam (`TemplateMatcherNotImplementedV1`, out at M6), the
> extends/template/matcher deferred-surface retirements, and the composed≡flat
> canonical-text + three-fingerprint equivalence tests (no encoder change) —
> and Slice G, the `.bed` migrator rework (D-079): `BedReader`/`BedMigrator`
> speak `Diagnosed<T>` and target the document model (`.bed` → `SpecDocument` →
> writer/seam — D-009's "save as TOML" realized), carrying `restrict_to`
> (D-057), detecting the effective-missing-token `as_attribute` idiom (D-068),
> parking excluded config without the silent degrade (the D-049 hygiene item;
> six `migrate (v2)` diagnostic codes), and re-routing the golden harness
> through migrate→resolve byte-identically — the migrated mini-mushroom
> reproduces the pinned Slice E fingerprints exactly.
>
> **The M2 exit review has landed (D-080/D-081).** Slice H implemented the
> value-bin `ordinal` path — `identity` + an explicit string `order` (D-081) —
> thresholding on the authored order under all four `direction × boundary`
> combinations, with `OrdinalOrderMissing`/`OrdinalOrderHasUnknownValue` at plan;
> this closed the last silent-output gap (an `identity` + `ordinal` spec no longer
> resolves, plans, and emits while ignoring the authored order/boundary). The
> standalone cleanup re-homed `AttributeNameDuplicate` / `ValueLabelKeyNotInDomain`
> from the planner to the resolve seam over the document model (D-080). Both are
> byte- and fingerprint-neutral on every golden and pinned baseline.
> `dotnet test` is green (547 tests).
>
> **The M3 triple-source audit has landed (spec/decisions only, D-082…D-085).** A
> review pass over the finalized triple + wide-column-key surface settled the
> contract — shape-specific `has_header`, optional/one-mode `columns`, absent-vs-
> missing semantics, ordinal string collation (new principle **P-12**),
> wide `dedupe` first-occurrence order on the shared sort-merge path, `keep` name
> uniqueness, and the structural diagnostic taxonomy — with the spec, `decisions.md`,
> and `principles.md` updated. This landing is docs-only and byte-/fingerprint-neutral
> (no enum members, no `FixtureCase.Active` change, no production code).
>
> **M3 is complete (Slices C–G).** Slices C–D added the triple reader and both orderings
> (`subject_grouped` single-pass; `unordered` first-appearance grouping); Slice E added wide
> `column` `fail`/`keep`; **Slice F** landed wide `dedupe` on a **bounded shared
> grouping/spool backend** (spill runs + bounded-fan-in merge, the two-channel
> storage-failure model, the public `EmitReplaySession`, and the `.cxt`
> object-name-sequence invariant), migrated triple `unordered` onto that backend with
> `EmitTripleAsync` owning ordering from a new `SourceExecution` plan carrier, and
> **retired `ObjectKeyColumnNotImplementedV1` entirely** (D-082…D-085). **Slice G** activated
> the three `mini-*_triples` goldens in the harness via `FixtureCase.Triple(...)` and a
> shape-aware golden orchestrator (a replay session for `.cxt`, single-pass `.dat`), which
> required **shape-aware `.bed` migration** (D-086: triple attributes bind by predicate name)
> and a **symmetrical `[output.dat].trailing_newline`** control with a shape-derived v2-compat
> `.dat` final-newline rule (D-087). All three unordered triple goldens are byte-identical to
> v2 (verified repeatable), the final stale-reject audit is clean, and every pinned fingerprint
> and canonical-byte baseline is preserved. `dotnet test` is green (777 tests: 776 passed, one
> platform-gated confidentiality test skipped off its OS). **M4 (discretizers beyond manual
> cuts) is next** — no M4 work has started.
>
> **The pre-M4 Tier 1 spec audit has landed (docs-only, D-088…D-092).** A review pass
> over the M4 contract — auto/frozen calibration byte-equivalence and the calibration
> population (D-088), the `equal_width` range-mode split (D-089), the `value_groups`
> execution contract (D-090), the `restrict_to` execution contract with existential
> matching, exact numeric entries, and the canonical `restrictions` fingerprint
> encoding (D-091), and numeric `free_per_value` rendered labels (D-092) — updated
> `bedrock-spec-v1.md`, `decisions.md`, and `roadmap.md` in place. It renames
> `RestrictToOnNumericRequiresRange` → `RestrictToNumericEntryRequired` and adds six
> diagnostics in the §16.4 registry; the enum members and rename land with their M4
> check sites. This landing is docs-only — no production code, tests, fixtures, enum
> members, or output/fingerprint bytes changed. **M4 implementation has not started.**
>
> **The pre-M4 Tier 2 implementation-contract audit has landed (docs-only,
> D-093…D-097 + D-091 in-place clarifications).** A second review — the
> calibrated-state boundary between Calibrate and Plan (D-093), the exact M4
> canonical fingerprint encodings and effective-configuration hashing (D-094),
> bounded-memory calibration with subject-local triple deduplication (D-095), numeric
> `free_per_value` identity and normalized domain/label/order keys (D-096), and
> filter-only restriction diagnostics (D-097) — updated `bedrock-spec-v1.md`,
> `decisions.md`, and `roadmap.md` in place, and clarified D-091's merged-`dedupe`
> restriction and type-directed migration. It normatively commits two future
> spec-validate enum members (`DeclaredDomainInvalid`, `ValueLabelKeyDuplicate`,
> deferred to their M4 sites) and widens `GroupingStorageFailed` to calibrate/emit.
> This landing is docs-only — no production code, tests, fixtures, enum members, or
> output/fingerprint bytes changed.
>
> **M4 Slice A — the calibration-preparation contract — is complete (D-098…D-100).**
> The two-stage source bootstrap (`ResolveReadSettings` → `WideCsvSession`/
> `TripleCsvSession` → schema-aware `Resolve → Diagnosed<ResolvedDocument>` →
> `Bind`), the opaque validated recursively-immutable `ResolvedSpec` token with
> site-typed name bindings and a three-state `SourceProvenance` union, the
> Core-owned `CalibratedSpec` (the single `Plan(CalibratedSpec, LabelStyle)` input)
> with per-mode completeness markers, the `Calibrator` (wide + triple; discovery-class
> observed-domain and `include` calibration; the G-3 triple structural checks; the
> pairing guard before any row), the `ConversionPlan` sealed internal-ctor plan
> carrying its calibrated state + label style, the fingerprint API re-signed over
> `plan.Calibrated.Spec` (byte-neutral — all pinned hashes and nine goldens
> unchanged), the include emit-crash closure (P-22), and `NoFormalAttributes` at plan
> all landed. `ObservedDomainCalibrationNotImplementedV1` retired. The four deferred
> discretizers stay read-rejected and `RestrictToNotImplementedV1` stays active at
> plan; only the abstract `PendingCalibration` + `CalibrationPending` carrier landed
> (no concrete pending variants). `dotnet test` is green (873 passed, 1 skipped).
> **M4 Slice B (free_per_value) is next.**

## Milestones

### M0 — Skeleton + golden harness

Solution per `AGENTS.md` layout, with packages created by the milestone that
first needs them — M0 creates `FcaBedrock.Diagnostics` + `FcaBedrock.Core` plus
the test projects; the remaining packages follow as their code lands (empty
shells up front would be speculative noise — P-3). xUnit wiring (BenchmarkDotNet
deferred to M8, where its first benchmark lives).
`FcaBedrock.Golden.Tests` running against the three v2 `fixtures/v2/` examples
with placeholder pass-throughs (expected = the v2 file; actual starts as a copy
until M1). One end-to-end smoke test: read `mini-mushroom.bed` + data, write
`.cxt` and `.dat`, byte-compare to v2 in `--v2-compat` mode.
**Exit:** `dotnet test` runs; golden harness can byte-compare.

Also wire `Directory.Build.props` (nullable on, language version, analyzers,
selective `TreatWarningsAsErrors`) so the `.editorconfig` severities bite in CI.
Add mechanical backstops for principles where practical: Core purity
architecture test, large-test category CI filter, determinism repeatability
tests, and package dependency guardrails.

### M1 — Reproduce v2 on mini-mushroom + mini-adult

Wide-CSV `Source` with the streaming primitive layer (right shape from day one
even though minis are tiny). Discretizers `identity`, `manual_cuts`. Scales
`nominal`, `dichotomic`, `ordinal`. `value_labels` (D-023). Both writers with
byte-equality to v2. v2 `.bed` reader as the only spec input at this stage.
The `mini-dates` example is **not** an M1 target — date support is deferred
(D-038), so it is a parked fixture activated only when date scaling lands.
**Exit:** golden tests byte-identical (or a single documented diff behind
`--v2-compat`). Smallest end-to-end slice proving the architecture.

### M2 — TOML spec format + fingerprinting

New TOML schema, reader/writer, and three fingerprints — `schema_fingerprint`
plus per-format `cxt_output_fingerprint` / `dat_output_fingerprint` (D-051) over a
plan-derived canonical JSON encoding (D-053). One-way `.bed` → TOML migrator,
moved toward `Diagnosed<T>` while it is reworked (the D-049 migrator-hygiene item;
minimum bar: no silently-dropped parked config). Round-trip tests over the
M2-supported v1 surface (the D-070/D-072 carrier scope) plus the three v2
examples, covering
omitted-vs-authored defaults (D-049 presence tracking), parked config,
`restrict_to` string/range/open-ended/mixed forms, `[output]` per-leaf `extends`
merge, multi-file `extends` chains, `[provenance]`, and preserved-but-rejected
templates/matchers. Add `interordinal`/`biordinal`/`contranominal` as parsable
types the planner rejects (D-010). `[output]`, `[provenance]`, `extends`
(D-027, position-preserving override D-052) parsing.

The finalized M2 spec decisions **D-050…D-058** land here (see the decisions.md
index for titles). Also the value-bin `ordinal` path + live `boundary` knob
deferred from slice 2 (D-047), and the remaining D-049 writer-fidelity items
(presence tracking; the default-`boundary` cut-bin trap §12.3; the
`value_type`-vs-discretizer rule §10.2). New diagnostics land as their emit
sites do; data-phase codes are aggregated from day one (count + bounded sample,
not per-row).

The **Tier 1 spec-audit** decisions **D-060…D-065** further refine the M2
contract (ordinal-over-cuts validation, the `value_type` matrix, dropping the
unreachable cross-attribute-restrict diagnostic, `restrict_to` shape-validation
vs M4 execution, the object-key taxonomy with wide `column` execution → M3,
calibration-before-`restrict_to`); see the decisions.md index.

The M1-adjacent conformance pass that reconciled already-shipped M1 code with
the M2 wording has **landed** (see Current position). The M2 spec/docs describe
the v1 **end-state**; the M1 byte-equality goldens were not reopened, and the
malformed-numeric change was byte-neutral (still "keep object, no cross", only
adds a diagnostic).

**Verification gates:** re-run the M1 golden suite after the whitespace and
malformed-numeric changes (v2-compat guard); a fingerprint-stability golden for
the canonical numeric encoding (`30` / `30.0` / `3e1`); value-bin ordinal
conformance tests over all `direction × boundary` combinations plus
`OrdinalBoundaryIncompatibleWithCuts` and `OrdinalOrderNotAllowedWithCuts` (both at
spec validate, D-060); and `.gitattributes` for new byte-sensitive
TOML/expected-output fixtures.

**Deferred from M2:** triple headers / triple role-name binding / object-/
subject-name filtering → M3 (triple-source audit); `restrict_to` *execution* → M4;
template/matcher *resolution* → M6. M2 parses/preserves/round-trips these where the
format requires it (e.g. templates/matchers under `extends`) but rejects their use
with the transitional diagnostics above.

The **Tier 2 register (D-066…D-072)** settles the M2 model boundary and carrier
scope: the document-model / Core two-model split with a single resolve+validate
seam owning the static diagnostics by phase (D-066/D-067); the canonical
fingerprint encoding pinned before any stored hash ships (D-069);
`missing_policy = "as_attribute"` scheduled into M2 (D-068); and three
carrier-vs-execution gates — deferred discretizers recognized by kind name and
rejected at read/resolve without parameter carriers (D-070), absent/`[]`
`declared_domain` rejected until Calibrate lands (D-071), and the basic triple
carrier round-tripping but rejecting conversion → M3 (D-072). Net M2 execution
scope: only `identity`/`manual_cuts`/`ordered_cuts` convert; the value-bin
ordinal is string-only (`identity` + explicit `order`; numeric → M4); triple
conversion is M3.
**Exit:** TOML specs in the M2-supported v1 surface round-trip; v2 specs migrate;
known v1 features outside M2 produce clear diagnostics.

### M3 — Three-column (triple) source

Subject-grouped fast path (single-pass streaming) first; unordered slow path
(external sort-merge; the in-memory buffer is a runtime knob, not a spec field)
second. Object-key derivation from the subject column. Reproduce the three triple
goldens byte-identical (`mini-mushroom_triples`, `mini-adult_triples`, and the
named-subjects `mini-adult_triples_named`, spec §19.3 — all `unordered`, since
their inputs are subject-interleaved; first-appearance object order). Also lands **wide
`object_key.mode = "column"`** execution and activates `duplicate_object_policy`
(D-064) — the same column-object-key machinery, shared with triple's subject-derived
key; M2 only parses/round-trips/rejects it (`ObjectKeyColumnNotImplementedV1`).

The **M3 triple-source audit (D-082…D-085)** settled the contract before coding:
shape-specific `has_header` (triple defaults `false`); `columns` optional / one
addressing mode; object identity = the resolved subject (an authored triple
`object_key` is rejected); **absent predicate = no observation** (≠ a present-missing
value); structural row/source errors are `Error`; ordinal string collation is a
project-wide rule (new principle P-12); wide `dedupe` runs on the shared
sort-merge path and emits in **first-occurrence** order — the same first-appearance
principle as triple `unordered` (neither sorts object output); `keep` guarantees
unique object names; and the structural diagnostic
taxonomy (new `ObjectKeyValueInvalid` / `TripleColumnsNotDistinct`, extended
`SourceBindingInvalid`). `TripleSourceNotImplementedV1` retired with the
subject_grouped reader (Slice C) — briefly replaced by a narrower transitional
unordered guard, which itself retired when the `unordered` grouping landed
(Slice D); `ObjectKeyColumnNotImplementedV1` narrowed to wide `dedupe` when
`fail`/`keep` landed (Slice E) and retired when `dedupe` landed (Slice F, on the
bounded shared grouping/spool backend). **Slices C–G are implemented: Slice G** activated
the three `mini-*_triples` goldens (`FixtureCase.Triple(...)`, a shape-aware golden
orchestrator), which required **shape-aware `.bed` migration** (D-086: triple attributes
bind by predicate name) and a **symmetrical `[output.dat].trailing_newline`** control with a
shape-derived v2-compat `.dat` final-newline rule (D-087), plus the final repeatability
verification and stale-reject audit. **M3 is complete.**
**Exit:** both triple orderings work; all three triple-input goldens match; wide column
object keys convert with `duplicate_object_policy` honored.

### M4 — Discretizers beyond manual cuts (continuous + grouping)

`free_per_value`, `equal_width(n)`, `equal_frequency(n)` with their knobs
(`range`, `precision`, `tie_policy`, `cut_placement`), plus the **`value_groups`**
discretizer (D-022/D-055) — the non-M1 discretizers deferred from M2 (recognized by
name and rejected there, D-070). Calibration pass over synthetic distributions;
`value_groups` `unmatched = "passthrough"` resolves its data-dependent bins here
too. Restrict-on-raw-value semantics tested explicitly (D-021). `calibrate` command
groundwork (D-028).

The **pre-M4 Tier 1 spec audit** (D-088…D-092, docs-only) pins the M4 contract and
widens its scope to also include: **`unknown_value_policy = "include"`**
calibration and **closure of the confirmed `include` emit crash**; **identity
observed-domain calibration** (filling an absent `declared_domain` from data),
which **retires `ObservedDomainCalibrationNotImplementedV1`**; and `restrict_to`
**execution** — **existential** matching (D-091) with **exact numeric entries**
(`{ value = n }`) alongside ranges — which retires `RestrictToNotImplementedV1`.
Auto/frozen calibration is byte-equivalent (D-088); `equal_width` `range = "manual"`
stays spec-determined (D-089); `value_groups` gets its regex/validation/ordinal
contract (D-090).

The **pre-M4 Tier 2 implementation-contract audit** (D-093…D-097, docs-only) fixes
three further M4 obligations: **bounded-memory calibration** — equal-frequency and
percentile-range cuts are **exact and bounded-memory at M4** (spill-or-equivalent,
byte-identical to the in-memory path; approximate quantiles prohibited; M8 only
tunes budgets and benchmarks, D-095); **restriction/emit observability** — the
already-registered `NoObjectsEmitted`, `AttributeHasNoCrosses`, and
`ObjectHasNoCrosses` warnings (§16.4) get their **emit sites at M4** as
`restrict_to` execution lands (no spec change — already registered); and the
**calibrated-state contract** — Calibrate produces a Core-owned, immutable resolved
outcome (cuts, observed domains, `include` additions, pass-through bins) that Plan
consumes without re-derivation (D-093). Subject-local triple deduplication keeps the
triple calibration bounded (D-095), and two new spec-validate diagnostics
(`DeclaredDomainInvalid`, `ValueLabelKeyDuplicate`, D-096) land with their M4
validate sites.

**Manifest deferral boundary.** Numeric calibrated cuts remain **required**
manifest data (§15), and all resolved calibration outcomes — cuts, observed
domains, included values, and pass-through bins — remain in the calibrated
spec/plan; only *additional* non-cut manifest representation of
discovered/appended/passthrough values is deferred to the manifest layer, without
re-deriving M4 semantics.

**Exit:** auto-binning calibrates deterministically and byte-identically to its
frozen form; cuts captured in manifest; `value_groups` (incl. passthrough)
converts; observed-domain calibration and `unknown_value_policy = "include"`
resolve; `restrict_to` filters (existential; exact-numeric and range entries).

### M5 — Discovery / auto-detect

Single-pass probe producing a draft TOML spec; v2's 100-distinct-value cap as a
config knob with a "truncated" marker; defaults to `identity` + `nominal`.
**Exit:** `probe` produces an editable draft spec from raw data.

### M6 — Templates + matchers

The bulk-edit model in `Spec` (defaults < templates < matchers < per-attribute
overrides; last-match-wins). Replaces v2 "Repeat-To". Also lands the
**naming-fidelity carriers** deferred from M2 — `display_name` and
`formal_attribute_format` on attributes/templates (and `[defaults]
.formal_attribute_format`), until now recognized-but-rejected at read
(`SpecSurfaceNotYetSupported`, §16.4).
**Exit:** the Internet-Ads dataset (1554 booleans) expressible in <50 lines of
TOML; resolution precedence tested; `display_name` / `formal_attribute_format`
round-trip and drive rendered names.

### M7 — CLI

`convert`, `validate`, `plan` (dry-run plan inspection), `stats` (context
statistics without writing), `calibrate`, `migrate` (.bed → TOML),
`fingerprint`. `--v2-compat`, `--sample`, compression flags as they land.
**Exit:** Core is dogfoodable end-to-end without a UI; `plan`/`validate` give a
fast spec-authoring loop.

### M8 — First scaling / benchmark pass

BenchmarkDotNet against synthetic 7.3M- and 73M-record datasets (in
`FcaBedrock.Benchmarks`, gated behind a category filter — NOT in normal
`dotnet test`). Profile, fix allocation hotspots, set memory budgets. Pressure-
tests the `Sources` and `Conversion` streaming choices (D-007).

**Cross-platform resident-accounting validation** (prerequisite for the
cross-platform v1 / Avalonia release, M9). D-082's resident-accounting layout
constants carry a numerical `actual retained ≤ modeled` guarantee only on
.NET 10 CoreCLR **x64**. Before shipping a cross-platform release, validate them on
the other target runtimes: validate the constants on .NET 10 CoreCLR **ARM64**,
exercising **macOS ARM64** and, where available, **Linux/Windows ARM64** in CI, and
verify `Unsafe.SizeOf<RankedRow<T>>`, object/array/string layouts, reference sizes,
alignment, and the numerical `actual retained ≤ modeled` guarantee on each. Introduce
**target-specific correctness constants** if runtime layouts differ, and extend D-082's
numerical guarantee beyond x64 **only after** each target is validated. Validated
Windows, Linux, and macOS runtime targets are a **prerequisite for the cross-platform
release**. Note the split (as in the grouping-backend note below): M8 may tune the
buffer budget and fan-in, but the layout **safety constants are correctness inputs** —
they cannot be performance-tuned without revalidation.

**Calibration budgets.** M8 may tune the equal-frequency / percentile calibration
memory budget and benchmark its spill/aggregate algorithms, but **exact
bounded-memory calibration already exists at M4** (D-095) — boundedness is a
correctness input established there, never here.

**Exit:** documented throughput/memory at target scale; no full-matrix
materialization.

### M9 — Avalonia desktop

Parallel-able from M5 onward; does not gate the CLI track. MVVM over the same
Core. Progress reporting + cancellation already plumbed from M1.
**Exit:** load → inspect → edit spec → export, on Windows/macOS/Linux.

## Deferred backlog (not v1)

Modelled in the spec where noted, so adding them later isn't a format break.

- **Composite object keys** (D-024) — modelled, planner rejects in v1;
  implement v1.1 once streaming is proven.
- **Advanced scales** `interordinal`, `biordinal`, `contranominal` (D-010) —
  modelled, planner rejects; implement post-v1.
- **Date value type** (`value_type = "date"`) and date scaling (D-038) —
  reserved, planner rejects (`DateValueTypeNotImplementedV1`); reproduces v2's
  `d` type when implemented. `mini-dates` is the parked fixture. Re-enabling is
  a non-breaking addition (the `value_type` field already exists).
- **`std_dev` discretizer** (D-020) — removed entirely; re-add as a new
  discretizer kind if a real need appears (non-breaking).
- **Cross-attribute restrict** ("include attr A only when attr B = X") — **not
  modelled** in v1 (no reserved carrier syntax; the unreachable
  `RestrictCrossAttributeNotImplementedV1` was dropped, D-062); prose-only in spec
  §20. Future enhancement.
- **Post-context reductions** — clarify / reduce / minimum-support, in a
  sibling `FcaBedrock.Reduce` tool (D-025). Min-support flagged by the thesis.
- **Direct DB / SPARQL adapters** — thesis future work; new `Sources` adapters
  behind the existing `IObjectRecordStream` abstraction. (SPARQL2FCA may inform
  this — see `docs/lineage.md` once that source is folded in.)
- **XLSX input** — separate `FcaBedrock.Sources.Excel`; defer unless painful.
- **JSONL / NDJSON input** — modern 3-column analog; consider modelling
  `shape = "jsonl"` in the binding even before implementing the reader.
- **Multi-level taxonomic value hierarchies** — value_groups is single-level in
  v1; multi-level (Bachelors → Uni-Degree → Education with per-analysis
  granularity) is a real design exercise, deferred until single-level ships.
- **Sampling / compressed output / memory-budget knob** — streaming filters and
  writer wrappers; additive, land opportunistically (likely around M7/M8).
- **TCA (triadic FCA)** — out of scope for the foreseeable.

## Notes for whoever picks this up

- The eight-package figure in older notes predates `FcaBedrock.Diagnostics`
  (D-006); the count is now 9.
- M1's "byte-identical to v2" goal is aggressive but is the cheapest proof that
  the pipeline reproduces the compatibility target. It is **not** proof that v2
  was semantically perfect — v2 has known bugs (lineage.md). If vNext
  intentionally diverges, record the decision and gate compatibility behavior
  behind `--v2-compat` where needed, rather than chasing a v2 quirk forever.
- The CLI track (M0–M8) and the UI track (M9) are independent after M5. Don't
  let UI work block converter progress.
- The 2026-06-26 review deferred three tightening items: cut validation (now D-056,
  landed in M2), a `.cxt` size/diagnostics item to M7, and an allocation item to
  M8. The latter two are tracked here pending their own `decisions.md` entries when
  M7/M8 are picked up.
- Conversion run/session API (M7): M3 Slice F promoted the interim replay helper to the
  public `EmitReplaySession` (`EmitReplay.Begin`) — it brackets one conversion attempt,
  collects data diagnostics once, and aggregates grouping storage failures across the
  `.cxt` two-pass, flushing the finals at disposal (P-16). Revisit at M7, when CLI
  orchestration can own a real conversion-run abstraction that emits once and serializes
  separately; the session may be superseded by that API.
- Grouping backend knobs (M7/M8): `GroupingOptions` (in-memory budget, merge fan-in,
  temp root) is **internal** — never a spec/TOML/fingerprint input (the storage strategy
  never changes bytes). Exposing a memory-budget/temp knob is an M7 concern; tuning the
  provisional budget/fan-in defaults against real 7.3M–73M distributions is M8 (P-19).
- Phase alignment for `AttributeNameDuplicate` (noted at the Slice F review,
  2026-07-05): **done at the M2 exit review (D-080).** The check — and its twin
  `ValueLabelKeyNotInDomain` — were re-homed from `ConversionPlanner` to the
  resolve seam over the document model, matching the §16.4 "spec validate" cell
  (the D-067 phase-ownership reading), byte- and fingerprint-neutral.
