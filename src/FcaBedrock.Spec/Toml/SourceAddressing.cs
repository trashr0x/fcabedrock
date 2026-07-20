using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Where an attribute's raw values come from, once its <c>source</c> has been
/// addressed against the binding and any supplied schema (§10.2) — and
/// <em>only</em> that. Deliberately carries the <b>authored</b> <c>value_type</c>
/// rather than an effective one: addressing happens before template application,
/// while the effective value type depends on the effective discretizer (D-061),
/// so the two are separate steps with one owner each (D-121).
/// </summary>
internal abstract record AddressedSource(SourceValueType? AuthoredValueType);

/// <summary>A wide source addressed to its resolved physical column index (§10.2).</summary>
internal sealed record AddressedColumn(int Index, SourceValueType? AuthoredValueType)
    : AddressedSource(AuthoredValueType);

/// <summary>A triple source addressed by its predicate selector (§10.2/§5.3).</summary>
internal sealed record AddressedPredicate(string Name, SourceValueType? AuthoredValueType)
    : AddressedSource(AuthoredValueType);

/// <summary>
/// One attribute's addressing outcome: the addressed source, or null with the one
/// <c>SourceBindingInvalid</c> explaining why. Exactly one of the two is present.
/// </summary>
internal readonly record struct AddressedAttribute(AddressedSource? Source, BedrockDiagnostic? Diagnostic);

/// <summary>
/// The single owner of attribute source addressing (D-121). Runs <b>once</b> per
/// resolve — after binding resolution, before matcher application — and produces,
/// per attribute in declaration order, either an <see cref="AddressedSource"/> or
/// the one <c>SourceBindingInvalid</c> that explains the failure.
/// <para>
/// It exists because a <c>source_index_range</c> matcher selects on the
/// <b>resolved physical column index</b> (§9.2/D-115), which means addressing must
/// precede application — while the resulting binding diagnostic must still be
/// reported exactly once, in the attribute's ordinary validation slot. Splitting
/// "address the source" from "report and consume it" is what makes both true:
/// <c>SpecResolver.ResolveAttribute</c> consumes this result and never re-resolves
/// or re-diagnoses a source.
/// </para>
/// <para>
/// There is no circularity, because <c>source</c> can never arrive from a template
/// (the closed §9.1 field list, D-114) — binding always precedes application. The
/// pass opens no source and reads no data row (§7 phase 1): it consults only the
/// authored document plus optional schema metadata.
/// </para>
/// </summary>
internal static class SourceAddressing
{
    /// <summary>
    /// Addresses every attribute's source, appending any resolved
    /// <see cref="AttributeSourceNameBinding"/> to <paramref name="nameBindings"/> in
    /// attribute declaration order (the established order the
    /// <c>ResolvedSpec</c> trust boundary sees, D-098).
    /// </summary>
    public static AddressedAttribute[] Address(
        IReadOnlyList<AttributeSection> attributes,
        SourceShape shape,
        SourceSchema? schema,
        bool hasHeader,
        List<ResolvedNameBinding> nameBindings)
    {
        var addressed = new AddressedAttribute[attributes.Count];
        for (var i = 0; i < attributes.Count; i++)
        {
            addressed[i] = Address(attributes[i], shape, schema, hasHeader, nameBindings);
        }

        return addressed;
    }

    private static AddressedAttribute Address(
        AttributeSection section,
        SourceShape shape,
        SourceSchema? schema,
        bool hasHeader,
        List<ResolvedNameBinding> nameBindings)
    {
        var label = string.IsNullOrEmpty(section.Name) ? "<unnamed>" : section.Name;
        switch (section.Source)
        {
            // §10.2: source kind must match the binding shape.
            case ColumnSourceSection when shape == SourceShape.Triple:
                return Invalid(label, "has a column source, which requires a wide binding");

            case ColumnSourceSection column:
                return AddressColumn(column, label, section.Name, schema, hasHeader, nameBindings);

            case PredicateSourceSection when shape != SourceShape.Triple:
                return Invalid(label, "has a predicate source, which requires a triple binding");

            case PredicateSourceSection predicate:
                // The predicate is a data selector, not a header name — no schema
                // resolution; it only must be a non-empty string (§5.3/§10.2).
                return predicate.Name is { Length: > 0 } predicateName
                    ? new AddressedAttribute(new AddressedPredicate(predicateName, predicate.ValueType), null)
                    : Invalid(label, "has a predicate source with no name");

            default:
                return Invalid(label, "declares no source");
        }
    }

    private static AddressedAttribute AddressColumn(
        ColumnSourceSection column,
        string label,
        string? attributeName,
        SourceSchema? schema,
        bool hasHeader,
        List<ResolvedNameBinding> nameBindings)
    {
        // §10.2: exactly one of index/name.
        if (column is { Index: { } index, Name: null })
        {
            if (index < 0)
            {
                return Invalid(label, $"declares negative source index {index}");
            }

            // The conversion pipeline resolves schema-aware (G-1/D-098), so this seam
            // owns the source-index range check. A schema-less resolve (spec tooling)
            // leaves the width unknown; ResolvedSpec.Create is the trust-boundary backstop.
            if (schema is not null && index >= schema.ColumnCount)
            {
                return Invalid(label,
                    $"declares source index {index}, which is out of range for a source with {schema.ColumnCount} columns");
            }

            return new AddressedAttribute(new AddressedColumn(index, column.ValueType), null);
        }

        string problem;
        if (column.Index is not null)
        {
            problem = "declares both a source index and a source name; exactly one is allowed";
        }
        else if (column.Name is not { } byName)
        {
            problem = "declares neither a source index nor a source name";
        }
        else if (!hasHeader)
        {
            problem = $"binds source name '{byName}' but the binding declares has_header = false";
        }
        else if (schema?.Header is not { } header)
        {
            problem = $"binds source name '{byName}' but no header schema was supplied";
        }
        else
        {
            // §10.2: a source name must resolve to exactly one column.
            var found = ResolveUniqueHeader(header, byName);
            if (found >= 0)
            {
                // Record the site-typed name binding for the ResolvedSpec trust boundary
                // (D-098); an empty attribute name is already an AttributeNameMissing error
                // that fails the success gate, so the binding is never consumed there.
                if (!string.IsNullOrEmpty(attributeName))
                {
                    nameBindings.Add(new AttributeSourceNameBinding(attributeName, byName, found));
                }

                return new AddressedAttribute(new AddressedColumn(found, column.ValueType), null);
            }

            problem = found == -1
                ? $"binds source name '{byName}', which is not in the source header"
                : $"binds source name '{byName}', which matches multiple source header columns; it must resolve to exactly one";
        }

        return Invalid(label, problem);
    }

    private static AddressedAttribute Invalid(string attribute, string problem) =>
        new(null, new BedrockDiagnostic(
            DiagnosticCode.SourceBindingInvalid, DiagnosticSeverity.Error,
            $"Attribute '{attribute}' {problem} (§10.2).",
            new DiagnosticLocation(AttributeName: attribute)));

    /// <summary>
    /// §10.2/§5.3: a header name must resolve to exactly one column. Returns the sole
    /// index, -1 when no header matches, or -2 when several do — the -2 case is the
    /// duplicate-matching-header reject shared by wide sources, wide column object
    /// keys, and triple roles (ordinal compare, P-12).
    /// </summary>
    public static int ResolveUniqueHeader(IReadOnlyList<string> header, string name)
    {
        var matches = 0;
        var index = -1;
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.Ordinal))
            {
                matches++;
                if (index < 0)
                {
                    index = i;
                }
            }
        }

        return matches switch { 1 => index, 0 => -1, _ => -2 };
    }
}
