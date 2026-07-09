using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// An authored attribute <c>source</c> (§10.2). The document keeps the
/// authored form — including possibly-invalid states (D-066); the resolver
/// turns it into a Core <c>ColumnSource</c> (index + value type) or diagnoses
/// <c>SourceBindingInvalid</c>.
/// </summary>
public abstract record SourceSection;

/// <summary>
/// A wide column source (§10.2). Exactly one of <paramref name="Index"/> /
/// <paramref name="Name"/> must be authored; both and neither are representable
/// here and diagnosed at resolve (D-066).
/// </summary>
/// <param name="Index">0-based column index, when bound by index.</param>
/// <param name="Name">Header name, when bound by name (requires <c>has_header = true</c> and a header schema).</param>
/// <param name="ValueType">Authored value type; null defaults per-discretizer at resolve (§10.2/D-061).</param>
public sealed record ColumnSourceSection(int? Index, string? Name, SourceValueType? ValueType) : SourceSection;

/// <summary>
/// A triple predicate source (§10.2/§5.3): resolves to a Core <c>PredicateSource</c>
/// under a triple shape (matched against each row's predicate at emit, D-082) and is
/// <c>SourceBindingInvalid</c> under wide.
/// </summary>
/// <param name="Name">The predicate name.</param>
/// <param name="ValueType">Authored value type; null defaults per-discretizer at resolve (§10.2/D-061).</param>
public sealed record PredicateSourceSection(string? Name, SourceValueType? ValueType) : SourceSection;
