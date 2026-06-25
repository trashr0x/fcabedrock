namespace FcaBedrock.Core.Discretization;

/// <summary>
/// Whether a cut discretizer's outer bins extend to ±∞ (<see cref="Open"/>) or
/// are dropped so out-of-range values get no bin (<see cref="Closed"/>). Spec
/// §11.2. Three cuts produce four bins when <see cref="Open"/>, two when
/// <see cref="Closed"/>.
/// </summary>
public enum BinEnds
{
    /// <summary>Outer bins extend to ±∞: <c>&lt;c0</c> and <c>&gt;=cn</c> are produced.</summary>
    Open,

    /// <summary>Only interior bins; values outside the cuts' range get no bin.</summary>
    Closed,
}
