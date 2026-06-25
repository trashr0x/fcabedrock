using FcaBedrock.Export;

namespace FcaBedrock.Golden.Tests;

// The golden harness over the real v2 fixtures: it runs the full conversion
// pipeline (.bed -> source -> plan -> emit -> write) under WriterOptions.V2Compat
// and asserts byte-identical output against the v2 goldens. This is v2-compat
// *compatibility evidence*; native-path *spec conformance* is asserted separately
// (SpecConformanceTests). The active fixture set is FixtureCase.Active.
public sealed class GoldenFixtureTests
{
    public static IEnumerable<object[]> Cases =>
        FixtureCase.Active.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Cxt_WhenConvertedV2Compat_ThenByteIdenticalToGolden(FixtureCase fixture)
    {
        var actual = await GoldenConversion.WriteCxtAsync(fixture, WriterOptions.V2Compat);

        AssertBytesEqual(fixture.ExpectedCxtPath, actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Dat_WhenConvertedV2Compat_ThenByteIdenticalToGolden(FixtureCase fixture)
    {
        var actual = await GoldenConversion.WriteDatAsync(fixture, WriterOptions.V2Compat);

        AssertBytesEqual(fixture.ExpectedDatPath, actual);
    }

    [Fact]
    public void MiniMushroom_WhenPresent_ThenHasGoldenOutputs()
    {
        var exampleDir = Path.Combine(FixturePaths.V2Root, "mini-mushroom");
        if (!Directory.Exists(exampleDir))
        {
            return; // headline fixture not added yet
        }

        var marker = Path.Combine("mini-mushroom", "expected");
        var outputs = FixturePaths.EnumerateExpectedOutputs()
            .Where(p => p.Contains(marker, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(outputs);
    }

    private static void AssertBytesEqual(string expectedPath, byte[] actual)
    {
        var expected = File.ReadAllBytes(expectedPath);
        var result = ByteComparer.Compare(expected, actual);

        Assert.True(result.AreEqual, $"{expectedPath}{Environment.NewLine}{result.Message}");
    }
}
