using System.Globalization;
using FcaBedrock.Diagnostics;
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
    public async Task MiniMushroom_WhenConverted_ThenReportsItsEmptyColumnAgainstV2sOwnBytes()
    {
        // §7/§16.4/D-105: AttributeHasNoCrosses on a golden is EVIDENCE, not tolerance. v2's own
        // mini-mushroom.cxt carries eight columns and five incidence rows, and the
        // veil-type-universal column (id 4) is '.' in every one of them — the fixture's mushrooms
        // all have a partial veil. So the warning states a fact about the compatibility target,
        // and this test derives that fact INDEPENDENTLY from the golden bytes rather than from
        // the emitter, then requires the emitter to agree.
        var fixture = FixtureCase.Active.Single(f => f.Variant == "mini-mushroom");
        var text = await File.ReadAllTextAsync(fixture.ExpectedCxtPath);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        // Burmeister layout (§18.1): "B", blank, n_objects, n_attributes, blank, names…, rows.
        var objectCount = int.Parse(lines[2], CultureInfo.InvariantCulture);
        var attributeCount = int.Parse(lines[3], CultureInfo.InvariantCulture);
        var names = lines.Skip(5).Take(objectCount + attributeCount).Skip(objectCount).ToArray();
        var rows = lines.Skip(5 + objectCount + attributeCount).Take(objectCount).ToArray();

        var emptyInGolden = Enumerable.Range(0, attributeCount)
            .Where(id => rows.All(row => row[id] == '.'))
            .Select(id => names[id])
            .ToArray();
        Assert.Equal(["veil-type-universal"], emptyInGolden);

        // The emitter must report exactly that column, once, as one aggregate.
        var diagnostics = await GoldenConversion.CollectCxtDiagnosticsAsync(fixture, WriterOptions.V2Compat);
        var warning = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("1 formal attribute(s)", warning.Message, StringComparison.Ordinal);
        Assert.Contains("veil-type-universal", warning.Message, StringComparison.Ordinal);

        // Every object crosses something and objects were emitted, so the other two stay silent.
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ObjectHasNoCrosses);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoObjectsEmitted);
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
