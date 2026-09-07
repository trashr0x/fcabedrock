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

**Citation.** Becker, B. and Kohavi, R. (1996). *Adult*. UCI Machine Learning Repository.
<https://doi.org/10.24432/C5XW20>.

## What is acquired, and what is committed

The data is **not** in this repository and never will be. `fcabedrock` benchmarks acquire it under
the explicit `prepare adult` verb, write the archive entry's bytes verbatim, and record the exact
byte length and SHA-256 in the corpus catalog. Preparation is outside every measured interval, and
no ordinary test or benchmark measurement performs any network access.

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
