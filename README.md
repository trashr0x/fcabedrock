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

Under active development. **M1 is complete**: the full pipeline — v2 `.bed`
spec reader, wide CSV/TSV source, planner, streaming emitter, `.cxt`/`.dat`
writers — reproduces FcaBedrock v2 byte-for-byte on the mini-mushroom and
mini-adult fixture families (under `--v2-compat`).

What works today vs what is designed but not yet implemented:

- **Today (M1):** wide CSV/TSV input; v2 `.bed` specs; deterministic
  `.cxt`/`.dat` output, with a `--v2-compat` preset.
- **M2 (in progress):** the TOML Bedrock spec format, fingerprinting, and a
  one-way `.bed` → TOML migrator.
- **M3 and beyond:** subject–predicate–value triple input (M3), auto-binning
  discretizers (M4), discovery, templates, the full CLI, benchmarks, and a
  desktop UI.

`docs/roadmap.md` is the live source for the current position.

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
