using System.Globalization;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

// The ResolvedSpec.Create trust boundary (D-098/G-1): it exhaustively validates the structural
// invariants downstream phases trust, throwing ArgumentException for a hand-built graph that
// violates any of them, so the planner's residual invariants become unreachable-by-construction.
public sealed class ResolvedSpecTests
{
    private static ResolvedSpec Create(BedrockSpec spec, SourceSchema? schema, params ResolvedNameBinding[] nameBindings) =>
        ResolvedSpec.Create(spec, schema, SpecFixtures.Settings(spec.Binding), nameBindings);

    [Fact]
    public void Create_WhenValidFullyDeclaredWide_ThenSucceeds()
    {
        var resolved = Create(SpecFixtures.MiniMushroom(), new SourceSchema(5));

        Assert.Equal(SourceShape.Wide, resolved.Spec.Binding.Shape);
        Assert.Equal(5, resolved.Schema!.ColumnCount);
    }

    [Fact]
    public void Create_WhenIncludedAttributeHasNoDiscretizer_ThenThrows()
    {
        var attr = new AttributeSpec("g", new ColumnSource(0, SourceValueType.String), Include: true,
            Discretizer: null, new NominalScale(), ["b"], RestrictTo: [], SpecFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

        Assert.Throws<ArgumentException>(() => Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenPredicateSourceUnderWideBinding_ThenThrows()
    {
        var attr = new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
            new IdentityDiscretizer(), new NominalScale(), ["b"], RestrictTo: [], SpecFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

        Assert.Throws<ArgumentException>(() => Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenColumnSourceUnderTripleBinding_ThenThrows()
    {
        var attr = SpecFixtures.Nominal("g", 0, ["b"]); // a ColumnSource
        Assert.Throws<ArgumentException>(() =>
            Create(new BedrockSpec(SpecFixtures.TripleSubjectGrouped(), [attr]), new SourceSchema(3)));
    }

    [Fact]
    public void Create_WhenTripleBindingHasNullOrdering_ThenThrows()
    {
        var binding = new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail), new TripleColumns(0, 1, 2), Ordering: null);
        var spec = new BedrockSpec(binding, [SpecFixtures.PredicateNominal("c", "hasColor", ["r"])]);

        Assert.Throws<ArgumentException>(() =>
            ResolvedSpec.Create(spec, new SourceSchema(3),
                SourceReadSettings.Create(SourceShape.Triple, "utf-8", ',', '"', false, "?", TripleOrdering.Unordered), []));
    }

    [Fact]
    public void Create_WhenTripleRolesNotDistinct_ThenThrows()
    {
        var binding = new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(0, DuplicateObjectPolicy.Fail), new TripleColumns(0, 0, 2), TripleOrdering.Unordered);
        var spec = new BedrockSpec(binding, [SpecFixtures.PredicateNominal("c", "hasColor", ["r"])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(3)));
    }

    [Fact]
    public void Create_WhenWideBindingCarriesTripleColumns_ThenThrows()
    {
        var binding = new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: true, "invariant", "?",
            new RowIndexObjectKey(), new TripleColumns(0, 1, 2), Ordering: null);
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(2)));
    }

    [Fact]
    public void Create_WhenUndefinedEnumValueViaCast_ThenThrows()
    {
        var attr = SpecFixtures.Nominal("g", 0, ["b"]) with { UnknownValuePolicy = (UnknownValuePolicy)99 };
        Assert.Throws<ArgumentException>(() => Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenLocaleUnresolvable_ThenThrows()
    {
        var binding = SpecFixtures.WideRowIndex() with { Locale = "not-a-real-locale" };
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenSourceIndexOutOfRange_ThenThrows()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("g", 5, ["b"])]);
        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(2)));
    }

    [Fact]
    public void Create_WhenObjectKeyIndexOutOfRange_ThenThrows()
    {
        var binding = new Binding(SourceShape.Wide, "utf-8", ',', '"', HasHeader: true, "invariant", "?",
            new ColumnObjectKey(5, DuplicateObjectPolicy.Fail));
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(2)));
    }

    [Fact]
    public void Create_WhenSchemaColumnCountNegative_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("g", 0, ["b"])]), new SourceSchema(-1)));

    [Fact]
    public void Create_WhenSchemaHeaderCountMismatchesColumnCount_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("g", 0, ["b"])]),
                new SourceSchema(3, ["only", "two"])));

    [Fact]
    public void Create_WhenNameBindingsButNullSchema_ThenThrows() =>
        Assert.Throws<ArgumentException>(() =>
            Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("age", 0, ["b"])]),
                schema: null, new AttributeSourceNameBinding("age", "age", 0)));

    [Fact]
    public void Create_WhenNameBindingMatchesHeaderAndSite_ThenSucceeds()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("age", 1, ["b"])]);

        var resolved = Create(spec, new SourceSchema(2, ["id", "age"]), new AttributeSourceNameBinding("age", "age", 1));

        Assert.Equal(2, resolved.Schema!.ColumnCount);
    }

    [Fact]
    public void Create_WhenNameBindingIndexMismatchesHeader_ThenThrows()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("age", 1, ["b"])]);

        // The header says "age" is at index 1, but the binding claims index 0.
        Assert.Throws<ArgumentException>(() =>
            Create(spec, new SourceSchema(2, ["id", "age"]), new AttributeSourceNameBinding("age", "age", 0)));
    }

    [Fact]
    public void Create_WhenNameBindingSiteIndexMismatchesAttribute_ThenThrows()
    {
        // The header has "age" at index 1 and the binding says index 1, but the attribute's
        // resolved source is actually column 0 — the site check catches the divergence.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [SpecFixtures.Nominal("age", 0, ["b"])]);

        Assert.Throws<ArgumentException>(() =>
            Create(spec, new SourceSchema(2, ["id", "age"]), new AttributeSourceNameBinding("age", "age", 1)));
    }

    [Fact]
    public void Create_WhenObjectKeyNameBindingMatchesSite_ThenSucceeds()
    {
        var binding = SpecFixtures.WideRowIndex() with { ObjectKey = new ColumnObjectKey(1, DuplicateObjectPolicy.Fail) };
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        var resolved = Create(spec, new SourceSchema(2, ["id", "key"]), new ObjectKeyNameBinding("key", 1));

        Assert.IsType<ColumnObjectKey>(resolved.Spec.Binding.ObjectKey);
    }

    [Fact]
    public void Create_WhenObjectKeyNameBindingIndexMismatchesSite_ThenThrows()
    {
        var binding = SpecFixtures.WideRowIndex() with { ObjectKey = new ColumnObjectKey(1, DuplicateObjectPolicy.Fail) };
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        // The object key resolves to column 1, but the binding claims the "key" header at index 0.
        Assert.Throws<ArgumentException>(() =>
            Create(spec, new SourceSchema(2, ["key", "other"]), new ObjectKeyNameBinding("key", 0)));
    }

    [Theory]
    [InlineData(TripleRole.Subject, 0)]
    [InlineData(TripleRole.Predicate, 1)]
    [InlineData(TripleRole.Value, 2)]
    public void Create_WhenTripleRoleNameBindingMatchesSite_ThenSucceeds(TripleRole role, int index)
    {
        var spec = new BedrockSpec(SpecFixtures.TripleSubjectGrouped(TripleOrdering.Unordered),
            [SpecFixtures.PredicateNominal("c", "hasColor", ["r"])]);
        var header = new[] { "s", "p", "v" };

        var resolved = Create(spec, new SourceSchema(3, header), new TripleRoleNameBinding(role, header[index], index));

        Assert.Equal(SourceShape.Triple, resolved.Spec.Binding.Shape);
    }

    [Fact]
    public void Create_WhenTripleRoleNameBindingIndexMismatchesSite_ThenThrows()
    {
        var spec = new BedrockSpec(SpecFixtures.TripleSubjectGrouped(TripleOrdering.Unordered),
            [SpecFixtures.PredicateNominal("c", "hasColor", ["r"])]);

        // The subject role resolves to column 0, but the binding claims header "p" at index 1.
        Assert.Throws<ArgumentException>(() =>
            Create(spec, new SourceSchema(3, ["s", "p", "v"]), new TripleRoleNameBinding(TripleRole.Subject, "p", 1)));
    }

    [Fact]
    public void Create_WhenObjectKeyIsNull_ThenThrows()
    {
        // A null union member is a structural violation the trust boundary rejects cleanly, not an NRE.
        var binding = SpecFixtures.WideRowIndex() with { ObjectKey = null! };
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        Assert.ThrowsAny<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenRestrictToEntryIsNull_ThenThrows()
    {
        var attr = SpecFixtures.Nominal("g", 0, ["b"]) with { RestrictTo = [null!] };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        Assert.ThrowsAny<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    // --- restrict_to entries (§10.4/D-091/D-105) -----------------------------

    [Fact]
    public void Create_WhenRestrictToEntryIsAnUnknownVariant_ThenThrows()
    {
        // RestrictToEntry is deliberately not mechanically closed — the Spec document model reuses
        // it (D-057) — so the trust boundary rejects an unrecognized variant explicitly. Silently
        // carrying one would reach the emitter's matcher, which cannot classify it.
        var attr = SpecFixtures.Nominal("g", 0, ["b"]) with { RestrictTo = [new UnknownRestrictToEntry()] };
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenAllThreeRecognizedVariantsPresent_ThenSucceeds()
    {
        // The complete M4 union resolves: a string entry on a string source, and exact + range
        // entries on a number source. (Entry-vs-value_type consistency is the seam's job; this
        // boundary checks representability.)
        var gene = SpecFixtures.Nominal("Gene", 0, ["Bmp5"]) with { RestrictTo = [new RestrictToValue("Bmp5")] };
        var age = SpecFixtures.NumericCuts("age", 1, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToNumber(30.0), new RestrictToRange(10.0, 20.0), new RestrictToRange(null, null)],
        };

        var resolved = Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [gene, age]), new SourceSchema(2));

        Assert.Equal(3, resolved.Spec.Attributes[1].RestrictTo.Count);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_WhenExactRestrictValueIsNonFinite_ThenThrows(double value)
    {
        // D-091/round-6 High-2: the seam diagnoses an authored non-finite value
        // (RestrictToRangeInvalid) and returns Diagnosed.Failed BEFORE any strict factory runs,
        // so reaching this boundary with one means a hand-built graph — genuine programmer error.
        // Downstream then trusts finiteness (the fingerprint's formatter rejects non-finite
        // outright, and the matcher compares without re-checking).
        var attr = SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToNumber(value)],
        };

        Assert.Throws<ArgumentException>(() => Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1)));
    }

    [Theory]
    [InlineData(double.NaN, null)]
    [InlineData(null, double.NaN)]
    [InlineData(double.NegativeInfinity, 20.0)]
    [InlineData(10.0, double.PositiveInfinity)]
    public void Create_WhenARangeBoundIsNonFinite_ThenThrows(double? from, double? to)
    {
        var attr = SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToRange(from, to)],
        };

        Assert.Throws<ArgumentException>(() => Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenRangeBoundsAreOmitted_ThenSucceedsBecauseOpenIsNullNotInfinity()
    {
        // §10.4: an open end is null — NOT ±infinity — so {} and one-sided ranges are valid and
        // must not be caught by the non-finite check.
        var attr = SpecFixtures.NumericCuts("age", 0, [30.0], new NominalScale()) with
        {
            RestrictTo = [new RestrictToRange(null, null), new RestrictToRange(90.0, null), new RestrictToRange(null, 5.0)],
        };

        var resolved = Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1));

        Assert.Equal(3, resolved.Spec.Attributes[0].RestrictTo.Count);
    }

    [Fact]
    public void Create_WhenAuthoredRestrictListMutatedAfterwards_ThenTheResolvedSpecIsUnaffected()
    {
        // D-098 recursive immutability: Create deep-snapshots the graph, so a caller-held list
        // cannot reach a resolved spec, a plan, an emit, or a fingerprint afterwards.
        var authored = new List<RestrictToEntry> { new RestrictToValue("Bmp5") };
        var attr = SpecFixtures.Nominal("Gene", 0, ["Bmp5"]) with { RestrictTo = authored };

        var resolved = Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1));
        authored.Add(new RestrictToValue("Wnt1"));

        Assert.Single(resolved.Spec.Attributes[0].RestrictTo);
    }

    [Fact]
    public void Create_WhenInspected_ThenRestrictEntriesAreNotCastableToAMutableCollection()
    {
        // The snapshot must be recursively immutable, not merely copied: an IReadOnlyList backed
        // by a plain array is castable back to T[] and mutable through it (D-098 Critical-2).
        var attr = SpecFixtures.Nominal("Gene", 0, ["Bmp5"]) with { RestrictTo = [new RestrictToValue("Bmp5")] };

        var resolved = Create(new BedrockSpec(SpecFixtures.WideRowIndex(), [attr]), new SourceSchema(1));

        var entries = resolved.Spec.Attributes[0].RestrictTo;
        Assert.IsNotType<RestrictToEntry[]>(entries);
        Assert.IsNotType<List<RestrictToEntry>>(entries);
    }

    private sealed record UnknownRestrictToEntry : RestrictToEntry;

    [Fact]
    public void Create_WhenObjectKeyIsUnknownSubtype_ThenThrows()
    {
        // ObjectKey is a public, externally-derivable record — the trust boundary must reject an
        // unknown subtype rather than let it fall through the planner to a silent row_index at emit.
        var binding = SpecFixtures.WideRowIndex() with { ObjectKey = new UnknownObjectKey() };
        var spec = new BedrockSpec(binding, [SpecFixtures.Nominal("g", 0, ["b"])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenOriginalParsingCultureMutatedAfterResolution_ThenClassificationUnaffected()
    {
        // The resolved discretizer carries a read-only culture clone, so mutating the original
        // culture's NumberFormat cannot change parsing/classification after resolution (D-098, P-11).
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone(); // a mutable clone
        var discretizer = ManualCutsDiscretizer.Create([30.0], BinEnds.Open, culture).Value!;
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [new AttributeSpec("age", new ColumnSource(0, SourceValueType.Number), Include: true,
                discretizer, new NominalScale(), DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        var resolved = Create(spec, new SourceSchema(1));

        culture.NumberFormat.NumberDecimalSeparator = ","; // would break "40.5" if it leaked through

        var resolvedDisc = Assert.IsType<ManualCutsDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(resolvedDisc.Culture.IsReadOnly);
        Assert.Equal(BinResult.Bin(">=30"), resolvedDisc.Discretize("40.5"));
    }

    [Fact]
    public void Create_WhenFreePerValueParsingCultureMutatedAfterResolution_ThenClassificationUnaffected()
    {
        // The numeric free_per_value discretizer is rebuilt with a read-only culture clone, so a
        // caller mutating the originally-mutable culture cannot change parsing/classification after
        // resolution (D-098 recursive immutability, P-11) — analogous to the manual-cuts case.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone(); // a mutable clone
        var discretizer = new FreePerValueDiscretizer(SourceValueType.Number, culture);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [new AttributeSpec("v", new ColumnSource(0, SourceValueType.Number), Include: true,
                discretizer, new NominalScale(), DeclaredDomain: ["40.5"], RestrictTo: [], SpecFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        var resolved = Create(spec, new SourceSchema(1));

        culture.NumberFormat.NumberDecimalSeparator = ","; // would change "40.5" if it leaked through

        var resolvedDisc = Assert.IsType<FreePerValueDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(resolvedDisc.Culture.IsReadOnly);
        Assert.Equal(BinResult.Bin("40.5"), resolvedDisc.Discretize("40.5")); // still the "." decimal
    }

    // --- equal_width / pending calibration (M4 Slice C, D-102) ----------------

    private static BedrockSpec EqualWidthSpec(Discretizer discretizer) =>
        new(SpecFixtures.WideRowIndex(),
            [new AttributeSpec("score", new ColumnSource(0, SourceValueType.Number), Include: true,
                discretizer, new NominalScale(), DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);

    [Fact]
    public void Create_WhenEqualWidthParsingCultureMutatedAfterResolution_ThenClassificationUnaffected()
    {
        // The equal_width discretizer is rebuilt over a read-only culture clone, exactly like
        // manual_cuts and free_per_value — a caller mutating its own culture cannot change which
        // bin a value lands in after resolution (D-098 recursive immutability, P-11).
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone(); // a mutable clone
        var discretizer = EqualWidthDiscretizer.CreateManual(4, 0, 100, CutPrecision.Exact, culture).Value!;
        var resolved = Create(EqualWidthSpec(discretizer), new SourceSchema(1));

        culture.NumberFormat.NumberDecimalSeparator = ","; // would break "30.5" if it leaked through

        var resolvedDisc = Assert.IsType<EqualWidthDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(resolvedDisc.Culture.IsReadOnly);
        Assert.Equal(BinResult.Bin("[25, 50)"), resolvedDisc.Discretize("30.5"));
    }

    [Fact]
    public void Create_WhenEqualWidthResolved_ThenCutsAreRetainedNotRederivedAndNotCastable()
    {
        var discretizer = EqualWidthDiscretizer.CreateManual(4, 0, 100, CutPrecision.Exact, CultureInfo.InvariantCulture).Value!;

        var resolved = Create(EqualWidthSpec(discretizer), new SourceSchema(1));

        var resolvedDisc = Assert.IsType<EqualWidthDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.Equal([25.0, 50.0, 75.0], resolvedDisc.Cuts);
        Assert.IsNotType<double[]>(resolvedDisc.Cuts);
        Assert.IsNotType<List<double>>(resolvedDisc.Cuts);
        Assert.Equal(RoundToPrecision.Create(1), EqualWidthDiscretizer
            .CreateManual(4, 0, 100, RoundToPrecision.Create(1), CultureInfo.InvariantCulture).Value!.Precision);
    }

    [Fact]
    public void Create_WhenCalibrationPendingResolved_ThenCarrierSurvivesOnAReadOnlyCulture()
    {
        // The pending carrier must survive resolution intact — it is what Calibrate replaces
        // (D-093) — with its culture re-homed like any other.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        var pending = new CalibrationPending(new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), culture);

        var resolved = Create(EqualWidthSpec(pending), new SourceSchema(1));

        var carrier = Assert.IsType<CalibrationPending>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(carrier.Culture.IsReadOnly);
        Assert.Equal(new PendingEqualWidth(4, EqualWidthRange.MinMax, CutPrecision.Exact), carrier.Config);
    }

    // The trust boundary also re-checks equal_width's range/precision/bounds coherence, and — at
    // Slice D — equal_frequency's tie_policy/cut_placement and the pending union's variants (see
    // ResolvedSpec.ValidateDiscretizerEnums). Those arms are deliberately unreachable from outside
    // Core and have no negative test, because the states they reject are UNREPRESENTABLE rather
    // than merely rejected (P-10, asserted directly by
    // EqualWidthDiscretizerTests.EqualWidthDiscretizer_WhenInspected_ThenNoPublicConstructorOrSetter,
    // PendingEqualWidth's guards, and PendingEqualFrequencyTests' undefined-enum rejections): every
    // property is get-only so `with` cannot desync them, the only constructors are the validating
    // factories, and CutPrecision / PendingCalibration are private-protected-closed unions no
    // out-of-assembly type can extend. They stay as the P-10 backstop for a future in-assembly
    // caller, matching this file's existing defensive arms.

    // --- equal_frequency (M4 Slice D, D-103) ----------------------------------

    [Fact]
    public void Create_WhenPendingEqualFrequencyResolved_ThenCarrierSurvivesOnAReadOnlyCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        var pending = new CalibrationPending(
            new PendingEqualFrequency(3, TiePolicy.Right, CutPlacement.Midpoint), culture);

        var resolved = Create(EqualWidthSpec(pending), new SourceSchema(1));

        var carrier = Assert.IsType<CalibrationPending>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(carrier.Culture.IsReadOnly);
        Assert.Equal(new PendingEqualFrequency(3, TiePolicy.Right, CutPlacement.Midpoint), carrier.Config);
    }

    [Fact]
    public void Create_WhenEqualFrequencyResolved_ThenCutsAreRetainedAndNotCastable()
    {
        // The calibrated cuts ARE the resolved identity: there is no data here to re-select from,
        // so the snapshot must carry them rather than re-derive (D-093).
        var resolved = Create(EqualWidthSpec(EqualFrequency([2, 3])), new SourceSchema(1));

        var discretizer = Assert.IsType<EqualFrequencyDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.Equal([2.0, 3.0], discretizer.Cuts);
        Assert.Equal(3, discretizer.Bins);
        Assert.IsNotType<double[]>(discretizer.Cuts);
        Assert.IsNotType<List<double>>(discretizer.Cuts);
    }

    [Fact]
    public void Create_WhenEqualFrequencyParsingCultureMutatedAfterResolution_ThenClassificationUnaffected()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        var resolved = Create(EqualWidthSpec(EqualFrequency([25, 50], culture)), new SourceSchema(1));

        culture.NumberFormat.NumberDecimalSeparator = ","; // would break "30.5" if it leaked through

        var discretizer = Assert.IsType<EqualFrequencyDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(discretizer.Culture.IsReadOnly);
        Assert.Equal(BinResult.Bin("[25, 50)"), discretizer.Discretize("30.5"));
    }

    // Builds an executable equal_frequency the only way production can: through the calibrated-state
    // substitution over a pending carrier.
    private static EqualFrequencyDiscretizer EqualFrequency(IReadOnlyList<double> cuts, CultureInfo? culture = null)
    {
        var pendingSpec = EqualWidthSpec(new CalibrationPending(
            new PendingEqualFrequency(cuts.Count + 1, TiePolicy.Left, CutPlacement.RightValue),
            culture ?? CultureInfo.InvariantCulture));
        var created = FcaBedrock.Core.Calibration.CalibratedSpec.Create(
            Create(pendingSpec, new SourceSchema(1)), [new FcaBedrock.Core.Calibration.CalibratedCuts("score", cuts)]);
        Assert.True(created.TryGetValue(out var calibrated));
        return Assert.IsType<EqualFrequencyDiscretizer>(calibrated.Spec.Attributes[0].Discretizer);
    }

    [Fact]
    public void Create_WhenFreePerValueValueTypeIsUndefinedEnum_ThenThrows()
    {
        // A cast can smuggle an undefined SourceValueType into the discretizer; the trust boundary
        // rejects it (defined-enum-member check, D-098).
        var discretizer = new FreePerValueDiscretizer((SourceValueType)99, CultureInfo.InvariantCulture);
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [new AttributeSpec("v", new ColumnSource(0, SourceValueType.Number), Include: true,
                discretizer, new NominalScale(), DeclaredDomain: ["1"], RestrictTo: [], SpecFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    // D-096/D-098: the trust boundary re-checks that numeric free_per_value domain/label/order keys are
    // canonical numeric identities, so a hand-built non-canonical spec cannot reach the planner/emitter
    // (where a non-canonical bin would be silently unmatchable). The resolve seam already guarantees it.
    private static AttributeSpec NumericFreePerValue(
        IReadOnlyList<string> domain, IReadOnlyList<string>? order = null,
        IReadOnlyDictionary<string, string>? valueLabels = null)
    {
        Scale scale = order is null
            ? new NominalScale()
            : new OrdinalScale(OrdinalDirection.Ge, DropTop: false, OrdinalBoundary.Inclusive, order);
        return new AttributeSpec("v", new ColumnSource(0, SourceValueType.Number), Include: true,
            new FreePerValueDiscretizer(SourceValueType.Number, CultureInfo.InvariantCulture), scale,
            domain, RestrictTo: [], valueLabels ?? SpecFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);
    }

    [Theory]
    [InlineData("bad")]       // unparseable
    [InlineData("90.0")]      // non-canonical form (canonical is "90")
    [InlineData("Infinity")]  // non-finite
    [InlineData("-0")]        // signed zero (canonical is "0")
    public void Create_WhenNumericFreePerValueDomainKeyNotCanonical_ThenThrows(string key)
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [NumericFreePerValue([key])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenNumericFreePerValueValueLabelKeyNotCanonical_ThenThrows()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [NumericFreePerValue(["90"], valueLabels: new Dictionary<string, string> { ["90.0"] = "ninety" })]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenNumericFreePerValueOrderKeyNotCanonical_ThenThrows()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(), [NumericFreePerValue(["90"], order: ["90.0"])]);

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenNumericFreePerValueCanonicalKeys_ThenSucceeds()
    {
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [NumericFreePerValue(["90", "0"], order: ["90", "0"], valueLabels: new Dictionary<string, string> { ["90"] = "ninety" })]);

        var resolved = Create(spec, new SourceSchema(1));

        Assert.Equal(["90", "0"], resolved.Spec.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Create_WhenStringFreePerValueNonNumericKeys_ThenSucceedsVerbatim()
    {
        // String free_per_value keys are verbatim strings — never subject to numeric canonicalization.
        var spec = new BedrockSpec(SpecFixtures.WideRowIndex(),
            [new AttributeSpec("g", new ColumnSource(0, SourceValueType.String), Include: true,
                new FreePerValueDiscretizer(SourceValueType.String, CultureInfo.InvariantCulture),
                new NominalScale(), ["b", "90.0", "n"], RestrictTo: [], SpecFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);

        var resolved = Create(spec, new SourceSchema(1));

        Assert.Equal(["b", "90.0", "n"], resolved.Spec.Attributes[0].DeclaredDomain);
    }

    // --- value_groups (M4 Slice E, D-104) -------------------------------------

    private static BedrockSpec ValueGroupsSpec(Discretizer discretizer) =>
        new(SpecFixtures.WideRowIndex(),
        [
            new AttributeSpec("edu", new ColumnSource(0, SourceValueType.String), Include: true,
                discretizer, new NominalScale(), DeclaredDomain: [], RestrictTo: [], SpecFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn),
        ]);

    private static CalibrationPending PassthroughPending(params ValueGroup[] groups) =>
        new(new PendingValueGroupsPassthrough(groups), CultureInfo.InvariantCulture);

    [Fact]
    public void Create_WhenValueGroupsIsExecutable_ThenAcceptedAndReachableUnchanged()
    {
        var discretizer = ValueGroupsDiscretizer.Create(
            [SpecFixtures.Group("School", "11th")], ValueGroupsUnmatched.Other);

        var resolved = Create(ValueGroupsSpec(discretizer), new SourceSchema(1));

        // Cultureless and immutable-by-construction (both factories snapshot the groups and each
        // group snapshots its own values), so — like identity and ordered_cuts — the trust boundary
        // reuses the instance rather than rebuilding it: there is no mutable state to re-home.
        Assert.Same(discretizer, resolved.Spec.Attributes[0].Discretizer);
    }

    [Fact]
    public void ValueGroupsDiscretizer_WhenInspected_ThenUndefinedUnmatchedIsUnrepresentableNotMerelyRejected()
    {
        // Two complementary guarantees, which together are why no undefined value can reach
        // matching, planning, emission, or fingerprints:
        //   (1) the factory rejects an undefined member outright (asserted in
        //       ValueGroupsDiscretizerTests), and
        //   (2) Unmatched is get-only — no setter and no `with`-settable init — so a cast value
        //       cannot be grafted onto an already-built instance the way it can onto a positional
        //       record. That is what makes the trust boundary's RequireDefined arm defence in
        //       depth rather than the only line of defence.
        Assert.Null(typeof(ValueGroupsDiscretizer).GetProperty(nameof(ValueGroupsDiscretizer.Unmatched))!.SetMethod);
        Assert.Throws<ArgumentException>(() =>
            ValueGroupsDiscretizer.Create([SpecFixtures.Group("School", "11th")], (ValueGroupsUnmatched)99));
    }

    [Fact]
    public void Create_WhenPassthroughPendingCarrier_ThenRecognizedAndRebuiltWithAReadOnlyCulture()
    {
        // Without this arm every passthrough spec would throw at the trust boundary as an unknown
        // pending variant — the deliberate recognition Slice E adds.
        var resolved = Create(ValueGroupsSpec(PassthroughPending(SpecFixtures.Group("School", "11th"))), new SourceSchema(1));

        var pending = Assert.IsType<CalibrationPending>(resolved.Spec.Attributes[0].Discretizer);
        Assert.True(pending.Culture.IsReadOnly);
        var config = Assert.IsType<PendingValueGroupsPassthrough>(pending.Config);
        Assert.Equal(["School"], config.Groups.Select(g => g.Label));
    }

    [Fact]
    public void Create_WhenPassthroughPendingCarriesDuplicateLabels_ThenThrows()
    {
        // The carrier is freely constructible and validates no collection rule of its own, so the
        // trust boundary owns label distinctness for it (§11.6/D-090).
        var spec = ValueGroupsSpec(PassthroughPending(
            SpecFixtures.Group("School", "11th"), SpecFixtures.Group("School", "Bachelors")));

        Assert.Throws<ArgumentException>(() => Create(spec, new SourceSchema(1)));
    }

    [Fact]
    public void Create_WhenPassthroughPendingCarriesAnOtherLabel_ThenAcceptedBecausePassthroughAddsNoSyntheticBin()
    {
        // The Other-collision rule is scoped to unmatched = "other" alone: passthrough adds no
        // synthetic bin, so "Other" is an ordinary group label there and must not be rejected.
        var resolved = Create(ValueGroupsSpec(PassthroughPending(SpecFixtures.Group("Other", "11th"))), new SourceSchema(1));

        Assert.IsType<CalibrationPending>(resolved.Spec.Attributes[0].Discretizer);
    }

    [Fact]
    public void Create_WhenCallerMutatesGroupsAfterResolution_ThenTheResolvedGraphIsUnaffected()
    {
        // The outer group list and each group's inner values are both caller-owned before
        // construction; neither may survive on the resolved graph (D-098).
        var values = new List<string> { "11th" };
        var groups = new List<ValueGroup> { ValueGroup.Create("School", values, null) };
        var resolved = Create(ValueGroupsSpec(ValueGroupsDiscretizer.Create(groups, ValueGroupsUnmatched.Skip)), new SourceSchema(1));

        groups.Add(SpecFixtures.Group("Undergrad", "Bachelors"));
        values.Add("HS-grad");

        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(resolved.Spec.Attributes[0].Discretizer);
        Assert.Equal(["School"], discretizer.Groups.Select(g => g.Label));
        Assert.Equal(["11th"], discretizer.Groups[0].Values);
        Assert.Equal(BinOutcome.Unknown, discretizer.Discretize("HS-grad").Outcome);
    }

    [Fact]
    public void Create_WhenValueGroupsResolved_ThenAuthoredNullAndAuthoredEmptyValuesBothSurvive()
    {
        // The presence distinction must survive the trust boundary intact — the §14 encoding reads
        // it directly, so collapsing [] to null here would silently change fingerprint bytes (G-11).
        var omitted = ValueGroup.Create("Pattern", null, "^I[0-9]{2}");
        var authoredEmpty = ValueGroup.Create("Empty", [], "^J[0-9]{2}");
        var resolved = Create(
            ValueGroupsSpec(ValueGroupsDiscretizer.Create([omitted, authoredEmpty], ValueGroupsUnmatched.Skip)),
            new SourceSchema(1));

        var groups = Assert.IsType<ValueGroupsDiscretizer>(resolved.Spec.Attributes[0].Discretizer).Groups;
        Assert.Null(groups[0].Values);
        Assert.NotNull(groups[1].Values);
        Assert.Empty(groups[1].Values!);
    }

    private sealed record UnknownObjectKey : ObjectKey;
}
