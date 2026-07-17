using System.Collections.Frozen;
using System.Collections.Immutable;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// Produces an immutable deep snapshot of a <see cref="SpecDocument"/> for
/// <see cref="ResolvedDocument"/> (D-098/G-1): every section list/dictionary is
/// rebuilt over <see cref="ImmutableArray{T}"/> / frozen maps, so post-resolve
/// mutation of a caller-owned document cannot leak into fingerprinting. The
/// scalar-only sections (<c>[spec]</c>, <c>[provenance]</c>, <c>[output]</c>,
/// <c>[defaults]</c>) are already immutable records and are reused.
/// </summary>
internal static class DocumentSnapshot
{
    public static SpecDocument Take(SpecDocument document) =>
        document with
        {
            Binding = document.Binding is { } binding ? SnapshotBinding(binding) : null,
            Templates = document.Templates.Select(SnapshotTemplate).ToImmutableArray(),
            Matchers = document.Matchers.Select(SnapshotMatcher).ToImmutableArray(),
            Attributes = document.Attributes.Select(SnapshotAttribute).ToImmutableArray(),
        };

    private static BindingSection SnapshotBinding(BindingSection binding) =>
        binding.ObjectKey is { Columns: { } columns } key
            ? binding with { ObjectKey = key with { Columns = columns.ToImmutableArray() } }
            : binding;

    private static AttributeSection SnapshotAttribute(AttributeSection attribute) =>
        attribute with
        {
            Discretizer = SnapshotDiscretizer(attribute.Discretizer),
            Scale = SnapshotScale(attribute.Scale),
            DeclaredDomain = attribute.DeclaredDomain?.ToImmutableArray(),
            RestrictTo = attribute.RestrictTo?.ToImmutableArray(),
            ValueLabels = SnapshotLabels(attribute.ValueLabels),
        };

    private static TemplateSection SnapshotTemplate(TemplateSection template) =>
        template with
        {
            Discretizer = SnapshotDiscretizer(template.Discretizer),
            Scale = SnapshotScale(template.Scale),
            DeclaredDomain = template.DeclaredDomain?.ToImmutableArray(),
            RestrictTo = template.RestrictTo?.ToImmutableArray(),
            ValueLabels = SnapshotLabels(template.ValueLabels),
        };

    private static MatcherSection SnapshotMatcher(MatcherSection matcher) =>
        matcher.Match is { SourceIndexRange: { } range } match
            ? matcher with { Match = match with { SourceIndexRange = range.ToImmutableArray() } }
            : matcher;

    private static DiscretizerSection? SnapshotDiscretizer(DiscretizerSection? discretizer) => discretizer switch
    {
        ManualCutsDiscretizerSection { Cuts: { } cuts } manual => manual with { Cuts = cuts.ToImmutableArray() },
        OrderedCutsDiscretizerSection ordered => ordered with
        {
            Order = ordered.Order?.ToImmutableArray(),
            Cuts = ordered.Cuts?.ToImmutableArray(),
        },

        // §11.6: value_groups nests one list inside another, so the snapshot must be deep — the
        // outer groups list AND each group's authored values. Copying only the outer list would
        // leave every inner list caller-owned and mutable. `?.ToImmutableArray()` preserves the
        // authored-null vs authored-empty distinction the §14 encoding depends on (G-11): null
        // stays null, and an authored empty list stays an (immutable) empty list.
        ValueGroupsDiscretizerSection { Groups: { } groups } valueGroups => valueGroups with
        {
            Groups = groups.Select(static group => group with { Values = group.Values?.ToImmutableArray() }).ToImmutableArray(),
        },
        _ => discretizer,
    };

    private static ScaleSection? SnapshotScale(ScaleSection? scale) =>
        scale is OrdinalScaleSection { Order: { } order } ordinal
            ? ordinal with { Order = order.ToImmutableArray() }
            : scale;

    private static IReadOnlyDictionary<string, string>? SnapshotLabels(IReadOnlyDictionary<string, string>? labels) =>
        labels is null
            ? null
            : labels.Count == 0
                ? FrozenDictionary<string, string>.Empty
                : labels.ToFrozenDictionary(StringComparer.Ordinal);
}
