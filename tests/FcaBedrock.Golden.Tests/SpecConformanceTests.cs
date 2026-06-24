using System.Text;
using FcaBedrock.Export;

namespace FcaBedrock.Golden.Tests;

// Spec-conformance, distinct from the golden harness: the goldens prove v2-compat
// *bytes*, which the spec deliberately does NOT make the native default. These
// tests run the *native* (non-v2-compat) path and assert it matches
// bedrock-spec-v1.md, with section citations. Agreement with the spec, not merely
// with v2 (principle P-8). Driven off mini-mushroom (comma + header).
public sealed class SpecConformanceTests
{
    private static readonly FixtureCase Mushroom = FixtureCase.Active[0];

    private static async Task<string> NativeCxtAsync() =>
        Encoding.UTF8.GetString(await GoldenConversion.WriteCxtAsync(Mushroom, WriterOptions.Native));

    private static async Task<string> NativeDatAsync() =>
        Encoding.UTF8.GetString(await GoldenConversion.WriteDatAsync(Mushroom, WriterOptions.Native));

    [Fact]
    public async Task NativeDat_WhenWritten_ThenLfAndNoTrailingSpace()
    {
        // §18.2 / §21.4: native .dat has no trailing space after the last id; LF endings.
        var dat = await NativeDatAsync();

        Assert.Equal("1 2 4 6\n1 3 4 8\n3 4 6\n1 2 4 7\n3 4 6\n", dat);
    }

    [Fact]
    public async Task NativeDat_WhenComparedToV2Compat_ThenDiffersOnlyByTrailingSpaceAndEndings()
    {
        // The two paths share one schema (same ids); they differ only in the v2-isms.
        var native = await NativeDatAsync();
        var v2 = Encoding.UTF8.GetString(await GoldenConversion.WriteDatAsync(Mushroom, WriterOptions.V2Compat));

        Assert.Equal(v2.Replace(" \r\n", "\n", StringComparison.Ordinal), native);
    }

    [Fact]
    public async Task NativeCxt_WhenWritten_ThenBurmeisterLayoutWithLfAndTrailingNewline()
    {
        // §18.1: B, blank, counts, blank, object names, attribute names, incidence rows; LF; trailing newline.
        var expected =
            "B\n\n5\n8\n\n" +
            "0\n1\n2\n3\n4\n" +
            "bruises?\ngill-size-broad\ngill-size-narrow\nveil-type-partial\nveil-type-universal\n" +
            "ring-number-none\nring-number-one\nring-number-two\n" +
            "XX.X.X..\nX.XX...X\n..XX.X..\nXX.X..X.\n..XX.X..\n";

        Assert.Equal(expected, await NativeCxtAsync());
    }

    [Fact]
    public async Task NativeCxt_WhenDichotomic_ThenFormalAttributeIsColumnNameAlone()
    {
        // §10.7 / §12.2: dichotomic default name is {column} alone, no value suffix.
        var lines = (await NativeCxtAsync()).Split('\n');

        Assert.Contains("bruises?", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("bruises?-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NativeCxt_WhenNominalWithValueLabels_ThenNamesUseDisplayLabels()
    {
        // §10.8: value_labels render display names (broad/narrow), not raw values (b/n),
        // without changing order or count.
        var lines = (await NativeCxtAsync()).Split('\n');

        Assert.Contains("gill-size-broad", lines);
        Assert.Contains("gill-size-narrow", lines);
        Assert.DoesNotContain("gill-size-b", lines);
    }
}
