using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec;

/// <summary>
/// Maps a parsed <see cref="BedDocument"/> plus a caller-supplied
/// <see cref="Binding"/> to a <see cref="BedrockSpec"/>. The v2 type codes become
/// (discretizer, scale) pairs (decisions.md D-002); attributes bind positionally,
/// matching v2. This slice maps <c>c</c> and <c>b</c> and excluded attributes;
/// <c>o</c>/<c>n</c>/<c>d</c> arrive with later slices.
/// </summary>
public static class BedToSpec
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    /// <summary>Builds a spec from <paramref name="document"/> under <paramref name="binding"/>.</summary>
    public static BedrockSpec ToSpec(BedDocument document, Binding binding)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(binding);

        var attributes = new List<AttributeSpec>(document.AttributeCount);
        for (var i = 0; i < document.AttributeCount; i++)
        {
            attributes.Add(MapAttribute(document, i));
        }

        return new BedrockSpec(binding, attributes);
    }

    private static AttributeSpec MapAttribute(BedDocument document, int index)
    {
        var name = document.Names[index];
        var source = new ColumnSource(index);

        // Excluded: carry only name/source/include — no emitted-only fields, so the
        // planner's §10.9 guard stays satisfied (the v2 categories/values are dropped).
        if (!document.Convert[index])
        {
            return new AttributeSpec(name, source, Include: false, Discretizer: null, Scale: null,
                DeclaredDomain: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
        }

        var type = document.Types[index];
        var values = document.Values[index];
        var categories = document.Categories[index];

        return type switch
        {
            "c" => new AttributeSpec(name, source, Include: true, new IdentityDiscretizer(), new NominalScale(),
                values, ValueLabels(values, categories), MissingPolicy.Skip, UnknownValuePolicy.Warn),
            "b" => new AttributeSpec(name, source, Include: true, new IdentityDiscretizer(), new DichotomicScale(values[0]),
                values, NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn),
            _ => throw new NotSupportedException(
                $"v2 .bed type '{type}' on attribute '{name}' is not supported in this slice."),
        };
    }

    // Display labels (Attribute Categories) keyed by raw value (Category Values),
    // dropping identity mappings so unchanged values carry no redundant label (§10.8).
    private static Dictionary<string, string> ValueLabels(
        IReadOnlyList<string> values, IReadOnlyList<string> categories)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var j = 0; j < values.Count; j++)
        {
            if (j < categories.Count && !string.Equals(values[j], categories[j], StringComparison.Ordinal))
            {
                labels[values[j]] = categories[j];
            }
        }

        return labels;
    }
}
