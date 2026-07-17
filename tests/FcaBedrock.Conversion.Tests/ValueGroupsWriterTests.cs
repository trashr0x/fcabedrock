using System.Globalization;
using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;

namespace FcaBedrock.Conversion.Tests;

/// <summary>
/// <c>value_groups</c> through the writers (§11.6, M4 Slice E / D-104): the <b>actual output
/// bytes</b> for each unmatched policy, on the wide and both triple paths, plus repeat
/// determinism (P-7). Byte-level rather than plan-level, because that is where a claim about
/// output behaviour is genuinely settled — the exporters themselves are unchanged and dumb (P-15).
/// </summary>
public sealed class ValueGroupsWriterTests
{
    private static ValueGroup Group(string label, params string[] values) => ValueGroup.Create(label, values, null);

    private static BedrockSpec Spec(ValueGroupsUnmatched unmatched) =>
        new(ConversionFixtures.Wide(hasHeader: false), [
            new AttributeSpec("edu", new ColumnSource(0, SourceValueType.String), Include: true,
                ValueGroupsDiscretizer.Create([Group("School", "11th", "HS-grad"), Group("Uni", "Bachelors")], unmatched),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Skip),
        ]);

    private static BedrockSpec PassthroughSpec() =>
        new(ConversionFixtures.Wide(hasHeader: false), [
            new AttributeSpec("edu", new ColumnSource(0, SourceValueType.String), Include: true,
                new CalibrationPending(new PendingValueGroupsPassthrough([Group("School", "11th", "HS-grad")]), CultureInfo.InvariantCulture),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Skip),
        ]);

    private const string Data = "11th\nBachelors\nPhD";

    private static async Task<ConversionPlan> PlanAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return plan;
    }

    // Calibrates then plans — the only route a passthrough spec can reach the writers by (D-093).
    private static async Task<ConversionPlan> CalibrateAndPlanAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        var calibration = await Calibrator.CalibrateAsync(ConversionFixtures.ResolveFor(spec, schema), source);
        Assert.True(calibration.TryGetValue(out var calibrated),
            string.Join("; ", calibration.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.True(ConversionPlanner.Plan(calibrated!).TryGetValue(out var plan));
        return plan;
    }

    private static async Task<string> WriteCxtAsync(ConversionPlan plan, BedrockSpec spec, string csv)
    {
        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(
            plan,
            () => Emitter.EmitAsync(plan, ConversionFixtures.SourceOver(csv, spec.Binding), new List<BedrockDiagnostic>()),
            WriterOptions.Native,
            stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<string> WriteDatAsync(ConversionPlan plan, BedrockSpec spec, string csv)
    {
        using var stream = new MemoryStream();
        await DatWriter.WriteAsync(
            Emitter.EmitAsync(plan, ConversionFixtures.SourceOver(csv, spec.Binding), new List<BedrockDiagnostic>()),
            WriterOptions.Native,
            stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // --- skip -----------------------------------------------------------------

    [Fact]
    public async Task Cxt_WhenSkip_ThenOnlyGroupColumnsAndTheUngroupedRowIsAllDots()
    {
        var plan = await PlanAsync(Spec(ValueGroupsUnmatched.Skip), Data);

        var cxt = await WriteCxtAsync(plan, Spec(ValueGroupsUnmatched.Skip), Data);

        // Burmeister: B, blank, rows, cols, blank, object names, attribute names, incidence.
        // Three objects × two group columns; "PhD" matched nothing, so its row is empty.
        Assert.Equal(
            "B\n\n3\n2\n\n0\n1\n2\nedu-School\nedu-Uni\nX.\n.X\n..\n",
            cxt);
    }

    [Fact]
    public async Task Dat_WhenSkip_ThenTheUngroupedRowIsEmpty()
    {
        var plan = await PlanAsync(Spec(ValueGroupsUnmatched.Skip), Data);

        // base_index 1: edu-School → 1, edu-Uni → 2. The "PhD" row carries no ids at all.
        Assert.Equal("1\n2\n\n", await WriteDatAsync(plan, Spec(ValueGroupsUnmatched.Skip), Data));
    }

    // --- other ----------------------------------------------------------------

    [Fact]
    public async Task Cxt_WhenOther_ThenTheSyntheticColumnIsLastAndCatchesTheUngroupedValue()
    {
        var plan = await PlanAsync(Spec(ValueGroupsUnmatched.Other), Data);

        var cxt = await WriteCxtAsync(plan, Spec(ValueGroupsUnmatched.Other), Data);

        // §17 rule 3: edu-Other is ordered after every declared group.
        Assert.Equal(
            "B\n\n3\n3\n\n0\n1\n2\nedu-School\nedu-Uni\nedu-Other\nX..\n.X.\n..X\n",
            cxt);
    }

    [Fact]
    public async Task Dat_WhenOther_ThenTheUngroupedRowCarriesTheOtherId() =>
        Assert.Equal(
            "1\n2\n3\n",
            await WriteDatAsync(await PlanAsync(Spec(ValueGroupsUnmatched.Other), Data), Spec(ValueGroupsUnmatched.Other), Data));

    // --- passthrough ----------------------------------------------------------

    [Fact]
    public async Task Cxt_WhenCalibratedPassthrough_ThenDiscoveredBinsAreColumnsAfterTheGroups()
    {
        var plan = await CalibrateAndPlanAsync(PassthroughSpec(), Data);

        var cxt = await WriteCxtAsync(plan, PassthroughSpec(), Data);

        // "Bachelors" and "PhD" both matched no group, so both became bins — in first-observation
        // order, after the declared group (§17 rule 3).
        Assert.Equal(
            "B\n\n3\n3\n\n0\n1\n2\nedu-School\nedu-Bachelors\nedu-PhD\nX..\n.X.\n..X\n",
            cxt);
    }

    [Fact]
    public async Task Dat_WhenCalibratedPassthrough_ThenEachObjectCarriesItsDiscoveredBinId() =>
        Assert.Equal(
            "1\n2\n3\n",
            await WriteDatAsync(await CalibrateAndPlanAsync(PassthroughSpec(), Data), PassthroughSpec(), Data));

    // --- ordinal over groups --------------------------------------------------

    [Fact]
    public async Task Cxt_WhenOrdinalOverGroups_ThenCumulativeThresholdsInTheAuthoredOrder()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [
            new AttributeSpec("edu", new ColumnSource(0, SourceValueType.String), Include: true,
                ValueGroupsDiscretizer.Create(
                    [Group("School", "11th"), Group("Uni", "Bachelors"), Group("Post", "PhD")], ValueGroupsUnmatched.Skip),
                new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, ["School", "Uni", "Post"]),
                DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Skip),
        ]);
        var plan = await PlanAsync(spec, Data);

        var cxt = await WriteCxtAsync(plan, spec, Data);

        // ge+inclusive over the authored order: 11th → ≥School only; Bachelors → ≥School, ≥Uni;
        // PhD → all three. Cumulative, exactly as §12.3 specifies.
        Assert.Equal(
            "B\n\n3\n3\n\n0\n1\n2\nedu->=School\nedu->=Uni\nedu->=Post\nX..\nXX.\nXXX\n",
            cxt);
    }

    // --- Triple paths ---------------------------------------------------------

    private static BedrockSpec TripleSpec(TripleOrdering ordering) =>
        new(ConversionFixtures.Triple(ordering), [
            new AttributeSpec("edu", new PredicateSource("edu", SourceValueType.String), Include: true,
                ValueGroupsDiscretizer.Create([Group("School", "11th"), Group("Uni", "Bachelors")], ValueGroupsUnmatched.Other),
                new NominalScale(), DeclaredDomain: [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Skip),
        ]);

    private const string TripleGrouped = "s0,edu,11th\ns1,edu,Bachelors\ns2,edu,PhD";
    private const string TripleInterleaved = "s0,edu,11th\ns2,edu,PhD\ns1,edu,Bachelors";

    [Theory]
    [InlineData(TripleOrdering.SubjectGrouped, TripleGrouped)]
    [InlineData(TripleOrdering.Unordered, TripleInterleaved)]
    public async Task Cxt_WhenTriple_ThenObjectsFollowFirstAppearanceAndGroupsClassifyIdentically(
        TripleOrdering ordering, string data)
    {
        var spec = TripleSpec(ordering);
        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        using var stream = new MemoryStream();
        await CxtWriter.WriteAsync(
            plan,
            () => Emitter.EmitTripleAsync(plan, ConversionFixtures.TripleSourceOver(data, spec.Binding), new List<BedrockDiagnostic>()),
            WriterOptions.Native,
            stream);

        // §17 rule 4: object order is first appearance of each subject — s0, s2, s1 under the
        // interleaved input. Classification is identical either way; only the object order differs.
        var expectedNames = ordering == TripleOrdering.SubjectGrouped ? "s0\ns1\ns2" : "s0\ns2\ns1";
        var expectedRows = ordering == TripleOrdering.SubjectGrouped ? "X..\n.X.\n..X" : "X..\n..X\n.X.";
        Assert.Equal(
            $"B\n\n3\n3\n\n{expectedNames}\nedu-School\nedu-Uni\nedu-Other\n{expectedRows}\n",
            Encoding.UTF8.GetString(stream.ToArray()));
    }

    // --- Determinism (P-7) ----------------------------------------------------

    [Fact]
    public async Task Cxt_WhenWrittenTwice_ThenIdenticalBytes()
    {
        // Both .cxt passes and both runs: value_groups adds no order-dependent state.
        var plan = await PlanAsync(Spec(ValueGroupsUnmatched.Other), Data);

        Assert.Equal(
            await WriteCxtAsync(plan, Spec(ValueGroupsUnmatched.Other), Data),
            await WriteCxtAsync(plan, Spec(ValueGroupsUnmatched.Other), Data));
    }

    [Fact]
    public async Task CalibrateAndWrite_WhenRepeated_ThenIdenticalBytesEndToEnd()
    {
        // The whole calibrate → plan → emit → write chain repeated: the discovered bin set, its
        // order, and the bytes must all be reproducible from the same input (P-7).
        var first = await WriteCxtAsync(await CalibrateAndPlanAsync(PassthroughSpec(), Data), PassthroughSpec(), Data);
        var second = await WriteCxtAsync(await CalibrateAndPlanAsync(PassthroughSpec(), Data), PassthroughSpec(), Data);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Cxt_WhenPlannedUnderEitherLabelStyle_ThenValueGroupsNamesAreStyleIndependent()
    {
        // LabelStyle is the bin-label render hook (D-044): a CUT discretizer re-renders its
        // interior labels for v2-compat, but a group label is not a cut label — value_groups keeps
        // the base, style-independent rendering — so the two styles must produce identical names
        // and therefore identical bytes.
        //
        // (This is about LabelStyle, not WriterOptions.V2Compat, which changes line endings and
        // trailing spaces for every discretizer alike — writer formatting, not scaling semantics.)
        var spec = Spec(ValueGroupsUnmatched.Other);
        var source = ConversionFixtures.SourceOver(Data, spec.Binding);
        var schema = await source.GetSchemaAsync();

        Assert.True(ConversionFixtures.PlanFor(spec, schema, LabelStyle.Native).TryGetValue(out var native));
        Assert.True(ConversionFixtures.PlanFor(spec, schema, LabelStyle.V2Compat).TryGetValue(out var v2Compat));

        Assert.Equal(
            native!.FormalAttributes.Select(a => a.RenderedName),
            v2Compat!.FormalAttributes.Select(a => a.RenderedName));
        Assert.Equal(
            await WriteCxtAsync(native, spec, Data),
            await WriteCxtAsync(v2Compat, spec, Data));
    }
}
