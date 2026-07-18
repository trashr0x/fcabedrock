using FcaBedrock.Sources;

namespace FcaBedrock.Discovery;

/// <summary>
/// The wide observation pass state: one <see cref="RetainedDomain"/> per physical schema
/// column, sharing one <see cref="RetentionBudget"/>.
/// <para>
/// <b>Observation is set-based and idempotent</b> (D-106): a record contributes each of its
/// cleaned present values to that column's domain, and a value already seen changes nothing —
/// not the order, not either total. That is what makes "one record pass" sufficient and makes a
/// probed domain equal to the domain a conversion of the same source calibrates.
/// </para>
/// <para>
/// <b>Cleaned values only.</b> The session has already applied the §5.1 quote-aware trim and
/// missing normalization, so a <see langword="null"/> field here is missing — an empty cell, a
/// cell equal to the effective <c>missing_token</c>, or a cell a ragged short row never reached
/// — and a missing value is not an observation. Discovery re-does none of that: it does not
/// tokenize, trim, or match the missing token itself.
/// </para>
/// </summary>
internal sealed class WideObservation
{
    private readonly RetainedDomain[] _domains;
    private readonly RetentionBudget _budget;

    public WideObservation(int columnCount, ProbeOptions options)
    {
        _domains = new RetainedDomain[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            _domains[i] = new RetainedDomain(options.ValueRetentionLimit);
        }

        _budget = new RetentionBudget(options.MaxTotalRetainedValues, options.MaxTotalRetainedValueText);
    }

    /// <summary>The domain observed for a physical column.</summary>
    public RetainedDomain Domain(int column) => _domains[column];

    /// <summary>
    /// Observes one cleaned record, returning the guard it breached — at which point the probe
    /// must stop and produce no draft (D-110).
    /// </summary>
    public BudgetBreach Observe(ObjectRecord record)
    {
        // The ordered schema is the whole addressing space: a ragged LONG row's extra fields
        // cannot create an attribute, because attributes are the schema's columns and nothing
        // else (D-107), and a ragged SHORT row's absent cells are already null — missing, not an
        // error (§5.4, D-085).
        var columns = Math.Min(_domains.Length, record.FieldCount);
        for (var column = 0; column < columns; column++)
        {
            if (record.Field(column) is not { } value)
            {
                continue;
            }

            var domain = _domains[column];
            if (!domain.TryReserve(value))
            {
                continue;
            }

            // Charged before retention so a breach leaves this domain exactly as it was. The
            // draft is discarded either way, but a half-updated domain would be a trap for the
            // next reader.
            var breach = _budget.TryCharge(value.Length);
            if (breach is not BudgetBreach.None)
            {
                return breach;
            }

            domain.Retain(value);
        }

        return BudgetBreach.None;
    }
}
