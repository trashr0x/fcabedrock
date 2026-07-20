namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[matcher]]</c> (§9.2), presence-tracked (D-066): applies a
/// template to the attributes its selector matches. Composed per §13 rule 4 (base
/// entries then derived, so a derived matcher's template layers over a base
/// matcher's for any field both author) and <b>executed</b> at the resolve seam
/// from M6 Slice B (D-114/D-115/D-121). A matcher configures already-declared
/// attributes; it never synthesizes one, and no matcher state enters Core.
/// </summary>
/// <param name="Match">The authored <c>match</c> selector table; exactly one selector (§9.2, parse-enforced).</param>
/// <param name="Template">Id of the <c>[[template]]</c> to apply (§9.2); required (parse-enforced).</param>
public sealed record MatcherSection(MatchSection? Match, string? Template);

/// <summary>
/// The authored <c>match</c> selector of a <c>[[matcher]]</c> (§9.2). Exactly one
/// of the two is authored, and each is shape-validated at parse (D-115): a
/// <c>name_regex</c> is non-empty and compilable in the whole-name form that
/// actually executes, and a <c>source_index_range</c> is exactly two integers
/// satisfying <c>0 ≤ lo ≤ hi</c>.
/// </summary>
/// <param name="NameRegex">Whole-logical-name pattern (§9.2/§10.1) — never a header, predicate text, display name, or rendered formal name.</param>
/// <param name="SourceIndexRange">Inclusive, zero-based range over resolved physical column indexes, authored as <c>[lo, hi]</c>.</param>
public sealed record MatchSection(string? NameRegex, IReadOnlyList<long>? SourceIndexRange);
