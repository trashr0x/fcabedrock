using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// One <c>value_groups</c> group (spec §11.6, D-022/D-090): a <see cref="Label"/> plus
/// at least one matcher — an explicit <see cref="Values"/> list, a regex
/// <see cref="Pattern"/>, or both. A value matches when it equals one explicit value
/// <b>or</b> the pattern matches it (OR within a group); the discretizer walks groups
/// in declaration order and the first match wins.
/// <para>
/// <b>Authored presence survives</b> (D-094/G-11): <see cref="Values"/> is
/// <see langword="null"/> when <c>values</c> was omitted and a list — possibly
/// <b>empty</b> — when it was authored, because the §14 encoding writes <c>values</c>
/// only when authored and an authored <c>values = []</c> must be byte-distinct from an
/// omitted one. Authored order and duplicates are retained: this is authored
/// configuration, not a canonicalized set.
/// </para>
/// <para>
/// <b>Regex semantics</b> (§11.6/D-090): the pattern is compiled <b>once</b>, here,
/// with <see cref="RegexOptions.CultureInvariant"/>, default backtracking, and
/// <see cref="Regex.InfiniteMatchTimeout"/> — passed <b>explicitly</b>, because the
/// constructor overloads that omit it inherit the host's ambient
/// <c>REGEX_DEFAULT_MATCH_TIMEOUT</c>, which would make the same spec over the same
/// input complete on one machine and throw <see cref="RegexMatchTimeoutException"/>
/// on another (P-7 determinism), on the exception channel rather than the diagnostic
/// one (P-14). Matching is <b>partial</b> (unanchored
/// <see cref="Regex.IsMatch(string)"/>) and <b>case-sensitive</b> unless the author
/// writes an inline option such as <c>(?i)</c>, which is honored as part of the
/// pattern. Explicit values compare with <b>ordinal</b> equality (P-12). Nothing here
/// is trimmed, case-folded, anchored, or culture-normalized.
/// </para>
/// <para>
/// Equality follows the <see cref="ManualCutsDiscretizer"/> precedent: the compiled
/// matcher state and the snapshotted list are reference-compared, so two groups built
/// from equal arguments are not <c>Equals</c>. Compare <see cref="Label"/> /
/// <see cref="Values"/> / <see cref="Pattern"/> when identity matters.
/// </para>
/// </summary>
public sealed record ValueGroup
{
    // Compiled once at construction and reused for every observed value — the calibrator's
    // passthrough discovery and the emitter's classification share this one matcher, so the
    // two phases cannot disagree about what "matched" means (D-090).
    private readonly Regex? _regex;

    private ValueGroup(string label, IReadOnlyList<string>? values, string? pattern, Regex? regex)
    {
        Label = label;
        Values = values;
        Pattern = pattern;
        _regex = regex;
    }

    /// <summary>The authored group label — the bin label this group produces (§11.6).</summary>
    public string Label { get; }

    /// <summary>
    /// The authored explicit values in authored order with duplicates retained, or
    /// <see langword="null"/> when <c>values</c> was not authored. An authored empty list is
    /// preserved as empty (G-11) — it is a valid matcher-free half of a group whose
    /// <see cref="Pattern"/> carries the matching.
    /// </summary>
    public IReadOnlyList<string>? Values { get; }

    /// <summary>The authored regex pattern verbatim, or <see langword="null"/> when not authored.</summary>
    public string? Pattern { get; }

    /// <summary>
    /// Builds a group, validating it as the P-10 backstop behind the reader's
    /// <c>SpecFieldInvalid</c> gate (§11.6/D-090 — the reader owns the user-facing
    /// diagnostic; reaching this factory with invalid arguments is programmer error).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="label"/> is empty; any entry of <paramref name="values"/> is empty;
    /// <paramref name="pattern"/> is empty or not a valid .NET regex; or the group carries
    /// no usable matcher. The matcher predicate is exactly
    /// <c>(values is { Count: &gt; 0 }) || pattern is not null</c> — applied after the
    /// individual empty-value/empty-pattern checks — so an authored <c>values = []</c>
    /// alone is invalid while <c>values = []</c> alongside a valid pattern is valid
    /// (G-11). An authored empty list is never normalized to null.
    /// </exception>
    public static ValueGroup Create(string label, IReadOnlyList<string>? values, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (label.Length == 0)
        {
            throw new ArgumentException("a value_groups group label must be non-empty (§11.6).", nameof(label));
        }

        // Snapshot before validating, so a caller mutating its list afterwards can neither
        // slip an empty value past this gate nor change what the group matches (D-098).
        IReadOnlyList<string>? snapshot = null;
        if (values is not null)
        {
            var copy = values.ToImmutableArray();
            foreach (var value in copy)
            {
                ArgumentNullException.ThrowIfNull(value, nameof(values));
                if (value.Length == 0)
                {
                    throw new ArgumentException(
                        $"value_groups group '{label}' has an empty explicit value; every authored value must be non-empty (§11.6).",
                        nameof(values));
                }
            }

            snapshot = copy;
        }

        Regex? regex = null;
        if (pattern is not null)
        {
            if (pattern.Length == 0)
            {
                throw new ArgumentException(
                    $"value_groups group '{label}' has an empty pattern; an authored pattern must be non-empty (§11.6).",
                    nameof(pattern));
            }

            try
            {
                // InfiniteMatchTimeout is passed EXPLICITLY: the two-argument overload silently
                // inherits the host's ambient REGEX_DEFAULT_MATCH_TIMEOUT, which would make
                // matching host-dependent (P-7) and surface as an exception mid-calibrate/emit
                // rather than a diagnostic (P-14).
                regex = new Regex(pattern, RegexOptions.CultureInvariant, Regex.InfiniteMatchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    $"value_groups group '{label}' has an invalid regex pattern '{pattern}' (§11.6): {ex.Message}",
                    nameof(pattern), ex);
            }
        }

        // G-11: at least one NON-EMPTY explicit value or a non-empty pattern. The empty checks
        // above already rejected empty entries, so a surviving non-empty list qualifies.
        if (snapshot is not { Count: > 0 } && pattern is null)
        {
            throw new ArgumentException(
                $"value_groups group '{label}' carries no usable matcher; a group needs at least one explicit value or a pattern (§11.6).",
                nameof(values));
        }

        return new ValueGroup(label, snapshot, pattern, regex);
    }

    /// <summary>
    /// Whether <paramref name="value"/> belongs to this group: ordinal equality against any
    /// authored explicit value, <b>or</b> an unanchored regex match. The single matching
    /// authority — calibration discovery and emit classification both route through it.
    /// </summary>
    internal bool Matches(string value)
    {
        if (Values is { } values)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return _regex is { } regex && regex.IsMatch(value);
    }
}
