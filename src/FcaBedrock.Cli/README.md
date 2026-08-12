# FcaBedrock.Cli

Installing this package provides `fcabedrock`, a command-line tool for **Formal
Concept Analysis (FCA) preprocessing**. It ingests structured data — wide
CSV/TSV, or subject–predicate–value triples — applies a user-curated *Bedrock
spec* describing how raw values become formal-context attributes via conceptual
scaling, and emits deterministic Burmeister `.cxt` and FIMI `.dat` formal
contexts for downstream FCA tools.

## Install

`FcaBedrock.Cli` is a **technical preview**. It is **not yet published to a
public NuGet feed**, so the command below applies only once the package is
available on a NuGet source you have already configured:

```text
dotnet tool install --global FcaBedrock.Cli
```

Until then, the route is a local pack from a clone of the sources:

```text
dotnet pack src/FcaBedrock.Cli/FcaBedrock.Cli.csproj -c Release -o <dir>
dotnet tool install --global --add-source <dir> FcaBedrock.Cli
```

Both routes need the .NET 10 SDK. To remove the tool:

```text
dotnet tool uninstall --global FcaBedrock.Cli
```

## Commands

`SPEC`, `DATA` and `BED` are positional operands; everything else is a named
option, spelled in full, with no aliases and no abbreviations.

### convert

Convert a data source into a formal context.

```text
fcabedrock convert spec.toml data.csv --out out/context --format both
```

Writes `out/context.cxt` and/or `out/context.dat`, plus
`out/context.manifest.toml` — the run's audit record, emitted by default and
suppressed with `--no-manifest`. `--out` names a base: the ruled extension is
appended, there is no default and no inference from an extension, and
`--format cxt|dat|both` is required. Existing targets are refused without
`--force`. `--temp-dir DIR` relocates spill storage. Stdout is empty on
success; nothing becomes public unless the whole run succeeds. `--v2-compat`
applies the v2 compatibility preset (CRLF, v2 bin-label style, v2 `.dat`
conventions); exact byte equality with v2 output is locked for the checked-in
golden fixture families — it is not a promise of universal v2 byte reproduction
for arbitrary inputs.

### validate

Validate a spec, optionally against a data source's schema.

```text
fcabedrock validate spec.toml data.csv
```

Schema validation only. With DATA it reads just enough to acquire the schema —
the header, or the first record when headerless — and checks name bindings.
DATA is optional; the spec alone validates too:

```text
fcabedrock validate spec.toml
```

Prints nothing on success — exit 0 is the answer. Problems appear as stderr
diagnostics. Writes nothing.

### plan

Print the conversion plan and the three native fingerprints.

```text
fcabedrock plan spec.toml data.csv
```

Prints the full plan to stdout: every formal attribute with its canonical
identity and rendered name, the calibration and restriction summaries, and the
three native fingerprints. DATA is required for every plan — calibration may
need rows — and no file is written.

### stats

Print formal-context statistics.

```text
fcabedrock stats spec.toml data.csv
```

One counting pass over the context. Prints exactly six fields: `objects`,
`formal_attributes`, `crosses`, `density`, `crossless_objects`,
`empty_attributes`. When the context has zero cells it prints exactly
`density = n/a (0 cells)` and keeps the other five fields. Writes nothing.

### calibrate

Freeze every data-dependent outcome into a standalone spec.

```text
fcabedrock calibrate spec.toml data.csv --out frozen.toml
```

Freezes automatic cuts, observed domains, `include` additions, and pass-through
bins into a fully-fingerprinted standalone spec; an `extends` chain is
flattened. The result is byte-idempotent when rerun on its own output.
`--out -` writes the spec to stdout instead; `--force` is a file-target option
and is rejected with `--out -`.

### probe

Generate a draft spec from a data source.

```text
fcabedrock probe data.csv --shape wide --out draft.toml
```

Generates an editable draft spec. `--shape wide|triple` is required and never
inferred. A file target leaves stdout empty; `--out -` emits the canonical
draft on stdout instead; `--force` applies only to a file target and is
rejected with `--out -`. Optional read settings: `--delimiter`, `--header`,
`--missing-token`, `--locale`, `--limit`. Triple additionally accepts
`--ordering` and the `--subject`/`--predicate`/`--value` trio — supplied
together, in one addressing mode (all zero-based indices or all header names),
with names requiring `--header true`.

### migrate

Migrate a v2 `.bed` file to a Bedrock spec.

```text
fcabedrock migrate legacy.bed --out migrated.toml
```

One-way v2 `.bed` → TOML. `--shape` defaults to `wide`;
`--scaling discrete|progressive` defaults to `discrete`. Under the wide shape,
`--object-key row_index|column` is available and `--object-key-column` is
required exactly when `--object-key column`, and invalid otherwise. Triple role
options follow probe's addressing rules, and triple output always authors
`ordering = "unordered"` — there is no `--ordering` option. Delivery matches
probe: a file target leaves stdout empty, `--out -` writes to stdout, `--force`
applies only to a file target and is rejected with `--out -`, and an existing
target is refused without `--force`.

### fingerprint

Report the three native fingerprints and each stored field's state.

```text
fcabedrock fingerprint spec.toml data.csv
```

Reports the three computed native fingerprints and each stored field's
`match` / `stale` / `absent` state. `--write --out NEW_SPEC` writes a corrected
copy of the root spec — only for a fully-frozen spec, and it preserves
`extends`. With `--out -` only the corrected spec is written and the report is
suppressed; on a file target the report goes to stdout after the file commits.
`--out` and `--force` are valid only with `--write`, and `--write` requires
`--out`. There is deliberately no `--v2-compat` here: v2 byte compatibility is
a convert-only override.

## Exit codes and diagnostics

Exit codes. 0 success — warnings are included and never move it off 0. 1 any
Error or Fatal diagnostic, or a host, runtime, or publication failure. 2 usage.
3 cooperative cancellation on the exact host token, with no diagnostic. 4 an
unexpected internal fault.

Diagnostics render one LF-terminated line each on stderr; primary results go to
stdout. `fcabedrock --help` prints the full grammar for every command, and
`fcabedrock --version` prints the tool version.

## Technical preview

Validated on **x64**, with Windows as the primary host; broader platform
validation follows at M8. The global tool is explicitly a **temporary**
technical-preview distribution — a standalone route is committed for the public
release.

## License

MIT.

Copyright © Constantinos Orphanides.
