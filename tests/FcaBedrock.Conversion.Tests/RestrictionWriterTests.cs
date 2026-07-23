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
/// Restriction execution proven at the <b>serialized bytes</b>, not merely at the plan or the
/// emitted-object list (§10.4/§18/D-105) — object names and row counts are only observable for
/// real in a written artifact.
/// <para>
/// Also the §7 calibration-before-filtering proof and the §19.4 worked example end to end.
/// </para>
/// </summary>
public sealed class RestrictionWriterTests
{
    [Fact]
    public async Task Cxt_WhenRowIndexObjectsAreFiltered_ThenSurvivorNamesAreInputPositionsInTheBytes()
    {
        // §5.4/G-2, the round-5 Medium-4 vector, asserted in the ACTUAL .cxt bytes: row_index
        // names are source positions and filtering never renumbers them.
        //
        // The fixture is chosen so survivor rank and input position DISAGREE for every survivor:
        // rows 0 and 2 are filtered, so the survivors are input positions 1 and 3 while their
        // survivor ranks would be 0 and 1. An implementation that renumbered would write "0\n1".
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
        [
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("tissue", 1, "endoderm"),
        ]);

        var cxt = await WriteCxtAsync(spec, "Wnt1,endoderm\nBmp5,endoderm\nShh,endoderm\nBmp5,endoderm");

        Assert.Equal(
            "B\n\n2\n1\n\n" +          // 2 objects, 1 formal attribute
            "1\n3\n" +                  // ← the object names: input positions, NOT 0 and 1
            "tissue-endoderm\n" +
            "X\nX\n",
            cxt);
    }

    [Fact]
    public async Task Dat_WhenObjectsAreFiltered_ThenOnlySurvivingRowsAreWritten()
    {
        // .dat carries no names, so the proof there is the row COUNT and content.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
        [
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("tissue", 1, "endoderm", "mesoderm"),
        ]);

        var dat = await WriteDatAsync(spec, "Wnt1,endoderm\nBmp5,mesoderm\nShh,endoderm");

        Assert.Equal("2\n", dat); // one row: the surviving Bmp5 object, crossing tissue-mesoderm (id 2, 1-based)
    }

    [Fact]
    public async Task Cxt_WhenTheSameSpecIsConvertedTwice_ThenTheBytesAreIdentical()
    {
        // P-7: restriction execution is on an output path, so it carries a repeatability test.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
        [
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("tissue", 1, "endoderm", "mesoderm"),
        ]);
        const string data = "Wnt1,endoderm\nBmp5,mesoderm\nBmp5,endoderm";

        Assert.Equal(await WriteCxtAsync(spec, data), await WriteCxtAsync(spec, data));
        Assert.Equal(await WriteDatAsync(spec, data), await WriteDatAsync(spec, data));
    }

    [Fact]
    public async Task Cxt_WhenNoAttributeRestricts_ThenTheBytesAreIdenticalToAnUnrestrictedRun()
    {
        // The restriction-free path must be untouched: a spec with no restrict_to emits exactly
        // what it did before restrictions could execute.
        var plain = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [ConversionFixtures.Nominal("tissue", 0, "endoderm", "mesoderm")]);

        // …and a restriction that matches everything must not change the bytes either.
        var restrictedButTotal = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
        [
            ConversionFixtures.Nominal("tissue", 0, "endoderm", "mesoderm") with
            {
                RestrictTo = [new RestrictToValue("endoderm"), new RestrictToValue("mesoderm")],
            },
        ]);

        const string data = "endoderm\nmesoderm";
        Assert.Equal(await WriteCxtAsync(plain, data), await WriteCxtAsync(restrictedButTotal, data));
    }

    // --- §7: calibration and vocabulary precede filtering --------------------

    [Fact]
    public async Task Calibrate_WhenRestrictionsWouldExcludeRows_ThenTheObservedDomainStillSpansTheInputUniverse()
    {
        // §7/D-065, proven at the CALIBRATION OUTCOME rather than only at output equality: the
        // observed domain is computed over the input universe, BEFORE restrict_to selects objects.
        //
        // The discriminator: "mesoderm" appears only on rows the restriction excludes. A
        // filter-then-calibrate implementation would observe only {endoderm} and plan one column;
        // the correct order observes {endoderm, mesoderm} and plans two — the second of which
        // then legitimately carries no crosses.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
        [
            Filter("Gene", 0, new RestrictToValue("Bmp5")),
            ConversionFixtures.Nominal("tissue", 1) with { DeclaredDomain = null }, // omitted → calibrated
        ]);
        var source = ConversionFixtures.SourceOver("Bmp5,endoderm\nWnt1,mesoderm", spec.Binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());

        var calibrated = await Calibrator.CalibrateAsync(resolved, source);
        Assert.True(calibrated.TryGetValue(out var state));

        // The calibration outcome itself saw the excluded row.
        var observed = Assert.IsType<ObservedDomain>(Assert.Single(state.Calibrations));
        Assert.Equal(["endoderm", "mesoderm"], observed.Values);

        // …so the vocabulary has both columns, and the surviving object crosses only one.
        Assert.True(ConversionPlanner.Plan(state).TryGetValue(out var plan));
        Assert.Equal(["tissue-endoderm", "tissue-mesoderm"], plan!.FormalAttributes.Select(f => f.RenderedName));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        Assert.Equal([0], Assert.Single(objects).CrossedFormalAttributeIds);
        Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses); // tissue-mesoderm
    }

    // --- no extra pass -------------------------------------------------------

    [Theory]
    [InlineData(DuplicateObjectPolicy.Fail)]
    [InlineData(DuplicateObjectPolicy.Dedupe)]
    public async Task Emit_WhenRestrictionsExecute_ThenTheSourceIsStillEnumeratedExactlyOnce(DuplicateObjectPolicy policy)
    {
        // P-16/D-105: restriction evaluation rides the EXISTING pass — it reads the same rows the
        // classification already reads, and the dedupe path reuses the existing grouping/spool
        // backend. Counted directly rather than inferred from equal output, because equal output
        // is exactly what a wasteful second pass would also produce.
        var spec = new BedrockSpec(
            ConversionFixtures.WideWithKey(0, policy),
            [
                Filter("Gene", 1, new RestrictToValue("Bmp5")),
                ConversionFixtures.Nominal("tissue", 2, "endoderm"),
            ]);

        var inner = ConversionFixtures.SourceOver("p1,Bmp5,endoderm\np2,Wnt1,endoderm", spec.Binding);
        var counting = new CountingRecordSource(inner);
        Assert.True(ConversionFixtures.PlanFor(spec, await inner.GetSchemaAsync()).TryGetValue(out var plan));

        await foreach (var _ in Emitter.EmitAsync(plan!, counting, new List<BedrockDiagnostic>()))
        {
        }

        Assert.Equal(1, counting.Enumerations);
    }

    private sealed class CountingRecordSource(Sources.IRecordSource inner) : Sources.IRecordSource
    {
        public int Enumerations { get; private set; }

        public SourceProvenance Provenance => inner.Provenance;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchemaAsync(cancellationToken);

        public async IAsyncEnumerable<Sources.ObjectRecord> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Enumerations++;
            await foreach (var record in inner.ReadAsync(cancellationToken))
            {
                yield return record;
            }
        }
    }

    // --- §19.4: the worked example, end to end -------------------------------

    [Fact]
    public async Task Emit_WhenTheSpec194EmageExample_ThenItExecutesEndToEnd()
    {
        // §19.4 made executable (D-105): two FILTER-ONLY attributes (Gene, Strength) shaping which
        // objects enter the context without becoming columns, one grouped emitted dimension
        // (Tissue), and one EMITTED-AND-RESTRICTED numeric dimension (TheilerStage). Each
        // restriction is existential over the subject's complete triple group, and each filters
        // whole objects, not observations.
        //
        // s1: Bmp5 + strongly detected + TS 5 + endoderm     → survives
        // s2: Bmp5 + strongly detected + TS 12 + endoderm    → TS outside [3, 9)
        // s3: Bmp5 + weakly detected  + TS 5 + mesoderm      → no strong observation
        // s4: Wnt1 + strongly detected + TS 5 + mesoderm     → wrong gene
        // s5: Bmp5 + strongly detected + TS 5 + mesoderm     → survives (multi-valued Strength)
        var spec = new BedrockSpec(ConversionFixtures.Triple(TripleOrdering.Unordered),
        [
            TripleFilter("Gene", "Gene", new RestrictToValue("Bmp5")),
            ConversionFixtures.PredicateNominal("Tissue", "Tissue", ["endoderm", "mesoderm"]),
            TripleFilter("Strength", "Strength", new RestrictToValue("strongly detected")),
            new AttributeSpec("TheilerStage", new PredicateSource("TheilerStage", SourceValueType.Number),
                Include: true,
                ManualCutsDiscretizer.Create([6.0], BinEnds.Open, System.Globalization.CultureInfo.InvariantCulture).Value!,
                new NominalScale(), DeclaredDomain: [],
                RestrictTo: [new RestrictToRange(3, 9)],
                ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);

        const string data =
            "s1,Gene,Bmp5\ns1,Strength,strongly detected\ns1,TheilerStage,5\ns1,Tissue,endoderm\n" +
            "s2,Gene,Bmp5\ns2,Strength,strongly detected\ns2,TheilerStage,12\ns2,Tissue,endoderm\n" +
            "s3,Gene,Bmp5\ns3,Strength,weakly detected\ns3,TheilerStage,5\ns3,Tissue,mesoderm\n" +
            "s4,Gene,Wnt1\ns4,Strength,strongly detected\ns4,TheilerStage,5\ns4,Tissue,mesoderm\n" +
            "s5,Gene,Bmp5\ns5,Strength,weakly detected\ns5,Strength,strongly detected\ns5,TheilerStage,5\ns5,Tissue,mesoderm";

        var source = ConversionFixtures.TripleSourceOver(data, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        // Gene and Strength are filter-only: no columns, but two of the four restrictions.
        Assert.Equal(["Tissue-endoderm", "Tissue-mesoderm", "TheilerStage-<6", "TheilerStage->=6"],
            plan!.FormalAttributes.Select(f => f.RenderedName));
        Assert.Equal(["Gene", "Strength", "TheilerStage"], plan.Restrictions.Select(r => r.AttributeName));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitTripleAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        // s5 survives on its SECOND Strength observation — existential matching over a
        // multi-valued predicate, which a single-valued fixture could not distinguish.
        Assert.Equal(["s1", "s5"], objects.Select(o => o.Name));
        Assert.Equal([0, 2], objects[0].CrossedFormalAttributeIds); // endoderm + TS<6
        Assert.Equal([1, 2], objects[1].CrossedFormalAttributeIds); // mesoderm + TS<6

        // §19.4's own note: the TS vocabulary was fixed before filtering, so the surviving
        // TS 3-8 objects need not span every bucket — TheilerStage->=6 is legitimately empty.
        var warning = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.AttributeHasNoCrosses);
        Assert.Contains("TheilerStage->=6", warning.Message, StringComparison.Ordinal);
    }

    // --- helpers ------------------------------------------------------------

    private static AttributeSpec Filter(string name, int index, params RestrictToEntry[] entries) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static AttributeSpec TripleFilter(string name, string predicate, params RestrictToEntry[] entries) =>
        new(name, new PredicateSource(predicate, SourceValueType.String), Include: false, null, null, [],
            entries, ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static async Task<string> WriteCxtAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        using var stream = new MemoryStream();
        var diagnostics = new List<BedrockDiagnostic>();
        using (var session = EmitReplay.Begin(sink => Emitter.EmitAsync(plan!, source, sink), diagnostics))
        {
            await CxtWriter.WriteAsync(plan!, session.Open, WriterOptions.Native, stream);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<string> WriteDatAsync(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        Assert.True(ConversionFixtures.PlanFor(spec, await source.GetSchemaAsync()).TryGetValue(out var plan));

        using var stream = new MemoryStream();
        await DatWriter.WriteAsync(
            Emitter.EmitAsync(plan!, source, new List<BedrockDiagnostic>()), WriterOptions.Native, stream);

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
