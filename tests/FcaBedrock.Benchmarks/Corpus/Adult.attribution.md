# UCI Adult attribution

The benchmark suite's `External` cases read the UCI Adult dataset. This file records its source,
its licence, and how the suite uses it.

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
| Entry used | `adult.data`: the training split, headerless, 15 comma-delimited columns |
| Entry bytes | 3,974,305 |
| Entry SHA-256 | `5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d` |

**Citation.** Becker, B. and Kohavi, R. (1996). *Adult*. UCI Machine Learning Repository.
<https://doi.org/10.24432/C5XW20>.

## How the suite uses it

The data is not stored in this repository. Only `prepare adult`, and `prepare all`, which includes
it, download the archive; they write the `adult.data` entry verbatim. No test, benchmark run or CI
job downloads it, and only a run that names the `External` category selects its cases.

The length and SHA-256 above are pinned in `AdultCorpus.cs` and checked on download and on every
later use. A file that does not match is refused. The pin shows that the bytes are the expected
ones, not who published them.

The published file ends with an empty line. The reader skips blank records, so the 32,561 census
rows convert to 32,561 objects.

## What is ours

The Bedrock spec in `AdultSpecs.cs` (the cuts, the columns that become attributes, and the
handling of `?` cells in `workclass`, `occupation` and `native-country`) is FcaBedrock material.
It is not part of the dataset and is not covered by its licence.
