using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Discovery;

/// <summary>
/// The triple observation pass state: one <see cref="RetainedDomain"/> per discovered predicate
/// in first-appearance order, sharing one <see cref="RetentionBudget"/>, plus the structural
/// subject checks that keep a draft convertible.
/// <para>
/// <b>Same observation semantics as wide</b> (D-106): set-based, idempotent, ordinal identity,
/// first-observation order, and cleaned values taken as the seam delivers them — no
/// retokenizing, trimming, missing-normalization, or typing here. The difference is only
/// <em>where</em> attributes come from: wide's are the schema's columns, known before the pass;
/// triple's are the predicates, discovered <em>during</em> it — which is why the attribute guard
/// is charged here rather than from the schema (D-110).
/// </para>
/// <para>
/// <b>Structural validation is symmetric with conversion</b> (D-106/D-111). Subject usability is
/// checked on <em>every</em> row under <em>both</em> orderings, before predicate filtering,
/// through the shared Core predicate the calibrate and emit halts use — otherwise probe could
/// author a draft that its own same-source conversion rejects (D-107). Contiguity is checked only
/// under an explicitly selected <c>subject_grouped</c>: under <c>unordered</c> interleaved
/// subjects are legal, and probe runs no grouping pass to make them contiguous.
/// </para>
/// </summary>
internal sealed class TripleObservation(ProbeOptions options, bool subjectGrouped)
{
    private readonly Dictionary<string, RetainedDomain> _domains = new(StringComparer.Ordinal);
    private readonly List<string> _predicates = [];
    private readonly RetentionBudget _budget =
        new(options.MaxTotalRetainedValues, options.MaxTotalRetainedValueText);

    // The contiguity state. `_completed` is D-110's inherited P-16 bounded-metadata carve-out —
    // the object-names class the converter already retains — so it is charged to no guard and
    // creates no fourth one. It stays empty under `unordered`, where nothing consults it.
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private string? _currentSubject;
    private bool _started;

    /// <summary>The discovered predicates, in first-appearance order.</summary>
    public IReadOnlyList<string> Predicates => _predicates;

    /// <summary>The domain observed for a discovered predicate.</summary>
    public RetainedDomain Domain(string predicate) => _domains[predicate];

    /// <summary>
    /// Observes one cleaned row, returning the diagnostic that must end the pass — a structural
    /// subject problem or a breached guard — or null to continue. Every one of those outcomes
    /// yields no draft (D-107/D-110).
    /// </summary>
    public BedrockDiagnostic? Observe(TripleRow row)
    {
        // Before predicate filtering, deliberately: a row whose predicate this probe would ignore
        // still carries a subject, and conversion would halt on it. Checking after the filter
        // would let an unusable subject through whenever its predicate happened to be missing.
        if (!ObjectNameValidity.IsUsable(row.Subject))
        {
            return ProbeDiagnostics.SubjectUnusable(row.RecordIndex);
        }

        var subject = row.Subject!;
        if (subjectGrouped)
        {
            if (!_started)
            {
                _currentSubject = subject;
                _started = true;
            }
            else if (!string.Equals(subject, _currentSubject, StringComparison.Ordinal))
            {
                _completed.Add(_currentSubject!);
                if (_completed.Contains(subject))
                {
                    return ProbeDiagnostics.SubjectNotContiguous(subject, row.RecordIndex);
                }

                _currentSubject = subject;
            }
        }

        // §7.1: an empty or missing predicate is ignored — it names no attribute, and it is not
        // an error. The seam has already normalized empty and missing-token cells to null; the
        // empty check is the belt to that braces for a hand-written session.
        if (string.IsNullOrEmpty(row.Predicate))
        {
            return null;
        }

        var predicate = row.Predicate;
        if (!_domains.TryGetValue(predicate, out var domain))
        {
            // Guard 1, charged as attributes are discovered rather than counted up front, and
            // charged BEFORE the value is retained so the guard precedence is
            // attributes → values → text (D-110). Equality is legal; only exceeding breaches.
            //
            // The DYNAMIC message, not wide's exact-count one: stopping right here is what keeps
            // the guard bounded, and it is also exactly what leaves the source's real predicate
            // count unknown — so the diagnostic claims only the bound it can justify.
            if (_predicates.Count == options.MaxDiscoveredAttributes)
            {
                return ProbeDiagnostics.DynamicAttributeLimitExceeded(options.MaxDiscoveredAttributes);
            }

            domain = new RetainedDomain(options.ValueRetentionLimit);
            _domains.Add(predicate, domain);
            _predicates.Add(predicate);
        }

        // A predicate is discovered even when its value is missing: the attribute is real, and an
        // all-missing one simply authors no domain (§10.3, D-107). Missing values enter neither
        // retention nor the guard totals.
        if (row.Value is not { } value || !domain.TryReserve(value))
        {
            return null;
        }

        // Charged before retention so a breach leaves this domain exactly as it was — see
        // WideObservation for why a half-updated domain would be a trap.
        var breach = _budget.TryCharge(value.Length);
        if (breach is not BudgetBreach.None)
        {
            return breach is BudgetBreach.Values
                ? ProbeDiagnostics.ValueLimitExceeded(options.MaxTotalRetainedValues)
                : ProbeDiagnostics.TextLimitExceeded(options.MaxTotalRetainedValueText);
        }

        domain.Retain(value);
        return null;
    }
}
