using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// The <c>equal_frequency</c> TOML surface (§11.5, M4 Slice D / D-103): the document carrier,
/// the reader's parse-phase field gates, canonical writing, round-trip idempotence, and the
/// resolve seam.
/// <para>
/// Unlike <c>equal_width</c> there is no range/span surface and no spec-determined mode: every
/// <c>equal_frequency</c> spec draws its cuts from the population, so resolution always yields
/// the <c>CalibrationPending</c> carrier the Calibrate phase replaces (§7/D-093).
/// </para>
/// </summary>
public sealed class EqualFrequencySpecTests
{
    private static string Attribute(string discretizer) =>
        $"[spec]\nversion = 1\n[[attribute]]\nname = \"a\"\nsource = {{ kind = \"column\", index = 0 }}\ndiscretizer = {discretizer}\n";

    private static EqualFrequencyDiscretizerSection ReadDiscretizer(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<EqualFrequencyDiscretizerSection>(document.Attributes[0].Discretizer);
    }

    private static void AssertFieldInvalid(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecFieldInvalid);
    }

    // --- Reading -------------------------------------------------------------

    [Fact]
    public void Read_WhenKindIsEqualFrequency_ThenNoLongerTheTransitionalReject()
    {
        // D-103 reverses the D-070 tier-2 reject: equal_frequency now has a real carrier, so it
        // must NOT produce DiscretizerKindNotYetSupported any more.
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 4 }"));

        Assert.True(result.IsOk);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DiscretizerKindNotYetSupported);
    }

    [Fact]
    public void Read_WhenOnlyBinsAuthored_ThenTiePolicyAndCutPlacementStayUnauthored()
    {
        // Presence tracking (D-049): the §11.5 defaults are left/right_value, but an omitted field
        // must stay omitted so the writer does not invent authored config.
        var section = ReadDiscretizer("{ kind = \"equal_frequency\", bins = 4 }");

        Assert.Equal(4, section.Bins);
        Assert.Null(section.TiePolicy);
        Assert.Null(section.CutPlacement);
    }

    [Theory]
    [InlineData("left", TiePolicy.Left)]
    [InlineData("right", TiePolicy.Right)]
    public void Read_WhenEveryTiePolicySpelling_ThenCarried(string spelling, TiePolicy expected) =>
        Assert.Equal(expected, ReadDiscretizer($"{{ kind = \"equal_frequency\", bins = 4, tie_policy = \"{spelling}\" }}").TiePolicy);

    [Theory]
    [InlineData("right_value", CutPlacement.RightValue)]
    [InlineData("midpoint", CutPlacement.Midpoint)]
    public void Read_WhenEveryCutPlacementSpelling_ThenCarried(string spelling, CutPlacement expected) =>
        Assert.Equal(expected, ReadDiscretizer($"{{ kind = \"equal_frequency\", bins = 4, cut_placement = \"{spelling}\" }}").CutPlacement);

    [Fact]
    public void Read_WhenEveryFieldAuthored_ThenAllCarried()
    {
        var section = ReadDiscretizer("{ kind = \"equal_frequency\", bins = 7, tie_policy = \"right\", cut_placement = \"midpoint\" }");

        Assert.Equal(7, section.Bins);
        Assert.Equal(TiePolicy.Right, section.TiePolicy);
        Assert.Equal(CutPlacement.Midpoint, section.CutPlacement);
    }

    [Fact]
    public void Read_WhenBinsMissing_ThenSpecFieldInvalid()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\" }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("declares no bins", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenBinsIsNotAnInteger_ThenSpecFieldInvalidNamesTheField()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 4.5 }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("'bins' expects an integer", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("2147483648")]  // above int.MaxValue: the carrier is long?, so parse gates the range
    public void Read_WhenBinsOutOfRange_ThenSpecFieldInvalid(string bins) =>
        AssertFieldInvalid($"{{ kind = \"equal_frequency\", bins = {bins} }}");

    [Fact]
    public void Read_WhenBinsIsExactlyIntMaxValue_ThenAccepted() =>
        // The boundary the long? carrier exists to police: int.MaxValue is in range, one more is not.
        Assert.Equal(int.MaxValue, ReadDiscretizer($"{{ kind = \"equal_frequency\", bins = {int.MaxValue} }}").Bins);

    [Fact]
    public void Read_WhenTiePolicySpellingUnrecognized_ThenSpecFieldInvalidListsTheAcceptedSpellings()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 4, tie_policy = \"middle\" }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("\"left\" or \"right\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenCutPlacementSpellingUnrecognized_ThenSpecFieldInvalidListsTheAcceptedSpellings()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 4, cut_placement = \"left_value\" }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("\"right_value\" or \"midpoint\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tie_policy = 4")]
    [InlineData("cut_placement = true")]
    [InlineData("tie_policy = [\"left\"]")]
    public void Read_WhenAFieldHasTheWrongType_ThenSpecFieldInvalid(string field) =>
        AssertFieldInvalid($"{{ kind = \"equal_frequency\", bins = 4, {field} }}");

    [Fact]
    public void Read_WhenAnUnknownKeyIsPresent_ThenSpecKeyUnrecognized()
    {
        // equal_frequency has a real carrier now, so its table is walked — an unknown key inside
        // it is ordinary unknown-key handling, not the deferred-kind silence (D-075).
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 4, wibble = 1 }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, diagnostic.Code);
        Assert.Contains("wibble", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenAuthoredWithARangeField_ThenSpecKeyUnrecognized() =>
        // equal_frequency has no span surface: `range` belongs to equal_width (§11.4/§11.5).
        Assert.Equal(
            DiagnosticCode.SpecKeyUnrecognized,
            Assert.Single(SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 4, range = \"min_max\" }")).Diagnostics).Code);

    [Fact]
    public void Read_WhenSeveralFieldsAreInvalid_ThenAllAggregateWithoutAFactoryException()
    {
        // P-14: every field is read before the gates run, so independent problems report together
        // rather than the first one stopping the pass — and the strict carrier constructor never
        // sees them.
        var result = SpecReader.Read(Attribute("{ kind = \"equal_frequency\", bins = 1, tie_policy = \"middle\", cut_placement = \"nope\" }"));

        Assert.False(result.IsOk);
        Assert.Equal(3, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
    }

    // --- Writing and round-trip ----------------------------------------------

    // Returns the canonical `discretizer = …` value alone, so the round-trip below can feed it
    // straight back in.
    private static string WriteDiscretizer(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var text = SpecWriter.Write(document);
        var line = text.Split('\n').Single(l => l.StartsWith("discretizer = ", StringComparison.Ordinal));
        return line["discretizer = ".Length..];
    }

    [Fact]
    public void Write_WhenEveryFieldAuthored_ThenCanonicalPresentationOrder() =>
        // §11.5 presentation order: kind, bins, tie_policy, cut_placement — regardless of the
        // order the author wrote them in.
        Assert.Equal(
            "{ kind = \"equal_frequency\", bins = 4, tie_policy = \"right\", cut_placement = \"midpoint\" }",
            WriteDiscretizer("{ cut_placement = \"midpoint\", tie_policy = \"right\", bins = 4, kind = \"equal_frequency\" }"));

    [Fact]
    public void Write_WhenDefaultsOmitted_ThenTheyStayOmitted() =>
        // D-049: the writer records what the author wrote. The resolved defaults are spelled where
        // they are semantically load-bearing — the §14 fingerprint — not injected into the text,
        // which would silently change an author's spec on a round-trip.
        Assert.Equal(
            "{ kind = \"equal_frequency\", bins = 4 }",
            WriteDiscretizer("{ kind = \"equal_frequency\", bins = 4 }"));

    [Fact]
    public void Write_WhenOnlyOneDefaultAuthored_ThenOnlyThatOneIsWritten() =>
        // Presence is tracked per field, not per table.
        Assert.Equal(
            "{ kind = \"equal_frequency\", bins = 4, cut_placement = \"right_value\" }",
            WriteDiscretizer("{ kind = \"equal_frequency\", bins = 4, cut_placement = \"right_value\" }"));

    [Theory]
    [InlineData("{ kind = \"equal_frequency\", bins = 4 }")]
    [InlineData("{ kind = \"equal_frequency\", bins = 4, tie_policy = \"left\" }")]
    [InlineData("{ kind = \"equal_frequency\", bins = 4, cut_placement = \"right_value\" }")]
    [InlineData("{ kind = \"equal_frequency\", bins = 2, tie_policy = \"right\", cut_placement = \"midpoint\" }")]
    [InlineData("{ kind = \"equal_frequency\", bins = 10, tie_policy = \"left\", cut_placement = \"midpoint\" }")]
    public void WriteReadWrite_WhenEqualFrequencyForm_ThenCanonicalTextIsIdempotent(string discretizer)
    {
        // parse → write → parse → write is a fixed point for every supported form (D-075).
        var first = WriteDiscretizer(discretizer);
        var second = WriteDiscretizer(first);

        Assert.Equal(first, second);
    }

    // --- Resolution (§10.2/§11.5/§12.3) --------------------------------------

    private static Diagnosed<Core.Spec.BedrockSpec> Resolve(
        DiscretizerSection discretizer, ScaleSection? scale = null, Core.Spec.SourceValueType? valueType = null)
    {
        var document = DocumentFixtures.Document([
            DocumentFixtures.Attribute("score", DocumentFixtures.Column(0, valueType),
                discretizer: discretizer, scale: scale ?? new NominalScaleSection()),
        ]);
        var resolved = SpecResolver.Resolve(document, new Core.Spec.SourceSchema(1));
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<Core.Spec.BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<Core.Spec.BedrockSpec>.Failed(resolved.Diagnostics);
    }

    private static EqualFrequencyDiscretizerSection Section(
        long? bins = 4, TiePolicy? tiePolicy = null, CutPlacement? cutPlacement = null) =>
        new(bins, tiePolicy, cutPlacement);

    private static PendingEqualFrequency ResolvePending(EqualFrequencyDiscretizerSection section)
    {
        var result = Resolve(section);
        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var carrier = Assert.IsType<CalibrationPending>(spec!.Attributes[0].Discretizer);
        return Assert.IsType<PendingEqualFrequency>(carrier.Config);
    }

    [Fact]
    public void Resolve_WhenEqualFrequency_ThenAlwaysTheCalibrationPendingCarrier()
    {
        // §7/§11.5: there is no spec-determined mode — every configuration is data-dependent, so
        // it can never resolve to an executable discretizer.
        var result = Resolve(Section());

        Assert.True(result.TryGetValue(out var spec));
        Assert.IsType<CalibrationPending>(spec!.Attributes[0].Discretizer);
        Assert.True(CalibratedSpec.RequiresData(spec));
    }

    [Fact]
    public void Resolve_WhenDefaultsOmitted_ThenTheSeamResolvesThemForTheCarrier()
    {
        // The document keeps the authored/omitted distinction; the resolved carrier carries one
        // concrete configuration, so the calibrator and the §14 fingerprint never see "omitted".
        var config = ResolvePending(Section());

        Assert.Equal(TiePolicy.Left, config.TiePolicy);
        Assert.Equal(CutPlacement.RightValue, config.CutPlacement);
        Assert.Equal(4, config.Bins);
    }

    [Fact]
    public void Resolve_WhenConfigurationAuthored_ThenCarriedVerbatim()
    {
        var config = ResolvePending(Section(7, TiePolicy.Right, CutPlacement.Midpoint));

        Assert.Equal(7, config.Bins);
        Assert.Equal(TiePolicy.Right, config.TiePolicy);
        Assert.Equal(CutPlacement.Midpoint, config.CutPlacement);
    }

    [Fact]
    public void Resolve_WhenPendingCarrierBuilt_ThenCultureIsTheResolvedReadOnlyParsingCulture()
    {
        // P-11: the carrier hands the calibrator the spec's binding.locale, never an ambient one,
        // and the ResolvedSpec boundary re-homes it read-only (D-098).
        var result = Resolve(Section());
        Assert.True(result.TryGetValue(out var spec));
        var carrier = Assert.IsType<CalibrationPending>(spec!.Attributes[0].Discretizer);

        Assert.True(carrier.Culture.IsReadOnly);
        Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, carrier.Culture);
    }

    [Fact]
    public void Resolve_WhenBinsIsUnusable_ThenAttributeScalingMissingRatherThanAThrow()
    {
        // The reader owns the authored form, so this is the backstop for a hand-built section that
        // bypassed it: the strict carrier constructor must never see an invalid argument, and an
        // unbuildable discretizer resolves like the others (§10.9) instead of throwing.
        var result = Resolve(Section(bins: 1));

        Assert.False(result.IsOk);
        Assert.Equal(DiagnosticCode.AttributeScalingMissing, Assert.Single(result.Diagnostics).Code);
    }

    // --- value_type (§10.2/D-061) --------------------------------------------

    [Fact]
    public void Resolve_WhenValueTypeAbsent_ThenEqualFrequencyFixesNumber() =>
        // Its cuts are numeric, so equal_frequency is number-fixing exactly like equal_width.
        Assert.Equal(
            Core.Spec.SourceValueType.Number,
            Assert.IsType<Core.Spec.ColumnSource>(
                Resolve(Section()).Value!.Attributes[0].Source).ValueType);

    [Fact]
    public void Resolve_WhenValueTypeIsNumber_ThenAccepted() =>
        Assert.True(Resolve(Section(), valueType: Core.Spec.SourceValueType.Number).IsOk);

    [Fact]
    public void Resolve_WhenValueTypeIsString_ThenSourceValueTypeInvalid()
    {
        var result = Resolve(Section(), valueType: Core.Spec.SourceValueType.String);

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SourceValueTypeInvalid, diagnostic.Code);
        Assert.Contains("equal_frequency is number-fixing", diagnostic.Message, StringComparison.Ordinal);
    }

    // --- ordinal over cut bins (§12.3/D-060) ---------------------------------

    [Fact]
    public void Resolve_WhenOrdinalWithAuthoredOrder_ThenOrdinalOrderNotAllowedWithCuts()
    {
        // equal_frequency's bins are cut intervals, so the cut geometry is the ordering authority
        // — an authored scale.order would be a second, conflicting one (§12.3). It needs no
        // equal_frequency-specific ordinal path: the existing cut-kind gate covers it.
        var result = Resolve(Section(), new OrdinalScaleSection(null, null, ["a", "b"], null));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.OrdinalOrderNotAllowedWithCuts);
    }

    [Fact]
    public void Resolve_WhenOrdinalWithoutOrder_ThenAcceptedBecauseCutGeometryOrdersTheBins() =>
        // The complement: no authored order means no conflict — OrdinalOrderMissing is a value-bin
        // rule, and these are cut bins.
        Assert.True(Resolve(Section(), new OrdinalScaleSection(null, null, null, null)).IsOk);
}
