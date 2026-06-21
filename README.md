# FcaBedrock vNext

A ground-up .NET 10 rewrite of **FcaBedrock**, a preprocessing tool for
**Formal Concept Analysis (FCA)**. It ingests structured data (wide CSV/TSV and
subject–predicate–value triples), applies a user-curated TOML *Bedrock spec*
describing how raw values become formal-context attributes via conceptual
scaling, and emits deterministic **Burmeister `.cxt`** and **FIMI `.dat`**
formal-context files for downstream FCA tools (ConExp, In-Close, etc.).

The design centres on one idea: **discretization and scaling are orthogonal.** A
*discretizer* maps a raw value to a bin label; a *scale* maps a bin label to
zero or more formal attributes. Every legacy attribute "type" is a
(discretizer, scale) pair, and new combinations fall out for free.

## Status

Pre-implementation. The design phase is complete; see `docs/` for the normative
spec and the engineering record. Implementation starts at milestone **M0**
(`docs/roadmap.md`).

## Documentation

- **`docs/bedrock-spec-v1.md`** — the normative Bedrock file-format spec.
- **`docs/principles.md`** — engineering invariants the code must satisfy.
- **`docs/decisions.md`** — architectural decision log, with rationale.
- **`docs/roadmap.md`** — milestones M0–M9 and the deferred backlog.
- **`docs/lineage.md`** — what the predecessors (v2, the PhD thesis, the
  SPARQL2FCA prototype) settled, distilled.
- **`CLAUDE.md`** — repo orientation for contributors and coding agents.

## Lineage and credits

This project is a from-scratch successor to the original **FcaBedrock** (v2,
VB.NET), developed during the author's PhD at Sheffield Hallam University and
published as:

> Andrews, S., Orphanides, C.: *FcaBedrock, a Formal Context Creator.*
> In: Croitoru, M., Ferré, S., Lukose, D. (eds.) ICCS 2010. LNCS, vol. 6208,
> pp. 181–184. Springer, Heidelberg (2010).

The original tool was co-authored with **Simon Andrews**, the author's PhD
supervisor, and remains available on
[SourceForge](https://sourceforge.net/projects/fcabedrock). vNext is a
sole-authored rewrite that reproduces the v2 compatibility behaviour (verified
against the v2 output as byte-equality golden tests) while modernising the
architecture; the conceptual-scaling semantics derive from the author's PhD
thesis on appropriating structured data for FCA. The SPARQL2FCA prototype
informs the (deferred) triple-store source path.

## License

MIT — see `LICENSE`. (Same license as FcaBedrock v2.)

Copyright © Constantinos Orphanides.
