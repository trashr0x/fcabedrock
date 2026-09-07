# FcaBedrock.Benchmarks

The internal M8 benchmark suite: one BenchmarkDotNet host over the real production paths, plus the
corpus, oracle, and evidence layer those measurements need to mean something. It is **not** a test
project and is never reachable from `dotnet test` (principle P-20); it is not packed, and no
production package references it.

See `docs/benchmarks.md` for the evidence pack and `docs/decisions.md` D-124 for why the suite is
shaped this way.

## Running it

Corpora are **prepared explicitly**, never as a side effect of a run. A `prepare` argument is
either a tier — which prepares every case of that size — or a single case id:

```
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare small
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare working scale7m
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare scale73m
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare adult
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare all
```

`prepare adult` is the **only** command in this repository that touches the network: it downloads the
UCI Adult training split, writes its bytes verbatim, and records their length and digest. Every other
corpus is generated from pinned integer arithmetic. No ordinary test and no benchmark measurement
performs any network access. Attribution and licence are in `Corpus/Adult.attribution.md`.

Then use the ordinary BenchmarkDotNet command line — there is no wrapper grammar:

```
# every default (Small) case
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --filter '*'

# prove the harness runs, without measuring anything meaningful
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --filter '*' --job dry

# what would be selected, without running it
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --list flat

# the working baseline (730,000 records per case)
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Working --filter '*'

# the 7.3M tier
dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- --anyCategories Scale --filter '*Scale7M*'

# the 73M tier, with the reduced repetition count that tier is run at
dotnet run -c Release --project tests/FcaBedrock.Benchmarks --     --anyCategories Scale --filter '*Scale73M*' --warmupCount 1 --iterationCount 3
```

## Jobs

Two shapes, chosen by the selection rather than declared per case:

| Selection | Job | Why |
| --- | --- | --- |
| Small (the default) | Throughput, one invocation per iteration, unroll 1 | operations measured in milliseconds; fresh state per iteration |
| Working or Scale | **Monitoring**, 1 launch, 2 warmups, 5 iterations | an operation that already takes seconds gives Throughput's pilot stage nothing to find, and the multiplication would only lengthen the run |

The 73M tier uses the same Monitoring job with `--warmupCount 1 --iterationCount 3` supplied on the
command line. That is BenchmarkDotNet's own option, not a third job declared here: a run costing
hours should be a deliberate choice made where the run is started.

**Release only.** A Debug build fails validation rather than reporting numbers from an unoptimized
assembly.

## Selection

Two category axes. A **tier** says how much data a case reads (`Small`, `Working`, `Scale`); a
**surface** says which production path it measures (`Source`, `Convert`, `Mini`, …).

* With no category named, the selection is **Small**.
* `Working` and `Scale` are **opt-in by category and by nothing else** — `--filter '*'` will not
  reach either, because those cases read 730,000, 7.3M, and 73M records and cost minutes to hours.
* A job named with `--job` replaces the suite's own, so a run never executes each case twice.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Every selected case built, executed, and validated. |
| 1 | A build, execution, or critical validation failure — no publishable result. |
| 2 | Nothing was selected, so nothing was measured (or an unknown tier was named). |

## What is measured

| Surface | Families |
| --- | --- |
| Source drain | W16 wide, Ads-width (1,559 columns), long-text, UCI Adult |
| Calibrate | four shapes in one pass; count-sensitive triple; sixteen simultaneous quantile attributes |
| Plan | the pure planner: narrow (33 formal attributes) against Ads-width (1,568) |
| Emit + `.dat` | W16, both T10 layouts, keyed dedupe, Ads-width, long-text, Adult, the v2 minis |
| Emit + `.cxt` | the v2 minis (byte-equal to v2), working W16, small Ads-width, Adult |
| Probe | complete, truncated, and guard-breach outcomes; the three limits straddled at their exact thresholds |
| Grouping | budget 8/64/256 MiB and merge fan-in 4/16/32, one axis at a time |
| CLI host | one complete `convert` at the argv boundary, through the real publication transaction and manifest |
| Hash pairs | `InputHashTracker` and `HashingWriteStream`, each wrapped against unwrapped |

The CLI-host cases are labelled **CLI-host throughput** and are never installed-command latency:
process start, host resolution, the tool shim, runtime startup, and console attachment are outside
the measured interval. The packaging smokes cover the real executable boundary.

## Where the bytes go

Generated corpora, produced artifacts, and BenchmarkDotNet's results are bulk evidence and stay out
of Git. They live under `artifacts/bench/` by default; set `FCABEDROCK_BENCH_ROOT` to an absolute
path to put them on another volume — the 73M tier is large.

```
<root>/corpus     prepared inputs and their specs
<root>/corpus/catalog   one identity record per prepared case
<root>/output     one iteration's artifact at a time, deleted after it is validated
<root>/results    BenchmarkDotNet logs, CSV, Markdown, and full JSON
```

Only generator definitions, specs, attribution, the metadata contract, and tiny expectations are
committed.

## What a measurement includes

Every timed file operation starts from **prepared but unopened** inputs and outputs, and covers
opening, the full awaited production operation, complete consumption, and the final flush and
disposal. Data generation, output reset, oracle derivation, harness hashing, and validation are all
outside it.

Every completed iteration is validated **after** disposal and **outside** timing. A validation
failure throws, so a case that produced the wrong bytes has no throughput result — correctness is
never deferred to global cleanup, where one late failure would silently cover every iteration before
it.

## Three memory meanings, kept apart

1. **Allocated** (the report column) is BenchmarkDotNet's process-wide *managed allocated bytes* per
   operation. It is not peak live memory, not native allocation, and not another process's memory.
2. **Modelled retained bytes** come from the internal grouping/calibration observers and the
   layout witnesses in the Conversion test suite. They are scoped algorithmic guarantees, not a
   whole-process limit.
3. **Sampled working set** comes from separate `dotnet-counters` traces of the real self-contained
   command. A sampled maximum is a lower bound on the true peak; no sample is not zero.

None of the three is ever read off another.
