using System.Globalization;

namespace FcaBedrock.Discovery;

/// <summary>
/// The caller-overridable knobs of a <see cref="Prober"/> run (D-108/D-110): the
/// per-attribute retention limit, the three aggregate boundedness guards, and the draft's
/// <c>locale</c>. Immutable, built through the validating <see cref="Create"/> factory
/// (P-10) so an out-of-range limit or an unusable locale cannot reach the engine.
/// <para>
/// <b>Options are caller contract, not spec text.</b> The four numeric options shape a
/// draft; they are never authored into <c>[binding]</c> and never enter a fingerprint
/// (D-108). Only <see cref="Locale"/> is a read setting, and it is authored explicitly like
/// every other one (D-107) — inert at M5, since probe parses no numbers, but a draft that
/// declares the locale it was produced under stays self-documenting.
/// </para>
/// <para>
/// <b>Why the guards live here and not in the spec.</b> They bound one probe of one source
/// (the motivating arithmetic: 1,554 attributes × 100,000 values would permit 155.4M retained
/// strings). Their accounting is deterministic and logical — counts and UTF-16 code units,
/// never an available-memory figure, which would make the same input succeed on one machine
/// and fail on another (P-7/P-11).
/// </para>
/// </summary>
public sealed class ProbeOptions
{
    private ProbeOptions(
        int valueRetentionLimit,
        int maxDiscoveredAttributes,
        long maxTotalRetainedValues,
        long maxTotalRetainedValueText,
        string locale)
    {
        ValueRetentionLimit = valueRetentionLimit;
        MaxDiscoveredAttributes = maxDiscoveredAttributes;
        MaxTotalRetainedValues = maxTotalRetainedValues;
        MaxTotalRetainedValueText = maxTotalRetainedValueText;
        Locale = locale;
    }

    /// <summary>
    /// The options a caller gets by passing none: every documented default (D-108's 100,000
    /// retention limit, the three D-110 guard defaults, and <c>invariant</c>).
    /// </summary>
    public static ProbeOptions Default { get; } = Create();

    /// <summary>
    /// Builds validated options. Every numeric option is <b>independently</b> required to be
    /// at least 1 and throws <see cref="ArgumentOutOfRangeException"/> otherwise; there is no
    /// cross-limit validation, because the guards measure different things and no combination
    /// of them is incoherent. A null <paramref name="locale"/> throws
    /// <see cref="ArgumentNullException"/> and one the resolve seam would reject throws
    /// <see cref="ArgumentException"/> — validated here so a draft can never fail its own
    /// reread/resolve guarantee (D-107) on a field the caller chose.
    /// </summary>
    /// <param name="valueRetentionLimit">Maximum distinct values retained per attribute (D-108).</param>
    /// <param name="maxDiscoveredAttributes">Maximum attributes a probe may discover (D-110 guard 1).</param>
    /// <param name="maxTotalRetainedValues">Maximum retained values summed across attributes (D-110 guard 2).</param>
    /// <param name="maxTotalRetainedValueText">Maximum retained value text, in UTF-16 code units (D-110 guard 3).</param>
    /// <param name="locale">The draft's <c>binding.locale</c> (§5.1); inert at M5.</param>
    public static ProbeOptions Create(
        int valueRetentionLimit = 100_000,
        int maxDiscoveredAttributes = 10_000,
        long maxTotalRetainedValues = 2_000_000,
        long maxTotalRetainedValueText = 50_000_000,
        string locale = "invariant")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(valueRetentionLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDiscoveredAttributes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalRetainedValues, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalRetainedValueText, 1);
        ArgumentNullException.ThrowIfNull(locale);

        if (!IsAcceptedLocale(locale))
        {
            throw new ArgumentException(
                $"locale '{locale}' is neither \"invariant\" nor a known culture name (§5.1).", nameof(locale));
        }

        return new ProbeOptions(
            valueRetentionLimit, maxDiscoveredAttributes, maxTotalRetainedValues,
            maxTotalRetainedValueText, locale);
    }

    /// <summary>
    /// The maximum number of <b>distinct</b> cleaned non-missing values retained per attribute
    /// (D-108). Reaching it exactly is not truncation; a further distinct value beyond it is
    /// (the strictly-greater boundary).
    /// </summary>
    public int ValueRetentionLimit { get; }

    /// <summary>
    /// The maximum number of attributes a probe may discover (D-110 guard 1). For a wide
    /// source this is the schema's column count, known before any record is read.
    /// </summary>
    public int MaxDiscoveredAttributes { get; }

    /// <summary>
    /// The maximum number of retained values summed across attributes (D-110 guard 2), counted
    /// once per <em>retaining attribute</em> with no cross-attribute deduplication.
    /// </summary>
    public long MaxTotalRetainedValues { get; }

    /// <summary>
    /// The maximum retained value text (D-110 guard 3), summed as each retained string's
    /// UTF-16 code-unit length. Catches the pathology guard 2 cannot: few values, each huge.
    /// </summary>
    public long MaxTotalRetainedValueText { get; }

    /// <summary>
    /// The <c>binding.locale</c> the draft authors (§5.1). Retained verbatim as supplied — the
    /// resolve seam accepts <c>invariant</c> case-insensitively, so a caller's spelling
    /// survives into the draft rather than being silently rewritten.
    /// </summary>
    public string Locale { get; }

    // The predicate SpecResolver applies to binding.locale, deliberately duplicated rather than
    // hoisted into a shared public helper: neither package should grow a locale API for one
    // internal agreement. A cross-check test pins the two against each other, so
    // drift fails a test rather than silently producing a draft that cannot resolve itself.
    // predefinedOnly matters — under ICU, GetCultureInfo synthesizes a culture for almost any
    // well-formed tag, which would make acceptance OS-dependent (P-7/P-11).
    private static bool IsAcceptedLocale(string locale)
    {
        if (string.Equals(locale, "invariant", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
