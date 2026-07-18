namespace FcaBedrock.Discovery;

/// <summary>Which D-110 aggregate guard a retention would have breached, if any.</summary>
internal enum BudgetBreach
{
    /// <summary>The retention fits; both totals were charged.</summary>
    None,

    /// <summary>Guard 2 — maximum total retained distinct values.</summary>
    Values,

    /// <summary>Guard 3 — maximum total retained value text.</summary>
    Text,
}

/// <summary>
/// The two record-pass aggregate guards of D-110 (the third — maximum discovered attributes —
/// is decided from the schema before any record is read, so it needs no running total).
/// <para>
/// <b>Logical accounting only.</b> Both totals count things the input determines — retained
/// values, and their UTF-16 code units — never an available-memory figure, so the same record
/// sequence breaches on every machine or on none (P-7/P-11).
/// </para>
/// <para>
/// <b>A breach is a hard failure, never a silent truncation.</b> Aggregate pressure must not
/// quietly shrink some other attribute's domain: only the per-attribute limit produces a
/// usable, marked, truncated draft (D-108). Here the probe stops and produces none, so a
/// runaway vocabulary can never yield a partial draft that reads as complete.
/// </para>
/// </summary>
internal sealed class RetentionBudget(long maxValues, long maxText)
{
    private long _values;
    private long _text;

    /// <summary>Retained values charged so far, across every attribute.</summary>
    public long RetainedValues => _values;

    /// <summary>Retained value text charged so far, in UTF-16 code units.</summary>
    public long RetainedText => _text;

    /// <summary>
    /// Charges one newly retained value of <paramref name="textLength"/> UTF-16 code units,
    /// or reports which guard it would have breached (values before text, D-110's precedence).
    /// Equality with a maximum is legal; only exceeding one breaches.
    /// <para>
    /// Compared before adding — <c>max - total &lt; increment</c> rather than
    /// <c>total + increment &gt; max</c> — so the sum itself is unreachable when it would
    /// overflow, whatever maxima the caller chose.
    /// </para>
    /// </summary>
    public BudgetBreach TryCharge(int textLength)
    {
        if (maxValues - _values < 1)
        {
            return BudgetBreach.Values;
        }

        if (maxText - _text < textLength)
        {
            return BudgetBreach.Text;
        }

        _values++;
        _text += textLength;
        return BudgetBreach.None;
    }
}
