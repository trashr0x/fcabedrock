# Benchmarks: FcaBedrock vNext

This guide describes FcaBedrock's benchmark suite. It explains what the suite measures and how,
gives representative results, and states the limits of those results. The scale targets are
7.3 million and 73 million records, 10 and 100 times the v2 EMAGE workload of about 732,000
triples (D-007). The suite's design and rationale are in `docs/decisions.md` (D-124, and D-128
for the tiered spill schedule), and `tests/FcaBedrock.Benchmarks/README.md` explains how to run it.

The scaling baseline at 730,000, 7.3M and 73M records and the later checks of the corrected build
were measured on one Windows x64 machine. The tiered spill schedule was assessed later on a Windows
host at 730,000 and 7.3M records, with no machine identity recorded and no 73M run. The raw
evidence (reports, logs, traces and archives) is held outside Git. Where this guide quotes a digest,
the digest identifies the recorded bytes; it does not promise that they are still available.

Three limits apply throughout. First, the `CLI host` rows describe the build that produced them, not
the shipped command: a later publication correction reaches that measured interval, and the
comparison meant to bound its cost was inconclusive
([Command latency after the publication correction](#command-latency-after-the-publication-correction)).
Second, the tiered spill schedule has no 73M result
([Tiered spill schedule for count-sensitive calibration](#tiered-spill-schedule-for-count-sensitive-calibration)).
Third, the delimited reading change of 2026-10-01 reaches every case that reads delimited input, so
every earlier result describes the reader before it. Only its comparison with the previous reader
was measured after it, and that comparison does not bound its elapsed cost on small inputs
([Delimited reading rules: comparison with the previous reader](#delimited-reading-rules-comparison-with-the-previous-reader)).

## What is measured

The suite is one BenchmarkDotNet host over the real production paths: source drains for wide and
triple CSV, calibration (including count-sensitive calibration: `equal_frequency`, and `equal_width`
over the `percentile_p1_p99` range, which need the exact count of every value), planning, emit,
`.dat` and `.cxt` export, probe, the grouping backend's memory budget and merge fan-in, input and
output hashing, and a complete `convert` through the in-process CLI host. The `CLI host` cases
measure host throughput, not installed-command latency. Separate `dotnet-counters` traces of the
real self-contained executable cover whole-command memory.

Every timed operation starts from a corpus prepared on disk with **nothing open**. The measured
interval covers opening, the full awaited production operation, complete consumption, and the final
flush and disposal. Corpus generation, output reset, oracle derivation, harness hashing and
validation are all outside it.

Every completed iteration is validated **after** disposal. The artifact must have exactly the
expected length and digest, or the calibration exactly the expected cuts and domain, and the run must
carry no diagnostic that would oblige a caller to discard its output. A validation failure throws, so
**a case that produced the wrong result has no number at all**. The launcher exits non-zero on any
build, execution or validation failure, and also when a run measured nothing.

Expected results are derived from the corpus definition and the documented spec semantics, never by
invoking the code under test. Where the population makes an exact expectation derivable, the oracle
is exact: the equal-frequency cuts for `n_seq` are order statistics over a known arithmetic sequence.
Where it does not, as for a deliberately tie-heavy population whose exact cuts would mean
re-implementing the algorithm under test, the check is shape plus an independent equality, which is
weaker, and it is not presented as the exact kind.

Two corpora are **external** checks on a suite whose other expectations are authored in this
repository: the immutable v2 minis, produced by a different program years earlier and compared byte
for byte, and UCI Adult, whose distributions nobody here chose.

### Three memory meanings

1. **Allocated** is BenchmarkDotNet's process-wide **managed allocated bytes per operation**, with
   its GC collection counts. It is not peak live memory, not native allocation and not another
   process's memory. It is reported beside the recorded record and byte denominators, so a
   per-record figure can be derived and rechecked against the corpus table.
2. **Modelled retained bytes** come from the grouping and calibration observers and from the
   retained-layout witnesses. They are scoped algorithmic guarantees over a stable graph, not a
   whole-process limit.
3. **Sampled working set** comes from the separate `dotnet-counters` traces. A sampled maximum is a
   **lower bound** on the true peak, and no sample is not zero.

None of the three is ever read off another.

## Corpora

Generated and downloaded data, produced artifacts and raw results stay out of Git; only generator
definitions, specs, attribution, the metadata contract and small expectations are committed. Every
prepared case records its generator revision, geometry, exact byte length and SHA-256, and a case
that no longer matches is refused rather than measured. These digests identify the measured inputs.

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
| `lexical-quoted-multiline-small` | 10,000 | 6 | 1,233,359 | 1 | `033156e9595c402b8784ea2feced5882530a35594756b2bec4a482e958bbbf46` |
| `lexical-tab-small` | 10,000 | 16 | 1,322,294 | 1 | `73416763e9e32898ba82904e333329c4e0818c241b164cc697bb87e69210fa3d` |
| `lexical-space-small` | 10,000 | 16 | 1,322,294 | 1 | `4dba04384203ae5e1490dc8722f0dea9eaaa6dc6a2fed8ff25d324f819fc1f38` |
| `lexical-many-fields-small` | 2,000 | 200 | 1,559,645 | 1 | `27423e8caaf663a9541ff5c74789af5c5749b9d15774d4eea7ee72736e67380c` |
| `lexical-long-fields-small` | 500 | 4 | 10,002,012 | 1 | `c6e15ec0b0d6091765f14e2a78aa42d30bd10723247b13a675094c863a63d81f` |
| `lexical-unicode-whitespace-small` | 10,000 | 16 | 2,122,294 | 1 | `baab84cb8f63ab3e27c6ab0aa7f92b099fb18e9e91d252246003fe9e2e87542a` |
| `lexical-blank-runs-small` | 10,000 | 16 | 1,332,294 | 1 | `701c5f90046353abd0d2e5511826d17e0735811583f858c90c102bb714fe82c3` |
| `adult` † | 32,561 | 15 | 3,974,305 | 3 | `5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d` |

† The only **acquired** corpus. Its length and digest are **pinned in the source** (`AdultCorpus.cs`)
and enforced on every acquisition and reuse, so this row names one specific file rather than
whatever a later download returns. The pin establishes byte identity, not publisher authenticity.

- **W16** is sixteen wide columns: four numeric (strictly increasing, heavily tied, skewed, wide
  signed decimal), eight categorical over eight-value domains, and four binary. It carries both
  missing forms (an explicit token and an empty cell) and two values that force the quoted-field path
  (spec §5.1.1).
- **T10** is ten triple rows per subject in two layouts of the same observations: contiguous, and
  block-interleaved with first-appearance subject order unchanged. It carries a multi-valued
  predicate, an exact duplicate row, one numeric value in two equivalent spellings, and a predicate
  no attribute binds. The two layouts have the same byte length and different digests.
- **w16keyed** is W16 with a leading object-key column whose values repeat four times and never
  adjacently, converted under `duplicate_object_policy = "dedupe"`. Wide dedupe shares the
  grouping backend with unordered triples, so this measures that backend from the other side.
- **ads** is 1,559 columns (three numeric, a local flag, 1,554 sparse term flags at about 1.2%
  density, and a class), matching the internet-advertisements geometry. It tests width, and it is
  outside the scale ladder: 1,559 columns do not need 73 million rows to be wide.
- **longtext** is four columns of long values (eight fixed 512-character blobs and a per-row note of
  64 to 1,024 characters), the only family that can reach probe's retained-text guard before its
  retained-value guard.
- **lexical** is seven small wide cases, each stressing one part of the delimited-text grammar
  (spec §5.1.1) rather than a scale: quoted fields with doubled quotes and line breaks
  (`quoted-multiline`), TAB and space delimiters (`tab`, `space`), 200 short fields per record
  (`many-fields`), four 5,000-character fields per record (`long-fields`), fields padded with a
  no-break space and an ideographic space (`unicode-whitespace`), and a blank line after every
  record (`blank-runs`). The first five read the same under the previous reading rules; the last
  two measure the Unicode-whitespace and blank-record rules themselves, so they have no comparison
  with the previous reader. Each case's expected drain summary comes from its generator's value
  definitions, not from a reader.
- **adult** is the UCI Adult training split, acquired rather than generated; its attribution and
  licence are in `tests/FcaBedrock.Benchmarks/Corpus/Adult.attribution.md`. The published file ends
  with a doubled newline, so after its 32,561 census rows it holds one empty final line. That line
  is a blank record, which the reader skips (spec §5.1.1), so the recorded count is 32,561 and the
  conversion produces 32,561 objects. Results recorded before acquisition revision 3 counted the
  empty line as a 32,562nd record and object.

## Selection and routine CI

The suite has four tier categories. `Small` is the default; `Working`, `Scale` and `External` are
**opt-in by category and by nothing else**, so no name filter and no surface category reaches one.

| Tier | Bare run and routine CI | Reached by | Why it is gated |
| --- | --- | --- | --- |
| `Small` (with `Micro`) | yes | the default | n/a |
| `Working` | no | `--anyCategories Working` | 730,000 records per case |
| `Scale` | no | `--anyCategories Scale` | 7.3M and 73M records; hours |
| `External` | no | `--anyCategories External` | its corpus is **acquired**, not generated |

`Working` and `Scale` are opt-in because of their cost. `External` is opt-in because of a
dependency: its three cases read only 32,561 records, but the corpus comes from
`archive.ics.uci.edu`, and an outage there must not fail a build that has nothing to do with it.
Opting in is not skipping: a selected case whose corpus is absent is a hard failure that names the
exact `prepare` command.

Routine CI prepares `micro small` and runs `--anyCategories Small --filter '*' --job dry` on each
native target. A green run proves exactly what it selected, the Small-category cases and their
oracles on that platform. It measures nothing, and it claims nothing about Adult. The two Small
cases named "mini-adult" are the committed v2 fixtures, compared byte for byte with v2's output, not
the acquired dataset.

The real-data evidence is required of each release candidate instead: all three `External` cases
must pass on the final Windows x64 build against the verified corpus (D-124). An unreachable host, a
length or digest mismatch, and a failure on verified bytes are kept apart, and none of them becomes a
pass. The most recent recorded pass was at `3b2e4a80` on 2026-09-11, with every completed iteration
validated after disposal and the Adult plan-shape proof run once per conversion case before
measurement. At the same revision the five `probe` rows in the result tables were re-validated under
the per-attribute probe oracle, not re-timed. Both are correctness results for that revision, and
the Adult obligation applies again at every later release candidate.

## Environment and applicability

The scaling baseline and the tuning runs were taken on one machine:

| | |
| --- | --- |
| Revision | a working state based on `38b3dda`; the baseline runs of 2026-09-06 predate every later commit, and no commit is recorded for the re-measurement after the calibration fix |
| OS | Windows 11 Pro (10.0.26200) |
| CPU | AMD Ryzen 5 5600X: 1 CPU, 6 physical / 12 logical cores |
| Runtime | .NET 10.0.11, X64 RyuJIT AVX2, **Concurrent Workstation GC** (the product default, not forced) |
| SDK | 10.0.302 |
| BenchmarkDotNet | 0.15.8, out-of-process toolchain, Release |
| Storage | a Samsung SSD 850 EVO 500 GB (SATA, NTFS); **not** the system volume, and carrying no page file |
| Bench root | one directory on that volume holding corpora, outputs, **grouping spools** and results |

**Storage is a stated condition.** Corpora, produced artifacts, BenchmarkDotNet's results and the
grouping spool all live on that one volume. The suite points the grouping backend at its own root
through the production `GroupingOptions.TempDirectory` seam, the one the CLI's `--temp-dir` uses,
rather than letting it default to the OS temporary directory. Otherwise a scale run would measure
two devices at once, and a multi-gigabyte spill would land on the system volume.

**The filesystem cache is warm.** Preparation, validation and repeated iterations read the same
files, and no cache is dropped between iterations, so these are warm-cache figures wherever the
corpus fits in RAM. Nothing here is a cold-disk measurement.

Hosted CI hardware produced no performance figure in this guide. Each result set describes the code
it measured, and later changes limit how far it reaches:

| Result set | Code it describes | Does it describe today's code? |
| --- | --- | --- |
| Baseline measurements, tuning runs, and the re-measurement after the calibration fix (from 2026-09-06; .NET 10.0.11) | a working state based on `38b3dda`; no commit recorded | not after the delimited reading change (2026-10-01), which reaches every case that reads delimited input; its reach into planning was not examined. Before that change: component rows yes, by reachability, because every executable product change committed since `38b3dda` was in count-sensitive calibration or CLI publication; † rows (count-sensitive calibration): no, the tiered spill schedule (D-128) changed that path; `CLI host` rows: no, the publication correction (D-125) changed publication |
| Publication-latency comparison (2026-09-09) | `4216610b` against its unchanged pre-fix control | no: it describes those two builds, and it was inconclusive at the 5% bound |
| Shipped-code validation and memory traces (2026-09-10; traces on .NET 10.0.10) | `50f6aa62`, whose `src/` equals that of `3d71524b` | not after the delimited reading change, which reaches every one of these commands. Before it: yes, except the auto-calibrated rows and traces, which predate the tiered spill schedule |
| Tiered spill schedule assessment (adopted 2026-09-21; a Windows host; environment not recorded) | a candidate whose source equals `47e2ce71` apart from one documentation comment | not after the delimited reading change, which reaches these cases. Before it: yes, for the three measured count-sensitive calibration cases at 730,000 and 7.3M records; no 73M run |
| Delimited reading comparison (2026-10-01; .NET 10.0.12) | the delimited reading change, uncommitted on `a450b144`, against `a450b144` with only the lexical cases' benchmark source added | yes, for the cases it compared: the measured source is this change's, apart from later documentation and tests that no measured case executes and later diagnostic, exception and help wording that, by reading, no measured iteration produces; no build of the committed revision was measured |

The details are in [Command latency after the publication correction](#command-latency-after-the-publication-correction),
[Tiered spill schedule for count-sensitive calibration](#tiered-spill-schedule-for-count-sensitive-calibration)
and [Delimited reading rules: comparison with the previous reader](#delimited-reading-rules-comparison-with-the-previous-reader).

## Findings

### Streaming throughput is linear in the record count

The wide source drain reads **2.83M, 2.74M and 2.81M records/second** at 730,000, 7.3M and 73M
records, the same rate across a hundredfold range, at 721 to 723 B/record. The triple drain (10.6M
to 11.3M records/second), grouped triple conversion (4.2M to 4.4M) and wide emit (1.24M to 1.33M)
also hold their rate: these streaming paths do not degrade with size, which is the property D-007's
streaming design exists to provide. The paths that merge spilled data do slow down: unordered
triple conversion falls from 1.00M to 0.81M and 0.70M records/second, and four-shape calibration
holds 1.25M at 730,000 and 7.3M records and falls to 0.75M at 73M. These are component measurements
of the measured paths, not a rate to expect from a whole `convert` invocation.

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

The `.dat` writer allocates **nothing measurable**: the 1,532 B/record a wide conversion allocates is
spent before a byte reaches the exporter. A profiling pass has one place to look, and it is not the
writer.

### The `.cxt` writer takes two passes where `.dat` takes one

At the working tier, `.cxt` takes **2.40x** the time and **2.10x** the allocation of `.dat` over the
same context (1.432 s / 2.18 GB against 596.3 ms / 1.04 GB). The `.cxt` layout puts the counts and
every object name before the incidence rows (§18.1). This implementation's writer buffers only the
object names, which are bounded metadata, in a first pass, and then replays the emission for the
rows: two complete passes where `.dat` streams once. The spec also allows a writer that spools the
incidence rows during the name pass instead of replaying the emission, so 2.40x and 2.10x are
measurements of this writer against `.dat` on this case, not a property of the format.

### Unordered triple input takes 4-6x as long to convert as grouped input

A triple conversion of the same observations takes **4.2x** as long unordered as grouped at 730,000
records, **5.4x** at 7.3M and **6.2x** at 73M. Each ratio compares two whole conversions, and both
conversions read, group, emit and export, so the ratio does not show how the extra time divides
between those phases. The difference is consistent with the external sort-merge or buffering that
unordered input requires (§5.3). `subject_grouped` is a declaration a user can make when the data
really is grouped, and this is what it is worth.

### Input-stability hashing costs 12-19% of a source pass, and allocates nothing

Hashing the input costs **+18.7%** of a source pass at 730,000 records and **+11.9%** at 7.3M.
Hashing the output costs far less, **+1.9%** and **+5.7%** of a `.dat` export, because a staged
`.dat` is a fraction of its input. The cost is CPU over bytes the pass was already reading, and the
wrappers allocate nothing the reported precision resolves: both arms agree in the rounded `MB` and
`GB` columns, which is not a byte-identity claim. Neither is a switch: inline hashing of every
complete input pass is a correctness guarantee (D-122 part 5, §17), and the unhashed arm is a
component experiment that no user can select.

### An auto-calibrated convert took about twice as long as a declared one at target scale

An auto-calibrated convert reads its input **twice**, once to calibrate and once to emit, and hashes
both, with the second required to agree with the first. A spec that declares its domains runs no
calibration pass and reads its input once. At 7.3M and 73M, where the declared and auto-calibrated
commands read the same unordered corpus, the auto command took **2.0x** as long. The auto command
adds calibration work as well as a second read, so this whole-command ratio does not isolate the
cost of hashing, of the second read or of a repeated conversion. At 730,000 records the declared
command measured reads the grouped layout while the auto command reads the unordered one, so that
tier's 6.7x ratio also includes the difference between the two layouts. Both arms are `CLI host`
rows from before the publication correction, and the auto arm also predates the tiered spill
schedule; the ratios describe those builds, and no corrected-build timing is published
([Command latency after the publication correction](#command-latency-after-the-publication-correction)).

### The manifest sidecar is at or below the noise floor

In the baseline measurements the manifest-bearing convert was **faster** than `--no-manifest` at
730,000 records (729.0 ms against 756.0 ms, StdDev 9.4 and 44.6 ms) and 4.0% slower at 7.3M
(6.975 s against 6.708 s). The sign is not stable across tiers, so the sidecar's timing cost is at
the edge of what that measurement resolved. Its allocation is not zero: at 730,000 records the
manifest-bearing arm allocated **1,118,368,544 B** against **1,118,098,280 B**, an exact difference
of **270,264 B** that rounds away at GB precision. `--no-manifest` suppresses only the sidecar; the
input pass and the staged output are hashed either way. These figures predate the publication
correction, and no corrected-build elapsed comparison of the two arms exists.

### Wide dedupe is the most expensive path in the matrix

`keyed dedupe` converts 7.3M rows into 1.825M objects in **29.0 s**, allocating **19.47 GB**: 2,864
B/record, the highest figure in the 7.3M table. Against W16 converted without a key (6.549 s and
1,532 B/record), that is 4.4x the time and 1.9x the allocation per record. Its emit drain takes
28.7 s, so again the writer is not the cost. Non-contiguous keys cannot be merged without the
spool, and this is what that machinery costs when every key recurs at maximum distance.

### Width is a planner cost, paid once

The pure planner takes **54.7 us / 38.3 KB** for a 33-column plan and **4.2 ms / 3.07 MB** for a
1,568-column one: 77x the time for 48x the columns. It is paid once per conversion, so it is
irrelevant at scale and dominant for a small one: at 1,000 Ads-width rows, planning is a measurable
share of the whole job.

### Whole-command memory

Six `dotnet-counters` traces of the real self-contained executable, sampling once a second while a
genuine `fcabedrock convert` ran, record whole-command memory for the shipped code (built at
`50f6aa62`; the two auto-calibrated traces predate the tiered spill schedule). WS is the working
set. Every figure is a **sampled maximum**, a lower bound on the true peak with profiling overhead
present, never an exact peak or a portable ceiling. Every trace exited 0 with no diagnostic output,
its line count was derived independently, and its manifest's input, spec and output hashes equalled
SHA-256 values recomputed over the actual files. Every output matched its retained expectation byte
for byte; for the two auto-calibrated commands that match is regression continuity, not an
independent quantile oracle. The four triple traces placed their grouping spool on the non-system
volume with an explicit `--temp-dir`, an acquisition condition that supports no timing comparison.

| Command | Samples | WS max (MB) | GC heap max (MB) | GC committed max (MB) | CPU max (%) | Time-in-GC max (%) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| wide declared, 7.3M | 7 | 62.23872 | 15.828216 | 18.39104 | 9.5238 | 1 |
| triple declared, 7.3M | 10 | 282.927104 | 131.863824 | 246.050816 | 9.7025 | 51 |
| triple auto, 7.3M | 20 | 359.325696 | 227.694024 | 356.962304 | 9.1026 | 54 |
| wide declared, 73M | 68 | 62.85312 | 17.531696 | 18.71872 | 9.8830 | 4 |
| triple declared, 73M | 103 | 1,750.761472 | 1,409.648912 | 1,698.635776 | 9.5052 | 93 |
| triple auto, 73M | 207 | 1,746.702336 | 1,463.971712 | 1,693.45024 | 9.3750 | 74 |

**The wide declared path is flat.** It converted 7.3M and 73M records in a sampled working set of
about 62 MB, and the tenfold input changed that maximum by a ratio of 1.0099 (GC committed 1.0178).
That ratio is checked against an investigation threshold of 3, which is a trigger for a closer look,
not a limit, and it did not fire. The baseline traces of the same two commands (2026-09-06) read
62.5 and 62.1 MB. BenchmarkDotNet measured **104 GB allocated** for the same 73M wide conversion,
which is why the memory meanings must stay apart: allocation is throughput through the collector,
and working set is what the machine must find.

**The grouping path grows with the number of subjects, as it may.** From 7.3M to 73M records the
triple working set grew 6.19x (declared) and 4.86x (auto) while subjects grew tenfold: about 239
bytes per distinct subject at 73M. That is consistent with the EP-16 bounded-metadata carve-out
(`docs/engineering-principles.md`), under which the subject-key vocabulary grows with the number of
objects, read against the rule that streaming is not constant total process memory. Consistency is
not attribution: the traces do not show which structure holds the memory. The 73M triple runs
spilled to the grouping spool rather than holding their state resident. Free space on the spool
volume dipped by about 5.6 GiB, and the product removed its own spool workspace on success, leaving
no residue.

Time in GC peaked at 1% and 4% for the wide conversions and at 51% to 93% for the triple ones. The
higher readings on the triple commands are consistent with their grouping path, but a sample of the
whole process does not attribute GC time to a component. CPU peaked between 9.10% and 9.88% of
twelve logical cores in every trace, near one logical core's share (8.33%); a one-second sample of
total process CPU does not show how many threads did the work.

**None of this proves a streaming or no-materialization guarantee.** A dense 7,300,000 × 15
incidence matrix would be about 0.013 GiB packed, small enough to hide inside the observed working
set, so a sampled process maximum cannot show that no such matrix was built. Exit status, a flat
working set and an allocation total prove no algorithmic bound either. That guarantee is carried by
the grouping and calibration observers and the retained-layout witnesses, which these traces
reconcile with rather than replace.

## Results by tier

`Surface` is the production path a row measures. `Mean` and `StdDev` are BenchmarkDotNet's, over the
iterations each job declares. `Records/s` and `MiB/s` are records and input bytes divided by the
mean, using the corpus table's denominators, so both can be rechecked. `Allocated` is managed
allocation per operation. `B/record` is omitted for the Ads-width family, whose records have 1,559
fields against W16's sixteen; divide by the column count for a per-field figure.

The working and 7.3M tiers run the Monitoring job (1 launch, 2 warmups, 5 iterations); the 73M tier
runs it with 1 warmup and 3 iterations. At 73M the same case measured 25% apart in two independent
runs (see the tuning section), so a small difference in these tables is not a finding.

Every row is a baseline measurement from 2026-09-06 unless it is marked †. The † rows were
re-measured after the calibration fix, and they are exactly the count-sensitive calibration rows,
which the later tiered spill schedule (D-128) changed again. The `CLI host` rows, including the
sidecar comparison, describe the build before the publication correction (D-125), which reaches the
measured `CLI host` interval. They are not measurements of the shipped command, and no corrected
elapsed figure replaces them
([Command latency after the publication correction](#command-latency-after-the-publication-correction)).
The five `probe` rows were later re-validated, not re-timed.

### 730,000 records

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

† Re-measured after the calibration fix; the later tiered spill schedule (D-128) changed this path
again. The many-quantile row replaces a recorded failure: the baseline run of that case produced no
number (see the corrected-defect section).

### 7.3M records (10x the EMAGE workload)

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
| hash pair | source pass, unhashed / hashed | 2.373 s / 2.655 s | 3.1 / 14.1 ms | n/a | 4.91 GB both | 723 |
| hash pair | `.dat` export, unhashed / hashed | 5.925 s / 6.264 s | 14.8 / 74.7 ms | n/a | 10.42 GB both | 1,532 |
| CLI host | wide convert | 6.975 s | 51.8 ms | 1.05M | 10.42 GB | 1,532 |
| CLI host | wide convert, `--no-manifest` | 6.708 s | 51.5 ms | 1.09M | 10.42 GB | 1,532 |
| CLI host | triple convert | 9.168 s | 87.2 ms | 0.80M | 4.28 GB | 629 |
| CLI host | auto-calibrated, **two input passes** † | 18.250 s | 27.0 ms | 0.40M | 8.70 GB | 1,279 |

† Re-measured after the calibration fix, as above. The many-quantile row replaces a recorded
failure. Sixteen simultaneous exact accumulators over 7.3M records is the most expensive
calibration at this tier, 52 s against 5.8 s for the four-shape spec: the shipped budget gives each
accumulator a sixteenth of 64 MiB, so every one of them spills and merges. That is the cost of
sixteen exact populations, not a regression. In a separate paired assessment the tiered spill
schedule later reduced this case's elapsed time by about 25%
([Tiered spill schedule for count-sensitive calibration](#tiered-spill-schedule-for-count-sensitive-calibration)).

### 73M records (100x the EMAGE workload)

**Every case completed and validated**, each iteration's artifact checked against its independent
expectation after disposal.

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

† Re-measured after the calibration fix, as above. Both rows' elapsed times are within this tier's
between-run drift, and their reported `Allocated` values, rounded to 49.25 GB and 86.04 GB, match
the rounded values of their baseline rows. That match is the expected result for a fix that only
stops a valid population being refused; equal rounded values do not show equal byte totals.

‡ Divided by this command's own input, the T10 unordered corpus (1,800.8 MiB), as every other row
uses its own case's input. The command reads the input twice, so about 18 MiB/s crosses the
parser.

## Command latency after the publication correction

The publication correction (D-125) holds a live reference to every object whose identity authorizes
a later mutation, on Windows as well as Unix. That work sits inside the measured `CLI host` interval,
so every `CLI host` row in the tier tables, both sides of the sidecar comparison, and the baseline
whole-command traces describe the build before it. They keep that provenance; none is relabelled or
overwritten.

A four-form paired comparison was pre-registered to bound the correction's cost on elapsed time.
It ran in full on 2026-09-09: twelve interleaved invocations of the unchanged pre-fix control and the
corrected build `4216610b` in a fixed six-block order, with every iteration validated after disposal.
Each form is one Working-tier `CLI host` case: wide `.dat`, `--no-manifest`, `.cxt` and
`--format both`. The point estimate is the geometric-mean change in elapsed time of the corrected
build against the control, `U` is its one-sided 95% upper limit, control `max/min` is the unchanged
build's own spread across its six launches, and the order ratio is the comparison's check for an
effect of block order. All four forms had to pass every applicable check.

| Working form | Point estimate | One-sided 95% `U` | `U ≤ 5%` | control `max/min` | `≤ 1.05` | order ratio |
| --- | ---: | ---: | --- | ---: | --- | ---: |
| Wide | +0.6969% | **5.6809%** | **fail** | 1.036610 | pass | 1.015293 |
| No-manifest | +1.4417% | 2.7330% | pass | 1.022896 | pass | 1.001014 |
| CXT | +0.4623% | 1.5892% | pass | 1.022205 | pass | 1.011346 |
| Both | −1.0576% | 0.9893% | pass | **1.071382** | **fail** | 1.010199 |

Two forms failed. Wide's `U` exceeded 5% because of one iteration in one candidate launch that ran
about 200 ms above its four siblings at identical GC counts. Both failed on the control arm: the
unchanged pre-fix build moved 7.1% across its own six launches. No block was discarded, no bound
recomputed, no margin widened and no sample added.

**The correction's effect on command latency is therefore unknown.** The comparison was complete and
inconclusive at its 5% bound, and no retry is required: D-126 records this as a permanent limitation
of the evidence. The point estimates are shown so that nothing is hidden; they are not a bound.
Nothing here may be read as neutral, as no measurable regression, as equality, as non-regression, as
probably below 5% or as a speedup, and no corrected-build elapsed figure, throughput rate,
publication or sidecar overhead, or new absolute CLI baseline is derived anywhere in this guide. The
corrected build allocates slightly more per published run: within one machine state, the four
Working forms allocated +2,880 to +9,416 bytes more than the unchanged control, on operations of
1.04 GB to 3.23 GB, apart from one pair distorted by an anomalous control reading (D-125).

What was measured on the shipped code instead is correctness and allocation. All fifteen `CLI host`
cases were run at `50f6aa62` on 2026-09-10, one invocation each, with every completed iteration
validated after disposal and all three 73M cases at their full 1-warmup, 3-iteration job. No
allocation investigation threshold was crossed on any row.

| Case | Tier | Allocated B/op | Oracle strength |
| --- | --- | ---: | --- |
| `CliHostConvertWideSmall` | Small | 16,621,592 | independent bytes |
| `CliHostConvertTripleSmall` | Small | 5,010,744 | independent bytes |
| `CliHostConvertWideWorking` | Working | 1,118,374,216 | independent bytes |
| `CliHostConvertNoManifestWorking` | Working | 1,118,103,720 | independent bytes + sidecar proved absent |
| `CliHostConvertCxtWorking` | Working | 2,345,519,184 | independent CXT bytes |
| `CliHostConvertBothWorking` | Working | 3,462,974,704 | two independent expectations |
| `CliHostConvertTripleGroupedWorking` | Working | 159,463,792 | independent bytes |
| `CliHostConvertAutoWorking` | Working | 845,252,024 | **limited auto** |
| `CliHostConvertWideScale7M` | 7.3M | 11,185,802,648 | independent bytes |
| `CliHostConvertNoManifestScale7M` | 7.3M | 11,185,532,808 | independent bytes + sidecar proved absent |
| `CliHostConvertTripleScale7M` | 7.3M | 4,593,008,128 | independent bytes |
| `CliHostConvertAutoScale7M` | 7.3M | 9,337,122,160 | **limited auto** |
| `CliHostConvertWideScale73M` | 73M | 111,858,366,736 | independent bytes |
| `CliHostConvertTripleScale73M` | 73M | 45,882,478,752 | independent bytes |
| `CliHostConvertAutoScale73M` | 73M | 92,380,125,112 | **limited auto** |

Oracle strength is not uniform. The declared, CXT and both-format rows check exact length and
SHA-256 against an expectation derived from the corpus definition and the documented spec
semantics; the no-manifest rows also prove the sidecar absent. The three **limited auto** rows check
only the expected subject count, a diagnostic policy that allows only `ObservedDomainUsed`,
manifest presence, and byte determinism across their iterations. Their external continuity comes
from the whole-command traces and is regression evidence, not an independent quantile oracle, and
these three rows predate the tiered spill schedule. BenchmarkDotNet's manifest check is presence or
absence only; the traces carry the full manifest, input and output hash consistency.

Allocation is nearly but not perfectly deterministic, and it depends on machine state, so these are
exact integers, and no delta is carried from one row to another. A cumulative allocation total is
not a retained-memory measurement. These runs also produced elapsed output, which stays unpublished
raw data. The run departed from its protocol in two disclosed ways that D-126 records; it is
admissible evidence, not an exactly protocol-compliant run.

## Tuning: the grouping budget and merge fan-in

The grouping and calibration memory budget and the merge fan-in are the only internal defaults the
suite may move. The rule for moving one is a repeatable improvement of at least 5% elapsed time or
10% allocation, larger than the observed run-to-run variation, reproduced in two independent runs
and confirmed at both target sizes (D-124). Every budget and fan-in was validated against the same
expected output digest on every iteration, so the byte-neutrality the rule relies on (D-082) was
measured, not assumed.

| Tier | Run | 8 MiB | 64 MiB (default) | 256 MiB | 256 vs 64, time | 256 vs 64, alloc |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 730k | 1 | 958.1 ms | 762.5 ms | 471.1 ms | **-38.2%** | **-27.9%** |
| 730k | 2 | 936.2 ms | 731.1 ms | 508.8 ms | **-30.4%** | **-29.3%** |
| 7.3M | 1 | 9.275 s | 9.107 s | 7.281 s | **-20.1%** | **-20.1%** |
| 7.3M | 2 | 9.329 s | 9.253 s | 7.457 s | **-19.4%** | **-20.1%** |
| 73M | 1 | 124.914 s | 99.877 s | 92.964 s | **-6.9%** | **+0.05%** |
| 73M | 2 | 157.822 s | 125.071 s | 120.110 s | **-4.0%** | **+0.05%** |

**The 64 MiB default is kept, because the rule is not met.** A 256 MiB budget is clearly faster at
730,000 and 7.3M records, but at 73M, the larger of the two target sizes, it fails three ways. The
elapsed gain falls to 6.9% and 4.0%, below the 5% bar in run 2. The allocation gain disappears
(+0.05%). And the same 64 MiB case measured 99.877 s and 125.071 s in the two runs, a 25% drift
larger than the effect.

The budget is also not a grouping-only setting. It sizes the count-sensitive calibration accumulator
before any record is read, which
`SpillEquivalenceTests.ModelledResident_ShouldScaleWithTheBudgetRatherThanWithTheData` pins at 8, 64
and 256 MiB. A 10,000-record calibration therefore allocates the same accumulator as a 73M-record
one, measured at **106 MB for 10,000 records** at the default, and quadrupling the budget would
quadruple that floor for every conversion with a count-sensitive attribute. Separate grouping and
calibration budgets would let grouping take its gain without that cost; that is an architecture
change left for a later decision.

At the working tier, one axis at a time under a spill-forcing budget:

| Fan-in | Run 1 | Run 2 |
| ---: | ---: | ---: |
| 4 | 3.585 s / 802.55 MB | 3.699 s / 802.55 MB |
| 16 (default) | 2.555 s / 542.22 MB | 2.527 s / 542.21 MB |
| 32 | 2.558 s / 541.44 MB | 2.478 s / 541.44 MB |

**Fan-in 16 is kept.** Dropping to 4 costs **+40%** and **+46%** time and **+48%** allocation;
raising to 32 changed elapsed time by +0.1% in one run and -1.9% in the other, and allocation by
-0.1% in both, which is noise.

Probe's retention limit and its three aggregate guards were exercised at their exact thresholds from
both sides, and the scale probes show what the default limit does at target scale: W16 and T10 both
truncate at 7.3M and 73M, in bounded time and memory. **No probe default is changed.** Adopting one
would alter draft bytes, warnings and success-versus-guard outcomes, so it is a separate semantic
decision (spec §7.1, D-110).

## Corrected defect: many-attribute count-sensitive calibration

The suite found one production defect: **a spec with four or more `equal_frequency` attributes failed
to calibrate 730,000 records under the shipped 64 MiB budget**, on healthy storage. The calibration
returned no result, so the baseline runs of `ManyQuantileCalibrateWorking` and
`ManyQuantileCalibrateScale7M` published no number:

```
Error GroupingStorageFailed: Grouping spool storage failure (CleanupDelete/DeleteFailed)
affected 1 operation - the conversion used external sort-merge spool storage (§16.4, D-082).
```

Over the same 730,000 records, varying only the number of `equal_frequency` attributes and the
budget:

| Attributes | 64 MiB, before | 512 MiB, before | 64 MiB, after | 512 MiB, after |
| ---: | --- | --- | --- | --- |
| 1 | OK | OK | OK | OK |
| 2 | OK | OK | OK | OK |
| 4 | **FAILED** | OK | OK | OK |
| 8 | **FAILED** | OK | OK | OK |
| 16 | **FAILED** | **FAILED** | OK | OK |

The decisive pair is **2 attributes at 64 MiB (OK)** against **16 attributes at 512 MiB (FAILED)**.
The budget is divided across the count-sensitive attributes, so both give each accumulator
**32 MiB**: the cause was the number of attributes running together, not the room each one had. The
matrix is now a check gated by `FCABEDROCK_CALIBRATION_MATRIX=1`
(`SpillEquivalenceTests.ManyQuantiles_ShouldCalibrateAtEveryAttributeCountAndBudget`). Against the
pre-fix build it reproduces the left half exactly, and against the fixed build all five pass at both
budgets with identical cuts. On a build without the fix, raising the budget helps only up to a point:
four and eight attributes pass at 512 MiB, and sixteen still fail.

The refusal came from the merge check in `ValueCountMerger.MergeBatch`:

```
liveBytes + projected > 3 * baselineT
```

`baselineT` was **one accumulator's** cumulative raw spill payload, while `SpoolWorkspace.LiveBytes`
counts the **whole workspace**, which `CalibrationRun` shares across every count-sensitive attribute.
With one accumulator the two sides describe the same set; with sixteen, the left side grew with the
attribute count and the right side did not. Nothing was wrong with the storage. The emit path never
had the mismatch, because `FirstAppearanceGrouping` creates a workspace per grouping call and sums
the runs of that same workspace.

`T` is now the workspace's cumulative original-spill payload, summed across every accumulator that
shares it: one consistently scoped `≤3T` guarantee, not a per-attribute multiplier or a larger
allowance. The fix removes false failures and nothing else: no diagnostic, severity, registry entry,
public API, output byte, ordering rule, resource bound or default changed, and the registry stays
at 82. A genuine byte-bound or pending-cap breach is still an `Error` with no result, and broken
storage is still refused (D-124). After the fix the two cases measured **2.909 s / 609.95 MB**
(730,000 records) and **52.260 s / 5.03 GB** (7.3M), validated per iteration against independent
expectations. These are baseline figures: in a separate paired assessment, the tiered spill
schedule later reduced the 7.3M case's elapsed time by about 25%.

## Tiered spill schedule for count-sensitive calibration

D-128 changes only how count-sensitive calibration schedules its spill runs. Original spills enter
generation zero, and each time a generation holds `F` runs (the merge fan-in), they are merged
oldest-first into one run of the next generation, so a value is rewritten at most once per
generation instead of repeatedly. The change leaves the benchmark jobs, corpora, oracles, cuts,
diagnostics, public API, fingerprints and output bytes unchanged.

Three existing cases were measured: the targeted `ManyQuantileCalibrateScale7M` (sixteen
count-sensitive attributes at 7.3M) and two guards, `ManyQuantileCalibrateWorking` and
`CalibrateWideScale7M`. The targeted case ran as two order-balanced pairs and each guard as one
reverse-order pair, all on the existing Monitoring job (one launch, two warmups, five iterations),
and every iteration passed the existing `CalibrationOracle` and `OutputValidation` checks. An
earlier single-pair screen measured the targeted case at `0.760480` of baseline; that ratio was
treated as unreplicated and is not pooled. In the tables, A is the build before the change and B the
candidate; AB and BA give which ran first; B/A is the candidate mean over the baseline mean; "fresh"
marks a pair run for this assessment; and an endpoint is one benchmark case.

| Endpoint / order | Baseline mean | Candidate mean | B/A |
| --- | ---: | ---: | ---: |
| `ManyQuantileCalibrateWorking`, fresh BA | 2.819 s | 2.761 s | 0.979558 |
| `CalibrateWideScale7M`, fresh BA | 5.571 s | 5.509 s | 0.988863 |
| `ManyQuantileCalibrateScale7M`, fresh AB | 48.046 s | 35.884 s | 0.746878 |
| `ManyQuantileCalibrateScale7M`, fresh BA | 46.737 s | 35.170 s | 0.752516 |

The two targeted ratios have a geometric mean of **`0.749692`**: about 25% less elapsed time, or
1.334x throughput for this operation. The two baseline means, taken about seventeen minutes apart
around the candidate launches, differ by 2.80%, and the AB and BA ratios differ by 0.75%; both are
far smaller than the effect. Two order-balanced pairs support the observed improvement, but they are
not a statistical bound on drift or order effects. The geometric means of the earlier
forward-order and the fresh reverse-order guard ratios are `0.991274` for
`ManyQuantileCalibrateWorking` and `0.994901` for `CalibrateWideScale7M`, both inside the 1.05 guard
(at most 5% slower) and best read as no detectable movement.

| Endpoint | Baseline allocation | Candidate allocation | Delta |
| --- | ---: | ---: | ---: |
| `ManyQuantileCalibrateWorking` | 639,582,544 B/op | 639,585,360 B/op | +2,816 B/op |
| `CalibrateWideScale7M` | 5,381,260,272 B/op | 5,381,260,696 B/op | +424 B/op |
| `ManyQuantileCalibrateScale7M` | 5,396,369,904 B/op | 5,396,386,416 B/op | +16,512 B/op |

The largest delta is 0.0003% of the targeted case's allocation, far inside the existing
`max(2% of baseline, 65,536 B)` allowance. The independently counted bytes written by the 7.3M
many-quantile case fall from `9,017,744,128` B to `5,571,308,288` B (ratio `0.617816`), which
explains the elapsed gain without a change in managed allocation. The baseline † rows in
[Results by tier](#results-by-tier) predate this change.

**Limits.** This is a direct engineering assessment with BenchmarkDotNet, not a formal admission. No
73M run was performed. The benchmark emits no per-launch calibration digest and BenchmarkDotNet
deletes its worker, so neither a per-launch digest comparison nor a surviving-worker hash is
claimed; output identity rests on the permanent Conversion regression suite and the earlier observer
and CLI evidence. The assessment ran on a Windows host and records no machine, OS build, .NET
runtime or SDK identity, so it is not compared with the baseline environment.

**Source identity.** The measured candidate's `QuantileAccumulator.cs` and `ICalibrationObserver.cs`
are byte-identical to those committed at `47e2ce71`. Its `QuantileRunCatalog.cs` differs from the
committed file only in one token of an XML documentation comment, which the committed file
replaces with the citation `(D-128)`, so no executable source differs. No build of the committed
revision was measured, and the assessment records no build-to-commit tie or runtime identity.

## Delimited reading rules: comparison with the previous reader

The delimited reading change (spec §5.1.1; D-041, D-054 and D-137) was compared with the previous
reader on 2026-10-01. Each result is the ratio of the change's BenchmarkDotNet reported median to
the previous reader's for the same case, checked against a limit set for this comparison. **The
comparison does not bound the change's elapsed cost on small inputs.** In four of the eight Small
cases that read the same records under both readers, at least one measured ratio is above the 1.30
limit, and measurements of the same case disagree widely. Every larger case stays within its limit.
The change is accepted with its small-input cost unbounded (D-138). No claim is made that that cost
is within 30%, that the change has no regression, or about installed-command latency.

**What was compared.** The previous reader is commit `a450b144` with only the lexical cases'
benchmark source added: the lexical corpus generator, its independent oracle, the seven lexical
benchmark classes and their registration, identical on both sides. The change was measured on the
same commit before it was committed. Both sides read the same prepared corpora
([Corpora](#corpora)), and every measured iteration was validated against its independent
expectation after disposal.

| | |
| --- | --- |
| OS | Windows 11 (10.0.26300) |
| CPU | AMD Ryzen 5 5600X: 1 CPU, 6 physical / 12 logical cores |
| Runtime | .NET 10.0.12, X64 RyuJIT x86-64-v3, Concurrent Workstation GC |
| SDK | 10.0.401 |
| BenchmarkDotNet | 0.15.8, out-of-process toolchain, Release |
| Power plan | High performance |
| Storage | corpora and results on the system volume |

**How it was measured.** The jobs and the measured interval are the suite's own (D-124). The Small
cases run the fresh-iteration Throughput job: one invocation per iteration and unroll factor one,
with BenchmarkDotNet choosing the warmup and iteration counts adaptively. The Working and 7.3M cases
run the long-run Monitoring job: one launch, two warmups and five iterations of one invocation
each. A drain iteration constructs a fresh session over the prepared, unopened input, reads its
schema and drains every record, all inside timing. Resolving the read settings and deriving the
independent expectation happen before timing, and validation happens after it. Allocation is
managed allocated bytes per operation, not peak memory. Each Small case was measured three times,
each Working case twice and each 7.3M case once, every time on the previous reader and then on the
change.

The limits set for this comparison:

- Drains that read the same records under both readers: time ratio at most 1.30, allocation ratio
  at most 1.10.
- `QuotedMultilineSourceDrainSmall`, the quote-heavy case: time ratio at most 1.50, allocation
  ratio at most 1.10.
- Six Working calibration and conversion cases: time ratio at most 1.10. No allocation limit
  applies to them; their allocation ratios are shown for context.
- `UnicodeWhitespaceSourceDrainSmall` and `BlankRunsSourceDrainSmall`, whose meaning changed, have
  no previous-reader result. Their time per MB of input is compared with that of the change's
  slowest same-records Small case in the same measurement, `WideSourceDrainSmall` both times, with a
  limit of 2.

Ratios above their limit are in bold.

| Small case | Time ratios | Time limit | Allocation ratio |
| --- | --- | ---: | ---: |
| `SpaceDelimitedSourceDrainSmall` | **2.4233**, **2.4289**, 0.2758 | 1.30 | 0.9999 |
| `TabDelimitedSourceDrainSmall` | **2.4730**, 0.8177, **2.3812** | 1.30 | 0.9999 |
| `ManyFieldsSourceDrainSmall` | 0.7469, **2.4597**, 0.7256 | 1.30 | 0.9995 |
| `WideSourceDrainSmall` | 0.9088, 0.9242, **3.0519** | 1.30 | 1.0378 |
| `TripleSourceDrainGroupedSmall` | 1.1832, 1.1799, 1.2953 | 1.30 | 1.0002 |
| `TripleSourceDrainUnorderedSmall` | 1.1804, 1.2306, 1.2697 | 1.30 | 1.0002 |
| `LongFieldsSourceDrainSmall` | 1.0259, 0.9823, 1.0041 | 1.30 | 1.0000 |
| `QuotedMultilineSourceDrainSmall` | 1.2181, 1.2918, 1.3443 | 1.50 | 1.0000 |

| Working case | Time ratios | Time limit | Allocation ratio |
| --- | --- | ---: | ---: |
| `WideSourceDrainWorking` | 0.8562, 0.8572 | 1.30 | 1.0371, 1.0373 |
| `TripleSourceDrainUnorderedWorking` | 1.0724, 0.9932 | 1.30 | 1.0000 |
| `CalibrateWideWorking` | 0.9465, 0.9400 | 1.10 | 1.0310 |
| `CalibrateTripleUnorderedWorking` | 1.0241, 0.9931 | 1.10 | 1.0000 |
| `CalibrateTripleGroupedWorking` | 1.0429, 1.0495 | 1.10 | 1.0000 |
| `WideConvertDatWorking` | 0.9556, 0.9363 | 1.10 | 1.0175 |
| `TripleConvertDatGroupedWorking` | 1.0362, 1.0208 | 1.10 | 1.0000 |
| `TripleConvertDatUnorderedWorking` | 0.9889, 1.0123 | 1.10 | 1.0000 |

| 7.3M case | Time ratio | Time limit | Allocation ratio |
| --- | ---: | ---: | ---: |
| `WideSourceDrainScale7M` | 0.8552 | 1.30 | 1.0370 |
| `TripleSourceDrainUnorderedScale7M` | 1.0435 | 1.30 | 1.0000 |

| Changed-meaning case | Time per MB against the slowest same-records Small case | Limit |
| --- | --- | ---: |
| `UnicodeWhitespaceSourceDrainSmall` | 0.2657, 0.4314 | 2.00 |
| `BlankRunsSourceDrainSmall` | 0.5064, 0.1581 | 2.00 |

The wide drains allocate 3.7% to 3.8% more than the previous reader, about 27 bytes more per record
at each of the three tiers, and every other drain stays within 0.05% of the previous reader's
allocation.

**What the Small results show, and what they do not.** The recorded iteration times change level by
large factors, between processes and within one. Some Small runs stay at one level throughout, and
others move to a faster level partway through. The previous reader's build measured
`TabDelimitedSourceDrainSmall` at 4.22, 12.70 and 4.27 ms and `WideSourceDrainSmall` at 12.66, 12.85
and 3.86 ms; the change measured `SpaceDelimitedSourceDrainSmall` at 10.26, 10.44 and 3.54 ms. Every
ratio above 1.30 combines a relatively fast previous-reader median with a slow median for the
change, and the 0.2758 for `SpaceDelimitedSourceDrainSmall` is the reverse. BenchmarkDotNet's
adaptive stopping measured from 15 to 100 iterations per Small run, with many counts between, so a
reported median depends on when, or whether, its process reached a faster level. A median can also
mix levels: one grouped triple run of the previous reader reported 3.86 ms while its last iterations
took about 1.1 ms. No fast level of the change was observed for `TabDelimitedSourceDrainSmall` or
`WideSourceDrainSmall`. Tiered JIT compilation, on-stack replacement or dynamic profile-guided
optimization could produce such levels, but none was instrumented, so the cause is inferred, not
measured. These results show neither a stable threefold slowdown nor a small-input cost within
1.30, and a short command or a small library read may run at the slower level.

**Measurement conditions.** BenchmarkDotNet generates, restores and builds its worker project
before measuring, as it always does; no other build, test or measurement ran during the
measurements. CPU use sampled once a second for 60 seconds before and after each measurement window
averaged 1.2% to 1.7%. Those samples bracket the windows; they do not show continuously zero
competing load.

**Scope.** The larger drains and the six calibration and conversion cases are the closest of these
to the 7.3M and 73M targets, but they come from one machine and at most two measurements each. Not
compared: the grouped triple drain at 7.3M, any 73M case, `CLI host`, Adult, and the categorical
`include` recovery and `value_groups` pass-through calibrations. The change reaches every case that
reads delimited input, so the results earlier in this guide describe the previous reader; only the
cases here were measured after the change.

## What has not been measured

- **No admissible published elapsed result, derived throughput rate or overhead estimate is
  established for the corrected `CLI host` path.** The corrected build's runs recorded elapsed
  output, which stays unpublished raw data. The publication correction's effect on command latency
  is inconclusive at the 5% bound, and no retry is required
  ([Command latency after the publication correction](#command-latency-after-the-publication-correction)).
- **The tiered spill schedule has no 73M result**, and no `CLI host` row or whole-command trace has
  been recorded since it.
- **The delimited reading change's elapsed cost on small inputs is not bounded**, and no result
  earlier in this guide was re-measured after the change
  ([Delimited reading rules: comparison with the previous reader](#delimited-reading-rules-comparison-with-the-previous-reader)).
- **The five `probe` timings are baseline observations.** The probe cases were later re-validated
  under the corrected per-attribute oracle, which establishes correctness, not a new elapsed figure.
- **A bounded successful probe scan at 7.3M or 73M is not represented.** Every synthetic family's
  numeric and subject columns grow with the row count, so W16 and T10 truncate at those tiers under
  the default retention limit. Bounded successful scans exist at the micro and small tiers.
- **Real-data evidence is Windows x64 only.** Routine CI never runs the acquired Adult corpus, on any
  platform.
- **D-082's `actual retained ≤ modelled` guarantee has executed coverage only on the targets where
  the retained-layout witnesses ran.** Execution does not widen the guarantee's scope.
- **Sampled memory is a lower bound**, and no trace proves a streaming or no-materialization
  guarantee.
- **Only the publication comparison carries a statistical bound across launches.** The tier
  tables report single runs, the tiered spill assessment reports pairs, and between-run drift at
  73M reached 25%.
- **Nothing here is a cold-disk measurement**, and no figure is a comparison with another tool.

## Reproducing the results

`tests/FcaBedrock.Benchmarks/README.md` owns corpus preparation, selection, jobs and the per-tier
commands. Set `FCABEDROCK_BENCH_ROOT` to an absolute path on the volume to be measured. The commands
below are the ones that README does not list.

```pwsh
$env:FCABEDROCK_BENCH_ROOT = '<absolute path>'

# the grouping budget and fan-in comparison
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Working --filter '*GroupingBudgetWorking*' '*GroupingFanInWorking*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*GroupingBudgetScale7M*'
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*GroupingBudgetScale73M*' --warmupCount 1 --iterationCount 3

# the controlled attribute-count matrix, gated out of the ordinary suite
$env:FCABEDROCK_CALIBRATION_MATRIX = '1'
dotnet test tests/FcaBedrock.Benchmarks.Tests -c Release `
    --filter-method '*ManyQuantiles_ShouldCalibrateAtEveryAttributeCountAndBudget*'

# whole-command traces, outside BenchmarkDotNet, against the published executable
dotnet-counters collect --format csv --output traces/<name>.csv --refresh-interval 1 `
    --counters 'System.Runtime[cpu-usage,working-set,gc-heap-size,gc-committed,alloc-rate,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,time-in-gc]' `
    -- publish/win-x64/FcaBedrock.Cli.exe convert <spec> <data> --out <base> --format dat
```
