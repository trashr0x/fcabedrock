using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// An authored attribute <c>source</c> (§10.2). The document keeps the
/// authored form, including possibly-invalid states (D-066); the resolver
/// turns it into a Core <c>ColumnSource</c> (column index) or
/// <c>PredicateSource</c> (predicate name), each with the effective value type,
/// or, when it declares the reserved <c>value_type = "date"</c>, into the Core
/// <c>UnimplementedDateSource</c>; or it diagnoses <c>SourceBindingInvalid</c>.
/// </summary>
public abstract record SourceSection
{
    /// <summary>
    /// Whether the source declares the reserved <c>value_type = "date"</c> (§10.2/§11.7,
    /// D-038). <c>SourceValueType</c> has no date member, so the reserved value is carried here
    /// and the subtype's <c>ValueType</c> stays null; the planner refuses the attribute
    /// (<c>DateValueTypeNotImplementedV1</c>). The reader never sets both. A hand-built source
    /// that sets both would need two <c>value_type</c> keys, which no TOML document can hold, so
    /// the resolver and the writer refuse it as corrupt document state. It is an init property
    /// rather than a positional parameter (the D-087 pattern).
    /// </summary>
    public bool HasDateValueType { get; init; }
}

/// <summary>
/// A wide column source (§10.2). Exactly one of <paramref name="Index"/> /
/// <paramref name="Name"/> must be authored; both and neither are representable
/// here and diagnosed at resolve (D-066).
/// </summary>
/// <param name="Index">0-based column index, when bound by index.</param>
/// <param name="Name">Header name, when bound by name (requires <c>has_header = true</c> and a header schema).</param>
/// <param name="ValueType">Authored value type; null defaults per-discretizer at resolve (§10.2/D-061), unless <see cref="SourceSection.HasDateValueType"/> carries the reserved date.</param>
public sealed record ColumnSourceSection(int? Index, string? Name, SourceValueType? ValueType) : SourceSection;

/// <summary>
/// A triple predicate source (§10.2/§5.3): resolves to a Core <c>PredicateSource</c>
/// under a triple shape (matched against each row's predicate at emit, D-082) and is
/// <c>SourceBindingInvalid</c> under wide.
/// </summary>
/// <param name="Name">The predicate name.</param>
/// <param name="ValueType">Authored value type; null defaults per-discretizer at resolve (§10.2/D-061), unless <see cref="SourceSection.HasDateValueType"/> carries the reserved date.</param>
public sealed record PredicateSourceSection(string? Name, SourceValueType? ValueType) : SourceSection;
