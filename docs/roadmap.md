# Roadmap: FcaBedrock vNext

This roadmap records what FcaBedrock can do now, which milestones are complete, what the current
milestone is doing, what a release candidate must prove, and what is deferred beyond v1. Each
milestone is a vertical slice that leaves the system working. The reasons for each choice are in
`docs/decisions.md`, the normative file format is `docs/bedrock-spec-v1.md`, and measurements and
their limits are in `docs/benchmarks.md`.

## Current position

FcaBedrock is a technical preview. The pipeline reads wide CSV/TSV and subject-predicate-value
triple sources, applies a TOML spec (legacy `.bed` specs migrate to TOML), and writes deterministic
`.cxt` and `.dat` formal contexts through the eight commands of the `fcabedrock` tool. `README.md`
lists the capabilities.

| Milestone | Delivers | State |
| --- | --- | --- |
| M0 | Solution skeleton and golden harness | Complete |
| M1 | v2 output reproduced on the mini fixtures | Complete |
| M2 | TOML spec format and fingerprints | Complete |
| M3 | Triple source and wide column object keys | Complete |
| M4 | Calibrated discretizers, `value_groups` and `restrict_to` | Complete |
| M5 | Draft-spec discovery (`probe`) | Complete |
| M6 | Templates and matchers | Complete |
| M7 | The `fcabedrock` command-line tool | Complete |
| M8 | First scaling and benchmark pass | Complete |
| M8.1 | Generation-tiered spill catalogue for count-sensitive calibration | Complete |
| M8.2 | Writing, ownership and provenance hardening | In progress |
| M9 | Avalonia desktop application | Planned; begins after M8.2 |

M8's evidence closed at `3b2e4a80`, where the whole-solution test run counted 4,623 tests: 20
skipped and none failed. M8's documentation was integrated at `3d71524b`, and M8.1 is integrated on
`main` at `47e2ce71` (D-128). The most recent recorded full-suite result is 4,686 tests (4,666
passed, 20 skipped, none failed) from an offline Release run on 2026-09-22 of a candidate based on
`47e2ce7` whose C# changes were comment-only. It is not a run at `47e2ce7` or at any later commit.

No release has been made, and the `FcaBedrock.Cli` package is not published to a public NuGet feed.
This roadmap records no five-target native CI, tested-archive, UCI Adult acceptance, scale-probe or
`main`-push CI result for any revision after `3b2e4a80`. Each release candidate supplies its own
evidence ([Release-candidate obligations](#release-candidate-obligations)).

M8.2, the current milestone, changes how delimited data is read (spec §5.1.1, D-041, D-054), so it
can change the output of a spec it still accepts: blank records are skipped everywhere, the
whitespace removed around fields and numbers is the full Unicode whitespace set, quoting is
validated and malformed quoting is refused, the delimiter alphabet is fixed, and quoted CR content
read through a stream that returns short reads is no longer reordered (D-137). Its other
product-behavior changes are the wording of TOML syntax-error messages (D-133), the rejection of
three kinds of malformed spec, with one new diagnostic (D-135), the rejection of attribute names and
wide-binding fields that spec §10.1 and §5.2 forbid, `value_type = "date"` passing `validate` and
being refused at plan instead of at parse (D-038), `calibrate` keeping the authored
order of `value_labels` (D-075), an authored negative zero in `manual_cuts` or a manual
`equal_width` range resolving as zero (D-101), an `ordinal` scale over closed-ended
`manual_cuts` or `ordered_cuts` bins getting one correctly crossed threshold per bin (spec §12.3),
publication cleanup on Windows becoming able to reach its private transaction files at full paths
of 260 characters or more (D-125), a halted `convert` run no longer reporting that its readable
data file could not be read, and wording edits to some diagnostic messages, the `--help` text and
the package description.
M9 begins after M8.2 is accepted and integrated. Work deferred beyond v1 is in the
[deferred backlog](#deferred-backlog-not-v1).

## Milestones

### M0: Skeleton + golden harness

M0 created the solution skeleton in the `AGENTS.md` layout. Each package is created by the
milestone that first needs it, because empty shells would be speculative (EP-3), so M0 created
`FcaBedrock.Diagnostics`, `FcaBedrock.Core` and the test projects with their xUnit wiring. Its scope
included `FcaBedrock.Golden.Tests`, which compares output with the three v2 examples in
`fixtures/v2/`, and one end-to-end smoke test: read `mini-mushroom.bed` and its data, write `.cxt`
and `.dat`, and compare both with v2 under `--v2-compat`.

M0's scope also included wiring `Directory.Build.props` (nullable on, language version,
analyzers, selective `TreatWarningsAsErrors`) so that the `.editorconfig` severities apply in CI,
and mechanical backstops for the principles where practical: a Core purity architecture test, a
large-test category filter in CI, determinism repeatability tests and package dependency
guardrails.

**Exit (met):** `dotnet test` runs, and the golden harness can compare bytes.

### M1: Reproduce v2 on mini-mushroom + mini-adult

The first end-to-end slice: the wide-CSV source with its streaming primitive layer, the `identity`
and `manual_cuts` discretizers, the `nominal`, `dichotomic` and `ordinal` scales, `value_labels`
(D-023), both writers, and the v2 `.bed` reader as the spec input. The `mini-dates` fixture stays
parked until date support lands (D-038; see the backlog).

**Exit (met):** the golden tests are byte-identical to v2, or differ by a single documented diff
behind `--v2-compat`. Both fixture families reproduce byte for byte through the whole pipeline
(D-041 to D-049), and output is proven on two axes: golden v2-compat byte equality and native spec
conformance (D-043). A later conformance pass reconciled the M1 code with the M2 wording (D-050,
D-056, D-059 and the §5.1 whitespace rule) without changing a golden byte.

Byte equality with v2 is the cheapest proof that the pipeline reproduces the compatibility target.
It does not prove that v2 was semantically correct. An
intentional divergence is recorded as a decision, with the compatibility behavior kept behind
`--v2-compat` where needed.

### M2: TOML spec format + fingerprinting

M2 delivered the native TOML spec format: a strict reader and a canonical writer (D-075), a
presence-tracked document model with one resolve-and-validate seam that owns the static
diagnostics by phase (D-066, D-067, D-076), composition through `extends` with position-preserving
attribute override (D-027, D-052, D-078), `[output]` and `[provenance]`,
`missing_policy = "as_attribute"` (D-068), and the value-bin `ordinal` path over an explicit string
`order` (D-081). `interordinal`, `biordinal` and `contranominal` parse, and the planner rejects them
(D-010).

Three fingerprints, `schema_fingerprint` and the per-format `cxt_output_fingerprint` and
`dat_output_fingerprint` (D-051), hash a plan-derived canonical JSON encoding (D-053, pinned by
D-069 and D-077). The one-way `.bed` to TOML migrator targets the document model and reports
through `Diagnosed<T>`, so no parked configuration is dropped silently (D-079).

D-050 to D-058 and D-060 to D-072 set the contract. D-074 to D-081 are the implementation
decisions. The verification includes a fingerprint-stability golden over the numeric spellings
`30`, `30.0` and `3e1`, and value-bin ordinal conformance tests over every `direction × boundary`
combination.

**Exit (met):** TOML specs in the M2-supported v1 surface round-trip, v2 specs migrate, and known
v1 features outside M2 produce clear diagnostics.

### M3: Three-column (triple) source

M3 delivered the subject-predicate-value source in both orderings. `subject_grouped` streams in
one pass. `unordered` groups by first appearance on a bounded shared grouping backend: an external
sort-merge with a bounded merge fan-in, whose memory budget is an internal runtime setting and never
a spec field. The object key derives from the subject. Wide `object_key.mode = "column"` executes
with `duplicate_object_policy`, and wide `dedupe` shares the grouping backend and keeps
first-occurrence order (D-083). M3 also made `EmitReplaySession` public and added shape-aware `.bed`
migration (D-086) and a symmetrical `[output.dat].trailing_newline` control (D-087).

D-082 to D-085 set the contract: shape-specific `has_header`, one addressing mode for `columns`,
object identity from the resolved subject, an absent predicate as no observation (unlike a present
missing value), ordinal string collation as a project-wide rule (EP-12), unique object names under
`keep`, and `Error` severity for structural row and source errors.

**Exit (met):** both triple orderings work; all three triple goldens (`mini-mushroom_triples`,
`mini-adult_triples` and the named-subjects `mini-adult_triples_named`, spec §19.3) match v2 byte
for byte; and wide column object keys convert with `duplicate_object_policy` honored.

### M4: Discretizers beyond manual cuts (continuous + grouping)

Every v1 discretizer kind executes: `free_per_value` with numeric identity (D-101), `equal_width`
in its `manual`, `min_max` and `percentile_p1_p99` range modes over one shared cut engine (D-102,
D-103), `equal_frequency` (D-103), and `value_groups` with pass-through discovery (D-104).
Calibration records automatic cuts, observed domains, `unknown_value_policy = "include"` additions
and pass-through bins in a Core-owned calibrated state that planning consumes without re-deriving
them (D-093, D-098). `restrict_to` executes with existential matching and exact numeric entries
(D-091, D-105), and the `NoObjectsEmitted`, `ObjectHasNoCrosses` and `AttributeHasNoCrosses`
warnings report at emit.

Equal-frequency and percentile cuts are exact and bounded in memory: the quantile accumulator
spills to disk rather than approximating, and the spill and in-memory paths produce identical bytes
(D-095, D-103). D-088 to D-097 set the contract. D-098 to D-105 are the implementation decisions.

**Exit (met):** auto-binning calibrates deterministically and byte-identically to its frozen form;
cuts are captured in the calibrated state the manifest reads; `value_groups`, including
pass-through, converts; observed-domain calibration and `unknown_value_policy = "include"` resolve;
and `restrict_to` filters with existential, exact-numeric and range entries.

M4 deferred only the manifest's additional representation of non-cut calibration outcomes
(discovered, appended and pass-through values). M7 resolved it: the manifest records all four
outcome kinds (D-122, §15).

### M5: Discovery / `probe`

`probe` is an optional draft-spec generator outside the convert pipeline (D-003, D-036, D-106;
spec §7.1). The caller selects the shape (`wide` or `triple`) and the read settings, and probe
infers nothing structural: no delimiter, header, shape or type detection. It reads the cleaned
records once, in input order, and authors every discovered attribute as string-valued `identity`
plus `nominal`. A successful draft rereads, resolves and converts the same source under the same
settings with no Error or Fatal diagnostic (D-107).

Retention is per attribute. The `limit` option (default 100,000) truncates an attribute that has
strictly more distinct values, and a truncated attribute authors its retained prefix with
`unknown_value_policy = "include"`, so converting the draft recovers the complete schema (D-108).
Three aggregate guards bound a probe as a whole, with deterministic logical accounting and never
machine memory; a breach fails with `ProbeLimitExceeded` and no draft (D-110). The public surface
is `Prober` and `ProbeOptions`, over an unbound source session that the CSV adapters implement
(D-109). The caller writes the draft through `SpecWriter`, whose canonical form wraps long
`declared_domain` arrays (D-113). D-106 to D-113 govern M5.

This corrects an earlier roadmap note about v2's "100-distinct-value cap": v2's backend retained up
to 100,000 distinct values per attribute, and its UI displayed only the first 100 (`docs/lineage.md`
§1, D-108). M5 has no display limit. Guided, advisory type detection is future Discovery work (see
the backlog).

**Exit (met):** `probe` produces an editable draft spec from raw data: deterministic over the record
sequence (D-112), immediately usable, read in one cleaned data pass with no grouped or
count-sensitive pass, and correct end to end on the `mini-*` fixtures in both shapes.

### M6: Templates + matchers

M6 delivered the bulk-edit model in `Spec`, which replaces v2's Repeat-To. Resolution follows the
five tiers of spec §9.2, and matching templates layer field by field in declaration order (D-114).
Each matcher has exactly one selector, a whole-name `name_regex` or an inclusive zero-based
`source_index_range` (D-115), and matchers configure declared attributes without creating any.
`display_name` and `formal_attribute_format` render names through a closed placeholder grammar
(D-117, D-120). Templates and matchers apply at the resolver seam, so Core stays free of them
(D-118, D-121). D-114 to D-121 govern M6; D-116 sets its diagnostics.

**Exit (met; restated by D-119):** a single self-contained Internet-Ads-style spec, with the
complete attribute inventory plus one template and one matcher for the repeated boolean scaling,
converts end to end, and its declarative and materialized forms resolve to identical plans,
fingerprints and bytes. The exit measures the removal of repetitive per-attribute curation, not
total file length, because §2 requires an `[[attribute]]` for every logical attribute and matchers
configure rather than create. `InternetAdsExitTests` demonstrates it over a deterministic synthetic
corpus with the complete raw `ad.data` layout (1,559 columns) and no UCI data row copied, and every
exit spec carries the pinned Kushmerick/UCI `[provenance]`.

### M7: CLI

M7 delivered the `fcabedrock` tool with eight commands: `convert`, `validate`, `plan`, `stats`,
`calibrate`, `probe`, `migrate` and `fingerprint`. M5 supplies `probe`'s behavior; M7 adds a
command surface over it and no new discovery semantics. D-122 is the contract, with its normative
text in the spec, and D-123 records the implementation architecture.

In brief: SPEC and DATA are positional, and every other choice is a named option. Exit codes run
from 0 to 4, and warnings keep exit 0. Each diagnostic is one deterministic stderr line. Writing
commands need an explicit `--out` and refuse an existing target without `--force`. `convert`
publishes through a staged transaction that commits each file atomically and rolls back on a
best-effort basis. By default it writes one `BASE.manifest.toml` last, as the run's public commit
marker, and `--no-manifest` suppresses only that sidecar. Every complete input pass is hashed
inline, and a replay pass whose hash differs from the first fails the run before anything is
committed. `--temp-dir` is the one runtime placement option. The run and publication coordinator is
CLI-internal. M7 left the registry at 82, with no M7 transitional diagnostic.

M7 centralizes diagnostic presentation and progress observation so that color, progress and
machine-readable output can land later without reworking run orchestration. Those features and the
excluded sampling, compressed artifacts and arbitrary output-setting overrides are in the deferred
backlog.

M7 distributes a .NET global tool as a temporary technical preview, validated on x64. Its
documented route is `winget install Microsoft.DotNet.SDK.10` and then
`dotnet tool install --global FcaBedrock.Cli`, with equivalent guidance for other platforms, in
`README.md` and the packed `src/FcaBedrock.Cli/README.md`. The public release must provide a
standalone route ([Release-candidate obligations](#release-candidate-obligations)).

The presence-versus-emptiness rules of D-122 part 15 landed with tests from resolution through
calibration, freeze, manifest, fingerprint and `probe`. An omitted `declared_domain` requests
observed-domain calibration, while an authored `[]` is complete. `unknown_value_policy = "include"`
can still add observed values to it. `missing_policy = "as_attribute"` can still add the missing
column. `probe` omits `declared_domain` for an attribute whose observations were all missing; it
never authors `[]`. Over a complete empty string-bin universe, an omitted ordinal `order` is
`OrdinalOrderMissing`, while `order = []` is the valid empty permutation. Legal empty calibration
outcomes freeze and serialize as explicit empty arrays, and empty calibrated cuts stay unsuccessful.

**Exit (met):** the argv-boundary exit floor of D-122 part 14, item by item:

1. every command exercised through argv, with the eight-handler command-table lock;
2. all nine active goldens reproduced through the real CLI path (`GoldenArgvFloorTests`);
3. manifest byte locks for all four `[[run.calibrations]]` kinds, empty arrays, Unicode and control
   escaping, long values and their wrapping, argv, timestamps and both `[[run.spec_files]]` paths;
4. exit, diagnostic and rendering-grammar byte locks for every location-field combination;
5. publication, overwrite and collision cases;
6. rollback and incomplete-run recovery cases;
7. `calibrate` freeze and idempotence over every freeze mapping, the empty-outcome `[]` freeze and
   the authored-empty combinations;
8. `fingerprint` freeze, `--write` and idempotence;
9. the `probe` and `migrate` grammars: triple role addressing by index and by name, mixed-mode and
   partial-role usage failures, and the wide object-key options;
10. size-advisory threshold cases, with `OutputCxtSizeAdvisory` live;
11. `extends` identity and cycle cases;
12. signal cases;
13. input-stability mismatch cases;
14. a global-tool pack, install, uninstall and `--version` smoke in an isolated tool path
    (`ToolSmokeTests`, gated by `FCABEDROCK_TOOL_SMOKE=1`) beside the always-on `ToolPackTests`;
15. representative excluded flags returning usage exit 2;
16. every existing golden, canonical-TOML, SHA, registry and architecture lock still green;
17. a fast normal suite;
18. the exact global-tool route documented in user documentation before exit;
19. no document left describing M7 work as outstanding.

`ToolPackTests` checks the packed tool package directly, including a repository commit equal to the
checkout's `git rev-parse HEAD` and a readme byte-identical to the authored one. It pins no
whole-package bytes, because two packs of identical sources differ (D-125 part 7).

### M8: First scaling / benchmark pass

An internal BenchmarkDotNet suite (`tests/FcaBedrock.Benchmarks`, outside the normal `dotnet test`
run) measures the real production paths on synthetic 7.3M- and 73M-record datasets, 10 and 100
times the v2 EMAGE workload (D-007). Its purpose is to test the streaming choices of `Sources` and
`Conversion`, find allocation hotspots and settle the memory budgets. D-124 records the suite's
design.

M8 may tune the grouping and calibration memory budget and the merge fan-in; adopting new probe
defaults is a separate semantic decision (D-124). M8 cannot establish or weaken what earlier
milestones made correctness inputs: exact bounded-memory calibration (D-095), probe's
deterministic logical accounting (D-110), and D-082's resident-accounting layout constants, which
change only with revalidation. M8 also measures the cost of inline input-stability hashing and
manifest hashing; a later opt-out would need its own ruling and would never activate by file size
(D-122). M8 added the standalone self-contained distribution and per-platform build, test,
accounting and packaging checks: the platform validation that the M7 global-tool route does not
claim (D-122 part 13).

**Exit (met):** documented throughput and memory at target scale, with no full-matrix
materialization. `docs/benchmarks.md` records the results at 730,000, 7.3M and 73M records on one
identified Windows x64 machine. The no-materialization guarantee rests on the grouping and
calibration observers and the retained-layout witnesses, not on sampled memory traces.

What M8 found and changed:

- **A calibration defect.** A spec with four or more `equal_frequency` attributes failed to
  calibrate 730,000 records under the shipped 64 MiB budget on healthy storage, with
  `GroupingStorageFailed` and no result. The merge allowance compared the whole shared spool
  workspace with one attribute's spill payload; it now uses the spill payload of every attribute
  sharing the workspace, with no new diagnostic, bound or default (D-124).
- **Tuning.** The grouping budget stays at 64 MiB and the merge fan-in at 16. A larger budget was
  faster at 730,000 and 7.3M records but did not meet the evidence rule at 73M (D-124).
- **Two pre-existing M7 defects.** The first native CI run exposed them: publication trusted a
  remembered file identifier that the operating system can reissue, and a package test pinned one
  NuGet producer's metadata file name. D-125 corrected both: publication now holds open every object
  whose identity authorizes a later mutation, until its last authorized use.
- **Archive delivery.** The archive tested is the archive delivered: each target's smoke extracts
  and runs the archive the workflow uploads, the Unix apphost is recorded executable, and every
  entry of a Windows archive records zero external attributes (D-124 corrections of 2026-09-09 and
  2026-09-10). Off Windows, the packaging tests have run only in hosted CI.
- **Three validation defects.** An exhaustive review found that the Adult plan-shape check, the
  probe outcome oracle and the Windows archive mode check proved less than they claimed. A
  tests-only correction fixed all three at `3b2e4a80`, and the affected checks ran again there
  (D-124 correction of 2026-09-11).

The publication correction's effect on command latency is unknown. A pre-registered paired
comparison ran in full and was inconclusive at its 5% bound, and no retry is required (D-126). No
neutrality, equality, non-regression, speedup or corrected-build absolute rate follows from it.
Correctness, allocation and resource evidence for the corrected build is separate from latency: at
`50f6aa62` on 2026-09-10, all fifteen `CLI host` cases were validated with their allocation
recorded, and six whole-command resource traces were taken.

M8's evidence closed at `3b2e4a80`, whose runs took place on 2026-09-11 and 2026-09-12. At that
revision the `probe` rows were re-validated, the three UCI Adult cases passed on Windows x64, and
the native CI run passed on all five targets and produced the three tested archives. Each of these
results belongs to that revision. `docs/benchmarks.md` has the measurements, their provenance and
their limits.

### M8.1: generation-tiered quantile spill catalogue

Count-sensitive calibration now schedules its spill runs in generations instead of repeatedly
merging the whole growing catalogue (D-128). Original spills enter generation zero, and each time a
generation holds `F` runs (the merge fan-in) they merge into one run of the next generation, so a
value is rewritten at most once per generation. Cuts, frozen specs, fingerprints, diagnostics, the
public API and output bytes are unchanged. The merge fan-in still bounds the open readers, and the
closed run catalogue has a fixed ceiling (`K = (F-1)*L`, D-128). Permanent Conversion tests keep an
independent model of the schedule, real workspace observations, exact occupancy and byte checks,
and the storage, cancellation and overflow cases.

A direct BenchmarkDotNet assessment of the sixteen-attribute 7.3M calibration case, run as two
order-balanced pairs, measured a candidate-to-baseline elapsed ratio with a geometric mean of
0.749692, about 25% less elapsed time. The two guard cases showed no detectable movement, and
allocation was effectively unchanged. This is practical adoption evidence, not a formal admission.
There is no 73M result, and D-126's publication-latency limitation is unaffected.

M8.1 is integrated on `main` at `47e2ce71`. The measured source differs from the integrated source
only in one token of an XML documentation comment, so no executable source differs, but no build of
the integrated revision was measured. `docs/benchmarks.md` has the figures and limits.

### M8.2: Writing, ownership and provenance hardening

M8.2 is the current milestone, and M9 waits for it to be accepted and integrated. It hardens how
the repository is written, who owns each lasting fact, how evidence provenance is stated, and how
authored text is checked. It changes no CLI command or option, exit-code meaning, determinism rule
or fingerprint format. Reading delimited data by spec §5.1.1 (D-041, D-054) can change `.cxt`,
`.dat` and manifest bytes of a spec it still accepts when the input has blank records, whitespace
other than spaces around fields or numbers, or outer whitespace beside a quoted field; it refuses
malformed quoting and delimiters outside the v1 alphabet, keeping all 95 delimiters that were usable
before. Ordinary CSV and TSV whose fields are unquoted or correctly quoted, with only spaces around
them and no blank lines, read as before, and so do the v2 fixtures. A library caller whose stream
returns short reads can also see corrected quoted CR content; no CLI file read was observed to be
affected (D-137). The reading change's elapsed cost on small inputs is not bounded (D-138);
`docs/benchmarks.md` has its comparison with the previous reader. Its public API additions are
the binding-only resolver stage `SpecResolver.ResolveBinding` (D-135), the delimiter-alphabet
predicate `SourceReadSettings.IsInDelimiterAlphabet` (D-054), the attribute-name rule
`AttributeNameValidity.IsValid` (spec §10.1), and the date reject-carrier: the document flag
`SourceSection.HasDateValueType`, the Core source `UnimplementedDateSource` and the
`DiagnosticCode` member `DateValueTypeNotImplementedV1`, which replaces the removed member
`SpecSurfaceNotYetSupported` (D-038). Removing that member lowers by one the numeric value of the
47 members from `SpecVersionUnsupported` to `ObjectKeyCompositeNotImplementedV1`, and the new
member takes the value the last of them had. The CLI and the registry name diagnostics, but the
public enum's integral values still change: code compiled against the earlier enum, or a stored
numeric value, maps to a different code. It declares TOML
1.1.0, the grammar of the parser the spec reader has always used, and the `SpecTomlInvalid`
syntax-error message now names that grammar (D-133). It rejects five inputs the spec never
allowed: a composed spec with no `[[attribute]]` is the new `AttributesMissing` (Error, spec
resolve); a `base_index` other than 0 or 1 or a negative `size_advisory_bytes` is
`SpecFieldInvalid` (D-135), and so is an attribute `name` containing CR, LF or `"` (spec §10.1)
or a `[binding]` that declares `shape = "wide"` and sets `ordering` or `columns` (spec §5.2); a
composed wide binding that inherits either from another file is `SourceBindingInvalid` instead. A
spec with `value_type = "date"` now passes `validate`, and every command that plans refuses it with
`DateValueTypeNotImplementedV1` (Fatal, plan) instead of the parse-phase
`SpecSurfaceNotYetSupported` (spec §10.2); the registry keeps 83 codes. No other diagnostic code,
severity or phase changes. `calibrate` now
writes `value_labels` in their authored order, as D-075 requires; it could reorder a map before,
with no effect on any fingerprint or on `.cxt` or `.dat` bytes. For a library caller, each
non-empty `value_labels` map of `ResolvedDocument.Document` is now a read-only wrapper whose
`SyncRoot` reaches the snapshot's own copy (D-098); Core's resolved state stays immutable. An
authored negative zero in `manual_cuts` or in a manual `equal_width` range's `vmin` or `vmax` now
resolves as zero, like every other numeric identity (D-101): such a spec's fingerprints change
once (all three for a cut, the two output fingerprints for a bound) with no `fp_format` change, a
`-0` cut's `.cxt` column names change (`<-0` becomes `<0`) while its incidence and `.dat` bytes do
not, and a diagnostic about such a value shows `0`. An `ordinal` scale over `manual_cuts` or
`ordered_cuts` bins with `ends = "closed"` now gets one threshold per bin at that bin's own edge,
as spec §12.3 requires: closed cuts 30/40/50 give `<40` and `<50` for `le`, and `>=30`
and `>=40` for `ge`. It used to get one threshold per cut: `le` named its thresholds one cut too
low (`<30` crossed `[30, 40)`) and added a last one that crossed the same bins as the one before
it, and `ge` added a last threshold, `>=50` here, that no object could cross. Such a spec's `.cxt`
bytes and three fingerprints change, with no `fp_format` change. Its `.dat` bytes change only where
an object's crossed column IDs change, through different crossings or the renumbering of crossed
columns after a removed one: dropping a closed `ge` attribute's never-crossed last column changes
only the IDs of crossed columns after it, and input whose values all fall outside the closed range
writes the same `.dat` bytes. A run manifest records the changed fingerprints and the hashes of
the changed files. `drop_top = true` now suppresses the outermost cut threshold of closed bins, as
it already suppressed the `all` of open ones, and a closed `ge` attribute no longer draws an
`AttributeHasNoCrosses` warning for that column. Every other combination of discretizer and scale,
ordinal over open-ended cut bins included, keeps its output, and so do the v2 fixtures. On Windows,
publication cleanup can now reach its private transaction files at full paths of 260 characters
or more (D-125). Without Windows long-path support, its removal open was refused at those lengths
and the refusal was read as the file being gone, so a successful run could leave private files
beside its outputs, and a later run to the same output base could refuse at exit 1 because of
them. Cleanup is otherwise unchanged: it is best effort, it removes only an object it proves is
its own, and an object it cannot remove (after an I/O or permission failure, or a refused proof)
stays in place as before. A `convert` run that collects an Error or Fatal diagnostic no longer
adds the code-less error that its readable data file could not be read when no input pass read
that file to the end, as happens when a halt (an unusable or duplicate object key, or a
non-contiguous subject) comes well before the end of a large file. Unless an output stage failed,
the data file could not be read, two complete input passes hashed differently or the run was
cancelled, each reported as before, such a run reports its diagnostics alone and exits 1. The
`NoFormalAttributes` warning now also names an attribute that yields no column as a cause. The
wording of some diagnostic and internal exception messages changes to remove dashes used as
punctuation and references to past milestones, `--help` headings take the form `name: summary`,
and the package description loses its dash (WP-1, WP-6). Otherwise, normative spec
prose changes follow D-134's three reviewed kinds: correcting a passage to its rule owner, stating
enforced defaults, bounds or allowed values that were shown only in examples, and stating rules
already enforced by the reader or resolver. Spec §8 also states the exact `.cxt` size-advisory
default applied by the CLI, 1,073,741,824 bytes, as D-134 records, spec §7.1 states the three
probe aggregate-guard defaults that `ProbeOptions` applies, and D-136 settles the
stored-fingerprint limit in spec §3 and §14 as a recommendation. These prose changes leave
implemented behavior unchanged.

Its work proceeds in this order:

1. `docs/writing-principles.md` owns how documents and comments are written (WP-1 to WP-10),
   `docs/engineering-principles.md` owns the engineering invariants as `EP-1` to `EP-23`, and the
   owner map in `AGENTS.md` names one document for each lasting fact (D-129).
2. `eng/check-authored-text.ps1` checks authored text mechanically: strict UTF-8, control
   characters, obsolete spellings and the two instruction entry points (D-130). Links and style
   remain review judgments.
3. The user documentation, the benchmark guide, the decision log and this roadmap are revised for
   readers. The decision log, the roadmap and the benchmark guide take colon heading separators
   (D-131), and no separate public evidence file exists (D-132).
4. The remaining documents, source and test comments, and engineering and workflow files are
   reviewed against the same rules, and a closing audit reconciles the whole pass.

### M9: Avalonia desktop

An Avalonia desktop application (D-008), MVVM over the same Core, which also exposes M5's `probe`
in the UI. M9 begins after M8.2 is accepted and integrated. It does not block the CLI track: UI work
must not hold up converter progress. The library APIs already accept cancellation; progress
observation exists only as a CLI-internal seam (D-122 part 3).

**Precondition:** the run and publication coordinator is CLI-internal (D-122 part 9), so an EP-4
public-surface extraction review must happen before any Desktop reuse of it. No production package
may reference `Cli`, and `EmitReplaySession` stays the supported public bracket for one conversion
attempt until a proven replacement exists.

**Exit:** load, inspect, edit a spec and export, on Windows, macOS and Linux.

## Release-candidate obligations

Each release candidate must supply its own evidence for every item below, and routine CI can be
green while any of them is outstanding. A result carries from one revision to a later one only when
that change's exact diff is shown not to reach what the result covers (D-126); otherwise the
candidate runs it again.

- **Native proof and tested archives.** The candidate's own CI run executes the ordinary suite, the
  resident-accounting witnesses, the Small benchmark smoke and both package smokes natively on
  Windows x64, Linux x64 and macOS ARM64, plus Linux ARM64 and Windows ARM64 where available.
  Portable test code is not proof for a platform where it has not run. The same run produces the
  three self-contained archives (`win-x64`, `linux-x64`, `osx-arm64`), and each target's smoke
  validates, extracts and runs the exact archive the workflow uploads (D-124).
- **Real data on the final Windows x64 candidate.** All three UCI Adult (`External`) cases pass on
  the candidate's final Windows x64 build against the corpus whose length and SHA-256 are pinned in
  `AdultCorpus.cs`, and the result is retained. Routine CI never runs this corpus, so an
  outage at the data host cannot fail an unrelated build. An unreachable host leaves the obligation
  open, a length or digest mismatch is an input-identity failure to investigate, and a failure on
  verified bytes is a correctness finding; none of the three becomes a pass (D-124). The corpus
  attribution is in `tests/FcaBedrock.Benchmarks/Corpus/Adult.attribution.md`.
- **Resident accounting on each target.** D-082's resident-accounting layout constants carry the
  numerical `actual retained ≤ modeled` guarantee only on .NET 10 CoreCLR x64. Before a
  cross-platform release, they must be validated on each .NET 10 CoreCLR target the release
  supports, x64 and ARM64 as applicable, including macOS ARM64 and, where available, Linux and
  Windows ARM64. Each validation checks `Unsafe.SizeOf<RankedRow<T>>`, object, array and string
  layouts, reference sizes, alignment, and the numerical guarantee itself. A target whose layout
  differs gets its own correctness constants, and the guarantee extends to a target only after that
  target is validated. These constants are correctness inputs and are never tuned for performance.
- **A standalone public release.** The global tool is a temporary technical-preview distribution.
  The public release must provide a standalone, self-contained route that needs no prior .NET
  knowledge, the global tool may coexist with it, and the public-release documentation must lead
  with the standalone route (D-122 part 13).

## Deferred backlog (not v1)

Items modelled in the spec can be added later without a format break.

- **Composite object keys** (D-024): modelled, and the planner rejects them in v1. Implement in
  v1.1 once streaming is proven.
- **Advanced scales** `interordinal`, `biordinal` and `contranominal` (D-010): modelled, and the
  planner rejects them. Implement after v1.
- **Date value type** (`value_type = "date"`) and date scaling (D-038) —
  reserved, planner rejects (`DateValueTypeNotImplementedV1`); reproduces v2's
  `d` type when implemented. `mini-dates` is the parked fixture. Re-enabling is
  a non-breaking addition (the `value_type` field already exists).
- **Refuse reserved v1 features before the calibration data pass**: when a spec needs calibration,
  `convert` and the other commands that plan read the whole data file before the planner refuses an
  advanced scale (`ScaleNotImplementedV1`), a composite object key
  (`ObjectKeyCompositeNotImplementedV1`) or `value_type = "date"` (`DateValueTypeNotImplementedV1`).
  The refusal needs no data, so the cost is time spent before a certain error; no output is wrong.
  A fix moves the planner's data-independent refusal checks ahead of the data pass and needs its
  own small design; the refusal stays a plan-phase diagnostic (D-010).
- **`std_dev` discretizer** (D-020): removed entirely. It can return as a new discretizer kind if a
  real need appears, without a breaking change.
- **Cross-attribute restrict** ("include attribute A only when attribute B = X"): not modelled in
  v1, with no reserved carrier syntax (D-062). Spec §20 describes it in prose only.
- **Population-relative calibration** (quantiles over only the objects `restrict_to` keeps): a
  recorded future option, not a v1 setting. v1 calibrates over the input universe, before
  `restrict_to` selects objects (D-065, spec §7).
- **Post-context reductions** (clarify, reduce and minimum support) in a sibling `FcaBedrock.Reduce`
  tool (D-025). The thesis flagged minimum support.
- **Direct database and SPARQL adapters**, from the thesis's future work: new `Sources` adapters
  that implement both source seams, the bound record source for conversion and the unbound
  streaming source session for `probe` (D-109), without refactoring either. SPARQL2FCA may inform
  the design (`docs/lineage.md`).
- **XLSX input**: a separate `FcaBedrock.Sources.Excel`, deferred unless its absence becomes
  painful.
- **JSONL / NDJSON input**: a modern analog of the three-column source. Consider modelling
  `shape = "jsonl"` in the binding before implementing the reader.
- **Multi-level taxonomic value hierarchies**: `value_groups` is single-level in v1. Multiple
  levels with per-analysis granularity (Bachelors, Uni-Degree, Education) are a design exercise of
  their own.
- **Sampling, compressed output and arbitrary output-setting overrides**: excluded from M7
  (D-122). Sampling changes rows and fingerprints, compression changes artifact, hash and advisory
  semantics, and arbitrary overrides widen the native-versus-effective fingerprint rules, so each
  returns only through its own contract decision, never as an opportunistic flag. Unknown flags are
  usage errors meanwhile.
- **Grouping memory budget and merge fan-in**: internal settings, never a spec, TOML or fingerprint
  input, because the storage strategy never changes output bytes. M8 measured them and kept 64 MiB
  and 16 (D-124). `--temp-dir` is the one runtime placement option, and it is byte- and
  fingerprint-neutral (D-122).
- **Separate grouping and calibration budgets**: one budget sizes both the grouping backend and the
  count-sensitive calibration accumulator, which is allocated before any record is read. Raising it
  for grouping would raise that fixed cost for every conversion with a count-sensitive attribute.
  Separate budgets would let grouping take its measured gain; that architecture change needs its
  own decision (D-124, `docs/benchmarks.md`).
- **Probe default adoption**: probe's retention limit and guard defaults were measured at target
  scale and not changed. New defaults would change draft bytes, warnings and success-versus-guard
  outcomes, so adopting them needs its own approval and a reconciliation with spec §7.1 and D-110.
- **Guided, advisory type detection** in `probe` ("this looks continuous: add ranges?"): recognized
  future Discovery work, with no milestone assigned (D-106).
- **A scheduled or separate real-data CI job**: not adopted, because it would add a cadence and a
  maintenance cost with no check that the explicit release-candidate run does not already provide.
  Reconsider it if that manual run proves insufficient (D-124).
- **Color and progress output**: committed follow-ups, deliberately not in M7. M7 ships plain,
  terminal-independent output but centralizes diagnostic presentation and progress observation so
  that these land without reworking run orchestration. The flags and any terminal library are
  undecided (D-122).
- **Machine-readable diagnostics**: an anticipated later requirement, a second stable output
  contract designed against a real caller. The same centralization applies; M7 has no flag or
  schema for it (D-122).
- **TCA (triadic FCA)**: out of scope for the foreseeable future.
