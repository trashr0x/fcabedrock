using System.Globalization;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Export;

namespace FcaBedrock.Golden.Tests;

// Focused triple-conversion tests complementing the byte-equality theories in
// GoldenFixtureTests: they lock the triple factory defaults, first-appearance object
// order through the production route, independent-run repeatability, and the D-087
// shape-derived .dat final-newline rule.
public sealed class TripleGoldenTests
{
    private static readonly string[] TripleVariants =
        ["mini-mushroom_triples", "mini-adult_triples", "mini-adult_triples_named"];

    public static IEnumerable<object[]> TripleCases =>
        TripleVariants.Select(v => new object[] { FixtureCase.Active.Single(c => c.Variant == v) });

    private static FixtureCase Case(string variant) => FixtureCase.Active.Single(c => c.Variant == variant);

    [Fact]
    public void Triple_WhenAuthored_ThenTripleShapeUnorderedHeaderlessDefaults()
    {
        var binding = FixtureCase.Triple(',', hasHeader: false);

        Assert.Equal(SourceShape.Triple, binding.Shape);
        Assert.Equal(TripleOrdering.Unordered, binding.Ordering);
        Assert.False(binding.HasHeader);
        Assert.Equal(',', binding.Delimiter);
        Assert.Null(binding.Columns);   // normative 0/1/2 (the resolver default)
        Assert.Null(binding.ObjectKey); // the object key is always the subject (§5.4)
    }

    [Fact]
    public async Task NamedTriple_WhenConverted_ThenFirstAppearanceObjectOrder()
    {
        // Through the production route (raw TripleCsvSource -> planner TripleExecution ->
        // Emitter.EmitTripleAsync via PrepareAsync): object names are the first appearance
        // of each subject (§17 rule 4 / D-082), never sorted — a sorted order would start
        // "Alice". Complements the synthetic UnorderedTripleEmitterTests by locking the real
        // fixture bytes' object order.
        var cxt = Encoding.UTF8.GetString(await GoldenConversion.WriteCxtAsync(Case("mini-adult_triples_named"), WriterOptions.Native));
        var lines = cxt.Split('\n');
        var objectCount = int.Parse(lines[2], CultureInfo.InvariantCulture);

        Assert.Equal(
            ["Sam", "Jenny", "John", "Andrew", "Mary", "Laura", "Alice", "Tim"],
            lines.Skip(5).Take(objectCount));
    }

    [Theory]
    [MemberData(nameof(TripleCases))]
    public async Task TripleCxt_WhenConvertedTwiceIndependently_ThenByteIdentical(FixtureCase fixture)
    {
        // Two fully independent conversions (fresh source/stream/session each): run 1 must
        // equal the golden bytes and run 2 must equal run 1 — determinism, nothing compared
        // to itself.
        var golden = await File.ReadAllBytesAsync(fixture.ExpectedCxtPath);
        var run1 = await GoldenConversion.WriteCxtAsync(fixture, WriterOptions.V2Compat);
        var run2 = await GoldenConversion.WriteCxtAsync(fixture, WriterOptions.V2Compat);

        Assert.Equal(golden, run1);
        Assert.Equal(run1, run2);
    }

    [Theory]
    [MemberData(nameof(TripleCases))]
    public async Task TripleDat_WhenConvertedTwiceIndependently_ThenByteIdentical(FixtureCase fixture)
    {
        var golden = await File.ReadAllBytesAsync(fixture.ExpectedDatPath);
        var run1 = await GoldenConversion.WriteDatAsync(fixture, WriterOptions.V2Compat);
        var run2 = await GoldenConversion.WriteDatAsync(fixture, WriterOptions.V2Compat);

        Assert.Equal(golden, run1);
        Assert.Equal(run1, run2);
    }

    [Fact]
    public async Task Dat_WhenV2Compat_ThenFinalNewlineDerivesFromShape()
    {
        // D-087 on produced bytes (DatOptionsFor stays private): under v2-compat a wide .dat
        // ends with the trailing CRLF, while its triple twin is byte-identical *minus* that
        // final CRLF (v2's triple converter wrote no final terminator). Native triple output
        // ends with a single LF, not CRLF.
        var wideV2 = await GoldenConversion.WriteDatAsync(Case("mini-adult"), WriterOptions.V2Compat);
        var tripleV2 = await GoldenConversion.WriteDatAsync(Case("mini-adult_triples"), WriterOptions.V2Compat);
        var tripleNative = await GoldenConversion.WriteDatAsync(Case("mini-adult_triples"), WriterOptions.Native);

        Assert.Equal([(byte)'\r', (byte)'\n'], wideV2[^2..]);
        Assert.Equal(wideV2[..^2], tripleV2); // triple = wide minus the final CRLF (same body)
        Assert.Equal((byte)'\n', tripleNative[^1]);
        Assert.NotEqual((byte)'\r', tripleNative[^2]); // LF only, not CRLF
    }
}
