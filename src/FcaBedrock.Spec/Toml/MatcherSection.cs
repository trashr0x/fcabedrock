namespace FcaBedrock.Spec.Toml;

/// <summary>
/// One authored <c>[[matcher]]</c> (§9.2), presence-tracked (D-066): applies a
/// template to attributes matched by a pattern. M2 carries and composes
/// matchers (§13 rule 4: base entries then derived, preserving §9.2's
/// last-match-wins precedence) but never applies them — a present matcher
/// fails resolve with <c>TemplateMatcherNotImplementedV1</c> until M6 (D-078).
/// </summary>
/// <param name="Match">The authored <c>match</c> pattern table.</param>
/// <param name="Template">Id of the <c>[[template]]</c> to apply (§9.2).</param>
public sealed record MatcherSection(MatchSection? Match, string? Template);

/// <summary>
/// The authored <c>match</c> pattern of a <c>[[matcher]]</c> (§9.2). Carried
/// verbatim; M6 owns pattern semantics and validation (arity, regex syntax).
/// </summary>
/// <param name="NameRegex">Regular expression matched against attribute names.</param>
/// <param name="SourceIndexRange">Inclusive source-column index range, authored as a two-element array; carried at authored arity (D-078).</param>
public sealed record MatchSection(string? NameRegex, IReadOnlyList<long>? SourceIndexRange);
