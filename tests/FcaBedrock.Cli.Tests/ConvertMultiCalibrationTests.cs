using System.Security.Cryptography;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Auto-calibration with <b>several</b> count-sensitive attributes, through the real argv →
/// calibrate → plan → emit → stage → publish → manifest path.
/// <para>
/// One calibration owns one spool workspace and every count-sensitive attribute spills into it, so
/// the D-082 degraded-cleanup allowance has to be read at that workspace's scope. Read at one
/// attribute's scope instead it shrinks as attributes are added, and a perfectly ordinary spec is
/// refused with <c>GroupingStorageFailed</c> on healthy storage — a failure with no output, no
/// manifest, and a non-zero exit, which is why it belongs at this seam and not only in
/// <c>Conversion.Tests</c>.
/// </para>
/// <para>
/// The memory budget is internal and stays at its shipped default here, so these commands need not
/// spill at all: the arithmetic proof of the scope lives in
/// <c>Conversion.Tests/MultiAttributeCalibrationTests</c>, where a budget can be made small enough
/// to force one. What this suite pins is the <b>whole command</b> — several count-sensitive
/// attributes calibrate, publish, and record a manifest, with no storage diagnostic anywhere — and,
/// beside it, that a genuine calibration error still publishes nothing at all.
/// </para>
/// </summary>
public sealed class ConvertMultiCalibrationTests
{
    // Four equal_frequency attributes and one percentile range over the same five numeric columns:
    // five simultaneous count-sensitive accumulators, which is the shape a single-attribute spec
    // cannot exercise. Column 1 is deliberately low-cardinality, so the attributes' populations are
    // genuinely unequal.
    private const string ManyCalibratedSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "seq"
        source = { kind = "column", index = 0, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 4 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "small"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 2 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "rev"
        source = { kind = "column", index = 2, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 3 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "tied"
        source = { kind = "column", index = 3, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 2 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "skew"
        source = { kind = "column", index = 4, value_type = "number" }
        discretizer = { kind = "equal_width", bins = 2, range = "percentile_p1_p99" }
        scale = { kind = "nominal" }
        """;

    // Twelve rows: seq ascending, small over three values, rev descending, tied over four values,
    // skew ascending. Every population clears its distinct-count and spread guard.
    private static readonly string ManyCalibratedData =
        "seq,small,rev,tied,skew\n" +
        string.Join(
            '\n',
            Enumerable.Range(1, 12).Select(i =>
                $"{i},{100 + (i % 3)},{13 - i},{200 + (i % 4)},{i * 2}")) + "\n";

    // The same five predicates under ordering = "unordered", so the calibrator's grouped second
    // pass runs: a FirstAppearanceGrouping workspace and the count-calibration workspace are then
    // live in the same command, each with its own retained-byte accounting.
    private const string ManyCalibratedTripleSpec = """
        [spec]
        version = 1

        [binding]
        shape = "triple"
        ordering = "unordered"
        columns = { subject = 0, predicate = 1, value = 2 }

        [[attribute]]
        name = "seq"
        source = { kind = "predicate", name = "seq", value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 4 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "small"
        source = { kind = "predicate", name = "small", value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 2 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "rev"
        source = { kind = "predicate", name = "rev", value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 3 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "skew"
        source = { kind = "predicate", name = "skew", value_type = "number" }
        discretizer = { kind = "equal_width", bins = 2, range = "percentile_p1_p99" }
        scale = { kind = "nominal" }
        """;

    // Predicate-major, so no subject's rows are contiguous.
    private static readonly string ManyCalibratedTripleData = string.Join(
        '\n',
        new[] { "seq", "small", "rev", "skew" }.SelectMany(predicate =>
            Enumerable.Range(1, 12).Select(i => $"s{i:D2},{predicate},{Value(predicate, i)}"))) + "\n";

    private static int Value(string predicate, int i) => predicate switch
    {
        "seq" => i,
        "small" => 100 + (i % 3),
        "rev" => 13 - i,
        _ => i * 2,
    };

    // A population that genuinely cannot be calibrated: two distinct values against three bins is
    // the §11.5 distinct-count guard, which is an Error with no calibrated result.
    private const string ImpossibleSpec = """
        [spec]
        version = 1

        [binding]
        shape = "wide"
        has_header = true

        [[attribute]]
        name = "seq"
        source = { kind = "column", index = 0, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 4 }
        scale = { kind = "nominal" }

        [[attribute]]
        name = "flat"
        source = { kind = "column", index = 1, value_type = "number" }
        discretizer = { kind = "equal_frequency", bins = 3 }
        scale = { kind = "nominal" }
        """;

    private const string ImpossibleData = "seq,flat\n1,7\n2,7\n3,8\n4,8\n";

    [Fact]
    public async Task Convert_WhenSeveralAttributesAreCountSensitive_ThenBothFormatsPublishWithNoStorageDiagnostic()
    {
        using var run = ConvertRun.Wide(ManyCalibratedSpec, ManyCalibratedData);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.Equal(0, exit);

        // The spurious failure this fix removes is exactly a GroupingStorageFailed Error on healthy
        // storage, so its absence is asserted by name rather than by "stderr is empty" — an
        // unrelated warning must not be able to hide it, and it must not be able to hide behind one.
        Assert.DoesNotContain("GroupingStorageFailed", run.Harness.StdErr, StringComparison.Ordinal);

        // Published, complete, and provable: both artifacts exist, the manifest records both of
        // their hashes, and the transaction left nothing behind.
        Assert.True(File.Exists(run.Target(".cxt")));
        Assert.True(File.Exists(run.Target(".dat")));

        var manifest = run.Text(".manifest.toml");
        Assert.Contains($"hash = \"{Hash(run.Target(".cxt"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"hash = \"{Hash(run.Target(".dat"))}\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"input_hash = \"{Hash(run.Data)}\"", manifest, StringComparison.Ordinal);
        Assert.Empty(run.Residue());

        // And the calibration actually happened: five attributes' worth of scaled columns, so the
        // command is not passing by having skipped the count-sensitive work.
        Assert.Contains("seq", run.Text(".cxt"), StringComparison.Ordinal);
        Assert.Contains("skew", run.Text(".cxt"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WhenSeveralCountSensitivePredicatesAreUnordered_ThenItPublishesWithNoStorageDiagnostic()
    {
        // The two-workspace command: the grouping backend reconstructs the subjects and four
        // count-sensitive accumulators share the calibration workspace.
        using var run = ConvertRun.Wide(ManyCalibratedTripleSpec, ManyCalibratedTripleData);

        var exit = await run.ConvertAsync("--format", "dat");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("GroupingStorageFailed", run.Harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(run.Target(".dat")));
        Assert.Contains($"hash = \"{Hash(run.Target(".dat"))}\"", run.Text(".manifest.toml"), StringComparison.Ordinal);
        Assert.Empty(run.Residue());
    }

    [Fact]
    public async Task Convert_WhenTheSameSpecRunsTwice_ThenTheCommittedBytesAreIdentical()
    {
        // P-7 across the whole command, with several accumulators live: nothing about how the
        // populations were counted may reach the output.
        using var first = ConvertRun.Wide(ManyCalibratedSpec, ManyCalibratedData);
        using var second = ConvertRun.Wide(ManyCalibratedSpec, ManyCalibratedData);

        Assert.Equal(0, await first.ConvertAsync("--format", "both"));
        Assert.Equal(0, await second.ConvertAsync("--format", "both"));

        Assert.Equal(first.Bytes(".cxt"), second.Bytes(".cxt"));
        Assert.Equal(first.Bytes(".dat"), second.Bytes(".dat"));
    }

    [Fact]
    public async Task Convert_WhenOneOfSeveralPopulationsCannotBeCalibrated_ThenNothingIsPublished()
    {
        // The other side of the correction: removing a false failure must not weaken a real one.
        // `flat` has two distinct values against three bins, which §11.5 refuses outright.
        using var run = ConvertRun.Wide(ImpossibleSpec, ImpossibleData);

        var exit = await run.ConvertAsync("--format", "both");

        Assert.NotEqual(0, exit);
        Assert.Contains("CalibrationDataInsufficient", run.Harness.StdErr, StringComparison.Ordinal);

        // No artifact, no manifest, no residue: a failed calibration publishes nothing (D-122).
        Assert.False(File.Exists(run.Target(".cxt")));
        Assert.False(File.Exists(run.Target(".dat")));
        Assert.False(File.Exists(run.Target(".manifest.toml")));
        Assert.Empty(run.Residue());
    }

    private static string Hash(string path) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
