namespace FcaBedrock.Discovery;

/// <summary>
/// One attribute's observed domain: the first <c>limit</c> distinct cleaned non-missing values
/// in first-observation order, with ordinal identity (P-12) and set-based idempotent intake
/// (D-106). Deliberately the same semantics as the calibrator's private domain observer, so a
/// probed domain and the domain a conversion of the same source calibrates are identical rather
/// than coincidentally equal — a cross-check test pins that.
/// <para>
/// <b>Bounded by construction.</b> The distinct-value set never grows past <c>limit</c>: at
/// capacity a value that is not already in it proves a further distinct value exists — which is
/// all D-108's strictly-greater rule permits probe to claim — and is then dropped rather than
/// remembered. Probe therefore never knows, and never reports, an exact over-limit count;
/// counting distinct values would require the very retention the limit exists to bound.
/// </para>
/// </summary>
internal sealed class RetainedDomain(int limit)
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _values = [];

    /// <summary>
    /// Whether more than <c>limit</c> distinct values were observed. Equality with the limit is
    /// <b>not</b> truncation (D-108): a domain that fits exactly is complete.
    /// </summary>
    public bool Truncated { get; private set; }

    /// <summary>The retained values, in first-observation order.</summary>
    public IReadOnlyList<string> Values => _values;

    /// <summary>
    /// Classifies <paramref name="value"/> for retention without retaining it, returning true
    /// exactly when it is genuinely new and there is room. Split from <see cref="Retain"/> so
    /// the caller can charge the aggregate budget in between and abort on a breach without ever
    /// leaving this domain half-updated. A repeat costs one lookup and changes nothing —
    /// repeated values affect neither order nor accounting.
    /// </summary>
    public bool TryReserve(string value)
    {
        if (_seen.Contains(value))
        {
            return false;
        }

        if (_values.Count == limit)
        {
            Truncated = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Retains a value <see cref="TryReserve"/> has just approved, appending it after every
    /// earlier one so the retained order is first-observation order (§17 rule 3).
    /// </summary>
    public void Retain(string value)
    {
        _seen.Add(value);
        _values.Add(value);
    }
}
