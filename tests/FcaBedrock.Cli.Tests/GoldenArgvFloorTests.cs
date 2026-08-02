using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The M7 argv floor (D-122 part 14): every active v2 golden reproduced through the <b>real
/// CLI path</b> — <c>migrate</c> to a spec file, then <c>convert --v2-compat</c> to both
/// artifacts — and byte-compared against the checked-in expected outputs, then repeated.
/// <para>
/// Nothing of the library golden harness is reused: the CLI <em>is</em> the orchestrator here,
/// so this suite carries its own nine-row table and its own independent literal inventory of
/// the active variants. The active set is never derived from a directory listing, a filename
/// prefix or grammar, or parsed prose — there are <b>eleven</b> <c>.bed</c> files under the
/// copied fixture tree and only nine are active, so a listing would be wrong in a way that
/// silently widens the floor.
/// </para>
/// </summary>
public sealed class GoldenArgvFloorTests
{
    /// <summary>
    /// The independent oracle: exactly the nine active variants, in order. Authored here, not
    /// derived, so an inventory change in either direction fails one readable assertion.
    /// </summary>
    private static readonly string[] ActiveVariants =
    [
        "mini-mushroom/mini-mushroom",
        "mini-mushroom/mini-mushroom_tabbed_noheader",
        "mini-adult/mini-adult",
        "mini-adult/mini-adult_noheader",
        "mini-adult/mini-adult_employment_ordinal_discrete",
        "mini-adult/mini-adult_employment_ordinal_progressive",
        "mini-mushroom/mini-mushroom_triples",
        "mini-adult/mini-adult_triples",
        "mini-adult/mini-adult_triples_named",
    ];

    /// <summary>
    /// The two deliberately parked, non-active inputs. Named so their exclusion is explicit
    /// rather than accidental: they are copied into the output like every other fixture.
    /// </summary>
    private static readonly string[] ParkedVariants =
        ["mini-dates/mini-dates_triples", "mini-dates/mini-dates_triples_restricted"];

    // The argv facts of each active variant. Rows 5 and 6 deliberately reuse mini-adult.data,
    // and row 5 states --scaling discrete explicitly even though it is the default, so the
    // table states each fixture's fact rather than relying on one.
    private static readonly FloorCase[] Table =
    [
        new("mini-mushroom", "mini-mushroom", "wide", ",", "true"),
        new("mini-mushroom", "mini-mushroom_tabbed_noheader", "wide", "\t", "false"),
        new("mini-adult", "mini-adult", "wide", ",", "true"),
        new("mini-adult", "mini-adult_noheader", "wide", ",", "false"),
        new("mini-adult", "mini-adult_employment_ordinal_discrete", "wide", ",", "true")
        {
            Scaling = "discrete",
            DataVariant = "mini-adult",
        },
        new("mini-adult", "mini-adult_employment_ordinal_progressive", "wide", ",", "true")
        {
            Scaling = "progressive",
            DataVariant = "mini-adult",
        },
        new("mini-mushroom", "mini-mushroom_triples", "triple", ",", "false"),
        new("mini-adult", "mini-adult_triples", "triple", ",", "false"),
        new("mini-adult", "mini-adult_triples_named", "triple", ",", "false"),
    ];

    public static TheoryData<string> Rows() => [.. Table.Select(row => row.Name)];

    private static FloorCase Case(string name) => Table.Single(row => row.Name == name);

    // ---- the inventory oracle --------------------------------------------------------------

    [Fact]
    public void Floor_WhenTheTableIsInspected_ThenItCoversExactlyTheNineActiveVariants()
    {
        Assert.Equal(ActiveVariants, Table.Select(row => row.Name));

        foreach (var row in Table)
        {
            Assert.True(File.Exists(row.Bed), $"missing .bed: {row.Bed}");
            Assert.True(File.Exists(row.Data), $"missing .data: {row.Data}");
            Assert.True(File.Exists(row.ExpectedCxt), $"missing expected .cxt: {row.ExpectedCxt}");
            Assert.True(File.Exists(row.ExpectedDat), $"missing expected .dat: {row.ExpectedDat}");
        }
    }

    [Fact]
    public void Floor_WhenTheParkedDateInputsAreConsidered_ThenTheyAppearNowhereInTheFloor()
    {
        // They exist on disk and in the copied tree, and they are still not part of the M7
        // floor: `dates stay parked` is an inventory decision, not a filename property.
        foreach (var parked in ParkedVariants)
        {
            Assert.DoesNotContain(parked, ActiveVariants);
            Assert.DoesNotContain(Table, row => row.Name == parked);
        }

        Assert.DoesNotContain(Table, row => row.Family == "mini-dates");
    }

    // ---- the floor itself --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Floor_WhenAFixtureIsMigratedAndConvertedThroughArgv_ThenTheCxtIsByteIdenticalToTheV2Golden(
        string name)
    {
        var row = Case(name);
        using var temp = TempDirectory.Create();

        var produced = await RunAsync(row, temp);

        AssertGoldenBytes(row, "cxt", await File.ReadAllBytesAsync(row.ExpectedCxt), produced.Cxt);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Floor_WhenAFixtureIsMigratedAndConvertedThroughArgv_ThenTheDatIsByteIdenticalToTheV2Golden(
        string name)
    {
        var row = Case(name);
        using var temp = TempDirectory.Create();

        var produced = await RunAsync(row, temp);

        AssertGoldenBytes(row, "dat", await File.ReadAllBytesAsync(row.ExpectedDat), produced.Dat);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Floor_WhenTheWholeArgvRouteIsRepeated_ThenTheSpecAndBothArtifactsAreByteIdentical(string name)
    {
        // The spec §17 / P-7 repeatability leg, over the whole orchestration rather than one
        // library call: two independent temporary directories, same bytes.
        var row = Case(name);
        using var first = TempDirectory.Create();
        using var second = TempDirectory.Create();

        var one = await RunAsync(row, first);
        var two = await RunAsync(row, second);

        Assert.Equal(one.Spec, two.Spec);
        Assert.Equal(one.Cxt, two.Cxt);
        Assert.Equal(one.Dat, two.Dat);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Floor_WhenTheV2CompatDatIsWritten_ThenItsFinalNewlineFollowsTheSourceShape(string name)
    {
        // D-087's shape-derived split, surviving the argv route: the wide v2-compatible .dat
        // keeps its final CRLF and the triple one has no final newline at all.
        var row = Case(name);
        using var temp = TempDirectory.Create();

        var text = Encoding.UTF8.GetString((await RunAsync(row, temp)).Dat);

        if (row.Triple)
        {
            Assert.False(text.EndsWith('\n'), "a triple v2-compat .dat has no final newline");
        }
        else
        {
            Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Floor_WhenAFixtureConverts_ThenStdoutIsEmptyAndNoErrorIsReported(string name)
    {
        var row = Case(name);
        using var temp = TempDirectory.Create();
        var spec = temp.Resolve(row.Variant + ".toml");

        var migrate = new CliTestHarness();
        var migrated = await migrate.RunAsync(["migrate", row.Bed, "--out", spec, .. row.MigrateFlags]);

        var convert = new CliTestHarness();
        var converted = await convert.RunAsync(
            "convert", spec, row.Data, "--out", temp.Resolve(row.Variant),
            "--format", "both", "--v2-compat", "--no-manifest");

        Assert.Equal(0, migrated);
        Assert.Equal(string.Empty, migrate.StdOut);
        Assert.Equal(string.Empty, migrate.StdErr);
        Assert.Equal(0, converted);
        Assert.Equal(string.Empty, convert.StdOut);
        AssertNoFailureReported(convert.StdErr);
    }

    // A Warning is a legitimate property of a v2 fixture — a formal attribute that no object
    // crosses is one — and never moves the exit off 0 (D-122 part 2). An Error, a Fatal, or a
    // code-less host line is a different thing, and the floor admits none.
    private static void AssertNoFailureReported(string stderr)
    {
        foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.DoesNotContain(": error ", line, StringComparison.Ordinal);
            Assert.DoesNotContain(": fatal ", line, StringComparison.Ordinal);
            Assert.False(line.StartsWith("error", StringComparison.Ordinal), line);
            Assert.False(line.StartsWith("fatal", StringComparison.Ordinal), line);
        }
    }

    // ---- the one orchestration ----------------------------------------------------------------

    // migrate(argv) -> spec file, then convert(argv) -> both artifacts. Every step is a real
    // argv invocation through the host; nothing calls a library route directly.
    private static async Task<(byte[] Spec, byte[] Cxt, byte[] Dat)> RunAsync(FloorCase row, TempDirectory temp)
    {
        var spec = temp.Resolve(row.Variant + ".toml");
        var migrate = new CliTestHarness();

        Assert.Equal(0, await migrate.RunAsync(["migrate", row.Bed, "--out", spec, .. row.MigrateFlags]));
        Assert.Equal(string.Empty, migrate.StdErr);
        Assert.True(File.Exists(spec), "migrate wrote no spec file");

        var output = temp.Resolve(row.Variant);
        var convert = new CliTestHarness();

        Assert.Equal(
            0,
            await convert.RunAsync(
                "convert", spec, row.Data, "--out", output, "--format", "both", "--v2-compat", "--no-manifest"));
        Assert.Equal(string.Empty, convert.StdOut);

        return (
            await File.ReadAllBytesAsync(spec),
            await File.ReadAllBytesAsync(output + ".cxt"),
            await File.ReadAllBytesAsync(output + ".dat"));
    }

    // Divergence triage is part of the contract: a mismatch here on a variant whose LIBRARY
    // route is green in GoldenFixtureTests is an argv-orchestration divergence, and neither a
    // fixture nor an expected byte may be touched to resolve it (P-9).
    private static void AssertGoldenBytes(FloorCase row, string kind, byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        Assert.Fail(
            $"argv .{kind} bytes differ for {row.Name}: {expected.Length} expected, {actual.Length} produced. "
            + $"GoldenFixtureTests covers {row.Name}'s library route independently — if that suite is green "
            + "for this variant the divergence is in the argv orchestration, and no fixture or expected "
            + "output may be edited or regenerated to close it.");
    }

    /// <summary>One active variant, as the argv the floor drives it with.</summary>
    private sealed record FloorCase(string Family, string Variant, string Shape, string Delimiter, string Header)
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "fixtures", "v2");

        /// <summary>Explicit only where the fixture's own scaling is the point (rows 5 and 6).</summary>
        public string? Scaling { get; init; }

        /// <summary>A variant may reuse another variant's <c>.data</c>; default to its own.</summary>
        public string DataVariant { get; init; } = Variant;

        public string Name => $"{Family}/{Variant}";

        public bool Triple => Shape == "triple";

        public string Bed => Path.Combine(Root, Family, Variant + ".bed");

        public string Data => Path.Combine(Root, Family, DataVariant + ".data");

        public string ExpectedCxt => Path.Combine(Root, Family, "expected", Variant + ".cxt");

        public string ExpectedDat => Path.Combine(Root, Family, "expected", Variant + ".dat");

        public string[] MigrateFlags => Scaling is null
            ? ["--shape", Shape, "--delimiter", Delimiter, "--header", Header]
            : ["--shape", Shape, "--delimiter", Delimiter, "--header", Header, "--scaling", Scaling];

        public override string ToString() => Name;
    }
}
