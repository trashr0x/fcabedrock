namespace FcaBedrock.Core.Spec;

/// <summary>
/// Tells a spec how to apply itself to a concrete data source. Spec §5. The
/// binding is separable from per-attribute scaling (decisions.md D-001): notably,
/// a v2 <c>.bed</c> file carries none of this, so it is supplied by the caller
/// when migrating a v2 spec.
/// </summary>
public sealed record Binding(
    SourceShape Shape,
    char Delimiter,
    char QuoteChar,
    bool HasHeader,
    string Locale,
    string MissingToken,
    ObjectKey ObjectKey);
