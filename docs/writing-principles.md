# Writing Principles: FcaBedrock vNext

How this repository writes documents and comments. These rules bind everyone who writes here,
human or assistant. They are about *writing*, not about engineering judgment; the engineering
invariants live in `docs/engineering-principles.md`.

Scope boundaries with the other docs:

- `engineering-principles.md` = what must be true of the system.
- **`writing-principles.md` (this file) = how we state anything at all.**
- `decisions.md` = why we chose a specific thing on a specific date.

These rules take selected *structural* practices from ASD-STE100 (Simplified Technical English):
one instruction per step, the condition before the instruction, one topic per paragraph, and
consistent terminology. **This is not ASD-STE100 compliance.** That specification's core is a
controlled dictionary of approved words, and adopting it would force precise project terms such as
*discretizer*, *formal attribute* and *quantile accumulator* into approved synonyms. We keep our
vocabulary and borrow the structure.

No rule in this file is enforced by a tool; the mechanical authored-text integrity command is
documented in `eng/README.md`. A rule here that no reader could act on has failed and should be cut.

---

## WP-1: Keep only useful, current information

Every retained sentence must help a future reader use, change, verify or understand the current
system. Delete text that only records how the project got here: run ledgers, retry narration,
text about obligations that were open at the time, and repeated evidence tables. Git is the
historical record.

This is not a licence to delete rationale. A sentence explaining *why* a constraint exists is
useful information about the current system.

*Check when:* text describes a past process rather than a present fact.

## WP-2: Use common words and the shortest wording that preserves meaning

Prefer the ordinary word. Cut filler. Where a shorter phrasing says the same thing, use it.
WP-10 governs the boundary.

*Check when:* a sentence runs long, or a long word has a short synonym.

## WP-3: State each fact once, and link to its owner

One document owns each lasting fact; the others link to it. Duplicated facts drift. Keep enough
local context that a package README or a safety comment still works on its own. The owner map is
in `AGENTS.md`.

*Check when:* you are about to restate something another document already owns.

## WP-4: Prefer active voice and direct statements where clearer

"The planner decides the order" beats "the order is decided". Passive voice is allowed where the
actor is genuinely unknown or irrelevant.

*Check when:* a sentence hides who or what acts.

## WP-5: One idea per sentence, one purpose per paragraph

Split a sentence that carries two independent claims. Give a procedure one instruction per step,
with the condition stated before the instruction.

*Check when:* a sentence contains "and" joining two unrelated claims.

## WP-6: Avoid vague jargon, fashionable metaphor, filler, and dashes used as punctuation

**Dashes.** Do not use an em dash (`U+2014`). Do not use a spaced hyphen or a double hyphen as a
dash. Use a period, comma, colon, semicolon or parentheses, or rewrite the sentence. An en dash
(`U+2013`) is for numeric and date ranges only, so an alphabetic compound takes hyphens instead.
Ordinary compound hyphenation is unaffected.

Exempt everywhere: fenced code blocks, inline code, command lines, file paths, URLs and literal
test data. This is what keeps `bins - 1`, `vmax - vmin` and `dotnet run -- prepare small` correct
as written.

**Words.** Avoid `actually`, `additionally`, `align with`, `bolster`, `crucial`, `deep dive`,
`delve`, `emphasize`, `enduring`, `enhance`, `foster`, `garner`, `gate` (figurative),
`highlight` (as a verb), `interplay`, `intricate`, `key` (as an adjective), `landscape`
(abstract), `load-bearing`, `meticulous`, `pivotal`, `robust` (figurative), `showcase`,
`tapestry`, `testament`, `underscore` (as a verb), `valuable` and `vibrant`.

Two narrow exemptions, because these are project vocabulary rather than metaphor:

- `key` as a **noun**: object key, composite key, key mode, cache key.
- `gate` only in this project's **named process obligations**: the delivery, native, archive,
  archival, Adult, review and smoke gates, and `gated by` for benchmark tier and environment
  selection. Everywhere else a gate is a **guard**, a **check**, a **precondition** or a
  **refusal**, and this repository already uses those words.

Replace `load-bearing` with the precise reason or constraint. A term that tells the reader
something is important without saying why has told them nothing.

*Check when:* writing any sentence, and before accepting assistant-generated prose.

## WP-7: Define necessary project terms and use them consistently

Where the project needs a term, define it once and then always use it. Do not alternate between
two words for one concept.

*Check when:* introducing a term, or reaching for a synonym of an existing one.

## WP-8: Preserve exact contracts, qualifications, uncertainty, and normative words

`must` and `may` are not interchangeable, and neither is `is` and `should be`. Keep stated
uncertainty, measurement limits, applicability and the conditions on a claim. Never make a past
result sound more certain than it was recorded to be.

*Check when:* shortening anything normative, measured or qualified.

## WP-9: Comments give a reason, a risk, a constraint or a non-obvious fact

A comment explains what the code cannot say for itself: why this order, which failure this guards,
what a caller must not do, which platform trap this avoids. A comment that restates the next line
is noise. Do not delete an XML doc, a decision link or a safety sentence to satisfy WP-2.

*Check when:* adding or removing any comment.

## WP-10: Precision wins when shortening would change meaning

This rule overrides WP-2 and WP-5. If the shorter wording drops a qualification, a condition, an
actor or a limit, keep the longer wording.

*Check when:* any edit makes text shorter.

---

## Review checklist

A tool cannot answer these. A reviewer must.

1. Is every retained sentence still true of the current system?
2. Does anything here belong to another document's owner?
3. Did any contract, qualification, uncertainty or normative word change meaning?
4. Is the terminology the project's own, used consistently?
5. Does each comment still explain a reason, risk, constraint or non-obvious fact?
6. Is the evidence claimed no stronger than the evidence recorded?
7. Is anything private in here: a machine path, a username, a run identifier, an expiring link?
