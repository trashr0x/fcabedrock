# UCI Adult — attribution and provenance

The M8 benchmark suite measures one **externally acquired** corpus. This file records where it
comes from, what licence it carries, and which part of the case is ours rather than theirs.

## Dataset

| | |
| --- | --- |
| Name | Adult (also known as "Census Income") |
| Authors | Barry Becker and Ronny Kohavi |
| Year | 1996 |
| Publisher | UCI Machine Learning Repository |
| DOI | <https://doi.org/10.24432/C5XW20> |
| Licence | Creative Commons Attribution 4.0 International (CC BY 4.0) |
| Archive | <https://archive.ics.uci.edu/static/public/2/adult.zip> |
| Entry used | `adult.data` — the training split, headerless, 15 comma-delimited columns |
| Entry bytes | 3,974,305 |
| Entry SHA-256 | `5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d` |

**Citation.** Becker, B. and Kohavi, R. (1996). *Adult*. UCI Machine Learning Repository.
<https://doi.org/10.24432/C5XW20>.

## What is acquired, and what is committed

The data is **not** in this repository and never will be. `fcabedrock` benchmarks acquire it under
the explicit `prepare adult` verb, write the archive entry's bytes verbatim, and record the exact
byte length and SHA-256 in the corpus catalog. Preparation is outside every measured interval, and
no ordinary test or benchmark measurement performs any network access.

## The pinned identity, and what it is worth

The length and digest above are pinned in `AdultCorpus.cs` and enforced twice: on a fresh
acquisition, before any catalog entry is written, and on every reuse — the second **independently of
the catalog's own recorded digest**. That second check is the point. The catalog records whatever
arrived, so a changed upstream file and a catalog rewritten beside it agree with each other
perfectly, and every figure recorded against "the Adult corpus" would then describe different bytes
without anything saying so. A download that does not match is refused and removed, never adopted.

It establishes **byte identity with the file M8 measured** — the one in `docs/benchmarks.md` — and
nothing more. It is not a signature and says nothing about publisher authenticity; no attestation
for this dataset exists to check against. Changing the accepted identity is a deliberate act that
bumps the acquisition revision alongside it, so previously prepared corpora are refused rather than
silently reinterpreted, and the evidence that depended on the old bytes is re-established.

## Where it runs, and where it does not

The Adult cases carry the opt-in `External` benchmark category. A bare run, a broad `--filter '*'`,
an `*Adult*` filter, and every routine native CI job **exclude** them, so no ordinary run and no CI
job depends on `archive.ics.uci.edu` being reachable; an outage there cannot fail a build that has
nothing to do with it.

That is a narrowing of where the evidence comes from, not a waiver of it. A successful
`--anyCategories External` run of all three cases on the final Windows x64 candidate is a
**blocking** acceptance obligation for M8 and for each release candidate (D-124, `docs/roadmap.md`).
Three failure modes are kept apart: an unreachable host leaves the obligation outstanding; a
length or digest mismatch is an input-identity failure to investigate; and a failure with verified
bytes is a correctness finding. None of the three can become a pass.

What *is* committed is the curated Bedrock spec in `AdultSpecs.cs`: the choice of cuts, of which
columns become attributes, and of how the `?` cells in `workclass`, `occupation`, and
`native-country` are handled. That is authored FcaBedrock material — an analyst's decisions about
someone else's data — and it is not part of the dataset or covered by its licence.

## One property of the published file, recorded rather than smoothed away

`adult.data` ends with a **doubled newline**. It therefore carries the 32,561 census rows everyone
cites *plus one empty final row* — 32,562 records — and a reader is right to yield that row: under
RFC 4180 a doubled line break ends one record and begins another. Converting the file as published
produces a 32,562nd object with no crosses, and an `ObjectHasNoCrosses` warning saying so.

The suite records the count the pipeline actually reads, not the count the literature quotes. Two
alternatives were rejected: trimming the trailing line at preparation would mean the recorded digest
no longer describes the published bytes, and counting only non-empty lines would make every derived
rate slightly wrong while hiding the fact. A benchmark over real data is worth having precisely
because real data has corners like this one.

## Why a real corpus is in the matrix at all

Every other family here is synthetic, designed by the same person who wrote its expectations. Adult
was not: its distributions, missing cells, and domain sizes were fixed by a census long before this
tool existed, and the original FcaBedrock used it. A measurement over it is therefore evidence that
the synthetic families cannot supply on their own.

Because its output is data-dependent rather than derivable, the Adult case is validated against a
**stable reviewed baseline** plus semantic assertions — object count, attribute count, expected
diagnostics — rather than against an independent oracle. The baseline hash is regression evidence;
it is not proof of semantic correctness, and this suite does not present it as such.
