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

**M8 (a scaling and benchmark pass) is next**, followed by the desktop UI (M9).

`docs/roadmap.md` is the live source for the detailed current position.

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

The preview is validated on **x64**, with Windows as the primary host; broader
platform validation is an M8 task. The global tool is explicitly a **temporary**
technical-preview distribution — a standalone, self-contained route is committed
for the public release.

## Documentation

- **`docs/bedrock-spec-v1.md`** — the normative Bedrock file-format spec.
- **`docs/principles.md`** — engineering invariants the code must satisfy.
- **`docs/decisions.md`** — architectural decision log, with rationale.
- **`docs/roadmap.md`** — milestones M0–M9 and the deferred backlog.
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
