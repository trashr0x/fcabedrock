# CLAUDE.md — FcaBedrock vNext

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
/CLAUDE.md                     # this file
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

- **Target framework:** .NET 10. Modern APIs are welcome where justified
  (`System.IO.Pipelines`, `IAsyncEnumerable`, `Span`/`Memory`, `ValueTask`),
  but do not cargo-cult performance tricks. Justify allocations in hot paths.
- **The core library is UI-independent.** No UI types leak into `Core`,
  `Conversion`, or `Export`.
- **Determinism is a correctness property.** Same spec + same normalized
  input ⇒ byte-identical output. The ordering rules live in the planner, not
  scattered. See spec §17 (Determinism rules) and `docs/decisions.md` D-004.
- **Exporters are dumb.** All planning/conversion semantics happen before
  export. A writer only serializes an already-decided result.
- **Error handling:** `Result<T, BedrockDiagnostic>` (alias `BedrockResult<T>`)
  for single-error operations; `Diagnosed<T>` (value + diagnostic list) for
  aggregating operations like validation and planning. Convert streams
  diagnostics alongside the emit stream. Codes are an enum, not free strings.
- **Scale of intent:** v1 targets 10×–100× the v2 EMAGE workload
  (~7.3M–73M records). Streaming is a v1 concern, not a v2 retrofit.
- **Prefer small composable pieces over god classes.** Keep responsibilities
  narrow, APIs focused, and implementation units easy to test and replace.

## Workflow for a new session

1. Read this file + `docs/decisions.md` + `docs/roadmap.md` (current position).
   Skim `docs/principles.md` — it's the invariant set code must satisfy.
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

Agent sessions use isolated Git worktrees by default.

- one task = one branch = one worktree;
- the main checkout is the human/operator workspace;
- do not reuse a dirty worktree for unrelated work;
- do not switch branches with uncommitted changes;
- do not commit unless explicitly asked;
- do not rewrite history unless explicitly asked;
- before editing, run `git status`;
- before handing work back, run `git status` and summarize changed files;
- if the diff grows outside the requested task, stop and explain why before
  continuing.

Recommended setup:

```bash
git worktree add ../fcabedrock-agent-<task> -b agent/<task> main
```

Worktrees isolate work; they do not justify broad diffs, opportunistic cleanup,
or unrelated refactors.

## Plan before code

For any non-trivial implementation task, do not start editing immediately.

First inspect the relevant docs and code, then propose a short, concrete plan:

- what will change, and which files/projects it touches;
- whether any **public** API, contract, or diagnostic changes (this is the
  workflow face of principle P-4 — design the surface before the implementation);
- what tests will be added or updated;
- assumptions, open questions, and ambiguities that need confirmation.

For non-trivial implementation work, present the plan and wait for a go-ahead
before editing code.

For consequential changes — new public surface, cross-package contract, a
deviation from the spec or a decision, anything touching determinism or output
bytes — the plan should be more explicit about the contract, risks, and test
coverage before asking for approval.

If the task is purely mechanical or trivial, say so and proceed with a brief
note rather than a full plan.

If scope expands mid-task, stop and revise the plan before continuing — this is
the planning-time face of "surgical changes" (principle P-1) and of the
diff-growth rule in the git-discipline section above.

Do not implement first and explain later.

## Current status

Design phase complete. Final review passed.
Next up: **M0 — solution skeleton + golden-fixture harness.**
See `docs/roadmap.md`.
