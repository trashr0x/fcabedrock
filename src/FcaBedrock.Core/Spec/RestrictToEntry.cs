namespace FcaBedrock.Core.Spec;

/// <summary>
/// One <c>restrict_to</c> entry: an object-level filter on raw values, applied
/// before discretization (spec §10.4). A resolved carrier only in this slice
/// (D-057): the planner does not read it and conversion runs unfiltered. The
/// plan-phase reject (<c>RestrictToNotImplementedV1</c>) and shape validation
/// (D-063) land with the M2 validation slice; execution is M4.
/// </summary>
public abstract record RestrictToEntry;

/// <summary>Keeps objects whose raw value equals <paramref name="Value"/> (categorical form, §10.4).</summary>
/// <param name="Value">The raw value to match.</param>
public sealed record RestrictToValue(string Value) : RestrictToEntry;

/// <summary>
/// Keeps objects whose numeric raw value lies in [<paramref name="From"/>,
/// <paramref name="To"/>) — low-inclusive, high-exclusive, either end open when
/// null (§10.4).
/// </summary>
/// <param name="From">Inclusive lower bound; open when null.</param>
/// <param name="To">Exclusive upper bound; open when null.</param>
public sealed record RestrictToRange(double? From, double? To) : RestrictToEntry;
