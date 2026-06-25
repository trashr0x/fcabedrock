using FcaBedrock.Core.Spec;
using FcaBedrock.Spec;

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

    // The discrete-vs-progressive scaling mode the v2 .bed never recorded (the two
    // ordinal .bed files are byte-identical); supplied out-of-band like the Binding.
    public ScalingMode ScalingMode { get; init; } = ScalingMode.Discrete;

    public static Binding Wide(char delimiter, bool hasHeader) =>
        new(SourceShape.Wide, delimiter, '"', hasHeader, "invariant", "?", new RowIndexObjectKey());

    public static IReadOnlyList<FixtureCase> Active { get; } =
    [
        new("mini-mushroom", "mini-mushroom", Wide(',', hasHeader: true)),
        new("mini-mushroom", "mini-mushroom_tabbed_noheader", Wide('\t', hasHeader: false)),
        new("mini-adult", "mini-adult", Wide(',', hasHeader: true)),
        new("mini-adult", "mini-adult_noheader", Wide(',', hasHeader: false)),
        new("mini-adult", "mini-adult_employment_ordinal_discrete", Wide(',', hasHeader: true)) { DataVariant = "mini-adult" },
        new("mini-adult", "mini-adult_employment_ordinal_progressive", Wide(',', hasHeader: true))
        {
            DataVariant = "mini-adult",
            ScalingMode = ScalingMode.Progressive,
        },
    ];

    public override string ToString() => Variant;
}
