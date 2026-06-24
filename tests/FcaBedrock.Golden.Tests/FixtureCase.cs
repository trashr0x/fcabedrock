using FcaBedrock.Core.Spec;

namespace FcaBedrock.Golden.Tests;

// One activated golden fixture: its .bed, .data, expected outputs, and the binding
// the v2 .bed never recorded (delimiter/header/shape are supplied here, per the
// fixtures README's sanction for a typed fixture-case list). The active set grows
// per milestone — mini-adult joins next M1 slice, triples at M3, dates stay parked.
public sealed record FixtureCase(string Family, string Variant, Binding Binding)
{
    private string Dir => Path.Combine(FixturePaths.V2Root, Family);

    public string BedPath => Path.Combine(Dir, $"{Variant}.bed");

    public string DataPath => Path.Combine(Dir, $"{DataVariant}.data");

    public string ExpectedCxtPath => Path.Combine(Dir, "expected", $"{Variant}.cxt");

    public string ExpectedDatPath => Path.Combine(Dir, "expected", $"{Variant}.dat");

    // A variant may reuse another variant's .data (fixtures README); default to its own.
    public string DataVariant { get; init; } = Variant;

    public static Binding Wide(char delimiter, bool hasHeader) =>
        new(SourceShape.Wide, delimiter, '"', hasHeader, "invariant", "?", new RowIndexObjectKey());

    public static IReadOnlyList<FixtureCase> Active { get; } =
    [
        new("mini-mushroom", "mini-mushroom", Wide(',', hasHeader: true)),
        new("mini-mushroom", "mini-mushroom_tabbed_noheader", Wide('\t', hasHeader: false)),
    ];

    public override string ToString() => Variant;
}
