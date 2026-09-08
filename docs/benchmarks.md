# M8 benchmarks — evidence pack

The M8 measurement record: what is measured, on what, under what rules, and what the numbers say.
The architecture and its rationale are `decisions.md` **D-124**; how to run the suite is
`tests/FcaBedrock.Benchmarks/README.md`. This file is the evidence.

> **Status.** The suite, its corpora and oracles, the CLI-host and hashing coverage, the standalone
> distribution and its smoke, the `eng/` commands, and `.github/workflows/ci.yml` are implemented and
> green. The controlled Windows x64 baseline is measured at the working (730,000), 7.3M, and 73M
> tiers. The suite found one production defect — multi-attribute count-sensitive calibration refusing
> a valid spec on healthy storage — and it is **fixed**, with its affected measurements replaced; see
> [Corrected defect](#corrected-defect--many-attribute-count-sensitive-calibration). **Nothing has
> executed on Linux or macOS**, and no external repository, workflow run, or published artifact
> exists — see [What has not been measured](#what-has-not-been-measured). The one acquired corpus is
> outside routine CI and carries its own acceptance obligation; see
> [Selection, and what routine CI proves](#selection-and-what-routine-ci-proves).

## What a number here means

Every timed operation starts from a corpus prepared on disk with **nothing open**, and the measured
interval covers opening, the full awaited production operation, complete consumption, and the final
flush and disposal. Corpus generation, output reset, oracle derivation, harness hashing, and
validation are all outside it.

Every completed iteration is validated **after** disposal: the artifact must have exactly the
expected length and digest, or the calibration exactly the expected cuts and domain, and the run must
carry no diagnostic that would oblige a caller to discard its output. A validation failure throws, so
**a case that produced the wrong result has no throughput number at all**. The launcher exits
non-zero on any build, execution, or validation failure, and also when a run measured nothing.

Expected results are derived from the corpus definition plus the documented spec semantics, never by
invoking the code under test. Where the population makes an exact expectation derivable it is exact:
the equal-frequency cuts for `n_seq` are order statistics over a known arithmetic sequence, so the
oracle states them outright. Where it does not — a deliberately tie-heavy population whose exact cuts
would mean re-implementing the feasibility algorithm under test — the check is shape plus an
independent equality, and this file says which is which rather than implying the stronger one.

Two corpora are **external** checks on a suite whose other expectations are authored in this
repository: the immutable v2 minis, produced by a different program years earlier and compared
byte-for-byte, and UCI Adult, whose distributions nobody here chose.

### Three memory meanings, never interchanged

1. **Allocated** in the tables below is BenchmarkDotNet's process-wide **managed allocated bytes per
   operation**, with its GC collection counts. It is not peak live memory, not native allocation, and
   not another process's memory. It is reported beside the recorded input-record and input-byte
   denominators, so a per-record figure can be derived and rechecked against the corpus catalog.
2. **Modelled retained bytes** come from the grouping and calibration observers and from the
   retained-layout witnesses. They are scoped algorithmic guarantees over a stable graph, not a
   whole-process limit.
3. **Sampled working set** comes from separate `dotnet-counters` traces of the real self-contained
   command. A sampled maximum is a **lower bound** on the true peak, and no sample is not zero.

## Environment

Every measurement below was taken on one machine, in one identified configuration.

| | |
| --- | --- |
| Revision | `agent/m8-scaling-reset`, based on `main` at `38b3dda` |
| OS | Windows 11 Pro (10.0.26200) |
| CPU | AMD Ryzen 5 5600X — 1 CPU, 6 physical / 12 logical cores |
| Runtime | .NET 10.0.11, X64 RyuJIT AVX2, **Concurrent Workstation GC** (the product default, not forced) |
| SDK | 10.0.302 |
| BenchmarkDotNet | 0.15.8, out-of-process toolchain, Release |
| Storage | `D:` — Samsung SSD 850 EVO 500 GB, SATA, NTFS; **not** the system volume, and carrying no page file |
| Bench root | `D:\tmp\fcabedrock-m8-g15-bench` — corpora, outputs, **grouping spools**, and results |

**Storage is a stated condition, not an accident.** Corpora, produced artifacts, BenchmarkDotNet's
own results, and the grouping backend's sort-merge spool all live on one identified volume: the suite
points the backend at its own root through the production `GroupingOptions.TempDirectory` seam — the
same one the CLI's `--temp-dir` uses — rather than letting it default to the OS temporary directory
on the system drive. Without that, a scale run would be measuring two devices at once, and a
multi-gigabyte spill would land on the volume the operating system is running from.

**Filesystem cache is warm.** Preparation, validation, and repeated iterations all read the same
files, and no cache is dropped between iterations. These are therefore warm-cache figures where the
corpus fits in RAM. Nothing here is a cold-disk claim, and none is made.

Hosted or otherwise unidentified hardware has produced **no** figure in this file.

## Corpora

Generated data, downloaded data, produced artifacts, and raw results are bulk evidence and stay out
of Git; only generator definitions, specs, attribution, the metadata contract, and tiny expectations
are committed. Every prepared case records its generator revision, geometry, exact byte length, and
SHA-256, and anything that no longer matches is refused rather than measured.

| Case | Records | Cols | Bytes | rev | SHA-256 |
| --- | ---: | ---: | ---: | ---: | --- |
| `w16-small` | 10,000 | 16 | 624,470 | 1 | `0cd1977bcb4abc00381b5086f6a5e321bfb56d420a45f0c50a31e02c1d26248d` |
| `w16-working` | 730,000 | 16 | 46,997,989 | 1 | `06a00c4f5571c8e98344cf1d4e60db4373a17613f382410e6141ca72d13ec459` |
| `w16-scale7m` | 7,300,000 | 16 | 477,289,935 | 1 | `00ae4fbd27a24d26a0baa44e288cc48b051b6ed333bfd9453bb40d0dfe28ace6` |
| `w16-scale73m` | 73,000,000 | 16 | 4,845,890,526 | 1 | `c9d149ff9d5019561da0e0ee08b686593a56192ef3e75565770090aa4c213e82` |
| `t10-grouped-small` | 10,000 | 3 | 258,674 | 1 | `b6bdbed144683360992103d4ef743c108136468e6d207fda308acd3fee28d802` |
| `t10-unordered-small` | 10,000 | 3 | 258,674 | 1 | `a97aec84654e7be474fa92f63c2906e645887ac3296ac3f8c30be9597289d4e6` |
| `t10-grouped-working` | 730,000 | 3 | 18,883,436 | 1 | `5b30d5ce6b9ff960fb57ce155d212cbf358dcbffa0a3969d844be92ef207485b` |
| `t10-unordered-working` | 730,000 | 3 | 18,883,436 | 1 | `f63ecd591f882570f5307e793f2b4f0121a8725dc5225007449174c2eb7b3f81` |
| `t10-grouped-scale7m` | 7,300,000 | 3 | 188,822,966 | 1 | `7ed9e8f237fdb4e711d69a415fe0a3884a43fc9e9f3cf3ff02fabc5d8f4efeae` |
| `t10-unordered-scale7m` | 7,300,000 | 3 | 188,822,966 | 1 | `0f5c3e359b2ec9cfb696d512496f59c43643491d7863a7245b6c61899fde2e5f` |
| `t10-grouped-scale73m` | 73,000,000 | 3 | 1,888,273,396 | 1 | `a924274ae9da2981e536cb41aa27eb105847f7359fe8f5f438ccf879cd24c4ac` |
| `t10-unordered-scale73m` | 73,000,000 | 3 | 1,888,273,396 | 1 | `6cd06b928f21fb5e5242e65a35b25ca52f7c94f0fc5acde0ad1bf223443d8470` |
| `w16keyed-small` | 10,000 | 17 | 744,475 | 1 | `2aaebcf63275ecb672625ca62d556f7394a7add2a661970a0abb084892bc47ba` |
| `w16keyed-working` | 730,000 | 17 | 55,757,994 | 1 | `f36ece4488d1d8460f560978d6af26618b7ee05fa8de6942ef9f62e2f9577306` |
| `w16keyed-scale7m` | 7,300,000 | 17 | 564,889,940 | 1 | `b5ee56a40185fed3ddd4d4017e2647b77e8aaff5f41ae4883b8ad2af281e5c0b` |
| `ads-micro` | 1,000 | 1,559 | 3,139,005 | 1 | `599b84e51d328c2cef8e7b3d756a8c9e60287a81a345443dd3cf58b6e030387b` |
| `ads-small` | 10,000 | 1,559 | 31,306,637 | 1 | `a18e2b06b7aa7b3971c5de5c6af501e4b27ff037da387cb3ef3f729f7877390d` |
| `longtext-small` | 10,000 | 4 | 10,673,639 | 1 | `eb2bafe58228bf1c7479748b7224bd018cd884adef401fbe068f40cb314444cc` |
| `longtext-working` | 730,000 | 4 | 781,351,597 | 1 | `daf06a6167e2a8fb2f7bb63306ec2518ed4682a7d7ad575a9c091a20ba08c84a` |
| `adult` † | 32,562 | 15 | 3,974,305 | 2 | `5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d` |

† The only **acquired** corpus. Its row is not just a record of what was prepared: that length and
digest are **pinned in the source** (`AdultCorpus.cs`) and enforced on every acquisition and reuse,
so this table names one specific file rather than whatever a later download happens to return.

**W16** is sixteen wide columns: four numeric (strictly increasing, heavily tied, skewed, wide signed
decimal), eight categorical over eight-value domains, four binary. It carries both missing forms — an
explicit token and an empty cell — and two domain values that force the RFC 4180 quoting path.

**T10** is ten triple rows per subject in two physical layouts of the *same* observations: contiguous,
and block-interleaved so that first-appearance subject order is unchanged. It carries a multi-valued
predicate, an exact duplicate row, one numeric value in two equivalent raw spellings, and a predicate
no attribute binds. The two layouts have identical byte length and different digests, which is what
"the same rows in a different order" should look like.

**w16keyed** is W16 with a leading object-key column whose values repeat four times and never
adjacently, converted under `duplicate_object_policy = "dedupe"`. Wide dedupe shares the
grouping/sort-merge backend with triple `unordered`, so this measures that backend from the other
side.

**ads** is 1,559 columns — three numeric, a local flag, 1,554 sparse term flags at about 1.2%
density, and a class — matching the geometry of the internet-advertisements workload. It pressures
*width*, and is deliberately outside the target-scale matrix: 1,559 columns do not need seventy-three
million rows to be wide.

**longtext** is four columns whose values are long rather than numerous: eight fixed 512-character
blobs and a per-row note of 64 to 1,024 characters. It is the only family that can reach probe's
retained-*text* guard before its retained-*value* guard.

**adult** is the UCI Adult training split, acquired rather than generated
(`tests/FcaBedrock.Benchmarks/Corpus/Adult.attribution.md`). One property of the published file is
worth stating: it ends with a **doubled newline**, so it carries the 32,561 census rows everyone
cites *plus one empty final row*. A reader is right to yield that row, so the recorded count is
32,562 and the conversion produces a 32,562nd object with no crosses. The suite records the count the
pipeline actually reads rather than the count the literature quotes.

## Selection, and what routine CI proves

Four tier categories. `Small` is the default; `Working`, `Scale`, and `External` are **opt-in by
category and by nothing else** — no name filter and no surface category reaches one.

| Tier | Bare run and routine CI | Reached by | Why it is gated |
| --- | --- | --- | --- |
| `Small` (with `Micro`) | yes | the default | — |
| `Working` | no | `--anyCategories Working` | 730,000 records per case |
| `Scale` | no | `--anyCategories Scale` | 7.3M and 73M records; hours |
| `External` | no | `--anyCategories External` | its corpus is **acquired**, not generated |

The first two are gated by cost. `External` is gated by **dependency**: its three cases are quick
— 32,562 records — but the corpus comes from `archive.ics.uci.edu`, and requiring it in every native
job would let an outage at a research-data host fail a build that has nothing to do with it. Opting
in is not skipping: a *selected* case whose corpus is absent is a hard failure carrying the exact
`prepare` command, and the launcher exits non-zero when a run measured nothing.

**Routine CI** prepares `micro small` and runs `--anyCategories Small --filter '*' --job dry` on each
native target. What a green run proves is exactly what it selected: the Small-category cases and
their oracles on that platform. It does **not** claim Adult ran, on that candidate or on any
non-Windows target. Two Small-category cases are named "mini-adult" — they are the committed
immutable v2 fixtures, compared byte-for-byte against v2's own output, and they are not the acquired
dataset.

**The real-data evidence is required of the candidate instead.** Before M8 acceptance, and for each
later release candidate, all three `External` cases must pass on the final Windows x64 build against
the verified corpus, retained as durable evidence. It is a **blocking** obligation — routine CI can
be green while it is outstanding — enforced by the closure checklist, the independent review, and
operator acceptance, exactly as the controlled 7.3M/73M matrix on this page is. Three outcomes are
kept apart, and none of them can become a pass:

| Outcome | What it means |
| --- | --- |
| UCI unreachable | **evidence-availability** failure; the obligation stays open, no routine job fails |
| length or digest mismatch | **input-identity** failure to investigate; never retried into acceptance |
| failure or oracle mismatch on verified bytes | a **correctness finding** to diagnose |

A retained corpus may be reused only when its pinned identity, its committed spec digest, and its
acquisition revision all verify — which is ordinary verified reuse, not a fallback.

### The acceptance run (2026-09-07, Windows x64)

Run at the amendment's revision, from a hermetic root, after one explicit fresh acquisition whose
bytes matched the pin exactly. Every completed iteration validated after disposal.

| Case | Mean | StdDev | Records | Allocated |
| --- | ---: | ---: | ---: | ---: |
| `adult emit + cxt export` | 110.0 ms | 4.04 ms | 32,562 | 143.95 MB |
| `adult emit + dat export` | 50.58 ms | 0.924 ms | 32,562 | 67.13 MB |
| `wide source drain` (Adult) | 13.05 ms | 0.238 ms | 32,562 | 21.82 MB |

An independent earlier run the same day agreed within variance (112.7 / 49.67 / 13.00 ms) at
identical allocations. These are candidate acceptance measurements on the fresh-iteration job; they
are not part of the controlled baseline below and are not comparable with it.

Full evidence — the hermetic Small Dry smoke (48 cases, no Adult, and identical when repeated in the
same workspace), the selected-but-unprepared failure, the name-filter checks, and the
catalog-stability check against corpora prepared before the amendment — is under
`evidence/s6-github/adult-external-amendment/` in the bench root.

<!-- RESULTS -->

## How to read the tables

`Mean` and `StdDev` are BenchmarkDotNet's, over the iteration count each job declares. `Records/s`
and `MiB/s` are `records / mean` and `input bytes / mean`, using the denominators the corpus catalog
recorded — both are recheckable from the corpus table above. `Allocated` is managed allocation per
operation.

**`B/record` is misleading for the wide family and is omitted there.** An Ads-width record is 1,559
fields against W16's sixteen, so a per-*record* figure compares different amounts of work; divide by
the column count for a per-field figure.

Jobs: Small runs Throughput with one invocation per iteration; Working and 7.3M run **Monitoring**,
1 launch, 2 warmups, 5 iterations; 73M runs the same job with 1 warmup and 3 iterations.

## Results — working tier (730,000 records)

| Surface | Case | Mean | StdDev | Allocated | B/record |
| --- | --- | ---: | ---: | ---: | ---: |
| source | wide drain (W16) | 257.6 ms | 5.9 ms | 501.85 MB | 721 |
| source | triple drain (T10 unordered) | 68.9 ms | 2.2 ms | 84.78 MB | 122 |
| source | long-text drain | 683.9 ms | 2.0 ms | 1.60 GB | 2,359 |
| calibrate | wide, four shapes in one pass † | 582.7 ms | 10.6 ms | 601.49 MB | 864 |
| calibrate | **many-quantile, 16 attributes** † | 2.909 s | 22.3 ms | 609.95 MB | 876 |
| calibrate | triple count-sensitive, grouped † | 138.1 ms | 3.5 ms | 190.83 MB | 274 |
| calibrate | triple count-sensitive, unordered † | 717.3 ms | 4.5 ms | 469.52 MB | 674 |
| calibrate | `include` recovery | 251.3 ms | 8.2 ms | 501.86 MB | 721 |
| calibrate | `value_groups` pass-through | 261.3 ms | 7.7 ms | 501.86 MB | 721 |
| probe | wide (truncating) | 478.4 ms | 13.4 ms | 518.29 MB | 744 |
| probe | triple (truncating) | 134.3 ms | 4.6 ms | 92.56 MB | 133 |
| emit | wide drain, **no export** | 566.3 ms | 6.1 ms | 1.04 GB | 1,530 |
| emit + `.dat` | wide | 596.3 ms | 2.8 ms | 1.04 GB | 1,530 |
| emit | triple grouped drain, **no export** | 151.0 ms | 1.7 ms | 150.78 MB | 217 |
| emit + `.dat` | triple grouped | 174.0 ms | 4.1 ms | 150.79 MB | 217 |
| emit | triple unordered drain, **no export** | 727.8 ms | 7.2 ms | 347.48 MB | 499 |
| emit + `.dat` | triple unordered | 727.6 ms | 13.1 ms | 347.49 MB | 499 |
| emit | keyed dedupe drain, **no export** | 2.210 s | 15.1 ms | 1.45 GB | 2,135 |
| emit + `.dat` | keyed dedupe | 2.270 s | 77.5 ms | 1.45 GB | 2,135 |
| emit + `.dat` | long text | 861.9 ms | 6.2 ms | 1.93 GB | 2,839 |
| emit + `.cxt` | wide | 1.432 s | 12.5 ms | 2.18 GB | 3,211 |
| hash pair | source pass, **unhashed** | 240.6 ms | 3.2 ms | 501.85 MB | 721 |
| hash pair | source pass, **hashed** | 285.7 ms | 12.3 ms | 501.85 MB | 721 |
| hash pair | `.dat` export, **unhashed** | 609.9 ms | 5.0 ms | 1.04 GB | 1,530 |
| hash pair | `.dat` export, **hashed** | 621.2 ms | 8.0 ms | 1.04 GB | 1,530 |
| CLI host | wide convert | 729.0 ms | 9.4 ms | 1.04 GB | 1,532 |
| CLI host | wide convert, `--no-manifest` | 756.0 ms | 44.6 ms | 1.04 GB | 1,532 |
| CLI host | triple grouped convert | 227.0 ms | 7.7 ms | 152.07 MB | 218 |
| CLI host | auto-calibrated, **two input passes** † | 1.527 s | 17.4 ms | 806.09 MB | 1,158 |
| CLI host | `--format both` | 2.354 s | 53.5 ms | 3.23 GB | 4,744 |
| CLI host | `.cxt` | 1.663 s | 25.7 ms | 2.18 GB | 3,213 |

† Re-measured in **session C** against the corrected build; every other row is session A. The
calibration correction described under [Corrected defect](#corrected-defect--many-attribute-count-sensitive-calibration)
is the reason, and the rows are marked rather than silently replaced because a table mixing two
sessions should say so. The many-quantile row is new: session A recorded it as a **failure with no
throughput number**.

## Results — 7.3M records (10x the motivating EMAGE workload)

| Surface | Case | Mean | StdDev | Records/s | Allocated | B/record |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| source | wide drain | 2.667 s | 80.9 ms | 2.74M | 4.91 GB | 723 |
| source | triple drain | 653.2 ms | 9.3 ms | 11.17M | 847.66 MB | 122 |
| calibrate | wide, four shapes † | 5.839 s | 51.9 ms | 1.25M | 5.01 GB | 737 |
| calibrate | **many-quantile, 16 attributes** † | 52.260 s | 680.0 ms | 0.14M | 5.03 GB | 740 |
| calibrate | triple count-sensitive † | 8.841 s | 53.2 ms | 0.83M | 4.54 GB | 667 |
| probe | wide (truncating) | 4.111 s | 30.7 ms | 1.78M | 4.93 GB | 725 |
| emit | wide drain, **no export** | 5.885 s | 107.4 ms | 1.24M | 10.42 GB | 1,532 |
| emit + `.dat` | wide | 6.549 s | 26.8 ms | 1.11M | 10.42 GB | 1,532 |
| emit + `.dat` | triple grouped | 1.653 s | 19.5 ms | 4.41M | 1.50 GB | 220 |
| emit | triple unordered drain | 8.906 s | 27.7 ms | 0.82M | 4.28 GB | 629 |
| emit + `.dat` | triple unordered | 9.005 s | 41.8 ms | 0.81M | 4.28 GB | 629 |
| emit | keyed dedupe drain | 28.692 s | 706.9 ms | 0.25M | **19.47 GB** | 2,864 |
| emit + `.dat` | keyed dedupe | 29.001 s | 369.0 ms | 0.25M | **19.47 GB** | 2,864 |
| hash pair | source pass, unhashed / hashed | 2.373 s / 2.655 s | 3.1 / 14.1 ms | — | 4.91 GB both | 723 |
| hash pair | `.dat` export, unhashed / hashed | 5.925 s / 6.264 s | 14.8 / 74.7 ms | — | 10.42 GB both | 1,532 |
| CLI host | wide convert | 6.975 s | 51.8 ms | 1.05M | 10.42 GB | 1,532 |
| CLI host | wide convert, `--no-manifest` | 6.708 s | 51.5 ms | 1.09M | 10.42 GB | 1,532 |
| CLI host | triple convert | 9.168 s | 87.2 ms | 0.80M | 4.28 GB | 629 |
| CLI host | auto-calibrated, **two input passes** † | 18.250 s | 27.0 ms | 0.40M | 8.70 GB | 1,279 |

† Re-measured in session C against the corrected build, as above. The many-quantile row replaces a
recorded **failure**. Sixteen simultaneous exact accumulators over 7.3M records is by a wide margin
the most expensive calibration in the matrix — 52 s against 5.8 s for the four-shape spec — and that
is the honest cost of sixteen exact populations, not a regression: the shipped budget gives each of
them a sixteenth of 64 MiB, so every one of them spills and merges.

## Results — 73M records (100x the motivating EMAGE workload)

**Every case completed and validated.** 1 launch, 1 warmup, 3 measured iterations; each iteration's
artifact checked against its independent expectation after disposal.

| Surface | Case | Mean | StdDev | Records/s | MiB/s | Allocated | B/record |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| source | wide drain | 25.948 s | 136.1 ms | 2.81M | 178 | 49.16 GB | 723 |
| source | triple drain | 6.442 s | 69.2 ms | 11.33M | 280 | 8.28 GB | 122 |
| calibrate | wide, four shapes † | 97.200 s | 1.956 s | 0.75M | 48 | 49.25 GB | 724 |
| probe | wide (truncating) | 39.825 s | 729.1 ms | 1.83M | 116 | 49.17 GB | 723 |
| probe | triple (truncating) | 10.067 s | 33.4 ms | 7.25M | 179 | 8.29 GB | 122 |
| emit | wide drain, **no export** | 55.058 s | 388.5 ms | 1.33M | 84 | 104.17 GB | 1,532 |
| emit + `.dat` | wide | 60.503 s | 35.7 ms | 1.21M | 76 | 104.17 GB | 1,532 |
| emit + `.dat` | triple grouped | 16.846 s | 1.019 s | 4.33M | 107 | 14.89 GB | 219 |
| emit | triple unordered drain | 98.386 s | 408.7 ms | 0.74M | 18 | 42.73 GB | 629 |
| emit + `.dat` | triple unordered | 104.689 s | 924.1 ms | 0.70M | 17 | 42.73 GB | 629 |
| CLI host | wide convert | 65.121 s | 477.8 ms | 1.12M | 71 | 104.18 GB | 1,532 |
| CLI host | triple convert | 99.166 s | 681.8 ms | 0.74M | 18 | 42.73 GB | 629 |
| CLI host | auto-calibrated, **two input passes** † | 201.540 s | 8.910 s | 0.36M | 9 ‡ | 86.04 GB | 1,265 |

† Re-measured in session C against the corrected build. Both are within the between-session drift
this tier already shows, and both allocate **identically** to session A — which is the expected
result for a correction that only stops a valid population being refused.

‡ Corrected denominator, not a slowdown. The session-A row read 23 MiB/s, which is this corpus's
elapsed time divided by the **wide** corpus's size; the auto-calibrated command reads the T10
unordered corpus (1,800.8 MiB), and every other row in this table already used its own case's input.
9 MiB/s is that same convention applied consistently. The command still reads the input twice, so
about 18 MiB/s crosses the parser.

## Findings

### Throughput is linear in the record count

The wide source drain reads **2.83M, 2.74M, and 2.81M records/second** at 730,000, 7.3M, and 73M
records — the same rate across a hundredfold range, at a constant **723 B/record**. The same holds
for every other surface measured at more than one tier. Nothing in the pipeline degrades with size,
which is the property D-007's streaming design exists to provide and the one this milestone was
convened to check.

### Serialization is nearly free; emission is the cost

Pairing each conversion with an **emit drain** that produces the same objects and writes nothing
isolates the writer:

| Case | emit drain | + `.dat` | writer's share | extra allocation |
| --- | ---: | ---: | ---: | ---: |
| wide, 730k | 566.3 ms | 596.3 ms | **+5.3%** | ~0 |
| wide, 7.3M | 5.885 s | 6.549 s | **+11.3%** | 0 |
| wide, 73M | 55.058 s | 60.503 s | **+9.9%** | 0 |
| triple unordered, 7.3M | 8.906 s | 9.005 s | **+1.1%** | 0 |
| triple unordered, 73M | 98.386 s | 104.689 s | **+6.4%** | 0 |

The `.dat` writer allocates **nothing measurable** — the 1,532 B/record a wide conversion allocates
is spent before a byte reaches the exporter. A profiling pass has one place to look, and it is not
the writer. That is the sharpest actionable result in this pack.

### The `.cxt` format costs what its layout implies

At the working tier, `.cxt` takes **2.40x** the time and **2.10x** the allocation of `.dat` over the
same context (1.432 s / 2.18 GB against 596.3 ms / 1.04 GB). A `.cxt` header carries counts and every
object name before the matrix, so a conforming writer takes a bounded name pass and then replays the
emission (§18.1) — two complete passes where `.dat` streams once. The factor is the format's, not the
implementation's.

### Accepting unordered triple input costs 4-6x, and it is grouping, not reading

| Tier | grouped | unordered | ratio |
| --- | ---: | ---: | ---: |
| 730k | 174.0 ms | 727.6 ms | 4.2x |
| 7.3M | 1.653 s | 9.005 s | 5.4x |
| 73M | 16.846 s | 104.689 s | 6.2x |

Reading is not the difference. The triple **source drain** takes 6.442 s at 73M, so of the 104.7 s an
unordered conversion spends, **fewer than 7 seconds are spent reading** and the rest is grouping.
`subject_grouped` is a declaration a user can make when their data really is grouped, and this is
what it is worth.

### Input-stability hashing costs 12-19% of a source pass, and allocates nothing

| Tier | unhashed | hashed | cost | allocation |
| --- | ---: | ---: | ---: | --- |
| 730k | 240.6 ms | 285.7 ms | **+18.7%** | identical (501.85 MB) |
| 7.3M | 2.373 s | 2.655 s | **+11.9%** | identical (4.91 GB) |

Output hashing costs far less — **+1.9%** at 730k and **+5.7%** at 7.3M — because a staged `.dat` is
a fraction of the input it came from. Both wrappers allocate **nothing**: the cost is pure CPU over
bytes the pass was already reading.

Neither is a switch. Inline hashing of every complete input pass is a correctness guarantee
(D-122 part 5 / §17); the unhashed arm is a component experiment that no user can select.

### The second input pass is the real input-stability cost

An auto-calibrated convert reads its input **twice** — once to calibrate, once to emit — and hashes
both, with the second required to agree with the first. Against a declared spec over the same corpus:

| Tier | declared (one pass) | auto (two passes) † | ratio |
| --- | ---: | ---: | ---: |
| 730k | 227.0 ms | 1.527 s | 6.7x |
| 7.3M | 9.168 s | 18.250 s | 2.0x |
| 73M | 99.166 s | 201.540 s | 2.0x |

At target scale the second pass is exactly what it looks like: the conversion done twice. A spec that
declares its domains avoids it.

† The auto arm is session C's; the declared arm is session A's, unaffected by the calibration
correction because a declared spec runs no calibration pass at all. The ratio is unchanged at both
target sizes.

### The manifest sidecar is at or below the noise floor

At 730k the manifest-bearing arm was **faster** than `--no-manifest` (729.0 ms against 756.0 ms,
StdDev 9.4 and 44.6 ms); at 7.3M it was 4.0% slower (6.975 s against 6.708 s). The sign is not stable
across tiers, so the honest statement is that the sidecar's cost is at the edge of what this
measurement resolves. Both arms allocate identically, and `--no-manifest` suppresses **only** the
sidecar: the complete input pass and the staged output are still hashed either way.

### Wide dedupe is the most expensive path in the matrix

`keyed dedupe` converts 7.3M rows into 1.825M objects in **29.0 s**, allocating **19.47 GB** — 2,864
B/record, the highest figure here, and 4.4x the allocation of the same rows converted without a key.
Its emit drain is 28.7 s, so again the writer is not the cost. Non-contiguous keys cannot be merged
without the spool, and this is what that machinery costs when every key recurs at maximum distance.

### Width is a planner cost, paid once

The pure planner takes **54.7 us / 38.3 KB** for a 33-column plan and **4.2 ms / 3.07 MB** for a
1,568-column one — 77x the time for 48x the columns. It is paid once per conversion, so it is
irrelevant at scale and dominant for a small one: at 1,000 Ads-width rows, planning is a measurable
share of the whole job.

### Whole-command working set: the wide path is constant-memory, the grouping path is not

Separate `dotnet-counters` traces of the **real self-contained executable** — not BenchmarkDotNet, not
this process — sampling once a second while a genuine `fcabedrock convert` ran:

| Command | Peak working set | Peak GC heap | Peak committed | Samples |
| --- | ---: | ---: | ---: | ---: |
| wide declared, 7.3M (455 MiB in) | **62.5 MB** | 14.8 MB | 18.2 MB | 11 |
| wide declared, 73M (4.51 GiB in) | **62.1 MB** | 18.0 MB | 18.3 MB | 99 |
| triple unordered declared, 7.3M § | 256.7 MB | 172.3 MB | 210.9 MB | 15 |
| triple unordered declared, 73M § | **1,721.1 MB** | 1,412.8 MB | 1,679.4 MB | 219 |
| triple unordered **auto-calibrated**, 7.3M § | 357.8 MB | 228.5 MB | 360.7 MB | 31 |
| triple unordered **auto-calibrated**, 73M § | 1,873.9 MB | 1,413.8 MB | 1,894.7 MB | 315 |

§ Session C, against the corrected executable, with the command, spec digest, data digest, output
digest and executable hash recorded in
`evidence/session-c-calibration-guard-fix/trace-provenance.md`. The two declared rows **replace**
session A's `t10u-scale7m.csv` and `t10u-scale73m.csv`, which are retained beside that record: their
CSVs hold counter samples only, the run log recorded a command template rather than an argv, and two
specs exist for that corpus — one declared and one auto-calibrated — so which was traced could not
be established. Rather than infer it from a file name, both were re-taken. The two auto-calibrated
rows are new; they are the triple commands whose code path the calibration correction actually
touches, and no earlier trace covered them.

The 7.3M declared row lands within 0.2 MB of session A's on working set and on exactly its committed
figure, which suggests the original was indeed the declared command — but a corroboration is not a
provenance record, and the replacement stands on its own. The 73M declared row reads about 5% *lower*
than session A's across all three counters despite 219 samples against 162; that is what a sampled
maximum is worth on a transient peak, and it is the reason this table says "lower bound" rather than
"peak".

The wide declared traces are **not** replaced. Their spec is fully declared — identity discretizers
with `declared_domain` and `manual_cuts`, no `equal_frequency` or `percentile_p1_p99` anywhere — so
`CalibratedSpec.RequiresData` is false and those commands run no calibration pass at all. The
corrected code is unreachable from them.

**The wide declared path converts 4.51 GiB of input in 62 MB of memory, and that figure does not
move between 7.3M and 73M records.** A tenfold increase in input changed the peak working set by
0.6%. That is the streaming guarantee (D-007, I3) observed end to end on the shipped command, and it
is the single result this milestone existed to obtain.

It also shows plainly why the three memory meanings must not be confused: BenchmarkDotNet measured
**104 GB allocated** for the same 73M wide conversion, and the process held **62 MB**. Allocation is
throughput through the collector; working set is what the machine must find.

**The triple unordered path is different, and legitimately so.** Its peak working set grows about
sevenfold from 7.3M to 73M — roughly 250 bytes per distinct subject at the larger size. That is the
P-16 metadata carve-out doing exactly what it says: the grouping key vocabulary is bounded metadata
that grows with the number of *objects*, not a materialized matrix. I3's wording is the right one to
read this against — "streaming" is not "constant total process memory". A user grouping 7.3 million
unordered subjects should expect to provide about 1.8 GB.

**Auto-calibrating that path costs about 100 MB more at 7.3M and about 9% more at 73M.** The extra is
the count-sensitive accumulator, which is sized from its share of the budget before a record is read,
plus a second pass over the input. At 73M the two runs' peak GC heaps are within 1 MB of each other
(1,412.8 against 1,413.8), which is what one expects when the grouping vocabulary dominates and the
accumulator is a fixed addition rather than a data-sized one.

Three further observations from the same traces:

- **GC pressure is confined to the grouping path.** Time-in-GC peaked at **3%** for the wide
  conversion, **86%** for the 73M triple unordered declared one, and **91%** auto-calibrated.
- **Conversion is single-threaded.** CPU usage peaked at 10.0% on a 12-logical-core machine — one
  core — in every trace.
- **The corrected calibration path holds no more memory than the guard's scope implies.** The
  auto-calibrated 73M command completed with a peak working set 9% above its declared counterpart,
  not a multiple of it: fixing the baseline's scope removed a false refusal, and did not enlarge the
  resources the merge actually uses.

A sampled maximum is a **lower bound** on the true peak: the wide 7.3M run was observed 11 times
across 26 seconds, and a spike between samples would not appear. No sample is not zero.

## Tuning — the grouping budget and merge fan-in

These two internal knobs are the only defaults M8 may move, and only after the evidence gate: a
repeatable improvement of at least 5% elapsed or 10% allocations, **larger than observed run
variability**, reproduced in two independent sessions and confirmed at **both** target sizes.

Every budget and every fan-in was validated against the *same* expected output digest on every
iteration, so the byte-neutrality the permission rests on (D-082) is measured here, not assumed.

### The candidate: 256 MiB instead of 64 MiB

| Tier | Session | 8 MiB | 64 MiB (default) | 256 MiB | 256 vs 64, time | 256 vs 64, alloc |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 730k | A | 958.1 ms | 762.5 ms | 471.1 ms | **-38.2%** | **-27.9%** |
| 730k | B | 936.2 ms | 731.1 ms | 508.8 ms | **-30.4%** | **-29.3%** |
| 7.3M | A | 9.275 s | 9.107 s | 7.281 s | **-20.1%** | **-20.1%** |
| 7.3M | B | 9.329 s | 9.253 s | 7.457 s | **-19.4%** | **-20.1%** |
| 73M | A | 124.914 s | 99.877 s | 92.964 s | **-6.9%** | **+0.05%** |
| 73M | B | 157.822 s | 125.071 s | 120.110 s | **-4.0%** | **+0.05%** |

### Decision: retain 64 MiB. The gate is not met.

The improvement is real and reproducible at the working tier and at 7.3M. It **fails at 73M**, which
is the larger of the two target sizes the gate requires it to be confirmed at, and it fails three
ways at once:

1. **The elapsed gain collapses with scale** — 38%, then 20%, then 4-7%. Session B's 73M figure of
   -4.0% is below the 5% bar outright.
2. **The allocation gain disappears entirely at 73M** (+0.05%, i.e. nothing), so the 10% allocation
   arm of the bar is not met either. At 73M the data dwarfs any budget; both arms spill about as
   much, and the extra 192 MiB buys proportionally less.
3. **Between-session variability at 73M is larger than the effect.** The same 64 MiB case measured
   99.877 s in session A and 125.071 s in session B — a **25% drift** between launches, against a
   candidate effect of 4-7%. The gate's "larger than observed run variability" clause is decisive
   here, and it is only visible *because* two sessions were run.

There is also a cost that a raise would impose everywhere, not only where the gain is:

**The budget is not a grouping-only knob.** It also sizes the count-sensitive calibration
accumulator, which is allocated from its share of the budget *before any record is read*.
`SpillEquivalenceTests.ModelledResident_ShouldScaleWithTheBudgetRatherThanWithTheData` pins that
relationship at 8, 64, and 256 MiB: the modelled peak is at least a quarter of the budget and within
its bound, whatever the input size. So a 10,000-row calibration allocates the same accumulator as a
73-million-row one — measured at **106 MB for 10,000 records** at today's default — and quadrupling
the budget would quadruple that floor for every conversion with a count-sensitive attribute, at every
size, in exchange for a gain that is largest exactly where it matters least.

**The internal default is unchanged. No production code was modified.** The plan's own guidance
applies: the absence of a proven worthwhile change is preferable to speculative optimization.

**A follow-up worth someone's decision, not taken here:** the grouping backend and the calibration
accumulator share one `MaxBufferedBytes`, and the evidence above says they want different values —
grouping benefits from more, calibration pays for it unconditionally. Separating them would let the
20% grouping gain at 7.3M be taken without the calibration cost. That is an architecture change, so
it is recorded rather than made.

### Merge fan-in: retain 16

At the working tier, one axis at a time under a spill-forcing budget:

| Fan-in | Session A | Session B |
| ---: | ---: | ---: |
| 4 | 3.585 s / 802.55 MB | 3.699 s / 802.55 MB |
| 16 (default) | 2.555 s / 542.22 MB | 2.527 s / 542.21 MB |
| 32 | 2.558 s / 541.44 MB | 2.478 s / 541.44 MB |

Dropping to 4 costs **+40%** time and **+48%** allocation, so the current value is well clear of the
cliff. Raising to 32 changed elapsed time by +0.1% in one session and -1.9% in the other, and
allocation by -0.1% in both: no effect outside noise, in either direction. **Retained at 16.**

### Probe defaults: measured, not adopted

Probe's retention limit and its three aggregate guards were exercised at their exact thresholds from
both sides (`ProbeAccountingOracleTests`), and the scale probes record what the default limit does to
real target-scale data: both W16 and T10 truncate at 7.3M and 73M, in bounded time and bounded
memory. **No probe default is changed**, and none could be here: probe-default adoption alters draft
bytes, warnings, and success-versus-guard-failure, so it is a separate observable semantic decision
requiring its own approval and a §7.1/D-110 reconciliation.

## Raw evidence

Everything below is under `D:\tmp\fcabedrock-m8-g15-bench` on the machine described above, outside
Git, and it expires with that directory unless the operator preserves it.

| Location | Contents |
| --- | --- |
| `corpus/`, `corpus/catalog/` | the 20 prepared corpora, their specs, and one identity record each |
| `results/results/` | BenchmarkDotNet logs, CSV, GitHub-Markdown, and full JSON for the most recent run of every case |
| `evidence/session-a/` | **424 files** — session A's complete raw reports, preserved before session B reran the tuning cases |
| `evidence/session-b/` | **424 files** — session B's |
| `evidence/session-c-calibration-guard-fix/` | session C: the corrected build's replacement measurements, the before/after regression and attribute-count-matrix runs, the full-suite log, and `results-preexisting/` (the 449 files the working results directory held before session C wrote into it) |
| `traces/` | the six `dotnet-counters` CSV traces (the two superseded triple ones are under the session-C evidence directory) |
| `publish/win-x64/`, `publish/fcabedrock-win-x64.zip` | the self-contained distribution the smoke validated |

BenchmarkDotNet writes one report per benchmark **type** and overwrites it on the next run, which is
why session A's tuning reports were copied aside before session B started, and session B's before
session C did. Superseded evidence keeps its own identity; it is never relabelled as a later run. The
session-A ManyQuantile reports in particular are retained as **failures** — they are the provenance
of the corrected defect, not a stale copy of a number.

Run logs: `C:\tmp\m8-g15-a-{small,working,scale7m,scale73m}.log` and
`C:\tmp\m8-g15-b-{working,scale7m,scale73m}.log`; session C's are in its evidence directory.

## Commands

```pwsh
$env:FCABEDROCK_BENCH_ROOT = 'D:\tmp\fcabedrock-m8-g15-bench'
$bm = 'dotnet run -c Release --project tests/FcaBedrock.Benchmarks --'

# preparation (explicit, outside every measured interval)
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare small working scale7m scale73m
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare adult   # the only networked step

# what routine CI runs on each native target - generated corpora only, no download
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare micro small
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Small --filter '*' --job dry

# the real-data acceptance run (Windows x64, on the final candidate)
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories External --filter '*'

# session A - the baseline matrix
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --filter '*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Working --filter '*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*Scale7M*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*Scale73M*' --warmupCount 1 --iterationCount 3

# session B - the independent tuning comparison (grouping cases only)
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Working --filter '*GroupingBudgetWorking*' '*GroupingFanInWorking*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*GroupingBudgetScale7M*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*GroupingBudgetScale73M*' --warmupCount 1 --iterationCount 3

# session C - the calibration correction's replacement measurements
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --filter '*' --job dry
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --filter '*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Working `
    --filter '*ManyQuantileCalibrateWorking*' '*CalibrateWideWorking*' '*CalibrateTripleGroupedWorking*' `
             '*CalibrateTripleUnorderedWorking*' '*CliHostConvertAutoWorking*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale `
    --filter '*ManyQuantileCalibrateScale7M*' '*CalibrateWideScale7M*' `
             '*CalibrateTripleUnorderedScale7M*' '*CliHostConvertAutoScale7M*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale `
    --filter '*CalibrateWideScale73M*' '*CliHostConvertAutoScale73M*' --warmupCount 1 --iterationCount 3

# the controlled attribute-count matrix, gated out of the ordinary suite
$env:FCABEDROCK_CALIBRATION_MATRIX = '1'
dotnet test tests/FcaBedrock.Benchmarks.Tests -c Release `
    --filter-method '*ManyQuantiles_ShouldCalibrateAtEveryAttributeCountAndBudget*'

# whole-command traces, outside BenchmarkDotNet, against the published executable
dotnet-counters collect --format csv --output traces/<name>.csv --refresh-interval 1 `
    --counters 'System.Runtime[cpu-usage,working-set,gc-heap-size,gc-committed,alloc-rate,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,time-in-gc]' `
    -- publish/win-x64/FcaBedrock.Cli.exe convert <spec> <data> --out <base> --format dat
```

### Which session C did not repeat, and why

Only the measurements that execute the changed code were replaced. The correction is confined to the
baseline the **count-sensitive calibration** merge is gated against — `QuantileAccumulator`'s two
`ValueCountMerger.Consolidate` calls — so a case whose timed work never constructs a
`QuantileAccumulator` cannot have been affected, and its session-A number stands:

| Not repeated | Why the diff cannot reach it |
| --- | --- |
| fixed-plan emit drains and `.dat`/`.cxt` exports | the plan is prepared outside the timed region; no calibration runs inside it |
| declared-spec CLI-host converts, `--no-manifest` pairs, `--format both`, `.cxt` | fully declared specs need no data pass to calibrate at all |
| hash-wrapper pairs | a source pass and a writer, with no calibration in either |
| grouping-budget and fan-in sweeps (sessions A and B) | declared T10 plans through `FirstAppearanceGrouping`/`RunMerger`, neither of which changed; the 64 MiB / fan-in-16 retention decision therefore stands unaltered |
| source drains, pure planning, probe/discovery | no calibration in the timed path |
| resident row-layout witnesses | the accumulator's retained graph is unchanged — no field was added or removed |

The rule this follows is revision 02's: name the dependency and check it, rather than rerun
everything or nothing.

Session A ran 2026-09-06 10:10-12:35 UTC; session B 12:36-13:17; the traces 13:18-13:23.




## Corrected defect — many-attribute count-sensitive calibration

This is the milestone's one production finding: the suite refused a valid spec, the refusal was
diagnosed to a real defect, and the defect was fixed. Both the failure and its replacement are kept
below, because a benchmark that discovers a bug is worth more as a record of the whole sequence than
as a corrected number.

### What session A measured

**A spec with four or more `equal_frequency` attributes failed to calibrate 730,000 records under the
shipped 64 MiB default budget** — a failure, not a slow success: the calibration returned no result,
so `ManyQuantileCalibrateWorking` and `ManyQuantileCalibrateScale7M` published **NA** and no
throughput number. Those reports are retained under their session-A identity.

```
Error GroupingStorageFailed: Grouping spool storage failure (CleanupDelete/DeleteFailed)
affected 1 operation - the conversion used external sort-merge spool storage (§16.4, D-082).
```

The same corpus, the same 730,000 records, varying only the number of `equal_frequency` attributes
and the budget:

| Attributes | 64 MiB, before | 512 MiB, before | 64 MiB, after | 512 MiB, after |
| ---: | --- | --- | --- | --- |
| 1 | OK | OK | OK | OK |
| 2 | OK | OK | OK | OK |
| 4 | **FAILED** | OK | OK | OK |
| 8 | **FAILED** | OK | OK | OK |
| 16 | **FAILED** | **FAILED** | OK | OK |

The decisive pair is **2 attributes at 64 MiB (OK)** against **16 attributes at 512 MiB (FAILED)**.
The budget is divided across the count-sensitive attributes, so both of those give each accumulator
**32 MiB**. Same per-attribute budget, opposite outcomes — so the independent variable was the number
of attributes running concurrently, not how much room each one had.

That matrix is now a gated check rather than a one-off:
`SpillEquivalenceTests.ManyQuantiles_ShouldCalibrateAtEveryAttributeCountAndBudget`, run with
`FCABEDROCK_CALIBRATION_MATRIX=1`. Against the pre-fix build it reproduces the left half exactly —
1 and 2 pass, 4, 8 and 16 fail — and against the corrected build all five pass at both budgets with
identical cuts.

### The mechanism

The rendered aggregate carried no path sample, which distinguishes the escalation in
`ValueCountMerger.MergeBatch` from an ordinary failed delete. That site checks

```
liveBytes + projected > 3 * baselineT
```

`baselineT` was **one accumulator's** cumulative raw spill payload, while `SpoolWorkspace.LiveBytes`
is the **whole workspace's** live spool bytes — and `CalibrationRun` creates one workspace shared by
every count-sensitive attribute. With a single accumulator the two describe the same set and the
check is D-082's intended 3T guarantee; with sixteen, the left side grew with the attribute count
while the right side did not. Nothing was wrong with the storage.

The emit path never shared the defect: `FirstAppearanceGrouping` creates a workspace per grouping
call and computes `baselineT` by summing the runs of that same workspace, so both sides describe one
set.

### The fix, and what it does not change

`T` is now the workspace's cumulative original-spill payload — the sum across every accumulator
sharing it — which is the scope `liveBytes` always had. It is still original spills only, never
consolidation output, and it never falls when a run is deleted or an attribute finishes. This is one
consistently scoped `≤3T` guarantee, not a per-attribute multiplier and not a larger allowance.

It removes false failures and nothing else. No diagnostic, severity, registry entry, public API,
output byte, ordering rule, resource bound, or default changed; the registry stays at 82 and every
golden and canonical-byte pin is untouched. A genuine byte-bound or pending-cap breach is still an
Error with no result, and broken storage is still refused — proved by tests that fail every delete
and every spill write and still require the Error. The full contract is D-124.

### What replaced the failure

`ManyQuantileCalibrateWorking` now measures **2.909 s / 609.95 MB** and `ManyQuantileCalibrateScale7M`
**52.260 s / 5.03 GB**, both validated per iteration against independent expectations. Sixteen exact
accumulators are expensive — 52 s against 5.8 s for the four-shape spec at 7.3M — because the shipped
budget gives each a sixteenth of 64 MiB, so all sixteen spill and merge. That is a cost, not a defect,
and it is the honest price of sixteen exact populations in one pass.

A practical note for a reader on an older build: raising the budget helped up to a point (four and
eight attributes passed at 512 MiB) and stopped helping (sixteen failed at 512 MiB too), which is
exactly what the shared-versus-per-attribute reading predicts.


## What has not been measured

Named here so their absence is explicit rather than inferred.

- **No native target has produced a complete passing run, and no artifact exists.** The first
  five-target workflow run,
  [`34241484619`](https://github.com/trashr0x/fcabedrock/actions/runs/34241484619) at `a09e302`,
  **failed**, and remains failed evidence at that revision. What it did establish is real and
  narrow: all five jobs ran on their intended native architectures — both optional ARM64 targets
  included — and passed checkout, SDK setup, the environment and process-architecture assertions,
  restore, the Release build, and all 25 resident-layout witnesses, the first time D-082's retained
  accounting had executed anywhere but Windows x64. All five then failed at `Test (Release)`, so
  corpus preparation, the Small Dry smoke, both package smokes, the self-contained
  publish/run/archive and the upload were **skipped**, and **no artifact was produced or retained**.
  The grouping was: Linux x64 and Linux ARM64 each nine publication failures across
  `PublicationOwnershipTests` and `PublicationRecoveryTests`, plus one `ToolPackTests` failure;
  macOS ARM64, Windows x64 and Windows ARM64 the `ToolPackTests` failure alone. Every suite totalled
  4,498 tests, and **no benchmark, corpus, oracle, selection or identity case failed anywhere**.
  Both defects were pre-existing M7 blockers in files byte-identical to `main`, exposed by this gate
  precisely because nothing had ever executed them off Windows x64; both are corrected under
  **D-125**. Until a complete run passes, D-082's numerical `actual retained ≤ modelled` guarantee
  still extends to validated x64 alone.
- **The measured CLI-host figures in this document are superseded, and their controlled replacement
  is outstanding.** D-125 retains a live reference for every object whose identity authorizes a
  later mutation — on Windows as well as Unix — and that acquisition and its release sit **inside**
  the measured CLI-host interval, so every `CliHostConvert*` figure here, both sides of the
  manifest-versus-no-manifest comparisons, and all six whole-command traces stop describing the
  shipped code. What **is** established is that the correction costs nothing measurable: with the
  pre-fix and post-fix builds measured **interleaved in one machine state**, the post-fix wide
  convert sits inside one standard deviation of the pre-fix one and the post-fix `.cxt` convert is
  faster, while **allocation is byte-identical on both builds in every case** — the expected result
  for a native handle that allocates nothing managed. What is **not** available is a controlled
  replacement figure: on the machine as it stands, the *unchanged* pre-fix code measures about 10%
  slower than the session-A row with three times the deviation, so this machine is demonstrably not
  in session A's controlled state and any absolute number taken here would be a measurement of that
  state. The rows below therefore keep their original provenance as the measurements of the revision
  that produced them; they are neither relabelled nor overwritten with a noisier figure. Component
  measurements that never reach `PublicationTransaction` — the Sources drains, calibration, the
  grouping-budget and fan-in series, pure planning, emit and export, probe, and the hash-pair
  wrappers — are unaffected, because the correction touches no timed line of code in any of them.
- **Routine CI does not exercise the acquired Adult corpus, on any platform.** That is deliberate
  (see [Selection](#selection-and-what-routine-ci-proves)), and it means no cross-platform Adult
  evidence exists or is claimed. The real-data evidence is Windows x64 only.
- **No cross-published archive has been executed.** `eng/publish-selfcontained.ps1` can produce a
  folder for another runtime identifier, and that shows only that the SDK can emit files for it.
- **A bounded-cardinality *successful* probe scan at 7.3M or 73M is not represented.** Every
  synthetic family's numeric and subject columns scale with the row count, so at those tiers both W16
  and T10 truncate under the default retention limit. That is the honest outcome and it is what the
  scale probe cases record; a bounded successful scan at target scale would need a corpus designed
  for it. Bounded successful scans do exist at the micro and small tiers.
- **Nothing here is a cold-disk measurement**, and no figure is a comparison against another tool.
