namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The <c>declared_domain</c> that won for one attribute after template and matcher application
/// (§9.2), exactly as authored: captured before the resolver parses a numeric
/// <c>free_per_value</c> domain to canonical identities (§10.3, D-096), because those identities
/// cannot give back a spelling such as <c>2.50</c>. <see cref="SpecFreezer"/> writes it back as
/// the prefix of a frozen <c>include</c> domain (spec §7, D-122 part 10).
/// </summary>
/// <param name="AttributeName">The attribute's declared name.</param>
/// <param name="Values">
/// The winning domain's entries in authored order: null when no tier authors a domain, and an
/// empty list for an authored <c>[]</c> (D-123 point 10). <c>[defaults]</c> has no domain, so it
/// never supplies one.
/// </param>
internal sealed record AuthoredDomain(string? AttributeName, IReadOnlyList<string>? Values);
