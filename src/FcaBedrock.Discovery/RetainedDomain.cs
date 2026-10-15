namespace FcaBedrock.Discovery;

/// <summary>What <see cref="RetainedDomain.Reserve"/> decided for one observed value.</summary>
internal enum Reservation
{
    /// <summary>The value is already retained, so it changes nothing.</summary>
    Repeat,

    /// <summary>The value is new and fits: the caller charges the budget, then retains it.</summary>
    Reserved,

    /// <summary>
    /// The value is new but the domain is full, so the domain is truncated and the value is
    /// dropped. Every later new value, a repeat of an earlier dropped one included, is
    /// discarded again, because dropped values are not remembered.
    /// </summary>
    Discarded,
}

/// <summary>
/// One attribute's observed domain: the first <c>limit</c> distinct cleaned non-missing values
/// in first-observation order, with ordinal identity (EP-12) and set-based idempotent intake
/// (D-106). Deliberately the same semantics as the calibrator's private domain observer, so a
/// probed domain and the domain a conversion of the same source calibrates are identical rather
/// than coincidentally equal; a cross-check test pins that.
/// <para>
/// <b>Bounded by construction.</b> The distinct-value set never grows past <c>limit</c>: at
/// capacity a value that is not already in it proves a further distinct value exists (which is
/// all D-108's strictly-greater rule permits probe to claim) and is then dropped rather than
/// remembered. Probe therefore never knows, and never reports, an exact over-limit count;
/// counting distinct values would require the very retention the limit exists to bound.
/// </para>
/// <para>
/// <b>Observation and the saved domain are different things.</b> A value containing CR or LF is
/// observed and retained like any other: it keeps its place in the first-observation order and
/// is charged to the aggregate guards, and it can make the domain truncated. Only the draft
/// leaves it out of the saved domain, and only when the domain is complete (§7.1).
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

    /// <summary>
    /// Whether any observed value contained CR or LF: retained, repeated, the first one over
    /// the limit or a later discarded one. Once true it stays true. A truncated domain with such
    /// a value yields no draft, because <c>include</c> would recover the value at conversion
    /// (§7.1).
    /// </summary>
    public bool HasUnsafeValue { get; private set; }

    /// <summary>The retained values, in first-observation order.</summary>
    public IReadOnlyList<string> Values => _values;

    /// <summary>
    /// Classifies <paramref name="value"/> for retention without retaining it. Split from
    /// <see cref="Retain"/> so the caller can charge the aggregate budget in between and abort
    /// on a breach without ever leaving this domain half-updated. A repeat costs one lookup and
    /// changes nothing: repeated values affect neither order nor accounting.
    /// <para>
    /// The CR/LF check comes first, so it sees every observation, including the repeats and
    /// discarded values that never reach the retained list.
    /// </para>
    /// </summary>
    public Reservation Reserve(string value)
    {
        if (!HasUnsafeValue && ContainsLineBreak(value))
        {
            HasUnsafeValue = true;
        }

        if (_seen.Contains(value))
        {
            return Reservation.Repeat;
        }

        if (_values.Count == limit)
        {
            Truncated = true;
            return Reservation.Discarded;
        }

        return Reservation.Reserved;
    }

    /// <summary>
    /// Retains a value <see cref="Reserve"/> has just approved, appending it after every
    /// earlier one so the retained order is first-observation order (§17 rule 3).
    /// </summary>
    public void Retain(string value)
    {
        _seen.Add(value);
        _values.Add(value);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a retained value, compared ordinally without
    /// allocating a string for it.
    /// </summary>
    public bool ContainsRetained(ReadOnlySpan<char> value) =>
        _seen.GetAlternateLookup<ReadOnlySpan<char>>().Contains(value);

    /// <summary>
    /// Whether <paramref name="value"/> contains CR or LF: a category that cannot name a formal
    /// attribute (§10.7), so a draft never saves it.
    /// </summary>
    public static bool ContainsLineBreak(string value) => value.AsSpan().IndexOfAny('\r', '\n') >= 0;
}
