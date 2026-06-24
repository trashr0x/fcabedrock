namespace FcaBedrock.Spec;

/// <summary>
/// The raw parsed contents of a v2 <c>.bed</c> file — index-aligned parallel
/// arrays, one entry per attribute (the brittle v2 layout that decisions.md D-009
/// replaces with TOML). Notably it carries <b>no</b> binding (delimiter, header,
/// shape): the v2 format never recorded those, so the caller supplies a
/// <see cref="FcaBedrock.Core.Spec.Binding"/> when mapping to a spec.
/// </summary>
/// <param name="AttributeCount">The declared number of attributes.</param>
/// <param name="Names">Attribute names (<c>[Attributes]</c>).</param>
/// <param name="Categories">Display labels per attribute (<c>[Attribute Categories]</c>).</param>
/// <param name="Values">Raw values per attribute (<c>[Category Values]</c>).</param>
/// <param name="Convert">Include flags (<c>[Convert Attribute]</c>).</param>
/// <param name="Types">v2 type codes c/b/o/n/d (<c>[Attribute Type]</c>).</param>
/// <param name="RestrictTo">Raw restriction line per attribute (<c>[Restrict To Values]</c>); empty when none.</param>
public sealed record BedDocument(
    int AttributeCount,
    IReadOnlyList<string> Names,
    IReadOnlyList<IReadOnlyList<string>> Categories,
    IReadOnlyList<IReadOnlyList<string>> Values,
    IReadOnlyList<bool> Convert,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> RestrictTo);
