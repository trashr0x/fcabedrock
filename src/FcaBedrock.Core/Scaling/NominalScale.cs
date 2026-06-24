namespace FcaBedrock.Core.Scaling;

/// <summary>
/// One formal attribute per bin; an object crosses exactly the one matching its
/// bin. Spec §12.1. For <i>N</i> bins, produces <i>N</i> formal attributes in
/// bin order.
/// </summary>
public sealed record NominalScale : Scale
{
    public override string Kind => "nominal";

    internal override IReadOnlyList<FormalAttributeShape> BuildShapes(IReadOnlyList<string> binLabels)
    {
        var shapes = new List<FormalAttributeShape>(binLabels.Count);
        foreach (var bin in binLabels)
        {
            shapes.Add(new FormalAttributeShape(ValueLabel: bin, ScaleOp: "", BinKey: bin, CrossingBins: [bin]));
        }

        return shapes;
    }
}
