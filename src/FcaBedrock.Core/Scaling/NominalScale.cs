namespace FcaBedrock.Core.Scaling;

/// <summary>
/// One formal attribute per bin; an object crosses exactly the one matching its
/// bin. Spec §12.1. For <i>N</i> bins, produces <i>N</i> formal attributes in
/// bin order.
/// </summary>
public sealed record NominalScale : Scale
{
    public override string Kind => "nominal";

    internal override IReadOnlyList<FormalAttributeShape> BuildShapes(BinScheme bins)
    {
        var shapes = new List<FormalAttributeShape>(bins.Labels.Count);
        for (var i = 0; i < bins.Labels.Count; i++)
        {
            var bin = bins.Labels[i];
            shapes.Add(new FormalAttributeShape(
                ValueLabel: bin, ScaleOp: "", BinKey: bin, Bin: bins.Bins[i], CrossingBins: [bin]));
        }

        return shapes;
    }
}
