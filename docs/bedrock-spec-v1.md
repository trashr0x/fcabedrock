# FcaBedrock vNext — Bedrock Spec Schema (v1)

**Status:** Normative. This is the canonical reference for the Bedrock v1
format: when code and this spec disagree, the code is the bug
(`docs/principles.md` P-8). Changes go through review and are recorded in
`docs/decisions.md` — never made silently.

**Audience:** Project maintainers and tool authors implementing readers,
writers, validators, and converters for the Bedrock spec format.

**Out of scope:** Implementation details (algorithms, project layout,
namespace structure, .NET-specifics). UI/UX. Post-context reductions
(handled by sibling tools).

---

## 1. How to read this document

- **Normative** sections describe what a conforming Bedrock spec
  implementation MUST do. They use the words *MUST*, *MUST NOT*,
  *SHOULD*, *MAY* in the RFC 2119 sense.
- **Informative** sections (marked or in worked examples) describe
  intent, motivation, or example usage. They are not constraints on
  implementations.
- TOML examples assume the file is the entire spec unless noted.
  Comments inside TOML examples are informative, not part of the syntax.
- Any field marked **(deferred)** is reserved in v1 and rejected by the
  v1 planner with a clear diagnostic, so v1 spec files using that
  feature parse but fail to plan. The reservation prevents naming
  collisions when the feature is implemented.

## 2. Top-level file structure

A Bedrock spec is a UTF-8 TOML 1.0 document containing the following
sections, all optional except `[spec]`, `[binding]`, and at least one
`[[attribute]]`:

| Section | Cardinality | Purpose |
| --- | --- | --- |
| `[spec]` | exactly 1 | Spec metadata, version, fingerprints |
| `[provenance]` | 0 or 1 | Author, source, lineage metadata |
| `[binding]` | exactly 1 | How this spec applies to a data source |
| `[defaults]` | 0 or 1 | Per-spec default policies |
| `[output]` | 0 or 1 | Output-format options |
| `[[template]]` | 0..N | Named attribute templates for reuse |
| `[[matcher]]` | 0..N | Pattern-based template application |
| `[[attribute]]` | 1..N | Logical attributes and how to scale them |

Order within the file is informative. Fingerprints are computed over a canonical
structure derived from the **resolved/calibrated plan**, not over the spec's TOML
text (§14), so file formatting never affects schema or output identity.

A spec MUST declare `version` in `[spec]`. Implementations encountering
an unknown `version` MUST refuse to load the spec and emit
`SpecVersionUnsupported`.

## 3. The `[spec]` block

```toml
[spec]
version = 1                                 # required, integer
schema_fingerprint     = "sha256:abc123..." # optional; written by tooling (frozen specs)
cxt_output_fingerprint = "sha256:def456..." # optional; written by tooling (frozen specs)
dat_output_fingerprint = "sha256:7890ab..." # optional; written by tooling (frozen specs)
extends = "base.toml"                       # optional; spec composition
description = "Mini-mushroom analysis"      # optional; free text
```

**`version`** *(required, integer)*. Currently `1`. Bump on incompatible
schema changes. Implementations MUST refuse unknown versions.

**`schema_fingerprint`** *(optional, string)*. Deterministic hash of the
formal-attribute schema this spec produces (their ordered list with full
identifying info). Two specs with the same `schema_fingerprint` produce
identical attribute IDs. Controls `.dat` compatibility.

**`cxt_output_fingerprint`** *(optional, string)*. Deterministic hash of
everything that affects the `.cxt` byte-level output, including formal-attribute
rendered names. Controls `.cxt` byte equality.

**`dat_output_fingerprint`** *(optional, string)*. Deterministic hash of
everything that affects the `.dat` byte-level output. `.dat` carries numeric IDs,
not names, so rendered names do not enter it. Controls `.dat` byte equality.

All three fingerprints SHOULD be written by tooling on save **for fully-frozen
specs only** (§14) — a spec whose schema is data-dependent (absent
`declared_domain`, an auto-binning discretizer, `unknown_value_policy =
"include"`, `value_groups` `unmatched = "passthrough"`, or any not-yet-executable
`restrict_to`) omits the stored fingerprints rather than storing a value the next
dataset would invalidate. When present they are verified on load and emitted as
warnings if mismatched (`SchemaFingerprintStale`, `CxtOutputFingerprintStale`,
`DatOutputFingerprintStale`). All are SHA-256 over the plan-derived canonical
structure described in §14.

**`extends`** *(optional, relative path)*. See §13.

**`description`** *(optional, free text)*. Informational only.

## 4. The `[provenance]` block

```toml
[provenance]
author       = "Constantinos Orphanides"
created_at   = 2026-05-09T10:00:00Z          # TOML datetime
source_url   = "https://archive.ics.uci.edu/ml/datasets/Mushroom"
source_hash  = "sha256:..."                  # of the data file this spec was authored against
derived_from = "fcabedrock-v2/agaricus-lepiota.bed"
notes        = "Original v2 spec, modernized for vNext."
```

All fields optional. Provenance never affects schema or output identity
(not part of either fingerprint). It exists for citation, reproducibility
audits, and human readers.

## 5. The `[binding]` block

The binding tells the spec how to apply itself to a concrete data source.

### 5.1 Common binding fields

```toml
[binding]
shape         = "wide"                       # "wide" | "triple"
encoding      = "utf-8"                      # default "utf-8"
delimiter     = ","                          # default ","; any single char
quote_char    = "\""                         # default "\""
has_header    = true                         # only meaningful for shape = "wide"
locale        = "invariant"                  # default "invariant"
missing_token = "?"                          # default "?"; "" disables token detection
```

**`shape`** *(required, enum)*. `"wide"` for one-row-per-object DSV,
`"triple"` for subject-predicate-value DSV. An absent `shape` is
`BindingShapeMissing` (Error).

**`encoding`** *(default `"utf-8"`)*. Any encoding accepted by the
implementation. Implementations MUST support at least UTF-8.

**`delimiter`** *(default `","`)*. A single non-newline character; common
alternatives `"\t"`, `";"`, `"|"`. It MUST differ from `quote_char`
(`BindingDelimiterQuoteConflict` otherwise).

**`quote_char`** *(default `"\""`)*. RFC 4180-style quoting; `""` inside a quoted
field is an escaped quote. **v1 supports only the standard double quote `"`**; a
custom `quote_char` parses but is rejected with `QuoteCharNotSupportedV1`. (The
field is retained so a later version can lift the restriction without a format
change.)

**`has_header`** *(default `true`)*. Only used when `shape = "wide"`. If
true, the first non-empty row is consumed as a header and is available
for column-by-name binding.

**`locale`** *(default `"invariant"`)*. Governs how raw values parse to numbers
(decimal separator). Any IETF BCP 47 tag, or `"invariant"` for culture-invariant
parsing (decimal point). All parsing in the pipeline uses this one declared
locale; determinism comes from the locale being part of the spec, not from
hardcoding invariant. (Date parsing is reserved for the deferred date value
type — §11.7.) Recommended: keep `"invariant"` unless you specifically need a
locale's conventions.

**`missing_token`** *(default `"?"`)*. Any string equal to this token,
after whitespace trimming, is treated as missing. To disable
token-based missing detection, set `missing_token = ""`. Empty string
cells are *always* missing regardless of this setting.

**Whitespace.** Leading and trailing whitespace around an **unquoted** data field
value is trimmed before any interpretation — missing-token detection, matching
against `declared_domain` / `value_labels` keys / `restrict_to` / a `dichotomic`
`true_value` / `value_groups`, and numeric parsing. Whitespace inside a **quoted**
field is preserved (deliberate spaces survive). The spec-side strings you write in
the TOML are taken **verbatim** and never trimmed; only the data-side field value
is. The rule is uniform across all matching, so a value never fails to match
purely because of surrounding spaces in the source file.

### 5.2 Wide-CSV binding

No additional fields. Attributes bind by `index` or `name`:

```toml
[binding]
shape = "wide"
has_header = true
# attributes use { kind = "column", index = N } or { kind = "column", name = "..." }
```

### 5.3 Triple binding

```toml
[binding]
shape = "triple"
ordering = "unordered"                       # "subject_grouped" | "unordered"
columns = { subject = 0, predicate = 1, value = 2 }
```

**`ordering`** *(required for triple, enum)*. `"subject_grouped"` allows
single-pass streaming (faster, less memory). `"unordered"` requires
external sort-merge or buffering (slower, more memory).

**`columns`** *(required for triple, table)*. Maps the three logical
roles to column indices. Supports remapping if the source columns are in
non-standard order. Indices are 0-based.

Attributes under triple binding use
`{ kind = "predicate", name = "..." }` to bind by predicate string.

> **Triple-source surface is finalized at M3.** Beyond the above, v1 reserves but
> does not yet settle the triple-specific surface: header rows for triple input,
> binding `columns` by header **name** (rather than 0-based index), and
> object/subject-name filtering are all deferred to the M3 triple-source audit
> (`roadmap.md`). M2 neither adds nor relies on them; `has_header` stays
> meaningful only for `shape = "wide"`.

### 5.3.1 Triple multi-value and grouping semantics

For triple input, rows with the same resolved object key contribute to the
**same** formal object. Multiple values for the same source predicate are
**unioned**: each matching source value may set its own formal-attribute cross.
A subject with both `(obj1, Tissue, endoderm)` and `(obj1, Tissue, mesoderm)`
yields one formal object `obj1` crossing both `Tissue-endoderm` and
`Tissue-mesoderm`. Duplicate identical triples are **idempotent** (the second
sets the same cross). This is normal input, not a duplicate-object condition,
and `duplicate_object_policy` (§6.1) does not apply to it.

Whether multiple values produce multiple crosses or are folded into one is a
property of the chosen **scale**, not a source-level toggle: `nominal` produces
one cross per distinct value (the union above); `dichotomic` or a `value_groups`
mapping can fold several values into a single formal attribute. There is no
separate "collapse multi-values" option — pick the scale that expresses the
intent (one standard way per concern).

**Grouping under `subject_grouped`.** When `ordering = "subject_grouped"`, all
rows for a given subject MUST be contiguous; this is what permits single-pass,
zero-buffer streaming. If a subject recurs after a different subject has
intervened, the converter emits `TripleSubjectNotContiguous` (Error),
identifying the subject and record index, and stops. Input that is not
subject-grouped MUST declare `ordering = "unordered"`, which buffers or
sort-merges (slower, more memory) and imposes no contiguity requirement.

### 5.4 Object key resolution

```toml
[binding.object_key]
mode = "row_index"                           # "row_index" | "column" | "composite"
```

**`mode = "row_index"`** *(default for wide; not allowed for triple)*.
Object names are `0`, `1`, `2`, … in input order. Keys are unique by
construction; `duplicate_object_policy` does not apply. Declaring `row_index`
under `shape = "triple"` is `ObjectKeyModeInvalidForShape` (Error, spec validate).

**`mode = "column"`**:

```toml
[binding.object_key]
mode = "column"
column = "id"                                # name (with header) or index
```

Object name is taken from the named/indexed column. The column is excluded
from the conversion (no formal attributes generated from it). Duplicate key
values are governed by `duplicate_object_policy` (§6.1). A `column` object key
missing its `column`, or naming a column that does not resolve, is
`ObjectKeyBindingInvalid` (Error, spec validate).

> **Wide `column` execution lands at M3.** Wide `object_key.mode = "column"` (and
> with it the `duplicate_object_policy` machinery, §6.1) is **parsed and
> round-tripped** from M2, but its *execution* is sequenced with the triple
> object-key work at M3: until then conversion **rejects** a wide `column` object
> key with `ObjectKeyColumnNotImplementedV1` (transitional) rather than silently
> falling back to row index. Triple binding's subject-derived `column` key (below)
> is the M3 driver. (M1/M2 wide conversion uses `row_index`.)

**`mode = "composite"`** **(deferred)**:

```toml
[binding.object_key]
mode = "composite"
columns = ["customer_id", "session_id"]
aggregate = "union"                          # "union" | "intersection"
```

Rows sharing the composite key are merged into one formal object. The v1
planner emits `ObjectKeyCompositeNotImplementedV1` and stops.

For triple binding with no `[binding.object_key]` block, the default is
`mode = "column"` with `column` set to the subject column from
`binding.columns.subject`. This matches v2's behavior (subject becomes
object name). Repeated subjects accumulate per §5.3.1 and are not a
duplicate-object condition.

## 6. The `[defaults]` block

Sets per-spec defaults that individual attributes can override. All fields
optional; fall through to hard-coded defaults if absent.

```toml
[defaults]
include                  = true              # attribute is included by default
missing_policy           = "skip"            # "skip" | "as_attribute"
unknown_value_policy     = "warn"            # "skip" | "warn" | "fail" | "include"
duplicate_object_policy  = "fail"            # "fail" | "keep" | "dedupe"
# formal_attribute_format = "{column}-{value}" # optional global override; absent = scale-specific default (§10.7)
ordinal_direction        = "ge"              # "ge" | "le"
ordinal_boundary         = "inclusive"       # "inclusive" | "strict"
```

`duplicate_object_policy` is a spec-wide setting (it affects context
shape, not a per-attribute concern) and SHOULD live only here, not on
individual attributes.

`formal_attribute_format`, when present in `[defaults]`, is an explicit global
override for emitted attributes. If absent, the scale-specific defaults from
§10.7 apply (this is the normal case — there is no hard-coded `{column}-{value}`
default).

`ordinal_direction` and `ordinal_boundary` supply the defaults for an `ordinal`
scale (§12.3) that omits `direction` / `boundary`; a per-attribute `scale` field
wins (§9 precedence). **`direction`** applies to **all** ordinal scales — for cut-bin
scales it selects which bin edge each threshold sits on (`le` → upper, `ge` → lower).
**`boundary`** selects the operator only for **value-bin** ordinal scales (where all
four `direction × boundary` combinations are live); for **cut-bin** scales the
operator is fixed by the cut geometry, so a **defaulted** `boundary` never selects it
and never trips `OrdinalBoundaryIncompatibleWithCuts` (§12.3) — only an
explicitly-authored straddling `boundary` does. Authored-vs-default provenance is
preserved by the reader/writer.

### 6.1 Duplicate object keys

Applicability depends on object-key mode (§5.4):

- **`row_index` (wide):** keys are positional and unique by construction.
  The policy does not apply.
- **triple input:** repeated subjects are normal and accumulate onto one
  formal object (§5.3.1). The policy does not apply; this is never a
  duplicate-object condition.
- **`column` (wide):** two rows with the same key value trigger the policy.

For `column` mode, given input where key `P001` appears at rows 1 and 3:

- **`"fail"`** *(default)*: emit `DuplicateObjectKey` (Error) naming the key
  and record index, and stop. Rationale: choosing a column as the key asserts
  that it *identifies* objects; a duplicate means that assertion is false, and
  the safe response is to surface it rather than silently invent or merge. v2
  wide mode always used `row_index`, so there is no v2 precedent to preserve
  here.
- **`"keep"`**: each row becomes its own formal object; the colliding key is
  disambiguated by appending the record index (`P001`, …, `P001#2`). Emit
  `DuplicateObjectKey` (Warning). Note the generated `#N` name does not appear
  in the source data.
- **`"dedupe"`**: rows sharing a key collapse to one formal object; later rows'
  crosses union onto the first. Emit `DuplicateObjectKey` (Info). Note this can
  cross mutually-exclusive bins on one object (e.g. two ages), which is only
  meaningful for genuinely set-valued data.

There is no `"merge"` value; cross-row merging by a *derived* key is the
deferred `composite` object-key feature (§5.4).

## 7. Processing phases

A conversion proceeds through four ordered phases. Implementations MUST preserve
this separation (see decisions.md D-003, D-005).

1. **Parse / validate** — resolve TOML syntax, `extends` composition, and
   *static* spec validity (cut validation §11.2/§11.8, scale/discretizer
   compatibility — including `OrdinalOrderNotAllowedWithCuts` and
   `OrdinalBoundaryIncompatibleWithCuts` (§12.3), `value_labels` keys in domain
   when live (§10.8), duplicate `name`s, formal-attribute identity collisions,
   `BindingShapeMissing` when `shape` is absent). Reads no data
   *rows*. It MAY inspect source *schema metadata* supplied by the caller —
   header names, column count — to validate source bindings (e.g. a
   `{ kind = "column", name = "age" }` binding against an actual header);
   binding by column *index* needs no schema at all. It does not scan object
   records or values. Produces a validated spec or aggregated diagnostics.
2. **Calibrate** — the only phase that reads data to resolve *data-dependent
   schema elements*: absent `declared_domain`s (observed-domain discovery),
   auto-discretizer cuts (`equal_width`, `equal_frequency`),
   `unknown_value_policy = "include"` extensions, and `value_groups`
   `unmatched = "passthrough"` (which discovers one column per observed ungrouped
   value, §11.6). A numeric value that is **present but unparseable** is excluded
   from calibration (§11.5) — it never influences a cut. Produces a fully-resolved
   spec; calibrated cuts are captured in the run manifest (§15).
3. **Plan** — consume a validated, calibrated spec and produce the immutable
   `ConversionPlan`: the ordered formal-attribute schema with stable IDs, scale
   instances, restriction predicates, and ordering rules. Pure; reads no data.
4. **Emit** — stream objects through the plan, applying restriction, then
   discretization, then scaling, producing the output. `.dat` is single-pass.
   `.cxt` needs the object count and all object names before any incidence row,
   so it uses a replay-or-spool strategy (§18.1) — never materializing the full
   incidence matrix in Core.

**Fully-declared specs skip Calibrate.** A spec with explicit `declared_domain`s,
only `manual_cuts` / `identity` / `value_groups` / `free_per_value`
discretizers, no `unknown_value_policy = "include"`, and no `value_groups`
`unmatched = "passthrough"` is fully determined by its own text: Parse → Plan →
Emit, deterministic from the spec alone, no data pre-pass that affects the schema.

**`convert` auto-calibrates by default** (D-005, D-028): a spec needing
calibration is calibrated in-line, and the resolved cuts are recorded in the
manifest so the run stays reproducible without a separate step. `fcabedrock
calibrate` freezes calibration into the spec (auto cuts become `manual_cuts`)
for version-controlled reproducibility.

**`convert` calibrates but never discovers.** Discovery (draft-spec generation
from data) is the separate `probe` operation (D-003), never performed implicitly
by convert. A spec with an absent `declared_domain` *is* calibrated — the
observed domain is filled in — but the user is warned (`ObservedDomainUsed`,
Warning) because the resulting formal-attribute schema then depends on this
specific input rather than on the spec alone. To make such a spec
input-independent, declare the domain explicitly or freeze it with `calibrate`.

**Calibration and vocabulary precede object filtering.** The formal-attribute
**vocabulary** (which columns exist) and any auto-discretizer **calibration** are
computed over the **input universe** — *before* `restrict_to` (§10.4) selects which
objects are emitted. `restrict_to` filters **emitted objects**, never the
calibration population or the column set: define the attribute vocabulary first,
then select objects (the FCA model). A consequence is that after filtering some
columns may carry no crosses (`AttributeHasNoCrosses`, §16.4) — allowed, not an
error. (Population-relative calibration — quantiles over only the surviving objects
— is a recognized future option recorded in decisions.md/roadmap, not a v1 setting.)

## 8. The `[output]` block

Output-formatting options. All optional with sensible defaults. They feed the
per-format output fingerprints (§14) — `[output.cxt]` and `bin_label_unicode` →
`cxt_output_fingerprint`; `[output.dat]` → `dat_output_fingerprint` — and none
feeds `schema_fingerprint`.

```toml
[output]
bin_label_unicode = false                    # default — ASCII operators (>=, <=)

[output.cxt]
line_endings      = "lf"                     # default — "lf" | "crlf"
trailing_newline  = true                     # default — emit \n after last matrix row
size_advisory_bytes = 1_073_741_824          # default — warn when output would exceed 1 GB

[output.dat]
line_endings              = "lf"             # default — "lf" | "crlf"
base_index                = 1                # default — 1 | 0  (FIMI is 1-based; 0 for ML conventions)
nonempty_line_trailing_space = false         # default — no trailing space after the last item id
empty_line_trailing_space = false            # default — bare empty line for objects with no crosses
```

**`bin_label_unicode`**. With `false` (default), bin labels and ordinal
thresholds use ASCII operators (`<30`, `[30, 40)`, `>=50`). With `true`,
Unicode operators (`<30`, `[30, 40)`, `≥50`). ASCII default chosen for
ConExp compatibility — ConExp is Java/2009-era and not all installations
handle UTF-8 reliably.

**`size_advisory_bytes`**. The convert pipeline computes a projected
`.cxt` size before emit (`n_objects × n_formal_attributes` characters
plus name overhead) and emits `OutputCxtSizeAdvisory` (Warning) if the
estimate exceeds this threshold. Default 1 GB is conservative; ConExp
struggles well below this. Set to `0` to disable.

**`.dat` trailing space.** vNext's native `.dat` output has **no** trailing
space after the last item id on a line (`nonempty_line_trailing_space = false`)
and a bare empty line for crossless objects (`empty_line_trailing_space =
false`). v2 emitted a trailing space on every non-empty line; that is one of
the v2-isms reproduced only under `--v2-compat` (below), not the vNext default.
Both knobs exist for callers whose FIMI consumer has a specific expectation.

**v2 byte-equality mode** is *not* a spec setting — it's a CLI flag
(`--v2-compat`) on the convert command that overrides `[output]` to v2's exact
byte conventions. The complete set of v2-isms, all behind this one flag: CRLF
line endings (`.cxt` and `.dat`), `30to<40`-style bin labels, and a trailing
space on every non-empty `.dat` line. Keeping every v2-ism behind the single
flag means the vNext default output is uniformly clean; v2 reproduction is one
switch, not a scattering of legacy defaults.

## 9. Templates and matchers

### 9.1 `[[template]]`

Reusable attribute configurations referenced by name. Templates may
contain any `[[attribute]]` field except `name`, `source`, and
`description` (those are always per-attribute).

```toml
[[template]]
id = "boolean_yes_no"
discretizer    = { kind = "identity" }
scale          = { kind = "dichotomic", true_value = "Yes" }
declared_domain = ["Yes", "No"]
```

### 9.2 `[[matcher]]`

Applies a template to attributes matched by a pattern. Used as the
replacement for v2's "Repeat-To" feature. Matchers run in declaration
order; the last match wins for any given attribute.

```toml
[[matcher]]
match    = { name_regex = "^feature_\\d+$" }
template = "boolean_yes_no"

[[matcher]]
match    = { source_index_range = [10, 1553] }   # for v2 Internet-Ads-style data
template = "boolean_yes_no"
```

**Resolution order** (lowest to highest precedence) for an attribute's
final config:

1. Hard-coded built-in defaults
2. `[defaults]` overrides
3. Matcher templates (in declaration order)
4. Per-attribute `template = "..."` reference
5. Per-attribute explicit fields

The resolved (post-merge) per-attribute config is what feeds into the
schema fingerprint. Source-file template references and matcher rules
are not part of the fingerprint themselves.

> **Resolution lands at M6; M2 carries them through.** Templates and matchers are
> **parsed, preserved, and merged under `extends`** in M2 (so a spec using them
> round-trips through the TOML reader/writer), but they are **not applied**. Any
> spec that actually *uses* them — a present `[[matcher]]`, or an `[[attribute]]`
> with a `template = "..."` reference — fails planning/conversion with
> `TemplateMatcherNotImplementedV1` until matcher resolution is implemented at M6
> (`roadmap.md`). An unreferenced `[[template]]` block round-trips without error.

## 10. The `[[attribute]]` block

Each `[[attribute]]` describes one logical attribute and how it becomes
zero or more formal attributes.

### 10.1 Common fields

```toml
[[attribute]]
name         = "education"                   # required, unique within spec
source       = { kind = "column", index = 1 } # required; see §10.2
display_name = "Education"                   # optional; defaults to name
description  = "Highest level of education"  # optional; informational
include      = true                          # default from [defaults] or hard-coded true
template     = "..."                         # optional template id to inherit from
```

**`name`** *(required, unique, string)*. Any non-empty string excluding
newlines and the TOML key-quoting character `"`. Real-world data files
use names like `"bruises?"`, `"feature.1"`, `"days@home"`; the spec
accepts these as-is so it can round-trip through `.bed` migration and
other external sources without renaming. Matchers use full regex
matching against this string. Template `id` fields (§9.1) use the
stricter `[A-Za-z_][A-Za-z0-9_-]*` form because they're referenced by
code.

**`source`** *(required)*. See §10.2.

**`display_name`** *(optional)*. Used in `formal_attribute_format`'s
`{display_name}` placeholder. Defaults to `name`.

**`include`** *(boolean, default per `[defaults]` or `true`)*. If `false`,
the attribute generates no formal attributes and emits no incidence — but its
own `restrict_to` filter (§10.4) still applies. This is the **filter-only
attribute** pattern: filter objects by a field without analyzing that field.
An `include = false` attribute with no `restrict_to` is inert (a harmless
no-op, allowed during staged spec editing). The attribute is always validated
syntactically regardless of `include`.

### 10.2 Source bindings

For `binding.shape = "wide"`:

```toml
source = { kind = "column", index = 0 }
source = { kind = "column", name = "age" }   # requires has_header = true
```

For `binding.shape = "triple"`:

```toml
source = { kind = "predicate", name = "age" }
```

A wide-CSV `column` source MUST supply **exactly one** of `index` or `name`
(neither or both is `SourceBindingInvalid`); binding by `name` requires
`has_header = true` (also `SourceBindingInvalid` otherwise), while binding by
`index` needs no header.

A source may declare a **value type** controlling how raw values parse before
discretization:

```toml
source = { kind = "column", index = 0, value_type = "number" }            # numeric parse
source = { kind = "column", index = 1, value_type = "string" }            # no parse (default for identity/value_groups)
```

`value_type` is `"string"` or `"number"`. Each discretizer either **fixes** the
type or is **flexible**:

- **String-fixing** — `identity`, `value_groups`, `ordered_cuts`: bins are category
  strings; only `value_type = "string"` is valid (the default).
- **Number-fixing** — `manual_cuts`, `equal_width`, `equal_frequency`: cuts are
  numeric; only `value_type = "number"` is valid (the default).
- **Flexible** — `free_per_value`: accepts **either**. With `"string"` (default)
  each distinct spelling is its own bin; with `"number"` the *parsed numeric value*
  is the bin identity, so `90`, `90.0`, and `9e1` collapse to one bin. Use
  `free_per_value` — not `identity`, which is string-fixing — for numeric
  distinct-value binning.

The value `"date"` is **reserved but not implemented in v1** (§11.7): a spec setting
`value_type = "date"` parses but is rejected by the v1 planner with
`DateValueTypeNotImplementedV1`. A `value_type` that is not one of these, that a
type-fixing discretizer disallows (e.g. `identity` + `"number"`, or `manual_cuts` +
`"string"`), or that conflicts with the `restrict_to`-implied type — a **string**
`value_type` paired with a numeric-**range** `restrict_to` — is
`SourceValueTypeInvalid` (Error). The mirror case, a **numeric** source with a
non-range string `restrict_to` entry, is owned by `RestrictToOnNumericRequiresRange`
(§10.4), not this code.

A spec MUST NOT declare two attributes with the same `name`. Two attributes
MAY share the same `source` — this is how one field carries multiple scalings
(e.g. `age` as nominal bins alongside `age_ordinal` as ordinal thresholds, or
an emitted attribute plus a filter-only attribute over the same field with
different semantics). Distinct `name`s keep the **canonical** formal-attribute
identities apart (§14). Two failure modes, distinct causes:

- If two configurations would produce an identical **canonical identity**
  (logical name + scale + bin/threshold key + operator), the planner emits
  `FormalAttributeCollision` (Error).
- If two distinct canonical identities would render to the **same `.cxt`
  name** (e.g. a `formal_attribute_format` that drops `{column}`), the planner
  emits `FormalAttributeNameCollision` (Error) — duplicate column names in a
  `.cxt` are confusing and almost always unintended.

### 10.3 declared_domain

```toml
declared_domain = ["Bachelors", "Masters", "PhD", "HS-grad", "11th"]
```

The set of raw values that are recognized as schema-bearing. Values
*not* in this list are subject to `unknown_value_policy`. Only meaningful
for the raw-value discretizers `identity` and `free_per_value`; ignored for
cut-based discretizers (`manual_cuts`, `ordered_cuts`, `equal_width`, etc.) and
for `value_groups` (whose `groups` + `unmatched` already define recognition —
§11.6, D-055).

When `declared_domain` is explicit, its **declaration order drives the
formal-attribute (column) order** for `identity` and `free_per_value` scaled
nominally (§17 rule 3) — this is what makes a spec-first run reproducible and
v2-byte-compatible regardless of the order values happen to appear in the data.

If `declared_domain` is absent **or an empty list `[]`**, the Calibrate phase (§7)
fills it from the observed domain in the data, and the user is warned
(`ObservedDomainUsed`) because the resulting schema then depends on this specific
input. An empty `[]` is treated as **absent** — *not* as "zero columns"; only a
**non-empty** explicit list drives column order (§17 rule 3). The TOML
reader/writer round-trips an authored `[]` verbatim; `calibrate`/freeze may replace
it with the observed values. For input-independent, spec-first workflows, declare
the domain explicitly or freeze it with `fcabedrock calibrate`.

### 10.4 restrict_to

Object-level filter. Empty/absent ⇒ no filter. Multiple entries within
one attribute are OR'd; restrictions across multiple attributes are
AND'd. Operates on **raw values** before discretization.

`restrict_to` applies whether or not the attribute is included (§10.1). A
**filter-only attribute** (`include = false` + `restrict_to`) filters objects
without contributing any formal attribute to the schema — the clean way to
say "keep only objects whose Gene is Bmp5" without emitting a Gene column.

For categorical sources (string raw values):

```toml
restrict_to = ["Bachelors", "PhD"]
```

For continuous sources (numeric raw values):

```toml
restrict_to = [
  { from = 10, to = 20 },                    # 10 ≤ x < 20
  { from = 40, to = 50 },                    # 40 ≤ x < 50
  { from = 90 },                             # x ≥ 90 (open-ended)
  { to = 5 },                                # x < 5 (open-ended)
]
```

Range bounds are inclusive on the low side and exclusive on the high
side, matching the bin convention.

Mixed forms (string and range) within the same `restrict_to` list **parse and
round-trip** (D-057), but each entry must satisfy the attribute's single
`value_type` (§10.2): a string-fixing source accepts only string entries, a
number-fixing source only ranges. A genuinely mixed list is therefore a
**validation error** (`SourceValueTypeInvalid` / `RestrictToOnNumericRequiresRange`,
below) — no single-attribute `value_type` admits both.

**Static validation (M2).** Even though `restrict_to` *execution* is deferred
(below), its *shape* is validated at parse/validate from M2 onward:

- a numeric source (`value_type = "number"`, or a numeric-cut discretizer) whose
  `restrict_to` contains a non-range (bare string) entry is
  `RestrictToOnNumericRequiresRange` (Error). This code — **not**
  `SourceValueTypeInvalid` (§10.2) — owns the numeric-source/string-entry mismatch;
- a `restrict_to` string value absent from an explicit `declared_domain` (where one
  applies — `identity` / `free_per_value`) is `RestrictToValueNotInDomain`
  (Warning), a typo-catcher.

These are *shape* checks only — no rows are filtered until execution lands at M4.

> **Execution lands at the restriction milestone (M4).** `restrict_to` is a v1
> feature, but its *execution* is sequenced after M2: M2 **parses and
> round-trips** every form above (string list, open- and closed-range, mixed),
> yet planning/conversion **rejects** any `restrict_to` with
> `RestrictToNotImplementedV1` until M4 (`roadmap.md`) — it is never silently
> ignored. While unimplemented, `restrict_to` does **not** enter the output
> fingerprints, and a spec containing any `restrict_to` is not "fully frozen", so
> tooling stores no fingerprints for it (§14).

### 10.5 missing_policy

```toml
missing_policy = "skip"           # default — missing produces no cross
missing_policy = "as_attribute"   # missing produces a "<name>-missing" formal attribute
```

`"as_attribute"` matches the v2 behavior of including `?` in
`[Category Values]`: missing values produce their own formal attribute
in the schema and that attribute crosses for objects with missing
values. For nominal scales this is one extra formal attribute; for
dichotomic scales it is the second formal attribute (true_value crosses
when present, missing crosses when absent); for ordinal scales it
follows the threshold formal attributes. The rule is uniform: the
missing attribute appends after the scale's formal attributes (D-074).

### 10.6 unknown_value_policy

```toml
unknown_value_policy = "warn"     # default — no cross, object kept, emit UnknownValueObserved (Warning)
unknown_value_policy = "skip"     # observed value not in declared_domain → no cross, object kept, no diagnostic
unknown_value_policy = "fail"     # abort conversion: UnknownValueObserved (Error)
unknown_value_policy = "include"  # extend declared_domain on the fly with the observed value
```

Default `"warn"` is the data-quality-conscious choice: unknown values are
not silently dropped, but the conversion still completes. Switch to
`"skip"` when you've intentionally declared a partial domain and don't
want warnings about the values you knew you were dropping. `"fail"` is
for production pipelines where unexpected values indicate upstream
breakage. `"include"` is for exploratory work — it resolves during the
Calibrate phase (§7), extends the schema with each newly-observed value, and
therefore makes `schema_fingerprint` data-dependent; implementations MUST
recompute the fingerprint after calibration and emit `UnknownValuePolicyInclude`
(Warning) so the data-dependence is visible. The `UnknownValueObserved`
severity follows the policy: `warn` → Warning, `fail` → Error.

**Numeric attributes.** For numeric attributes (cut-based discretizers, where
`declared_domain` is ignored — §10.3) there are no out-of-domain *categorical*
values, so `unknown_value_policy` instead governs the severity of a **present but
unparseable** numeric value (`SourceValueUnparseable`, §11.5): `skip` → no cross,
no diagnostic; `warn` → no cross, Warning; `fail` → Error/abort; `include` →
no cross, Warning (an unparseable token cannot be added to a numeric domain, so
`include` behaves as `warn` here).

### 10.7 formal_attribute_format

**Default formal-attribute naming is scale-specific** — used when an attribute
does *not* set `formal_attribute_format` explicitly:

- `nominal`: `{column}-{value}` (e.g. `gill-size-broad`)
- `ordinal`: `{column}-{scale_op}{value}` (e.g. `age->=40`)
- `dichotomic`: `{column}` **alone** — the single formal attribute is the column
  name with no value suffix (matches v2: `bruises?`, `US-citizen`, not
  `bruises?-bruises`)
- `missing_policy = "as_attribute"` adds `{column}-missing`

An explicit `formal_attribute_format` **overrides the scale default entirely**
and applies to every formal attribute that attribute produces:

```toml
formal_attribute_format = "{column}-{value}"     # explicit nominal-style
formal_attribute_format = "{value}"              # just the value, no column prefix
formal_attribute_format = "{display_name}::{value}" # custom separator
```

For a dichotomic attribute, the default omits `{value}`; to opt *into*
`bruises?-bruises` you set `formal_attribute_format = "{column}-{value}"`
explicitly. Because the override is total, M1 byte-equality relies on the
dichotomic default being `{column}` alone (no explicit format in the v2-derived
specs).

Template placeholders:

- `{name}` — the attribute's `name` field
- `{column}` — same as `name` (alias for clarity in wide-CSV context)
- `{display_name}` — the attribute's `display_name`, defaults to `name`
- `{value}` — the value-side label (a discretizer bin label, a category
  value, etc.)
- `{scale_op}` — for ordinal scales, the inequality operator (`>=`, `<=`,
  `<`, `>`, or Unicode per §8); empty for non-ordinal scales

**`{value}` resolution by formal-attribute kind** (so a custom
`formal_attribute_format` that uses `{value}` is well-defined everywhere):

| Formal attribute | `{value}` resolves to |
| --- | --- |
| `nominal` bin | the bin label (category value or cut-bin label) |
| `ordinal` threshold | the threshold label (`{scale_op}` carries the operator) |
| `dichotomic` (the single column) | the scale's `true_value` (the default format omits it) |
| `missing_policy = "as_attribute"` column | the literal `missing` |

If two formal attributes render to the same name under the chosen format (e.g. a
real category value `missing` colliding with the missing column), the planner
emits `FormalAttributeNameCollision` (§10.2).

This setting affects `cxt_output_fingerprint` only — not `schema_fingerprint`, and
not `dat_output_fingerprint` (`.dat` carries numeric IDs, no names).

### 10.8 value_labels

A map from raw value (as it appears in the source file) to display label
(as it appears in formal-attribute names). v2's `[Attribute Categories]`
served exactly this role: the data file contains `b` and `n` for
gill-size, but the output `.cxt` shows `gill-size-broad` and
`gill-size-narrow`. Without `value_labels`, the spec would have to
either rewrite the data file or accept ugly formal-attribute names.

```toml
declared_domain = ["b", "n"]
value_labels    = { b = "broad", n = "narrow" }
# Formal attributes (with default format "{column}-{value}"):
#   gill-size-broad, gill-size-narrow
# Raw incidence in the file is still matched against "b" and "n".
```

**Applicability.** `value_labels` is consulted only when the discretizer is
`identity` or `free_per_value` — those are the discretizers whose bin label IS
the raw value. For `value_groups`, the group `label` field already serves this
purpose; for `manual_cuts` and the auto-binning discretizers, bin labels are
computed from cuts. Under any of those discretizers `value_labels` is **dormant
and ignored** — never an error — exactly as `declared_domain` is ignored for
cut-based discretizers (§10.3). This keeps an attribute toggleable: switching its
discretizer does not force you to strip retained labels (D-049).

**Coverage.** This applies only when `value_labels` is *live* (an `identity` or
`free_per_value` discretizer). A raw value present in `declared_domain` but
absent from `value_labels` falls through to the raw value as label. A label in
`value_labels` for a value not in `declared_domain` is an error
(`ValueLabelKeyNotInDomain`) — a typo-catcher. Under a discretizer that does not
consult `value_labels` the labels are dormant, so neither error fires.

**Determinism.** `value_labels` affects only the output formal-attribute
names, not their *order* or *count*. It contributes to
`cxt_output_fingerprint` (rendered names) but not `schema_fingerprint` or
`dat_output_fingerprint`.

### 10.9 discretizer and scale

`discretizer` and `scale` are **required when `include = true`** — the two
orthogonal pieces that turn raw values into formal attributes (see §11 and
§12).

```toml
discretizer = { kind = "...", ... }
scale       = { kind = "...", ... }
```

**`include = false` is an authoring toggle (D-049).** When `include = false` the
attribute emits no formal attributes and no incidence, and `discretizer`/`scale`
MAY be omitted. But any emitted-shaping config it *does* carry — `discretizer`,
`scale`, `value_labels`, `declared_domain`, `formal_attribute_format`,
`display_name`, `missing_policy`, `unknown_value_policy` — is **retained but
ignored**, never an error. This lets you park an attribute (toggle it off without
stripping its scale) and switch it back on later with its configuration intact —
the round-trip the TOML reader/writer relies on. The attribute is still validated
*syntactically* (§10.1), and `restrict_to` still applies (§10.4); only the
emitted-shaping semantics are dormant while excluded. The states:

- `include = false` + `restrict_to` → filter-only (filters objects, emits nothing).
- `include = false`, anything else → inactive: emits nothing; carried config is parked.
- `include = true` → active: `discretizer` and `scale` required and fully validated.

## 11. Discretizer reference

A discretizer maps each raw value to either a bin label or to "no bin"
(missing or out-of-range). The output of a discretizer is the input to a
scale.

### 11.1 `identity`

Pass-through. Each distinct raw value becomes its own bin, with the
value itself as the label.

```toml
discretizer = { kind = "identity" }
```

Pairs naturally with `nominal` (one formal attribute per value) or
`dichotomic` (one formal attribute total). With `ordinal`, requires the
scale's `order` field to declare ordering over the values.

### 11.2 `manual_cuts`

User-defined numeric cut points.

```toml
discretizer = {
  kind = "manual_cuts",
  cuts = [30, 40, 50],                       # required; ascending; numeric
  ends = "open",                             # default "open"; "open" | "closed"
}
```

**`cuts`** *(required, array of numbers, length ≥ 1, strictly ascending)*.

**`ends`** *(default `"open"`)*. With `"open"`, bins extend to ±∞ at the
ends — three cuts produce four bins (`<c0`, `[c0,c1)`, `[c1,c2)`, `≥c2`).
With `"closed"`, only the interior bins are produced — three cuts
produce two bins (`[c0,c1)`, `[c1,c2)`); values outside the cuts'
overall range produce no bin (treated as out-of-range). `ends = "closed"`
therefore requires **at least two cuts** (one cut yields no interior bin); fewer
is `DiscretizerEndsClosedTooFewCuts` (Error).

Out-of-range objects are kept (no cross emitted for the discretized
attribute, similar to missing). To exclude them entirely, use
`restrict_to`.

Bin label format: `"<{c0}"`, `"[{c_i}, {c_{i+1}})"`, `">={c_n}"` (ASCII
operators by default; see §8 for the `bin_label_unicode` knob). The
exact format affects `cxt_output_fingerprint` (a `.cxt` name concern) but not
`schema_fingerprint`.
A v2-compat byte-equality mode is available at the writer level (CLI
flag `--v2-compat` on `convert`), not as a spec setting, since v2
compatibility is a one-time output concern rather than a spec property.

### 11.3 `free_per_value`

One bin per distinct observed value (or declared domain). Equivalent to
`identity` for categorical sources; useful for numeric sources where
you want one bin per distinct number rather than ranges.

```toml
discretizer = { kind = "free_per_value" }
```

Different from `identity` in that it is **type-flexible** (§10.2): with
`value_type = "number"` its bin identity is the *parsed numeric value*, so `90`,
`90.0`, and `9e1` collapse to one bin; with `value_type = "string"` (the default)
each distinct spelling is its own bin. Use `free_per_value` — not `identity` — for
numeric distinct-value bins; `identity` is string-only (§10.2).

### 11.4 `equal_width`

Auto-computed cuts at equal width.

```toml
discretizer = {
  kind  = "equal_width",
  bins  = 4,                                 # required, integer ≥ 2
  range = "min_max",                         # default
        # | "percentile_p1_p99"
        # | "manual"
  vmin  = 0.0, vmax = 100.0,                 # required only when range = "manual"
  precision = "exact",                       # default "exact" | { round_to = 1.0 }
}
```

Calibration produces `bins - 1` cut points. Combined with `ends = "open"`
(implicit; the auto-discretizer always uses open ends so new data outside
the calibration range still falls in the first/last bin), this gives
exactly `bins` bins.

The calibrated cut values MUST be captured in the run manifest sidecar
(see §15) when calibration runs on-the-fly.

### 11.5 `equal_frequency`

Auto-computed cuts placed for approximately equal counts per bin.

```toml
discretizer = {
  kind          = "equal_frequency",
  bins          = 4,                         # required, integer ≥ 2
  tie_policy    = "left",                    # default; "left" | "right"
  cut_placement = "right_value",             # default; "right_value" | "midpoint"
}
```

**`tie_policy = "left"`**: values equal to a cut fall into the lower
bin. **`"right"`**: into the upper bin. With heavy ties, bin counts may
deviate substantially from `n/bins`; this is inherent to the algorithm,
not a bug.

**`cut_placement = "right_value"`**: cuts equal the first value of each
new bin. **`"midpoint"`**: cuts equal `(last_of_bin_i + first_of_bin_i+1)/2`.

**Determinism (normative).** The calibration is reproducible across machines and
runs under these rules, which apply to both `equal_width` and `equal_frequency`:

- **Parsing:** raw values are parsed to `double` using `binding.locale` (§5.1),
  whose default is `invariant`. Determinism comes from the locale being a
  *declared* part of the spec, not from hardcoding invariant — the same spec
  parses identically everywhere. A value that is **present but not a usable finite
  number** — it fails to parse under that locale, or parses to NaN or ±∞ — is
  **not** treated as missing (D-050, superseding the earlier "NaN/∞ → missing"
  wording). It is a present-but-invalid value: the object is kept, no cross is
  emitted for that attribute, the value is **excluded** from calibration (it never
  influences a cut), and `SourceValueUnparseable` is reported at the severity
  `unknown_value_policy` selects (§10.6). Only empty cells and explicit
  `missing_token` matches are *missing* and follow `missing_policy`.
- **Sort:** calibration sorts the surviving values ascending by IEEE-754
  total order (`double` default comparer), a stable, culture-independent order.
- **Insufficient distinct values:** if the count of distinct surviving values is
  fewer than `bins`, the planner emits `CalibrationDataInsufficient` (Error) and
  stops, rather than silently producing fewer bins.
- **Ties:** governed by `tie_policy` (above); the result is fully determined.

The exact quantile-index formula and the rounding precision of `midpoint`
cut values and their rendered labels are **settled at M4** (the calibration
milestone) and pinned by golden tests then; they are deliberately not frozen
here, as they are tuning choices rather than determinism guarantees. The rules
above are the determinism guarantees and are stable now.

### 11.6 `value_groups`

Many-to-one value mapping. Each group has a `label` and either an
explicit `values` list, a `pattern` (regex), or both.

```toml
discretizer = {
  kind = "value_groups",
  groups = [
    { label = "Uni-Degree", values = ["Bachelors", "Masters", "PhD"] },
    { label = "School",     values = ["11th", "HS-grad"] },
  ],
  unmatched = "skip",                        # default; "skip" | "other" | "passthrough"
}
```

**Group form: explicit values**:

```toml
{ label = "...", values = ["v1", "v2", ...] }
```

**Group form: regex pattern** (using .NET regex syntax):

```toml
{ label = "ICD-10-Cardiac", pattern = "^I[0-9]{2}" }
```

**Group form: combined** — value matches if it's in `values` *or* matches
`pattern`:

```toml
{ label = "...", values = [...], pattern = "..." }
```

**`unmatched`**:

- `"skip"` — values not matching any group produce no bin (subject to
  `unknown_value_policy`).
- `"other"` — values not matching any group fall into a synthetic group
  labelled `Other`.
- `"passthrough"` — values not matching any group keep their raw value
  as the bin label (mixed grouped and ungrouped attributes). Because the set of
  pass-through bins is **discovered from the data**, this makes the schema
  data-dependent: it resolves in the Calibrate phase (§7), emits
  `ValueGroupsPassthroughDataDependent` (Warning), and — like the other
  data-dependent cases — means tooling stores no fingerprints for the spec unless
  it is frozen (§14).

A value matching multiple groups falls into the first matching group in
declaration order; this is part of the planner's deterministic resolution
and IS captured in the schema fingerprint.

`declared_domain` is **not** consulted for `value_groups` (D-055): the `groups`
and the `unmatched` policy together define which values are recognized, so a
separate domain list would be a second, overlapping gate. Recognition is
therefore: a value matches a group → its group label; otherwise the `unmatched`
policy decides (`skip` defers to `unknown_value_policy`, `other` → the synthetic
`Other` bin, `passthrough` → the value's own raw label).

### 11.7 Date-valued sources (deferred)

Date-valued scaling is **reserved but not implemented in v1**. v1 treats dates
as strings (via `identity` / `value_groups`) unless a future date value type
and discretizer are implemented. A spec that sets `value_type = "date"`
(§10.2) is parsed but rejected by the v1 planner with
`DateValueTypeNotImplementedV1`.

This is a deliberate scope decision: continuous *numeric* support is the v1
priority, and full date support pulls in cut syntax, `DateOnly`/`DateTime`
semantics, day-space binning, label rounding, and date-specific diagnostics
that are not worth front-loading before coding. v2 had a distinct date type
(`d`), so this is a conscious parity deferral, not an oversight (see
decisions.md D-038, lineage.md). When date support lands post-v1, the intended
shape is unchanged from the orthogonal model: a date source parses to a
sortable date value that feeds the existing numeric discretizers — no new
scale. The `mini-dates` example is correspondingly a **deferred** fixture, not
part of the M1 compatibility target.

### 11.8 `ordered_cuts`

Cut points over an **ordered categorical** domain — the categorical sibling of
`manual_cuts` (§11.2). Where `manual_cuts` cuts a numeric axis, `ordered_cuts`
cuts a user-declared category order. This is how v2's `n` (ordinal) type is
expressed in the orthogonal model.

```toml
discretizer = {
  kind  = "ordered_cuts",
  order = ["Unskilled", "Clerical", "Professional", "Managerial"],  # required; low → high
  cuts  = ["Managerial"],                                           # required; members of order, ascending by position
  ends  = "open",                                                   # default "open"; "open" | "closed"
}
```

**`order`** *(required, array of strings)*. The domain low→high. A raw value not
in `order` produces **no bin** (subject to `unknown_value_policy`). Entries MUST
be distinct and non-empty; duplicate or empty entries are `OrderDomainInvalid`
(Error).

**`cuts`** *(required, length ≥ 1)*. Each is a member of `order`, strictly
ascending by position. A value equal to a cut falls into the bin **at or above**
it — the same half-open rule as `manual_cuts` (the cut is the lower edge of the
upper bin). A cut not present in `order` is `OrderedCutsCutNotInDomain` (Error);
cuts out of position order or duplicated are `OrderedCutsNotAscending` (Error).
The `ends = "closed"` ≥2-cuts rule (§11.2) applies here too
(`DiscretizerEndsClosedTooFewCuts`).

**`ends`** behaves as in §11.2. Bin labels reuse the §11.2 template over the
**category strings**: `"<{c0}"`, `"[{c_i}, {c_{i+1}})"`, `">={c_n}"`, with the
same `--v2-compat` interior transform (`{c_i}to<{c_{i+1}}`). The labels are schema
strings; there is no numeric parse and no locale (categories are used verbatim).

Pairs with `nominal` (discrete — one formal attribute per bin) or `ordinal`
(progressive — cumulative thresholds; §12.3).

> **v2 `.bed` section roles differ for `n`.** For `o` (`manual_cuts`), both
> `[Attribute Categories]` and `[Category Values]` carry the numeric cut spec.
> For `n` (`ordered_cuts`), `[Attribute Categories]` carries the **ordered
> domain** and `[Category Values]` carries the **cut** (e.g. `<,Managerial,>`).
> Neither file records the discrete/progressive choice (the two `.bed`s are
> byte-identical) — it is supplied out-of-band on migration.

## 12. Scale reference

A scale maps each bin label (the discretizer's output) to zero or more
formal attributes.

### 12.1 `nominal`

One formal attribute per bin. An object's bin label maps to exactly one
formal attribute (the one matching its bin); all others get no cross.

```toml
scale = { kind = "nominal" }
```

For *N* distinct bin labels, produces *N* formal attributes.

### 12.2 `dichotomic`

One formal attribute total. The attribute crosses iff the object's bin
label equals `true_value`.

```toml
scale = { kind = "dichotomic", true_value = "Yes" }
```

**`true_value`** *(required, string)*. The bin label that maps to "true."
Other bin labels produce no cross. With `missing_policy = "as_attribute"`,
a second formal attribute is emitted for missing values.

**Naming.** By default the single formal attribute is named `{column}` alone
(no value suffix) — see §10.7. This matches v2 (`bruises?`, not
`bruises?-bruises`). A value suffix appears only if `formal_attribute_format`
is set explicitly with `{value}`.

A dichotomic scale on more than 2 distinct bin labels is valid but
unusual; consider whether `value_groups` to collapse to two labels first
would be clearer.

### 12.3 `ordinal`

Cumulative threshold formal attributes. For *N* ordered bin labels,
produces *N* formal attributes.

```toml
scale = {
  kind      = "ordinal",
  direction = "ge",                          # default; "ge" | "le"
  boundary  = "inclusive",                   # default; "inclusive" | "strict"
  order     = ["Pre-Uni", "Undergrad", "Postgrad"],  # required for non-numeric
  drop_top  = false,                         # default; suppress the tautological top
}
```

**`direction = "ge"`** (default): formal attributes mean "value is ≥
threshold." For order `["Pre-Uni", "Undergrad", "Postgrad"]`, a Postgrad
object has `≥Pre-Uni`, `≥Undergrad`, `≥Postgrad`. Reads as "level
achieved."

**`direction = "le"`**: formal attributes mean "value is ≤ threshold."
For numeric order `[20, 40, 60]`, a 25-year-old has `<40`, `<60` (or
`≤40`, `≤60` depending on `boundary`). Reads as "below threshold." This
matches v2's progressive-scaling output.

**`boundary = "inclusive"`** (default): the inequality is non-strict
(`≥` / `≤`). **`"strict"`**: strict (`>` / `<`).

**`order`** *(value-bin ordinal scales only)*. Declares the natural order of bin
labels, and applies **only** to **value-bin** discretizers (`identity` /
`free_per_value`): there it is **required** for non-numeric labels and optional for
numeric labels (the natural numeric order is used if absent). It **MUST NOT** be
present with a **cut** discretizer (`manual_cuts`, `ordered_cuts`, `equal_width`,
`equal_frequency`), whose bin order is fixed by the cut geometry (§17 rule 3) — the
cut discretizer is the single source of order. An `order` over cut bins is
`OrdinalOrderNotAllowedWithCuts` (Error, spec validate); a value-bin ordinal scale
that needs `order` but omits it is `OrdinalOrderMissing` (Error), and an `order`
entry not among the bin labels is `OrdinalOrderHasUnknownValue` (Error).

**`drop_top`** *(default `false`)*. The "top" formal attribute (the one
true for everything in `direction = "ge"` — i.e., `≥<lowest>`) is
tautological for objects with non-missing data. Set `drop_top = true` to
suppress it. The lattice's supremum is unaffected; only the explicit
formal attribute is omitted.

**Over cut bins (`manual_cuts` / `ordered_cuts`).** When the ordered bins come
from a cut discretizer, each threshold sits at a bin's far edge: for `le`, bin
*i*'s **upper** edge (so cuts 30/40/50 give `<30`, `<40`, `<50`); for `ge`, its
**lower** edge. An **open** end (§11.2 `ends = "open"`) has no finite edge there,
so its tautological threshold is labelled **`all`** rather than a value — v2's
`age-all`. This keeps "N bins → N formal attributes" exact (four open bins → four
columns). `all` is **canonical**: it is emitted on the native path too, so a
`--v2-compat` run does not change the schema (§14 — column count/identity is
style-independent); `drop_top` suppresses it. Because half-open `[lo, hi)` cut
bins can only be crossed whole, only the boundary aligned with that half-openness
is well-defined — `le` pairs with `<` (strict), `ge` with `>=` (inclusive); the
straddling combinations (`le`+inclusive, `ge`+strict) are meaningful only over
*value* bins (e.g. `identity` with an explicit `order`).

Over cut bins the cut **geometry** determines the operator. An **omitted or
defaulted** `boundary` (including one inherited from `[defaults].ordinal_boundary`,
§6) does **not** request an operator — the geometry renders it (`<` for `le`, `>=`
for `ge`), *regardless of the defaulted value* (so `[defaults].ordinal_boundary =
"strict"` does not turn a `ge` cut threshold into `>`). Only an **explicitly
authored, per-attribute** `boundary` requesting the straddling combination
(`le`+inclusive or `ge`+strict) is invalid → `OrdinalBoundaryIncompatibleWithCuts`
(Error, **spec validate**). The reader/writer preserves whether `boundary` was
authored or defaulted (§6) — both so the round-trip stays faithful and so this
check fires only on the authored case. **Over value bins** (`identity` /
`free_per_value` with an explicit `order`) there is no half-open geometry, so all
four `direction × boundary` combinations are well-defined and `boundary` is fully
live; this value-bin ordinal path is implemented at M2.

### 12.4 Modelled but not implemented in v1

The following scales parse but the v1 planner rejects them with
`ScaleNotImplementedV1`:

#### 12.4.1 `interordinal` **(deferred)**

Both ≤ and ≥ thresholds combined. For *N* ordered values, produces 2*N*
formal attributes.

#### 12.4.2 `biordinal` **(deferred)**

Split-point scaling for polar values (e.g., {strongly disagree, disagree,
agree, strongly agree}).

#### 12.4.3 `contranominal` **(deferred)**

Inequality scale (≠). Produces *N* formal attributes for *N* values.
Mostly theoretical (max formal-concept generation for benchmarking).

## 13. Composition: `extends`

```toml
[spec]
version = 1
extends = "../base/emage.toml"
```

The base spec is loaded and merged with the current spec. Merge semantics:

1. `[binding]` fields are unioned, with the current spec's values
   overriding the base's per-field.
2. `[defaults]` fields are unioned per-field, current overrides base.
3. `[[template]]` entries from both are concatenated. If two templates
   share an `id`, the current spec's wins.
4. `[[matcher]]` entries from both are concatenated. Order: base
   matchers, then current matchers (so current matchers take precedence
   per the last-match-wins rule in §9.2).
5. `[[attribute]]` entries merge by `name`, **position-preserving**: a base
   attribute keeps its original position; a derived attribute with the same
   `name` replaces it **in place** (whole-attribute replacement, no field-level
   merge — too error-prone, so inherited fields *including* `restrict_to` are
   dropped unless the override repeats them); a derived attribute with a new
   `name` is appended after all inherited attributes. To suppress an inherited
   attribute, override it with `include = false`. Attribute order is column order
   (§17 rule 1), so position-preserving override keeps a derived spec's column
   order stable when it only re-tunes inherited attributes.
6. `[output]`, `[output.cxt]`, and `[output.dat]` merge **per leaf field**
   (current overrides base field-by-field; a base `[output.cxt]` line-ending and
   a derived `[output.cxt]` trailing-newline both survive).
7. `[provenance]` from the current spec wins (provenance is per-spec,
   not inherited).

Multi-level `extends` is allowed (a chain); the merge above is applied at **each**
step, base-most first. A referenced base spec that cannot be found is
`SpecExtendsNotFound` (Fatal); cycles MUST be detected and rejected with
`SpecExtendsCycle` (Fatal).

Fingerprints are computed over the *resolved* (fully merged) plan, not the source
files, so a derived spec and an equivalent flat spec fingerprint identically. Any
fingerprint fields stored in a **base** spec's `[spec]` block are ignored when
resolving a derived spec and recomputed for the resolved result.

## 14. Fingerprints

Three fingerprints, all SHA-256, all written to the `[spec]` block of a
**fully-frozen** spec (below): one `schema_fingerprint` plus a per-format
`cxt_output_fingerprint` and `dat_output_fingerprint`.

**Canonical formal-attribute identity ≠ rendered `.cxt` name.** This distinction
underpins both fingerprints, so it is stated first:

- **Canonical identity** is the planner's stable column identity for a formal
  attribute: `(logical_attribute_name, scale_kind, canonical_bin_or_threshold_key,
  scale_operator)`. It determines `.dat` column identity and ordering, and it is
  what `schema_fingerprint` hashes. It is independent of how the attribute is
  *named* in output.
- **Rendered name** is the string that appears in a `.cxt` for that column,
  produced from the canonical identity by `formal_attribute_format`,
  `display_name`, and `value_labels`. These affect rendered names and
  `cxt_output_fingerprint` only — never canonical identity, `schema_fingerprint`,
  or `dat_output_fingerprint`.

**`schema_fingerprint`** is computed from the **final ordered list of planned
formal attributes only** — their canonical identities, in plan order, as
produced by the Plan phase (§7). Nothing else is an input. Policies such as
`missing_policy = "as_attribute"` and `unknown_value_policy = "include"` are
**not** independent hash inputs: when they change the column set, that change is
already reflected in the planned formal-attribute list (the extra
`{column}-missing` column, or the appended included values, *is* in the list).
Likewise `binding.locale`, declared-domain order, and discretizer cuts enter
only through their effect on the resolved list. The rule is exact: **two
calibrated plans that yield the same ordered list of canonical formal-attribute
identities have the same `schema_fingerprint`**, regardless of the settings that
produced them.

It does **not** depend on object-/row-affecting settings (object-key mode,
`duplicate_object_policy`, `restrict_to`, object ordering): those change which
*rows* appear, not which *columns* exist. Two specs with the same
`schema_fingerprint` produce `.dat` files with identical column identity and
IDs for the same input through the same binding.

**Per-format output fingerprints.** Rather than one `output_fingerprint`, the
spec carries a `cxt_output_fingerprint` and a `dat_output_fingerprint`, so that a
`.cxt`-only setting does not perturb the `.dat` hash and vice-versa (D-051). Both
build on `schema_fingerprint` and add the settings that change *that format's*
bytes:

- **Shared inputs** (in **both** output fingerprints — they change which objects,
  crosses, columns, and rows appear, for either format): `schema_fingerprint`;
  the row-shaping settings `schema_fingerprint` deliberately omits —
  `duplicate_object_policy` and the object-ordering policy (and `restrict_to`
  filters *once their execution is implemented*; until then `restrict_to` is not
  an input, §10.4); and the conversion-affecting binding/source settings —
  binding shape and column/predicate mappings, `encoding`, `has_header`,
  `delimiter`, `quote_char`, `missing_token`, source `value_type`s,
  `missing_policy`, `unknown_value_policy`, `binding.locale`, object-key mode, and
  discretizer/scale configuration.
- **`cxt_output_fingerprint` adds** the `.cxt`-only settings: the rendered
  formal-attribute names (`formal_attribute_format`, `display_name`,
  `value_labels`), the bin-label style (the `--v2-compat` cut-label transform) and
  `bin_label_unicode`, and the `.cxt` writer settings (line endings,
  `trailing_newline`).
- **`dat_output_fingerprint` adds** only the `.dat` writer settings: `base_index`,
  line endings, and trailing-space settings. Rendered names and `.cxt`-only
  settings never affect `.dat` (it carries numeric IDs, not names).

Two specs with the same `cxt_output_fingerprint` (resp. `dat_output_fingerprint`)
produce byte-identical `.cxt` (resp. `.dat`) for identical input. Provenance (§4)
is in none of the three fingerprints.

**Canonical hash input.** All three fingerprints hash a fixed UTF-8 **canonical
JSON structure generated from the resolved/calibrated plan** — never the spec's
TOML text (D-053). The structure carries a format-version tag (so the encoding can
evolve without silent collisions); arrays stay in planned order (column order is
significant); object/map keys are sorted; strings use one documented JSON escaping
rule; and numbers are the **parsed** numeric value reformatted with invariant,
shortest round-trippable .NET formatting, so `30`, `30.0`, and `3e1` hash
identically and a cut never renders as `34.250000001` on one machine and `34.25`
on another. Cut-bin open ends are encoded as **structural flags**, not as `∞`
strings.

**Stored only for fully-frozen specs.** Tooling writes the stored fingerprints
only when the spec is fully determined by its own text — no observed-domain
calibration (absent `declared_domain`), no auto-binning discretizer, no
`unknown_value_policy = "include"`, no `value_groups` `unmatched = "passthrough"`,
and no `restrict_to` while its execution is unimplemented. A spec needing any of
these is data-dependent (or not-yet-executable), so a stored hash would be
invalidated by the next dataset (or by the feature landing); such runs record the
**effective** fingerprints in the run manifest (§15) instead.

**Native vs effective fingerprints (CLI overrides).** Fingerprints stored in
the `[spec]` block describe the spec's **native resolved output settings only**
— what the spec produces with no CLI overrides. A CLI override such as
`--v2-compat` (§8) does not rewrite the spec or its stored fingerprints; it
changes line endings, bin labels, and `.dat` trailing space at run time. The
run manifest (§15) records the **effective** `cxt_output_fingerprint` /
`dat_output_fingerprint` after overrides, which may legitimately differ from the
spec-stored values. On load, each spec-stored fingerprint is verified against the
spec's *native* settings only: a mismatch there is a real warning
(`SchemaFingerprintStale` / `CxtOutputFingerprintStale` /
`DatOutputFingerprintStale`); a difference between the spec-stored and manifest
fingerprints under `--v2-compat` is expected, not an error. The
`schema_fingerprint` is unaffected by output-only CLI overrides.

## 15. Run manifest

When `convert` runs, a sidecar `<output>.manifest.toml` is emitted **by default**,
containing:

```toml
[run]
tool_version           = "fcabedrock-vnext 1.0.0"
timestamp              = 2026-05-09T12:34:56Z   # recorded; not a fingerprint input
command_line           = ["fcabedrock", "convert", "--spec", "foo.toml", ...]  # recorded; not a fingerprint input
spec_path              = "foo.toml"
spec_file_hash         = "sha256:..."           # raw TOML bytes of the spec file
schema_fingerprint     = "sha256:..."
cxt_output_fingerprint = "sha256:..."           # present if a .cxt was written
dat_output_fingerprint = "sha256:..."           # present if a .dat was written
input_path             = "data.csv"
input_hash             = "sha256:..."
output_path            = "ctx.dat"
output_hash            = "sha256:..."

# For an `extends` chain, every spec file in the chain is recorded:
# [[run.spec_files]]
# path = "analysis.toml"   ; hash = "sha256:..."
# [[run.spec_files]]
# path = "base/emage.toml" ; hash = "sha256:..."

[run.calibration]
# Only present if any auto-discretizer ran
"age" = { discretizer = "equal_frequency", cuts = [38.0, 49.0, 52.0] }
```

The manifest is **written by default** on every `convert`. It captures everything
needed to reproduce the conversion exactly, including any auto-calibrated cuts and
— for an `extends` chain — the path and raw hash of every spec file involved. The
output fingerprints recorded here are the **effective** ones (after any CLI
overrides such as `--v2-compat`), so they may differ from the spec-stored native
values (§14); only the format(s) actually written are recorded. `spec_file_hash`
is the raw TOML bytes, distinct from the canonical, plan-derived
`schema_fingerprint`. `timestamp` and `command_line` are recorded for the audit
trail but are not fingerprint inputs. Citing a manifest in a paper is sufficient
for reproducibility audits.

## 16. Diagnostics

### 16.1 `BedrockDiagnostic`

Every problem detected by parsing, validation, planning, calibration, or
conversion is reported as a `BedrockDiagnostic`:

```csharp
public readonly record struct BedrockDiagnostic(
    DiagnosticCode      Code,
    DiagnosticSeverity  Severity,
    string              Message,
    DiagnosticLocation? Location = null,
    object?             Context  = null);
```

### 16.2 `DiagnosticSeverity`

```csharp
public enum DiagnosticSeverity { Info, Warning, Error, Fatal }
```

- **Info**: informational (e.g., "auto-discretizer calibrated to cuts X").
- **Warning**: non-fatal issue (e.g., an empty column, a stale fingerprint).
- **Error**: fatal to the operation but recoverable for the next call
  (e.g., spec validation fails, but file remains usable).
- **Fatal**: unrecoverable; the implementation should stop processing.

### 16.3 `DiagnosticLocation`

```csharp
public readonly record struct DiagnosticLocation(
    string?  File,                // path to the spec/data file, if applicable
    int?     Line,                // 1-based for spec files
    int?     Column,              // 1-based for spec files
    string?  AttributeName,       // for attribute-scoped issues
    long?    RecordIndex);        // for data issues, 0-based
```

Any subset of fields may be populated.

### 16.4 Diagnostic codes (v1 registry)

Every distinct condition has its own `DiagnosticCode`; each code is owned by
exactly one phase — the "Where" column below is the phase-ownership contract
(decisions.md D-067). v1's initial set:

| Code | Severity | Where |
| --- | --- | --- |
| `SpecVersionUnsupported` | Fatal | spec parse |
| `SpecExtendsCycle` | Fatal | spec resolve |
| `SpecExtendsNotFound` | Fatal | spec resolve |
| `BindingShapeMissing` | Error | spec validate |
| `BindingLocaleInvalid` | Error | spec validate |
| `AttributeNameDuplicate` | Error | spec validate |
| `AttributeNameMissing` | Error | spec validate |
| `AttributeScalingMissing` | Error | spec validate |
| `DiscretizerCutsNotAscending` | Error | spec validate |
| `DiscretizerCutsTooFew` | Error | spec validate |
| `ValueLabelKeyNotInDomain` | Error | spec validate |
| `SourceValueTypeInvalid` | Error | spec validate |
| `RestrictToOnNumericRequiresRange` | Error | spec validate |
| `RestrictToValueNotInDomain` | Warning | spec validate |
| `FormalAttributeCollision` | Error | plan |
| `FormalAttributeNameCollision` | Error | plan |
| `OrdinalOrderMissing` | Error | plan |
| `OrdinalOrderHasUnknownValue` | Error | plan |
| `OrdinalOrderNotAllowedWithCuts` | Error | spec validate |
| `ScaleNotImplementedV1` | Fatal | plan |
| `ObjectKeyCompositeNotImplementedV1` | Fatal | plan |
| `ObjectKeyBindingInvalid` | Error | spec validate |
| `ObjectKeyModeInvalidForShape` | Error | spec validate |
| `ObjectKeyColumnNotImplementedV1` | Error | plan (transitional) |
| `TripleSourceNotImplementedV1` | Error | plan (transitional) |
| `DateValueTypeNotImplementedV1` | Fatal | plan |
| `ObservedDomainUsed` | Warning | calibrate |
| `CalibrationDataInsufficient` | Error | calibrate |
| `UnknownValueObserved` | Warning or Error (per `unknown_value_policy`) | calibrate/emit |
| `UnknownValuePolicyInclude` | Warning | calibrate |
| `TripleSubjectNotContiguous` | Error | emit |
| `DuplicateObjectKey` | Error, Warning, or Info (per `duplicate_object_policy`) | emit |
| `SourceValueUnparseable` | Warning or Error (per `unknown_value_policy`; `skip` silent) | calibrate/emit |
| `QuoteCharNotSupportedV1` | Error | spec validate |
| `BindingDelimiterQuoteConflict` | Error | spec validate |
| `SourceBindingInvalid` | Error | spec validate |
| `OrderedCutsCutNotInDomain` | Error | spec validate |
| `OrderedCutsNotAscending` | Error | spec validate |
| `OrderDomainInvalid` | Error | spec validate |
| `DiscretizerEndsClosedTooFewCuts` | Error | spec validate |
| `OrdinalBoundaryIncompatibleWithCuts` | Error | spec validate |
| `ValueGroupsPassthroughDataDependent` | Warning | calibrate |
| `RestrictToNotImplementedV1` | Error | plan (transitional) |
| `TemplateMatcherNotImplementedV1` | Error | plan (transitional) |
| `SchemaFingerprintStale` | Warning | spec load |
| `CxtOutputFingerprintStale` | Warning | spec load |
| `DatOutputFingerprintStale` | Warning | spec load |
| `NoFormalAttributes` | Warning | plan |
| `NoObjectsEmitted` | Warning | emit |
| `AttributeHasNoCrosses` | Warning (aggregated) | emit |
| `ObjectHasNoCrosses` | Warning (aggregated) | emit |
| `OutputCxtSizeAdvisory` | Warning | export |

`EmptyExtent` / `EmptyIntent` were dropped in favor of the unambiguous,
correctly-phased `AttributeHasNoCrosses` (an empty column, emit) and
`ObjectHasNoCrosses` (an empty row, emit); whole-context emptiness is
`NoFormalAttributes` (zero columns, plan) and `NoObjectsEmitted` (zero rows after
filtering, emit). All four still write a structurally-valid (if degenerate)
output rather than failing.

**Transitional codes.** `RestrictToNotImplementedV1`,
`TemplateMatcherNotImplementedV1`, `ObjectKeyColumnNotImplementedV1`, and
`TripleSourceNotImplementedV1` are emitted only by milestones *before* the
feature's implementation milestone (restrict_to → M4, templates/matchers → M6,
wide `column` object keys → M3, triple sources → M3, `roadmap.md`); they are
removed once the feature lands and are **not** part of the v1 end-state set. They are distinct from the permanent `*NotImplementedV1`
reservations in §20.

**Aggregation.** Data-phase diagnostics that can fire per value or per object —
`SourceValueUnparseable`, `UnknownValueObserved`, `AttributeHasNoCrosses`,
`ObjectHasNoCrosses` — are emitted **aggregated**: a per-attribute (or per-source)
count with a bounded sample, never one diagnostic per row, so a malformed column
at 73M records does not produce 73M diagnostics.

The full list is maintained in code as the `DiagnosticCode` enum.

## 17. Determinism rules

The following rules are normative and ensure same-spec + same-input ⇒
same-output across runs and across machines:

1. **Spec attribute order** = order of appearance of `[[attribute]]`
   blocks in the resolved (post-`extends`) spec.
2. **Within an attribute, formal-attribute order** = the scale's
   canonical enumeration, which is itself deterministic given the
   discretizer's output:
   - `nominal`: bin labels in the order produced by the discretizer.
   - `dichotomic`: single attribute, no order question.
   - `ordinal`:
     - over **value bins** (`identity` / `free_per_value`): ascending order of
       `scale.order`, or natural numeric order;
     - over **cut bins** (`manual_cuts` / `ordered_cuts` / `equal_width` /
       `equal_frequency`): the discretizer's bin order (rule 3) — `scale.order`
       is forbidden there (§12.3, D-060).
3. **Discretizer bin order**:
   - `manual_cuts`: ascending by cut value.
   - `ordered_cuts`: ascending by cut **position** in the declared `order`
     (the categorical analogue of `manual_cuts`; §11.8).
   - `equal_width`, `equal_frequency`: ascending by computed
     cut value.
   - `value_groups`: group declaration order in the spec; a synthetic `Other`
     bin (`unmatched = "other"`) is ordered **after** all declared groups, and
     data-discovered `passthrough` bins follow in first-observation order.
   - `identity`, `free_per_value`:
     - if `declared_domain` is a **non-empty** explicit list, bin order is
       **declaration order**;
     - if the domain is calibrated from observed values (absent
       `declared_domain`), bin order is **first-observation order** during
       calibration (deterministic given source order);
     - if `unknown_value_policy = "include"` appends newly-observed values
       during calibration, those are **appended after** the declared/observed
       values, in first-observation order.
4. **Object order in the output** = source emission order. Wide CSV: row
   order. Triple subject-grouped: order of first appearance of each
   subject. Triple unordered: post-sort order, where the sort key is the
   subject string under invariant culture.
5. **Attribute IDs in `.dat`** = `base_index`-based (default 1; §8), in the
   order from rules 1–2.
6. **Formal-attribute names in `.cxt`** = produced by
   `formal_attribute_format` from the same ordering.
7. **Locale-sensitive parsing** uses `binding.locale`, default
   `"invariant"`. Deviations from invariant locale MUST be explicit in
   the spec.
8. **Multi-valued sources** (triple input, or any source where one object
   carries several values for one attribute) accumulate crosses by union
   (§5.3.1); the set of crossed formal attributes for an object is
   order-independent, so emission order within a row follows rule 5 (ascending
   formal-attribute ID), not value-arrival order.

## 18. Output formats

### 18.1 Burmeister `.cxt`

Layout (line-by-line):

```text
B
<blank>
<n_objects>
<n_attributes>
<blank>
<object_name_1>
...
<object_name_N>
<formal_attribute_name_1>
...
<formal_attribute_name_M>
<row_1>     # M characters, 'X' for cross, '.' for no-cross
...
<row_N>
```

Line endings: `\n` by default. `\r\n` available via `[output.cxt]`
section in the spec, or `--v2-compat` CLI flag.

Trailing newline after the last matrix row: emitted by default (matches
v2 and is what most consumers expect). Set `[output.cxt]
trailing_newline = false` to suppress it.

**Write strategy.** The layout fixes the order: count, then all object names,
then all attribute names, then the incidence rows. A writer cannot simply count
in one pass and stream rows in a second naive pass, because emitting the object
names consumes the object stream that the rows also need. A conforming `.cxt`
writer therefore uses **either** a replayable source plus a bounded object-name
buffer (replay the source for the incidence rows), **or** a temporary
incidence-row spool written during the name pass and copied out after the
header. It MUST NOT materialize the full incidence matrix in memory: holding
object names and counts (bounded metadata) is allowed; holding all
crosses/cells in Core is not. (`.dat`, by contrast, needs no header count and
streams in a single pass.)

### 18.2 FIMI `.dat`

One line per object, listing the IDs of the formal attributes that object
crosses, separated by single spaces. vNext's native output has **no** trailing
space after the last id:

```text
1 2 4 6
1 3 4 8
3 4 6
```

Under `--v2-compat` (or `[output.dat] nonempty_line_trailing_space = true`),
each non-empty line gains a trailing space before the line ending, matching v2:

```text
1 2 4 6 
1 3 4 8 
3 4 6 
```

**Index base** is 1 by default (matching v2 and the FIMI format
specification). Set `[output.dat] base_index = 0` for 0-based output
(used by some ML/pandas-adjacent toolchains).

**Empty objects** (no crosses) produce an empty line. Trailing space on
empty lines defaults to false (no spurious whitespace); set
`[output.dat] empty_line_trailing_space = true` if a consumer requires it.

**Line endings**: `\n` by default. `\r\n` available via `[output.dat]
line_endings = "crlf"` or `--v2-compat`.

## 19. Worked examples

### 19.1 mini-mushroom (v2 compat)

Original v2 `.bed` translates to:

```toml
[spec]
version = 1
description = "v2 mini-mushroom spec, modernized"

[binding]
shape = "wide"
has_header = true
missing_token = "?"

[binding.object_key]
mode = "row_index"

[[attribute]]
name = "class"
source = { kind = "column", index = 0 }
include = false                              # was [Convert Attribute] = False
# excluded → emits nothing; discretizer/scale/etc. are optional here and ignored
# while off (D-049, an authoring toggle). class is dropped from the analysis.

[[attribute]]
name = "bruises?"
source = { kind = "column", index = 1 }
discretizer = { kind = "identity" }
scale = { kind = "dichotomic", true_value = "t" }
declared_domain = ["t", "f"]
# Formal attribute: "bruises?" (column name only — dichotomic default omits
# the value suffix, §10.7). No value_labels needed: the name carries no value.

[[attribute]]
name = "gill-size"
source = { kind = "column", index = 2 }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
declared_domain = ["b", "n"]
value_labels    = { b = "broad", n = "narrow" }
# Formal attributes: gill-size-broad, gill-size-narrow

[[attribute]]
name = "veil-type"
source = { kind = "column", index = 3 }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
declared_domain = ["p", "u"]
value_labels    = { p = "partial", u = "universal" }
# Formal attributes: veil-type-partial, veil-type-universal

[[attribute]]
name = "ring-number"
source = { kind = "column", index = 4 }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
declared_domain = ["n", "o", "t"]
value_labels    = { n = "none", o = "one", t = "two" }
# Formal attributes: ring-number-none, ring-number-one, ring-number-two
```

Produces 8 formal attributes matching v2's mini-mushroom.cxt byte-for-byte
(when run with `--v2-compat` for line endings, or with the appropriate
`[output]` settings).

### 19.2 mini-adult (v2 compat)

```toml
[spec]
version = 1

[binding]
shape = "wide"
has_header = false
missing_token = "?"

[binding.object_key]
mode = "row_index"

[[attribute]]
name = "age"
source = { kind = "column", index = 0 }
discretizer = { kind = "manual_cuts", cuts = [30, 40, 50], ends = "open" }
scale = { kind = "nominal" }                 # v2's "discrete" continuous mode

[[attribute]]
name = "education"
source = { kind = "column", index = 1 }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
declared_domain = ["Bachelors", "Masters", "11th", "HS-grad"]

[[attribute]]
name = "employment"
source = { kind = "column", index = 2 }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
declared_domain = ["Clerical", "Managerial", "Professional", "Unskilled"]

[[attribute]]
name = "sex"
source = { kind = "column", index = 3 }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
declared_domain = ["Male", "Female"]

[[attribute]]
name = "US-citizen"
source = { kind = "column", index = 4 }
discretizer = { kind = "identity" }
scale = { kind = "dichotomic", true_value = "Yes" }
declared_domain = ["Yes", "No"]

[[attribute]]
name = "class"
source = { kind = "column", index = 5 }
include = false
```

**Employment as an ordered cut (v2 `n`).** The `mini-adult_employment_ordinal_*`
fixtures re-type `employment` as `n`: an `ordered_cuts` discretizer (§11.8) over
the ordered domain, cut at `Managerial`. The `discrete` and `progressive` files
share one `.bed` (byte-identical); the mode is supplied out-of-band on migration.

```toml
# discrete (nominal):  employment-<Managerial, employment->=Managerial
discretizer = { kind = "ordered_cuts",
                order = ["Unskilled", "Clerical", "Professional", "Managerial"],
                cuts  = ["Managerial"], ends = "open" }
scale = { kind = "nominal" }

# progressive (ordinal, le):  employment-<Managerial, employment-all
scale = { kind = "ordinal", direction = "le" }
```

### 19.3 mini-adult_triples_named (triple input, named subjects)

```toml
[spec]
version = 1

[binding]
shape = "triple"
ordering = "subject_grouped"
columns = { subject = 0, predicate = 1, value = 2 }
missing_token = "?"

# binding.object_key defaults to mode = "column", column = subject column

[[attribute]]
name = "age"
source = { kind = "predicate", name = "age" }
discretizer = { kind = "manual_cuts", cuts = [30, 40, 50], ends = "open" }
scale = { kind = "nominal" }

# ... (other attributes analogous to §19.2 with kind = "predicate")
```

### 19.4 EMAGE-style with ordinal grouping and restriction

Hypothetical analysis of mouse gene expression: which tissues express
Bmp5 strongly across early Theiler stages.

```toml
[spec]
version = 1
description = "Bmp5 strong expression in endoderm/mesoderm, early TS"

[binding]
shape = "triple"
ordering = "unordered"
columns = { subject = 0, predicate = 1, value = 2 }

[[attribute]]
name = "Gene"
source = { kind = "predicate", name = "Gene" }
include = false                              # filter-only: keep only Bmp5 objects,
restrict_to = ["Bmp5"]                       # but do not emit a Gene column

[[attribute]]
name = "Tissue"
source = { kind = "predicate", name = "Tissue" }
discretizer = { kind = "value_groups",
  groups = [
    { label = "Endoderm",  values = ["endoderm", "primitive endoderm"] },
    { label = "Mesoderm",  values = ["mesoderm", "extra-embryonic mesoderm"] },
  ],
  unmatched = "skip"                         # other tissues drop out of analysis
}
scale = { kind = "nominal" }

[[attribute]]
name = "Strength"
source = { kind = "predicate", name = "Strength" }
include = false                              # filter-only: keep only strong
restrict_to = ["strongly detected"]          # observations; do not emit a Strength column

[[attribute]]
name = "TheilerStage"
source = { kind = "predicate", name = "TheilerStage" }
discretizer = { kind = "equal_frequency", bins = 4 }
scale = { kind = "ordinal", direction = "ge" }
restrict_to = [{ from = 3, to = 9 }]         # TS 3-8
```

This single spec captures: keep only objects observed for Bmp5 and only their
strongly-detected observations within Theiler stages 3–8, then analyze the
surviving objects along two emitted dimensions — Tissue (grouped into
Endoderm/Mesoderm) and TheilerStage (four ordinal buckets). `Gene` and `Strength`
are the two **filter-only** attributes (`include = false` + `restrict_to`): they
shape *which objects* enter the context without becoming *columns* in it.
`TheilerStage` is **emitted and restricted** — it is not filter-only. Note its
`equal_frequency` cuts calibrate over the **input universe** before `restrict_to`
filters objects (§7), so the surviving TS 3–8 objects need not span all four
buckets and some columns may end up empty.

> This example illustrates the v1 **end-state**. `restrict_to` execution (and the
> `equal_frequency` calibration shown here) land at later milestones (M4); under
> M2 a spec like this round-trips but is rejected at conversion with
> `RestrictToNotImplementedV1` (§10.4).

## 20. Modelled-but-not-implemented appendix (v1)

Reserved in the spec format; v1 planner rejects with the listed
diagnostic code. Re-listed here for visibility.

| Feature | Diagnostic | Notes |
| --- | --- | --- |
| Scale `interordinal` | `ScaleNotImplementedV1` | §12.4.1 |
| Scale `biordinal` | `ScaleNotImplementedV1` | §12.4.2 |
| Scale `contranominal` | `ScaleNotImplementedV1` | §12.4.3 |
| Object key `composite` | `ObjectKeyCompositeNotImplementedV1` | §5.4 |
| Date value type (`value_type = "date"`) + date scaling | `DateValueTypeNotImplementedV1` | §11.7 |

**Not modelled in v1 (no reserved carrier).** Cross-attribute restrict ("include
attr A only when attr B = X") has **no reserved syntax** — unlike the rows above, no
v1 spec can express it, so there is no rejection diagnostic. It is prose-only future
work (D-062).

## 21. Decisions log

Settled questions from the design conversation, recorded so future readers
don't re-litigate. Items 1–11 record the **spec-field defaults** in full —
this section is their home (`docs/decisions.md` cross-references them here).
Items 12–25 are one-line pointers to the owning spec sections and
`docs/decisions.md` entries; item numbers are stable (they are referenced by
number, e.g. "§21-item-16" in D-051).

1. **Bin label style** → math notation (`<30`, `[30, 40)`, `≥50` or
   `>=50` depending on §8 `bin_label_unicode`). v2 style available via
   writer-level `--v2-compat` flag for M1 byte-equality testing.

2. **`drop_top` default** → `false` (keep the tautological top). FCA
   classical convention. Override per-attribute when clutter outweighs
   correctness.

3. **`unknown_value_policy` default** → `"warn"`. Data-quality-conscious
   choice; conversion still completes. v2's silent-skip is `"skip"`.

4. **FIMI `.dat` trailing space** → both non-empty and empty lines default to
   no trailing space (clean modern output). Configurable via `[output.dat]
   nonempty_line_trailing_space` and `empty_line_trailing_space`. All v2-isms
   (CRLF, `30to<40` labels, trailing space) live behind `--v2-compat` so the
   native default is uniformly clean.

5. **`.cxt` size advisory threshold** → 1 GB. Configurable via
   `[output.cxt] size_advisory_bytes`. Subject to revision once we
   benchmark consumer tooling.

6. **ASCII vs Unicode operators** → ASCII default (`>=`, `<=`) for
   ConExp compat. Unicode via `[output] bin_label_unicode = true`.

7. **Literal value strings that look like operators** (e.g., `>50K`,
   `<=50K` in mini-adult's class column) → treated as opaque string
   values inside `declared_domain` and `restrict_to`. No parser
   ambiguity in TOML since the structure makes it unambiguous (unlike
   v2's parallel-array `.bed` where context was implicit). Sanity-tested
   in M1.

8. **v2 byte-equality** → not a spec setting. CLI flag `--v2-compat` on
   `convert` overrides `[output]` to v2 conventions (CRLF, `30to<40`
   bin labels, trailing space on `.dat` lines, etc.). Keeps the spec
   format clean; v2-compat is a migration concern, not a property of
   the spec.

9. **`std_dev` discretizer** → removed from v1 entirely (not deferred).
   If a real need surfaces later it can be added as a new discretizer
   kind without breaking compatibility.

10. **`value_labels` field** → added as §10.8 to capture v2's
    `[Attribute Categories]` (display) ↔ `[Category Values]` (raw)
    distinction. Required for M1 byte-equality on mini-mushroom output.

11. **Ordinal "pre-school to undergrad" use case** → expressible via
    `value_groups` discretizer with nominal scale (Interpretation A from
    the design conversation). Bin labels can be free-form strings naming
    the range. Overlapping ranges sharing a boundary (Interpretation C,
    e.g., interordinal/biordinal) remain deferred.

12. **Triple multi-value union; scale decides folding** → §5.3.1;
    decisions.md D-030/D-031.

13. **Filter-only attributes** → §10.1, §10.4, §10.9; D-032/D-049. Used by the
    EMAGE example (§19.4).

14. **`source` may repeat across attributes** → §10.2; D-033.

15. **Duplicate object keys defined by key mode; `fail` default** → §5.4, §6.1;
    D-034.

16. **Fingerprint scope** → §14; D-035/D-051/D-053.

17. **Processing phases** → §7; D-036 (with D-003/D-005/D-028).

18. **Discretizer/scale required only when emitting** → §10.9; D-037(a)/D-049.

19. **Scale-specific default naming** → §10.7; D-037(a).

20. **Locale governs numeric parsing; not an independent fingerprint input** →
    §5.1, §11.5, §14; D-035/D-036.

21. **Date support deferred** → §10.2, §11.7; D-038 (v2 type-code map in
    lineage.md).

22. **Native vs effective fingerprints (CLI overrides)** → §14, §15.

23. **Parse/validate may read source schema, not rows** → §7.

24. **M2 contract additions** → D-050…D-058 (decisions.md); the owning sections
    (§5.1, §10.4, §11.5, §11.6, §13, §14, §16.4) are updated in place.

25. **Tier 1 spec-audit additions** → D-060…D-065 (decisions.md); the owning
    sections (§5.4, §6, §7, §10.2, §10.4, §12.3, §17, §20) are updated in place.

---

*End of v1 spec schema document. Section count: 21. Sections marked
with diagnostic code `*NotImplementedV1` are reserved syntax; v1 planner
rejects but the parser accepts. The Decisions Log (§21) is informative,
not normative.*
