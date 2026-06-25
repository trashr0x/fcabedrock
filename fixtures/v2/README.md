# v2 compatibility fixtures

These fixtures preserve FcaBedrock v2 compatibility behaviour. The expected
`.cxt` and `.dat` files are golden outputs produced by the original FcaBedrock
v2 tool and must not be edited to make tests pass.

Dataset provenance and attribution for adapted fixtures are documented in
`ATTRIBUTION.md`.

## Layout

Fixtures are grouped by source family. Each family folder holds the `.bed` spec
and input data for every variant of that family, plus a single `expected/`
folder with the golden outputs for all of them:

```text
fixtures/v2/<family>/                  # mini-mushroom, mini-adult, mini-dates
  <variant>.bed                        # one spec per variant
  <variant>.data                       # input data (a variant may reuse another's)
  expected/                            # one shared folder per family
    <variant>.cxt                      # v2 golden output, per variant
    <variant>.dat
```

Notes:

* `<variant>` is the family name for the base case (e.g. `mini-adult`) or a
  suffixed name (e.g. `mini-adult_noheader`, `mini-mushroom_triples`,
  `mini-dates_triples_restricted`).
* All variants of a family share one folder and one `expected/` folder.
* A variant may reuse another variant's `.data` — e.g.
  `mini-dates_triples_restricted` has only a `.bed`, reusing
  `mini-dates_triples.data`.
* The `.data` extension is the UCI-style fixture input extension; the delimiter
  (CSV/TSV) and whether a header row is present are declared in the `.bed` spec,
  not implied by the extension. Treat `.data` files as text fixture inputs.

## Variant matrix

| Fixture variant                 | Source family    | Input shape / behaviour exercised                                             | Activation                              |
| ------------------------------- | ---------------- | ----------------------------------------------------------------------------- | --------------------------------------- |
| `mini-mushroom`                 | Mushroom-derived | Wide delimited input with header; dichotomic + nominal scaling                | M1                                      |
| `mini-mushroom_tabbed_noheader` | Mushroom-derived | TSV input; `has_header=false` / positional columns                            | M1                                      |
| `mini-mushroom_triples`         | Mushroom-derived | Subject-predicate-value triple input                                          | M3                                      |
| `mini-adult`                    | Adult-derived    | Wide delimited input with header; manual numeric cuts on age                  | M1                                      |
| `mini-adult_noheader`           | Adult-derived    | Wide delimited input; `has_header=false` / positional columns                 | M1                                      |
| `mini-adult_employment_ordinal_discrete`    | Adult-derived | Type `n` (`ordered_cuts`) on employment, discrete → nominal; reuses `mini-adult.data`   | M1                  |
| `mini-adult_employment_ordinal_progressive` | Adult-derived | Same `.bed` bytes, progressive → ordinal (le); cumulative `<…`/`all` thresholds | M1                  |
| `mini-adult_triples`            | Adult-derived    | Subject-predicate-value triple input with numeric subjects                    | M3                                      |
| `mini-adult_triples_named`      | Adult-derived    | Subject-predicate-value triple input with named subjects (spec §19.3)         | M3                                      |
| `mini-dates_triples`            | Handcrafted      | Triple input with date values                                                 | Parked — date support deferred by D-038 |
| `mini-dates_triples_restricted` | Handcrafted      | Triple input with date values plus `restrict_to`; shares the dates data shape | Parked — date support deferred by D-038 |

## Activation policy

* M1 activates the wide delimited fixtures needed for v2 byte-compatibility
  coverage.
* M3 activates the triple-input fixtures once the triple source adapter exists.
* `mini-dates*` fixtures are parked until date value handling is implemented.
  They should remain checked in, but should not be part of the active M1 golden
  test set.

The variant matrix above is documentation of test intent. The test harness does
**not** parse this Markdown as a source of truth. If the harness later needs
machine-readable fixture metadata, add a separate manifest or a typed
fixture-case list in tests.

Golden outputs are compatibility evidence. Do not edit expected `.cxt` or `.dat`
files to make a new implementation pass. If vNext intentionally diverges from
v2, record the decision and gate the behaviour behind the appropriate
compatibility mode.
