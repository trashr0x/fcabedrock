namespace FcaBedrock.Core.Planning;

/// <summary>
/// The canonical identity of a formal attribute — the <c>.dat</c> column identity
/// and (from M2) the schema-fingerprint unit. Distinct from the rendered
/// <c>.cxt</c> name: identity is style- and label-independent (spec §10.2, §14).
/// </summary>
/// <param name="AttributeName">The logical attribute (column) name.</param>
/// <param name="Scale">The scale kind (<c>nominal</c>, <c>dichotomic</c>, …).</param>
/// <param name="BinKey">The canonical bin/threshold key; empty for a dichotomic single.</param>
/// <param name="Operator">The ordinal operator (<c>&gt;=</c>, …); empty otherwise.</param>
public readonly record struct FormalAttributeIdentity(
    string AttributeName,
    string Scale,
    string BinKey,
    string Operator);
