using System.Collections.Immutable;

namespace FcaBedrock.Core.Calibration;

/// <summary>
/// One attribute's retained calibration outcome (manifest-ready, D-093/§15) — the
/// resolved data-dependent schema element a data-reading pass discovered.
/// The <see langword="private protected"/> base constructor blocks ordinary derivation
/// outside this assembly, but a record's protected copy constructor does not (CS8878).
/// <see cref="CalibratedSpec.Create"/> rejects an outcome of any other type, and the
/// freeze and manifest switches throw on one. Every list-bearing variant snapshots its
/// list into <see cref="ImmutableArray{T}"/>-backed storage at construction.
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
/// Resolved auto cuts (equal_width data ranges, equal_frequency). The constructor does
/// not validate them: <see cref="CalibratedSpec.Create"/> throws for a count other than
/// <c>bins - 1</c> and reports cuts that are not finite and strictly ascending as
/// <c>CalibrationCutsInvalid</c>.
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
/// The observed domain that fills an omitted <c>declared_domain</c>, in
/// first-observation order: each observed value as read, or its canonical numeric
/// identity for a numeric <c>free_per_value</c> source (D-101). May be empty (no
/// observations). Never co-occurs with <see cref="IncludeAdditions"/> for one
/// attribute: an omitted domain makes every observed value part of the observed domain.
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
/// (first-observation order). May be empty.
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
