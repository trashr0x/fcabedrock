# Lineage — what the predecessors settled

A one-time synthesis (not a maintained wiki) of the prior work that informs
FcaBedrock vNext: **FcaBedrock v2** (VB.NET), the **PhD thesis**, and
**SPARQL2FCA** (the WPF "ContextScaleWPF" prototype). Purpose: capture the
feature floor, the semantics already reasoned through, and the design signals,
so vNext neither under-builds (missing learned features) nor over-builds
(re-deriving settled answers). Cross-referenced from `decisions.md` and
`bedrock-spec-v1.md`.

---

## 1. FcaBedrock v2 (VB.NET, ICCS 2010)

The direct predecessor. A desktop tool that loads tabular or 3-column data,
auto-detects or accepts a `.bed` spec, and exports `.cxt` / `.dat`.

**Feature floor (vNext must meet or exceed):**

- Attribute types — **six type codes**, confirmed from `frmFcaBedrock.vb`
  (`characteristic(k)`): `c` categorical, `b` boolean, `o` continuous-numeric,
  `d` **date** (distinct type using `DateTime.Parse`, *not* "continuous"), `n`
  **ordinal** (ordered categories with discrete/progressive scaling), plus
  missing handling. Earlier notes that said "dates treated as continuous" were
  wrong: `d` shares the *binning logic* of `o` but parses `DateTime` over a
  date value space. Mapping to vNext's orthogonal model:

  | v2 code | meaning | vNext expression |
  | --- | --- | --- |
  | `c` | categorical | `identity` + `nominal` |
  | `b` | boolean | `identity` + `dichotomic` |
  | `o` | continuous-numeric, discrete/progressive | numeric discretizer + `nominal`/`ordinal` |
  | `d` | date, discrete/progressive | **deferred in v1** — `value_type="date"` reserved, planner rejects (`DateValueTypeNotImplementedV1`, spec §11.7) |
  | `n` | ordinal (ordered categories) | ordered categorical discretizer + `ordinal` scale |

  So vNext needs no new *scale* for `o`, `d`, or `n` — all fall out of the
  discretizer × scale split. `n` is already expressible today; `d` is a
  conscious parity deferral (continuous-numeric is the v1 priority; date scaling
  reserved but not implemented — D-038). `c`, `b`, `o`, `n` are the v1 target.
- Continuous treatments: free binning (one bin per value), user-defined
  boundaries with discrete (interval) scaling, user-defined boundaries with
  progressive (cumulative) scaling, equal-width auto-bins, equal-frequency
  auto-bins, std-dev bins. **vNext v1 covers all of these except std-dev**,
  which is intentionally *removed* (not deferred) — no current user need and not
  in the M1 compatibility target (D-020). Note v2's source has a std-dev branch
  and SPARQL2FCA had std-dev variants, so "all v2 behavior" is qualified: v1
  targets the compat/golden behavior minus std-dev (removed) and date (deferred).
- Attribute exclude (`[Convert Attribute] = False`).
- Declared values via `[Category Values]`; display labels via
  `[Attribute Categories]` (raw `b` → shown `broad`). **Both matter for
  byte-equality** — see vNext `value_labels` (D-023).
- Object restrict (`[Restrict To Values]`): OR within an attribute, AND across
  attributes, operating on raw values independent of bin assignment.
- "Repeat-To": positional bulk copy of an attribute's config forward to
  attribute N (used for Internet-Ads' 1554 booleans). vNext replaces this with
  templates + matchers (D-022-adjacent, spec §9).
- Auto-detect (data-first) and `.bed` load (spec-first).
- Missing handled by including/excluding `?` in `[Category Values]`.
- Output: `.cxt` (Burmeister) and `.dat` (FIMI), CRLF, with v2's specific byte
  layout. These are the M1 golden fixtures.

**`.bed` format:** parallel arrays under bracketed headers
(`[Number of Attributes]`, `[Attributes]`, `[Attribute Categories]`,
`[Category Values]`, `[Convert Attribute]`, `[Attribute Type]`,
`[Restrict To Values]`, `[End]`). Index-aligned and brittle — one misaligned
line silently corrupts the spec. vNext goes record-per-attribute TOML (D-009),
with a one-way `.bed` reader for migration.

**Operational scale:** ~732,681 EMAGE triples in the thesis work. vNext targets
10×–100× this (D-007).

---

## 2. PhD thesis (Orphanides, Jan 2023)

"Appropriating Data from Structured Sources for Formal Concept Analysis."
Establishes the *semantics* vNext implements and the *theory ceiling* it
leaves room for.

**Settled semantics:**

- The discretization vs scaling distinction is conceptual in the thesis
  (Ch 4.7 booleanization/discretization vs Ch 4.8 conceptual scaling). vNext
  makes it structural — the orthogonal discretizer × scale model (D-002).
- Continuous attributes: user-defined disjoint ranges (interval/nominal),
  hierarchical scaling (the ordinal equivalent: `>10, >20, …`), and auto
  equal-width / equal-frequency with a user-defined bin count. v2's distinct
  `d` (date) type used the same binning logic over a `DateTime` value space;
  vNext v1 **defers** date (D-038), implementing the numeric menu (spec
  §11–§12) and treating dates as strings until date support lands.
- Declared vs observed domain: the mushroom "class can be edible or poisonous
  even if only edible appears in this file" example. vNext's `declared_domain`
  (spec §10.3).
- Object restriction, attribute restriction, conceptual scaling,
  generalizability, user-driven + guided automation — the Ch 7 "essential
  features" list, effectively vNext's v1 acceptance contract.

**Theory ceiling (modelled, not implemented in v1 — D-010):**

- Full Ganter-Wille scale taxonomy: nominal, ordinal, interordinal, biordinal,
  contranominal. v1 implements nominal/dichotomic/ordinal; the rest are
  parsable-but-rejected.

**Future work the thesis flags (vNext backlog):**

- Direct DB / triple-store adapters (no CSV detour).
- Minimum-support filtering on the produced context (vNext: sibling
  `Reduce` tool, D-025).

---

## 3. SPARQL2FCA / "ContextScaleWPF" (WPF prototype)

A separate prototype, newer than v2 in some respects, explored triple-store /
SPARQL input and scaled query results into formal contexts. The source code is
not assumed to be available to future readers, but the approach is documented
in the PhD thesis, §5.4.4 "CUBIST Scaleful Approach". For vNext, this is
treated as a design signal for a future SPARQL adapter, not as a v1 requirement.

**What it confirms / adds beyond v2:**

- **SPARQL result sets as a first-class source.** A `SELECT` query's columns
  become attributes, its rows become objects. The bundled example is a DBpedia
  query for goalkeepers, their birth country, team, team's country, and stadium
  capacity, with `FILTER`s on capacity and population. This is the concrete
  shape of the thesis's "direct triple-store adapter" future work, and the
  template for a vNext `Sources` SPARQL adapter behind `IObjectRecordStream`.
- **An explicit `Ordinal` attribute type** in the type enum. This matches v2's
  own `n` (ordinal) type — both treat ordinal as first-class rather than a
  continuous sub-mode. In vNext's model this is the `ordinal` scale over an
  ordered categorical discretizer (D-038); the prototype and v2 agree it
  deserves explicit support.
- **An explicit `ScalingType { Discrete, Progressive }` toggle**, separate from
  attribute type. This is the clearest prior-art signal for vNext's
  orthogonal discretizer × scale split (D-002): the prototype already separated
  "what are the cuts" from "discrete or cumulative crossing."
- **`BinningType { EqualWidth, EqualDepth, ProgressiveStandardDeviation,
  ProgressiveSampleStandardDeviation }`.** "EqualDepth" = equal-frequency. The
  two std-dev variants differ only in population (÷n) vs sample (÷n−1) variance.
  vNext implements equal-width and equal-frequency; std-dev was cut (D-020) —
  this is the prior art for it, available if re-added later.
- **Bin label style** `name-{lo}to<{hi}`, with the final bin `name-{lo}to<={hi}`
  (closed at the top). Matches v2's `30to<40` family; vNext defaults to math
  notation and keeps the v2 style behind `--v2-compat` (D-011).
- **Web visualization export.** The prototype also explored sending formal
  contexts to a web-based lattice viewer developed as part of the CUBIST EU FP7
  project. This is not a vNext v1 concern; v1 focuses on `.cxt` and `.dat`.

**Known weaknesses (do NOT carry forward):**

- The prototype coupled UI, data access, scaling, and export logic too tightly.
  vNext keeps these concerns separated through Core, Sources, Conversion, and Export.
- Some binning behavior was prototype-oriented rather than specification-grade:
  fixed bin counts, ad-hoc cut placement, and order-coupled output assembly.
  vNext defines bin counts, ordering, calibration, and export behavior in the
  spec and planner instead.
- The prototype is useful as lineage for SPARQL/source ambition and scaling
  concepts, not as implementation behavior to reproduce byte-for-byte.

---

## 4. Net effect on vNext design

The three predecessors agree on the substance and differ only in rigor:

- **Feature floor** = v2's full surface. **Semantics** = the thesis. **Source
  ambition** (SPARQL/triple-store) and the **discrete/progressive + ordinal**
  signals = SPARQL2FCA.
- The single most important inherited idea — orthogonal discretization vs
  scaling — appears in embryo in SPARQL2FCA's separate `ScalingType` enum and
  is made the architectural spine of vNext (D-002).
- The SPARQL adapter is deferred (D-007 scope is CSV/triples first) but its
  shape is now concrete: a `SELECT` result set is just another
  `IObjectRecordStream`, so it slots in without disturbing Core/Conversion.
- Three independent binning bugs in SPARQL2FCA are explicitly catalogued above
  so vNext's principled implementations are validated against the *intent*, not
  the buggy prior code.

> Basis for this synthesis: the original FcaBedrock v2 behaviour and example
> files, the PhD thesis, and the SPARQL/CUBIST prototype lineage described in
> the thesis. This document records design-relevant lessons, not a forensic
> inventory of source archives.
