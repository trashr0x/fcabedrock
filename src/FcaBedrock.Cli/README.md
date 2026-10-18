# FcaBedrock.Cli

Installing this package provides `fcabedrock`, a command-line tool for **Formal
Concept Analysis (FCA) preprocessing**. It ingests structured data (wide
CSV/TSV, or subject-predicate-value triples), applies a user-curated *Bedrock
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
option, spelled in full, with no aliases and no abbreviations. An option's
value is the next argument (`--out draft.toml`) or follows `=` in the same
argument (`--out=draft.toml`). The `=` form passes any value, including one
that starts with `--`, such as `--missing-token=--`; the separate form reads
such an argument as the next option. A mistake in the command line, an invalid
option value included, is reported with exit 2 before any input file is read.

### convert

Convert a data source into a formal context.

```text
fcabedrock convert spec.toml data.csv --out out/context --format both
```

Writes `out/context.cxt` and/or `out/context.dat`, plus
`out/context.manifest.toml`: the run's audit record, emitted by default and
suppressed with `--no-manifest`. The folder `out` must already exist: the tool
does not create the folder its outputs go in. `--out` names a base: the ruled extension is
appended, there is no default and no inference from an extension, and
`--format cxt|dat|both` is required. `--out` must end in a file name, and
`--out -` is refused, because `convert` writes files only (see
[How output files are written](#how-output-files-are-written)). Existing
targets are refused without `--force`. With `--no-manifest`, an old
`out/context.manifest.toml` from an earlier run is refused as well, because it
would not describe the new outputs; `--force` removes it rather than replacing
it. `--temp-dir DIR` relocates spill
storage. The folder is used only if the run spills, so it is not checked in
advance, but an empty value is refused. Stdout is empty on
success; nothing becomes public unless the whole run succeeds. `--v2-compat`
applies the v2 compatibility preset (CRLF, v2 bin-label style, ASCII `>=` and
`<=` even when the spec sets `bin_label_unicode = true`, v2 `.dat`
conventions); exact byte equality with v2 output is locked for the checked-in
golden fixture families; it is not a promise of universal v2 byte reproduction
for arbitrary inputs. See [How output files are written](#how-output-files-are-written).

Without `--v2-compat`, `bin_label_unicode = true` writes `≥` and `≤` only where the
tool generates the operator, as in `age-≥50`; values, labels and name formats
you wrote keep their spelling.

### validate

Validate a spec, optionally against a data source's schema.

```text
fcabedrock validate spec.toml data.csv
```

Schema validation only. With DATA it uses only the first record (the header,
or the first non-blank record when headerless) and checks name bindings; no
later record is examined. Reading that record can read further into the file,
and a failure while doing so still fails the command, such as bytes that are
not UTF-8 a few lines in. A file that validates can still fail a later command
on a record that `validate` never read (see [Data files](#data-files)).
DATA can be left out when the spec binds everything by zero-based index:

```text
fcabedrock validate spec.toml
```

Without DATA there is no header, so a spec that binds an attribute's column, the
object key or a triple role by header name fails validation with
`SourceBindingInvalid` or `ObjectKeyBindingInvalid`; the first such line then
suggests `fcabedrock validate SPEC DATA`.

Prints nothing on success; exit 0 is the answer. Problems appear as stderr
diagnostics. Writes nothing.

### plan

Print the conversion plan and the three native fingerprints.

```text
fcabedrock plan spec.toml data.csv
```

Prints the full plan to stdout: every formal attribute with its canonical
identity and rendered name, the calibration and restriction summaries, and the
three native fingerprints. DATA is required for every plan (calibration may
need rows), and no file is written.

The column `missing_policy = "as_attribute"` adds ends its line with
`role=missing`. Its identity is the same as that of a nominal bin for a value
named `missing`, so one attribute cannot have both: that plan fails with
`FormalAttributeCollision`, and relabelling does not help.

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

`declared_domain` entries you wrote, directly or through a template or matcher,
are written back exactly as you wrote them. Entries the run discovered are
written by the tool: a number uses the decimal mark of `binding.locale` with
plain `-`, `+` and `E` (for example `-2,25` under `de-DE`), or the locale's own
signs where plain ones would not read back. These spellings come from the
culture data of the machine that runs `calibrate`. A machine whose culture data
differs can spell a discovered number differently, and for a few locales it
cannot read the other machine's spelling.
`--out -` writes the spec to stdout instead; `--force` is a file-target option
and is rejected with `--out -`. See
[How output files are written](#how-output-files-are-written).

### probe

Generate a draft spec from a data source.

```text
fcabedrock probe data.csv --shape wide --out draft.toml
```

Generates an editable draft spec. `--shape wide|triple` is required and never
inferred. A file target leaves stdout empty; `--out -` emits the canonical
draft on stdout instead; `--force` applies only to a file target and is
rejected with `--out -`. Optional read settings: `--delimiter`, `--header`,
`--missing-token`, `--locale`, `--limit`. `--delimiter` takes one character, or
the word `tab` in any letter case for a tab, which cmd.exe cannot type; the
draft still records the tab character itself. Triple also accepts
`--ordering` and the `--subject`/`--predicate`/`--value` trio: supplied
together, in one addressing mode (all zero-based indices or all header names),
with names requiring `--header true`. See
[How output files are written](#how-output-files-are-written).

A category that contains a line break (CR or LF) cannot name a column. When
all of an attribute's distinct values fit within `--limit`, the draft leaves
those categories out, keeps the others, sets that attribute's
`unknown_value_policy = "warn"`, and records the omission in the first line of
`[provenance].notes` and in the attribute's `description`, so the file itself
says what was left out. Every object is kept. If no category of the attribute
is left, its domain is written as `declared_domain = []`. If an attribute has
more distinct values than `--limit` and any of its values contains a line
break, probe writes no draft (`ProbeUnsafeTruncatedDomain`), because converting
the draft would bring those values back. A higher `--limit` that keeps every
value of that attribute lets probe leave them out instead, but it is not
guaranteed to: the probe's fixed limits on total retained values and text can
then stop it with no draft. Removing the line breaks from the data always
works.

Probe also renames an attribute whose columns could collide with another
attribute's, for example `a` holding `b-z` beside `a-b` holding `z`: both would
name the column `a-b-z`. The renamed attribute still reads the same source
column or predicate; its name becomes `probe_<n>_<k>`, and the
`ProbeAttributeNameAdjusted` warning lists it. Whether a rename is needed can
depend on `--limit`.

### migrate

Migrate a v2 `.bed` file to a Bedrock spec.

```text
fcabedrock migrate legacy.bed --out migrated.toml
```

One-way v2 `.bed` → TOML. The `.bed` file must be UTF-8, like a spec: one
UTF-8 byte order mark at the start is allowed, and anything else that is not
UTF-8 is refused. `--shape` defaults to `wide`;
`--scaling discrete|progressive` defaults to `discrete`. Under the wide shape,
`--object-key row_index|column` is available and `--object-key-column` is
required exactly when `--object-key column`, and invalid otherwise. A column
given by name needs a header row, so it is refused with `--header false`; a
zero-based index works either way. `--delimiter` takes the same spellings as
probe's, `tab` included. Triple role
options follow probe's addressing rules, and triple output always authors
`ordering = "unordered"`; there is no `--ordering` option. Delivery matches
probe: a file target leaves stdout empty, `--out -` writes to stdout, `--force`
applies only to a file target and is rejected with `--out -`, and an existing
target is refused without `--force`. See
[How output files are written](#how-output-files-are-written).

### fingerprint

Report the three native fingerprints and each stored field's state.

```text
fcabedrock fingerprint spec.toml data.csv
```

Reports the three computed native fingerprints and each stored field's
`match` / `stale` / `absent` state. `--write --out NEW_SPEC` writes a corrected
copy of the root spec, only for a fully-frozen spec, and it keeps the root's
`extends` chain. A spec that still depends on its data (for example an omitted
`declared_domain` or automatic cuts) is refused, writing nothing, and the error
names the next step: `fcabedrock calibrate SPEC DATA --out NEW_SPEC` writes a
fully frozen copy with its fingerprints already stored, if the data calibrates.
A copy written into another folder still inherits from the same
base file, reached by the same path: if the authored `extends` no longer leads
there from the new folder, the copy gets a relative reference that does, so its
text can differ from the original while its fingerprints and conversions do not.
The command refuses, writing nothing, when the base is on another drive or share
(no relative reference can reach it) or cannot be verified. With `--out -` only
the corrected spec is written, with `extends` as authored, and the report is
suppressed; on a file target the report goes to stdout after the file commits.
`--out` and `--force` are valid only with `--write`, and `--write` requires
`--out`. There is deliberately no `--v2-compat` here: v2 byte compatibility is
a convert-only override. See
[How output files are written](#how-output-files-are-written).

## How output files are written

Everything that writes a file (`convert` with or without its manifest, and
`probe`, `migrate`, `calibrate` and `fingerprint --write` when `--out` names a
path) publishes the same way. Work is staged beside the destination, checked,
and only then committed by a **rename**, never by writing over your file in
place. A run that fails, is cancelled, or produces an Error leaves nothing
committed. For `convert`, the manifest commits last and is what marks the run
complete.

**`--out -` bypasses all of this.** It writes the document to stdout, so no
file is created, nothing is staged, and `--force` is rejected (`convert` has no
`-` form; it always publishes files).

**`--out` must end in a file name.** The value is checked as text, before any
input is read, and a value that cannot name the file to write is refused with
exit 2: an empty value, and one whose last part is a folder, such as `.`, `..`
or `out/` (on Windows also `out\`, `C:` or `\\server\share`). On Windows a
name that ends in a dot or a space, such as `report.`, is refused too, because
Windows would drop the trailing dots and spaces from the name it writes. Names
with inner dots or spaces, such as
`report v1.data`, and a drive-relative name such as `C:report` are fine, and an
extended path (`\\?\C:\...`) may keep a final dot or space. On Linux and macOS
only `/` separates folders, so a backslash, a final dot and a final space are
ordinary characters there. For `convert`, `--out` names a base, so a folder of
that name does not stop the run: `--out out/context` writes
`out/context.cxt` beside a folder called `out/context`. A folder at the actual
file name, such as `out/context.cxt`, is refused before any work starts. The
folder the files go in must already exist; a run whose folder is missing says so
and creates nothing.

**A read-only file is not replaced on Windows.** With `--force`, a run checks
each existing file it would replace (and an old manifest a `--no-manifest` run
would remove) for the read-only attribute: once before it starts writing the new
files, and again just before it renames the first existing file aside. It
refuses a file the check finds read-only, because Windows lets the run rename
such a file aside but not delete it afterwards. If Windows cannot answer the
check, the run does not refuse the file on that account, and its other checks
still apply. An attribute set after the second check is not caught: the run
then meets the ordinary failure, or publishes and reports the unfinished cleanup
(below). The run never clears the attribute for you: clear it yourself if the
file may be replaced, or choose a different `--out`. On Linux and macOS, missing
write permission on the file itself does not stop the replacement.

**When cleanup after a successful run does not finish.** A run whose outputs are
published still exits 0 when something in its way, such as a file another
program has open, stops it removing all of its private files (names that
contain `.fcabedrock-`). It then prints one `warning:` line naming the output
base and, where known, the first private file that stayed and why. The outputs
and manifest are complete. The next run with the same `--out` finishes that
cleanup before it publishes, or refuses and names the file in its way, such as a
private file that is read-only. Fixing that one obstacle, for example by clearing
the attribute, lets the next run try again; another obstacle can still stop it.
Private files the tool cannot prove it created, and any it does not recognize,
are left in place: inspect them, or choose a different `--out`, rather than
deleting them on the strength of their names.

**One condition applies while a run is publishing.** The output files it is
writing, the directories on the way to them, and the private files it keeps
beside them must be left alone by other programs: during the run, and after an
interruption until you run the command again to let it clean up. Reading a
finished output is always fine. `--force` does not relax this: it only
authorizes replacing an existing destination.

Within a single run, a file that is swapped for a different one is detected and
the run fails without committing or deleting it. The run tries to move that file
back to where it came from; if it cannot, the file stays where the run's own
rename put it, which can be an output path, and the run keeps its private files
beside it. Across a crash that is
not possible to detect in general: an untouched leftover and a replacement that
took its place can look identical to the next run, so if the leftovers may have
been tampered with, remove them yourself rather than letting a retry decide.

Each individual file is committed atomically. **A set of files is not**: if a
run fails part-way through `--format both`, it undoes what it can, but there is
no instant at which all outputs appear together. On Linux and macOS filesystems
that cannot perform an atomically non-replacing rename (some NFS, 9p and FUSE
configurations, and macOS volumes without exclusive-rename support), publication
still works, through a checked rename that leaves a very small window in which
another program could create the destination first and have it overwritten.
Windows does not have that window. Which filesystems are affected is decided by
what the operations report, never by the path's spelling: a
Windows-backed WSL mount is not automatically one of them.

## The replacement character in specs

This version cannot read a spec that contains the replacement character U+FFFD
written literally anywhere in the file. The read fails with `SpecTomlInvalid`,
and the message speaks of an invalid UTF-16 surrogate sequence. The file is
valid: TOML allows the character, and the limitation is in the tool's TOML
reader. In a spec you write by hand, spell the character `\uFFFD` inside a
double-quoted string, which reads as the same character. A single-quoted string
or a comment has no escape, so it cannot hold the character. When the tool
writes a spec or a run manifest, it spells the character `\uFFFD`, so the
values that contain it read back.

## Data files

DATA must be UTF-8 text. One UTF-8 byte order mark at the start is allowed and
skipped, and a replacement character (U+FFFD) stored in the file is ordinary
data. Anything else that is not UTF-8 is refused, never replaced or guessed:
invalid or cut-off byte sequences, and a file that starts with a UTF-16 or
UTF-32 byte order mark, such as a spreadsheet's "Unicode text" export. Save the
file as UTF-8 and run the command again.

When a command cannot read DATA, its `error:` line says why and, when the
reader knows it, where:

```text
error: cannot read the data file 'data.csv': a quoted field is not closed before the end of the input (data record 1, field 0, counted from 0; record starts on line 3, defect on line 4). Add the missing closing quote, or double each quote inside the field.
```

Data record and field numbers count from 0 and skip the header and blank
lines, as `record=` locations do. Line numbers count from 1 and count every line
of the file, including blank lines and the lines inside a quoted field. A file
that is not UTF-8 gets no position, and its error can appear before the
command reaches the record that holds the bad bytes. `probe` reports the same
explanation in its `ProbeSourceReadFailed` diagnostic, with the data file named
at the start of the line.

## Exit codes and diagnostics

Exit codes. 0 success (warnings are included and never move it off 0). 1 any
Error or Fatal diagnostic, or a host, runtime, or publication failure. 2 usage.
3 cooperative cancellation on the exact host token, with no diagnostic. 4 an
unexpected internal fault.

Diagnostics render one LF-terminated line each on stderr; primary results go to
stdout. `fcabedrock --help` prints the full grammar for every command, and
`fcabedrock --version` prints the tool version.

Output redirected to a file or a pipe is UTF-8 without a byte order mark, with
LF line endings. On Windows, a console window receives the text as Unicode,
without conversion to the console's code page, and the tool changes no console
setting. Whether each character, such as `§`, is drawn depends on the console
and its font.

A line that starts with `error:` or `warning:` and has no code comes from the
tool itself, for example a missing input file; a diagnostic line names its code
after the severity. File and attribute names at the start of a diagnostic line
are JSON strings, so a quote in them appears as `\"`. In a message, a quote
appears as itself. In both, a backslash appears doubled (`\\`); a tab, line
break, carriage return, backspace or form feed appears as `\t`, `\n`, `\r`, `\b`
or `\f`; and any other control character or a line or paragraph separator
appears as an escape such as `\u0085`, so one diagnostic never spans two lines.
A message that already quotes a value with its own escapes, such as an invalid
formal-attribute name, has those backslashes doubled too: a name containing a
line break appears as `"x-bad\\nline"`.

Warnings that summarize many rows, such as `UnknownValueObserved`, count every
occurrence and show up to three different examples, in the order the run met
them. Each example is in double quotes, with its own backslash, quote, carriage
return and line feed escaped before the line's escaping doubles those
backslashes: a value holding a line break shows as `"a\\nb"`, one holding a
typed backslash-n as `"a\\\\nb"`, and one holding a quote as `"x\\"y"`. For a
`value_groups` attribute the warning says the values match no group.

Temporary storage is used only when a run's data no longer fits in memory:
grouping an unordered triple source or `dedupe` keys, or counting values for
`equal_frequency` and percentile cuts. It is created only then, so `--temp-dir`
is not checked when nothing needs storing. `GroupingStorageFailed` says what the
storage was doing and why it failed. As an error it stopped the run, and the
line ends by naming `--temp-dir DIR`, which chooses another directory for a new
run, such as an existing, writable folder with free space. As a warning the run
went on, but some temporary files may remain; choosing another `--temp-dir`
later does not remove them.

When a spec, `.bed` or data file cannot be opened, its `error:` line says that
the file does not exist or that access to it was denied, where the failed open
established which; a folder named in place of a file is refused as denied
access. A spec or `.bed` file that is not UTF-8 is reported as such, and any
other failure gets a plain `cannot read` line. A base named by `extends` that
cannot be loaded is the `SpecExtendsNotFound` diagnostic instead: the reference
must be a path relative to the folder of the file that holds it, naming a
readable UTF-8 spec.

## Technical preview

Continuous integration builds and tests this tool natively on **Windows x64**,
**Linux x64**, and **macOS ARM64**, including a smoke test that installs this
package and runs a conversion. The same checks also run on Linux ARM64 and
Windows ARM64 where available; those targets carry no support claim. Real-data
acceptance and performance measurement use Windows x64 only. The global tool is
explicitly a **temporary** technical-preview distribution; a standalone route is
committed for the public release.

## License

MIT.

Copyright © Constantinos Orphanides.
