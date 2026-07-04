namespace FcaBedrock.Core.Scaling;

/// <summary>
/// A modelled-but-deferred scale (spec §12.4/§20, D-010): <c>interordinal</c>,
/// <c>biordinal</c>, or <c>contranominal</c>. A reject-carrier only, mirroring
/// the D-072 triple pattern: it exists so a resolved spec can carry the authored
/// kind to <c>ConversionPlanner</c>, which refuses it with
/// <c>ScaleNotImplementedV1</c> (Fatal) before any planning —
/// parse-but-fail-to-plan. Never produces shapes.
/// </summary>
public sealed record UnimplementedScale : Scale
{
    /// <summary>Creates the marker for the authored deferred <paramref name="kind"/>.</summary>
    public UnimplementedScale(string kind) => Kind = kind;

    /// <inheritdoc />
    public override string Kind { get; }

    internal override IReadOnlyList<FormalAttributeShape> BuildShapes(BinScheme bins) =>
        throw new InvalidOperationException(
            $"Scale kind '{Kind}' is rejected at plan (ScaleNotImplementedV1); shapes are never built.");
}
