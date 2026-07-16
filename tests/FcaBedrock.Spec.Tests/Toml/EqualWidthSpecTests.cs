using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// The <c>equal_width</c> TOML surface (§11.4, M4 Slice C / D-102): the document carrier,
/// the reader's parse-phase field gates, canonical writing, and round-trip idempotence.
/// Slice C recognizes <c>range = "min_max"</c> and <c>"manual"</c> only —
/// <c>"percentile_p1_p99"</c> is modelled in Core but is an unrecognized <em>spelling</em>
/// here until its calibration lands at Slice D.
/// </summary>
public sealed class EqualWidthSpecTests
{
    private static string Attribute(string discretizer) =>
        $"[spec]\nversion = 1\n[[attribute]]\nname = \"a\"\nsource = {{ kind = \"column\", index = 0 }}\ndiscretizer = {discretizer}\n";

    private static EqualWidthDiscretizerSection ReadDiscretizer(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return Assert.IsType<EqualWidthDiscretizerSection>(document.Attributes[0].Discretizer);
    }

    private static void AssertFieldInvalid(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecFieldInvalid);
    }

    // --- Reading -------------------------------------------------------------

    [Fact]
    public void Read_WhenMinMaxWithDefaults_ThenRangeAndPrecisionStayUnauthored()
    {
        // Presence tracking (D-049): the §11.4 defaults are min_max/exact, but an omitted field
        // must stay omitted so the writer does not invent authored config.
        var section = ReadDiscretizer("{ kind = \"equal_width\", bins = 4 }");

        Assert.Equal(4, section.Bins);
        Assert.Null(section.Range);
        Assert.Null(section.Precision);
        Assert.Null(section.VMin);
        Assert.Null(section.VMax);
    }

    [Fact]
    public void Read_WhenMinMaxAuthoredExplicitly_ThenCarried()
    {
        var section = ReadDiscretizer("{ kind = \"equal_width\", bins = 8, range = \"min_max\", precision = \"exact\" }");

        Assert.Equal(8, section.Bins);
        Assert.Equal(EqualWidthRange.MinMax, section.Range);
        Assert.Equal(CutPrecision.Exact, section.Precision);
    }

    [Fact]
    public void Read_WhenManualWithBounds_ThenAllFieldsCarried()
    {
        var section = ReadDiscretizer(
            "{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0.0, vmax = 100.0, precision = { round_to = 1.0 } }");

        Assert.Equal(4, section.Bins);
        Assert.Equal(EqualWidthRange.Manual, section.Range);
        Assert.Equal(0.0, section.VMin);
        Assert.Equal(100.0, section.VMax);
        Assert.Equal(RoundToPrecision.Create(1), section.Precision);
    }

    [Fact]
    public void Read_WhenBoundsAuthoredAsIntegers_ThenAcceptedAsNumbers() =>
        // TOML integer and float nodes both feed a double field (the manual_cuts precedent).
        Assert.Equal(100.0, ReadDiscretizer("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0, vmax = 100 }").VMax);

    [Fact]
    public void Read_WhenBinsAtIntMaxValue_ThenAccepted() =>
        Assert.Equal(int.MaxValue, ReadDiscretizer($"{{ kind = \"equal_width\", bins = {int.MaxValue} }}").Bins);

    // --- Parse-phase field gates (§11.4) -------------------------------------

    [Fact]
    public void Read_WhenBinsMissing_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"equal_width\" }");

    [Fact]
    public void Read_WhenBinsNotAnInteger_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"equal_width\", bins = 4.5 }");

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void Read_WhenBinsBelowTwo_ThenSpecFieldInvalid(int bins) =>
        AssertFieldInvalid($"{{ kind = \"equal_width\", bins = {bins} }}");

    [Fact]
    public void Read_WhenBinsAboveIntMaxValue_ThenSpecFieldInvalid() =>
        // The carrier holds a long, so the reader range-checks before the int conversion.
        AssertFieldInvalid($"{{ kind = \"equal_width\", bins = {(long)int.MaxValue + 1} }}");

    [Fact]
    public void Read_WhenRangeUnrecognized_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"equal_width\", bins = 4, range = \"wibble\" }");

    [Fact]
    public void Read_WhenRangeIsPercentile_ThenSpecFieldInvalidNotDeferredKind()
    {
        // G-8b/D-102: percentile_p1_p99 is modelled in the Core enum but has no TOML spelling
        // until its calibration lands (Slice D). It must be an unrecognized RANGE spelling — never
        // silently mapped to min_max, never retained as an executable pending mode, and not
        // DiscretizerKindNotYetSupported (the kind itself IS supported now).
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = 4, range = \"percentile_p1_p99\" }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DiscretizerKindNotYetSupported);
        Assert.Contains("\"min_max\" or \"manual\"", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"manual\" }")]                 // neither bound
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0 }")]       // no vmax
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmax = 100 }")]     // no vmin
    public void Read_WhenManualMissingBounds_ThenSpecFieldInvalid(string discretizer) =>
        AssertFieldInvalid(discretizer);

    [Theory]
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"min_max\", vmin = 0, vmax = 100 }")]
    [InlineData("{ kind = \"equal_width\", bins = 4, vmin = 0, vmax = 100 }")] // range defaults to min_max
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"min_max\", vmin = 0 }")]
    public void Read_WhenBoundsAuthoredUnderDataRange_ThenSpecFieldInvalid(string discretizer) =>
        // §11.4/D-094: vmin/vmax are authored only under manual; a data range draws its span from
        // the data, so authoring them would be config the run silently ignores.
        AssertFieldInvalid(discretizer);

    [Theory]
    [InlineData("precision = \"wibble\"")]
    [InlineData("precision = 1")]
    [InlineData("precision = { }")]                    // no round_to
    [InlineData("precision = { round_to = 0 }")]       // must be > 0
    [InlineData("precision = { round_to = -1 }")]
    [InlineData("precision = { round_to = nan }")]
    [InlineData("precision = { round_to = inf }")]
    [InlineData("precision = { round_to = \"1\" }")]   // not a number
    public void Read_WhenPrecisionMalformed_ThenSpecFieldInvalid(string precision) =>
        AssertFieldInvalid($"{{ kind = \"equal_width\", bins = 4, {precision} }}");

    [Fact]
    public void Read_WhenPrecisionObjectHasUnknownKey_ThenSpecKeyUnrecognized()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = 4, precision = { round_to = 1, wibble = 2 } }"));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenDiscretizerHasUnknownKey_ThenSpecKeyUnrecognized()
    {
        // equal_width now has a real carrier, so its table is walked strictly — unlike the
        // deferred kinds, whose parameter keys are deliberately not walked (D-070 tier 2).
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = 4, wibble = 1 }"));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecKeyUnrecognized);
    }

    [Fact]
    public void Read_WhenSeveralEqualWidthProblems_ThenTheyAggregate()
    {
        // P-14: independent field problems report together, not first-one-wins.
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = 1, range = \"wibble\", precision = \"nope\" }"));

        Assert.False(result.IsOk);
        Assert.Equal(3, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
    }

    // One authored mistake gets ONE diagnostic (D-067): a malformed field reports its own type
    // error and must NOT also be reported as missing/absent by the semantic gate — the gates read
    // authored-vs-absent, not valid-vs-null. Asserted on the exact count, since a Contains-style
    // check cannot see the spurious second report.

    [Fact]
    public void Read_WhenBinsMalformed_ThenExactlyOneDiagnosticNamingTheType()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = \"four\" }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("expects an integer", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("declares no bins", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenManualBoundMalformed_ThenExactlyOneDiagnosticNamingTheType()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = \"x\", vmax = 100 }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("'vmin' expects a number", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenBoundMalformedUnderDataRange_ThenStillReportedAsAuthored()
    {
        // The corollary: a malformed vmin is still AUTHORED, so the "vmin/vmax apply only to
        // manual" gate must still fire — a mistyped bound must not smuggle itself past the rule.
        var result = SpecReader.Read(Attribute("{ kind = \"equal_width\", bins = 4, range = \"min_max\", vmin = \"x\" }"));

        Assert.False(result.IsOk);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("apply only to range", StringComparison.Ordinal));
    }

    // --- Writing and round-trip ----------------------------------------------

    private static string WriteDiscretizer(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));
        Assert.True(result.TryGetValue(out var document));
        var text = SpecWriter.Write(document);
        var line = text.Split('\n').Single(l => l.StartsWith("discretizer = ", StringComparison.Ordinal));
        return line["discretizer = ".Length..];
    }

    [Fact]
    public void Write_WhenManual_ThenCanonicalKeyOrderWithBounds() =>
        // §11.4 presentation order: kind, bins, range, vmin, vmax, precision.
        Assert.Equal(
            "{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0, vmax = 100, precision = { round_to = 1 } }",
            WriteDiscretizer("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0.0, vmax = 100.0, precision = { round_to = 1.0 } }"));

    [Fact]
    public void Write_WhenMinMax_ThenNoBoundsAndExactSpelling() =>
        Assert.Equal(
            "{ kind = \"equal_width\", bins = 4, range = \"min_max\", precision = \"exact\" }",
            WriteDiscretizer("{ kind = \"equal_width\", bins = 4, range = \"min_max\", precision = \"exact\" }"));

    [Fact]
    public void Write_WhenDefaultsOmitted_ThenOmittedFieldsStayOut() =>
        // The omitted range/precision are NOT materialized to their defaults: presence survives.
        Assert.Equal(
            "{ kind = \"equal_width\", bins = 4 }",
            WriteDiscretizer("{ kind = \"equal_width\", bins = 4 }"));

    [Fact]
    public void Write_WhenFractionalRoundTo_ThenFloatFormPreserved() =>
        Assert.Equal(
            "{ kind = \"equal_width\", bins = 4, precision = { round_to = 0.25 } }",
            WriteDiscretizer("{ kind = \"equal_width\", bins = 4, precision = { round_to = 0.25 } }"));

    [Theory]
    [InlineData("{ kind = \"equal_width\", bins = 4 }")]
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"min_max\", precision = \"exact\" }")]
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0, vmax = 100 }")]
    [InlineData("{ kind = \"equal_width\", bins = 10, range = \"manual\", vmin = -2.5, vmax = 7.5, precision = { round_to = 0.5 } }")]
    [InlineData("{ kind = \"equal_width\", bins = 4, range = \"manual\", vmin = 0.0, vmax = 100.0, precision = { round_to = 1.0 } }")]
    public void WriteReadWrite_WhenEqualWidthForm_ThenCanonicalTextIsIdempotent(string discretizer)
    {
        // The canonical form must be re-readable: parse → write → parse → write is a fixed point
        // for every Slice C-supported form (D-075).
        var first = WriteDiscretizer(discretizer);
        var second = WriteDiscretizer(first);

        Assert.Equal(first, second);
    }

    // --- Resolution (§10.2/§11.4/§12.3) --------------------------------------

    private static Diagnosed<Core.Spec.BedrockSpec> Resolve(DiscretizerSection discretizer, ScaleSection? scale = null,
        Core.Spec.SourceValueType? valueType = null)
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

    private static EqualWidthDiscretizerSection Section(
        long? bins = 4, EqualWidthRange? range = null, double? vmin = null, double? vmax = null, CutPrecision? precision = null) =>
        new(bins, range, vmin, vmax, precision);

    [Fact]
    public void Resolve_WhenManualRange_ThenExecutableDiscretizerNoCalibration()
    {
        // §7/D-089: a manual range is spec-determined — it resolves straight to an executable
        // discretizer and skips Calibrate.
        var result = Resolve(Section(4, EqualWidthRange.Manual, 0, 100));

        Assert.True(result.TryGetValue(out var spec));
        var discretizer = Assert.IsType<EqualWidthDiscretizer>(spec!.Attributes[0].Discretizer);
        Assert.Equal([25.0, 50.0, 75.0], discretizer.Cuts);
        Assert.False(CalibratedSpec.RequiresData(spec));
    }

    [Fact]
    public void Resolve_WhenDataRange_ThenCalibrationPendingCarrierRequiringData()
    {
        var result = Resolve(Section(4, EqualWidthRange.MinMax));

        Assert.True(result.TryGetValue(out var spec));
        var carrier = Assert.IsType<CalibrationPending>(spec!.Attributes[0].Discretizer);
        var config = Assert.IsType<PendingEqualWidth>(carrier.Config);
        Assert.Equal(4, config.Bins);
        Assert.Equal(EqualWidthRange.MinMax, config.Range);
        Assert.Equal(CutPrecision.Exact, config.Precision);
        Assert.True(CalibratedSpec.RequiresData(spec)); // calibration is required before planning
    }

    [Fact]
    public void Resolve_WhenRangeOmitted_ThenDefaultsToMinMaxPending() =>
        Assert.Equal(
            EqualWidthRange.MinMax,
            Assert.IsType<PendingEqualWidth>(
                Assert.IsType<CalibrationPending>(ResolveOk(Section()).Attributes[0].Discretizer).Config).Range);

    [Fact]
    public void Resolve_WhenPendingCarrierBuilt_ThenCultureIsTheResolvedReadOnlyParsingCulture()
    {
        // P-11: the carrier hands the calibrator the spec's binding.locale, never an ambient one,
        // and the ResolvedSpec boundary re-homes it read-only (D-098).
        var carrier = Assert.IsType<CalibrationPending>(ResolveOk(Section()).Attributes[0].Discretizer);

        Assert.True(carrier.Culture.IsReadOnly);
        Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, carrier.Culture);
    }

    private static Core.Spec.BedrockSpec ResolveOk(DiscretizerSection discretizer, ScaleSection? scale = null)
    {
        var result = Resolve(discretizer, scale);
        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return spec!;
    }

    private static DiagnosticCode SoleCode(Diagnosed<Core.Spec.BedrockSpec> result)
    {
        Assert.False(result.IsOk);
        return Assert.Single(result.Diagnostics).Code;
    }

    [Fact]
    public void Resolve_WhenManualRangeNonIncreasing_ThenEqualWidthRangeInvalid() =>
        // The seam owns the range semantics; parse only checked that both bounds were present.
        Assert.Equal(
            DiagnosticCode.EqualWidthRangeInvalid,
            SoleCode(Resolve(Section(4, EqualWidthRange.Manual, 100, 0))));

    [Fact]
    public void Resolve_WhenManualRangeNonFinite_ThenEqualWidthRangeInvalid() =>
        Assert.Equal(
            DiagnosticCode.EqualWidthRangeInvalid,
            SoleCode(Resolve(Section(4, EqualWidthRange.Manual, 0, double.PositiveInfinity))));

    [Fact]
    public void Resolve_WhenPrecisionCollapsesManualCuts_ThenEqualWidthCutsCollapsed() =>
        Assert.Equal(
            DiagnosticCode.EqualWidthCutsCollapsed,
            SoleCode(Resolve(Section(8, EqualWidthRange.Manual, 0, 2, RoundToPrecision.Create(1)))));

    [Fact]
    public void Resolve_WhenRangeDiagnostic_ThenTheAttributeCarriesTheScope() =>
        Assert.Equal("score", Resolve(Section(4, EqualWidthRange.Manual, 100, 0)).Diagnostics[0].Location?.AttributeName);

    // --- value_type (§10.2/D-061) --------------------------------------------

    [Fact]
    public void Resolve_WhenValueTypeAbsent_ThenEqualWidthFixesNumber() =>
        Assert.Equal(
            Core.Spec.SourceValueType.Number,
            Assert.IsType<Core.Spec.ColumnSource>(ResolveOk(Section(4, EqualWidthRange.Manual, 0, 100)).Attributes[0].Source).ValueType);

    [Fact]
    public void Resolve_WhenValueTypeNumberAuthored_ThenAccepted()
    {
        var result = Resolve(Section(4, EqualWidthRange.Manual, 0, 100), valueType: Core.Spec.SourceValueType.Number);

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(Core.Spec.SourceValueType.Number, Assert.IsType<Core.Spec.ColumnSource>(spec!.Attributes[0].Source).ValueType);
    }

    [Fact]
    public void Resolve_WhenValueTypeStringAuthored_ThenSourceValueTypeInvalid() =>
        // equal_width is number-fixing: its cuts are numeric (§10.2/D-061).
        Assert.Contains(
            Resolve(Section(4, EqualWidthRange.Manual, 0, 100), valueType: Core.Spec.SourceValueType.String).Diagnostics,
            d => d.Code == DiagnosticCode.SourceValueTypeInvalid);

    // --- ordinal over equal_width cut bins (§12.3/D-060) ---------------------

    [Fact]
    public void Resolve_WhenOrdinalOrderAuthoredOverEqualWidth_ThenOrdinalOrderNotAllowedWithCuts() =>
        // equal_width is a cut kind: its geometry is the single source of bin order, so an
        // authored scale.order is an error rather than silently-ignored config.
        Assert.Equal(
            DiagnosticCode.OrdinalOrderNotAllowedWithCuts,
            SoleCode(Resolve(
                Section(4, EqualWidthRange.Manual, 0, 100),
                new OrdinalScaleSection(OrdinalDirection.Le, Boundary: null, Order: ["a"], DropTop: null))));

    [Fact]
    public void Resolve_WhenOrdinalOrderAuthoredEmptyOverEqualWidth_ThenStillNotAllowed() =>
        // Presence is the violation (§12.3 "MUST NOT be present").
        Assert.Equal(
            DiagnosticCode.OrdinalOrderNotAllowedWithCuts,
            SoleCode(Resolve(
                Section(4, EqualWidthRange.Manual, 0, 100),
                new OrdinalScaleSection(OrdinalDirection.Le, Boundary: null, Order: [], DropTop: null))));

    [Theory]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Inclusive)]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Strict)]
    public void Resolve_WhenAuthoredBoundaryStraddlesEqualWidthGeometry_ThenIncompatible(
        OrdinalDirection direction, OrdinalBoundary boundary) =>
        Assert.Equal(
            DiagnosticCode.OrdinalBoundaryIncompatibleWithCuts,
            SoleCode(Resolve(
                Section(4, EqualWidthRange.Manual, 0, 100),
                new OrdinalScaleSection(direction, boundary, Order: null, DropTop: null))));

    [Theory]
    [InlineData(OrdinalDirection.Le, OrdinalBoundary.Strict)]
    [InlineData(OrdinalDirection.Ge, OrdinalBoundary.Inclusive)]
    public void Resolve_WhenAuthoredBoundaryMatchesEqualWidthGeometry_ThenAccepted(
        OrdinalDirection direction, OrdinalBoundary boundary) =>
        Assert.True(Resolve(
            Section(4, EqualWidthRange.Manual, 0, 100),
            new OrdinalScaleSection(direction, boundary, Order: null, DropTop: null)).IsOk);

    [Fact]
    public void Resolve_WhenBoundaryDefaultedOverEqualWidth_ThenNoDiagnostic() =>
        // D-060(c): only a per-attribute AUTHORED boundary can straddle; an omitted one is
        // defaulted and never selects the operator over cut bins.
        Assert.True(Resolve(
            Section(4, EqualWidthRange.Manual, 0, 100),
            new OrdinalScaleSection(OrdinalDirection.Le, Boundary: null, Order: null, DropTop: null)).IsOk);
}
