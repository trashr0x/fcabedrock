# Roadmap — FcaBedrock vNext

Milestones, current position, and the deferred-items backlog. Update the
"current position" marker as work progresses. Milestones are incremental
vertical slices, not waterfall phases — each should leave the system working.

## Current position and milestone history

**Now:** M1–M7 are complete. The `fcabedrock` global tool ships all eight commands over
the publication transaction, the run manifest, and the freeze engine (D-122/D-123), with
the diagnostic registry at 82. **M8 — the first scaling/benchmark pass — is in
progress**, under the **D-126** measurement limitation: the suite, its corpora and oracles,
the CLI-host and hashing coverage, the standalone distribution, and the target-scale
evidence landed under D-124; the canonical GitHub cutover is done; the first five-target
native run failed and found two pre-existing M7 defects, now corrected under **D-125**; and
a complete five-target run at `4216610b` has since passed on every target and produced all
three required archives, which are retained and hash-verified. The historical CLI-host
figures were **not** re-measured into a controlled replacement — the attempted comparison
failed its collective gate, so the correction's incremental latency is **inconclusive at
the 5% bound and no retry is owed** — while the corrected build's allocation and output
validation (all fifteen CLI-host cases, including all three 73M) and its six
corrected-command resource traces are **complete**. What remains is the finalization gate
sequence in the M8 block below, then M9. The milestone blocks below are the append-only
history; the M7 and M8 sections carry the live detail.

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
>
> **M4 Slice B — `free_per_value` + numeric identity — is complete (D-101).** The
> `free_per_value` discretizer is executable and leaves the transitional read-reject
> set (`DiscretizerKindNotYetSupported` narrows to the remaining three kinds). It is
> type-flexible (D-061): `value_type = "string"` bins each spelling verbatim,
> `value_type = "number"` bins the parsed numeric identity (`90`/`90.0`/`9e1` → one
> bin `90`, every zero spelling → `0`) via the new public `CanonicalNumber` (locale
> parse, finite-only, scoped positive-zero canonicalization — the `fp_format = 1`
> encoder and every stored hash byte-for-byte unchanged, G-6). The resolve seam
> normalizes numeric `declared_domain` / `value_labels` / `scale.order` keys to
> canonical identities (`DeclaredDomainInvalid`, `ValueLabelKeyDuplicate`) while the
> document round-trips authored spellings verbatim (D-096); the calibrator extends to
> numeric observed-domain + `include` calibration over canonical keys with a
> per-phase `SourceValueUnparseable` (D-100); numeric value-bin ordinals derive
> natural ascending order when no order is authored, all four `direction × boundary`
> combinations covered; and the `{"kind":"free_per_value"}` fingerprint is
> golden-locked (D-094). `equal_width`, `equal_frequency`, `value_groups`, and
> `restrict_to` stay transitionally rejected. One approved architecture-test
> correction: `Packages_ShouldBeFreeOfCycles` now slices by production assembly
> (its documented package-level intent) rather than by full namespace. `dotnet test`
> is green (996 passed, 1 skipped).
>
> **M4 Slice C — `equal_width` + the shared cut engine — is complete (D-102).** The
> `equal_width` discretizer is executable in both Slice C range modes and leaves the
> transitional read-reject set (`DiscretizerKindNotYetSupported` narrows to
> `equal_frequency`/`value_groups`). Its numeric-cut execution now runs through a new
> shared **`NumericCutBins`** engine that `manual_cuts` also composes — the extraction
> that makes the D-088 auto/frozen byte-equivalence **structural** rather than a
> property two code paths maintain (byte-neutral: all nine goldens, every pinned
> canonical-byte baseline and SHA vector, and the authored `-0.0` manual-cut encoding
> are unchanged). `range = "manual"` is spec-determined (cuts from `vmin`/`vmax`, no
> data pass, fully-frozen-eligible); `range = "min_max"` resolves to the
> `CalibrationPending` carrier the `Calibrator` fills with a **streaming min/max**
> (two doubles — never the population; count-insensitive, so the triple path needs no
> subject-local dedup), and `CalibratedSpec.Create` substitutes the executable
> discretizer over the retained `CalibratedCuts`. The cut formula is pinned and
> **sign-aware**, so every finite increasing range — `[-1.7e308, 1.7e308]` included —
> derives finite cuts (G-7), with `MidpointRounding.ToEven` and positive-zero
> canonicalization of computed cuts (G-6). Four diagnostics landed
> (`EqualWidthRangeInvalid`, `EqualWidthCutsCollapsed` at spec validate;
> `CalibrationDataInsufficient`, `CalibrationCutsInvalid` at calibrate) and both
> `equal_width` fingerprint encodings are golden-locked with independently-computed
> SHA-256 vectors (D-094). `range = "percentile_p1_p99"` stays an unrecognized range
> spelling (`SpecFieldInvalid`) until Slice D — enforced at the reader **and** at the
> calibrated-state boundary, so percentile cannot become executable by any route;
> `equal_frequency`, `value_groups`, and `restrict_to` stay transitionally rejected.
> `dotnet test` is green (1225 passed, 1 skipped).
>
> **M4 Slice D — `equal_frequency` + percentile + the bounded quantile engine — is
> complete (D-103).** M4's **count-sensitive** calibration: `equal_frequency` (§11.5) and
> `equal_width` `range = "percentile_p1_p99"` (§11.4) are executable, so `equal_frequency`
> leaves the transitional read-reject set (`DiscretizerKindNotYetSupported` narrows to
> **`value_groups` alone**) and percentile leaves the Slice C spelling gap now that its
> calibration exists. `equal_frequency` is number-fixing and — unlike `equal_width` — has **no
> spec-determined mode**: every configuration draws its cuts from the population, so it always
> resolves to the `CalibrationPending` carrier and composes the shared `NumericCutBins` engine
> (open ends), needing no ordinal path of its own. **Rank selection is exact and separate from
> binary64 placement**: the target `N·k/bins` is never materialized in floating point but
> cross-multiplied in `UInt128`, because above 2^53 a `double` rank silently selects the wrong
> order statistic — reachable at the v1 target population (D-007). The **§11.5 feasibility
> precedence** is now normative (G-5): honoring `tie_policy` and producing `bins - 1` distinct
> ascending gaps can be mutually unsatisfiable — on collision and at **both** domain edges — so
> boundaries take the nearest feasible gap in a monotone window, which may place a tied group on
> the opposite side of its preference; that is normal resolution, never a diagnostic, and it makes
> the distinct-gap obligation total with ascent structural. `cut_placement = "midpoint"` reuses
> §11.4's **sign-aware** split (neither form alone is overflow-safe) with an adjacent-double
> fallback, and every computed cut is positive-zero canonicalized (G-6 — authored bytes and
> `fp_format = 1` untouched). Percentile selects **exact order statistics** (never interpolated or
> sketched) and feeds the existing Slice C derivation, so there is no second copy of the
> interpolation formula. Exactness is bounded: a **fixed-capacity fill-and-spill**
> `QuantileAccumulator` (dictionary + charged sort buffer, `Modeled(cap) = 384 + cap·44` on x64)
> with online consolidation keeping the run catalog ≤ fan-in, a fixed pending-deletion cap the 3T
> byte rule provably cannot supply, release-before-merge, and a **consolidated-run two-pass replay**
> the zero-spill path shares — so spill/non-spill byte identity is structural. ±0 folds at intake,
> so which zero spelling reaches a key, a run, and a cut is pinned rather than left to arrival
> order (the dictionary and comparer treat both as one value either way). Counting is `checked`;
> overflow is the
> one new diagnostic, **`CalibrationPopulationTooLarge`** (G-13). Triple counts obey §5.3.1
> subject-locally: `subject_grouped` dedups inline, `unordered` adds a grouped second pass **only**
> when a count-sensitive need exists (never a third pass, never a dataset-wide set). The resource
> contract is stated honestly in **two tiers** — byte-exact accumulator state; structurally bounded
> I/O and run metadata, deliberately not byte-modeled because `FileStream` internals are
> runtime-owned. Both new fingerprint encodings are golden-locked with independently-computed
> SHA-256 vectors (D-094); every pre-Slice-D canonical byte, SHA vector, and all nine golden
> fixtures are unchanged. `value_groups` and `restrict_to` stay transitionally rejected.
> `dotnet test` is green (1520 passed, 1 skipped).
>
> **M4 Slice E — `value_groups` — is complete (D-104).** The grouping discretizer (§11.6) is
> executable under **all three** `unmatched` policies, and with it **every v1 discretizer kind is
> executable**: the D-070 deferred-kind set is empty, so `DiscretizerKindNotYetSupported` retires
> with its last owner and an unrecognized kind spelling is an ordinary `SpecFieldInvalid` (tier 3).
> `value_groups` is a **value-bin** discretizer whose universe is its *groups* — `declared_domain`
> and `value_labels` are both dormant under it (D-055/D-049), and it is string-fixing (D-061).
> Matching is pinned rather than incidental: explicit values compare **ordinally**, the regex is
> compiled **once** per group as **culture-invariant, partial (unanchored), case-sensitive unless
> the author writes `(?i)`**, with `InfiniteMatchTimeout` passed **explicitly** — the overloads
> that omit it inherit the host's ambient `REGEX_DEFAULT_MATCH_TIMEOUT`, which would make the same
> spec host-dependent and fail on the exception channel (P-7/P-14); a group matches on values
> **OR** pattern, and
> among groups the **first declared match wins** — which is why the `groups` array is planned-order
> in the TOML, the document, and the §14 bytes alike. **Authored presence survives** (G-11): an
> omitted `values` and an authored `values = []` are distinct all the way to the fingerprint, so
> two such specs share a `schema_fingerprint` but carry different output fingerprints — D-094's
> one-directional guarantee made concrete. `unmatched = "passthrough"` is the one data-dependent
> mode: it resolves to the `CalibrationPending` carrier and the Calibrate phase discovers one bin
> per distinct ungrouped raw value in **first-observation (raw input) order** (§17 r3), warning
> `ValueGroupsPassthroughDataDependent` whenever the mode runs — **zero discoveries included** —
> and retaining an empty outcome as the legitimate completeness marker. Discovery is set-based, so
> it is **discovery-class, not count-sensitive**: no quantile accumulator, no spill/merge, no
> subject-local dedup, no budget share — bounded by the attribute vocabulary (P-16). It reads the
> **raw pass only** for both triple orderings and never triggers the grouped second pass alone;
> when a count-sensitive attribute forces that pass, pass-through is fed from the raw one only
> (D-103), never twice, never a third pass — proven by enumeration counts, not by equal bins.
> Calibration and emit share the **same** Core matcher, so they cannot drift about what "unmatched"
> means. Ordinal over groups requires an explicit full permutation of the **group labels**
> (including `Other`) — strings have no natural order to derive — and `ordinal` + `passthrough` is
> rejected at spec validate. Three diagnostics landed (`ValueGroupsLabelDuplicate`,
> `OrdinalNotAllowedWithValueGroupsPassthrough` at spec validate;
> `ValueGroupsPassthroughDataDependent` at calibrate) and one retired, so the registry is **67**
> (65 + 3 − 1). The `value_groups` fingerprint encoding is golden-locked with independently
> computed SHA-256 vectors (D-094); every pre-Slice-E canonical byte, SHA vector, and all nine
> golden fixtures are unchanged. **`restrict_to` stays transitionally rejected at plan** — it is
> the only remaining M4 transition. `dotnet test` is green (1772 passed, 1 skipped).
>
> **M4 Slice F — `restrict_to` execution + emit observability — is complete, and with it
> M4 (D-105).** `restrict_to` **executes**: M4's last transitional code,
> `RestrictToNotImplementedV1`, is retired, so no M4 transition remains. Exact numeric
> entries land as the public `RestrictToNumber` — **parsed numeric identity**, not string
> spelling and not a single-point range, with **no tolerance**: `{ value = 30 }`,
> `{ value = 30.0 }`, and `{ value = 3e1 }` are one entry, and `-0` ≡ `0` via the seam's
> zero-canonicalization (G-6 — `CanonicalJson.AppendNumber`, every authored manual-cut
> byte, and `fp_format = 1` all untouched). Matching is **existential over formed
> objects** (OR within an attribute, AND across them; missing and absent-predicate match
> nothing; ordinal strings, locale-parsed numbers, half-open ranges, `{}` = any usable
> numeric) through **one shared matcher** on all four emit paths, so wide streaming, wide
> `dedupe`, and both triple orderings cannot drift. Restrictions **filter objects, not
> observations**: a survivor keeps *all* its crosses. **G-2 sequencing** is now pinned —
> a non-surviving row is not an object, so it trips no `fail` duplicate and consumes no
> `keep` name; **`row_index` names stay input positions, never renumbered by filtering**;
> `dedupe` groups first and its `DuplicateObjectKey` Info is **pre-filter**; triple
> decides at group close. **D-097** ownership is exact: a filter-only attribute's
> unparseable numeric is its *only* diagnostic owner (policy severity), while an
> included-and-restricted attribute's stays with the classification pass — at-most-once
> per observation per attribute per pass. The three §16.4 warnings registered since M2
> gained their sites — `NoObjectsEmitted`, `ObjectHasNoCrosses`, `AttributeHasNoCrosses`
> (aggregated, bounded samples, single-counted through the `.cxt` replay, and suppressed
> on any **invalid** run — G-12's own predicate: a structural/storage halt that truncates
> the stream, or a `fail`-policy abort that reads to completion but invalidates the
> operation) — and empty columns after filtering are **expected**, since §7 fixes the
> vocabulary before objects are selected. The canonical `restrictions` container joins
> **both** output fingerprints, carrying `unknown_value_policy` on every object (G-9 —
> a filter-only `fail` and `warn` spec must not hash alike) and sorting by
> **UTF-16-ordinal** canonical JSON before UTF-8 encoding (G-10), golden-locked with an
> independently computed SHA-256; restriction-free specs keep every prior byte and hash.
> `.bed` migration is **type-directed** (D-091): only a finite token on v2 type `o`
> becomes a number, under `binding.locale`; an invalid locale reinterprets nothing and
> mints no migrate diagnostic. **G-12** is now normative (§16.2/§18.1): a run's artifact
> is valid only if its diagnostics carry no Error/Fatal — **the caller must discard it**
> otherwise. A structural/storage halt truncates both `.cxt` passes identically (the
> name-sequence invariant cannot catch it); a `fail`-policy abort instead reads to
> completion — the aggregated diagnostic needs the whole population — so it leaves a
> **complete but invalid** artifact rather than a truncated one; either way `.dat` bytes
> precede any later Error. Writers stay dumb, and transactional publication remains M7. Four diagnostics added, one renamed (`RestrictToOnNumericRequiresRange` →
> `RestrictToNumericEntryRequired`, **no alias**), one retired — registry **70**
> (67 + 4 − 1). §19.4 is executable end-to-end. Every pre-Slice-F fingerprint byte and
> SHA vector, and all nine golden fixtures, are unchanged. `dotnet test` is green
> (2004 passed, 1 skipped).
>
> **M4 is complete. M5 (discovery / `probe`) is next** — no M5 work has started.
>
> **The pre-M5 Discovery audit has landed (docs-only, D-106…D-113).** A review pass settled
> the `probe` contract before any M5 code: the caller-selected shape with no structural/type
> inference and universal `identity` + `nominal` (D-106); the draft naming/binding matrix,
> validity guarantee, and content inventory (D-107); the 100,000 retention limit with
> strictly-greater truncation and prefix + `include` recovery (D-108); the general unbound
> source session and adapter/engine split (D-109); probe boundedness (D-110); the diagnostic
> governance — five future codes plus two phase widenings (D-111); determinism/cancellation
> and the verification suite (D-112); and canonical-writer multiline wrapping (D-113). It
> updated `bedrock-spec-v1.md` (new §7.1 plus §2/§5.1/§14/§16.4/§17), `decisions.md`,
> `roadmap.md`, and `lineage.md` in place, and corrected the legacy value-retention history
> (the backend retained up to 100,000 distinct values per attribute; the UI displayed only the
> first 100). This landing is **docs-only** — no production code,
> tests, fixtures, enum members (the registry stays **70**; the five `probe` codes are
> future, → **75** at M5), or output/fingerprint bytes changed.
>
> **M5 — Discovery / `probe` — is complete (Slices A–D).** `probe` produces an editable draft
> spec from raw data in **both** shapes: deterministic over the record sequence, immediately
> usable (reread → resolve → convert the same source), from **one** cleaned data pass with no
> grouped or count-sensitive pass, and correct end-to-end on the `mini-*` fixtures in both
> shapes. The whole milestone realizes D-106…D-113 with **no** new decision entry.
>
> **Slice A** landed D-113's canonical-writer multiline `declared_domain` wrapping — a private,
> byte-pinned **100-code-unit** cutoff measured over the complete escaped line, applying to
> `[[attribute]]` and `[[template]]` alike and to no other array — with zero edits to any
> pre-existing expectation. **Slice B** landed the D-109 unbound source seam: `ISourceSession` /
> `IWideSourceSession` / `ITripleSourceSession` over cleaned records, the typed
> `SourceReadException` channel, per-read triple roles that never enter session identity or
> `Bind`, the `CreateWide`/`CreateTriple` defaults factories, the Core-hoisted
> `ObjectNameValidity.IsUsable` that Conversion now delegates to, and the
> **both-shape header-tolerant open** that finally makes §10.2's duplicate/blank-header semantics
> reachable (behaviour-neutral for unique headers, header cells never
> missing-normalized). **Slice C** landed the `FcaBedrock.Discovery` package as a complete wide
> vertical: `Prober.ProbeAsync`, `ProbeOptions` (retention limit **100,000** plus the three D-110
> aggregate guards at **10,000 / 2,000,000 / 50,000,000**, logical accounting only), the wide
> naming/binding matrix, retention with prefix + `include` recovery, and the **five** probe
> diagnostics with live wide sites — registry **70 → 75**.
>
> **Slice D** completes M5 with the triple vertical. `Prober.ProbeTripleAsync` reads the rows
> once through the resolved roles, after the **binding-only role-map preflight**: Discovery
> builds the exact `[binding]` the draft would author and hands it to `SpecResolver` **before a
> single row is read**, so an invalid role map fails with the resolver's own §5.3 diagnostics
> **forwarded unchanged** — single ownership by construction (D-067), no re-validation in
> Discovery, no §16.4 cell moved, no new code. Predicates are discovered in **first-appearance
> order**; a §10.1-unusable predicate keeps its **exact** selector and takes the
> `predicate_<n>` fallback (with the same deterministic ladder wide uses when a real predicate
> already spells that name), and every adjustment rides one aggregated warning. The draft
> **preserves the caller's addressing mode verbatim** — all-index stays all-index, all-name stays
> all-name, an omitted map is authored explicitly as 0/1/2 — while the resolved indices remain
> read machinery only; `ordering` is authored as selected and `subject_grouped` is never
> inferred from observed contiguity. The two widened structural codes gained their **probe-phase
> sites**: `ObjectKeyValueInvalid` for subject usability under **both** orderings and
> `TripleSubjectNotContiguous` under an explicitly selected `subject_grouped` (judged over every
> valid-subject row, including ones whose predicate probe ignores), both asserted **equal to the
> calibrator's own diagnostics** over the same source rather than merely similar. Slice D adds
> **no enum member** — the registry stays **75** — and the complete public Discovery surface is
> still exactly `Prober` (two methods) and `ProbeOptions`. Production Discovery references only
> Sources, Spec, Core, and Diagnostics, performs no I/O, and touches `System.IO` solely to
> classify `IOException` / `InvalidDataException` crossing the session seam.
> Every golden `.cxt`/`.dat`, pinned fingerprint and SHA vector, fixture byte, and pre-existing
> canonical-TOML expectation is unchanged. `dotnet test` is green (2593 tests: 2592 passed, one
> platform-gated confidentiality test skipped off its OS).
>
> **M6 (templates + matchers) is next** — no M6 work has started.
>
> **The pre-M6 contract is settled (docs-only, D-114…D-119).** A pressure test over
> the M6 surface (2026-07-19), adjudicated with the operator and Codex, closed every
> open question before any M6 code: template/matcher resolution — field-wise layering
> in declaration order, presence-based override, whole-value compounds, authored
> provenance for winning template fields, and the closed flat template surface with no
> nesting (D-114); the two matcher selectors — whole-logical-name `name_regex` and the
> inclusive zero-based resolved `source_index_range`, exactly one per matcher (D-115);
> the diagnostic matrix — seven new permanent conditions by phase/severity/granularity,
> per-effective-attribute granularity, the deterministic family ordering, and the
> transitional retirement schedule (D-116); naming — the closed five-placeholder
> grammar, single-pass rendering, and the authored/rendered validity rules (D-117); the
> application site and architecture — resolver-seam application, template/matcher-free
> Core, planner-owned naming (D-118); and the **restated M6 exit** plus the verification
> floor (D-119). It updated `bedrock-spec-v1.md` (§6/§9.1/§9.2/§10.1/§10.7/§12.3/§13/
> §16.4), `decisions.md`, and `roadmap.md` in place. This landing is **docs-only** — no
> production code, tests, fixtures, or enum members (the registry stays **75**; the M6
> conditions' public names and their §16.4 rows land with the M6 implementation-surface
> review), and no output or fingerprint bytes changed.
>
> **M6 Slice A — naming: carriers, grammar, rendering, the plan guard, and parse
> ordering — is complete (D-120).** `display_name` and `formal_attribute_format`
> **execute**: they are carried, parse-validated, and canonically written on
> `[[attribute]]`, `[[template]]`, and `[defaults]` (additive non-positional `init`
> properties, so no existing constructor or deconstruction moved — the D-087 pattern),
> they compose correctly across `extends` (the per-field `MergeDefaults` carries the
> format explicitly, where whole-section attribute/template replacement carries the new
> properties for free), and they drive rendered names on attributes and `[defaults]`.
> Template-supplied naming is **carried and validated but inert** — template/matcher
> *use* stays rejected until Slice B, so `TemplateMatcherNotImplementedV1` is untouched.
> The §10.7 grammar has one owner, the new public **`NameFormat`** in Core (closed
> case-sensitive five-placeholder set with the `{column}` alias, `{{`/`}}` escaping, one
> left-to-right parse into tokens that rendering walks — so "substituted text is never
> rescanned" is structural, not a property two paths maintain), consumed by both the Spec
> reader and the planner. An explicit format is a **total override**, the missing column
> included (`{value}` = the literal `missing`), and `{value}` resolves through live
> `value_labels` — including the labelled dichotomic `true_value` and D-092 numeric
> identities. The permanent plan-time backstop **`FormalAttributeNameInvalid`** (Error,
> one aggregated diagnostic per affected logical attribute, count + ≤3 quoted/escaped
> samples in render order with a `(+N more)` tail) fails the **shared** plan, blocking
> `.dat` as well as `.cxt`; it is load-bearing beyond the M6 surface, since CR/LF can
> arrive from raw values, calibrated domains, and `value_labels` on the **default** path
> too (the empty-name half is format-reachable only — a default-rendered `f1-` is valid).
> Exporters never sanitize (P-15). Semantic parse diagnostics gained a **centralized
> source-position ordering** at the `SpecReader` boundary — `(Line, Column, emission
> ordinal)`, total by construction, span-less first — leaving the terminal
> syntax-vs-semantic phasing untouched. The closed D-075 deferred-**key** sets retired
> with their carriers (and `TomlTableCursor.Finish`'s now-dead parameter with them), so
> `SpecSurfaceNotYetSupported` narrows to its one remaining owner, the value-level
> `value_type = "date"`. One diagnostic added, none retired — registry **75 → 76**. No
> fingerprint-encoder or `fp_format` change: naming reaches identity only through the
> existing `rendered_names` input, so an effect-changing format moves
> `cxt_output_fingerprint` and `.cxt` bytes **only**, while an unreferenced
> `display_name` and a render-identical format are fully neutral. Every pre-M6 canonical
> byte, SHA vector, pinned fingerprint, and all nine golden fixtures are unchanged — no
> existing spec authors the naming surface. `dotnet test` is green (2720 tests: 2719
> passed, one platform-gated confidentiality test skipped off its OS).
>
> **M6 Slice B — template/matcher application at the resolver seam — is complete
> (D-121).** Templates and matchers **execute**: the §9.2 five-tier, field-wise,
> presence-based merge runs entirely inside `SpecResolver.Resolve`, after composition
> and source addressing and before effective-attribute validation, so an equivalent
> flat, materialized, template/matcher-authored, or `extends`-composed spec resolves to
> the same attributes, plan, three fingerprints, and byte-identical `.cxt`/`.dat`.
> Application is a document→document fold producing an **effective `AttributeSection`**
> — the same type a flat spec produces — which is what makes provenance automatic
> rather than a parallel model: a template-won `boundary` simply *is* a non-null
> authored field, so `OrdinalBoundaryIncompatibleWithCuts` fires on it exactly as on an
> explicit one, while `[defaults]` stays **below** the fold and keeps defaulted
> provenance. Both selectors execute through **one** schema-bounded addressing pass:
> `SourceAddressing` resolves every attribute's source once, so a
> `source_index_range` can select on the resolved physical index while its
> `SourceBindingInvalid` is still emitted **exactly once**, in the attribute's ordinary
> slot — the refactor is behaviour-neutral for every template-free spec. Value typing
> stays **post**-application (a template-supplied numeric-cut discretizer types a
> bare-string `restrict_to` and trips `RestrictToNumericEntryRequired`, matching the
> flat form), and `name_regex` compiles through one wrapped whole-name construction
> (`\A(?:…)\z`, `CultureInvariant`, explicit `InfiniteMatchTimeout`) shared by the parse
> gate and evaluation, so a pattern cannot parse and then match differently. Resolve
> diagnostics are assembled in **five families** — identity → [binding prefix] →
> matcher references/shape → attribute references → effective validation → warnings —
> with family 1 ahead of the shape gate; the two matcher Warnings come from **one**
> declaration-order traversal, so they interleave by matcher rather than grouping by
> code. Six diagnostics added, one retired — registry **76 → 81**, the M6-exit count —
> and with `TemplateMatcherNotImplementedV1` gone **no M6 transitional remains**
> (`SpecSurfaceNotYetSupported` survives with its one non-M6 owner, `value_type =
> "date"`). Core gains nothing and is held template/matcher-free by a new ArchUnitNET
> name rule; no fingerprint-encoder, `fp_format`, exporter, or production-reference
> change. Every v2 fixture, golden `.cxt`/`.dat`, canonical byte, and SHA pin is
> unchanged. `dotnet test` is green (2822 tests: 2821 passed, one platform-gated
> confidentiality test skipped off its OS).
>
> **M6 Slice C — the one-file Internet-Ads exit workflow — is complete, and with it
> M6 (D-119).** The accepted exit is demonstrated end-to-end by a new behaviour-organized
> `InternetAdsExitTests` suite in `FcaBedrock.Golden.Tests`, over a **deterministic,
> synthetic corpus that mirrors the complete raw `ad.data` layout** — 1,559 headerless
> columns (numeric `height`/`width`/`aratio` at 0–2 with representative `?` missing cells;
> binary `local` at 3; **exactly 1,554** binary term columns at 4–1557 with a few `?`; the
> `ad.`/`nonad.` class at 1558) — with **no UCI data row copied** and no clock, random,
> culture, or platform-newline input; the generated row width and index partition are
> asserted from the re-split text, and every exit spec authors the pinned Kushmerick/UCI
> `[provenance]` (`source_url` + `notes`), fingerprint-inert but asserted present. One
> self-contained **declarative** spec — the full 1,559-attribute inventory plus one
> `term_flag` template and one `source_index_range = [4, 1557]` matcher — resolves,
> calibrates, plans, and converts with no Error/Fatal and **no matcher warning**; the
> matcher configures **exactly** the 1,554 terms (indexes 0–3 and 1558 keep their own
> configuration, nothing is synthesized from source width), proven load-bearing because
> removing it fails the bare terms with `AttributeScalingMissing`. The declarative form
> **equals its independently built materialized twin** on effective attributes, the full
> plan, all three fingerprints, and byte-identical `.cxt`/`.dat`; a one-term change makes
> the same projections diverge (the sensitivity anchor). The **uncurated probe-style** form
> proves all three template-authored fields lose on every selected term (effective nominal +
> observed domain, not the template's dichotomic), emits **exactly one
> `MatcherFullyShadowed`**, and is byte/fingerprint/plan/effective-attribute neutral versus
> the same document without template/matcher; the two **de-shadow** curations (matcher-won
> versus materialized) converge on identical plans, fingerprints, and bytes. A **real
> `Prober.ProbeAsync`** draft — verified to carry 1,559 string identity+nominal attributes
> with their complete observed domains — is curated (template/matcher added, the three
> fields stripped from 4–1557, provenance attached) and carried through the actual
> resolve → calibrate → plan → emit → native `.cxt`/`.dat` export as one continuous
> document; the whole workflow is repeatable, with byte-identical artifacts and identical
> ordered diagnostics. `FcaBedrock.Golden.Tests` gains a single **test-only**
> `ProjectReference` to `FcaBedrock.Discovery` to drive that probe; **no production
> reference changed**, and Discovery still references only Sources, Spec, Core, and
> Diagnostics. Registry stays **81**, no M6 transitional exists
> (`TemplateMatcherNotImplementedV1` absent, `SpecSurfaceNotYetSupported` date-only), and
> every v2 fixture, golden `.cxt`/`.dat`, canonical byte, SHA pin, and architecture rule is
> unchanged. Slice C adds **no decision entry** (D-119/D-120/D-121 already govern) and makes
> no spec or `AGENTS.md` edit; the only production-file edits are comment-only retirement of
> historical review-traceability identifiers. `dotnet test` is green (**2835 tests: 2834
> passed, 0 failed, one platform-gated confidentiality test —
> `SpoolConfidentialityTests.CreateWorkspace_OnUnix_SetsMode700` — skipped off its OS**).
>
> **M7 is complete, and with it M1–M7.** The CLI landed across **eleven slices, A–K
> (master-plan steps S1–S11)**, under **D-122** — the adjudicated pre-implementation
> contract, 33 findings ruled and consented (audit: Fable; independent review: Codex;
> adjudication: Constantinos Orphanides, 2026-07-22), plus the docs-plan review rulings
> (authored-empty domains, the exact stderr grammar, the `[[run.calibrations]]` manifest
> shape) — and **D-123**, the implementation decisions. The `fcabedrock` global tool
> ships all eight commands (`convert`, `validate`, `plan`, `stats`, `calibrate`, `probe`,
> `migrate`, `fingerprint`) with the process/publication/manifest/freeze contracts,
> filesystem identity, injected signals, the input-stability gate, and the argv-boundary
> exit floor; spec §§3/7/8/10/11/13/14/15/16/17/18 carry the normative corrections. The
> diagnostic registry is **82** (`OutputCxtSizeAdvisory` joined at Slice B) and **no M7
> transitional diagnostic remains**. **Slice K** closes the milestone with the tool
> package itself: a purpose-built packed README and `PackageReadmeFile`, five always-on
> package assertions, and one environment-gated installed-tool smoke
> (`FCABEDROCK_TOOL_SMOKE=1`) that installs the packed tool from a local `<clear />` feed
> under an isolated `--tool-path`, converts, checks the manifest, and uninstalls.
> `FcaBedrock.Cli.Tests` measures **1,263 tests: 1,257 passed, 0 failed, 6 skipped** with
> the gate unset, and **1,258 passed, 5 skipped** with it set; the full-solution figures,
> the complete 19-row exit checklist, and the skip inventory are in the **M7 exit block**
> below. **M8 (the first scaling/benchmark pass) is next**, then M9.

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
re-deriving M4 semantics. (Resolved at M7: D-122/§15's `[[run.calibrations]]` records
all four retained outcome kinds completely.)

**Exit — all met (M4 complete, D-098…D-105):**

- [x] auto-binning calibrates deterministically and byte-identically to its frozen
      form (D-102/D-103; the shared `NumericCutBins` engine makes it structural);
- [x] cuts captured in the calibrated state the manifest reads (D-093/D-102/D-103;
      manifest *serialization* is M7 by design);
- [x] `value_groups` (incl. passthrough) converts (D-104);
- [x] observed-domain calibration and `unknown_value_policy = "include"` resolve
      (D-098; the include emit crash closed with it);
- [x] `restrict_to` filters — existential, exact-numeric and range entries (D-105).

No M4 transitional diagnostic or guard remains: `ObservedDomainCalibrationNotImplementedV1`
(Slice A), `DiscretizerKindNotYetSupported` (Slice E), and `RestrictToNotImplementedV1`
(Slice F) are all retired. Later milestones' transitions are untouched.

### M5 — Discovery / `probe`

An **optional** draft-spec generation operation, **outside** the convert pipeline
(D-003/D-036/D-106; spec §7.1). The **caller selects the shape** (`wide` | `triple`) and
read settings; probe infers nothing structural — no delimiter, header, shape, or type
detection. It reads the source's cleaned records **exactly once, in input order** (one
data-record pass, set-based and idempotent over cleaned values) and authors every discovered
attribute as string-valued `identity` + `nominal` (D-106). Guided, *advisory* type detection
("this looks continuous — add ranges?") is recognized **future Discovery UX**, unassigned to
a milestone — not part of the conservative M5 base.

**Retention.** A per-attribute distinct-value retention **`limit`** (a probe option;
default **100,000**) with **strictly-greater-than** truncation: a truncated attribute authors
its retained prefix as `declared_domain` plus `unknown_value_policy = "include"` — so
converting the draft recovers the complete schema (spec §10.6/§17) — and a deterministic
marker in its `description`; `[provenance].notes` always records the effective limit and the
truncated-attribute count, **including zero** (D-108). M5 has **no presentation/display
limit** — this corrects the earlier "v2's 100-distinct-value cap" note: the legacy backend
retained up to 100,000 distinct values per attribute, while the UI displayed only the first
100 (lineage §1; the exact legacy identifiers are recorded as audit evidence in decisions.md
D-108). Three deterministic aggregate guards bound a probe
as a whole (defaults pinned at implementation review), breaching to `ProbeLimitExceeded` with
no draft; the accounting is logical, never machine memory (D-110).

**Draft validity** (D-107): a successful draft contains at least one attribute, rereads under
the strict reader, resolves against the source schema, and **converts the same source under
the same settings** with no Error/Fatal (warnings and degenerate contexts allowed). Probe
returns `Diagnosed<SpecDocument>` over a **general unbound source session** (schema +
normalized records + cancellation), which the CSV wide/triple adapters implement; the
**caller** serializes via `SpecWriter` and owns file output (D-109). The canonical writer
gains deterministic multiline wrapping for long top-level `declared_domain` arrays (D-113).
Five diagnostics land with
their sites (`ProbeSourceReadFailed`, `ProbeNoAttributesDiscovered`,
`ProbeAttributeNameAdjusted`, `ProbeDomainTruncated`, `ProbeLimitExceeded`), and
`TripleSubjectNotContiguous` / `ObjectKeyValueInvalid` widen to `probe/calibrate/emit` —
registry **70 → 75** (D-111). The public Discovery API surface is fixed at an
implementation-time P-4 review.

**Exit:** `probe` produces an editable draft spec from raw data — deterministic over the
record sequence (D-112), immediately usable (reread → resolve → convert the same source), one
cleaned data pass with no grouped/count-sensitive pass, and correct end-to-end on the mini-*
fixtures.

### M6 — Templates + matchers

The bulk-edit model in `Spec`. Resolution precedence is the **five tiers of spec
§9.2** — built-ins < `[defaults]` < matching templates in declaration order < the
attribute's directly named `template` < explicit attribute fields — with matching
templates layering **field-wise** within the matcher tier (D-114); the spec is
normative, and this section does not restate it. Replaces v2 "Repeat-To". Also lands the
**naming-fidelity carriers** deferred from M2 — `display_name` and
`formal_attribute_format` on attributes/templates (and `[defaults]
.formal_attribute_format`), until now recognized-but-rejected at read
(`SpecSurfaceNotYetSupported`, §16.4).

The **pre-M6 contract audit** (D-114…D-119, docs-only) settled the milestone's
semantics before any code: the merge algebra and closed flat template surface
(D-114), both selector contracts (D-115), the diagnostic matrix and retirement
schedule (D-116), the naming grammar and rendered-name validity (D-117), and the
resolver-seam application site with a template/matcher-free Core (D-118).

**Exit:** a **single self-contained** Internet-Ads-style spec — the complete
generated attribute inventory (roughly 1,500 repetitive boolean feature columns)
plus one template/matcher expressing the repeated boolean scaling policy over
those already-declared attributes — converts end-to-end. The exit measures
**elimination of repetitive per-attribute manual curation, not total file
length**; the earlier "<50 lines of TOML" gate is **withdrawn** as unmeetable
(§2 requires an `[[attribute]]` per logical attribute, and matchers configure
rather than create — D-119). Matchers configure declared attributes only.
Equivalent declarative (template/matcher) and materialized (explicit
per-attribute) forms must resolve to identical plans, fingerprints, and bytes.
Resolution precedence is tested per the §9.2 contract, and `display_name` /
`formal_attribute_format` round-trip and drive rendered names. The full
completion gate — precedence tier pairs, representation equivalence, selector
semantics, diagnostic identity/ordering, exact rendered-name tables across all
five placeholders, the fingerprint neutrality/change matrix, round-trip,
unchanged existing locks, the one-file Ads workflow, and the architecture
boundary — is the D-119 verification floor, each item landing with its slice
(P-7).

### M7 — CLI

**Eight commands:** `convert`, `validate`, `plan` (dry-run plan inspection), `stats`
(context statistics without writing), `calibrate`, `probe` (draft-spec generation,
§7.1), `migrate` (.bed → TOML), `fingerprint`. M5 supplies the `probe` API/library
behavior; **M7 exposes it on the CLI** (and M9 in the UI) — the milestone adds no new
discovery semantics, only a command surface over M5's.

**The shared contract is settled (D-122; normative text in spec §§3/7/7.1/8/10.3/10.6/
11.6/13/14/15/16/17/18.1).** SPEC and DATA are positional, everything else named;
`--help`/`--version` on stdout, exit 0. Exit codes are 0/1/2/3/4, with warnings
staying 0 and host/environment failures CLI-owned and code-less. Diagnostics render as
one deterministic sparse-labelled stderr line each, results on stdout; no stdin source;
writing commands require an explicit `--out` and refuse existing targets without
`--force`. Publication stages, commits per file atomically, and rolls back best-effort
— a **default-on** `BASE.manifest.toml` per run with `[[run.outputs]]` and
`[[run.calibrations]]` publishes last as the public commit marker, while
`--no-manifest` suppresses the audit sidecar only and implementation-private
transaction state preserves incomplete-run detection. Every complete pass hashes its
raw input inline (the input-stability gate). `--temp-dir` is exposed on `convert`,
`plan`, `stats`, `calibrate`, and `fingerprint`; the run coordinator stays
CLI-internal.

**Committed follow-ups and non-goals.** Color and progress are **committed** later
features and machine-readable diagnostics an anticipated later requirement — M7 ships
none of them but **centralizes** presentation and progress observation so they land
without run-orchestration refactoring. Sampling, compressed artifacts, and arbitrary
output-setting overrides are **excluded** (unknown flags are usage errors); each
returns only through its own contract decision, and `--v2-compat` remains the sole
settled conversion override. The grouping memory budget and merge fan-in stay internal
pending M8 measurement.

**Distribution.** A .NET global tool validated on x64, installed via the documented
`winget install Microsoft.DotNet.SDK.10` then `dotnet tool install --global
FcaBedrock.Cli` route (with equivalent platform guidance) — **explicitly temporary
technical-preview distribution**. A standalone/self-contained route is committed for
the later public release. The **M7 implementation/distribution landing documented the
exact global-tool route in user documentation before M7 exit** (exit item 18), and
public-release docs must lead with the standalone route.

**Presence/emptiness obligations carried into the M7 master plan (all landed).** Three
presence-versus-emptiness obligations were settled contract and not yet implemented at
M7 planning time; they were inputs the M7 implementation master plan carried, not work
the preceding safe docs/tests hardening slice performed. All three landed — the
evidence families recorded below are where:

- Preserve **omitted versus authored-empty `declared_domain`** through calibration
  requirement, outcome selection, fully-frozen eligibility, freeze rewriting, and
  manifest generation. An authored `[]` is complete; an **omitted** domain requests
  `ObservedDomain` calibration (D-122 part 15).
- Enforce **ordinal full-permutation semantics for a complete empty string-bin
  universe**: an omitted `scale.order` is `OrdinalOrderMissing`, while `order = []` is
  the valid empty permutation. This corrects the current identity-only empty-domain
  validation skip.
- Preserve **legal empty `ObservedDomain`, `IncludeAdditions`, and `PassthroughBins`
  outcomes** through freeze and manifest serialization as **explicit empty arrays**.
  Empty **calibrated cuts** remain unsuccessful (`bins >= 2` makes `[]` always the
  wrong size).

The M7 implementation master plan assigned each item to an implementation slice and to
acceptance tests.

**Exit — all met (M7 complete, D-122/D-123; slices A–K / S1–S11):**

- [x] **1.** every command exercised through argv — `Cli.Tests` green end to end, with the
      eight-handler command-table lock
      (`RunPipelineTests.CommandTable_ThenExactlyTheImplementedCommandsHaveAHandler`);
- [x] **2.** all nine active goldens reproduced through the real CLI path —
      `GoldenArgvFloorTests`, with `FcaBedrock.Golden.Tests` at 85;
- [x] **3.** manifest byte locks — all four `[[run.calibrations]]` kinds, empty arrays,
      Unicode/control escaping, long values and D-113 wrapping, argv, timestamps, and both
      the no-chain and multilevel-chain `[[run.spec_files]]` paths —
      `RunManifestWriterTests`, `ConvertManifestTests`;
- [x] **4.** exit and diagnostic byte locks plus rendering-grammar locks for every
      location-field combination — `DiagnosticRendererTests`;
- [x] **5.** publication, overwrite and collision cases — `PublicationTests`,
      `SingleFilePublicationTests`, `PublicationOwnershipTests`,
      `PublicationFamilyBoundaryTests`;
- [x] **6.** rollback and incomplete-run recovery cases — `PublicationRecoveryTests`,
      `PublicationCrashMatrixTests`, `TransactionRecordTests`;
- [x] **7.** `calibrate` freeze and idempotence over every freeze mapping, the
      empty-outcome `[]` freeze, and the authored-empty `[]` combinations with
      `unknown_value_policy = "include"` and `missing_policy = "as_attribute"` —
      `SpecFreezerTests`, `CalibrateCommandTests`;
- [x] **8.** `fingerprint` freeze, `--write` and idempotence — `FingerprintCommandTests`,
      `FingerprintWriteTests`;
- [x] **9.** `probe`/`migrate` grammars — triple role index-mode and name-mode successes
      plus mixed-mode and partial-role usage failures, and the wide object-key conditional
      grammar — `ProbeCommandTests`, `MigrateCommandTests`;
- [x] **10.** advisory threshold cases — `Export.Tests.CxtSizeAdvisoryTests`, with
      `OutputCxtSizeAdvisory` live and the registry at **82**;
- [x] **11.** extends identity and cycle cases — `FileIdentityTests`,
      `FileSpecTextSourceTests` (their symbolic-link twins are named skips on a host
      without the privilege, listed below);
- [x] **12.** signal cases — `SignalSourceTests` plus the injected `ISignalSource` cases
      across the command suites;
- [x] **13.** input-stability mismatch cases — `RunPipelineTests` (D-123 point 5),
      `InputHashTests`, `ConvertHashingTests`;
- [x] **14.** a global-tool pack/install/uninstall/`--version` smoke in an isolated tool
      path — `ToolSmokeTests` (environment-gated) plus `ToolPackTests` (always on) —
      **Slice K**;
- [x] **15.** representative excluded flags (`--sample`, `--gzip`, machine/color/progress
      spellings) returning usage exit 2 — `CommandLineParserTests`, `UsageTextTests`;
- [x] **16.** every existing golden, canonical-TOML, SHA, registry and architecture lock
      still green — G3 (Architecture 10), G4 (Golden 85, including the second registry-82
      lock), G5 (Diagnostics 29), G6 (Spec 858), *and* G7, the only gate that also runs the
      other lock owners: `Core.Tests` (`FingerprintCalculatorTests`,
      `RestrictionFingerprintTests`), `Export.Tests` (writer bytes + `CxtSizeAdvisoryTests`),
      `Conversion.Tests`, `Sources.Tests`, `Discovery.Tests`, `Cli.Tests`;
- [x] **17.** the normal suite stays fast — G2 (`Cli.Tests`) **42.2 s** wall, 40.2 s run;
      G7 (whole solution) **51.2 s** wall, 46.0 s run;
- [x] **18.** the exact global-tool route documented in user documentation before exit —
      `README.md` `## Installation` plus the packed `src/FcaBedrock.Cli/README.md` —
      **Slice K**;
- [x] **19.** M7 status retired everywhere it was claimed — this block, plus `README.md`
      `## Status`, `AGENTS.md` `## Current status`, this milestone's current-position
      block, and the two stale spec tails (§10.3, §16.4) — **Slice K**.

**Measured at exit** (Windows 11, .NET SDK 10.0.302, x64): `FcaBedrock.Cli.Tests`
**1,263 / 1,257 passed / 0 failed / 6 skipped** with `FCABEDROCK_TOOL_SMOKE` unset, and
**1,263 / 1,258 / 0 / 5** with it set to `1`; the whole solution
**4,274 / 4,267 / 0 / 7** unset. The six CLI skips are the five platform/filesystem cases
— `FileIdentityTests.KeyFor_WhenASymbolicLinkAliasesTheFile_ThenTheKeysUnify`, its
`…AndIdentityIsUnavailable_ThenTheFallbackStillUnifiesThem` twin,
`FileSpecTextSourceTests.Compose_WhenACycleIsSpelledThroughASymbolicLink_ThenItIsStillDetected`,
`SingleFilePublicationTests.Publish_WhenTheTargetIsASymbolicLinkAliasOfTheInput_ThenItIsRefusedEvenWithForce`,
`PublicationTests.Publication_WhenAStageSurvivesAsResidue_ThenItIsReadableOnlyByItsOwner`
— plus the gated
`ToolSmokeTests.InstalledTool_WhenTheSmokeGateIsSet_ThenItPacksInstallsConvertsAndUninstalls`;
the solution's seventh is
`Conversion.Tests.SpoolConfidentialityTests.CreateWorkspace_OnUnix_SetsMode700`.

**Slice K packaging note.** The CLI project sets `PackageReadmeFile` and packs a
purpose-built `src/FcaBedrock.Cli/README.md` to the package root (`None Update`, so the
SDK's default glob is not duplicated). `ToolPackTests` asserts the packed contents
directly — the exact eleven-assembly set, `DotnetToolSettings.xml`/`deps.json`/
`runtimeconfig.json`, the nuspec identity including a `repository commit` equal to the
checkout's actual `git rev-parse HEAD`, the readme element and its byte-for-byte equality
with the authored file, and one version across the file name, the nuspec and
`ToolVersion.Current`. Nothing pins whole-package bytes, the GUID-named core-properties
part, `_rels/.rels` or entry order, because those differ between two packs of identical
sources. The gated smoke installs that package from a local `<clear />` feed under an
isolated `--tool-path`, with no global install and no network or machine feed.

**Presence versus emptiness — three landed evidence families (D-122 part 15).**

1. A template- or matcher-supplied `declared_domain = []` stays authored-complete through
   every resolution tier —
   `TemplateApplicationTests.Apply_WhenAHigherTierAuthorsAnEmptyDomain_ThenItOverridesToAnAuthoredCompleteEmpty`,
   `SpecResolverTests.Resolve_WhenDeclaredDomainOmittedVersusAuthoredEmpty_ThenPresenceSurvives`,
   `CalibratedSpecTests.RequiresData_WhenAuthoredEmptyDomainIdentityUnderWarn_ThenFalse`,
   `CalibratedSpecTests.RequiresData_WhenAuthoredEmptyDomainIdentityUnderInclude_ThenTrue`,
   `CalibratedSpecTests.Create_WhenAuthoredEmptyDomainUnderWarn_ThenNoOutcomeNeededAndDomainStaysEmpty`,
   `CalibratedSpecTests.Create_WhenAuthoredEmptyDomainUnderInclude_ThenRequiresIncludeAdditionsNeverObserved`,
   `ConversionPlannerTests.Plan_WhenIdentityNominalOverAuthoredEmptyDomain_ThenZeroColumnsAndNoFormalAttributes`,
   `ConversionPlannerTests.Plan_WhenIdentityNominalOverAuthoredEmptyDomainWithAsAttribute_ThenOnlyMissingColumn`,
   `SpecFreezerTests.Freeze_WhenObservedDomainEmpty_ThenAuthoredEmptyArray`,
   `SpecFreezerTests.Freeze_WhenAuthoredEmptyDomainCombinations_ThenFrozenFullyFrozenAndPreserved`.
2. Real-argv `probe` over all-missing observations **omits** the key rather than authoring
   `[]` — `ProbeCommandTests.Probe_WhenEveryObservationIsMissing_ThenTheDraftOmitsDeclaredDomain`,
   with library parity at
   `ProbeShapeParityTests.Drafts_WhenAnAttributeIsAllMissing_ThenBothOmitTheDomain`.
3. Authored-empty × `include` × `as_attribute` carried through calibration, freeze, output,
   manifest, fingerprint, and native + v2 replay —
   `CalibrateCommandTests.Calibrate_WhenTheAuthoredEmptyDomainCombinesWithPolicies_ThenTheFrozenSpecIsFullyFrozenAndPreserved`,
   `CalibrateCommandTests.Calibrate_WhenTheAuthoredEmptyDomainIsAsAttribute_ThenTheMissingColumnSurvivesTheFreeze`,
   `SpecFreezerTests.Freeze_WhenAuthoredEmptyDomainCombinations_ThenFrozenFullyFrozenAndPreserved`,
   `RunManifestWriterTests.Write_WhenAllFourCalibrationKinds_ThenExactCanonicalDocument`,
   `RunManifestWriterTests.Write_WhenValuesAreEmptyOrShort_ThenInline`,
   `ConvertManifestTests.Manifest_WhenAnOutcomeDiscoveredNothing_ThenItIsAnExplicitEmptyArray`,
   `FingerprintCalculatorTests.BuildCxtOutputJson_WhenValuesOmittedVersusAuthoredEmpty_ThenTheBytesDiffer`,
   `FingerprintCalculatorTests.Fingerprints_WhenValuesOmittedVersusAuthoredEmpty_ThenSameSchemaButDifferentOutputHashes`.

Core is dogfoodable end-to-end without a UI, and `plan`/`validate` give a fast
spec-authoring loop. Large benchmarks and release validation stay M8.

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
release**, and so is the real-data acceptance run below: **each release candidate**
submitted for release acceptance must have all three UCI Adult (`External`) cases pass
on its final Windows x64 build against the pinned corpus, retained as evidence. That
obligation attaches to the candidate, not to routine CI, and it does not lapse when M8
closes. Note the split (as in the grouping-backend note below): M8 may tune the
buffer budget and fan-in, but the layout **safety constants are correctness inputs** —
they cannot be performance-tuned without revalidation.

**Calibration budgets.** M8 may tune the equal-frequency / percentile calibration
memory budget and benchmark its spill/aggregate algorithms, but **exact
bounded-memory calibration already exists at M4** (D-095) — boundedness is a
correctness input established there, never here.

**Probe boundedness.** Likewise, `probe`'s per-attribute retention limit and its three
aggregate guards are **correctness inputs established at M5** (D-110) — deterministic logical
accounting, never machine memory. M8 may tune the guard **defaults** against real 7.3M–73M
distributions, but never establishes probe boundedness or its determinism.

**M7 cost and packaging boundaries.** M8 also **measures** the cost of M7's inline
per-pass input-stability hashing (D-122; any later opt-out needs its own explicit
ruling and never activates by file size) and of manifest hashing; tunes the grouping
memory budget and merge fan-in and the probe-guard defaults (above); and owns the
**standalone/self-contained public-release packaging gate** together with the broader
platform/architecture validation that M7's x64 global-tool dogfood route
deliberately does not claim (D-122).

**Exit:** documented throughput/memory at target scale; no full-matrix
materialization.

**In progress (2026-09-06).** The benchmark suite is landing under **D-124**: one
internal BenchmarkDotNet 0.15.8 executable (`tests/FcaBedrock.Benchmarks`) plus a
tested corpus/oracle layer (`tests/FcaBedrock.Benchmarks.Tests`), with `Small` the
default selection and the `Working`, `Scale`, and `External` tiers reachable only by
naming their category. The evidence pack is `docs/benchmarks.md`; it records what has
been measured, on what hardware, under what rules, and — deliberately — what has not.

Landed: the W16 wide, T10 triple (both physical layouts), keyed-dedupe, Ads-width
(1,559 columns), long-text, and externally acquired UCI Adult corpora, each with an
independent oracle or, for real data, a stable baseline plus semantic assertions;
source-drain, calibration, pure-plan, emit-to-`.dat`, emit-to-`.cxt`,
probe (including the three limits straddled at their exact thresholds),
grouping-budget/fan-in, CLI-host convert, and genuine input/output hashing-wrapper
cases; the immutable v2 minis as external `.dat` and `.cxt` byte oracles;
per-iteration validation after disposal; runtime-**observed** retained-layout
witnesses in `Conversion.Tests`; a self-contained standalone distribution with its own
gated publish/execute/archive smoke; `eng/` packaging commands; and
`.github/workflows/ci.yml`.

**The first native gate ran, failed, and found two M7 defects (2026-09-08/09).** Run
[`34241484619`](https://github.com/trashr0x/fcabedrock/actions/runs/34241484619) at
`a09e302` executed on all five native targets — both optional ARM64 jobs included — and
passed setup, the architecture assertions, restore, the Release build and all 25
resident-layout witnesses, which had never before executed off Windows x64. All five then
failed at `Test (Release)`, skipping corpus preparation, the Small Dry smoke, both package
smokes, the self-contained publish/run/archive and the upload; **no artifact was
produced**, and the run remains failed evidence at that revision. Linux x64 and Linux ARM64
each failed nine publication cases plus one `ToolPackTests` case; macOS ARM64, Windows x64
and Windows ARM64 failed `ToolPackTests` alone. Every suite totalled 4,498 tests and **no
M8 case failed anywhere**. Both defects were pre-existing M7 release blockers in files
byte-identical to `main` — a publication ownership defect that let a recycled file
identifier authorize publishing or deleting a substituted object, and a package test that
pinned one NuGet producer's metadata leaf — and both are corrected under **D-125**. Because
that correction reaches the measured CLI-host interval, the CLI-host figures in
`docs/benchmarks.md` are **superseded and were never replaced by a controlled
re-measurement**; component measurements are unaffected, because publication is unreachable
from their timed paths.

**The native gate then passed, and the corrected build was characterized under D-126
(2026-09-09).** A second run at `91188455` failed at a macOS-only global-tool-smoke defect
— a `/var` versus `/private/var` path-spelling comparison in the test's own assertion,
reached for the first time because no earlier run had got that far — and remains failed
evidence at that revision. Run
[`34289256438`](https://github.com/trashr0x/fcabedrock/actions/runs/34289256438) at
`4216610b` then **passed on all five native targets** and produced all three required
self-contained archives (`win-x64`, `linux-x64`, `osx-arm64`), which are retained,
decompressed, inspected, and hash-verified against GitHub's own server-side digests. The
Windows x64 External/Adult three-case acceptance also passed at that revision against the
D-124-pinned entry (3,974,305 bytes, SHA-256 `5b00264637…c86603d`).

What the corrected build was measured to do, and what could not be measured, is **D-126**:

- **The incremental publication latency is inconclusive at the 5% bound.** A pre-registered
  four-form paired comparison ran in full and **failed its collective gate** —
  `CliHostConvertWideWorking` returned a one-sided 95% upper limit of **5.6809%** against
  5%, and `CliHostConvertBothWorking` failed control stability at **1.071382** against 1.05
  (the *unchanged* build moving 7.1% across its own six launches). The attempt is complete
  and **no retry is required for M8**. It is never neutrality, non-regression, equality,
  "probably below 5%", a speedup, or an exact publication/sidecar overhead, and no
  corrected-build elapsed figure is published as a result or converted into any rate,
  curve or old/new arithmetic.
- **Allocation and per-iteration output validation are complete at 15/15 CLI-host cases**
  (six from session E, nine from session F), including all three 73M cases at their real
  1-warmup/3-measured policy. No allocation investigation trigger fired on any row. D-125's
  "allocation is byte-identical" claim is **retracted**: the corrected build allocates
  +2,880 to +9,416 bytes more per published run within one machine state.
- **The corrected-command resource traces are complete at 6/6** — the two wide declared
  traces from session F (which spool nothing, so they carry no `--temp-dir`) and the four
  triple declared/auto traces from session G, each with an explicit unique `--temp-dir`
  beneath `D:\tmp`. That spool placement is a **disclosed acquisition boundary**, adopted
  after the system volume could not host a ≈5.6 GiB grouping spool; it changed no output
  semantics, oracle, counter, product code or benchmark job — four byte-exact reproductions
  of session C's retained outputs prove it — and the 73M auto external digest continuity is
  now **closed**. It may never be used to compare timing across sessions.
- **Historical whole-command memory and throughput headlines stay session A/C
  observations** of the revisions that produced them, with the corrected build's own
  sampled figures recorded beside them rather than replacing them. Sampled maxima are lower
  bounds, and neither they, exit zero, a flat resident set, nor a BenchmarkDotNet
  allocation total proves a streaming bound — the algorithmic guarantee remains the
  independent observers and the retained-layout witnesses.

**Remaining M8 gates, none waived:** this documentation commit; a complete five-target
native run and three newly retained, inspected and hashed archives **at the documentation
head**, with the expensive benchmark, trace and Adult evidence carried across that commit
only after verifying the entire diff from `4216610b` is the three advisory Markdown files;
a fresh independent implementation review; operator acceptance and authorized merge; the
resulting main-push CI at the merge revision; and the separate GitLab archival gate. **M8
is not accepted, merged or complete until all of them have happened.**

**Completion obligations, none waived** (each stands for every later release candidate,
whatever its status at the current one):

- **Native proof on Windows x64, Linux x64, and macOS ARM64**, plus Linux/Windows
  ARM64 where available. Portable test code is not non-Windows proof: until each
  target has actually executed the ordinary suite, the resident-accounting witnesses,
  the Small harness smoke, and the package smokes, D-082's numerical guarantee extends
  to validated x64 alone. **Satisfied at `4216610b`** — all five targets executed all
  four, natively, in run `34289256438`. The obligation re-attaches at the documentation
  head.
- **Verified migration to the canonical public GitHub destination**, Actions
  established, and tested self-contained archives (`win-x64`, `linux-x64`,
  `osx-arm64`) delivered from runs at recorded revisions. **Satisfied at `4216610b`** —
  the cutover is verified and all three archives are retained and hash-verified. Three
  newly retained archives are required again at the documentation head.
- **A successful real-data (`External`) run on the final Windows x64 candidate.** The
  acquired UCI Adult corpus is deliberately outside routine CI — it is the one input
  this repository cannot generate, and requiring it in every native job would let an
  outage at a research-data host block package delivery. The evidence is required of
  the *candidate* instead: all three Adult cases must run successfully against the
  verified corpus (pinned length and SHA-256, D-124) on the final Windows x64 build,
  and the result retained durably. **Routine CI can be green while this is
  outstanding**, which is exactly why it is listed here. An unreachable host leaves
  the obligation open; a digest mismatch is an input-identity failure to investigate;
  a failure on verified bytes is a correctness finding. None becomes a pass.
  **Satisfied at `4216610b`** — all three cases passed on Windows x64 against the pinned
  entry, retained as durable evidence; the obligation re-attaches at the final candidate.
- **Probe-default adoption** remains a separate observable semantic decision — it
  changes draft bytes, warnings, and success-versus-guard-failure — and needs its own
  approval with a spec §7.1 / D-110 reconciliation. The defaults were measured at
  target scale and **not** adopted.

**Measured and settled (2026-09-06).** The controlled Windows x64 baseline is complete
at 730,000, 7.3M, and **73M** records, on identified hardware with every corpus,
output, spool and result on one non-system volume. Headlines:

- **Throughput is linear** — the wide source drain reads 2.83M, 2.74M and 2.81M
  records/second across a hundredfold range at a constant 723 B/record. This and the other
  component results below are unaffected by D-125, which is unreachable from their timed
  paths, and stand at their original session-A/session-C provenance.
- **A whole-command trace converted 4.51 GiB of input in a 62 MB sampled working set,
  unchanged between 7.3M and 73M**, while allocating 104 GB through the collector. The
  triple `unordered` path is different and legitimately so: its sampled working set grows
  with the *subject* count (about 260 B/subject; 1.8 GB at 7.3M subjects), which is the
  P-16 metadata carve-out, not a materialized matrix. Both are **session-A/session-C**
  observations of the executables those sessions built, not of the shipped build; the
  corrected build's own six traces are recorded separately in `docs/benchmarks.md`, and a
  sampled maximum is a lower bound on the true peak.
- **The `.dat` writer allocates nothing measurable**; emission does. The `.cxt` format
  costs 2.4x the time and 2.1x the allocation of `.dat` for the layout reason (§18.1).
- **Inline input-stability hashing costs 12-19% of a source pass, with no allocation the
  reported precision resolves** — the hashed and unhashed arms agree in the rounded
  `MB`/`GB` column, which is not a byte-identity claim. That is a **component** comparison
  at session-A provenance, unaffected by D-125.
- **The second pass an auto-calibrated spec requires costs 2x the whole conversion.** That
  ratio comes from `CLI host` rows (session A declared, session C auto), so it describes
  those revisions and **not** the shipped build; D-125 reaches that measured interval and
  no controlled timing replacement exists or is owed (D-126).
- **The grouping budget and merge fan-in were tested against the gate and retained.**
  A 256 MiB budget wins 30-38% at 730k and 19-20% at 7.3M but only 4-7% at 73M with no
  allocation gain, against a 25% between-session drift at that tier. See
  `docs/benchmarks.md` and D-124.

**One production defect was found by the suite, and fixed.** A spec with several
`equal_frequency` attributes could fail to calibrate under the shipped budget
(`GroupingStorageFailed`, **no result**) on healthy storage: calibration shares one
spool workspace across its count-sensitive attributes, but the D-082 `≤3T` merge
allowance compared that workspace's retained bytes against a **single attribute's**
spill payload, so the allowance shrank as attributes were added. The baseline is now
the workspace's, which is the scope its other side always had — one consistently
scoped guarantee, no new diagnostic, bound, or default, and no output byte moved.
The controlled 1/2/4/8/16-attribute by 64/512 MiB reproduction, the retained failure
reports, and the replacement measurements are in `docs/benchmarks.md`; the contract
and its evidence-replacement map are in D-124. `ManyQuantileCalibrateWorking` and
`ManyQuantileCalibrateScale7M`, which published NA as failures, now carry real
measurements.

### M9 — Avalonia desktop

Parallel-able from M5 onward; does not gate the CLI track. MVVM over the same
Core. Progress reporting + cancellation already plumbed from M1.

**Gate:** M7's run/publication coordinator is CLI-internal by decision (D-122), so a
**P-4 public-surface extraction review** MUST happen **before** any Desktop reuse of
it; no production package may reference `Cli`, and `EmitReplaySession` remains the
supported public bracket until a proven replacement exists.

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
- **Direct DB / SPARQL adapters** — thesis future work; new `Sources` adapters that fit the
  two complementary source seams: the bound `IObjectRecordStream` for **conversion** (an
  adapter built for a resolved spec) and the unbound streaming source session for
  **Discovery / `probe`** (schema + normalized records, D-109) — a future adapter implements
  both, and neither refactors the other. (SPARQL2FCA may inform this — see `docs/lineage.md`
  once that source is folded in.)
- **XLSX input** — separate `FcaBedrock.Sources.Excel`; defer unless painful.
- **JSONL / NDJSON input** — modern 3-column analog; consider modelling
  `shape = "jsonl"` in the binding even before implementing the reader.
- **Multi-level taxonomic value hierarchies** — value_groups is single-level in
  v1; multi-level (Bachelors → Uni-Degree → Education with per-analysis
  granularity) is a real design exercise, deferred until single-level ships.
- **Sampling / compressed output / arbitrary output-setting overrides** — streaming
  filters and writer wrappers. **Explicitly excluded from M7** (D-122): sampling
  changes rows and fingerprints, compression changes artifact/hash/advisory
  semantics, and arbitrary overrides expand the native-vs-effective fingerprint
  rules — so each returns only through **its own contract decision**, never as an
  opportunistic flag. Unknown flags are usage errors meanwhile.
- **Memory-budget / merge-fan-in knob** — the grouping backend's budget and fan-in
  stay internal through M7 and are tuned (not exposed) at M8; `--temp-dir` is the
  one runtime placement knob M7 exposes, byte- and fingerprint-neutral (D-122).
- **Color and progress output** — **committed** follow-up capabilities, deliberately
  not in M7. M7 ships plain terminal-independent output but centralizes diagnostic
  presentation and progress observation so these land without run-orchestration
  refactoring; exact flags and any terminal library remain undecided (D-122).
- **Machine-readable diagnostics** — an **anticipated later** requirement (a second
  stable output contract), against a real caller. Same centralization applies; no
  M7 flag or schema (D-122).
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
  M8. The `.cxt` size half is now **resolved by D-122 §7** — the advisory is the exact
  final serialized `.cxt` UTF-8 byte projection, computed after the name/count pass and
  before any output bytes, joining the registry as member 82 at its M7 emit site (spec
  §8). The allocation item remains tracked here pending its own `decisions.md` entry
  when M8 is picked up.
- Conversion run/session API (M7): M3 Slice F promoted the interim replay helper to the
  public `EmitReplaySession` (`EmitReplay.Begin`) — it brackets one conversion attempt,
  collects data diagnostics once, and aggregates grouping storage failures across the
  `.cxt` two-pass, flushing the finals at disposal (P-16). **D-122 settles the M7
  answer:** the run/publication coordinator is **CLI-internal**, nothing references
  `Cli`, and `EmitReplaySession` is **retained** as the public bracket — it is not
  superseded at M7. A P-4 public-surface extraction review is required before M9 reuse.
- Grouping backend knobs (M7/M8): `GroupingOptions` (in-memory budget, merge fan-in,
  temp root) is **internal** — never a spec/TOML/fingerprint input (the storage strategy
  never changes bytes). **D-122 settles the M7 exposure:** a runtime-only `--temp-dir`
  on `convert`, `plan`, `stats`, `calibrate`, and `fingerprint` (byte- and
  fingerprint-neutral, over a minimal public byte-neutral Conversion capability), with
  the memory budget and fan-in staying internal; tuning those provisional defaults
  against real 7.3M–73M distributions is M8 (P-19).
- Phase alignment for `AttributeNameDuplicate` (noted at the Slice F review,
  2026-07-05): **done at the M2 exit review (D-080).** The check — and its twin
  `ValueLabelKeyNotInDomain` — were re-homed from `ConversionPlanner` to the
  resolve seam over the document model, matching the §16.4 "spec validate" cell
  (the D-067 phase-ownership reading), byte- and fingerprint-neutral.
