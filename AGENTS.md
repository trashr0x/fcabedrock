# AGENTS.md — FcaBedrock vNext

This file orients any session (human or model) working on this repository.
**Read this file, then `docs/decisions.md`, before proposing or making
architectural changes.** The spec is normative; this file is operational.

## What this project is

FcaBedrock vNext is a ground-up .NET 10 rewrite of FcaBedrock — a tool for
**Formal Concept Analysis (FCA) preprocessing**. It ingests structured data
(CSV/TSV today; subject-predicate-value triples; SQL/SPARQL later), applies a
user-curated **Bedrock spec** describing how raw values become formal-context
attributes via conceptual scaling, and emits deterministic **Burmeister
`.cxt`** or **FIMI `.dat`** files for downstream FCA tools (ConExp, etc.).

Lineage: the original FcaBedrock (v2, VB.NET) began as the author's BSc final
year project, was extended during the author's PhD on appropriating structured
data for FCA, and was published at ICCS 2010 with Simon Andrews. vNext is a
sole-authored, from-scratch rewrite. See `docs/lineage.md` for what v2 did and
what the thesis settled.

- **Author / copyright:** Constantinos Orphanides.
- **License:** MIT.
- **Credit:** v2, the ICCS 2010 paper, the PhD thesis, and Simon Andrews are
  acknowledged in `README.md`.

## The one idea that anchors everything

**Discretization and scaling are orthogonal.** A *discretizer* maps a raw
value to a discrete bin label. A *scale* maps a bin label to zero or more
formal attributes. Every v2 "attribute type" is a (discretizer, scale) pair,
and new combinations fall out for free. If a change blurs this separation,
it's almost certainly wrong. See `docs/decisions.md` D-002.

```text
raw value --[discretizer]--> bin label --[scale]--> formal attribute(s)
```

## Repository layout

```text
/AGENTS.md                     # this file
/README.md                     # public-facing; includes credits
/docs/
  bedrock-spec-v1.md           # NORMATIVE spec for the Bedrock file format
  principles.md                # engineering invariants; check code against these
  decisions.md                 # architectural decision log (read before changing design)
  roadmap.md                   # milestones M0–M9, current position, deferred items
  lineage.md                   # what v2/thesis/SPARQL2FCA settled (one-time synthesis)
/.editorconfig                 # mechanical conventions (the linter-checkable rules)
/src/
  FcaBedrock.Diagnostics/      # Result<T,TError>, BedrockDiagnostic — leaf, referenced by all
  FcaBedrock.Core/             # domain types, scales, discretizers, planner. Zero I/O.
  FcaBedrock.Sources/          # wide-CSV + triple adapters; IObjectRecordStream
  FcaBedrock.Discovery/        # auto-detect: produces a draft spec from data
  FcaBedrock.Conversion/       # Plan (pure) + Calibrate + Emit (streaming) pipeline
  FcaBedrock.Export/           # .cxt + .dat writers
  FcaBedrock.Spec/             # TOML reader/writer, .bed migrator, fingerprinting
  FcaBedrock.Cli/              # `fcabedrock` command
  FcaBedrock.Desktop/          # Avalonia app (M9; doesn't gate the CLI track)
/tests/
  *.Tests/                     # xUnit per package
  FcaBedrock.Golden.Tests/     # byte-equality against the v2 mini-* fixtures
  FcaBedrock.Benchmarks/       # BenchmarkDotNet; large synthetic data, opt-in
/fixtures/
  v2/                          # the three v2 mini-* examples, checked in verbatim
```

Dependency rule: `Diagnostics` is the only internal package referenced by
everything. `Core` references only `Diagnostics` and `System.*`. No circular
references. `Core` never references `Sources`, `Export`, `Cli`, or `Desktop`.

## Hard conventions (do not violate without a decision-log entry)

Operational summary; the invariants are authoritative in `docs/principles.md`
(P-7/P-13/P-14/P-15/P-17) and `docs/decisions.md`.

- **Target framework: .NET 10.** Modern APIs (`System.IO.Pipelines`,
  `IAsyncEnumerable`, `Span`/`Memory`, `ValueTask`) where justified; justify
  allocations in hot paths, don't cargo-cult.
- **Determinism is a correctness property:** same spec + same normalized input ⇒
  byte-identical output; ordering rules live in the planner. Spec §17, D-004, P-7.
- **Core is pure / UI-independent; exporters are dumb** — all semantics decided
  before export, writers only serialize. P-13, P-15.
- **Error handling:** `Result<T, BedrockDiagnostic>` for single-error ops;
  `Diagnosed<T>` (value + diagnostic list) for aggregating ops (validation,
  planning); convert streams diagnostics alongside the emit stream. Codes are an
  enum. P-14.
- **Scale of intent:** v1 targets 10×–100× the v2 EMAGE workload (~7.3M–73M
  records) — streaming is a v1 concern, not a retrofit. D-007.
- **Small composable pieces over god classes.** P-17.

## Testing conventions

The project standard; full `Directory.Build.props` / MTP-runner mechanics and
rationale live in `docs/decisions.md` D-039/D-040.

- **One xUnit v3 test project per production package**, `FcaBedrock.<Package>.Tests`,
  created when that package first has testable code (not before).
- **Shared config is in `tests/Directory.Build.props`** — it re-imports the
  repo-root props and supplies `OutputType=Exe`, the `xunit.v3` reference, the
  `Xunit` global using, and the zero-tests guard. A new test project inherits all
  of it and carries **only** its own references/items. Don't repeat `using Xunit;`
  per file.
- **Naming:** test class `<ClassUnderTest>Tests` in a folder/namespace mirroring
  the production type; unit/behavioural methods `Subject_When<Condition>_Then<Outcome>`;
  architecture tests `Subject_Should<Outcome>` (**ArchUnitNET**, not hand-rolled
  reflection). Cross-cutting suites (golden harness, architecture) are organized by
  behaviour, not mirrored to a type.
- **Runner:** xUnit v3 on Microsoft.Testing.Platform. Run `dotnet test` (MTP mode
  via `global.json`), or `dotnet test --solution FcaBedrock.slnx`; the positional
  `dotnet test <solution>` form is rejected in MTP mode.

## Workflow for a new session

1. Read this file + `docs/roadmap.md` (current position) + the **index** at
   the top of `docs/decisions.md`; then read the decision entries your task
   touches (D-073). Read the log in full before proposing architectural
   changes (the rule at the top of this file). Skim `docs/principles.md` —
   it's the invariant set code must satisfy.
2. For spec questions, `docs/bedrock-spec-v1.md` is the source of truth.
   Do not infer format behavior from code; the spec governs.
3. When you make a real architectural decision, append an entry to
   `docs/decisions.md` (format in that file). Small diffs, reviewable.
4. The spec is normative; the docs are advisory notes. If code and spec
   disagree, the spec wins and the code is the bug (or the spec needs a
   reviewed change — not a silent one).
5. Golden tests (`fixtures/v2/`) are ground truth for **v2-compat byte
   equality, not semantic correctness**. Do not fix a golden mismatch by
   editing the fixture; the fixture records what v2 actually produced. If vNext
   intentionally diverges, record the decision and gate compatibility behavior
   behind `--v2-compat` where needed.

## Git / workspace discipline for agent sessions

**Default mode: you run unattended in your own per-task Git worktree** — not
optional setup; it's where every session works, whether or not the operator is
watching. One mode, deliberately: a "supervised vs unattended" split is just
another missable trigger.

**Worktree pre-flight — idempotent; run before your first edit, and re-run after
any resume/compaction:**

1. `git rev-parse --abbrev-ref HEAD` (and `git worktree list` if unsure).
2. Already on an `agent/<task>` branch in a dedicated worktree → proceed.
3. On `main` / the operator's checkout and about to edit → create the worktree
   first: `git worktree add ../fcabedrock-agent-<task> -b agent/<task> main`.

It's a *check*, so re-running after a resume is a cheap no-op — that is what makes
it survive compaction. Don't assume a task-start step ran.

**Operator curation is expected, not an anomaly.** Mid-task the operator may
stage/commit your files into logically-grouped commits on the branch (to organize
history), often from an external Git tool in the same worktree. So a clean/partial
`git status`, files you created already committed, or commits you did not author
are **not errors** — don't investigate, re-create, or re-stage; just continue. The
operator merges to `main` only at task finalization (the same point you would).

Standing rules:

- one task = one branch = one worktree;
- do not reuse a dirty worktree for unrelated work, or switch branches with
  uncommitted changes;
- **do not commit or rewrite history unless explicitly asked** — the operator
  commits;
- **never run `git clean`** — any flags, including `-n`/`--dry-run`. Your new
  task files stay untracked until the operator curates them, so a clean can
  delete your own work, not just build output; for a clean build use
  `dotnet clean`, and if `bin`/`obj` directories must be removed by hand,
  resolve and verify the exact paths first;
- before editing, run the pre-flight (location) and `git status` (cleanliness);
- at hand-off, summarize the files your task changed via `git diff main...HEAD`
  (the operator may have already committed some, so `git status` can be clean);
- if the diff grows outside the task, stop and explain before continuing.

Worktrees isolate work; they don't justify broad diffs, opportunistic cleanup, or
unrelated refactors.

## Plan before code

For non-trivial work, don't start editing immediately. Inspect the docs/code,
then propose a short concrete plan: what changes and which files/projects;
whether any **public** API/contract/diagnostic changes (the workflow face of P-4);
tests to add or update; and open questions. Present it and wait for a go-ahead.

For consequential changes — new public surface, a cross-package contract, a
deviation from the spec or a decision, or anything touching determinism or output
bytes — be more explicit about the contract, risks, and test coverage first.

Purely mechanical/trivial tasks: say so and proceed with a brief note.

If scope expands mid-task, stop and revise the plan before continuing (the
planning-time face of P-1 "surgical changes" and the diff-growth rule above).
Don't implement first and explain later.

## Current status

**M1–M7 are complete.** M7 (CLI) landed across Slices A–K (master-plan steps S1–S11)
under **D-122** (the adjudicated pre-implementation contract) and **D-123** (the
implementation decisions): the `fcabedrock` global tool with all eight commands, the
publication transaction, the run manifest, the freeze engine, filesystem identity, and
the argv-boundary exit floor. The diagnostic registry is **82**, and no M7 transitional
diagnostic remains. **M8 (the first scaling/benchmark pass) is next**, then M9.
`docs/roadmap.md` is the live source for current position, test count, and the
deferred backlog — consult it rather than duplicating the detail here.
