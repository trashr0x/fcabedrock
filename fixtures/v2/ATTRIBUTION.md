# Fixture attribution

This directory contains small v2 compatibility fixtures used to verify
FcaBedrock vNext behaviour against outputs produced by the original FcaBedrock
v2 tool.

The expected `.cxt` and `.dat` files are golden outputs and must not be edited
to make tests pass.

## UCI-derived fixtures

### `mini-mushroom*`

The `mini-mushroom` fixture family is derived from the UCI Machine Learning
Repository Mushroom dataset.

Citation:

> Mushroom [Dataset]. (1981). UCI Machine Learning Repository.
> <https://doi.org/10.24432/C5959T>

The fixtures are reduced/adapted compatibility examples, including alternate
representations such as headerless/TSV and triple-input forms. They are included
to exercise FcaBedrock input/spec/output behaviour and are not copies of the
full original dataset.

### `mini-adult*`

The `mini-adult` fixture family is derived from the UCI Machine Learning
Repository Adult dataset.

Citation:

> Becker, B. & Kohavi, R. (1996). Adult [Dataset].
> UCI Machine Learning Repository. <https://doi.org/10.24432/C5XW20>

The fixtures are reduced/adapted compatibility examples, including alternate
representations such as headerless and triple-input forms. They are included to
exercise FcaBedrock input/spec/output behaviour and are not copies of the full
original dataset.

## Handcrafted fixtures

### `mini-dates*`

The `mini-dates` fixture family is handcrafted for FcaBedrock compatibility /
regression coverage. It is not derived from an external dataset.

The dates fixtures are parked with the v2 fixtures for future date-handling
coverage, but vNext v1 does not implement date scaling.

## License note

The UCI Machine Learning Repository pages for the Mushroom and Adult datasets
list them under the Creative Commons Attribution 4.0 International license
(CC BY 4.0), which permits sharing and adaptation with appropriate credit.

See the linked UCI dataset pages for the current dataset metadata and license
terms.
