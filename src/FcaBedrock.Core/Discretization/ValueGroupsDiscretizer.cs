using System.Collections.Immutable;

namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Many-to-one value grouping (spec §11.6, D-022/D-055/D-090): each raw value takes the
/// label of the <b>first</b> declared group that matches it, and
/// <see cref="Unmatched"/> decides the rest. Declaration order is therefore
/// <b>semantic</b>, not presentation — a value matching several groups falls into the
/// first (§11.6), and that choice is captured in the schema fingerprint.
/// <para>
/// <c>declared_domain</c> is <b>not</b> consulted (D-055): the groups plus the
/// <c>unmatched</c> policy define recognition, so a domain would be a second
/// overlapping gate. <c>value_labels</c> is likewise dormant (§10.8/D-049) — a group
/// label already <i>is</i> the display label. Both defaults come from the base
/// (<c>ConsumesDeclaredDomain</c> / <c>ConsultsValueLabels</c> stay false), and its bins
/// are ordinary <b>value</b> bins, so <c>DescribeBins</c> uses the value-bin base too
/// (no cut geometry).
/// </para>
/// <para>
/// <b>Passthrough is data-dependent</b> and cannot be constructed by
/// <see cref="Create"/>: its bins are discovered by the Calibrate phase, so it resolves
/// to a <c>CalibrationPending</c> carrier and reaches its executable form only through
/// <see cref="CreatePassthrough"/>, called by <c>CalibratedSpec.Create</c> over the
/// retained <c>PassthroughBins</c> outcome (D-093).
/// </para>
/// </summary>
public sealed record ValueGroupsDiscretizer : Discretizer
{
    /// <summary>The synthetic bin an <see cref="ValueGroupsUnmatched.Other"/> policy adds (§11.6).</summary>
    private const string OtherLabel = "Other";

    // Iterated on the emit hot path, so the struct enumerator (not the boxed interface) is
    // what Discretize walks — no per-value allocation (P-18).
    private readonly ImmutableArray<ValueGroup> _groups;
    private readonly ImmutableArray<string> _passthroughBins;
    private readonly ImmutableArray<string> _binLabels;

    private ValueGroupsDiscretizer(
        ImmutableArray<ValueGroup> groups, ValueGroupsUnmatched unmatched, ImmutableArray<string> passthroughBins)
    {
        _groups = groups;
        Unmatched = unmatched;
        _passthroughBins = passthroughBins;

        // §17 rule 3: declared groups in declaration order, then the synthetic Other, or the
        // discovered passthrough bins in first-observation order. Fixed at construction — the
        // bin universe cannot drift from what Discretize produces.
        var labels = ImmutableArray.CreateBuilder<string>(groups.Length + passthroughBins.Length + 1);
        foreach (var group in groups)
        {
            labels.Add(group.Label);
        }

        if (unmatched == ValueGroupsUnmatched.Other)
        {
            labels.Add(OtherLabel);
        }

        labels.AddRange(passthroughBins);
        _binLabels = labels.ToImmutable();
    }

    /// <summary>The declared groups in <b>declaration order</b> — first match wins, so the order is semantic.</summary>
    public IReadOnlyList<ValueGroup> Groups => _groups;

    /// <summary>The resolved (authored or defaulted) policy for values matching no group.</summary>
    public ValueGroupsUnmatched Unmatched { get; }

    /// <summary>
    /// The calibrated pass-through bins in first-observation order (§17 rule 3), or empty
    /// under <see cref="ValueGroupsUnmatched.Skip"/>/<see cref="ValueGroupsUnmatched.Other"/>.
    /// Empty is also the legal zero-discovery outcome of a passthrough calibration.
    /// </summary>
    public IReadOnlyList<string> PassthroughBins => _passthroughBins;

    /// <summary>
    /// Builds the spec-determined form (<see cref="ValueGroupsUnmatched.Skip"/> or
    /// <see cref="ValueGroupsUnmatched.Other"/>) — the P-10 backstop behind the seam's
    /// clean diagnostic gate (the resolver owns the user-facing
    /// <c>ValueGroupsLabelDuplicate</c>, §11.6/D-090).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="unmatched"/> is <see cref="ValueGroupsUnmatched.Passthrough"/> (a
    /// data-dependent state that must go through calibration) or undefined; a group is null;
    /// two groups share a label under ordinal equality; or a group's label collides with the
    /// synthetic <c>Other</c> under <see cref="ValueGroupsUnmatched.Other"/>.
    /// </exception>
    public static ValueGroupsDiscretizer Create(IReadOnlyList<ValueGroup> groups, ValueGroupsUnmatched unmatched)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (!Enum.IsDefined(unmatched))
        {
            throw new ArgumentException(
                $"value_groups unmatched holds undefined enum value {(int)unmatched}.", nameof(unmatched));
        }

        if (unmatched == ValueGroupsUnmatched.Passthrough)
        {
            throw new ArgumentException(
                "value_groups unmatched = \"passthrough\" discovers its bins from data and cannot be constructed directly; " +
                "resolve it to a CalibrationPending carrier and let CalibratedSpec.Create substitute the calibrated form (§11.6/D-093).",
                nameof(unmatched));
        }

        var snapshot = Snapshot(groups, unmatched);
        return new ValueGroupsDiscretizer(snapshot, unmatched, ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// Builds the calibrated pass-through form over the authored <paramref name="groups"/> and
    /// the discovered <paramref name="passthroughBins"/> (first-observation order). Internal:
    /// the only caller is <c>CalibratedSpec.Create</c>'s substitution, so this state cannot be
    /// minted outside the calibrate→plan sequence (D-093).
    /// <para>
    /// A discovered bin equal to an authored group label is deliberately <b>not</b> rejected
    /// here — that collision is data-dependent, so it belongs to plan as
    /// <c>FormalAttributeCollision</c> (§11.6/D-090), not to a construction backstop.
    /// </para>
    /// </summary>
    internal static ValueGroupsDiscretizer CreatePassthrough(
        IReadOnlyList<ValueGroup> groups, IReadOnlyList<string> passthroughBins)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(passthroughBins);

        var snapshot = Snapshot(groups, ValueGroupsUnmatched.Passthrough);
        return new ValueGroupsDiscretizer(snapshot, ValueGroupsUnmatched.Passthrough, passthroughBins.ToImmutableArray());
    }

    // The shared group snapshot + label-distinctness gate. Ordinal throughout (P-12): "Other"
    // collides with the synthetic bin, "other" does not.
    private static ImmutableArray<ValueGroup> Snapshot(IReadOnlyList<ValueGroup> groups, ValueGroupsUnmatched unmatched)
    {
        var snapshot = groups.ToImmutableArray();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in snapshot)
        {
            ArgumentNullException.ThrowIfNull(group, nameof(groups));
            if (!labels.Add(group.Label))
            {
                throw new ArgumentException(
                    $"value_groups declares the label '{group.Label}' more than once; authored group labels must be distinct (§11.6).",
                    nameof(groups));
            }

            if (unmatched == ValueGroupsUnmatched.Other && string.Equals(group.Label, OtherLabel, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"value_groups declares a group labelled '{OtherLabel}', which collides with the synthetic bin unmatched = \"other\" adds (§11.6).",
                    nameof(groups));
            }
        }

        return snapshot;
    }

    /// <inheritdoc/>
    public override string Kind => "value_groups";

    /// <inheritdoc/>
    public override BinResult Discretize(string rawValue)
    {
        // §11.6: declaration order, first match wins.
        foreach (var group in _groups)
        {
            if (group.Matches(rawValue))
            {
                return BinResult.Bin(group.Label);
            }
        }

        return Unmatched switch
        {
            // No bin of its own — unknown_value_policy governs it (§10.6). Because there is no
            // declared_domain to extend (D-055), `include` yields a Warning and no schema
            // extension: it behaves as `warn` (§11.6/D-090), which falls out of this Unknown
            // rather than being a special case anywhere.
            ValueGroupsUnmatched.Skip => BinResult.Unknown(rawValue),
            ValueGroupsUnmatched.Other => BinResult.Bin(OtherLabel),

            // The raw spelling is its own bin. The planned KnownBins gate turns a value that
            // calibration never discovered — a between-pass data change — into an unknown.
            ValueGroupsUnmatched.Passthrough => BinResult.Bin(rawValue),
            _ => throw new InvalidOperationException($"unknown value_groups unmatched policy {Unmatched}."),
        };
    }

    // §17 rule 3, fixed at construction. value_groups ignores declared_domain (D-055), so the
    // parameter is unread — the groups and the unmatched policy are the whole bin universe.
    internal override IReadOnlyList<string> BinLabels(IReadOnlyList<string> declaredDomain) => _binLabels;
}
