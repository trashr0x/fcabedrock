namespace FcaBedrock.Core.Scaling;

/// <summary>
/// One formal attribute total; it crosses iff the object's bin equals
/// <see cref="TrueValue"/>. Spec §12.2. The default name is the column alone
/// (no value suffix — §10.7), matching v2's <c>bruises?</c> / <c>US-citizen</c>.
/// </summary>
public sealed record DichotomicScale(string TrueValue) : Scale
{
    public override string Kind => "dichotomic";

    internal override IReadOnlyList<FormalAttributeShape> BuildShapes(BinScheme bins) =>
        [new FormalAttributeShape(ValueLabel: null, ScaleOp: "", BinKey: "", Bin: new ValueBin(""), CrossingBins: [TrueValue])];
}
