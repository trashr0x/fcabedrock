using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The D-107 four-part draft guarantee for the <b>triple</b> shape: a successful probe produces a
/// draft that (1) has at least one attribute, (2) rereads under the strict TOML reader,
/// (3) resolves against the probed schema with no Error/Fatal, and (4) converts the same source
/// under the same effective settings with no Error/Fatal.
/// <para>
/// Probe runs no conversion itself — D-109 forbids Discovery from referencing it — so leg 4 is
/// proven here through a <b>test-only</b> Conversion reference. That is also what makes the
/// structural subject checks worth having: without them a probe could hand back a draft whose own
/// convert leg halts.
/// </para>
/// </summary>
public sealed class ProbeTripleGuaranteeTests
{
    private static string FixturePath(string example, string file) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", example, file);

    private static bool IsErrorOrWorse(BedrockDiagnostic diagnostic) =>
        diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;

    /// <summary>Runs all four legs over the probed source, returning the conversion diagnostics.</summary>
    private static async Task<IReadOnlyList<BedrockDiagnostic>> AssertGuaranteeAsync(
        SpecDocument draft, Func<Stream> openSource)
    {
        // (1) at least one attribute.
        Assert.NotEmpty(draft.Attributes);

        // (2) strict reread — the reader rejects unknown keys, so a draft carrying a field the
        // format does not define would fail right here.
        var toml = SpecWriter.Write(draft);
        var reread = SpecReader.Read(toml);
        Assert.True(reread.TryGetValue(out var rereadDocument), ProbeFixtures.Describe(reread.Diagnostics));
        Assert.DoesNotContain(reread.Diagnostics, IsErrorOrWorse);

        // …and to the SAME document: canonical text that round-trips to something else would
        // satisfy the remaining legs while quietly meaning something different.
        Assert.Equal(toml, SpecWriter.Write(rereadDocument));

        // (3) resolve against the probed schema, through the ordinary two-stage bootstrap.
        var settingsResult = SpecResolver.ResolveReadSettings(rereadDocument);
        Assert.True(settingsResult.TryGetValue(out var settings), ProbeFixtures.Describe(settingsResult.Diagnostics));

        var session = new TripleCsvSession(openSource, settings);
        var schema = await session.GetSchemaAsync();
        var resolved = SpecResolver.Resolve(rereadDocument, schema);
        Assert.DoesNotContain(resolved.Diagnostics, IsErrorOrWorse);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), ProbeFixtures.Describe(resolved.Diagnostics));

        // (4) convert the same source under the same effective settings.
        var source = session.Bind(resolvedDocument.Resolved);
        var calibrated = await Calibrator.CalibrateTripleAsync(resolvedDocument.Resolved, source);
        Assert.DoesNotContain(calibrated.Diagnostics, IsErrorOrWorse);
        Assert.True(calibrated.TryGetValue(out var calibratedSpec), ProbeFixtures.Describe(calibrated.Diagnostics));

        var planned = ConversionPlanner.Plan(calibratedSpec, LabelStyle.Native);
        Assert.DoesNotContain(planned.Diagnostics, IsErrorOrWorse);
        Assert.True(planned.TryGetValue(out var plan), ProbeFixtures.Describe(planned.Diagnostics));

        var emitDiagnostics = new List<BedrockDiagnostic>();
        var objects = 0;
        await foreach (var _ in Emitter.EmitTripleAsync(plan, source, emitDiagnostics))
        {
            objects++;
        }

        // Warnings and structurally-valid degenerate contexts are allowed (D-107); Errors are not.
        Assert.DoesNotContain(emitDiagnostics, IsErrorOrWorse);
        Assert.True(objects >= 0);

        var all = new List<BedrockDiagnostic>(calibrated.Diagnostics);
        all.AddRange(planned.Diagnostics);
        all.AddRange(emitDiagnostics);
        return all;
    }

    [Fact]
    public async Task Draft_WhenProbedFromASimpleTripleSource_ThenSatisfiesTheFourPartGuarantee()
    {
        const string csv = "s1,species,cat\ns1,colour,black\ns2,species,dog\ns2,colour,?\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));

        await AssertGuaranteeAsync(draft, TripleProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenProbedUnderSubjectGrouped_ThenSatisfiesTheFourPartGuarantee()
    {
        // The authored ordering must survive the round trip and still convert — an explicit
        // `subject_grouped` draft is read back as `subject_grouped`.
        const string csv = "s1,species,cat\ns1,colour,black\ns2,species,dog\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            csv, ordering: TripleOrdering.SubjectGrouped));

        Assert.Equal(TripleOrdering.SubjectGrouped, draft.Binding!.Ordering);
        await AssertGuaranteeAsync(draft, TripleProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenProbedWithAHeaderAndNameRoles_ThenSatisfiesTheFourPartGuarantee()
    {
        // The name-addressed map has to resolve on REREAD too, not only during the probe — which
        // is the whole reason the draft authors the caller's addressing mode rather than indices.
        const string csv = "subj,pred,val\ns1,species,cat\ns2,species,dog\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            csv, hasHeader: true, columns: TripleProbeFixtures.Names("subj", "pred", "val")));

        await AssertGuaranteeAsync(draft, TripleProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenProbedWithNonDefaultIndexRoles_ThenSatisfiesTheFourPartGuarantee()
    {
        const string csv = "cat,species,s1\nblack,colour,s1\ndog,species,s2\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            csv, columns: TripleProbeFixtures.Indexes(subject: 2, predicate: 1, value: 0)));

        Assert.Equal(["species", "colour"], draft.Attributes.Select(a => a.Name));
        await AssertGuaranteeAsync(draft, TripleProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenPredicateNamesWereAdjusted_ThenStillSatisfiesTheGuarantee()
    {
        // The case the naming matrix exists for: the synthesized name must resolve, and its
        // selector must still find the real predicate in the data.
        const string csv = "s1,\"q\"\"uote\",a\ns1,predicate_0,b\ns2,\"q\"\"uote\",c\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeAttributeNameAdjusted);
        await AssertGuaranteeAsync(draft, TripleProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenAPredicateHasNoValues_ThenStillSatisfiesTheGuarantee()
    {
        // An all-missing attribute authors no domain, so the Calibrate phase has to discover an
        // empty one — a structurally valid degenerate context, which D-107 explicitly allows.
        const string csv = "s1,species,cat\ns1,colour,?\ns2,colour,?\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));

        Assert.Null(draft.Attributes[1].DeclaredDomain);
        var diagnostics = await AssertGuaranteeAsync(draft, TripleProbeFixtures.Bytes(csv));

        // Degenerate, and reported as such — a warning, never an error.
        Assert.All(diagnostics, d => Assert.NotEqual(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public async Task Draft_WhenProbedFromMiniAdultTriples_ThenSatisfiesTheFourPartGuarantee()
    {
        var path = FixturePath("mini-adult", "mini-adult_triples.data");
        var settings = TripleProbeFixtures.TripleSettings();
        Func<Stream> open = () => File.OpenRead(path);

        var result = await Prober.ProbeTripleAsync(new TripleCsvSession(open, settings), settings);
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);

        // First-appearance predicate order over the real v2 fixture, whose rows are grouped by
        // predicate rather than by subject — so this is genuinely discovery order, not row order.
        Assert.Equal(
            ["age", "education", "employment", "sex", "US-citizen", "class"],
            draft.Attributes.Select(a => a.Name));

        // No type inference: an entirely numeric predicate is still string-valued identity +
        // nominal (D-106), with its values in first-observation order.
        Assert.Equal(SourceValueType.String, TripleProbeFixtures.SourceOf(draft, "age").ValueType);
        Assert.Equal(["39", "50", "38", "53", "28", "37", "49", "52"], draft.Attributes[0].DeclaredDomain);

        // The real `?` in the education predicate is missing, so it never enters the domain.
        Assert.Equal(["Bachelors", "HS-grad", "11th", "Masters"], draft.Attributes[1].DeclaredDomain);

        await AssertGuaranteeAsync(draft, open);
    }

    [Fact]
    public async Task Draft_WhenProbedFromMiniMushroomTriples_ThenSatisfiesTheFourPartGuarantee()
    {
        var path = FixturePath("mini-mushroom", "mini-mushroom_triples.data");
        var settings = TripleProbeFixtures.TripleSettings();
        Func<Stream> open = () => File.OpenRead(path);

        var result = await Prober.ProbeTripleAsync(new TripleCsvSession(open, settings), settings);
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        await AssertGuaranteeAsync(draft, open);
    }

    [Fact]
    public async Task Draft_WhenTruncated_ThenIncludeRecoversTheCompleteSchema()
    {
        // D-108's recovery claim for triple: a truncated draft converted over the probed source
        // must plan the SAME columns, in the same order, as an untruncated probe's — the retained
        // prefix first, then the dropped tail re-appended by the `include` calibration.
        const string csv = "s1,p,q\ns2,p,r\ns3,p,s\ns4,p,t\n";

        var full = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));
        var truncated = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(
            csv, options: ProbeOptions.Create(valueRetentionLimit: 2)));

        Assert.Equal(["q", "r", "s", "t"], full.Attributes[0].DeclaredDomain);
        Assert.Equal(["q", "r"], truncated.Attributes[0].DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Include, truncated.Attributes[0].UnknownValuePolicy);

        var fullColumns = await PlannedColumnsAsync(full, TripleProbeFixtures.Bytes(csv));
        var recovered = await PlannedColumnsAsync(truncated, TripleProbeFixtures.Bytes(csv));

        Assert.Equal(fullColumns, recovered);
        Assert.Equal(4, recovered.Count);
    }

    // --- Probe vs Calibrate --------------------------------------------------------------------

    [Fact]
    public async Task ProbedDomains_WhenComparedWithCalibrate_ThenTheyAgreeExactly()
    {
        // D-112 category 5 for triple: probe's ordered-distinct observation must equal the domain
        // Calibrate observes over the same source — same values, same order. Discovery
        // re-implements the observer because it may not reference Conversion, so this is what
        // stops the two from drifting.
        const string csv = "s1,p,b\ns2,q,z\ns3,p,a\ns4,p,b\ns5,q,y\ns6,p,c\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));
        var calibrated = await CalibratedDomainsAsync(draft, TripleProbeFixtures.Bytes(csv));

        Assert.Equal(["b", "a", "c"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(["z", "y"], draft.Attributes[1].DeclaredDomain);
        foreach (var attribute in draft.Attributes)
        {
            Assert.Equal(attribute.DeclaredDomain, calibrated[attribute.Name!]);
        }
    }

    [Fact]
    public async Task ProbedDomains_WhenTheSourceMixesMissingAndRepeats_ThenStillMatchCalibrate()
    {
        const string csv = "s1,p,x\ns2,p,?\ns3,p,x\ns4,p,y\ns5,p,\ns6,p,z\ns7,p,y\n";

        var draft = ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv));
        var calibrated = await CalibratedDomainsAsync(draft, TripleProbeFixtures.Bytes(csv));

        Assert.Equal(["x", "y", "z"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(draft.Attributes[0].DeclaredDomain, calibrated["p"]);
    }

    [Fact]
    public async Task ProbedDomains_WhenProbedFromMiniAdultTriples_ThenMatchCalibrateThroughout()
    {
        var path = FixturePath("mini-adult", "mini-adult_triples.data");
        var settings = TripleProbeFixtures.TripleSettings();
        Func<Stream> open = () => File.OpenRead(path);

        var draft = ProbeFixtures.Draft(
            await Prober.ProbeTripleAsync(new TripleCsvSession(open, settings), settings));
        var calibrated = await CalibratedDomainsAsync(draft, open);

        foreach (var attribute in draft.Attributes)
        {
            Assert.Equal(attribute.DeclaredDomain, calibrated[attribute.Name!]);
        }
    }

    // Calibrate's observed domains for the draft's own attributes, reached by stripping the
    // authored declared_domain so the Calibrate phase must discover them (§7 / D-098).
    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>?>> CalibratedDomainsAsync(
        SpecDocument draft, Func<Stream> open)
    {
        var stripped = draft with
        {
            Attributes = [.. draft.Attributes.Select(a => a with
            {
                DeclaredDomain = null,
                UnknownValuePolicy = null,
                Description = null,
            })],
        };

        var calibrated = await CalibrateAsync(stripped, open);
        return calibrated.Spec.Attributes.ToDictionary(a => a.Name, a => a.DeclaredDomain, StringComparer.Ordinal);
    }

    // The formal-attribute names a draft plans over a source — the observable form of "the same
    // column set, in the same order".
    private static async Task<IReadOnlyList<string>> PlannedColumnsAsync(SpecDocument draft, Func<Stream> open)
    {
        var calibrated = await CalibrateAsync(draft, open);
        var planned = ConversionPlanner.Plan(calibrated, LabelStyle.Native);

        Assert.True(planned.TryGetValue(out var plan), ProbeFixtures.Describe(planned.Diagnostics));
        return [.. plan.FormalAttributes.Select(a => a.RenderedName)];
    }

    private static async Task<CalibratedSpec> CalibrateAsync(SpecDocument document, Func<Stream> open)
    {
        var settings = SpecResolver.ResolveReadSettings(document).Value!;
        var session = new TripleCsvSession(open, settings);
        var resolved = SpecResolver.Resolve(document, await session.GetSchemaAsync());

        Assert.True(resolved.TryGetValue(out var resolvedDocument), ProbeFixtures.Describe(resolved.Diagnostics));
        var calibrated = await Calibrator.CalibrateTripleAsync(
            resolvedDocument.Resolved, session.Bind(resolvedDocument.Resolved));

        Assert.True(calibrated.TryGetValue(out var spec), ProbeFixtures.Describe(calibrated.Diagnostics));
        return spec;
    }
}
