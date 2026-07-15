using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;

namespace FcaBedrock.Core.Spec;

/// <summary>
/// The opaque resolution token (D-098/G-1): a sealed, non-positional class
/// produced only by the validating factory <see cref="Create"/>. It is the single
/// preparation identity for one conversion — pairing downstream (the calibrator,
/// the emitter, the fingerprint calculator) is by <b>reference identity</b> of
/// this instance, never of a caller-owned <see cref="BedrockSpec"/>. A forger
/// cannot mint the <em>same</em> token, only a parallel internally-consistent
/// chain, which is an honest second pipeline rather than a mixed one.
/// <para>
/// <see cref="Create"/> is also the validate-once trust boundary for
/// hand-built graphs (P-10): it deep-snapshots the spec graph into recursively
/// immutable storage (no public property returns a castable mutable backing
/// array), copies the schema and settings, and exhaustively validates every
/// structural invariant the downstream phases trust — so the planner's residual
/// range checks become unreachable-by-construction. The resolve seam keeps
/// authored errors on the diagnostic channel; this boundary is the programmer-error
/// backstop and throws <see cref="ArgumentException"/> on any violation. A
/// schema-less token is legal only for schema-less spec tooling; it cannot enter
/// the conversion pipeline (the calibrated-state factories reject it).
/// </para>
/// </summary>
public sealed class ResolvedSpec
{
    private ResolvedSpec(BedrockSpec spec, SourceSchema? schema, SourceReadSettings settings)
    {
        Spec = spec;
        Schema = schema;
        Settings = settings;
    }

    /// <summary>The immutable snapshot of the resolved spec.</summary>
    public BedrockSpec Spec { get; }

    /// <summary>The immutable snapshot of the source schema; null only for schema-less tooling flows.</summary>
    public SourceSchema? Schema { get; }

    /// <summary>The read settings this resolution was prepared with.</summary>
    public SourceReadSettings Settings { get; }

    /// <summary>
    /// Validates the graph exhaustively (see the type remarks) and returns the
    /// opaque token over a recursively-immutable snapshot. Throws
    /// <see cref="ArgumentException"/> (or <see cref="ArgumentNullException"/> for
    /// null <paramref name="spec"/>/<paramref name="settings"/>/<paramref name="nameBindings"/>)
    /// on any structural violation.
    /// </summary>
    public static ResolvedSpec Create(
        BedrockSpec spec,
        SourceSchema? schema,
        SourceReadSettings settings,
        IReadOnlyList<ResolvedNameBinding> nameBindings)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(nameBindings);
        ArgumentNullException.ThrowIfNull(spec.Binding);
        ArgumentNullException.ThrowIfNull(spec.Attributes);

        ValidateSchemaStructure(schema);
        ValidateSettings(settings, spec.Binding);
        ValidateLocale(spec.Binding.Locale);
        ValidateBindingCoherence(spec.Binding, schema);
        ValidateAttributes(spec, schema);
        ValidateNameBindings(nameBindings, spec, schema);

        return new ResolvedSpec(Snapshot(spec), SnapshotSchema(schema), settings);
    }

    // The schema is freely constructible, so the trust boundary re-checks it
    // (round-6 Medium-4): a non-negative column count and, when a header is
    // present, a count that matches.
    private static void ValidateSchemaStructure(SourceSchema? schema)
    {
        if (schema is null)
        {
            return;
        }

        if (schema.ColumnCount < 0)
        {
            throw new ArgumentException($"schema.ColumnCount {schema.ColumnCount} is negative.", nameof(schema));
        }

        if (schema.Header is { } header && header.Count != schema.ColumnCount)
        {
            throw new ArgumentException(
                $"schema header has {header.Count} names but ColumnCount is {schema.ColumnCount}.", nameof(schema));
        }
    }

    // (e) the locale must be "invariant" or a predefined culture — mirrors the seam's
    // predefinedOnly rule (P-7); a synthesized ICU culture would make acceptance
    // OS-dependent.
    private static void ValidateLocale(string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        if (string.Equals(locale, "invariant", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            throw new ArgumentException($"binding.Locale '{locale}' is neither \"invariant\" nor a predefined culture.");
        }
    }

    private static void ValidateSettings(SourceReadSettings settings, Binding binding)
    {
        if (settings.Shape != binding.Shape
            || !string.Equals(settings.Encoding, binding.Encoding, StringComparison.Ordinal)
            || settings.Delimiter != binding.Delimiter
            || settings.QuoteChar != binding.QuoteChar
            || settings.HasHeader != binding.HasHeader
            || !string.Equals(settings.MissingToken, binding.MissingToken, StringComparison.Ordinal)
            || settings.Ordering != binding.Ordering)
        {
            throw new ArgumentException("settings are inconsistent with the spec binding's scalars.", nameof(settings));
        }
    }

    private static void ValidateBindingCoherence(Binding binding, SourceSchema? schema)
    {
        RequireDefined(binding.Shape, "binding.Shape");
        RequireDefined(binding.ObjectKey);

        switch (binding.Shape)
        {
            case SourceShape.Wide:
                if (binding.TripleColumns is not null || binding.Ordering is not null)
                {
                    throw new ArgumentException("a wide binding must have null TripleColumns and Ordering.", nameof(binding));
                }

                break;

            case SourceShape.Triple:
                if (binding.TripleColumns is not { } columns)
                {
                    throw new ArgumentException("a triple binding must carry a resolved TripleColumns map.", nameof(binding));
                }

                if (binding.Ordering is not { } ordering)
                {
                    throw new ArgumentException("a triple binding must carry a resolved ordering.", nameof(binding));
                }

                RequireDefined(ordering, "binding.Ordering");
                RequireInRange(columns.Subject, schema, "triple subject column");
                RequireInRange(columns.Predicate, schema, "triple predicate column");
                RequireInRange(columns.Value, schema, "triple value column");
                if (columns.Subject == columns.Predicate || columns.Subject == columns.Value || columns.Predicate == columns.Value)
                {
                    throw new ArgumentException("triple roles must resolve to distinct columns.", nameof(binding));
                }

                if (binding.ObjectKey is not ColumnObjectKey { Policy: DuplicateObjectPolicy.Fail } subjectKey
                    || subjectKey.Index != columns.Subject)
                {
                    throw new ArgumentException(
                        "a triple binding's object key must be the subject-pinned ColumnObjectKey(subject, Fail).", nameof(binding));
                }

                break;

            default:
                throw new ArgumentException($"unknown source shape {binding.Shape}.", nameof(binding));
        }

        // Object-key index range (wide column key or triple subject key).
        if (binding.ObjectKey is ColumnObjectKey columnKey)
        {
            RequireInRange(columnKey.Index, schema, "object-key column");
        }
    }

    private static void ValidateAttributes(BedrockSpec spec, SourceSchema? schema)
    {
        foreach (var attribute in spec.Attributes)
        {
            ArgumentNullException.ThrowIfNull(attribute);
            RequireDefined(attribute.MissingPolicy, "attribute.MissingPolicy");
            RequireDefined(attribute.UnknownValuePolicy, "attribute.UnknownValuePolicy");

            // (b) source kind ⇔ binding shape.
            switch (attribute.Source)
            {
                case ColumnSource column:
                    if (spec.Binding.Shape != SourceShape.Wide)
                    {
                        throw new ArgumentException(
                            $"attribute '{attribute.Name}' has a column source under a non-wide binding.");
                    }

                    RequireDefined(column.ValueType, "source.ValueType");
                    RequireInRange(column.Index, schema, $"attribute '{attribute.Name}' source column");
                    break;

                case PredicateSource predicate:
                    if (spec.Binding.Shape != SourceShape.Triple)
                    {
                        throw new ArgumentException(
                            $"attribute '{attribute.Name}' has a predicate source under a non-triple binding.");
                    }

                    RequireDefined(predicate.ValueType, "source.ValueType");
                    break;

                default:
                    throw new ArgumentException($"attribute '{attribute.Name}' has an unrecognized source binding.");
            }

            // (a) every included attribute carries a discretizer and a scale.
            if (attribute.Include)
            {
                if (attribute.Discretizer is null || attribute.Scale is null)
                {
                    throw new ArgumentException(
                        $"included attribute '{attribute.Name}' must carry a discretizer and a scale.");
                }
            }

            ValidateDiscretizerEnums(attribute.Discretizer);
            ValidateScaleEnums(attribute.Scale);
            ValidateRestrictEntries(attribute);
        }
    }

    private static void ValidateDiscretizerEnums(Discretizer? discretizer)
    {
        switch (discretizer)
        {
            case ManualCutsDiscretizer manual:
                RequireDefined(manual.Ends, "manual_cuts.Ends");
                break;
            case OrderedCutsDiscretizer ordered:
                RequireDefined(ordered.Ends, "ordered_cuts.Ends");
                break;
        }
    }

    private static void ValidateScaleEnums(Scale? scale)
    {
        if (scale is OrdinalScale ordinal)
        {
            RequireDefined(ordinal.Direction, "ordinal.Direction");
            RequireDefined(ordinal.Boundary, "ordinal.Boundary");
        }
    }

    // (f) restriction entries are known variants. Numeric-finiteness validation is
    // staged to slice F (with RestrictToNumber); slice A only guards the union.
    private static void ValidateRestrictEntries(AttributeSpec attribute)
    {
        foreach (var entry in attribute.RestrictTo)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry is not (RestrictToValue or RestrictToRange))
            {
                throw new ArgumentException(
                    $"attribute '{attribute.Name}' has an unrecognized restrict_to entry '{entry.GetType().Name}'.");
            }
        }
    }

    private static void ValidateNameBindings(
        IReadOnlyList<ResolvedNameBinding> nameBindings, BedrockSpec spec, SourceSchema? schema)
    {
        if (nameBindings.Count == 0)
        {
            return;
        }

        if (schema?.Header is not { } header)
        {
            throw new ArgumentException(
                "name bindings require a schema with a header.", nameof(nameBindings));
        }

        foreach (var binding in nameBindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.Index < 0 || binding.Index >= header.Count || !string.Equals(header[binding.Index], binding.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"name binding '{binding.Name}' does not match the header at index {binding.Index}.", nameof(nameBindings));
            }

            var occurrences = 0;
            foreach (var name in header)
            {
                if (string.Equals(name, binding.Name, StringComparison.Ordinal))
                {
                    occurrences++;
                }
            }

            if (occurrences != 1)
            {
                throw new ArgumentException(
                    $"name binding '{binding.Name}' must occur exactly once in the header ({occurrences} occurrences).", nameof(nameBindings));
            }

            ValidateNameBindingSite(binding, spec);
        }
    }

    private static void ValidateNameBindingSite(ResolvedNameBinding binding, BedrockSpec spec)
    {
        switch (binding)
        {
            case AttributeSourceNameBinding attributeBinding:
                AttributeSpec? matched = null;
                foreach (var candidate in spec.Attributes)
                {
                    if (string.Equals(candidate.Name, attributeBinding.AttributeName, StringComparison.Ordinal))
                    {
                        if (matched is not null)
                        {
                            throw new ArgumentException(
                                $"name binding site attribute '{attributeBinding.AttributeName}' is not unique.");
                        }

                        matched = candidate;
                    }
                }

                if (matched is null || matched.Source is not ColumnSource column || column.Index != attributeBinding.Index)
                {
                    throw new ArgumentException(
                        $"name binding site attribute '{attributeBinding.AttributeName}' does not carry column index {attributeBinding.Index}.");
                }

                break;

            case ObjectKeyNameBinding objectKeyBinding:
                if (spec.Binding.ObjectKey is not ColumnObjectKey key || key.Index != objectKeyBinding.Index)
                {
                    throw new ArgumentException(
                        $"name binding site object key does not carry column index {objectKeyBinding.Index}.");
                }

                break;

            case TripleRoleNameBinding roleBinding:
                if (spec.Binding.TripleColumns is not { } columns)
                {
                    throw new ArgumentException("triple role name binding on a non-triple binding.");
                }

                var roleIndex = roleBinding.Role switch
                {
                    TripleRole.Subject => columns.Subject,
                    TripleRole.Predicate => columns.Predicate,
                    TripleRole.Value => columns.Value,
                    _ => throw new ArgumentException($"unknown triple role {roleBinding.Role}."),
                };
                if (roleIndex != roleBinding.Index)
                {
                    throw new ArgumentException(
                        $"name binding site triple role {roleBinding.Role} does not carry column index {roleBinding.Index}.");
                }

                break;

            default:
                throw new ArgumentException($"unknown name binding site {binding.GetType().Name}.");
        }
    }

    // ---- Recursive-immutable rebuild ----

    private static BedrockSpec Snapshot(BedrockSpec spec)
    {
        var attributes = ImmutableArray.CreateBuilder<AttributeSpec>(spec.Attributes.Count);
        foreach (var attribute in spec.Attributes)
        {
            attributes.Add(SnapshotAttribute(attribute));
        }

        // Binding is fully immutable already (scalars + immutable ObjectKey/TripleColumns records).
        return new BedrockSpec(spec.Binding, attributes.MoveToImmutable());
    }

    private static AttributeSpec SnapshotAttribute(AttributeSpec attribute) =>
        attribute with
        {
            Discretizer = SnapshotDiscretizer(attribute.Discretizer),
            Scale = SnapshotScale(attribute.Scale),
            DeclaredDomain = attribute.DeclaredDomain.ToImmutableArray(),
            RestrictTo = attribute.RestrictTo.ToImmutableArray(),
            ValueLabels = attribute.ValueLabels.Count == 0
                ? FrozenDictionary<string, string>.Empty
                : attribute.ValueLabels.ToFrozenDictionary(StringComparer.Ordinal),
        };

    // The M1 discretizers store their snapshots as ImmutableArray from construction, so the
    // only mutable state reachable through the graph is a culture-bearing discretizer's
    // CultureInfo (read during parsing). Reconstruct those through the factory with a read-only
    // culture clone so a programmatic caller cannot mutate NumberFormat after resolution and
    // change classification (D-098 recursive immutability, P-7/P-11). The cultureless kinds
    // (identity, ordered_cuts) are already fully immutable — reused as-is.
    private static Discretizer? SnapshotDiscretizer(Discretizer? discretizer) => discretizer switch
    {
        ManualCutsDiscretizer cuts => ManualCutsDiscretizer.Create(cuts.Cuts, cuts.Ends, ReadOnlyCulture(cuts.Culture)).Value!,
        _ => discretizer,
    };

    private static CultureInfo ReadOnlyCulture(CultureInfo culture) =>
        culture.IsReadOnly ? culture : CultureInfo.ReadOnly((CultureInfo)culture.Clone());

    // OrdinalScale holds the authored order by reference; rebuild it over an
    // ImmutableArray so no castable mutable array survives on the graph. The other
    // scales are already immutable.
    private static Scale? SnapshotScale(Scale? scale) =>
        scale is OrdinalScale { Order: { } order } ordinal
            ? ordinal with { Order = order.ToImmutableArray() }
            : scale;

    private static SourceSchema? SnapshotSchema(SourceSchema? schema) =>
        schema is null
            ? null
            : new SourceSchema(schema.ColumnCount, schema.Header is { } header ? header.ToImmutableArray() : null);

    private static void RequireInRange(int index, SourceSchema? schema, string what)
    {
        if (index < 0)
        {
            throw new ArgumentException($"{what} index {index} is negative.");
        }

        if (schema is not null && index >= schema.ColumnCount)
        {
            throw new ArgumentException(
                $"{what} index {index} is out of range for a source with {schema.ColumnCount} columns.");
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string what)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException($"{what} holds undefined enum value {value}.");
        }
    }

    // ObjectKey is a public, externally-derivable record hierarchy (not private-protected
    // closed), so the trust boundary validates its union exhaustively: an unknown subtype would
    // otherwise slip past the planner (no rejecting default) and be treated as row_index at emit
    // (`plan.ObjectKey as ColumnObjectKey` → null) — a silent semantic fallback (D-098, P-10).
    private static void RequireDefined(ObjectKey objectKey)
    {
        ArgumentNullException.ThrowIfNull(objectKey);
        switch (objectKey)
        {
            case RowIndexObjectKey:
            case CompositeObjectKey:
                break;
            case ColumnObjectKey column:
                RequireDefined(column.Policy, "object_key.Policy");
                break;
            default:
                throw new ArgumentException($"unknown object key type {objectKey.GetType().Name}.");
        }
    }
}
