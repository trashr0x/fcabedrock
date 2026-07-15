using System.Collections.Immutable;

namespace FcaBedrock.Core.Calibration;

/// <summary>
/// One attribute's retained calibration outcome (manifest-ready, D-093/§15) — the
/// resolved data-dependent schema element a data-reading pass discovered.
/// Mechanically closed (the <see langword="private protected"/> base constructor
/// admits no out-of-assembly variant), so the substitution/manifest switches are
/// exhaustive. Every list-bearing variant snapshots its list into
/// <see cref="ImmutableArray{T}"/>-backed storage at construction.
/// </summary>
public abstract record AttributeCalibration
{
    private protected AttributeCalibration(string attributeName)
    {
        ArgumentNullException.ThrowIfNull(attributeName);
        AttributeName = attributeName;
    }

    /// <summary>The attribute this outcome resolves.</summary>
    public string AttributeName { get; }
}

/// <summary>
/// Resolved auto cuts (equal_width data ranges, equal_frequency), ascending and
/// finite. Landed with the M4 numeric-discretizer slices; retained here for the
/// exhaustive union.
/// </summary>
public sealed record CalibratedCuts : AttributeCalibration
{
    /// <summary>Records the resolved <paramref name="cuts"/> for <paramref name="attributeName"/>.</summary>
    public CalibratedCuts(string attributeName, IReadOnlyList<double> cuts) : base(attributeName)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        Cuts = cuts.ToImmutableArray();
    }

    /// <summary>The resolved cut values.</summary>
    public IReadOnlyList<double> Cuts { get; }
}

/// <summary>
/// The observed domain that fills an absent <c>declared_domain</c> (raw
/// first-observation order). May be empty (no observations). Never co-occurs with
/// <see cref="IncludeAdditions"/> for one attribute — an absent domain makes every
/// observed value part of the observed domain.
/// </summary>
public sealed record ObservedDomain : AttributeCalibration
{
    /// <summary>Records the observed <paramref name="values"/> for <paramref name="attributeName"/>.</summary>
    public ObservedDomain(string attributeName, IReadOnlyList<string> values) : base(attributeName)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values.ToImmutableArray();
    }

    /// <summary>The observed domain values, in first-observation order.</summary>
    public IReadOnlyList<string> Values { get; }
}

/// <summary>
/// The <c>unknown_value_policy = "include"</c> additions (appended after the
/// declared values, first-observation order). An empty list is the zero-additions
/// marker.
/// </summary>
public sealed record IncludeAdditions : AttributeCalibration
{
    /// <summary>Records the appended <paramref name="values"/> for <paramref name="attributeName"/>.</summary>
    public IncludeAdditions(string attributeName, IReadOnlyList<string> values) : base(attributeName)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values.ToImmutableArray();
    }

    /// <summary>The values appended to the declared domain, in first-observation order.</summary>
    public IReadOnlyList<string> Values { get; }
}

/// <summary>
/// The <c>value_groups</c> <c>unmatched = "passthrough"</c> discovered bins
/// (first-observation order). May be empty. Landed with the M4 value_groups slice;
/// retained here for the exhaustive union.
/// </summary>
public sealed record PassthroughBins : AttributeCalibration
{
    /// <summary>Records the discovered passthrough <paramref name="values"/> for <paramref name="attributeName"/>.</summary>
    public PassthroughBins(string attributeName, IReadOnlyList<string> values) : base(attributeName)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values.ToImmutableArray();
    }

    /// <summary>The discovered passthrough bin values, in first-observation order.</summary>
    public IReadOnlyList<string> Values { get; }
}
