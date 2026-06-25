# Roadmap — FcaBedrock vNext

Milestones, current position, and the deferred-items backlog. Update the
"current position" marker as work progresses. Milestones are incremental
vertical slices, not waterfall phases — each should leave the system working.

## Current position

> **M1 complete — mini-mushroom + mini-adult reproduced byte-for-byte.** The whole
> pipeline runs end-to-end and matches v2 on both families: `.bed` reader
> (`FcaBedrock.Spec`), wide-CSV source over Sep (`FcaBedrock.Sources`, D-041),
> planner (`FcaBedrock.Core`), streaming emitter (`FcaBedrock.Conversion`), and
> `.cxt`/`.dat` writers with a `--v2-compat` preset (`FcaBedrock.Export`).
> **Slice 1** delivered `identity` + `nominal` + `dichotomic` + `value_labels` on
> mini-mushroom. **Slice 2** added the cut discretizers `manual_cuts` (numeric,
> locale-aware) and `ordered_cuts` (categorical, D-046) sharing one label helper,
> the `ordinal` scale (cumulative `le`, open-end `all`, D-047), the plan-time
> `LabelStyle` render hook (`30to<40` vs `[30, 40)`, D-044), `.bed` types `o`/`n`
> with the out-of-band `ScalingMode` (D-045), and the four **mini-adult** goldens
> (base, noheader, employment-ordinal discrete + progressive). Output is proven on
> two axes (D-043: golden v2-compat byte-equality + native spec conformance);
> ArchUnit dependency/cycle/purity rules are non-vacuous. `dotnet test` is green
> (136 tests).
>
> **Next: M2** — the TOML spec format + fingerprinting (and the value-bin `ordinal`
> path + independent `boundary` knob deferred from slice 2).

## Milestones

### M0 — Skeleton + golden harness

Solution per `CLAUDE.md` layout, with packages created by the milestone that
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

New schema, reader/writer, schema + output fingerprints. One-way `.bed` → TOML
migrator. Round-trip tests over every scale × discretizer combination plus the
three v2 examples. Add `interordinal`/`biordinal`/`contranominal` as parsable
types the planner rejects with structured diagnostics (D-010). `[output]`,
`[provenance]`, `extends` (D-027) parsing.
**Exit:** any v1 spec round-trips; v2 specs migrate; rejected scales produce
clear diagnostics.

### M3 — Three-column (triple) source

Subject-grouped fast path (single-pass streaming) first; unordered slow path
(external sort-merge, configurable in-memory buffer) second. Object-key
derivation from the subject column. Reproduce `mini-adult_triples_named`
byte-identical.
**Exit:** both triple orderings work; the triple-input golden matches.

### M4 — Continuous scaling beyond manual cuts

`free_per_value`, `equal_width(n)`, `equal_frequency(n)` with their knobs
(`range`, `precision`, `tie_policy`, `cut_placement`). Calibration pass over
synthetic distributions. Restrict-on-raw-value semantics tested explicitly
(D-021). `calibrate` command groundwork (D-028).
**Exit:** auto-binning calibrates deterministically; cuts captured in manifest.

### M5 — Discovery / auto-detect

Single-pass probe producing a draft TOML spec; v2's 100-distinct-value cap as a
config knob with a "truncated" marker; defaults to `identity` + `nominal`.
**Exit:** `probe` produces an editable draft spec from raw data.

### M6 — Templates + matchers

The bulk-edit model in `Spec` (defaults < templates < matchers < per-attribute
overrides; last-match-wins). Replaces v2 "Repeat-To".
**Exit:** the Internet-Ads dataset (1554 booleans) expressible in <50 lines of
TOML; resolution precedence tested.

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
- **Cross-attribute restrict** ("include attr A only when attr B = X") — noted
  in spec §19; future enhancement.
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
