using System.Globalization;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Spec;

/// <summary>
/// Maps a parsed <see cref="BedDocument"/> plus a caller-supplied
/// <see cref="Binding"/> to a <see cref="BedrockSpec"/>. The v2 type codes become
/// (discretizer, scale) pairs (decisions.md D-002); attributes bind positionally,
/// matching v2. Maps <c>c</c>, <c>b</c>, <c>o</c> (numeric cuts), <c>n</c> (ordered
/// cuts), and excluded attributes; <c>d</c> (date) is a parity deferral (D-038).
/// The discrete-vs-progressive choice for <c>o</c>/<c>n</c> is supplied out-of-band
/// via <see cref="ScalingMode"/> — the <c>.bed</c> never recorded it.
/// </summary>
public static class BedToSpec
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    /// <summary>
    /// Builds a spec from <paramref name="document"/> under <paramref name="binding"/>,
    /// with <paramref name="mode"/> selecting nominal (discrete) or ordinal
    /// (progressive) scaling for the cut types.
    /// </summary>
    public static BedrockSpec ToSpec(BedDocument document, Binding binding, ScalingMode mode = ScalingMode.Discrete)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(binding);

        var culture = ResolveCulture(binding.Locale);
        var attributes = new List<AttributeSpec>(document.AttributeCount);
        for (var i = 0; i < document.AttributeCount; i++)
        {
            attributes.Add(MapAttribute(document, i, culture, mode));
        }

        return new BedrockSpec(binding, attributes);
    }

    private static AttributeSpec MapAttribute(BedDocument document, int index, CultureInfo culture, ScalingMode mode)
    {
        if (document.Convert[index])
        {
            return MapByType(document, index, culture, mode);
        }

        // include = false is an authoring toggle (D-049): recover the v2 config so the
        // parked attribute round-trips and can be switched back on — but dormant config
        // must never block migration. An unsupported type, or config that fails to
        // parse, degrades to a bare excluded attribute (the pre-D-049 drop-on-exclude
        // behavior); the planner ignores an excluded attribute either way.
        var name = document.Names[index];
        var source = new ColumnSource(index);
        try
        {
            return MapByType(document, index, culture, mode) with { Include = false };
        }
        catch (Exception)
        {
            // Broad by intent: the failure cause is immaterial — unrecoverable dormant
            // config is dropped. The exception surface here is incidental (parse/ctor
            // failures), so narrowing would risk silently reverting to a hard failure;
            // a genuine bug in MapByType still surfaces on the included path above.
            return BareExcluded(name, source);
        }
    }

    // Builds the included (active) form from the v2 type code. Throws on an
    // unsupported type or malformed config; MapAttribute swallows that only for an
    // excluded attribute, whose config is dormant (D-049).
    private static AttributeSpec MapByType(BedDocument document, int index, CultureInfo culture, ScalingMode mode)
    {
        var name = document.Names[index];
        var source = new ColumnSource(index);
        var type = document.Types[index];
        var values = document.Values[index];
        var categories = document.Categories[index];

        return type switch
        {
            "c" => new AttributeSpec(name, source, Include: true, new IdentityDiscretizer(), new NominalScale(),
                values, ValueLabels(values, categories), MissingPolicy.Skip, UnknownValuePolicy.Warn),
            "b" => new AttributeSpec(name, source, Include: true, new IdentityDiscretizer(), new DichotomicScale(values[0]),
                values, NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn),
            // o (numeric) and n (ordered categorical) are both cut-based (D-045): the
            // cut spec is in [Category Values]; for n the ordered domain is in
            // [Attribute Categories]. Discrete → nominal, progressive → ordinal(le).
            "o" => Cut(name, source, NumericCuts(values, culture), mode),
            "n" => Cut(name, source, OrderedCuts(categories, values), mode),
            _ => throw new NotSupportedException(
                $"v2 .bed type '{type}' on attribute '{name}' is not supported in this slice."),
        };
    }

    private static AttributeSpec BareExcluded(string name, ColumnSource source) =>
        new(name, source, Include: false, Discretizer: null, Scale: null,
            DeclaredDomain: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec Cut(string name, ColumnSource source, Discretizer discretizer, ScalingMode mode) =>
        new(name, source, Include: true, discretizer, ScaleFor(mode),
            DeclaredDomain: [], NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static Scale ScaleFor(ScalingMode mode) =>
        mode == ScalingMode.Progressive ? new OrdinalScale(OrdinalDirection.Le) : new NominalScale();

    private static ManualCutsDiscretizer NumericCuts(IReadOnlyList<string> values, CultureInfo culture)
    {
        var (cutTokens, ends) = ParseCutSpec(values);
        var cuts = cutTokens.Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToList();
        return Require(ManualCutsDiscretizer.Create(cuts, ends, culture));
    }

    private static OrderedCutsDiscretizer OrderedCuts(IReadOnlyList<string> order, IReadOnlyList<string> values)
    {
        var (cutTokens, ends) = ParseCutSpec(values);
        return Require(OrderedCutsDiscretizer.Create(order, cutTokens, ends));
    }

    // M1 BedToSpec still throws and returns a bare spec (the Diagnosed<T> rework is M2, D-049):
    // convert a cut-validation failure (D-056) into an exception carrying the diagnostic messages.
    // On the included path MapByType throws (a clear D-056 message replaces the old opaque
    // IndexOutOfRange/wrong-bins behavior); on the excluded path MapAttribute's catch degrades to
    // BareExcluded. The factory's Diagnosed signature is already what the M2 reader will thread.
    private static T Require<T>(Diagnosed<T> result)
        where T : class =>
        result.TryGetValue(out var value)
            ? value
            : throw new FormatException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));

    // The v2 cut spec is the [Category Values] tokens with sentinel ends: a leading
    // "<" and/or trailing ">" mark open ends; the interior tokens are the cuts.
    private static (IReadOnlyList<string> Cuts, BinEnds Ends) ParseCutSpec(IReadOnlyList<string> tokens)
    {
        var open = tokens.Count > 0 && (tokens[0] == "<" || tokens[^1] == ">");
        var cuts = tokens.Where(t => t is not ("<" or ">")).ToList();
        return (cuts, open ? BinEnds.Open : BinEnds.Closed);
    }

    // Cut values in the .bed are invariant schema strings (§14); only the data
    // column is read with the binding locale. "invariant" maps to the invariant culture.
    private static CultureInfo ResolveCulture(string locale) =>
        string.Equals(locale, "invariant", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(locale);

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
