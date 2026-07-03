namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Presence-tracked document model of an authored TOML Bedrock spec (D-066):
/// a faithful mirror of what was written, where <c>null</c> means "not
/// authored" — defaults are merged only when <see cref="SpecResolver"/>
/// resolves the document into a Core <c>BedrockSpec</c>. This slice models
/// exactly the surface the Slice A resolver consumes or carries loudly;
/// omitted concepts (<c>display_name</c>, <c>formal_attribute_format</c>,
/// deferred discretizer kinds, <c>[[template]]</c>/<c>[[matcher]]</c>,
/// <c>[spec].extends</c>, <c>date</c>) join in their owning later slices.
/// </summary>
/// <param name="Spec">The <c>[spec]</c> section (§3); null when absent.</param>
/// <param name="Provenance">The <c>[provenance]</c> section (§4); carried inert.</param>
/// <param name="Binding">The <c>[binding]</c> section (§5); null when absent.</param>
/// <param name="Defaults">The <c>[defaults]</c> section (§6); null when absent.</param>
/// <param name="Output">The <c>[output]</c> section (§8); carried inert — writers take options from the caller.</param>
/// <param name="Attributes">The <c>[[attribute]]</c> array (§10), in authored order; empty when none.</param>
public sealed record SpecDocument(
    SpecSection? Spec,
    ProvenanceSection? Provenance,
    BindingSection? Binding,
    DefaultsSection? Defaults,
    OutputSection? Output,
    IReadOnlyList<AttributeSection> Attributes);
