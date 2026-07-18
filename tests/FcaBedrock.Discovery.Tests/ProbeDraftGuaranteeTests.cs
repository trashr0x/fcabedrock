using FcaBedrock.Conversion;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// The D-107 four-part draft guarantee, end to end: a successful probe produces a draft that
/// (1) has at least one attribute, (2) rereads under the <b>strict</b> TOML reader, (3) resolves
/// against the probed schema with no Error/Fatal, and (4) converts the same source under the
/// same effective settings with no Error/Fatal.
/// <para>
/// This is what makes "a probe draft is immediately usable" a tested property rather than a
/// hope: the reread → resolve → convert chain is exactly what a user does next. Probe itself
/// runs no conversion (D-109 forbids Discovery from referencing it), so leg 4 is proven here,
/// through a <b>test-only</b> Conversion reference.
/// </para>
/// </summary>
public sealed class ProbeDraftGuaranteeTests
{
    private static string FixturePath(string example, string file) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", example, file);

    /// <summary>
    /// Runs all four legs and returns the conversion's own diagnostics for further assertions.
    /// </summary>
    private static async Task<IReadOnlyList<BedrockDiagnostic>> AssertGuaranteeAsync(
        SpecDocument draft, Func<Stream> openSource)
    {
        // (1) at least one attribute.
        Assert.NotEmpty(draft.Attributes);

        // (2) strict reread of the canonical text. Strict matters: the reader rejects unknown
        // keys, so a draft carrying a field the format does not define would fail right here.
        var toml = SpecWriter.Write(draft);
        var reread = SpecReader.Read(toml);
        Assert.True(reread.TryGetValue(out var rereadDocument), ProbeFixtures.Describe(reread.Diagnostics));
        Assert.DoesNotContain(reread.Diagnostics, IsErrorOrWorse);

        // The reread document must also be the SAME document — canonical text that round-trips
        // to something else would satisfy legs 2-4 while quietly meaning something different.
        Assert.Equal(toml, SpecWriter.Write(rereadDocument));

        // (3) resolve against the probed source's own schema, through the ordinary two-stage
        // bootstrap the conversion pipeline uses.
        var settingsResult = SpecResolver.ResolveReadSettings(rereadDocument);
        Assert.True(settingsResult.TryGetValue(out var settings), ProbeFixtures.Describe(settingsResult.Diagnostics));

        var session = new WideCsvSession(openSource, settings);
        var schema = await session.GetSchemaAsync();
        var resolved = SpecResolver.Resolve(rereadDocument, schema);
        Assert.DoesNotContain(resolved.Diagnostics, IsErrorOrWorse);
        Assert.True(resolved.TryGetValue(out var resolvedDocument), ProbeFixtures.Describe(resolved.Diagnostics));

        // (4) convert the same source under the same effective settings.
        var source = session.Bind(resolvedDocument.Resolved);
        var calibrated = await Calibrator.CalibrateAsync(resolvedDocument.Resolved, source);
        Assert.DoesNotContain(calibrated.Diagnostics, IsErrorOrWorse);
        Assert.True(calibrated.TryGetValue(out var calibratedSpec), ProbeFixtures.Describe(calibrated.Diagnostics));

        var planned = ConversionPlanner.Plan(calibratedSpec, LabelStyle.Native);
        Assert.DoesNotContain(planned.Diagnostics, IsErrorOrWorse);
        Assert.True(planned.TryGetValue(out var plan), ProbeFixtures.Describe(planned.Diagnostics));

        var emitDiagnostics = new List<BedrockDiagnostic>();
        var objects = 0;
        await foreach (var _ in Emitter.EmitAsync(plan, source, emitDiagnostics))
        {
            objects++;
        }

        // Warnings and structurally-valid degenerate contexts are explicitly allowed (D-107);
        // Errors and Fatals are not.
        Assert.DoesNotContain(emitDiagnostics, IsErrorOrWorse);

        var all = new List<BedrockDiagnostic>(calibrated.Diagnostics);
        all.AddRange(planned.Diagnostics);
        all.AddRange(emitDiagnostics);
        Assert.True(objects >= 0);
        return all;
    }

    private static bool IsErrorOrWorse(BedrockDiagnostic diagnostic) =>
        diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Fatal;

    [Fact]
    public async Task Draft_WhenProbedFromASimpleWideSource_ThenSatisfiesTheFourPartGuarantee()
    {
        const string csv = "colour,size\nred,big\nblue,?\nred,small\n";

        var draft = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(csv));

        await AssertGuaranteeAsync(draft, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenProbedFromMiniMushroom_ThenSatisfiesTheFourPartGuarantee()
    {
        var path = FixturePath("mini-mushroom", "mini-mushroom.data");
        var settings = ProbeFixtures.WideSettings();
        Func<Stream> open = () => File.OpenRead(path);

        var result = await Prober.ProbeAsync(new WideCsvSession(open, settings), settings);
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["class", "bruises?", "gill-size", "veil-type", "ring-number"],
            draft.Attributes.Select(a => a.Name));

        // `bruises?` is the case worth naming: a header that is §10.1-valid but looks like the
        // missing token's cousin. Header cells are never missing-normalized, so it binds by name.
        Assert.Equal("bruises?", ((ColumnSourceSection)draft.Attributes[1].Source!).Name);
        Assert.Equal(["t", "f"], draft.Attributes[1].DeclaredDomain);

        await AssertGuaranteeAsync(draft, open);
    }

    [Fact]
    public async Task Draft_WhenProbedFromMiniAdult_ThenSatisfiesTheFourPartGuarantee()
    {
        var path = FixturePath("mini-adult", "mini-adult.data");
        var settings = ProbeFixtures.WideSettings();
        Func<Stream> open = () => File.OpenRead(path);

        var result = await Prober.ProbeAsync(new WideCsvSession(open, settings), settings);
        var draft = ProbeFixtures.Draft(result);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["age", "education", "employment", "sex", "US-citizen", "class"],
            draft.Attributes.Select(a => a.Name));

        // mini-adult carries a real `?` in the education column: it is missing, so it never
        // enters the domain — the observed values are the four real ones, in first-observation
        // order (not sorted, and not including the token).
        Assert.Equal(["Bachelors", "HS-grad", "11th", "Masters"], draft.Attributes[1].DeclaredDomain);

        // Ages are authored as STRINGS: M5 does no type inference, so a numeric-looking column
        // is still string-valued identity + nominal (D-106).
        Assert.Equal(SourceValueType.String, ((ColumnSourceSection)draft.Attributes[0].Source!).ValueType);
        Assert.Equal(["39", "50", "38", "53", "28", "37", "49", "52"], draft.Attributes[0].DeclaredDomain);

        await AssertGuaranteeAsync(draft, open);
    }

    [Fact]
    public async Task Draft_WhenProbedFromAHeaderlessSource_ThenSatisfiesTheFourPartGuarantee()
    {
        // Index-bound attributes and `has_header = false` must resolve and convert exactly as
        // name-bound ones do — the fallback path is not a second-class draft.
        const string csv = "red,big\nblue,small\n";
        var settings = ProbeFixtures.WideSettings(hasHeader: false);

        var draft = ProbeFixtures.Draft(
            await Prober.ProbeAsync(new WideCsvSession(ProbeFixtures.Bytes(csv), settings), settings));

        await AssertGuaranteeAsync(draft, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenProbedFromDuplicateAndBlankHeaders_ThenStillSatisfiesTheGuarantee()
    {
        // The case the whole naming matrix exists for. Before the read became header-tolerant
        // this input could not even be probed; now it must not merely probe but also CONVERT —
        // which is why duplicates bind by index rather than by an ambiguous name.
        const string csv = "a,a,,b\n1,2,3,4\n5,6,7,8\n";

        var result = await ProbeFixtures.ProbeCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ProbeAttributeNameAdjusted);
        await AssertGuaranteeAsync(draft, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Draft_WhenTruncated_ThenIncludeRecoversTheCompleteSchema()
    {
        // D-108's recovery claim, made concrete: a truncated draft converted over the probed
        // source must produce the SAME columns, in the same order, as an untruncated probe's —
        // the retained prefix first, then the dropped tail re-appended in first-observation
        // order by the `include` calibration (§10.6 / §17 rule 3).
        const string csv = "col\nq\nr\ns\nt\n";

        var full = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(csv));
        var truncated = ProbeFixtures.Draft(
            await ProbeFixtures.ProbeCsvAsync(csv, options: ProbeOptions.Create(valueRetentionLimit: 2)));

        Assert.Equal(["q", "r", "s", "t"], full.Attributes[0].DeclaredDomain);
        Assert.Equal(["q", "r"], truncated.Attributes[0].DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Include, truncated.Attributes[0].UnknownValuePolicy);

        var fullColumns = await PlannedColumnsAsync(full, ProbeFixtures.Bytes(csv));
        var recovered = await PlannedColumnsAsync(truncated, ProbeFixtures.Bytes(csv));

        Assert.Equal(fullColumns, recovered);
        Assert.Equal(4, recovered.Count);
    }

    [Fact]
    public async Task ProbedDomain_WhenComparedWithCalibrate_ThenTheyAgreeExactly()
    {
        // The cross-check that keeps probe and Calibrate structurally aligned (D-106): probe's
        // ordered-distinct observation must equal the domain Calibrate observes for the same
        // input — same values, same order. Discovery re-implements the observer because it may
        // not reference Conversion, so this is what stops the two from drifting.
        const string csv = "col\nb\na\nb\nc\na\nd\n";

        var probed = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(csv)).Attributes[0].DeclaredDomain;
        var calibrated = await CalibratedDomainAsync(csv);

        Assert.Equal(["b", "a", "c", "d"], probed);
        Assert.Equal(probed, calibrated);
    }

    [Fact]
    public async Task ProbedDomain_WhenTheSourceMixesMissingAndRepeats_ThenStillMatchesCalibrate()
    {
        const string csv = "col\nx\n?\nx\ny\n?\nz\ny\n";

        var probed = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(csv)).Attributes[0].DeclaredDomain;

        Assert.Equal(["x", "y", "z"], probed);
        Assert.Equal(probed, await CalibratedDomainAsync(csv));
    }

    // Calibrate's observed domain for a single-column source, reached by deliberately authoring
    // NO declared_domain so the Calibrate phase must discover one (§7 / D-098).
    private static async Task<IReadOnlyList<string>> CalibratedDomainAsync(string csv)
    {
        var document = new SpecDocument(
            new SpecSection(1, null, null, null, null, null),
            null,
            new BindingSection(SourceShape.Wide, null, null, null, true, null, null, null, null, null),
            null, null, [], [],
            [
                new AttributeSection(
                    "col", new ColumnSourceSection(0, null, SourceValueType.String), null, null, null,
                    new IdentityDiscretizerSection(), new NominalScaleSection(),
                    null, null, null, null, null),
            ]);

        var settings = SpecResolver.ResolveReadSettings(document).Value!;
        var session = new WideCsvSession(ProbeFixtures.Bytes(csv), settings);
        var resolved = SpecResolver.Resolve(document, await session.GetSchemaAsync()).Value!;
        var calibrated = await Calibrator.CalibrateAsync(resolved.Resolved, session.Bind(resolved.Resolved));

        Assert.True(calibrated.TryGetValue(out var spec), ProbeFixtures.Describe(calibrated.Diagnostics));
        return spec.Spec.Attributes.Single(a => a.Name == "col").DeclaredDomain;
    }

    // The formal-attribute names a draft plans over a source, which is the observable form of
    // "the same column set, in the same order".
    private static async Task<IReadOnlyList<string>> PlannedColumnsAsync(SpecDocument draft, Func<Stream> open)
    {
        var settings = SpecResolver.ResolveReadSettings(draft).Value!;
        var session = new WideCsvSession(open, settings);
        var resolved = SpecResolver.Resolve(draft, await session.GetSchemaAsync()).Value!;
        var calibrated = await Calibrator.CalibrateAsync(resolved.Resolved, session.Bind(resolved.Resolved));

        Assert.True(calibrated.TryGetValue(out var spec), ProbeFixtures.Describe(calibrated.Diagnostics));
        var planned = ConversionPlanner.Plan(spec, LabelStyle.Native);
        Assert.True(planned.TryGetValue(out var plan), ProbeFixtures.Describe(planned.Diagnostics));

        return [.. plan.FormalAttributes.Select(a => a.RenderedName)];
    }
}
