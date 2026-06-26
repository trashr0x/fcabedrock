# FcaBedrock vNext — Bedrock Spec Schema (v1)

**Status:** Draft for review (consolidation of design decisions made over
multiple design sessions). Intended to be the canonical reference once
agreed; subsequent changes go through the same review.

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

Order within the file is informative. The fingerprint is computed over a
canonical projection (sorted keys, normalized whitespace) so that file
formatting does not affect schema identity.

A spec MUST declare `version` in `[spec]`. Implementations encountering
an unknown `version` MUST refuse to load the spec and emit
`SpecVersionUnsupported`.

## 3. The `[spec]` block

```toml
[spec]
version = 1                                 # required, integer
schema_fingerprint = "sha256:abc123..."     # optional; written by tooling
output_fingerprint = "sha256:def456..."     # optional; written by tooling
extends = "base.toml"                       # optional; spec composition
description = "Mini-mushroom analysis"      # optional; free text
```

**`version`** *(required, integer)*. Currently `1`. Bump on incompatible
schema changes. Implementations MUST refuse unknown versions.

**`schema_fingerprint`** *(optional, string)*. Deterministic hash of the
formal-attribute schema this spec produces (their ordered list with full
identifying info). Two specs with the same `schema_fingerprint` produce
identical attribute IDs. Controls `.dat` compatibility.

**`output_fingerprint`** *(optional, string)*. Deterministic hash of
everything that affects the byte-level output, including formal-attribute
display names. Controls `.cxt` byte equality.

Both fingerprints SHOULD be written by tooling on save, verified on load,
and emitted as warnings if mismatched. Both are SHA-256 over a canonical
TOML projection.

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
missing_token = "?"                          # default "?"; any non-empty string
```

**`shape`** *(required, enum)*. `"wide"` for one-row-per-object DSV,
`"triple"` for subject-predicate-value DSV.

**`encoding`** *(default `"utf-8"`)*. Any encoding accepted by the
implementation. Implementations MUST support at least UTF-8.

**`delimiter`** *(default `","`)*. Any single character. Common
alternatives: `"\t"`, `";"`, `"|"`.

**`quote_char`** *(default `"\""`)*. RFC 4180-style quoting; `""` inside
a quoted field is an escaped quote.

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
construction; `duplicate_object_policy` does not apply.

**`mode = "column"`**:

```toml
[binding.object_key]
mode = "column"
column = "id"                                # name (with header) or index
```

Object name is taken from the named/indexed column. The column is excluded
from the conversion (no formal attributes generated from it). Duplicate key
values are governed by `duplicate_object_policy` (§6.1).

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
   *static* spec validity (overlapping cuts, scale/discretizer compatibility,
   `value_labels` keys in domain when live (§10.8), duplicate `name`s,
   formal-attribute identity collisions). Reads no data
   *rows*. It MAY inspect source *schema metadata* supplied by the caller —
   header names, column count — to validate source bindings (e.g. a
   `{ kind = "column", name = "age" }` binding against an actual header);
   binding by column *index* needs no schema at all. It does not scan object
   records or values. Produces a validated spec or aggregated diagnostics.
2. **Calibrate** — the only phase that reads data to resolve *data-dependent
   schema elements*: absent `declared_domain`s (observed-domain discovery),
   auto-discretizer cuts (`equal_width`, `equal_frequency`), and
   `unknown_value_policy = "include"` extensions. Produces a fully-resolved
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
discretizers, and no `unknown_value_policy = "include"` is fully determined by
its own text: Parse → Plan → Emit, deterministic from the spec alone, no data
pre-pass that affects the schema.

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

## 8. The `[output]` block

Output-formatting options. All optional with sensible defaults. All fields
contribute to `output_fingerprint`; none to `schema_fingerprint`.

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

A source may declare a **value type** controlling how raw values parse before
discretization:

```toml
source = { kind = "column", index = 0, value_type = "number" }            # numeric parse
source = { kind = "column", index = 1, value_type = "string" }            # no parse (default for identity/value_groups)
```

`value_type` is `"string"` (default for `identity` / `value_groups`) or
`"number"` (default for the numeric discretizers `manual_cuts`, `equal_width`,
`equal_frequency`). The value `"date"` is **reserved but not implemented in
v1** (§11.7): a spec setting `value_type = "date"` parses but is rejected by the
v1 planner with `DateValueTypeNotImplementedV1`. A `value_type` that is not one
of these, or is applied incompatibly with the discretizer, is
`SourceValueTypeInvalid` (Error).

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
for discretizers that operate on raw values (`identity`, `free_per_value`,
and `value_groups`); ignored for cut-based discretizers (`manual_cuts`,
`equal_width`, etc.).

When `declared_domain` is explicit, its **declaration order drives the
formal-attribute (column) order** for `identity` and `free_per_value` scaled
nominally (§17 rule 3) — this is what makes a spec-first run reproducible and
v2-byte-compatible regardless of the order values happen to appear in the data.

If `declared_domain` is empty or absent, the Calibrate phase (§7) fills it
from the observed domain in the data, and the user is warned
(`ObservedDomainUsed`) because the resulting schema then depends on this
specific input. For input-independent, spec-first workflows, declare the
domain explicitly or freeze it with `fcabedrock calibrate`.

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

Mixed forms (string and range) within the same `restrict_to` list are
allowed — useful when the discretizer is `value_groups` operating on a
mix of categorical and numeric raw values.

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
when present, missing crosses when absent).

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

This setting affects `output_fingerprint` but not `schema_fingerprint`.

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
`output_fingerprint` but not `schema_fingerprint`.

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
overall range produce no bin (treated as out-of-range).

Out-of-range objects are kept (no cross emitted for the discretized
attribute, similar to missing). To exclude them entirely, use
`restrict_to`.

Bin label format: `"<{c0}"`, `"[{c_i}, {c_{i+1}})"`, `">={c_n}"` (ASCII
operators by default; see §8 for the `bin_label_unicode` knob). The
exact format affects `output_fingerprint` but not `schema_fingerprint`.
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

Different from `identity` only in that it can be safely paired with
auto-bin scales (the scale knows the values came from numeric data).

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
  parses identically everywhere. A value that fails to parse under that locale,
  or parses to NaN or ±∞, is treated as **missing** for the attribute (per
  `missing_policy`) and is **excluded** from calibration — it never influences
  a cut.
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
  as the bin label (mixed grouped and ungrouped attributes).

A value matching multiple groups falls into the first matching group in
declaration order; this is part of the planner's deterministic resolution
and IS captured in the schema fingerprint.

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
in `order` produces **no bin** (subject to `unknown_value_policy`).

**`cuts`** *(required, length ≥ 1)*. Each is a member of `order`, strictly
ascending by position. A value equal to a cut falls into the bin **at or above**
it — the same half-open rule as `manual_cuts` (the cut is the lower edge of the
upper bin).

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

**`order`** *(required for non-numeric bin labels; optional for numeric)*.
Declares the natural order of bin labels. For numeric labels (output of
`manual_cuts`, `equal_width`, `equal_frequency`), the natural numeric
order is used if `order` is absent.

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
5. `[[attribute]]` entries are concatenated. If two attributes share a
   `name`, the current spec's wins entirely (no field-level merge —
   too error-prone).
6. `[provenance]` from the current spec wins (provenance is per-spec,
   not inherited).

Multi-level `extends` is allowed (a chain) but cycles MUST be detected
and rejected with `SpecExtendsCycle`.

The schema fingerprint is computed over the *resolved* (fully merged)
spec, not over the source files. So a derived spec and an equivalent
flat spec produce identical fingerprints.

## 14. Fingerprints

Two fingerprints, both SHA-256, both written to the `[spec]` block.

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
  `output_fingerprint` only — never canonical identity or `schema_fingerprint`.

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

**`output_fingerprint`** is computed from the **effective conversion plan plus
the effective output settings** — i.e. everything that can change the output
bytes for identical input, not just the formatting layer. Concretely it covers:
the `schema_fingerprint`; binding shape and column/predicate mappings;
`delimiter`, `quote_char`, and `missing_token`; source `value_type`s;
`missing_policy`; `unknown_value_policy`; object-key mode and
`duplicate_object_policy`; all `restrict_to` filters; discretizer and scale
configuration; `binding.locale`; object-ordering policy; the rendered
formal-attribute names (`formal_attribute_format`, `display_name`,
`value_labels`); bin-label style and `bin_label_unicode`; and writer settings —
`.dat` `base_index`, line endings (`.cxt` and `.dat`), trailing-space settings,
and `[output.cxt] trailing_newline`. Two specs with the same `output_fingerprint`
produce byte-identical output for identical input. Provenance (§4) is in neither
fingerprint.

**Native vs effective fingerprints (CLI overrides).** Fingerprints stored in
the `[spec]` block describe the spec's **native resolved output settings only**
— what the spec produces with no CLI overrides. A CLI override such as
`--v2-compat` (§8) does not rewrite the spec or its stored fingerprints; it
changes line endings, bin labels, and `.dat` trailing space at run time. The
run manifest (§15) records the **effective** `output_fingerprint` after
overrides, which may legitimately differ from the spec-stored value. On load,
the spec-stored fingerprint is verified against the spec's *native* settings
only: a mismatch there is a real warning; a difference between the spec-stored
and manifest fingerprints under `--v2-compat` is expected, not an error. The
`schema_fingerprint` is unaffected by output-only CLI overrides.

## 15. Run manifest

When `convert` runs, an optional sidecar `<output>.manifest.toml` is
emitted containing:

```toml
[run]
tool_version       = "fcabedrock-vnext 1.0.0"
timestamp          = 2026-05-09T12:34:56Z
command_line       = ["fcabedrock", "convert", "--spec", "foo.toml", ...]
spec_path          = "foo.toml"
spec_fingerprint   = "sha256:..."
output_fingerprint = "sha256:..."
input_path         = "data.csv"
input_hash         = "sha256:..."
output_path        = "ctx.dat"
output_hash        = "sha256:..."

[run.calibration]
# Only present if any auto-discretizer ran
"age" = { discretizer = "equal_frequency", cuts = [38.0, 49.0, 52.0] }
```

The manifest captures everything needed to reproduce the conversion
exactly, including any auto-calibrated cuts. The `output_fingerprint` recorded
here is the **effective** one — after any CLI overrides such as `--v2-compat`
— so it may differ from the spec-stored native fingerprint (§14). Citing a
manifest in a paper is sufficient for reproducibility audits.

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
- **Warning**: non-fatal issue (e.g., empty extent, deprecated field).
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

### 16.4 Initial diagnostic codes (illustrative)

Every distinct condition has its own `DiagnosticCode`. v1's initial set:

| Code | Severity | Where |
|---|---|---|
| `SpecVersionUnsupported` | Fatal | spec parse |
| `SpecExtendsCycle` | Fatal | spec resolve |
| `SpecExtendsNotFound` | Fatal | spec resolve |
| `BindingShapeMissing` | Error | spec validate |
| `AttributeNameDuplicate` | Error | spec validate |
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
| `ScaleNotImplementedV1` | Fatal | plan |
| `ObjectKeyCompositeNotImplementedV1` | Fatal | plan |
| `DateValueTypeNotImplementedV1` | Fatal | plan |
| `ObservedDomainUsed` | Warning | calibrate |
| `CalibrationDataInsufficient` | Error | calibrate |
| `UnknownValueObserved` | Warning or Error (per `unknown_value_policy`) | calibrate/emit |
| `UnknownValuePolicyInclude` | Warning | calibrate |
| `TripleSubjectNotContiguous` | Error | emit |
| `DuplicateObjectKey` | Error, Warning, or Info (per `duplicate_object_policy`) | emit |
| `EmptyExtent` | Warning | plan |
| `EmptyIntent` | Warning | emit |
| `OutputCxtSizeAdvisory` | Warning | export |

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
   - `ordinal`: ascending order of `order` (or natural numeric order).
3. **Discretizer bin order**:
   - `manual_cuts`: ascending by cut value.
   - `equal_width`, `equal_frequency`: ascending by computed
     cut value.
   - `value_groups`: declaration order in the spec.
   - `identity`, `free_per_value`:
     - if `declared_domain` is explicit, bin order is **declaration order**;
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
strongly-detected observations within Theiler stages 3–8 (three filter-only
attributes), then analyze the surviving objects along two emitted dimensions —
Tissue (grouped into Endoderm/Mesoderm) and TheilerStage (four ordinal
buckets). `Gene` and `Strength` are filter-only (`include = false`): they shape
*which objects* enter the context without becoming *columns* in it.

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
| Cross-attribute restrict (e.g. "include attr A only when attr B = X") | `RestrictCrossAttributeNotImplementedV1` | future enhancement |

## 21. Decisions log

Settled questions from the design conversation, recorded here so future
readers know the rationale and don't re-litigate.

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

12. **Triple multi-value semantics** (§5.3.1) → rows sharing an object key
    accumulate crosses by union; duplicate triples are idempotent; this is
    normal input, not a duplicate-object condition. Whether multiple values
    fold into one formal attribute is a property of the chosen scale, not a
    source toggle (one standard way). `subject_grouped` requires contiguous
    subjects (`TripleSubjectNotContiguous` Error otherwise); `unordered`
    imposes no contiguity.

13. **Filter-only attributes** (§10.1, §10.4) → `include = false` suppresses
    formal-attribute emission but **not** the attribute's own `restrict_to`; any
    emitted-shaping config it retains is ignored, not rejected (the authoring
    toggle, §10.9 / D-049). This makes object-filtering-by-a-field consistent
    across wide and triple input (v2 only did this consistently for wide). The
    EMAGE example (§19.4) uses it for `Gene` and `Strength`.

14. **`source` may repeat** (§10.2) → two attributes may share one `source` to
    apply multiple scalings (e.g. nominal bins + ordinal thresholds on `age`).
    `name` must still be unique; identical resulting formal-attribute identity
    is rejected (`FormalAttributeCollision`). Replaces the earlier blanket ban.

15. **Duplicate object keys** (§6.1) → defined by object-key mode. `row_index`
    and triple input: not applicable. Wide `column` mode: `duplicate_object_policy`
    defaults to **`fail`** (a duplicate in a column you chose as the identifier
    is probably a data error); `keep` (suffix + warn) and `dedupe` (union) opt
    in. The stray `"merge"` value and the `duplicate_policy` misnomer are
    removed; cross-row merge by derived key is the deferred `composite` feature.

16. **Fingerprint scope** (§14) → `schema_fingerprint` = formal-attribute schema
    only (column identity), excluding `duplicate_object_policy`, `restrict_to`,
    object-key mode, and ordering. `output_fingerprint` = everything affecting
    byte output, including those object/row settings plus naming and formatting.
    Provenance in neither.

17. **Processing phases** (§7) → Parse/validate → Calibrate → Plan → Emit.
    `convert` auto-calibrates by default (cuts captured in the manifest) but
    never *discovers*; absent `declared_domain`s are calibrated with an
    `ObservedDomainUsed` warning. Auto-discretizer determinism rules
    (parse/sort/insufficient-data/NaN) are fixed now (§11.5); exact quantile
    formula and label precision are settled at M4.

18. **Discretizer/scale required only when emitting** (§10.9) → required when
    `include = true`; optional when `include = false`. Emitted-shaping config on
    an excluded attribute is retained but ignored, not an error — `include` is an
    authoring toggle (D-049). Makes the filter-only examples valid.

19. **Scale-specific default naming** (§10.7) → nominal `{column}-{value}`;
    ordinal `{column}-{scale_op}{value}`; dichotomic `{column}` alone (no value
    suffix, matching v2 `bruises?`); `as_attribute` adds `{column}-missing`. An
    explicit `formal_attribute_format` overrides the scale default entirely.
    Required for M1 byte-equality.

20. **Locale governs numeric parsing; not an independent fingerprint input**
    (§5.1, §11.5, §14) → numeric parsing uses `binding.locale` (default
    `invariant`); determinism is from the declared locale, not hardcoded
    invariant. Locale is **not** a separate `schema_fingerprint` input: its
    effect is already captured in the resolved cuts/labels/columns, so two
    plans with the same formal-attribute identity fingerprint identically.
    Locale's observable effect lives in `output_fingerprint` and the manifest.

21. **Date support deferred** (§10.2, §11.7) → `value_type = "date"` is reserved
    but not implemented in v1; the planner rejects it with
    `DateValueTypeNotImplementedV1`. Continuous *numeric* support is the v1
    priority; full date support (cut syntax, `DateOnly`/`DateTime` semantics,
    day-space binning, label rounding, date diagnostics) is not worth
    front-loading. v2 had a distinct `d` type, so this is a conscious parity
    deferral, not an oversight. v2's `n` (Ordinal) type maps to our `ordinal`
    scale over an ordered categorical discretizer — already covered (see
    lineage.md). `mini-dates` is a deferred fixture, not an M1 target.

22. **Native vs effective fingerprints** (§14, §15) → spec-stored fingerprints
    describe native resolved output (no CLI overrides). `--v2-compat` changes
    effective output without rewriting the spec; the manifest records the
    effective `output_fingerprint`. A spec↔manifest difference under
    `--v2-compat` is expected, not an error.

23. **Parse/validate may read source schema, not rows** (§7) → it MAY inspect
    header names / column count to validate name-based bindings; it scans no
    object records or values.

---

*End of v1 spec schema document. Section count: 21. Sections marked
with diagnostic code `*NotImplementedV1` are reserved syntax; v1 planner
rejects but the parser accepts. The Decisions Log (§21) is informative,
not normative.*
