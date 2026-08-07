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

The canonical writer (the one tool that serializes a `SpecDocument`, §14) is
deterministic. It renders a long top-level
`declared_domain` array **multiline, one escaped value per line**, rather than as a single
unbounded line, so that large machine-generated domains — for example a `probe` draft's
`declared_domain` (§7.1) — stay readable and diff-friendly (decisions.md D-113). The wrapping cutoff is a
private, byte-pinned formatting constant — never a spec field, a setting, or a fingerprint
input — and wrapping never changes document semantics or any fingerprint.

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
specs only** (§14) — a spec whose schema is data-dependent (an **omitted**
`declared_domain` where a discretizer consumes it (`identity` / `free_per_value`) —
an **authored** domain, including an explicit empty `[]`, is complete and does
**not** disqualify (D-122) —
a **data-calibrated discretizer configuration**
(`equal_frequency`, or `equal_width` with a data-derived `range`; `equal_width`
`range = "manual"` is spec-determined and does **not** disqualify),
`unknown_value_policy = "include"`, or `value_groups`
`unmatched = "passthrough"`) omits the stored fingerprints rather than
storing a value the next
dataset would invalidate. `restrict_to` does **not** disqualify a spec — its entries
are authored text, and calibration precedes filtering (§7/§14). When present they are verified on load and emitted as
warnings if mismatched (`SchemaFingerprintStale`, `CxtOutputFingerprintStale`,
`DatOutputFingerprintStale`). All are SHA-256 over the plan-derived canonical
structure described in §14.

The M7 commands own **writing** these fields: `fcabedrock calibrate` writes them on
freeze, and `fcabedrock fingerprint --write` refreshes a fully frozen copy (§14,
D-122). On an `extends` chain the stored fields belong to the derived/root file
(§13 rule 8) — `calibrate` writes a flattened standalone spec, while
`fingerprint --write` **preserves the root's `extends`** and changes only these
stored fields semantically.

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

**`has_header`** *(default is **shape-specific**: `true` for `shape = "wide"`,
`false` for `shape = "triple"`)*. If true, the first non-empty record is consumed
as a header and is available for binding columns/roles by name; if false, every
record is data. Triple data is typically headerless, hence the `false` default
there — a `true` default would silently consume the first triple as a header. The
resolved behaviour is identical across shapes; there is no header heuristic
(guessing belongs to `probe`, not `convert` — and even there only to *future* probe
tooling: the M5 `probe` base performs **no** structural or type inference, so the
caller selects the shape and header handling, §7.1). Triple role binding by header **name**
(§5.3) requires `has_header = true`.

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
`true_value` / `value_groups`, numeric parsing, and — for triple input — deriving
the object name from the **subject** and matching the **predicate** selector.
Whitespace inside a **quoted** field is preserved (deliberate spaces survive). The spec-side strings you write in
the TOML are taken **verbatim** and never trimmed; only the data-side field value
is. The rule is uniform across all matching, so a value never fails to match
purely because of surrounding spaces in the source file.

**Numeric spec-side entries are the one exception to "verbatim".** A **numeric**
entry written in the spec — a numeric `restrict_to` value (§10.4), or a numeric
`free_per_value` `declared_domain` (§10.3), `value_labels` key (§10.8), or
`scale.order` entry (§12.3) — is **parsed** under `binding.locale` to its numeric
identity rather than compared as an opaque string, so `90`, `90.0`, and `9e1`
denote the same value and all zero spellings canonicalize to `0` (D-096). This is
a value-identity rule, not a whitespace one; surrounding whitespace remains
insignificant.

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

**`columns`** *(optional for triple, table; omitted ⇒ `{ subject = 0,
predicate = 1, value = 2 }`)*. Maps the three logical roles to source columns,
using **one addressing mode**: omit it entirely, give all three roles as 0-based
**indices**, or give all three as header **names** (requires `has_header = true`,
§5.1). Mixing indices and names, or a partial role table, is `SourceBindingInvalid`
(§10.2). The three roles MUST resolve to three **distinct** physical columns
(`TripleColumnsNotDistinct` otherwise). Header text used to resolve a role does not
become an output name (object and attribute names come from the subject values and
the attribute `name`); extra physical columns beyond the three roles are ignored.

Attributes under triple binding use
`{ kind = "predicate", name = "..." }` to bind by predicate string.

> **Triple-source surface finalized at M3 (D-082).** Header rows for triple input
> and binding `columns` by header **name** are settled above — `has_header` is
> shape-specific (§5.1), role binding may be by index or name. Object/subject
> identity is always the resolved **subject** (§5.4): there is no separate
> subject-name filter, and an authored `[binding.object_key]` under triple is
> rejected (`ObjectKeyModeInvalidForShape`). Object filtering is the ordinary
> `restrict_to` (§10.4).

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

**Calibration population (triple).** When an attribute's discretizer calibrates
(§7), each **distinct cleaned `(subject, predicate, value)` observation
contributes exactly once** to that attribute's calibration population — the same
idempotence that makes a repeated triple set the same cross (above) makes it count
once toward an auto-discretizer's cuts or an observed domain. This specializes the
general calibration-population rule (§7); it is the triple analogue of a wide row
being one independent observation (see also §11.5).

**Absent predicate vs missing value.** Triple input has no cell-per-column
guarantee: a subject may carry **no** row for a given predicate. An **absent**
predicate is **no observation** — it produces no cross and never crosses a
`missing_policy = "as_attribute"` `-missing` column (§10.5). A value is **missing**
(and then follows `missing_policy`) only when a **matching-predicate row exists**
and its value is empty, equals `missing_token`, or is absent because the row is too
short to reach the value column. An unknown or empty **predicate** keeps the subject
as an object but sets no crosses. Predicate matching is exact and ordinal (P-12);
because predicate strings are data (not schema), a mistyped attribute predicate
simply never matches and surfaces at emit as `AttributeHasNoCrosses`, not a
binding error.

**Grouping under `subject_grouped`.** When `ordering = "subject_grouped"`, all
rows for a given subject MUST be contiguous; this is what permits single-pass,
zero-buffer streaming. If a subject recurs after a different subject has
intervened, the converter emits `TripleSubjectNotContiguous` (Error),
identifying the subject and record index, and stops. Input that is not
subject-grouped MUST declare `ordering = "unordered"`, which buffers or
sort-merges (slower, more memory) and imposes no contiguity requirement. The
contiguity check and the subject's first-appearance position (§17 rule 4) are both
judged over **every valid subject row**, including rows whose predicate matches no
attribute or produces no cross.

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

Object name is taken from the named/indexed column. The column is **not implicit**
as an attribute — it generates no formal attributes on its own — but it MAY be
referenced explicitly by an `[[attribute]]` source (§10.2, D-033), the same field
serving as both object key and an analyzed attribute. Object order is the **order of
first occurrence of each cleaned key value** (§17 rule 4). Duplicate key values are
governed by `duplicate_object_policy` (§6.1). A `column` object key missing its
`column`, or naming a column that does not resolve, is `ObjectKeyBindingInvalid`
(Error, spec validate); a data-derived key that is empty, whitespace-only, or
contains newline/control characters is `ObjectKeyValueInvalid` (Error). A wide
object-key cell is validated at **emit**; a triple subject (which also scopes the
§5.3.1 calibration dedup) is validated at **calibrate/emit** (D-099, §16.4).

> **Wide `column` execution (D-083).** Wide `object_key.mode = "column"` — and with it
> the `duplicate_object_policy` machinery (§6.1) — executes at M3, sharing the
> object-key machinery with triple's subject-derived key: `fail`/`keep` stream
> single-pass and `dedupe` runs on the shared grouping/sort-merge/spool path. (M1/M2
> wide conversion used `row_index`; the transitional `ObjectKeyColumnNotImplementedV1`
> guard retired when `dedupe` landed.)

**`mode = "composite"`** **(deferred)**:

```toml
[binding.object_key]
mode = "composite"
columns = ["customer_id", "session_id"]
aggregate = "union"                          # "union" | "intersection"
```

Rows sharing the composite key are merged into one formal object. The v1
planner emits `ObjectKeyCompositeNotImplementedV1` and stops.

For triple binding the object key is **always** the resolved subject: with no
`[binding.object_key]` block the default is `mode = "column"` with `column` set to
`binding.columns.subject`. This matches v2's behavior (subject becomes object name).
An **authored** `[binding.object_key]` under `shape = "triple"` is rejected
(`ObjectKeyModeInvalidForShape`, Error, spec validate) — triple identity is not
repointable. Repeated subjects accumulate per §5.3.1 and are not a duplicate-object
condition.

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
wins (§9 precedence). They fill an omitted `direction` / `boundary` **only after
the winning effective scale has been selected** (§9.2), so they never participate
in the template/matcher merge itself. **`direction`** applies to **all** ordinal
scales — for cut-bin scales it selects which bin edge each threshold sits on
(`le` → upper, `ge` → lower).
**`boundary`** selects the operator only for **value-bin** ordinal scales (where all
four `direction × boundary` combinations are live); for **cut-bin** scales the
operator is fixed by the cut geometry, so a **defaulted** `boundary` never selects it
and never trips `OrdinalBoundaryIncompatibleWithCuts` (§12.3) — only an
explicitly-authored straddling `boundary` does, whether authored on the attribute
or arriving as a winning field from an applied template or matcher (§9.2, §12.3).
Authored-vs-default provenance is preserved by the reader/writer.

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
- **`"keep"`**: each row becomes its own formal object. Object names are assigned by
  the **converter** (the object-key resolver, **not** the writer — P-15 "exporters
  are dumb"), in object emission order (§17 rule 4), and are **unique by
  construction**: the first occurrence of a cleaned key takes the key itself; a later
  occurrence takes `<key>#<record-index>` (0-based source record index, e.g. `P001`,
  …, `P001#2`). If any candidate is already assigned — colliding with a literal data
  key or an earlier generated name — the converter appends `#1`, `#2`, … (ascending
  integers from 1) and takes the first unused; all comparisons are ordinal (P-12).
  The assigned-name set is bounded object-name metadata (P-16); `.cxt` serializes
  these names and `.dat` ignores them, so the guarantee is observable only in `.cxt`.
  A repeated cleaned key is reported as an aggregated `DuplicateObjectKey` (Warning);
  a candidate-name collision that forces the `#1`, `#2`, … escalation is reported
  separately as an aggregated `ObjectKeyNameDisambiguated` (Warning, with a bounded
  `key→name` sample) — one condition, one code. Both are counted and flushed once
  after the object stream (P-16), and a structural halt (an invalid key) suppresses
  any pending counts. The suffix is generated by the converter; it is not part of the
  duplicate row's cleaned key.
- **`"dedupe"`**: rows sharing a key collapse to one formal object; later rows'
  crosses union onto the first, and the object keeps the **first occurrence's**
  position (§17 rule 4). Emit **one aggregated** `DuplicateObjectKey` (Info) — a count
  of the merged (duplicate) rows with a bounded **source-order** sample, and **silent**
  when every key is unique. Because non-contiguous
  keys cannot be merged in a single naive pass without holding all crosses (P-16),
  `dedupe` uses external grouping/sort-merge/spool — the same machinery as triple
  `unordered` — and, like `unordered`, emits in **first-occurrence** order (§17
  rule 4); neither sorts its object output. Note this can cross mutually-exclusive
  bins on one
  object (e.g. two ages), meaningful only for genuinely set-valued data.

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
   schema elements*: **omitted** `declared_domain`s that a discretizer consumes
   (`identity` / `free_per_value`; observed-domain discovery),
   auto-discretizer cuts (`equal_width`, `equal_frequency`),
   `unknown_value_policy = "include"` extensions, and `value_groups`
   `unmatched = "passthrough"` (which discovers one column per observed ungrouped
   value, §11.6). A numeric value that is **present but unparseable** is excluded
   from calibration (§11.5) — it never influences a cut. Produces a **retained,
   immutable resolved outcome** — the resolved cuts, observed domains, `include`
   additions, and pass-through bins — that the Plan phase (and, later, the freeze
   path and manifest serialization, §15) consumes **without re-deriving** it from
   data (decisions.md D-093); the calibrated cuts are also captured in the run
   manifest (§15). The **equal-frequency and percentile-range** cut calibration
   MUST be **exact and bounded-memory** (§11.5) — its correctness does not depend on
   holding the whole population in memory.
3. **Plan** — consume a validated spec together with its **resolved
   calibrated-state** (phase 2 above — a fully-declared spec supplies it with no
   data pass, below) and produce the immutable `ConversionPlan`: the ordered
   formal-attribute schema with stable IDs, scale instances, restriction predicates,
   and ordering rules. Plan **never** plans from unresolved calibration-dependent
   state (D-093). Pure; reads no data.
4. **Emit** — stream objects through the plan, producing the output. Each formed
   object is **discretized and scaled first**, then the object as a whole is kept or
   dropped by `restrict_to`; restriction reads the object's **raw values, before
   discretization** (§10.4), but it selects *objects*, so an included attribute is
   classified — and reports its ordinary value diagnostics — whether or not the
   object survives (§10.4/D-097). A surviving object keeps **all** its crosses.
   `.dat` is single-pass. `.cxt` needs the object count and all object names before
   any incidence row, so it uses a replay-or-spool strategy (§18.1) — never
   materializing the full incidence matrix in Core.

**Fully-declared specs skip Calibrate.** A spec with an **authored**
`declared_domain` (including an explicit empty `[]`, D-122) wherever a discretizer
consumes one (`identity` / `free_per_value`), only
`manual_cuts` / `identity` / `value_groups` / `free_per_value` discretizers
(and `equal_width` with `range = "manual"`, whose cuts are fixed by the spec, not
the data — §11.4), no `unknown_value_policy = "include"`, and no `value_groups`
`unmatched = "passthrough"` is fully determined by its own text: Parse → Plan →
Emit, deterministic from the spec alone, no data pre-pass that affects the schema.
Such a spec is **already calibration-ready**: it satisfies the **same** resolved
calibrated-state contract as an auto-calibrated spec and enters the **identical
Plan input** — one Plan input shape, not a declared-vs-auto split (D-093).

**`convert` auto-calibrates by default** (D-005, D-028): a spec needing
calibration is calibrated in-line, and the resolved cuts are recorded in the
manifest so the run stays reproducible without a separate step.

`fcabedrock calibrate` freezes **every** data-dependent outcome into the spec
(D-122, generalizing D-088): automatic cuts become `manual_cuts`; an observed domain
becomes an explicit `declared_domain` — an **empty** observed outcome freezes as
`declared_domain = []`, a fixed empty domain (D-122); `unknown_value_policy =
"include"` additions are folded into the domain and the policy becomes fixed
`"warn"`; `value_groups` `unmatched = "passthrough"` bins become ordered singleton
groups and `unmatched` becomes fixed `"skip"`. Declaration/first-observation order is
preserved (§17 rule 3) and numeric entries use the canonical numeric spellings
(§11.3, D-096/D-101). The frozen result satisfies the fully-frozen gate (§14):
calibrate writes all three native stored fingerprints, and recalibrating its own
output is **byte-idempotent**. On an `extends` chain, calibrate writes one standalone
flattened frozen spec (§13/§14, D-122).

**Auto and frozen calibration are byte-equivalent.** For **every** freeze mapping
above — auto cuts (`equal_width`, `equal_frequency`), observed domains, `include`
additions, and pass-through bins — converting on the fly and converting with the
`calibrate`-frozen spec MUST produce **byte-identical** `.cxt` and `.dat` on the
**calibration dataset**, in **both native and `--v2-compat`** modes (D-122). This
makes the D-028 guarantee explicit at the output-byte level: freezing changes *when*
a data-dependent decision is resolved, never *which* decision. Only **audit
metadata** is exempt and need not match — the run manifest (§15), the recorded
command line, spec-file hashes, and calibration diagnostics.

**Calibration population.** Every calibration — auto-discretizer cuts or an
observed domain — is computed over one well-defined population: each record
contributes its **non-missing, usable** value for the attribute; a **numeric**
value contributes only when it parses to a **finite** number under `binding.locale`
(a present-but-unparseable value is excluded and never influences a cut, §11.5);
for **triple** input each distinct cleaned `(subject, predicate, value)`
observation contributes once (§5.3.1); and **wide** rows are independent
observations. This population is the input universe, evaluated before `restrict_to`
(below).

> **How the Calibrate phase landed (M4, complete).** **Slice A** (D-098) implemented the
> discovery-class calibration: filling an absent `declared_domain` under a
> consuming discretizer (`ObservedDomainUsed`) and `unknown_value_policy =
> "include"` (§10.6), for the M1 `identity` discretizer; **Slice B** (D-101)
> extended it to numeric `free_per_value`, whose observed/included values are
> canonical numeric identities (§11.3/D-096); **Slice C** (D-102) added the first
> auto-discretizer cut calibration — `equal_width` with `range = "min_max"`, a
> streaming minimum/maximum over the population above (its `range = "manual"` form
> is spec-determined and skips this phase entirely, §11.4); **Slice D** (D-103)
> added the **count-sensitive** calibration — `equal_frequency` (§11.5) and
> `equal_width` `range = "percentile_p1_p99"` (§11.4) — over the exact,
> bounded-memory aggregated population, together with the §5.3.1 subject-local
> deduplication their counts require; and **Slice E** (D-104) added the last one,
> `value_groups` `unmatched = "passthrough"` (§11.6), which discovers one bin per
> observed ungrouped value on the raw-order pass. Every §11 discretizer kind is
> executable. **Slice F** (D-105) then added `restrict_to` execution (§10.4),
> completing M4; it does not affect this phase, since restriction never shapes the
> calibration population (below).

**`convert` calibrates but never discovers.** Discovery (draft-spec generation
from data) is the separate `probe` operation (§7.1, D-003), never performed implicitly
by convert. A spec with an **omitted** `declared_domain` under a consuming discretizer
(`identity` / `free_per_value`) *is* calibrated — the observed domain is filled in
— but the user is warned (`ObservedDomainUsed`,
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

### 7.1 Discovery / `probe` (draft-spec generation)

> **Status: implemented — M5 complete.** Discovery landed across M5 Slices A–D, for
> both the wide and triple shapes. This section remains the normative contract the
> implementation realizes (decisions.md D-106…D-113); the roadmap tracks the
> implementation position and test count.

`probe` is an **optional draft-generation operation** that reads raw data and emits a
**draft Bedrock spec** the user then curates. It is **outside** the four-phase conversion
pipeline (§7): `convert` **calibrates but never discovers**, and `probe` is the separate
operation where discovery happens (D-003/D-036). Probe never runs as a side effect of
convert, and convert never infers a schema from data.

**The caller selects the shape; probe infers nothing structural.** The caller supplies the
`shape` (`wide` | `triple`) and any read settings; probe performs **no** delimiter sniffing,
header detection, shape detection, or value-type inference. This mirrors the legacy tool,
which likewise required choosing CSV/TSV, wide/triple, and header handling before
autodetection — it was never a zero-configuration sniffer. Every read setting has a
**caller-overridable default**. Most are the ordinary §5.1 **common** binding defaults, shared
by **both** shapes: delimiter `","`, `quote_char = "\""` (the only supported quote, §5.1),
`encoding = "utf-8"`, `missing_token = "?"` (an empty token disables token-based missing
detection; empty cells are always missing), and `locale = "invariant"` (inert at M5 — probe
parses no numbers). Only a few defaults are **shape-specific**:

- `has_header` follows §5.1's shape-specific default — **`true` for wide**, **`false` for
  triple**.
- **triple** additionally defaults `ordering = "unordered"` and roles
  `{ subject = 0, predicate = 1, value = 2 }`; a caller may supply a complete role map in one
  addressing mode (§5.3). **`ordering = "subject_grouped"` is explicit-only** — never a
  default and never inferred. (Wide has no `ordering` or role settings.)

**Every discovered attribute is string-valued `identity` + `nominal`.** M5 authors no
numeric, boolean, ordinal, or date typing. (Guided, *advisory* detection — tooling that
*suggests* an attribute looks continuous and offers ranges, never silently reinterpreting —
is recognized future Discovery UX, unassigned to a milestone; header/delimiter sniffing is
likewise future probe tooling, not M5.)

**Observation semantics.** Probe observes the source's **cleaned records exactly once, in
input order** — "single pass" means one *record* pass, not structural inference. Observation
is **per-attribute, set-based and idempotent** over cleaned values, with **ordinal** identity
(P-12) and **first-observation** domain order (§17 rule 3's principle). **Missing values are
not observations:** an empty field is always missing, a field equal to the effective
`missing_token` is missing, and an empty configured token disables token matching. For
**triple** input probe observes raw `(predicate, value)` pairs; predicates are discovered in
**first-appearance order**; an empty or missing predicate is ignored; repeated triples are
idempotent; there is **no grouped or count-sensitive second pass, ever**, and no contiguity
requirement under `unordered`. A probed attribute's domain equals the Calibrate phase's
observed domain (same values, same order) for the same input.

**Structural validation is symmetric with conversion.** Every triple probe validates
**subject usability under both orderings** — an empty, whitespace-only, control-character, or
absent subject halts the probe (`ObjectKeyValueInvalid`, §16.4) — because otherwise the draft
could violate the same-source conversion guarantee below. An **explicitly selected**
`subject_grouped` probe additionally validates **contiguity**
(`TripleSubjectNotContiguous`). **Wide** probe is row-index based and performs **no**
object-key validation. The seen-subject set contiguity validation needs is the inherited
bounded-metadata carve-out (P-16), not a fourth aggregate guard (below).

**Retention and truncation.** Probe retains, **per attribute**, at most the first `limit`
**distinct cleaned non-missing** values in first-observation order, allocated as observed.
The **default `limit` is 100,000**. **Truncation is strictly greater-than:** an attribute is
truncated only when **more than** `limit` distinct values exist; probe then knows only that
**at least one more distinct value exists** — never an exact over-limit count. A **truncated**
attribute authors its retained **prefix** as `declared_domain` (first-observation order) plus
`unknown_value_policy = "include"`, so converting the draft over the probed source **recovers
the complete schema** through the existing §10.6 include calibration (include-appended values
follow the declared prefix in first-observation order, §17 rule 3, so the resulting column set
and order match an untruncated probe's). An **untruncated** attribute authors its complete
non-empty domain; an **all-missing** attribute authors **no** domain (omitted). Each truncated
attribute carries a deterministic human-readable **marker in its `description`**, and
`[provenance].notes` **always** records the effective per-attribute limit and the number of
truncated attributes — **including zero**, so an untruncated draft still explains which limit
produced it. Description and notes are fingerprint-inert (§14). `limit` is a probe option,
never a `[binding]` field and never a fingerprint input; re-probe with a higher
limit to inspect more values.

**Draft validity guarantee.** A **successful** probe produces a draft that (1) contains **at
least one attribute**; (2) **rereads** under the strict TOML reader; (3) **resolves** against
the probed source's schema with no Error/Fatal; (4) **converts the same source under the same
effective settings** with no Error/Fatal (warnings and structurally valid degenerate contexts
are allowed — probe does not itself run conversion; tests enforce this). Probe returns **no
draft** (diagnostics only) when no valid draft exists: zero-column wide input, triple input
with no usable predicates, structural invalidity (unusable subjects under either ordering;
non-contiguous input under explicit `subject_grouped`), or impossible binding/naming.
**All-missing columns and header-only sources succeed** — attributes are authored with their
domains omitted, and their data-side emptiness surfaces at convert as the ordinary
degenerate-context warnings (§16.4).

**Naming and source binding.** For **wide** input a **unique, usable** header cell binds **by
name** (preserving reorder protection); a **duplicate, blank, unusable, or headerless** column
binds **by physical index**. Fallback names (headerless or unusable header cells) are
`column_<zero-based-index>`. Duplicate names disambiguate deterministically with
`#<source-index>`, escalating ordinally `#1`, `#2`, … to the first unused. For **triple**
input a usable predicate string is **both** the source selector and the attribute name; an
**unusable** predicate (invalid as a §10.1 name) keeps its **exact** source selector but
receives the logical name `predicate_<zero-based-first-appearance-ordinal>`; empty or missing
predicates are ignored. **No source selector is ever silently changed**, and every synthesized
name satisfies §10.1 validity and §10.2 uniqueness. Every adjustment or disambiguation is
diagnosed (aggregated Warning, `ProbeAttributeNameAdjusted`); plain headerless `column_N`
synthesis alone is **not** a warning.

**Draft content inventory.** The generated draft contains exactly:

- `[spec]` — `version = 1` and a deterministic probe `description`.
- `[binding]` — **every effective read setting, authored explicitly even when defaulted** (a
  self-documenting draft), including the complete triple ordering and role mappings.
- `[provenance]` — the deterministic notes above, and nothing else.
- Per attribute — `name`, `source` (with an **explicit** `value_type = "string"`),
  `discretizer = { kind = "identity" }`, and `scale = { kind = "nominal" }`, authored
  explicitly; the complete domain when untruncated and non-empty; the retained prefix plus
  `unknown_value_policy = "include"` plus the description marker when truncated; the domain
  omitted when all-missing.

```toml
[[attribute]]
name = "education"
source = { kind = "column", name = "education", value_type = "string" }
discretizer = { kind = "identity" }
scale = { kind = "nominal" }
```

A **truncated** attribute additionally carries the `include` policy and the marker, e.g.
(a domain truncated after 3 of 4 distinct values):

```toml
description = "probe: domain truncated after 3 distinct values; more exist."
declared_domain = ["Bachelors", "HS-grad", "11th"]
unknown_value_policy = "include"
```

**Nothing else** is authored: **no stored fingerprints** (a probe draft is never a frozen
artifact — even when its explicit domains would make it fully-declared, §14), **no
clock/timestamps** (`created_at` is never stamped — the no-clock rule), no tool version, no
`[defaults]`, `[output]`, templates, matchers, or `[binding.object_key]` section (the defaults
are correct: wide `row_index`, triple subject-pinned). The initial Discovery API accepts **no
speculative provenance parameters**; the caller may enrich the returned `SpecDocument`
afterwards (it is a public record).

**The Discovery source seam.** Discovery consumes a **general unbound streaming source
session** whose stable boundary is **ordered schema + streamed cleaned/normalized records +
source-shape information + cancellation + source diagnostics** — *not* a stream or file
abstraction. The CSV wide/triple **adapters** implement that boundary **outside** the
Discovery engine; a future SQL or SPARQL source could implement the same boundary without
refactoring, but such adapters are **examples of future work, not M5 promises**. Discovery
does **not** tokenize, fabricate conversion bindings, open file paths, or reference
`FcaBedrock.Conversion`; it references only `Sources`, `Spec`, `Core`, and `Diagnostics`.
Probe returns `Diagnosed<SpecDocument>`, and the **caller** serializes it via the canonical
writer (§14) and owns all file output. (Exact public interface and member names are reserved
for the implementation-time public-API review, P-4.)

```text
CSV adapter ───┐
Triple adapter ┼─> unbound source session ─> Discovery
Future SQL ────┘
```

An attribute whose observations were **all missing** yields **no** `declared_domain` in
the draft — the field is **omitted**, so converting the draft calibrates it; probe never
authors `[]`, which since D-122 denotes a **fixed empty domain** (§10.3).

**Boundedness.** Per-attribute retention is bounded by `limit` above. Three additional
deterministic **aggregate guards** (advanced probe options) bound a probe as a whole: maximum
discovered attributes, maximum total retained distinct values, and maximum total retained
value text. Their defaults and accounting constants are pinned during implementation review
against representative workloads (the motivating figure: 1,554 attributes × 100,000 values
permits a theoretical 155.4M retained strings). Guards use **deterministic logical accounting,
never available machine memory**. An aggregate breach emits `ProbeLimitExceeded` and yields
**no draft**; aggregate pressure **never silently truncates** further attributes — only the
per-attribute `limit` produces a usable, marked, truncated draft. Probe uses **no
spill/count-sensitive calibration machinery** (it is set-based and idempotent). At the CLI
(M7, D-122) `probe` exposes the shape, the ordinary §5.1 read settings, locale, and the
retention `--limit` only; the three aggregate guards remain **pinned public-API defaults
with no CLI flags**.

**Determinism and cancellation.** Probe's input is defined **generically**: the same ordered
normalized record sequence + ordered schema + effective source settings + probe options ⇒ an
**identical** `SpecDocument`, identical canonical TOML bytes, identical diagnostics,
first-occurrence diagnostic ordering, and identical bounded samples. This is
**record-sequence** determinism (§17), not file-byte determinism, so it holds equally for
future non-file adapters. Probe reads **no** clock, ambient culture, environment variable,
current directory, random source, or available-memory figure. **Cancellation propagates with
no diagnostic and no partial document — ever.** As for every output-producing path, the
repeatability test ships with the implementation (P-7).

## 8. The `[output]` block

Output-formatting options. All optional with sensible defaults. They feed the
per-format output fingerprints (§14) — `[output.cxt]` and `bin_label_unicode` →
`cxt_output_fingerprint`; `[output.dat]` → `dat_output_fingerprint` — and none
feeds `schema_fingerprint`. The one exception is `size_advisory_bytes`: it
changes a warning, never output bytes, so it is not a fingerprint input
(D-077).

```toml
[output]
bin_label_unicode = false                    # default — ASCII operators (>=, <=)

[output.cxt]
line_endings      = "lf"                     # default — "lf" | "crlf"
trailing_newline  = true                     # default — emit \n after last matrix row
size_advisory_bytes = 1_073_741_824          # default — warn when output would exceed 1 GB

[output.dat]
line_endings              = "lf"             # default — "lf" | "crlf"
trailing_newline          = true             # default — emit the final line terminator (see §18.2)
base_index                = 1                # default — 1 | 0  (FIMI is 1-based; 0 for ML conventions)
nonempty_line_trailing_space = false         # default — no trailing space after the last item id
empty_line_trailing_space = false            # default — bare empty line for objects with no crosses
```

**`bin_label_unicode`**. With `false` (default), bin labels and ordinal
thresholds use ASCII operators (`<30`, `[30, 40)`, `>=50`). With `true`,
Unicode operators (`<30`, `[30, 40)`, `≥50`). ASCII default chosen for
ConExp compatibility — ConExp is Java/2009-era and not all installations
handle UTF-8 reliably.

**`size_advisory_bytes`**. During `.cxt` export, after the writer's
object-name/count pass completes and **before any output bytes** (header, names, or
incidence rows) are written, the pipeline computes the **exact final serialized
`.cxt` size in UTF-8 bytes** under the resolved writer options — the `B` header and
blank lines, the decimal count lines, every object name and rendered
formal-attribute name, each configured line ending, the M-character incidence rows,
and the trailing-newline rule — and emits `OutputCxtSizeAdvisory` (Warning, export
phase) when the projection is **at or above** the threshold. The projection counts
**encoded bytes, not characters** (a non-ASCII name and CRLF line endings count at
their real width). A `.dat`-only run emits no advisory. Default 1 GB is
conservative; ConExp struggles well below this. Set to `0` to disable. The advisory
changes a warning, never output bytes, so it remains a non-input to all three
fingerprints (D-077/D-122).

**`.dat` trailing space.** vNext's native `.dat` output has **no** trailing
space after the last item id on a line (`nonempty_line_trailing_space = false`)
and a bare empty line for crossless objects (`empty_line_trailing_space =
false`). v2 emitted a trailing space on every non-empty line; that is one of
the v2-isms reproduced only under `--v2-compat` (below), not the vNext default.
Both knobs exist for callers whose FIMI consumer has a specific expectation.

**v2 byte-equality mode** is *not* a spec setting — it's a CLI flag
(`--v2-compat`) on the convert command that overrides `[output]` to v2's exact
byte conventions. The complete set of v2-isms, all behind this one flag: CRLF
line endings (`.cxt` and `.dat`), `30to<40`-style bin labels, a trailing
space on every non-empty `.dat` line, and a **shape-dependent `.dat` final
newline** — present for a wide source, absent for a triple source (v2's triple
converter wrote no final `.dat` line terminator; §18.2, D-087). Keeping every
v2-ism behind the single flag means the vNext default output is uniformly clean;
v2 reproduction is one switch, not a scattering of legacy defaults.

## 9. Templates and matchers

### 9.1 `[[template]]`

Reusable attribute configurations referenced by name.

```toml
[[template]]
id = "boolean_yes_no"
discretizer    = { kind = "identity" }
scale          = { kind = "dichotomic", true_value = "Yes" }
declared_domain = ["Yes", "No"]
```

**Eligible fields (closed).** A `[[template]]` body may carry exactly these
`[[attribute]]` fields, and no others:

`include`, `discretizer`, `scale`, `declared_domain`, `restrict_to`,
`value_labels`, `missing_policy`, `unknown_value_policy`, `display_name`,
`formal_attribute_format`.

`name`, `source`, `description`, and `template` are **not** legal inside
`[[template]]`: the first three are always per-attribute identity, and the fourth
is excluded because **v1 templates are flat**.

**No template nesting.** A template MUST NOT reference or inherit from another
template. There is no template graph, parent lookup, chain precedence, or cycle
diagnostic in v1; `extends` already composes specs (§13) and a matcher already
shares one template across many attributes. A `template` key inside
`[[template]]` is the ordinary wrong-table-key error (`SpecKeyUnrecognized`), the
same response any non-eligible key gets there. Template inheritance is a post-v1
question requiring a concrete caller (decisions.md D-114).

**Template identity.** `id` is **required**, MUST be unique across the composed
document (§13), and MUST match the stricter `[A-Za-z_][A-Za-z0-9_-]*` form of
§10.1 because it is referenced by name. A malformed `id` is a static shape
failure (`SpecFieldInvalid`, spec parse); a missing `id` and a duplicate `id` are
resolve-phase Errors (§16.4).

### 9.2 `[[matcher]]`

Applies a template to attributes matched by a pattern. Used as the
replacement for v2's "Repeat-To" feature.

```toml
[[matcher]]
match    = { name_regex = "^feature_\\d+$" }
template = "boolean_yes_no"

[[matcher]]
match    = { source_index_range = [10, 1553] }   # for v2 Internet-Ads-style data
template = "boolean_yes_no"
```

A matcher **configures already-declared logical attributes**. It never
synthesizes an attribute, never performs discovery, and never inspects data rows:
§2 requires an `[[attribute]]` block per logical attribute, and matcher
application only supplies configuration to attributes that already exist.

#### Selectors

A `match` table authors **exactly one** selector — `name_regex` **or**
`source_index_range`. Authoring both, or neither, is `SpecFieldInvalid` (Error,
spec parse). A matcher MUST also reference a template; a missing `template` key
is likewise `SpecFieldInvalid`.

**`name_regex`** selects on the complete logical `attribute.name` (§10.1) — never
a source header, predicate text, `display_name`, or a rendered formal-attribute
name. The pattern MUST match the **whole** logical name; explicit anchors remain
legal but are redundant when they express that same boundary. Matching uses .NET
regex with `RegexOptions.CultureInvariant`, is **case-sensitive** by default,
honors authored inline options (e.g. `(?i)`), and passes
`Regex.InfiniteMatchTimeout` explicitly — a finite, machine-speed-dependent
timeout would make the same spec host-dependent, and `RegexOptions.NonBacktracking`
is not adopted because it would silently narrow the regex language relative to
`value_groups`. Each pattern is compiled once and reused across attribute names.
An authored pattern MUST be non-empty and compilable; an empty or uncompilable
pattern is `SpecFieldInvalid` (Error, spec parse) — there is no dedicated
regex-error code, exactly as for `value_groups.pattern` (§11.6).

> **Deliberate divergence from §11.6.** `value_groups.pattern` is **partial**
> (unanchored `IsMatch`) because it searches *data values*; `name_regex` is
> **whole-name** because it is an identity test over a configuration-bounded set
> of names. The two are not to be "harmonized" later by accident.

**`source_index_range = [lo, hi]`** selects on the **resolved physical
wide-source column index** — inclusive on both ends, **zero-based**, the same
index space as `source.index` (§10.2). It is evaluated **after** ordinary
header/schema binding, so a name-bound source participates normally; a name-bound
source with no schema supplied fails the existing source-binding condition
(`SourceBindingInvalid`, §10.2) rather than any matcher-specific one. **Every**
declared logical attribute bound to an in-range index matches, including several
logical attributes bound to the same physical column (§10.2). The range MUST be
exactly two TOML integers satisfying `0 ≤ lo ≤ hi`; wrong arity, a non-integer, a
negative endpoint, or reversed endpoints are `SpecFieldInvalid` (Error, spec
parse). An endpoint **beyond the source width is legal over-coverage** — a
generous range simply has no further attributes to match. `source_index_range` is
**incompatible with `shape = "triple"`** (a predicate source has no column index)
and is an Error at spec resolve, one per incompatible matcher (§16.4).

#### Resolution order

**Resolution order** (lowest to highest precedence) for an attribute's
final config:

1. Hard-coded built-in defaults
2. `[defaults]` overrides
3. Matcher templates (in declaration order)
4. Per-attribute `template = "..."` reference
5. Per-attribute explicit fields

**Layering within tier 3.** **Every** matcher whose selector matches an attribute
participates, in declaration order. Matching templates layer **field-wise**: for a
field authored by more than one matching template, the **last matching template
that authors that field** wins. A later matching template that **omits** a field
never erases a value an earlier one supplied.

**Merge granularity and provenance.** Template/matcher application is
deterministic syntactic sugar for the equivalent explicit per-attribute
configuration, so:

- **Top-level fields layer by authored presence.** Omission inherits from the
  tier below; an explicit `false`, an authored empty collection (e.g.
  `declared_domain = []`, §10.3), or a value equal to its own default still
  overrides a lower tier. Presence, not value, drives the merge.
- **Compound fields are whole values.** `discretizer`, `scale`,
  `declared_domain`, `restrict_to`, `value_labels`, `display_name`, and
  `formal_attribute_format` are replaced **entire** across precedence tiers; they
  never deep-merge their internal leaves or map entries. (This matches §13 rule 1's
  whole-value nested `[binding]` tables, not `[output]`'s per-leaf merge.)
- **A winning template/matcher field counts as explicitly authored** for
  validation and provenance. A template cannot make otherwise-invalid
  configuration valid, nor suppress its established diagnostic — so a
  template-supplied `boundary = "strict"` with `direction = "ge"` over cut bins
  is invalid and reports `OrdinalBoundaryIncompatibleWithCuts` (§12.3), exactly
  as the equivalent explicit attribute would.
- **`[defaults].ordinal_direction` / `ordinal_boundary` fill last.** They fill an
  omitted `direction` / `boundary` only **after** the winning effective scale has
  been selected, and the filled value retains **defaulted**, not authored,
  provenance (§6, §12.3).
- **A replaced value is semantically irrelevant** — a lower-tier value that a
  higher tier replaces contributes neither behaviour nor validation.
- **Application covers every attribute, including `include = false`.** Emitted
  shaping supplied through a template stays **dormant** while the attribute is
  excluded, and a template-supplied `restrict_to` is **live** on a filter-only
  attribute, exactly as authored configuration is (§10.4, §10.9).
- **The effective attribute is then validated exactly like its equivalent flat
  declaration**, by the same condition owners (§16.4).

#### Diagnostics for matchers

A matcher that selects **zero attributes** produces one Warning — a typo-catcher,
not an error, since a pattern or range may legitimately over-cover.

A matcher is **fully shadowed** when it selects at least one attribute and, on
**every** selected attribute, **every field its template authors** is overridden
by a higher-precedence source — a later matching template authoring the same
field, the attribute's directly named template, or an explicit attribute field.
A fully-shadowed matcher produces one Warning. The determination is made at the
**merge** level over authored fields only, and is **independent of `include =
false` dormancy**: a matcher whose fields *win* on an excluded attribute is *not*
fully shadowed, even though the winning configuration is dormant while the
attribute is excluded.

Both warnings are ordered by matcher declaration order and neither affects
fingerprints or output. The full M6 diagnostic matrix — template identity,
unknown references, shape incompatibility, per-effective-attribute granularity,
and the deterministic family ordering — is §16.4.

#### Boundedness

Matcher evaluation is a **one-time, configuration- and schema-bounded**
resolution step: approximately attributes × matchers, with two integer
comparisons per attribute for a range and one compiled regex per `name_regex`
matcher. It reads **no data rows** (§7 phase 1), and no template or matcher
syntax enters Core, calibration, or per-row emission.

#### Fingerprints and equivalence

The resolved (post-merge) per-attribute config is what feeds into the
schema fingerprint. Source-file template references and matcher rules
are not part of the fingerprint themselves.

It follows that semantically equivalent **flat**, **materialized** (the same
configuration written out on every attribute), **template/matcher-authored**, and
**`extends`-composed** specs resolve to the same effective attributes, the same
plan, the same three fingerprints, and byte-identical `.cxt` / `.dat` output.
Adding a valid unused `[[template]]`, or a matcher that matches nothing, changes
no fingerprint and no output byte (the unmatched matcher adds only its Warning).

#### Unused templates are dormant

An **unused** `[[template]]` — unreferenced by any attribute and selected into by
no matcher — is **semantically dormant**: it round-trips and converts without
error. Parse-level shape checks and the §10.7 naming-format grammar still apply
to its authored body, but effective-semantic combinations (missing scaling, an
incomplete scale) are validated **only if the template applies** to some
attribute.

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
other external sources without renaming. A matcher's `name_regex` selects on
this string, and on the whole of it — see §9.2 for the exact matching contract.
Template `id` fields (§9.1) use the
stricter `[A-Za-z_][A-Za-z0-9_-]*` form because they're referenced by
code.

**`source`** *(required)*. See §10.2.

**`display_name`** *(optional)*. Used in `formal_attribute_format`'s
`{display_name}` placeholder. Defaults to `name`. An **authored**
`display_name` MUST be non-empty and MUST contain neither CR nor LF; a violation
is `SpecFieldInvalid` (Error, spec parse). The reason is structural: a
`display_name` can reach a rendered formal-attribute name, and `.cxt` is
line-oriented (§18.1), so a newline there would corrupt the file's structure
(§10.7).

**`include`** *(boolean, default per `[defaults]` or `true`)*. If `false`,
the attribute generates no formal attributes and emits no incidence — but its
own `restrict_to` filter (§10.4) still applies. This is the **filter-only
attribute** pattern: filter objects by a field without analyzing that field.
An `include = false` attribute with no `restrict_to` is inert (a harmless
no-op, allowed during staged spec editing). The attribute is always validated
syntactically regardless of `include`, and its **source binding** is checked on
the **same terms** as an included one: because the conversion pipeline resolves
**schema-aware** (the two-stage source bootstrap, D-098), an out-of-range or
unresolvable source index is reported as an aggregated `SourceBindingInvalid`
(Error, **spec validate**, §16.4) — for included **and** filter-only attributes
alike. The resolution trust boundary (`ResolvedSpec.Create`) re-checks it as a
programmer-error backstop for hand-built graphs, and the planner's residual
handling is an unreachable-by-construction invariant, never a user-facing
diagnostic (D-098).

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
`index` needs no header. A `name` — here or in a triple `columns` role (§5.3) — MUST
resolve to **exactly one** column; no matching header, or a duplicate matching
header, is `SourceBindingInvalid`.

The source `kind` MUST match the binding `shape`: `column` under `wide`, `predicate`
under `triple`. A mismatch (a `predicate` source under `wide`, or a `column` source
under `triple`) is `SourceBindingInvalid`, which also owns invalid triple `columns`
shape/addressing (§5.3) — a missing or partial role table, mixed index/name
addressing, or all-name binding without `has_header = true`.

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
`value_type` paired with a numeric-entry `restrict_to` (an exact `{ value = n }`
or a range) — is `SourceValueTypeInvalid` (Error). The mirror case, a **numeric**
source with a **bare string** `restrict_to` entry, is owned by
`RestrictToNumericEntryRequired` (§10.4), not this code.

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

**Numeric `free_per_value` domains.** For a numeric `free_per_value` source
(§11.3) each `declared_domain` entry is **parsed under `binding.locale`** to its
numeric identity (the §5.1 exception to verbatim strings), so `90`, `90.0`, and
`9e1` denote one domain member and all zero spellings canonicalize to `0`
(D-096). An entry that is **unparseable**, **non-finite** (NaN/±∞), or a
**normalization duplicate** of another entry (two spellings, one numeric
identity) is `DeclaredDomainInvalid` (Error, spec validate). Declaration order
still drives column order, read over these normalized identities (§17 rule 3).
For `identity` (string-only, §10.2) and categorical `free_per_value` the entries
remain verbatim strings.

If `declared_domain` is **omitted**, the Calibrate phase (§7) fills it from the
observed domain in the data, and the user is warned (`ObservedDomainUsed`) because
the resulting schema then depends on this specific input. Any **authored** domain is
complete: an explicit empty list `[]` denotes a **fixed empty domain** — **zero
declared value bins**, not a calibration request (D-122, revising D-071's earlier
empty-as-absent reading). An authored `[]` suppresses observed-domain discovery, but
it does **not by itself guarantee zero formal attributes**: `unknown_value_policy =
"include"` may still extend the domain with observed values (§10.6), and
`missing_policy = "as_attribute"` may still add the missing column (§10.5). This
holds through every resolution tier: a template-supplied `[]` is likewise
authored-complete (§9.2, D-114). The TOML reader/writer round-trips an authored `[]`
verbatim; `fcabedrock calibrate` freezes an **omitted** domain to the observed values
as an explicit `declared_domain` — and an empty observed outcome freezes as `[]`
(§7, D-122). For input-independent, spec-first workflows, declare the domain
explicitly or freeze it with `fcabedrock calibrate`.

> **Observed-domain calibration (M4 Slice A/B).** The Calibrate phase fills an
> **omitted** `declared_domain` on an included consuming
> discretizer from the observed data, warning with `ObservedDomainUsed` (§7). An
> authored `[]` is a complete fixed empty domain and is never calibrated (D-122;
> implemented at M7 Slice A). The
> transitional `ObservedDomainCalibrationNotImplementedV1` plan reject retired at
> M4 Slice A (D-098, superseding D-071). Cut discretizers ignore `declared_domain`
> (above) and are unaffected. The `identity` case executes from Slice A; the numeric
> `free_per_value` case joined at Slice B (D-101), observing canonical numeric
> identities (§11.3/D-096).

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

For continuous sources (numeric raw values) — **exact** numeric entries and
**ranges** may be mixed freely:

```toml
restrict_to = [
  { value = 30 },                            # exact: x == 30 (30, 30.0, 3e1 all match)
  { from = 10, to = 20 },                    # 10 ≤ x < 20
  { from = 40, to = 50 },                    # 40 ≤ x < 50
  { from = 90 },                             # x ≥ 90 (open-ended)
  { to = 5 },                                # x < 5 (open-ended)
  {},                                        # any usable numeric value
]
```

Range bounds are inclusive on the low side and exclusive on the high side,
matching the bin convention. An **exact** numeric entry `{ value = n }` matches by
**parsed numeric identity**, so `30`, `30.0`, and `3e1` all match a value of 30
(`n` must be finite). The empty range `{}` matches any **usable** numeric value —
equivalently, it excludes only missing/unparseable values.

**Execution is existential.** An object **passes** an attribute's `restrict_to`
when **at least one** observed raw value for that source matches **at least one**
entry (the OR within an attribute). For triple input an **absent** predicate
matches nothing and a **missing** value matches nothing — either way the object
fails that attribute's restriction. An object that fails **any** attribute's
restriction is **excluded** (the AND across attributes, §10.1). Under wide
`dedupe` (§6.1) the rows for a key are grouped **first** and the restriction is
evaluated existentially over the **merged** object: if any of the merged
observations matches, the **complete** object survives with **all** its
observations and crosses (to keep only a single row, use `keep`/`fail` or
upstream conflict resolution, not `dedupe`). Wide `row_index`/`fail`/`keep` — one
observation per source per object — is the one-observation special case: the
single cell either matches or it does not.

**Restriction-path diagnostics.** Evaluating a restriction reads raw values, so it
reports on them like any other data pass (D-097). On a **numeric** restriction a
**valid non-match** and a **missing** value are **silent** (a non-match is the
filter working, not an anomaly); an **unparseable or non-finite** input is a
**non-match** (it can match no numeric entry) **plus** an aggregated
`SourceValueUnparseable` at the severity `unknown_value_policy` selects — `skip`
silent, `warn` Warning, `fail` Error/abort, `include` Warning (§10.6). This is the
**only** diagnostic path for a **filter-only** attribute (`include = false`), which
is otherwise discarded before discretization — without it an unparseable filtered
value would report nothing. An **included-and-restricted** attribute keeps its
**ordinary** malformed/unknown-value diagnostics even when the restriction excludes
its object: restrictions **filter objects, not observations**, and each raw
observation is diagnosed **at most once** (the restriction pass and discretization
pass never double-count the same cell).

**Sequencing vs `duplicate_object_policy`.** Restriction is evaluated on each formed
object's **complete** observation set, and object formation order is unchanged
(decisions.md D-105). Concretely:

- **Wide `row_index` / `fail` / `keep`** — the row is the formed object. It is
  classified, then filtered; a **non-surviving row is not an object**, so it triggers
  no `fail` duplicate check and consumes no `keep` assigned name (§6.1 assigns those in
  **emission** order). **`row_index` names are input positions and filtering never
  renumbers them** (§5.4): if row 0 is filtered and row 1 survives, the survivor is
  still named `1`.
- **Wide `dedupe`** — grouping and merging **precede** restriction (above), and the
  aggregated `DuplicateObjectKey` (Info) counts merged rows **pre-filter**: it reports
  what the *input* contained, so a merged object the restriction later drops is still
  counted.
- **Triple** — the subject's group closes, then the restriction decides emission.
  Structural checks (subject validity, `subject_grouped` contiguity) precede filtering
  and are independent of it.

Within one `restrict_to` list every entry must satisfy the attribute's single
`value_type` (§10.2): a string-fixing source accepts only string entries, a
number-fixing source only **numeric** entries (exact `{ value = n }` or ranges). A
genuinely mixed string/numeric list is therefore a **validation error**
(`SourceValueTypeInvalid` / `RestrictToNumericEntryRequired`, below) — no
single-attribute `value_type` admits both.

**Static validation.** A `restrict_to` list's *shape* is validated at parse/validate,
independently of the data it will later filter:

- a numeric source (`value_type = "number"`, or a numeric-cut discretizer) whose
  `restrict_to` contains a **bare string** entry is `RestrictToNumericEntryRequired`
  (Error) — a numeric source requires a **numeric entry** (exact `{ value = n }`
  or a range). This code — **not** `SourceValueTypeInvalid` (§10.2) — owns the
  numeric-source/string-entry mismatch;
- an **invalid numeric entry** — an exact `{ value = n }` whose `n` is non-finite,
  or a range with equal, reversed, or non-finite **provided** bounds — is
  `RestrictToRangeInvalid` (Error). `{}` (both bounds omitted) is valid;
- a `restrict_to` string value absent from an explicit `declared_domain` (where one
  applies — `identity` / `free_per_value`) is `RestrictToValueNotInDomain`
  (Warning), a typo-catcher.

These are *shape* checks: they reject a list that could never filter meaningfully,
without reading a row.

`restrict_to` enters **both** output fingerprints via the canonical `restrictions`
container (§14). It does **not** make a spec data-dependent — the entries are authored
text, and §7 computes calibration and the column vocabulary over the input universe
*before* restriction selects objects — so a restricting spec can be fully frozen (§14).

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

**Triple input (what counts as missing).** For a predicate-backed attribute,
"missing" requires a **matching-predicate row** for the subject whose value is
empty, equals `missing_token`, or is absent because the row is too short. A
predicate simply **not present** for a subject is **no observation**, not missing
(§5.3.1): under `"as_attribute"` it does **not** cross `<name>-missing`. This is the
triple analogue of the wide cell-per-column model — where every object carries a
value (present or missing) for every column — which triple input does not guarantee.

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
(Warning) so the data-dependence is visible. Like all Calibrate-phase resolution,
`include` is implemented at **M4** (§7). The `UnknownValueObserved`
severity follows the policy: `warn` → Warning, `fail` → Error.
`fcabedrock calibrate` freezes `include` by folding the observed additions into an
explicit `declared_domain` (appended after the declared values, first-observation
order, §17 rule 3) and rewriting the policy to fixed `"warn"` (§7, D-122).

**Numeric attributes.** For a **numeric-typed** source — a cut-based discretizer
(where `declared_domain` is ignored, §10.3) **or** a numeric `free_per_value` — a
value that is **present but not a usable finite number** (it fails to parse under
`binding.locale`, or parses to NaN/±∞) is a **present-but-invalid** value (D-050,
§11.5): the object is kept, no cross is emitted for that attribute, the value is
**excluded from calibration**, and `SourceValueUnparseable` governs the diagnostic
at the severity `unknown_value_policy` selects — `skip` → no cross, no diagnostic;
`warn` → no cross, Warning; `fail` → Error/abort; `include` → no cross, Warning
(an unparseable token cannot be added to a numeric domain, so `include` behaves as
`warn` here). For a **cut-based** discretizer there are no out-of-domain
*categorical* values, so this is the only role `unknown_value_policy` plays; for a
numeric `free_per_value` **with** an explicit `declared_domain`, a **parseable**
value not in the domain is instead an ordinary out-of-domain value and follows the
categorical policy above. This same severity mapping governs an unparseable value
met on a **restriction** path, including a **filter-only** attribute's — the only
diagnostic route for a value that never reaches discretization (D-097, §10.4).

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
and applies to **every** formal attribute that attribute produces — **including
its `missing_policy = "as_attribute"` column**, which then renders through the
format (with `{value}` resolving to the literal `missing`, per the table below)
instead of the default `{column}-missing`. The missing column's *position* and
canonical identity are unaffected (§10.5, §14):

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

**Template placeholders — a closed, case-sensitive set.** These five are the
*only* placeholders; there are no others, and the set is not extensible by a
conforming implementation:

- `{name}` — the attribute's `name` field
- `{column}` — same as `name` (alias for clarity in wide-CSV context)
- `{display_name}` — the attribute's `display_name`, defaults to `name`
- `{value}` — the value-side label (a discretizer bin label, a category
  value, etc.)
- `{scale_op}` — for ordinal scales, the inequality operator (`>=`, `<=`,
  `<`, `>`, or Unicode per §8); empty for non-ordinal scales

**Grammar (normative).** `{{` renders a literal `{` and `}}` renders a literal
`}`. Parsing and substitution are a **single left-to-right pass**: text
substituted in from `name`, `display_name`, a raw value, or a `value_labels`
label is **never rescanned** as format syntax or brace escaping. (This is
required for determinism — §10.1 explicitly permits a `name` containing
brace-like text.) So `"{{{column}}}-{value}"` renders literal braces around the
column name.

**Static validity (all `SpecFieldInvalid`, Error, spec parse).** There is no
dedicated format diagnostic:

- an **unknown** placeholder (including a case variant such as `{Value}`), an
  **empty** placeholder `{}`, and an **unmatched or malformed** brace;
- an **empty format string** — `formal_attribute_format = ""` is rejected rather
  than rendering every formal attribute as the empty name;
- **CR or LF in the format's literal text.**

**Literal text may be empty.** The non-empty requirement above applies to the
format string *as a whole*, never to an individual literal span, so
`"{name}"`, `"{value}"`, and `"{name}{value}"` — whose literal spans between and
around the placeholders are empty — are perfectly valid.

These checks apply **wherever the format is authored** — `[defaults]`,
`[[template]]`, and `[[attribute]]` — including inside an **unused** template and
on an **excluded** attribute: authored *shape* is the parser's concern, while
dormancy is semantic (§9.2, §10.9).

**`{value}` resolution by formal-attribute kind** (so a custom
`formal_attribute_format` that uses `{value}` is well-defined everywhere):

| Formal attribute | `{value}` resolves to |
| --- | --- |
| `nominal` bin | the bin label (category value or cut-bin label) |
| `ordinal` threshold | the threshold label (`{scale_op}` carries the operator) |
| `dichotomic` (the single column) | the scale's `true_value` — **via `value_labels` when labels are live** (§10.8); the default format omits it |
| `missing_policy = "as_attribute"` column | the literal `missing` |

For a **numeric** `free_per_value` bin (a `value_type = "number"` value-bin,
§11.3), the `nominal`-bin `{value}` above is the **parsed numeric value** rendered
with the §14 invariant, shortest round-trippable formatting — so `90`, `90.0`, and
`9e1` share one bin rendered `90`.

`{value}` consults `value_labels` wherever labels are **live** (§10.8) — that is,
under `identity` / `free_per_value`. For a **dichotomic** scale this means the
**labelled** `true_value`, not necessarily the raw authored one: with
`true_value = "t"`, `value_labels = { t = "bruised" }`, and
`formal_attribute_format = "{column}-{value}"`, the attribute `bruises?` renders
`bruises?-bruised`. Under a discretizer that does not consult labels, the raw bin
label is used.

If two formal attributes render to the same name under the chosen format (e.g. a
real category value `missing` colliding with the missing column), the planner
emits `FormalAttributeNameCollision` (§10.2).

**Rendered-name validity (normative).** After final substitution, **every**
rendered formal-attribute name MUST be **non-empty** and MUST contain **neither
CR nor LF**. A violation is an Error at **plan**, reported per affected logical
attribute, alongside `FormalAttributeNameCollision` (§16.4). The rule exists
because `.cxt` is line-oriented (§18.1) and its writer is deliberately dumb
(P-15): a newline inside a rendered name would add a phantom line, so the
declared attribute count and the name block would disagree and every consumer
would misparse — silently, since the canonical fingerprint encoding escapes
control characters happily (§14).

This backstop is **not** limited to the naming surface: it also catches CR/LF and
emptiness arriving through raw values, calibrated domains, and `value_labels`, so
it covers routes that exist independently of `formal_attribute_format`. Because
the plan is shared, an invalid rendered name **fails the whole plan and therefore
blocks `.dat` emission as well as `.cxt`** — consistent with existing
formal-name-collision behavior, even though `.dat` serializes no names. Exporters
never sanitize, replace, escape, or independently validate a name (P-15). No
broader C0/Unicode-control prohibition is adopted in v1: only *empty*, *CR*, and
*LF* are constrained here.

This setting affects `cxt_output_fingerprint` only — not `schema_fingerprint`, and
not `dat_output_fingerprint` (`.dat` carries numeric IDs, no names). A naming
setting that does not change any rendered name is byte- and hash-neutral.

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
(`ValueLabelKeyNotInDomain`) — a typo-catcher. For a **numeric** `free_per_value`,
`value_labels` keys are parsed under `binding.locale` to the **same normalized
numeric identity** as the domain (§10.3, D-096): a key whose parsed value is not in
the domain stays `ValueLabelKeyNotInDomain`, while two keys collapsing to **one**
numeric identity (e.g. `90` and `90.0`) are `ValueLabelKeyDuplicate` (Error, spec
validate). Under a discretizer that does not
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
numeric distinct-value bins; `identity` is string-only (§10.2). That numeric bin's
**rendered label** — and any `{value}` in `formal_attribute_format` (§10.7) — is
the parsed number formatted with the §14 invariant, shortest round-trippable rule,
so the collapsed bin renders `90`, never `90.0` or `9e1`.

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

**Range mode determines the phase.** With `range = "manual"`, `vmin`/`vmax` fix the
span, the `bins - 1` cuts are computed **from the spec alone**, and no data
calibration runs: a manual `equal_width` is a **spec-determined** discretizer,
skips Calibrate (§7), and is **eligible for stored fingerprints** like any
fully-declared spec (§14). Missing `vmin`/`vmax` under `range = "manual"` is
`SpecFieldInvalid` (Error, spec parse); a non-finite or non-increasing
(`vmin ≥ vmax`) authored range is `EqualWidthRangeInvalid` (Error, spec validate);
and a `precision` / `round_to` that collapses the derived cuts (two cuts round to
the same value) is `EqualWidthCutsCollapsed` (Error, spec validate).

With a **data-derived** range (`min_max` / `percentile_p1_p99`), the span is drawn
from the calibration population (§7): data with **no usable spread** (all values
equal, or too few to bound the range) is `CalibrationDataInsufficient` (Error,
calibrate); final cuts that — after any rounding — are non-finite or not strictly
ascending are `CalibrationCutsInvalid` (Error, calibrate). The `equal_frequency`
**distinct-value guard** (§11.5) does **not** apply to `equal_width`: equal-width
bins are placed by span, not by count, so equal width tolerates fewer distinct
values than `bins`. Percentile-range (`percentile_p1_p99`) calibration is subject
to the same **exact, bounded-memory** obligation as `equal_frequency` (§11.5);
`min_max` is not — a streaming minimum and maximum is bounded by construction.

**Cut derivation (normative).** The `bins - 1` cuts are computed in binary64 over
the resolved span. For `i = 1 … bins - 1` with `t = i / bins`, the interpolation is
**sign-aware** so that no finite increasing range can overflow: when the span shares
a sign or has a zero bound (`vmin ≥ 0` or `vmax ≤ 0`) the cut is
`vmin + (vmax - vmin) * t`; when the span crosses zero (`vmin < 0 < vmax`) it is the
convex combination `vmin * (1 - t) + vmax * t`. Every finite increasing range
therefore derives **finite** cuts — `vmin = -1.7e308, vmax = 1.7e308` included — so a
range is never rejected merely for being wide. `precision = { round_to = r }` then maps
each cut to the nearest multiple of `r`, **halfway cases to even**; every computed cut
is canonicalized so a computed negative zero renders `0` (§14/decisions.md D-096).
The derived-cut validity rules above then apply to the result: they catch a `round_to`
that collapses two cuts onto one value, and — at the extreme margin of the double
range — a span too narrow to hold `bins - 1` distinct representable cuts. See
decisions.md D-102.

**Percentile range (normative).** `range = "percentile_p1_p99"` draws its span from
the **exact order statistics** `p1` and `p99` of the calibration population:
`p1 = v_i` for the least `i` with `C_i · 100 ≥ N`, and `p99 = v_i` for the least `i`
with `C_i · 100 ≥ 99 · N`, over the same aggregated ascending `(value, count)`
population §11.5 defines. The comparisons are exact integer arithmetic; percentile
positions MUST NOT be interpolated between neighbouring order statistics, taken from
an approximate-quantile sketch, or delegated to a machine-dependent library
percentile — any of which would break the §7 auto/frozen byte-equivalence. `p1 = p99`
(including an empty population) has no usable spread and is
`CalibrationDataInsufficient` (Error, calibrate). The selected span then feeds the
cut derivation above unchanged: `precision` applies **after** the span is chosen.
Unlike `min_max`, percentile is **count-sensitive**, so for triple input it obeys the
§5.3.1 subject-local deduplication. See decisions.md D-103.

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

**`tie_policy`** is a **calibration-time** rule. When a candidate boundary would
land inside a run of equal ("tied") values, `tie_policy` decides which side of the
boundary receives the **entire tied-value group**: `"left"` places the whole group
in the lower bin (the boundary sits at the group's right edge), `"right"` in the
upper bin. It never splits a tied group across bins. **Emit** does **no** tie
handling of its own — it applies the ordinary §11.2 half-open `[lo, hi)` geometry
to the resolved cuts, so at emit a value equal to a cut always falls in the upper
bin; `tie_policy` only governed *where the cut was placed* during calibration.
With heavy ties, bin counts may deviate substantially from `n/bins`; this is
inherent to the algorithm, not a bug.

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
  fewer than `bins`, calibration emits `CalibrationDataInsufficient` (Error) and
  stops, rather than silently producing fewer bins.
- **Ties:** governed by `tie_policy` (above); the result is fully determined.

**Formula-stage obligation.** When the number of distinct surviving values is
**≥ `bins`**, quantile placement plus `tie_policy` MUST select **`bins - 1`
distinct, strictly-ascending cut gaps** — it MUST NOT drop a bin merely because
several target boundaries land within one tied group (the tie policy resolves them
to the same side, and the remaining boundaries move on to the next distinct gaps).
The resulting numeric cuts remain subject to the post-formula validity check:
data-calibrated cuts that are **non-finite or not strictly ascending** are
`CalibrationCutsInvalid` (Error, calibrate).

**Feasibility prevails over tie-side preference (normative).** The obligation above
and `tie_policy` can be mutually unsatisfiable: a preferred gap may be unavailable
because an earlier boundary already took it, because reserving gaps for the later
boundaries forbids it, or because it is not a gap at all (`"right"` on the first
group prefers the gap *below* the whole domain; `"left"` on the last group prefers
the gap *above* it). In every such case **feasibility wins and `tie_policy` yields**.
Boundaries are allocated in **ascending target order**, each taking the nearest
feasible gap within the window `[p + 1, m - 1 - (bins - 1 - k)]` — where `p` is the
previously selected gap (initially `0`), `m` the distinct-value count, and `k` the
boundary's 1-based index. The lower bound forces gaps to strictly ascend; the upper
bound reserves one gap for every later boundary. This may place a tied group on the
**opposite side** of its policy preference. It is normal resolution, not an error:
it is never reported as invalid cuts. Since `m ≥ bins` guarantees the window is
non-empty, `bins - 1` distinct ascending gaps always exist, and the cuts are
strictly ascending **by construction**.

**Bounded-memory (normative).** Equal-frequency and percentile-range
(`equal_width` `percentile_p1_p99`, §11.4) calibration MUST be **exact,
deterministic, and bounded-memory** at **M4** — it is a correctness property, not
a later performance retrofit. When the calibration population exceeds the
working-memory budget, the implementation spills/sorts/aggregates (or uses an
equivalent exact method); the spill and non-spill paths MUST produce
**byte-identical** cuts and output. **Approximate quantiles are prohibited.** The
budget itself is an implementation internal — never a TOML field or a fingerprint
input (decisions.md D-095/D-082); the M8 scaling pass (`roadmap.md` M8) may tune it
and benchmark algorithms, but boundedness is established here, not there.

Counting is exact, so it is also **checked**: if a per-value count, the running
total, or a merge sum would exceed the implementation's integer range, calibration
stops with `CalibrationPopulationTooLarge` (Error, calibrate; §16.4) and yields no
calibrated result. It is a distinct condition — too much data to count exactly —
and MUST NOT be reported as `CalibrationDataInsufficient` (its opposite) or as a
storage failure. It is a contract-totality rule: no v1-scale workload reaches it
(decisions.md D-103).

**Quantile selection (normative).** Let the aggregated population be distinct
ascending finite values `v_1 … v_m` with counts `c_i`, cumulative counts `C_i`
(`C_0 = 0`), and total `N = C_m`; a **gap** `g ∈ 1 … m-1` lies between `v_g` and
`v_{g+1}`. Boundary `k` (`1 … bins-1`) targets the **exact rational** `N·k / bins`.
That target MUST NOT be materialized as a floating-point or decimal value: it is
compared by exact integer cross-multiplication (`C_i · bins` against `N · k`), which
a `double` rank cannot reproduce once `N` approaches 2^53. The target falls in the
first group `i` with `C_i · bins ≥ N · k`; the desired gap `d(k)` is:

- `N·k = C_i·bins` exactly (a **group edge**) → `d(k) = i`, **regardless of
  `tie_policy`** — the boundary already separates whole groups, so no tie exists;
- otherwise (strictly inside group `i`) → `d(k) = i` for `"left"`, `d(k) = i - 1`
  for `"right"`.

The feasibility window above then allocates the actual gap. `cut_placement`
finally values it: `"right_value"` → `v_{g+1}`; `"midpoint"` → the midpoint of
`v_g` and `v_{g+1}`, computed **sign-aware** exactly as in §11.4 (`a + (b - a)/2`
for a same-sign gap or one with a zero bound; `(a + b)/2` for a gap crossing zero),
so no extreme gap can overflow. If the midpoint cannot land strictly above `v_g` —
the two values are adjacent representable doubles — the cut is `v_{g+1}`, which is
membership-identical under the half-open geometry. Every computed cut is
canonicalized so a computed negative zero renders `0` (§14/decisions.md D-096). See
decisions.md D-103.

**Examples (normative).** The first two are the §11.5 tie-policy illustrations; the
last three pin the feasibility precedence. All use the default
`cut_placement = "right_value"`.

- `[1, 2, 2, 2, 3, 4]`, `bins = 3`, `tie_policy = "left"` → cuts `3, 4`. Two target
  boundaries fall inside the tied `2`-run; the formula must still yield **two**
  distinct ascending cuts (the second boundary moves to the next gap) — three bins
  result, never a collapse to two.
- `[1, 2, 2, 2, 3]`, `bins = 2`: one boundary lands in the `2`-run. `tie_policy =
  "left"` puts the whole `2`-group in the lower bin (cut at `3`), `"right"` puts it
  in the upper bin (cut at `2`). Either way, converting on the fly and converting
  from the `calibrate`-frozen spec (the auto cut becomes `manual_cuts`) produce
  byte-identical output on this dataset (§7).
- **Collision** — `[1, 2, 2, 2, 3]`, `bins = 3`, `tie_policy = "left"` → cuts
  `2, 3`. Both boundaries prefer the `2`-group's right edge; the window pushes the
  first down to the gap below it so a gap remains for the second, against `"left"`.
- **Last-group edge** — `[1, 2, 3, 4, 5, 5, 5, 5, 5, 5]`, `bins = 2`,
  `tie_policy = "left"` → cut `5`. The preferred gap is above the whole domain and
  therefore not a gap; the tied `5`-group lands **upper** despite `"left"`.
- **First-group edge** — `[5, 5, 5, 5, 5, 5, 6, 7, 8, 9]`, `bins = 2`,
  `tie_policy = "right"` → cut `6`. The preferred gap is below the whole domain;
  the tied `5`-group lands **lower** despite `"right"`.

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

`values` and `pattern` are **independently optional and may be combined** — a group
needs at least one of them.

**Regex semantics.** `pattern` is a **.NET regex** evaluated with
**culture-invariant** matching, **case-sensitive** by default, and **partial**
(unanchored `IsMatch`): `pattern = "I[0-9]{2}"` matches anywhere in the value.
Anchor (`^…$`) for full-string matching. Authored inline options are honored — e.g.
`(?i)` for case-insensitivity — because they are part of the pattern.

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
  it is frozen (§14). `fcabedrock calibrate` freezes `passthrough` by appending each
  discovered bin as a singleton group (`{ label = <value>, values = [<value>] }`) in
  first-observation order and rewriting `unmatched` to fixed `"skip"` (§7, D-122).

A value matching multiple groups falls into the first matching group in
declaration order; this is part of the planner's deterministic resolution
and IS captured in the schema fingerprint.

`declared_domain` is **not** consulted for `value_groups` (D-055): the `groups`
and the `unmatched` policy together define which values are recognized, so a
separate domain list would be a second, overlapping gate. Recognition is
therefore: a value matches a group → its group label; otherwise the `unmatched`
policy decides (`skip` defers to `unknown_value_policy`, `other` → the synthetic
`Other` bin, `passthrough` → the value's own raw label).

**Group validity (static, one condition → one code).** Validated at parse: each
group's `label` is present and non-empty; every authored explicit value is
non-empty; an authored `pattern` is non-empty and a valid regex; and the group
carries **at least one** non-empty explicit value **or** a non-empty pattern. Any
of these — a missing/empty label, an empty or invalid `pattern`, an empty explicit
value, or a group with neither matcher — is `SpecFieldInvalid` (Error, spec parse);
there is no dedicated regex-error code (an uncompilable pattern is one
`SpecFieldInvalid` condition). Both of these are valid, one matcher each:

```toml
{ label = "Degree", values = ["Bachelor", "Master"] }
{ label = "Degree", pattern = "^(Bachelor|Master)$" }
```

**Empty `groups`.** `groups` is required but MAY be an empty array (D-104). An empty
array declares no explicit groups; every usable value is therefore unmatched and
follows the selected `unmatched` policy. The group-validity rules above apply only to
entries that are present.

**Labels must be unique.** Authored group labels must be distinct, and — when
`unmatched = "other"` — an authored group whose label collides with the synthetic
`Other` bin is likewise a duplicate. A duplicate authored label is
`ValueGroupsLabelDuplicate` (Error, spec validate); duplicates **never** surface as
`SpecFieldInvalid`. A **pass-through** value merely *observed* to equal an authored
group label is data-dependent, not a static duplicate: it surfaces after
calibration at the plan-phase `FormalAttributeCollision` (§10.7). Repeated
observations of one pass-through value are **idempotent** — one bin, not a
collision.

**Ordinal over value groups.** A `value_groups` discretizer under an `ordinal`
scale (with `unmatched` `skip` or `other`, §12.3) **requires** an explicit
`scale.order` that is a **full permutation of the group labels** — including the
synthetic `Other` when `unmatched = "other"`. `value_groups` with `unmatched =
"passthrough"` **cannot** be ordinal (its bin set is data-discovered, so no
authored order can be a full permutation): `ordinal` + `passthrough` is
`OrdinalNotAllowedWithValueGroupsPassthrough` (Error, spec validate). Ordinal over
groups with an `Other` bin:

```toml
discretizer = { kind = "value_groups", unmatched = "other", groups = [
  { label = "School",    values = ["11th", "HS-grad"] },
  { label = "Undergrad", values = ["Bachelors"] },
  { label = "Postgrad",  values = ["Masters", "PhD"] },
]}
scale = { kind = "ordinal", direction = "ge",
  order = ["School", "Undergrad", "Postgrad", "Other"] }   # full permutation incl. Other
```

**`unknown_value_policy = "include"` under `unmatched = "skip"`.** An unmatched
value is not a domain gap to fill — `value_groups` does not consult
`declared_domain` (D-055) — so `include` has nothing to extend and **behaves as
`warn`**: no bin, `UnknownValueObserved` at Warning, and **no**
`UnknownValuePolicyInclude` (no schema extension occurred).

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

**`order`** *(value-bin and value-group ordinal scales only)*. Declares the natural
order of bin labels, and applies **only** to **value-bin** discretizers (`identity`
/ `free_per_value`) and to **`value_groups`** with `unmatched` `skip` or `other`
(§11.6): there it is **required** for non-numeric labels — always for
`value_groups`, whose group labels are strings — and optional for numeric value-bin
labels (the natural numeric **ascending** order is used if absent, D-096). It **MUST NOT** be present
with a **cut** discretizer (`manual_cuts`, `ordered_cuts`, `equal_width`,
`equal_frequency`), whose bin order is fixed by the cut geometry (§17 rule 3) — the
cut discretizer is the single source of order. An `order` over cut bins is
`OrdinalOrderNotAllowedWithCuts` (Error, spec validate); a value-bin or value-group
ordinal scale that needs `order` but omits it is `OrdinalOrderMissing` (Error), and
an `order` entry not among the bin/group labels is `OrdinalOrderHasUnknownValue`
(Error). `order` lists the **raw** bin values or **group labels** (never display
labels) and must be a **full permutation** of them — a label with no `order` entry
is likewise `OrdinalOrderMissing`, and for `unmatched = "other"` the synthetic
`Other` must appear in `order`. For a **numeric** value-bin `order`
(`free_per_value`), entries are parsed under `binding.locale` to the same
normalized identity as the bins (so `90`, `90.0`, `9e1` are one key); an
**invalid** (unparseable/non-finite) or **normalization-duplicate** numeric
`order` entry is `OrderDomainInvalid` (Error, spec validate), while a valid entry
not among the bins stays `OrdinalOrderHasUnknownValue` (D-096). In M2 this path executed for `identity` with an
explicit **string** `order` only; numeric value bins (`free_per_value`) activated at
M4 Slice B (D-101) — with the natural-numeric-ascending default when `order` is absent
— and ordinal `value_groups` activated at M4 Slice E (D-104), where the permutation
universe is the **group labels** (plus the synthetic `Other` under `unmatched =
"other"`), never `declared_domain`, which `value_groups` does not consult (D-055).

**`drop_top`** *(default `false`)*. The "top" formal attribute (the one
true for everything in `direction = "ge"` — i.e., `≥<lowest>`, and `≤<highest>`
for `direction = "le"`) is tautological for objects with non-missing data. Set
`drop_top = true` to suppress it. The lattice's supremum is unaffected; only the
explicit formal attribute is omitted. Over **value** bins under a **strict**
`boundary` there is no tautological threshold — the extreme threshold (`>{highest}`
/ `<{lowest}`) is instead statically empty and is **kept** (an empty column is
legal, §10.1) — so `drop_top` is a no-op there.

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
authored** `boundary` requesting the straddling combination (`le`+inclusive or
`ge`+strict) is invalid → `OrdinalBoundaryIncompatibleWithCuts`
(Error, **spec validate**). "Explicitly authored" means authored **on the
attribute, or arriving as a winning field from an applied template or matcher**
(§9.2): configuration applied through a template behaves exactly as though it had
been written on the attribute, so a template-supplied `ge` + `strict` over cut
bins trips this check just as an explicit one does. A boundary **filled from
`[defaults].ordinal_boundary`** after the effective scale is selected remains
**defaulted, not authored**, never selects the operator over cut bins, and never
trips this check. The reader/writer preserves whether `boundary` was
authored or defaulted (§6) — both so the round-trip stays faithful and so this
check fires only on the authored case. **Over value bins** (`identity` /
`free_per_value` with an authored `order`, a numeric `free_per_value` with the
derived natural numeric order, or `value_groups` with an authored group-label order)
there is no half-open geometry, so all four
`direction × boundary` combinations are well-defined and `boundary` is fully live;
this value-bin ordinal path landed at M2 for `identity` (string, explicit order),
extended to `free_per_value` at M4 Slice B (string requires an explicit order; numeric
uses an authored or natural-ascending order, D-101), and to `value_groups` at M4
Slice E (group labels always require an explicit order — they are strings, so there is
no natural order to derive, D-104).

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
   overriding the base's per-field. The **nested tables** — the triple
   `columns` role map and `[binding.object_key]` — override as **whole
   values**: an authored current table replaces the base's entirely
   (field-mixing a key mode from one file with columns from another, or
   partially remapping triple roles into silently duplicated indices, would
   compose incoherent hybrids — D-078), unlike `[output]`'s per-leaf merge
   (rule 6).
2. `[defaults]` fields are unioned per-field, current overrides base.
3. `[[template]]` entries from both are concatenated. If two templates
   share an `id`, the current spec's wins: it replaces the base's entry **in
   place** (base position kept, mirroring rule 5). This is **carrier
   composition only** — how the document lists merge — and is distinct from
   template resolution precedence, which §9.2 defines over the **composed**
   document.
4. `[[matcher]]` entries from both are concatenated. Order: base
   matchers, then current matchers (so a current matcher's template layers over
   a base matcher's for any field both author — §9.2).
5. `[[attribute]]` entries merge by `name`, **position-preserving**: a base
   attribute keeps its original position; a derived attribute with the same
   `name` replaces it **in place** (whole-attribute replacement, no field-level
   merge — too error-prone, so inherited fields *including* `restrict_to` are
   dropped unless the override repeats them); a derived attribute with a new
   `name` is appended after all inherited attributes. To suppress an inherited
   attribute, override it with `include = false` — and, replacement being
   whole-attribute, repeat at least `name` and `source`. Attribute order is
   column order (§17 rule 1), so position-preserving override keeps a derived
   spec's column order stable when it only re-tunes inherited attributes.
6. `[output]`, `[output.cxt]`, and `[output.dat]` merge **per leaf field**
   (current overrides base field-by-field; a base `[output.cxt]` line-ending and
   a derived `[output.cxt]` trailing-newline both survive).
7. `[provenance]` from the current spec wins (provenance is per-spec,
   not inherited).
8. `[spec]` is **per-spec**: the composed `version`, `description`, and any
   stored fingerprints are the current (most-derived) spec's, and `extends`
   is consumed by composition. A base's `[spec]` contributes nothing — but
   **every spec in the chain MUST itself declare `version = 1`** (§2), checked
   per file as the chain is composed (the referencing spec before any base
   loads, each base at its load — D-078), so a wrong-version file can never
   smuggle content into a v1 composed spec.

Multi-level `extends` is allowed (a chain); the merge above is applied at **each**
step, base-most first. A referenced base spec that cannot be found is
`SpecExtendsNotFound` (Fatal); cycles MUST be detected and rejected with
`SpecExtendsCycle` (Fatal).

Cycle detection compares **canonical file identities** owned by the host source
(D-078): the M7 file-backed host resolves **actual filesystem identity** where
available — unifying supported symlink/hardlink aliases as well as `.`/`..` and case
spellings of one file — and otherwise falls back to normalized full paths compared
with the actual volume/platform behavior; the fallback makes **no link-alias
guarantee** beyond what the host filesystem exposes (D-122). Identity keys are
internal — never output bytes, fingerprint inputs, or manifest content. Authored
references remain relative-only; an absolute reference stays the existing not-found
outcome.

**Template references are late-bound.** Because composition (rule 3) completes
before §9.2 resolution runs, a same-`id` template replacement is resolved
**late**: an **inherited base matcher** — and an inherited attribute's
`template = "..."` reference — resolves against the **composed winning**
template, not the base's original body. Base and derived matchers otherwise
retain their composed order (rule 4). This is a consequence of compose-then-
resolve, not an additional merge rule: overriding a template by `id` in a derived
spec re-targets every reference to it, throughout the composed document.

Fingerprints are computed over the *resolved* (fully merged) plan, not the source
files, so a derived spec and an equivalent flat spec fingerprint identically. Any
fingerprint fields stored in a **base** spec's `[spec]` block are ignored when
resolving a derived spec and recomputed for the resolved result.

**Writing commands treat a chain by purpose** (D-122): `calibrate` writes one
standalone flattened spec with `extends` consumed; `fingerprint --write` preserves
the root's `extends` and changes only its stored fingerprint fields semantically.

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
  `duplicate_object_policy` (which shapes which objects appear and in what order;
  and `restrict_to`, encoded as the canonical `restrictions` container defined
  below, §10.4); and the conversion-affecting binding/source settings —
  binding shape, the **resolved** column/predicate mappings (for triple, the
  resolved role→column-index map; a role bound by header name and the equivalent
  index bind hash identically, §5.3 — the triple `ordering` field is **not** a
  fingerprint input, since `subject_grouped` and `unordered` emit identical
  first-appearance bytes: an acceptance/streaming property, cf. `size_advisory_bytes`),
  `encoding` (a real input from
  M3, D-082 — UTF-8 specs keep their prior hash), `has_header`, `delimiter`,
  `quote_char`, `missing_token`, source `value_type`s, `missing_policy`,
  `unknown_value_policy`, `binding.locale`, object-key mode, and discretizer/scale
  configuration.
- **`cxt_output_fingerprint` adds** the `.cxt`-only settings: the rendered
  formal-attribute names (`formal_attribute_format`, `display_name`,
  `value_labels`), the bin-label style (the `--v2-compat` cut-label transform) and
  `bin_label_unicode`, and the `.cxt` writer settings (line endings,
  `trailing_newline`).
- **`dat_output_fingerprint` adds** only the `.dat` writer settings: `base_index`,
  line endings, trailing-space settings, and `trailing_newline` (§18.2). The
  `trailing_newline` key is encoded into the canonical JSON only when disabled
  (`false`); omitting it at the default `true` keeps every pre-D-087 stored `.dat`
  hash byte-identical. Rendered names and `.cxt`-only settings never affect `.dat`
  (it carries numeric IDs, not names).

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
strings: the cut-bin object's `lo_open`/`hi_open` booleans mean **unbounded
end** — the bin runs to ±∞ on that side — never interval inclusivity, since
every bounded cut bin is uniformly half-open `[lo, hi)` (§11.2); `<30`
therefore carries `hi_open = false`. A fingerprint value is the string
`sha256:` followed by 64 lowercase hex characters of the SHA-256 over the
canonical UTF-8 bytes (D-077).

**`restrict_to` → the `restrictions` container.** `restrict_to` is encoded as a
`restrictions` array in the **shared** portion of the structure (feeding **both**
output fingerprints; **excluded** from `schema_fingerprint`, which hashes columns,
not rows). It sits after `attributes` and `binding` in `shared`'s sorted key order,
is present **only when non-empty**, and — as an explicit **exception** to the
planned-order rule above — its arrays are **canonically sorted**, not left in
planned order, so restriction order is immaterial. Each **restriction object**
carries the attribute's resolved `source` reusing the per-attribute source encoding
(`{"predicate":<name>,"value_type":<type>}` or
`{"column":<index>,"value_type":<type>}`, D-077 — no new source vocabulary), its
`entries` array, and the attribute's resolved `unknown_value_policy` (serialized
`{"entries":[…],"source":{…},"unknown_value_policy":<string>}`, keys sorted
`entries` < `source` < `unknown_value_policy`):

- string exact `{"value":<string>}`; numeric exact `{"value":<number>}` (the number
  via the canonical formatter above, so `30`, `30.0`, `3e1` encode identically);
  numeric range `{"from":<number|null>,"to":<number|null>}` with **both** keys
  always present and an omitted bound as `null` (`{}` → `{"from":null,"to":null}`);
- `entries` are sorted by their complete canonical JSON using **ordinal**
  comparison, and **canonically-identical entries deduplicate** (so `{value = 30}`
  and `{value = 30.0}` collapse); overlapping-but-non-identical ranges are **not**
  merged;
- restriction objects (AND across attributes) are likewise sorted by their complete
  canonical JSON with **exact duplicates removed**. Filter-only attributes
  (`include = false` + `restrict_to`) contribute their restriction object here — not
  through the included-attribute column encoding.

**Ordinal here means UTF-16 code units, compared before UTF-8 encoding** (P-12's
definition; decisions.md D-105). The sort is applied to the canonical JSON **strings**,
never to their encoded bytes: the two orders diverge between a BMP character at or above
U+E000 and a supplementary character (U+E000 is one code unit `0xE000`, above U+1F600's
lead surrogate `0xD83D` — yet its UTF-8 lead byte `0xEE` sorts *below* `0xF0`). Sorting
encoded bytes would therefore hash the same spec differently.

**The `unknown_value_policy` key** is on **every** restriction object, uniformly.
`unknown_value_policy` is live, abort-affecting configuration on a **filter-only**
attribute (§10.4/§10.6: an unparseable filtered value is an Error under `fail` and a
Warning under `warn`), and a filter-only attribute contributes no entry to
`attributes` — without the key, two specs that behave differently would hash
identically. For an included-and-restricted attribute the value therefore appears both
here and in `attributes`: deliberate encoding redundancy for one uniform object shape,
not a double-counted input (nothing is summed).

**Canonical sorting and deduplication are a fingerprint projection only.** The TOML
document, the resolved spec, the plan, and emit all preserve authored order and
duplicates; only this encoding sorts and collapses them, because restriction order and
repetition are semantically immaterial (entries OR, restrictions AND).

**M4 discretizer encodings (`shared.attributes[].discretizer`).** Each attribute's
resolved discretizer is encoded in `shared` under the conventions above (UTF-8 no
BOM, compact JSON, keys sorted ordinal, `kind` a key, TOML enum spellings, the
canonical number formatter). The M4 discretizer kinds encode:

- `free_per_value` → `{"kind":"free_per_value"}` (its numeric-vs-string identity
  rides on `source.value_type`, already in `source`);
- `equal_width` → `{"bins":<int>,"kind":"equal_width","precision":<precision>,"range":<string>}`,
  adding `"vmax":<number>,"vmin":<number>` **only** when `range = "manual"`; the
  `<precision>` value mirrors its two TOML forms — the string `"exact"` or the object
  `{"round_to":<number>}`;
- `equal_frequency` → `{"bins":<int>,"cut_placement":<string>,"kind":"equal_frequency","tie_policy":<string>}`;
- `value_groups` → `{"groups":[…],"kind":"value_groups","unmatched":<string>}`, with
  `groups` in **declaration order** (significant — first match wins, §11.6 — so it is
  **not** sorted) and each group `{"label":<string>[,"pattern":<string>][,"values":[…]]}`
  (group keys sorted `label`/`pattern`/`values`; `pattern`/`values` present **only
  when authored**; the inner `values` array preserves **authored order, duplicates
  retained** — the arrays-in-planned-order default, only `restrictions` sort).

**Effective bins, authored kind.** Output fingerprints hash the **effective**
planned bins/cuts/columns/order — the resolved `bin` objects already in the
`schema` array — and the **effective** (calibrated / `include`-extended) domain; a
data-calibrated discretizer's **resolved cuts are not re-encoded** in its
`discretizer` sub-object (they are already schema `bin` objects, so duplicating
them would be redundant). That sub-object carries the **authored** kind and
configuration only. Because it feeds the two **output** fingerprints (via `shared`)
but **not** `schema_fingerprint` (columns only), an auto discretizer and its
`calibrate`-frozen `manual_cuts` form share a `schema_fingerprint` and emit
**byte-identical** contexts (§7, D-088), yet may legitimately carry **different**
`cxt`/`dat` output fingerprints — sound, because a shared output fingerprint
implies identical bytes but not the converse. These M4 canonical bytes and their
SHA-256 vectors are **golden-locked before the first M4 fingerprint is produced**
(the D-069 → Slice-E precedent). See decisions.md D-094.

**Stored only for fully-frozen specs.** Tooling writes the stored fingerprints
only when the spec is fully determined by its own text — no observed-domain
calibration (an **omitted** `declared_domain` where a discretizer consumes it —
`identity` / `free_per_value`; an **authored** domain, including an explicit `[]`,
is complete and frozen-eligible, D-122), no **data-calibrated discretizer
configuration**
(`equal_frequency`, or `equal_width` with a data-derived `range`;
`equal_width` `range = "manual"` is spec-determined and does **not** disqualify),
no `unknown_value_policy = "include"`, and no `value_groups`
`unmatched = "passthrough"`. A spec needing any of these is data-dependent, so a
stored hash would be invalidated by the next dataset; such runs record the
**effective** fingerprints in the run manifest (§15) instead.

`restrict_to` does **not** disqualify a spec: its entries are authored text, and §7
computes calibration and the column vocabulary over the input universe *before*
restriction selects objects — so a restricting spec is fully determined by its own text
and can be frozen. (This clause previously excluded `restrict_to` only because its
execution was unimplemented, which would have let a stored hash be invalidated by the
feature landing; that exclusion retired with M4 Slice F, D-105.)

**A `probe` draft never stores fingerprints.** A draft generated by Discovery (§7.1) is a
starting point for curation, not a frozen artifact, so tooling writes **none** of the three
stored fingerprints into it — even when its explicit domains would otherwise make it
fully-frozen-eligible. Freezing a draft (via `fcabedrock calibrate`, or by an authoring pass
that stores hashes) is a deliberate later step, after the user has reviewed it.

**Writing the stored fingerprints (M7 commands, D-122).** `fcabedrock calibrate`
produces a fully frozen spec and writes all three native fingerprints; rerunning it
on its own output is **byte-idempotent**, and stale stored values in the input warn
(`*FingerprintStale`) and are corrected in the output. `fcabedrock fingerprint SPEC
DATA` recomputes and reports the three native values with each stored field's
`match`/`stale`/`absent` state, and `--write --out NEW_SPEC` writes a corrected
canonical copy — **only** for a spec meeting the fully-frozen gate above. Both
commands share one semantic gate and write path; **neither writes in place**. On an
`extends` chain, calibrate flattens, while fingerprint-write preserves the root's
`extends` and changes only the stored fingerprint fields semantically (§13).
Effective override hashes are never stored in a spec (they remain manifest facts,
below), and `fingerprint` takes no `--v2-compat` (D-011 keeps that flag
convert-only).

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
fingerprints under `--v2-compat` is expected, not an error. Verification is
defined where a plan is computable — the resolved spec planned with its native
settings; a spec that fails resolve or plan reports those failures instead
(D-077). The `schema_fingerprint` is unaffected by output-only CLI overrides.

## 15. Run manifest

When `convert --out BASE` runs, a sidecar **`BASE.manifest.toml`** is emitted **by
default** — **one manifest per run**, whatever `--format` selected. For a
manifest-bearing run the manifest publishes **last** and is the run's **public commit
marker** (§16.2, D-122): staged residue without it is uncommitted. `--no-manifest`
suppresses this audit sidecar **only** — implementation-private transaction state
then marks the run incomplete until the complete requested artifact set commits,
disappears only on success, and preserves identical incomplete-run detection
(D-122). Failed, cancelled, or invalid runs **leave no committed run**; commit-phase
failures roll back best-effort, and surviving residue remains uncommitted and is
detected, reported, and safely cleaned on a later collision.

```toml
[run]
tool_version           = "fcabedrock-vnext 1.0.0"
timestamp              = 2026-07-22T12:34:56Z
command_line           = ["fcabedrock", "convert", "adult.toml", "adult.csv", "--out", "adult", "--format", "both"]
spec_path              = "adult.toml"
spec_file_hash         = "sha256:..."
schema_fingerprint     = "sha256:..."
cxt_output_fingerprint = "sha256:..."
dat_output_fingerprint = "sha256:..."
input_path             = "adult.csv"
input_hash             = "sha256:..."

[[run.outputs]]
format = "cxt"
path   = "adult.cxt"
hash   = "sha256:..."

[[run.outputs]]
format = "dat"
path   = "adult.dat"
hash   = "sha256:..."

[[run.calibrations]]
attribute   = "age"
kind        = "cuts"
discretizer = "equal_frequency"
cuts        = [38, 49, 52]

[[run.calibrations]]
attribute = "education"
kind      = "observed_domain"
values    = ["Bachelors", "HS-grad"]

[[run.calibrations]]
attribute = "workclass"
kind      = "include_additions"
values    = ["Assoc"]

[[run.calibrations]]
attribute = "industry"
kind      = "passthrough_bins"
values    = ["Self-emp"]
```

The manifest captures everything needed to reproduce the conversion exactly.
`timestamp` and `command_line` are the audit fields, recorded for the audit trail and
**not** fingerprint inputs. The per-format fingerprint fields are present **iff** that
format was written, and are the **effective** values (after any CLI override such as
`--v2-compat`), so they may differ from the spec-stored native values (§14).
`spec_file_hash` is the raw TOML bytes, distinct from the canonical, plan-derived
`schema_fingerprint`. `input_hash` is the raw input bytes — every complete pass hashes
them inline and a replay hash must match before commit (§17, D-122).

**`[[run.spec_files]]`** is present **only** when the spec is an `extends` chain (the
example above has none). It sits between `[[run.outputs]]` and `[[run.calibrations]]`,
carries one entry per chain file **root-first, then bases**, and each entry lists its
fields in the fixed order **`path`, then `hash`**.

**Path semantics.** `spec_path` and `input_path` are the **verbatim command
operands**. Each `[[run.outputs]] path` is the **invoked output-base spelling plus the
ruled extension** (§8/D-122), never normalized. Chain entries record the root operand
spelling and each authored referrer-relative `extends` spelling with its raw file
hash — canonical filesystem identity keys (§13) never enter the manifest.

**`[[run.calibrations]]`** is present **iff** any calibration outcome was retained. It
holds **one entry per calibrated attribute, which retains exactly one outcome** (the
closed `AttributeCalibration` union): `attribute`, `kind` ∈ {`cuts`,
`observed_domain`, `include_additions`, `passthrough_bins`}, and that kind's
variant-specific fields, in **spec-attribute order**, with legitimate zero-discovery
outcomes as explicit empty arrays (e.g. `values = []`). It records **all four**
retained outcome kinds completely (D-122; this resolves the roadmap's M4 manifest
deferral). **Array wrapping:** only long non-cut `values` arrays use the D-113
deterministic wrapping; `command_line`, `cuts`, and every other array remain inline.

**Serialization.** Manifest bytes are canonical and fully determined: UTF-8 without
BOM, LF line endings, the fixed field and section order shown, and the **same
canonical TOML literal conventions as the spec writer** (D-075/D-113) — basic
double-quoted strings with the pinned escape set, invariant shortest round-trippable
numbers (an integral cut renders `38`, never `38.0`), `key = value` with single
spaces, arrays in the documented single-line style with only long non-cut calibration
`values` arrays wrapping per D-113, and **no comments**. `timestamp` is whole-second
RFC 3339 UTC from an injected clock; `command_line` is the argv array verbatim;
`tool_version` is the single version string `--version` prints. Only `timestamp` and
`command_line` are audit-variable; every other field is a deterministic fact of the
run — a path spelling varies only when its **source** varies: the argv operands for
`spec_path`/`input_path`/output paths, the authored referrer-relative `extends`
spellings for chain entries. Concrete serializer ownership stays an
implementation-plan choice.

Citing a manifest in a paper is sufficient for reproducibility audits.

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

**Artifact validity (normative).** A conversion's output artifact is valid **only if**
the run's collected diagnostics contain **no Error and no Fatal**. On any Error/Fatal
**the caller MUST discard the output**, whatever was written. Writers emit to
caller-owned sinks and cannot retract bytes (§15/§18.1, P-15), so this is not something
the writer can enforce for the caller. An invalid run reaches that state one of **two**
ways, and they leave different bytes on disk:

- a **structural or grouping-storage halt** (an invalid object key, a non-contiguous
  `subject_grouped` subject, an in-path spool failure) stops the object stream, so both
  `.cxt` passes truncate **identically** — leaving a structurally well-formed but
  **truncated** file the object-name-sequence invariant (§18.1) cannot detect, and a
  `.dat` holding only the rows written before the halt;
- a **policy abort** (`unknown_value_policy = "fail"` meeting an unparseable value,
  §10.6/§10.4) does **not** stop enumeration: the aggregated per-attribute diagnostic
  (§16.4) requires reading the whole population, so the stream **completes**, the Error
  flushes at the end, and the output is fully written — a **complete but invalid**
  `.cxt`, and a `.dat` holding every survivor. "Abort" here is Error's operation-failed
  semantics, not "stop reading rows".

Either way the artifact is invalid and the caller must discard it. For `.cxt` the
diagnostics are authoritative only **after** the replay session is disposed, which is
when cross-pass aggregates are flushed. (decisions.md D-105.)

The M7 CLI realizes this discard through **staged publication**: artifacts stage on
the destination filesystem; a failed, cancelled, or invalid run **leaves no committed
run**; files commit atomically one-by-one with best-effort rollback; for a
manifest-bearing run the manifest publishes **last** as the public commit marker, and
under `--no-manifest` implementation-private transaction state preserves the same
incomplete-run detection (§15, D-122). **Exit codes:** 0 success (warnings included),
1 any Error/Fatal diagnostic or a host/runtime/publication failure, 2 usage, 3
cooperative cancellation, 4 unexpected internal fault (D-122).

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
(decisions.md D-067). The registry covers **pipeline** conditions only: ordinary
host/environment failures (a missing or unreadable input, permissions, an output or
publication failure) are **CLI-owned, code-less** errors on stderr with exit 1, and
never join this registry (D-122). Existing phase-owned conditions such as
`SpecExtendsNotFound` remain registry diagnostics. v1's initial set:

| Code | Severity | Where |
| --- | --- | --- |
| `SpecVersionUnsupported` | Fatal | spec resolve |
| `SpecTomlInvalid` | Fatal (parser warnings surface as Warning) | spec parse |
| `SpecKeyUnrecognized` | Error | spec parse |
| `SpecFieldInvalid` | Error | spec parse |
| `SpecSurfaceNotYetSupported` | Error | spec parse (transitional) |
| `SpecExtendsCycle` | Fatal | spec resolve |
| `SpecExtendsNotFound` | Fatal | spec resolve |
| `BindingShapeMissing` | Error | spec validate |
| `BindingLocaleInvalid` | Error | spec validate |
| `AttributeNameDuplicate` | Error | spec validate |
| `AttributeNameMissing` | Error | spec validate |
| `AttributeScalingMissing` | Error | spec validate |
| `DiscretizerCutsNotAscending` | Error | spec validate |
| `DiscretizerCutsTooFew` | Error | spec validate |
| `EqualWidthRangeInvalid` | Error | spec validate |
| `EqualWidthCutsCollapsed` | Error | spec validate |
| `ValueLabelKeyNotInDomain` | Error | spec validate |
| `ValueLabelKeyDuplicate` | Error | spec validate |
| `DeclaredDomainInvalid` | Error | spec validate |
| `SourceValueTypeInvalid` | Error | spec validate |
| `RestrictToNumericEntryRequired` | Error | spec validate |
| `RestrictToRangeInvalid` | Error | spec validate |
| `RestrictToValueNotInDomain` | Warning | spec validate |
| `FormalAttributeCollision` | Error | plan |
| `FormalAttributeNameCollision` | Error | plan |
| `FormalAttributeNameInvalid` | Error | plan |
| `OrdinalOrderMissing` | Error | plan |
| `OrdinalOrderHasUnknownValue` | Error | plan |
| `OrdinalOrderNotAllowedWithCuts` | Error | spec validate |
| `OrdinalNotAllowedWithValueGroupsPassthrough` | Error | spec validate |
| `ScaleNotImplementedV1` | Fatal | plan |
| `ObjectKeyCompositeNotImplementedV1` | Fatal | plan |
| `ObjectKeyBindingInvalid` | Error | spec validate |
| `ObjectKeyModeInvalidForShape` | Error | spec validate |
| `TripleColumnsNotDistinct` | Error | spec validate |
| `DateValueTypeNotImplementedV1` | Fatal | plan |
| `ObservedDomainUsed` | Warning | calibrate |
| `CalibrationDataInsufficient` | Error | calibrate |
| `CalibrationCutsInvalid` | Error | calibrate |
| `CalibrationPopulationTooLarge` | Error | calibrate |
| `UnknownValueObserved` | Warning or Error (per `unknown_value_policy`) | calibrate/emit |
| `UnknownValuePolicyInclude` | Warning | calibrate |
| `TripleSubjectNotContiguous` | Error | probe/calibrate/emit |
| `DuplicateObjectKey` | Error, Warning, or Info (per `duplicate_object_policy`) | emit |
| `ObjectKeyNameDisambiguated` | Warning (aggregated) | emit |
| `ObjectKeyValueInvalid` | Error | probe/calibrate/emit |
| `GroupingStorageFailed` | Error (in-path / escalated), or Warning (cleanup-only) | calibrate/emit |
| `SourceValueUnparseable` | Warning or Error (per `unknown_value_policy`; `skip` silent) | calibrate/emit |
| `QuoteCharNotSupportedV1` | Error | spec validate |
| `BindingDelimiterQuoteConflict` | Error | spec validate |
| `SourceBindingInvalid` | Error | spec validate |
| `OrderedCutsCutNotInDomain` | Error | spec validate |
| `OrderedCutsNotAscending` | Error | spec validate |
| `OrderDomainInvalid` | Error | spec validate |
| `DiscretizerEndsClosedTooFewCuts` | Error | spec validate |
| `OrdinalBoundaryIncompatibleWithCuts` | Error | spec validate |
| `ValueGroupsLabelDuplicate` | Error | spec validate |
| `ValueGroupsPassthroughDataDependent` | Warning | calibrate |
| `TemplateIdMissing` | Error | spec resolve |
| `TemplateIdDuplicate` | Error | spec resolve |
| `TemplateReferenceUnknown` | Error | spec resolve |
| `MatcherSelectorInvalidForShape` | Error | spec resolve |
| `MatcherSelectsNoAttributes` | Warning | spec resolve |
| `MatcherFullyShadowed` | Warning | spec resolve |
| `SchemaFingerprintStale` | Warning | spec load |
| `CxtOutputFingerprintStale` | Warning | spec load |
| `DatOutputFingerprintStale` | Warning | spec load |
| `NoFormalAttributes` | Warning | plan |
| `NoObjectsEmitted` | Warning | emit |
| `AttributeHasNoCrosses` | Warning (aggregated) | emit |
| `ObjectHasNoCrosses` | Warning (aggregated) | emit |
| `OutputCxtSizeAdvisory` | Warning | export |
| `BedStructureInvalid` | Fatal | migrate (v2) |
| `BedDateTypeNotSupported` | Error | migrate (v2) |
| `BedTypeUnrecognized` | Error | migrate (v2) |
| `BedAttributeConfigInvalid` | Error | migrate (v2) |
| `BedParkedConfigDropped` | Warning | migrate (v2) |
| `BedMissingTokenLabelDropped` | Warning | migrate (v2) |
| `ProbeSourceReadFailed` | Error | probe |
| `ProbeNoAttributesDiscovered` | Error | probe |
| `ProbeAttributeNameAdjusted` | Warning (aggregated) | probe |
| `ProbeDomainTruncated` | Warning (aggregated) | probe |
| `ProbeLimitExceeded` | Error | probe |

`EmptyExtent` / `EmptyIntent` were dropped in favor of the unambiguous,
correctly-phased `AttributeHasNoCrosses` (an empty column, emit) and
`ObjectHasNoCrosses` (an empty row, emit); whole-context emptiness is
`NoFormalAttributes` (zero columns, plan) and `NoObjectsEmitted` (zero rows after
filtering, emit). All four still write a structurally-valid (if degenerate)
output rather than failing.

**Transitional codes.** A transitional code is emitted only by milestones *before*
the feature's implementation milestone; it is removed once the feature lands and is
**not** part of the v1 end-state set. **No milestone transitional remains.**
(`TemplateMatcherNotImplementedV1` — owned by spec resolve, since templates and
matchers never resolve into Core (D-078) — retired at **M6 Slice B** (D-121) when
the application path landed: templates and matchers now execute, so the six named
spec-resolve rows above replaced the single reject.
`ObjectKeyColumnNotImplementedV1` retired when wide `dedupe`
landed at M3 Slice F; `ObservedDomainCalibrationNotImplementedV1` retired when
observed-domain calibration landed at M4 Slice A — D-098, so an absent
`declared_domain` under a consuming discretizer is now filled by the Calibrate
phase, §10.3; `RestrictToNotImplementedV1` retired when `restrict_to` execution
landed at M4 Slice F — D-105, **M4's last transitional code**, so a `restrict_to`
now filters objects rather than rejecting the conversion, §10.4.) They are distinct
from the permanent `*NotImplementedV1`
reservations in §20. (`DiscretizerKindNotYetSupported` — a recognized-but-deferred
discretizer kind rejected at read with no parameter carrier, D-070 — was
transitional on the same terms and **retired at M4 Slice E**, D-104: the set
narrowed as each kind landed (`free_per_value` at Slice B, D-101; `equal_width` at
Slice C, D-102; `equal_frequency` at Slice D, D-103, which also made `equal_width`'s
`range = "percentile_p1_p99"` spelling accepted) and emptied with `value_groups`, its
last owner. Every §11 discretizer kind now has a carrier and executes, so an
unrecognized kind spelling is an ordinary `SpecFieldInvalid`.) One parse-phase code
remains transitional: `SpecSurfaceNotYetSupported`, now carrying **exactly one**
recognized-but-unmodelled surface — **`value_type = "date"`**. The
extends/template/matcher entries were retired by their Slice F carriers (D-078),
and the **naming carriers** (`display_name`, `formal_attribute_format` on
`[[attribute]]`/`[[template]]`, `formal_attribute_format` on `[defaults]`) retired
at **M6 Slice A** when they gained real carriers (D-120) — with them the closed
per-table deferred-**key** sets are gone entirely, so the surviving owner is a
*value-level* reject inside the source reader rather than a key. The row retires
when the D-038 date carrier lands and hands over to the permanent plan-phase
`DateValueTypeNotImplementedV1`.

**M6 retirement schedule — complete.** The M6 template/matcher and naming contract
(decisions.md D-114…D-119) has landed in full. **The naming half landed at M6
Slice A (D-120):** the `display_name` / `formal_attribute_format` portion of
`SpecSurfaceNotYetSupported` retired, and that code now carries **only**
`value_type = "date"` until the D-038 carrier hands over to
`DateValueTypeNotImplementedV1`. **The application half landed at M6 Slice B
(D-121):** `TemplateMatcherNotImplementedV1` is **removed entirely** — member,
emit sites, and registry row — so **no M6 transitional remains**.

**The permanent M6 conditions — all seven named and live.** M6 introduced the
invalid states below. The seventh landed with its emit site at M6 Slice A as
`FormalAttributeNameInvalid` (D-120); the remaining six landed as **spec-resolve**
conditions with template/matcher application at M6 Slice B (D-121), each named in
the table above, following this registry's standing rule that a code joins the
enum with its emit site (D-085). Each condition's **owner phase, severity, and
granularity** is as settled in decisions.md D-116:

| Condition | Code | Where | Severity | Granularity |
| --- | --- | --- | --- | --- |
| `[[template]]` without an `id` | `TemplateIdMissing` | spec resolve | Error | one per template, in composed template order |
| duplicate template `id` in the composed document | `TemplateIdDuplicate` | spec resolve | Error | one per extra declaration, in composed template order |
| reference to an unknown template `id` | `TemplateReferenceUnknown` | spec resolve | Error | one per referencing matcher or attribute site |
| `source_index_range` under `shape = "triple"` | `MatcherSelectorInvalidForShape` | spec resolve | Error | one per incompatible matcher |
| matcher selecting zero attributes | `MatcherSelectsNoAttributes` | spec resolve | Warning | one per matcher |
| fully-shadowed matcher (§9.2) | `MatcherFullyShadowed` | spec resolve | Warning | one per matcher |
| invalid rendered formal-attribute name (empty, or containing CR/LF) | `FormalAttributeNameInvalid` | plan | Error | one per affected logical attribute |

`FormalAttributeNameInvalid` is **aggregated per logical attribute**: its message
carries the offending-name count plus a bounded sample of at most three rendered
names in render order, each quoted and escaped (backslash, double quote, CR, LF),
with a `(+N more)` tail when truncated — a pinned representation, so two runs and
two machines emit byte-identical messages (§17).

An unknown-reference diagnostic on an **attribute** carries the `AttributeName`
location; a matcher- or template-scoped diagnostic identifies its declaration and
its `id`/reference deterministically (by 1-based composed declaration ordinal). The
rendered-name Error sits alongside `FormalAttributeNameCollision` at plan (§10.7).

The two matcher **Warnings** are selector- and merge-level facts, so neither is
suppressed by an Error elsewhere on the same matcher: a matcher whose template
reference is unknown, or whose range is incompatible with `shape = "triple"`, still
reports `MatcherSelectsNoAttributes` when its selector chose nothing. A matcher
qualifies for **at most one** of the two, since `MatcherFullyShadowed` requires at
least one selected attribute.

**M6 static shape reuses `SpecFieldInvalid`** — no new parse-phase code is added
for: an invalid template-`id` grammar (§9.1), a matcher with no `template`
reference, an empty or uncompilable `name_regex`, a malformed
`source_index_range` (wrong arity, non-integer, negative, or reversed), both or
neither matcher selector, and the §10.7 naming-shape failures (unknown/empty
placeholder, unmatched or malformed brace, empty format string, CR/LF in format
literal text, and an empty or CR/LF-bearing `display_name`).

**Granularity and ordering for applied templates.** An effective attribute
assembled from templates is validated by the **existing** condition owners, and
its structured diagnostics are emitted **one per affected effective attribute, in
attribute declaration order** — never collapsed into a per-template aggregate,
because one invalid effective attribute may draw on several matching templates
plus higher precedence tiers, so the attribute is the only sound owner. A message
**may** name every contributing template/matcher site, and a CLI or UI **may**
group identical diagnostics for presentation, without changing the structured
diagnostic contract. The M7 CLI renders each diagnostic as **one deterministic
stderr line** in the sparse labelled form `file="…" line=N column=N attribute="…"
record=N: severity Code: escaped-message` — only populated location fields, in that
order; string fields as JSON string literals, integers invariant; lowercase
severity; all control characters and literal backslashes escaped; no location prefix
when no field is populated (`warning NoObjectsEmitted: …`); code-less host errors
render `error: escaped-message`. Library order is preserved and M7 does **no**
additional grouping (D-122). Within the resolve phase the deterministic family order
is:

1. template identity (missing / duplicate `id`);
2. matcher reference and shape compatibility;
3. attribute template references;
4. effective-attribute validation;
5. zero-match and fully-shadowed matcher warnings,

with declaration order (matchers, templates) or logical-attribute order preserved
**within** each family. Parse-phase diagnostics retain reader source-position
order.

**The `migrate (v2)` phase** is the one-way `.bed` → TOML migration (D-009/D-079),
a tooling phase outside the §7 processing pipeline. The migrator carries what the
document model can represent and defers semantic validation to the resolve seam,
so only transcription failures own codes here; a migrated spec then flows through
the ordinary parse/resolve/validate/plan phases above. `BedDateTypeNotSupported`
retires if the date carrier lands (D-038).

**The `probe` phase (M5, implemented).** Discovery / `probe` (§7.1) is a draft-generation
operation outside the §7 conversion pipeline. Its five codes above —
`ProbeSourceReadFailed`, `ProbeNoAttributesDiscovered`, `ProbeAttributeNameAdjusted`,
`ProbeDomainTruncated`, `ProbeLimitExceeded` — and the **`probe`** phase-ownership added to
`TripleSubjectNotContiguous` and `ObjectKeyValueInvalid` (both previously `calibrate/emit`,
now `probe/calibrate/emit`) were the M5 registry contract; **all seven sites are now live**
(decisions.md D-111) — the five `Probe*` codes on both shapes, and the two structural codes
at their probe-phase sites on the triple path — with the ownership and semantics below
unchanged. `ProbeSourceReadFailed` is a stream/read failure only and MUST NOT absorb a
structural subject error (that stays `ObjectKeyValueInvalid` / `TripleSubjectNotContiguous`);
`ProbeAttributeNameAdjusted` and `ProbeDomainTruncated` are aggregated (count + bounded
sample), and plain headerless `column_N` synthesis alone is not a warning. Cancellation is
**not** a diagnostic — a canceled probe leaves no document and no diagnostic (§7.1, D-112).

**Aggregation.** Data-phase diagnostics that can fire per value or per object —
`SourceValueUnparseable`, `UnknownValueObserved`, `AttributeHasNoCrosses`,
`ObjectHasNoCrosses` — are emitted **aggregated**: a per-attribute (or per-source)
count with a bounded sample, never one diagnostic per row, so a malformed column
at 73M records does not produce 73M diagnostics.

The `DiagnosticCode` enum is the authority for the codes a build can actually
raise; it grows per slice (P-3), so it holds fewer members than this registry — a
registry row joins the enum when the milestone owning its site lands (D-085). After
**M7 Slice B** the enum has **82** members: 81 after M6 Slice B, plus
`OutputCxtSizeAdvisory` at its export emit site (D-123). Exactly one row remains
outstanding — `DateValueTypeNotImplementedV1` (the D-038 date carrier) — joining
the enum when its own milestone lands. Every other row is live.

## 17. Determinism rules

The following rules are normative and ensure same-spec + same-input ⇒
same-output across runs and across machines.

The "same normalized input" precondition is **verified** by the M7 host: every
complete data pass hashes the raw bytes it consumes **inline**, and a replay whose
hash differs from the first pass fails the run **before any commit** (a code-less
host error; nothing publishes). A genuinely single-pass run records that pass's hash
under an explicit **stable-input precondition** (D-122).

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
     - over **value groups** (`value_groups` with `unmatched` `skip` / `other`):
       ascending order of the authored `scale.order` (§12.3);
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
     - for a **numeric** `free_per_value`, all three orderings above are read over
       the **normalized numeric identities** (§11.3, D-096), so equivalent spellings
       (`90` / `90.0` / `9e1`) occupy one position; a normalization-duplicate
       explicit domain entry is rejected earlier at validate
       (`DeclaredDomainInvalid`, §10.3).
4. **Object order in the output**:
   - **Wide** `row_index`, or `column` under `keep` / `fail` / all-unique keys:
     source **row order**.
   - **Wide** `column` under `dedupe`: **first-occurrence order of each cleaned key
     value** (later duplicates merge onto the first; §6.1) — a generalization of row
     order.
   - **Triple `subject_grouped`**: order of **first appearance** of each subject.
   - **Triple `unordered`**: order of **first appearance** of each cleaned subject
     (interleaved input allowed; the converter groups all rows per cleaned subject).

   Triple `unordered` and `subject_grouped` produce the **same** object order (first
   appearance of each cleaned subject) and differ only in the contiguity requirement:
   `unordered` accepts interleaved input, `subject_grouped` requires contiguity for
   single-pass streaming. Wide `dedupe` follows the same first-occurrence principle;
   none of these sorts its object output.
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

**Object-name sequence invariant (normative).** When the writer replays the object
stream, the two passes MUST yield the **same object-name sequence** — the same count
and the same order. The writer checks each pass-2 object's name against the pass-1
name at its position and fails the write (a structural error; **the caller must discard
the partial output**, §16.2) on a mismatch, overflow, or shortfall, so a
non-deterministic producer cannot silently misalign the header names and the incidence
rows. (Full producer content determinism — that a replay also yields the same
*crosses* — is a separate determinism property, §17; the writer enforces only name/row
alignment.)

**What this invariant does not catch.** It compares the two passes against *each other*,
so it is blind to any failure both passes reproduce identically — of which there are two
kinds, both leaving an invalid `.cxt` the invariant passes over (§16.2):

- a **structural halt** (an invalid object key, a non-contiguous `subject_grouped`
  subject, an in-path spool failure) stops the object stream at the same deterministic
  point in each pass, so the names still align, the write returns, and a structurally
  well-formed but **truncated** `.cxt` results;
- a **`fail`-policy abort** (§10.6/§10.4) does **not** truncate at all: the aggregated
  diagnostic requires reading the whole population (§16.4), so both passes emit the
  **complete** object sequence, the write returns, and a **complete but invalid** `.cxt`
  results.

In both cases the only signal is the Error in the run's diagnostics (§16.2), inspected
after the replay session is disposed. The writer cannot discard the file: the bytes are
already in a caller-owned sink, and a writer that decided what to publish would no longer
be a dumb exporter (P-15). Transactional publication belongs to the **M7
conversion-run host**: staged artifacts, per-file atomic commits, manifest-last
public commit marker (§15/§16.2, D-122).

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

**Trailing newline** after the last line: emitted by default, like `.cxt`
(§18.1). Set `[output.dat] trailing_newline = false` to suppress **only** the
final line terminator; the single-space separators *between* lines are
unaffected. Under `--v2-compat` the final `.dat` newline is **shape-dependent**:
present for a wide source (matching v2's wide converter) but **absent for a
triple source** — v2's triple converter wrote no final `.dat` line terminator.
The conversion orchestrator applies this v2-compat exception per resolved shape;
it is not a separate CLI flag, and native output ignores shape and honors
`trailing_newline` (D-087).

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
ordering = "unordered"
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

This single spec captures: keep only objects that were **observed for Bmp5**,
carry **at least one strongly-detected observation**, and have a Theiler stage in
3–8, then analyze the surviving objects along two emitted dimensions — Tissue
(grouped into Endoderm/Mesoderm) and TheilerStage (four ordinal buckets). Each
restriction filters **whole objects, not observations** (§10.4, D-097): a surviving
object keeps **all** its observations and crosses, not only the matching ones.
`Gene` and `Strength` are the two **filter-only** attributes (`include = false` +
`restrict_to`): they shape *which objects* enter the context without becoming
*columns* in it.
`TheilerStage` is **emitted and restricted** — it is not filter-only. Note its
`equal_frequency` cuts calibrate over the **input universe** before `restrict_to`
filters objects (§7), so the surviving TS 3–8 objects need not span all four
buckets and some columns may end up empty.

Each restriction is **existential** (§10.4): an object survives when **at least
one** observed value matches — a subject whose `Gene` triples include `Bmp5`, whose
`Strength` includes `strongly detected`, and whose `TheilerStage` value lands in
`[3, 9)`; a subject with **no** `Gene` predicate, or a missing value, fails that
restriction and is excluded. `TheilerStage` could instead pin exact stages —
`restrict_to = [{ value = 5 }, { value = 6 }]` keeps only TS 5 and 6 (matched by
parsed numeric identity).

> This example is **executable**: its `value_groups` grouping (M4 Slice E), its
> `equal_frequency` calibration (Slice D), and its `restrict_to` filtering (Slice F,
> D-105) all run. Nothing in it is transitional.

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
Items 12–26 are one-line pointers to the owning spec sections and
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
   (CRLF, `30to<40` labels, trailing space, the shape-dependent `.dat` final
   newline) live behind `--v2-compat` so the native default is uniformly clean.
   The `.dat` **final newline** itself defaults to present (`[output.dat]
   trailing_newline = true`, §18.2).

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

26. **M4 Tier 1 spec-audit additions** → D-088…D-092 (decisions.md); the owning
    sections (§3, §5.3.1, §7, §10.2, §10.3, §10.4, §10.6, §10.7, §11.3, §11.4,
    §11.5, §11.6, §12.3, §14, §16.4, §17, §19.4) are updated in place.

27. **M4 Tier 2 audit additions** → D-093…D-097 (+ D-091 in-place clarifications)
    (decisions.md); the owning sections (§5.1, §7, §10.1, §10.3, §10.4, §10.6,
    §11.4, §11.5, §12.3, §14, §16.4, §17, §19.4) are updated in place.

---

*End of v1 spec schema document. Section count: 21. Sections marked
with diagnostic code `*NotImplementedV1` are reserved syntax; v1 planner
rejects but the parser accepts. The Decisions Log (§21) is informative,
not normative.*
