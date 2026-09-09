# FcaBedrock vNext

A ground-up .NET 10 rewrite of **FcaBedrock**, a preprocessing tool for
**Formal Concept Analysis (FCA)**. It ingests structured data, applies a
user-curated *Bedrock spec* describing how raw values become formal-context
attributes via conceptual scaling, and emits deterministic **Burmeister
`.cxt`** and **FIMI `.dat`** formal-context files for downstream FCA tools
(ConExp, In-Close, etc.).

The design centres on one idea: **discretization and scaling are orthogonal.** A
*discretizer* maps a raw value to a bin label; a *scale* maps a bin label to
zero or more formal attributes. Every legacy attribute "type" is a
(discretizer, scale) pair, and new combinations fall out for free.

## Status

Under active development. **M1–M7 are complete.** The pipeline reproduces
FcaBedrock v2 byte-for-byte on the mini-mushroom and mini-adult fixture families
(under `--v2-compat`).

Implemented today:

- **Input:** wide CSV/TSV **and** subject–predicate–value triple sources.
- **Specs:** legacy `.bed` **and** the native TOML format — round-trip,
  composition (`extends`), templates and matchers, the three plan-derived
  fingerprints, and one-way `.bed` → TOML migration.
- **Conversion:** every v1 discretizer kind, calibration, and `restrict_to`.
- **Output:** deterministic `.cxt`/`.dat`, with a `--v2-compat` preset.
- **Discovery:** `probe` draft-spec generation for both wide and triple sources,
  as a library API **and** as the `probe` command.
- **CLI:** the `fcabedrock` command, shipping all eight commands — `convert`,
  `validate`, `plan`, `stats`, `calibrate`, `probe`, `migrate` and
  `fingerprint` — with the run manifest, the publication transaction, and the
  freeze engine.

**M8 — a scaling and benchmark pass — is in progress**, followed by the desktop
UI (M9). It adds an internal BenchmarkDotNet suite over the real production
paths, target-scale evidence at 7.3M and 73M input records, a self-contained
standalone distribution beside the global tool, and per-platform build, test,
accounting, and packaging checks.

`docs/roadmap.md` is the live source for the detailed current position, and
`docs/benchmarks.md` is the measurement record.

## Continuous integration

Every push and pull request builds, tests, checks resident-memory accounting,
smoke-tests both distributions, and produces a tested archive on each supported
native target: **Windows x64**, **Linux x64**, and **macOS ARM64**. Linux ARM64
and Windows ARM64 run additionally where available; they carry no support claim
and produce no distribution archive.

The workflow is `.github/workflows/ci.yml`. It is deliberately **not** a
performance measurement: hosted runners are shared and of unstated provenance, so
the only benchmark step there is a `Dry` run that executes each **Small-category**
case once and measures nothing. Comparative performance belongs to controlled,
identified hardware and is recorded in `docs/benchmarks.md`.

Every input those jobs need comes from this repository: the benchmark corpora they
prepare are generated from pinned arithmetic. The one corpus that is downloaded
rather than generated — the UCI Adult training split — is deliberately outside
routine CI, so an outage at a research-data host cannot fail a build that has
nothing to do with it. Its cases are run explicitly on the release candidate
instead, which `docs/benchmarks.md` records.

Each run of the three required targets uploads its self-contained archive as a
build artifact, retained for seven days.

## Installation

`FcaBedrock.Cli` is a **technical preview**. It is **not yet published to a
public NuGet feed**, so the global-tool command below applies only once the
package is available on a NuGet source you have configured.

Install the .NET 10 SDK first. On Windows:

```text
winget install Microsoft.DotNet.SDK.10
```

On other platforms, install the .NET 10 SDK by the method your platform
documents.

Then, once the package is available on a configured NuGet source:

```text
dotnet tool install --global FcaBedrock.Cli
```

Until it is published, install it from a local pack of this repository — the
same package the installed-tool smoke test installs under `--tool-path`:

```text
dotnet pack src/FcaBedrock.Cli/FcaBedrock.Cli.csproj -c Release -o <dir>
dotnet tool install --global --add-source <dir> FcaBedrock.Cli
```

Check the installation, and read the full grammar:

```text
fcabedrock --version
fcabedrock --help
```

To remove it:

```text
dotnet tool uninstall --global FcaBedrock.Cli
```

Per-command usage: see the [packed command guide](src/FcaBedrock.Cli/README.md).

### Standalone (no .NET installed)

The global tool is **framework-dependent**: it needs a matching .NET runtime on
the machine. The standalone distribution does not — it carries the runtime beside
the executable.

Build one for your platform:

```text
./eng/publish-selfcontained.ps1
```

That writes `artifacts/publish/<rid>/` and `artifacts/publish/fcabedrock-<rid>.zip`.
Unzip it anywhere and run the executable directly; it keeps its assembly name,
`FcaBedrock.Cli` (`.exe` on Windows), because the `fcabedrock` command name
belongs to the global-tool shim.

```text
./FcaBedrock.Cli --version
./FcaBedrock.Cli convert spec.toml data.csv --out out/context --format both
```

The required archives are `win-x64`, `linux-x64`, and `osx-arm64`, and CI
produces each of them on its own platform, then extracts that exact archive and
runs the extracted executable before uploading it — a cross-published folder
shows only that the SDK can emit files for another target, not that the result
runs there. On Linux and macOS the archive records `FcaBedrock.Cli` as
executable, so `./FcaBedrock.Cli` works straight out of the unzip.
"Self-contained" bundles .NET, not the operating system: globalization still uses
the host's ICU, which Windows 10+ ships and a Linux host provides through its
`libicu` packages.

The global tool remains a **temporary** technical-preview distribution.

## Documentation

- **`docs/bedrock-spec-v1.md`** — the normative Bedrock file-format spec.
- **`docs/principles.md`** — engineering invariants the code must satisfy.
- **`docs/decisions.md`** — architectural decision log, with rationale.
- **`docs/roadmap.md`** — milestones M0–M9 and the deferred backlog.
- **`docs/benchmarks.md`** — the M8 measurement record: what is measured, on
  what hardware, under what rules, and what has *not* been measured.
- **`eng/README.md`** — the packaging and smoke commands.
- **`docs/lineage.md`** — what the predecessors (v2, the PhD thesis, the
  SPARQL2FCA prototype) settled, distilled.
- **`AGENTS.md`** — repo orientation for contributors and coding agents
  (`CLAUDE.md` is its Claude Code import shim).

## Test fixtures

vNext's golden tests compare output byte-for-byte against fixtures produced by
FcaBedrock v2, under `fixtures/v2/`. Fixture-level dataset provenance and
attribution are documented in `fixtures/v2/ATTRIBUTION.md`.

## Lineage and credits

This project is a from-scratch successor to the original **FcaBedrock** (v2,
VB.NET). Initial versions of FcaBedrock were created as part of the author's BSc
Computing final year project, and subsequent versions were developed during the
author's PhD at Sheffield Hallam University on appropriating structured data for
Formal Concept Analysis (FCA).

The original FcaBedrock tool was published as:

> Andrews, S., Orphanides, C.: *FcaBedrock, a Formal Context Creator.*
> In: Croitoru, M., Ferré, S., Lukose, D. (eds.) ICCS 2010. LNCS, vol. 6208,
> pp. 181–184. Springer, Heidelberg (2010).

The ICCS 2010 paper is the primary publication reference for the original tool.
The later PhD thesis provides the fuller treatment of FCA data appropriation and
the conceptual-scaling semantics that inform vNext.

The original tool remains available on
[SourceForge](https://sourceforge.net/projects/fcabedrock). vNext is a
sole-authored rewrite that reproduces the v2 compatibility behaviour (verified
against the v2 output as byte-equality golden tests) while modernising the
architecture. The SPARQL2FCA prototype informs the deferred triple-store source
path.

## License

MIT — see `LICENSE`. (Same license as FcaBedrock v2.)

Copyright © Constantinos Orphanides.
