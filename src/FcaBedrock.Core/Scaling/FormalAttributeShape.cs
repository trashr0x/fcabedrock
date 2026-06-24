namespace FcaBedrock.Core.Scaling;

/// <summary>
/// A scale's plan-time description of one formal attribute, before the planner
/// renders its name, assigns its id, and inverts its crossings. Internal: the
/// contract between a <see cref="Scale"/> and the planner, not a public surface.
/// </summary>
/// <param name="ValueLabel">
/// The value side of the name (a bin label), or <see langword="null"/> when the
/// formal attribute carries no value (dichotomic — name is the column alone).
/// </param>
/// <param name="ScaleOp">The ordinal operator (e.g. <c>&gt;=</c>); empty otherwise.</param>
/// <param name="BinKey">The canonical bin/threshold key, for identity and collisions.</param>
/// <param name="CrossingBins">The bin labels for which this formal attribute crosses.</param>
internal sealed record FormalAttributeShape(
    string? ValueLabel,
    string ScaleOp,
    string BinKey,
    IReadOnlyList<string> CrossingBins);
