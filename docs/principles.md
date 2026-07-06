# Engineering Principles — FcaBedrock vNext

Project-wide invariants to check code against before committing. These are
**not** general "write good code" advice — each one states a choice whose
opposite a competent engineer might reasonably make, and each tells you
something to do (or not do) at a specific moment. If a principle here ever
reads as obviously-true-and-unactionable, it has failed and should be cut.

Scope boundaries with the other docs:

- `AGENTS.md` = where things live, how a session works (operational).
- `decisions.md` = why we chose a specific thing on a specific date (rationale).
- `roadmap.md` = what's next (sequence).
- **`principles.md` (this file) = what's always true, that code must conform to.**

Mechanical rules a tool can check (naming, formatting, `var`, usings) live in
`.editorconfig` and the analyzer ruleset, **not here**. If a linter can enforce
it, it should — this doc holds only the judgment calls a linter can't make.

The first section (**Working discipline**, P-1…P-6) governs *how you work on
the codebase* and matters most for AI-agent sessions, which tend to expand
scope. The remaining sections (**Correctness, Architecture, Performance,
Testing**, P-7…P-22) are invariants about *the system itself*. There are 22
principles in total; the count is deliberate, not a target — add or cut only
under the test stated above.

Internal cross-references use number + short title (e.g. `P-3 ("Do not invent
callers…")`) so that renumbering stays painless.

---

## Working discipline

### P-1 — Changes are surgical unless the task is explicitly a refactor

Touch only what the task requires. Do not restyle adjacent code, fix unrelated
issues, rename things opportunistically, or expand scope because you noticed
something. If a broader cleanup is warranted, propose it separately or record it
as follow-up work. Use `roadmap.md` or the project backlog for cleanup; use
`decisions.md` only when the follow-up records an architectural or project
decision. A diff that grows outside the requested behavior needs a stated
reason. This applies with extra force in agent sessions, where scope creep is
the default failure mode.

*Check when:* any diff touches files, APIs, or behavior not directly needed by
the task.

### P-2 — Do not silently violate project laws; name the conflict

The spec, decisions, roadmap, and these principles are constraints, not
suggestions. Do not silently bend one to satisfy a local change. When a task
conflicts with a project constraint, stop and name the conflict — challenge the
request and explain the break rather than complying quietly. When the right
behavior is unclear, verify it from code, tests, fixtures,
`bedrock-spec-v1.md`, `roadmap.md`, or `decisions.md`; if it still can't be
resolved, ask, or state the assumption explicitly in the change. Resolving an
ambiguity by guessing and staying silent is the thing this rule forbids.

*Check when:* a requested change appears to conflict with the spec, an existing
decision, a fixture, or any invariant below; or when behavior is unclear and
you're tempted to guess.

### P-3 — Do not invent callers, requirements, or futures

Do not justify a branch, option, interface, constructor parameter, compatibility
path, or public method with an imagined future caller or hypothetical
requirement. If the caller or requirement is real, verify it (in code, the
roadmap, or the spec) and cite it. If it isn't, don't build for it — YAGNI is
project policy here, not a preference. (The config-surface special case of this
rule is P-6 ("No speculative knobs").)

*Check when:* adding any surface — a branch, option, interface, public method,
constructor parameter, or compatibility path — whose only justification is "we
might need it."

### P-4 — Public surfaces are designed before implementation

For cross-package APIs, planner contracts, diagnostics, writers, sources, and
spec-facing types, define the public shape first: types, signatures, the
result/diagnostic model, ordering guarantees, streaming behavior, and
determinism expectations. Implementation can move freely behind that surface;
the surface itself should not churn casually. This explicitly does **not** apply
to `internal`/`private` types within a package — internal refactoring stays
cheap.

*Check when:* adding or changing a `public` type, interface, diagnostic, writer,
source, or package seam.

### P-5 — One project-standard way per concern

Use the established project-standard result type, diagnostic style, parser
style, test style, benchmark style, and writer pattern. Do not introduce a
second approach to a solved concern unless the current one genuinely can't do
the job — and when that happens, record the new standard in `decisions.md` and
migrate toward it, rather than leaving two competing patterns in place.

*Check when:* introducing a new library, helper pattern, test style, result
type, diagnostic shape, parser approach, serialization approach, or writer
pattern.

### P-6 — No speculative knobs

Options, flags, constructor parameters, modes, and feature switches need a
current caller and a documented reason. Do not add configurability because
someone might want it later; the general form of this rule is P-3 ("Do not
invent callers, requirements, or futures"). If a behavior is project
policy, encode it as policy, not as an option. Test seams are allowed when they
improve isolation, determinism, or coverage without creating different
production behavior. Required compatibility modes (e.g. `--v2-compat`) are
allowed when documented.

*Check when:* adding a CLI flag, options property, constructor parameter,
feature switch, compatibility mode, test seam, or alternative code path.

## Correctness

### P-7 — Determinism is a test, not an aspiration

Every path that produces output (a plan, an emitted stream, a `.cxt`/`.dat`
file, a fingerprint) has a test proving same-input ⇒ same-output, byte-for-byte
where applicable. **"I'll add the determinism test later" is disallowed for
these paths specifically** — the test ships in the same commit as the path.

*Check when:* adding or changing anything in Conversion, Export, or the planner.

### P-8 — The spec is the contract; code conforms to the spec

`bedrock-spec-v1.md` is normative. When code and spec disagree, the code is the
bug — or the spec gets a *reviewed* change with a `decisions.md` entry. Never a
silent divergence, never "the code is what it really does." Behavior is not
allowed to be discovered by reading the implementation.

*Check when:* implementing any spec'd field, default, or output format.

### P-9 — Golden fixtures are compatibility evidence; never edit them to pass

The `fixtures/v2/` files record what v2 actually produced — they are evidence of
v2 behavior, not a definition of correctness (v2 has known bugs; see
`lineage.md`). A golden mismatch is fixed by changing vNext code, or by
documenting an intentional divergence with a `decisions.md` entry and
compatibility behavior where required. **Never edit a fixture merely to make
current output pass** — that destroys its evidentiary value.

*Check when:* a golden test fails.

### P-10 — Make illegal states unrepresentable before validating against them

Prefer a type that cannot hold a bad value over a runtime check that rejects
one. A discriminated union, a private constructor with a smart factory, a
`readonly struct` with validated construction — these beat scattered guard
clauses. Validate at the boundary (untrusted input), then trust the type
inward. The opposite (defensive checks everywhere) is a real and common
practice; we reject it.

*Check when:* designing any Core domain type.

### P-11 — Floating-point and locale are determinism hazards, handled explicitly

Bin cuts, equal-frequency quantiles, and number/date parsing must specify
rounding and culture. Default to `CultureInfo.InvariantCulture`; never rely on
ambient locale. Where float cuts feed labels or fingerprints, the rounding rule
is explicit and tested (a cut that renders as `34.25` on one machine and
`34.250000001` on another is a determinism bug).

*Check when:* implementing discretizers, parsers, or anything numeric in output.

### P-12 — Strings compare and sort ordinally; culture-aware collation is a determinism hazard

String identity, equality, matching, deduplication, grouping, source binding, and
any deterministic *ordering* of strings use ordinal comparison
(`StringComparer.Ordinal` / `StringComparison.Ordinal` — a UTF-16 code-unit
compare), never a culture-aware one. Culture-aware collation, `InvariantCulture`
included, is ICU/NLS-version dependent: the same two strings can order or match
differently across machines and runtimes — a determinism bug on any path feeding
output bytes, IDs, or fingerprints (e.g. the unordered triple subject sort, spec
§17 rule 4). This is the string-side companion to P-11 ("Floating-point and
locale…"): `binding.locale` governs numeric/date *parsing* only (decimal
separators), never string collation — the two are separate concerns and must not
be conflated. Where a spec section fixes a *non-string* order (numeric or
positional cut order), that order governs; this principle is about string-keyed
comparison and ordering.

*Check when:* sorting, comparing, matching, deduplicating, grouping, or binding by
any string key — object names/keys, triple subjects, predicate selectors, header
names, declared-domain values, bin labels.

## Architecture

### P-13 — Core is pure: no I/O, no UI, no ambient state

`FcaBedrock.Core` references only `System.*` and `FcaBedrock.Diagnostics`. No
file access, no network, no `Console`, no `DateTime.Now`/`Guid.NewGuid` in
logic paths (inject them), no static mutable state. The test: every public Core
type is constructible and exercisable in a unit test without a file, a UI, or a
network. If you reach for `System.IO` in Core, the design is wrong.

*Check when:* adding any type or dependency to Core.

### P-14 — Errors are values at package boundaries; exceptions are for the unexpected

The project default for expected failures is result/diagnostic values, not
exceptions. Across package seams and for anything a caller can sensibly handle,
return `Result<T, BedrockDiagnostic>` or `Diagnosed<T>`. Exceptions are reserved
for programmer errors, impossible states, corrupt invariants, and genuinely
unexpected failures. Internal helpers may throw for violated preconditions or
impossible states; public package APIs map expected failures to diagnostics.
Aggregating operations such as validate and plan collect all diagnostics, not
just the first.

The distinction:

- bad spec field / invalid domain value / validation failure / handleable
  infrastructure failure: result/diagnostic.
- programmer error / impossible invariant / corrupt internal state: exception
  or assertion.

*Check when:* designing any public method that can fail.

### P-15 — Exporters are dumb; semantics happen before export

A writer serializes an already-decided result and makes zero scaling, ordering,
or policy decisions. A writer may *preserve* an order the planner already
decided — deterministic byte output requires it to honor that order — but it
must not *decide* semantic order itself. If a writer contains an `if` about
*what* to cross or *which* attribute comes first, that logic belongs in the
planner. The writer's only choices are byte-level formatting (line endings,
separators).

*Check when:* touching anything in Export.

### P-16 — The pipeline stays streaming; never materialize the full incidence matrix

`Emit` yields objects; the set of all crosses is never held in memory at once in
Core or Conversion. Bounded metadata collections are fine — object names, the
attribute list, calibration cuts; emitted objects, crosses, and incidence cells
are not. The `.cxt` writer (which needs counts and all names before any
incidence row) may **replay the source plus hold a bounded object-name buffer,
or spool incidence rows to a temp sink** — but never the full matrix in memory.
A change that buffers all emitted objects' crosses to a `List` before writing
violates this; materializing the (bounded) list of object names for a header
does not.

*Check when:* working in Conversion or Export, especially anything that calls
`.ToList()` / `.ToArray()` on an emit stream (as opposed to on a bounded
metadata collection).

### P-17 — Compose small pieces at real variation points; prefer composition over inheritance

Discretizers, scales, sources, and writers are small and single-purpose,
composed by the planner. Use interfaces at real variation points or test seams —
where isolation, determinism, or substitutability is actually needed. Do not
create one-interface-per-class abstractions by reflex; but do not make code hard
to test just to avoid an interface. A type that both decides cuts *and* formats
labels *and* writes bytes is three types wearing a trenchcoat.

Prefer composition over inheritance. Shared behavior belongs in composed
collaborators or helpers unless there is a genuine substitutable "is-a"
relationship. Inheritance is not a code-sharing mechanism.

*Check when:* a class crosses ~2 responsibilities or grows a second
`enum`-switch; or when you're about to add an interface, base class, or
`abstract` member.

## Performance

### P-18 — Allocation discipline is scoped to hot paths, not blanket

The emit loop and the byte-level parser are allocation-audited: prefer
`Span`/`Memory`, pooled buffers, `ValueTask`, no per-object closures or boxing.
Cold paths (spec parsing, CLI arg handling, discovery setup, calibration) are
written for clarity first — micro-optimizing them is wasted effort and added
risk. **Knowing which paths are which is the principle**; "optimize everything"
and "optimize nothing" are both wrong.

*Check when:* writing in Sources (parse loop) or Conversion (emit loop), audit
allocations; elsewhere, write for clarity.

### P-19 — Performance claims are measured, not asserted

Any "this is faster / lower-allocation" change to a hot path is backed by a
BenchmarkDotNet result in `FcaBedrock.Benchmarks`, not by intuition. A clever trick
justified by performance needs benchmark evidence; otherwise prefer the simpler
code. Non-obvious code that exists for *determinism or correctness* rather than
performance is governed by P-7 ("Determinism is a test, not an aspiration") and
P-11 ("Floating-point and locale are determinism hazards, handled explicitly"),
not by this one — but make the reason visible (in a test name, type name,
benchmark, nearby comment, or `decisions.md` entry) so the next reader does not
mistake it for cleverness. Modern APIs are used where *justified*, not because
they're modern.

*Check when:* introducing any non-obvious performance construct.

### P-20 — Large-scale tests are opt-in and never gate the normal suite

The 7.3M / 73M synthetic datasets live in `FcaBedrock.Benchmarks`, behind a category
filter. `dotnet test` stays fast and runs on the mini fixtures. A multi-minute
benchmark must never be reachable by a plain `dotnet test`.

*Check when:* adding any test that needs a large dataset.

## Testing

### P-21 — Golden and policy/property tests, where each applies

Where output bytes are affected, add or update a **golden test** (proves
byte/output compatibility). Where semantics are affected, add or update a
**policy/property test** (proves missing handling, exclusions, declared domains,
object restriction, ordering invariants, fingerprint stability). New scales and
discretizers usually require both; a pure internal refactor with no behavior
change may require neither.

*Check when:* adding or changing any scale, discretizer, source, policy, writer,
or output-affecting behavior.

### P-22 — A behavior bug fix starts with a failing test

Before fixing a behavior bug, write the test that fails because of it; the fix
is correct when that test goes green and nothing else goes red — a permanent
regression guard, which matters most on the silent determinism and byte-equality
paths. This rule applies to behavior bugs; docs/comments-only changes are out of
scope. Exceptions: the failure is already covered by an existing test, or it
cannot reasonably be reproduced in the normal suite (infra issue, dead-code
removal, emergency mitigation). If no failing test is added, document why in the
change.

*Check when:* fixing anything that changes behavior.

---

## How to use this file

- Before a commit, skim the "Check when" lines relevant to what you touched.
- A principle that keeps getting in the way is either wrong (cut it, with a
  `decisions.md` entry recording why) or right and being resisted (fix the
  code). It is never quietly ignored.
- New principles are added the same way: only if the opposite is conceivable
  and the rule is actionable at a specific moment. Platitudes get rejected in
  review.
- Mechanical conventions belong in `.editorconfig` / analyzers. If you find
  yourself writing a principle a tool could check, move it there instead.
- **Exceptions to these principles are allowed only when explicit, local, and
  justified in the change description or `decisions.md`. A silent exception is
  not an exception — it is drift.**
