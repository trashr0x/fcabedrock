using System.Text;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests;

public sealed class BedMigratorTests
{
    // --- helpers -----------------------------------------------------------

    private static BindingSection WideBinding(
        char delimiter = ',', bool hasHeader = true, string? missingToken = null, string? locale = null) =>
        new(SourceShape.Wide, Encoding: null, delimiter, QuoteChar: null, hasHeader,
            locale, missingToken, Ordering: null, Columns: null, ObjectKey: null);

    private static BedDocument ReadBed(string text)
    {
        Assert.True(BedReader.Read(text).TryGetValue(out var document));
        return document;
    }

    private static SpecDocument MigrateOk(
        string bed, BindingSection? binding = null, ScalingMode mode = ScalingMode.Discrete, string? derivedFrom = null)
    {
        var migrated = BedMigrator.Migrate(ReadBed(bed), binding ?? WideBinding(), mode, derivedFrom);
        Assert.True(migrated.TryGetValue(out var document), Describe(migrated.Diagnostics));
        return document;
    }

    private static BedrockSpec ResolveOk(SpecDocument document)
    {
        var resolved = SpecResolver.Resolve(document);
        Assert.True(resolved.TryGetValue(out var spec), Describe(resolved.Diagnostics));
        return spec;
    }

    private static string Describe(IReadOnlyList<BedrockDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message}"));

    // One synthetic attribute row of the v2 parallel-array layout; Categories
    // defaults to Values (identity labels), Restrict to a blank line.
    private sealed record BedAttr(
        string Name, string Type, string Values, string? Categories = null, bool Convert = true, string Restrict = "");

    private static string Bed(params BedAttr[] attrs)
    {
        var sb = new StringBuilder();
        sb.Append("[Number of Attributes]\n").Append(attrs.Length).Append("\n\n");
        Section(sb, "Attributes", attrs.Select(a => a.Name));
        Section(sb, "Attribute Categories", attrs.Select(a => a.Categories ?? a.Values));
        Section(sb, "Category Values", attrs.Select(a => a.Values));
        Section(sb, "Convert Attribute", attrs.Select(a => a.Convert ? "True" : "False"));
        Section(sb, "Attribute Type", attrs.Select(a => a.Type));
        Section(sb, "Restrict To Values", attrs.Select(a => a.Restrict));
        sb.Append("[End]\n");
        return sb.ToString();

        static void Section(StringBuilder sb, string header, IEnumerable<string> lines)
        {
            sb.Append('[').Append(header).Append("]\n");
            foreach (var line in lines)
            {
                sb.Append(line).Append('\n');
            }

            sb.Append('\n');
        }
    }

    // --- document shape ----------------------------------------------------

    [Fact]
    public void Migrate_WhenMushroomBed_ThenSpecVersion1AndNothingElseAuthored()
    {
        var document = MigrateOk(BedFixtures.MushroomBed);

        Assert.NotNull(document.Spec);
        Assert.Equal(1, document.Spec.Version);
        // A fresh migrated spec is not frozen: no stored fingerprints (D-057), and
        // no fabricated description/provenance/defaults/output.
        Assert.Null(document.Spec.SchemaFingerprint);
        Assert.Null(document.Spec.CxtOutputFingerprint);
        Assert.Null(document.Spec.DatOutputFingerprint);
        Assert.Null(document.Spec.Description);
        Assert.Null(document.Provenance);
        Assert.Null(document.Defaults);
        Assert.Null(document.Output);
        Assert.Empty(document.Templates);
        Assert.Empty(document.Matchers);
    }

    [Fact]
    public void Migrate_WhenDerivedFromSupplied_ThenProvenanceCarriesDerivedFromOnly()
    {
        var document = MigrateOk(BedFixtures.MushroomBed, derivedFrom: "fixtures/v2/mini-mushroom/mini-mushroom.bed");

        Assert.NotNull(document.Provenance);
        Assert.Equal("fixtures/v2/mini-mushroom/mini-mushroom.bed", document.Provenance.DerivedFrom);
        Assert.Null(document.Provenance.CreatedAt); // no clock in pure code (P-7)
        Assert.Null(document.Provenance.Author);
    }

    [Fact]
    public void Migrate_WhenMigrated_ThenBindingSectionEmbeddedVerbatim()
    {
        var binding = WideBinding('\t', hasHeader: false);

        var document = MigrateOk(BedFixtures.MushroomBed, binding);

        Assert.Same(binding, document.Binding);
    }

    [Fact]
    public void Migrate_WhenMigrated_ThenSourcesArePositionalWithNoValueType()
    {
        // value_type is derived from the discretizer kind at resolve (D-061);
        // authoring it would only create a place for the two to disagree.
        var document = MigrateOk(BedFixtures.EmploymentOrdinalBed);

        Assert.All(document.Attributes.Select((a, i) => (a, i)), pair =>
        {
            var source = Assert.IsType<ColumnSourceSection>(pair.a.Source);
            Assert.Equal(pair.i, source.Index);
            Assert.Null(source.Name);
            Assert.Null(source.ValueType);
        });
    }

    // --- type maps ---------------------------------------------------------

    [Fact]
    public void Migrate_WhenTypeC_ThenIdentityNominalWithDomainAndNonIdentityValueLabels()
    {
        var gillSize = MigrateOk(BedFixtures.MushroomBed).Attributes[2];

        Assert.IsType<IdentityDiscretizerSection>(gillSize.Discretizer);
        Assert.IsType<NominalScaleSection>(gillSize.Scale);
        Assert.Equal(["b", "n"], gillSize.DeclaredDomain);
        Assert.NotNull(gillSize.ValueLabels);
        Assert.Equal("broad", gillSize.ValueLabels["b"]);
        Assert.Equal("narrow", gillSize.ValueLabels["n"]);
        Assert.Null(gillSize.Include);       // included is the default, not authored
        Assert.Null(gillSize.MissingPolicy); // skip is the default, not authored
        Assert.Null(gillSize.UnknownValuePolicy);
    }

    [Fact]
    public void Migrate_WhenAllLabelsIdentity_ThenValueLabelsNotAuthored()
    {
        // education's categories equal its values; identity mappings carry no
        // information (§10.8), so value_labels stays unauthored.
        var education = MigrateOk(BedFixtures.EmploymentOrdinalBed).Attributes[1];

        Assert.Null(education.ValueLabels);
        Assert.Equal(["Bachelors", "Masters", "11th", "HS-grad"], education.DeclaredDomain);
    }

    [Fact]
    public void Migrate_WhenTypeB_ThenDichotomicWithFirstValueAsTrueValueAndCarriedLabels()
    {
        var bruises = MigrateOk(BedFixtures.MushroomBed).Attributes[1];

        var scale = Assert.IsType<DichotomicScaleSection>(bruises.Scale);
        Assert.Equal("t", scale.TrueValue);
        Assert.IsType<IdentityDiscretizerSection>(bruises.Discretizer);
        Assert.Equal(["t", "f"], bruises.DeclaredDomain);
        // Dichotomic labels are dormant (the single column renders the attribute
        // name alone), but v2 authored them — they round-trip as parked config
        // rather than being silently dropped (D-049/D-079; new vs the M1 migrator).
        Assert.NotNull(bruises.ValueLabels);
        Assert.Equal("bruises", bruises.ValueLabels["t"]);
        Assert.Equal("no", bruises.ValueLabels["f"]);
    }

    [Fact]
    public void Migrate_WhenTypeO_ThenManualCutsWithParsedDoublesAndAuthoredOpenEnds()
    {
        var age = MigrateOk(BedFixtures.EmploymentOrdinalBed).Attributes[0];

        var cuts = Assert.IsType<ManualCutsDiscretizerSection>(age.Discretizer);
        Assert.Equal([30.0, 40.0, 50.0], cuts.Cuts);
        Assert.Equal(BinEnds.Open, cuts.Ends);
        Assert.Null(age.DeclaredDomain); // cut discretizers ignore declared_domain (§10.3)
        Assert.Null(age.ValueLabels);
    }

    [Fact]
    public void Migrate_WhenCutSpecHasNoSentinels_ThenEndsAuthoredClosed()
    {
        // ends is always authored: the resolver defaults an absent ends to open,
        // but a sentinel-less v2 cut spec means closed — omission would flip it.
        var document = MigrateOk(Bed(new BedAttr("age", "o", "30,40,50")));

        var cuts = Assert.IsType<ManualCutsDiscretizerSection>(document.Attributes[0].Discretizer);
        Assert.Equal(BinEnds.Closed, cuts.Ends);
    }

    [Fact]
    public void Migrate_WhenTypeN_ThenOrderedCutsFromCategoriesWithAuthoredEnds()
    {
        // v2 section roles for n (D-046): [Attribute Categories] is the ordered
        // domain, [Category Values] is the cut spec.
        var employment = MigrateOk(BedFixtures.EmploymentOrdinalBed).Attributes[2];

        var cuts = Assert.IsType<OrderedCutsDiscretizerSection>(employment.Discretizer);
        Assert.Equal(["Unskilled", "Clerical", "Professional", "Managerial"], cuts.Order);
        Assert.Equal(["Managerial"], cuts.Cuts);
        Assert.Equal(BinEnds.Open, cuts.Ends);
        Assert.Null(employment.DeclaredDomain);
    }

    [Fact]
    public void Migrate_WhenDiscreteMode_ThenCutTypesGetNominalScale()
    {
        var document = MigrateOk(BedFixtures.EmploymentOrdinalBed, mode: ScalingMode.Discrete);

        Assert.IsType<NominalScaleSection>(document.Attributes[0].Scale); // age (o)
        Assert.IsType<NominalScaleSection>(document.Attributes[2].Scale); // employment (n)
    }

    [Fact]
    public void Migrate_WhenProgressiveMode_ThenOrdinalLeWithNoBoundaryOrderOrDropTopAuthored()
    {
        // direction is the migration's choice, so it is authored; boundary/order/
        // drop_top must stay unauthored — over cut bins an authored boundary or
        // order is a D-060 validation error, and the defaults already match v2.
        var document = MigrateOk(BedFixtures.EmploymentOrdinalBed, mode: ScalingMode.Progressive);

        foreach (var index in (int[])[0, 2])
        {
            var ordinal = Assert.IsType<OrdinalScaleSection>(document.Attributes[index].Scale);
            Assert.Equal(OrdinalDirection.Le, ordinal.Direction);
            Assert.Null(ordinal.Boundary);
            Assert.Null(ordinal.Order);
            Assert.Null(ordinal.DropTop);
        }

        // Non-cut types are unaffected by the mode.
        Assert.IsType<NominalScaleSection>(document.Attributes[1].Scale);    // education (c)
        Assert.IsType<DichotomicScaleSection>(document.Attributes[4].Scale); // US-citizen (b)
    }

    // --- resolved Core shape ------------------------------------------------

    [Fact]
    public void Migrate_WhenMushroomResolved_ThenCoreShapeMatchesTheM1Migration()
    {
        // The resolved Core spec the M1 Core-targeting migrator used to build,
        // asserted explicitly (the resolver's defaults must keep reproducing it —
        // trap T7 in the Slice G plan). Excluded attributes deliberately resolve
        // parked-with-nulls now (D-079); their fidelity is document-level.
        var resolved = ResolveOk(MigrateOk(BedFixtures.MushroomBed));

        Assert.Equal(
            new Binding(SourceShape.Wide, ',', '"', HasHeader: true, "invariant", "?", new RowIndexObjectKey()),
            resolved.Binding);

        var gillSize = resolved.Attributes[2];
        Assert.True(gillSize.Include);
        Assert.Equal(new ColumnSource(2, SourceValueType.String), gillSize.Source);
        Assert.IsType<IdentityDiscretizer>(gillSize.Discretizer);
        Assert.IsType<NominalScale>(gillSize.Scale);
        Assert.Equal(["b", "n"], gillSize.DeclaredDomain);
        Assert.Equal("broad", gillSize.ValueLabels["b"]);
        Assert.Equal("narrow", gillSize.ValueLabels["n"]);
        Assert.Equal(MissingPolicy.Skip, gillSize.MissingPolicy);
        Assert.Equal(UnknownValuePolicy.Warn, gillSize.UnknownValuePolicy);
        Assert.Empty(gillSize.RestrictTo);

        var bruises = resolved.Attributes[1];
        Assert.Equal(new ColumnSource(1, SourceValueType.String), bruises.Source);
        var dichotomic = Assert.IsType<DichotomicScale>(bruises.Scale);
        Assert.Equal("t", dichotomic.TrueValue);
        Assert.Equal(["t", "f"], bruises.DeclaredDomain);
        // The M1 migrator dropped dichotomic display labels silently; the reworked
        // one carries them as dormant config (D-079 behavior change #5).
        Assert.Equal("bruises", bruises.ValueLabels["t"]);

        // Parked-with-nulls (D-049 at the seam): the excluded attribute's config
        // lives in the document, not the resolved spec.
        var cls = resolved.Attributes[0];
        Assert.False(cls.Include);
        Assert.Null(cls.Discretizer);
        Assert.Null(cls.Scale);
    }

    [Fact]
    public void Migrate_WhenMushroomResolvedAndPlanned_ThenFormalAttributesMatchV2Names()
    {
        var spec = ResolveOk(MigrateOk(BedFixtures.MushroomBed));

        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(5)).TryGetValue(out var plan));
        Assert.Equal(
            [
                "bruises?", "gill-size-broad", "gill-size-narrow", "veil-type-partial",
                "veil-type-universal", "ring-number-none", "ring-number-one", "ring-number-two",
            ],
            plan.FormalAttributes.Select(f => f.RenderedName));
    }

    [Fact]
    public void Migrate_WhenBindingLocaleNonInvariant_ThenResolvedDiscretizerParsesWithIt()
    {
        // Cut tokens are invariant schema strings (§14); only the data column reads
        // with the binding locale, threaded to the discretizer by the resolver.
        var spec = ResolveOk(MigrateOk(BedFixtures.EmploymentOrdinalBed, WideBinding(locale: "de-DE")));

        Assert.Equal(
            BinResult.Bin(">=50"),
            Assert.IsType<ManualCutsDiscretizer>(spec.Attributes[0].Discretizer).Discretize("50,5"));
    }

    // --- round-trip through the writer/reader --------------------------------

    [Theory]
    [InlineData(ScalingMode.Discrete)]
    [InlineData(ScalingMode.Progressive)]
    public void Migrate_WhenWrittenAndReread_ThenCanonicalTextIsIdempotent(ScalingMode mode)
    {
        var document = MigrateOk(BedFixtures.EmploymentOrdinalBed, mode: mode, derivedFrom: "mini-adult.bed");

        var written = SpecWriter.Write(document);
        Assert.True(SpecReader.Read(written).TryGetValue(out var reread));
        Assert.Equal(written, SpecWriter.Write(reread));
    }

    [Fact]
    public void Migrate_WhenWrittenRereadAndResolved_ThenPlansIdenticallyToDirectResolve()
    {
        var document = MigrateOk(BedFixtures.MushroomBed);

        Assert.True(SpecReader.Read(SpecWriter.Write(document)).TryGetValue(out var reread));
        Assert.True(ConversionPlanner.Plan(ResolveOk(document), new SourceSchema(5)).TryGetValue(out var direct));
        Assert.True(ConversionPlanner.Plan(ResolveOk(reread), new SourceSchema(5)).TryGetValue(out var roundTripped));
        Assert.Equal(
            direct.FormalAttributes.Select(f => (f.RenderedName, f.Identity)),
            roundTripped.FormalAttributes.Select(f => (f.RenderedName, f.Identity)));
    }

    // --- restrict_to (D-057) -------------------------------------------------

    [Fact]
    public void Migrate_WhenRestrictLineNonEmpty_ThenStringEntriesCarriedVerbatim()
    {
        // v2 restrict is raw-value equality, OR'd within the attribute; tokens
        // carry verbatim (no per-token trim) — including on an excluded attribute,
        // where restrict_to is live filter-only config (§10.1/D-076), not parked.
        var document = MigrateOk(Bed(
            new BedAttr("name", "c", "chara,markos,katia", Restrict: "chara, katia"),
            new BedAttr("gene", "c", "Bmp5,Bmp7", Convert: false, Restrict: "Bmp5")));

        Assert.Equal(
            [new RestrictToValue("chara"), new RestrictToValue(" katia")],
            document.Attributes[0].RestrictTo);
        Assert.Equal([new RestrictToValue("Bmp5")], document.Attributes[1].RestrictTo);
        Assert.False(document.Attributes[1].Include);
    }

    [Fact]
    public void Migrate_WhenRestrictLineBlank_ThenRestrictToNotAuthored()
    {
        var document = MigrateOk(BedFixtures.MushroomBed);

        Assert.All(document.Attributes, a => Assert.Null(a.RestrictTo));
    }

    [Fact]
    public void Migrate_WhenRestrictOnNumericAttribute_ThenCarriedSilentlyAndResolveRejects()
    {
        // Faithful carry: v2 restricted numeric columns by raw-value equality, which
        // vNext ranges cannot express, so the tokens stay string entries and the
        // seam's shape check owns the mismatch (D-063) — the migrator stays silent.
        var migrated = BedMigrator.Migrate(
            ReadBed(Bed(new BedAttr("age", "o", "<,30,50,>", Restrict: "30,40"))), WideBinding());

        Assert.True(migrated.TryGetValue(out var document));
        Assert.Empty(migrated.Diagnostics);
        Assert.Equal(
            [new RestrictToValue("30"), new RestrictToValue("40")],
            document.Attributes[0].RestrictTo);

        var resolved = SpecResolver.Resolve(document);
        Assert.False(resolved.TryGetValue(out _));
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.RestrictToOnNumericRequiresRange);
    }

    // --- missing-token migration (D-068) --------------------------------------

    [Fact]
    public void Migrate_WhenCategoryValueEqualsDefaultMissingToken_ThenAsAttributeAndTokenOutOfDomain()
    {
        var attribute = MigrateOk(Bed(new BedAttr("strength", "c", "weak,?,strong"))).Attributes[0];

        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
        Assert.Equal(["weak", "strong"], attribute.DeclaredDomain);
        Assert.Null(attribute.ValueLabels);
    }

    [Fact]
    public void Migrate_WhenCustomMissingToken_ThenEffectiveTokenDetectedNotHardcodedQuestionMark()
    {
        // D-068: detection uses the effective binding.missing_token — "NA" here, so
        // "NA" leaves the domain and a literal "?" stays an ordinary category value.
        var attribute = MigrateOk(
            Bed(new BedAttr("strength", "c", "weak,NA,?")),
            WideBinding(missingToken: "NA")).Attributes[0];

        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
        Assert.Equal(["weak", "?"], attribute.DeclaredDomain);
    }

    [Fact]
    public void Migrate_WhenMissingTokenAuthoredEmpty_ThenDetectionDisabled()
    {
        // §5.1: missing_token = "" disables token-based missing detection, so no
        // category entry can be "the missing token" — "?" stays a domain value.
        var attribute = MigrateOk(
            Bed(new BedAttr("strength", "c", "weak,?")),
            WideBinding(missingToken: "")).Attributes[0];

        Assert.Null(attribute.MissingPolicy);
        Assert.Equal(["weak", "?"], attribute.DeclaredDomain);
    }

    [Fact]
    public void Migrate_WhenMissingTokenEntryHasDisplayLabel_ThenWarningBedMissingTokenLabelDropped()
    {
        // The missing column is canonically "{column}-missing" (§10.5/D-074); a v2
        // display label on the token has no carrier and drops — audibly.
        var migrated = BedMigrator.Migrate(
            ReadBed(Bed(new BedAttr("strength", "c", "weak,?,strong", Categories: "weak,unknown,strong"))),
            WideBinding());

        Assert.True(migrated.TryGetValue(out var document));
        var warning = Assert.Single(migrated.Diagnostics);
        Assert.Equal(DiagnosticCode.BedMissingTokenLabelDropped, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("unknown", warning.Message);
        Assert.Equal("strength", warning.Location?.AttributeName);
        Assert.Equal(MissingPolicy.AsAttribute, document.Attributes[0].MissingPolicy);
    }

    [Fact]
    public void Migrate_WhenTypeBTrueValueIsMissingToken_ThenErrorBedAttributeConfigInvalid()
    {
        // The token is excluded from the domain by D-068, so a dichotomic true value
        // equal to it is contradictory config no seam check would catch.
        var migrated = BedMigrator.Migrate(ReadBed(Bed(new BedAttr("flag", "b", "?,No"))), WideBinding());

        Assert.False(migrated.TryGetValue(out _));
        var error = Assert.Single(migrated.Diagnostics);
        Assert.Equal(DiagnosticCode.BedAttributeConfigInvalid, error.Code);
    }

    [Fact]
    public void Migrate_WhenTypeBSecondValueIsMissingToken_ThenAsAttributeAndTokenOutOfDomain()
    {
        var attribute = MigrateOk(Bed(new BedAttr("flag", "b", "Yes,?"))).Attributes[0];

        Assert.Equal("Yes", Assert.IsType<DichotomicScaleSection>(attribute.Scale).TrueValue);
        Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
        Assert.Equal(["Yes"], attribute.DeclaredDomain);
    }

    [Fact]
    public void Migrate_WhenWholeDomainIsMissingToken_ThenResolvesButPlanRejectsCalibration()
    {
        // Degenerate but representable: the domain empties out, resolve succeeds,
        // and the existing D-071 plan guard owns the empty-domain rejection.
        var document = MigrateOk(Bed(new BedAttr("strength", "c", "?")));
        Assert.Equal([], document.Attributes[0].DeclaredDomain);

        var planned = ConversionPlanner.Plan(ResolveOk(document), new SourceSchema(1));
        Assert.False(planned.TryGetValue(out _));
        Assert.Contains(planned.Diagnostics, d => d.Code == DiagnosticCode.ObservedDomainCalibrationNotImplementedV1);
    }

    // --- included attributes that cannot transcribe ---------------------------

    [Fact]
    public void Migrate_WhenIncludedTypeD_ThenErrorBedDateTypeNotSupported()
    {
        // Date is a parity deferral (D-038): failing is honest — a spec silently
        // missing an included attribute would change the analysis (the D-068 hazard).
        var migrated = BedMigrator.Migrate(
            ReadBed(Bed(new BedAttr("dob", "d", "<,01/01/1980,01/01/2000,>"))), WideBinding());

        Assert.False(migrated.TryGetValue(out _));
        var error = Assert.Single(migrated.Diagnostics);
        Assert.Equal(DiagnosticCode.BedDateTypeNotSupported, error.Code);
        Assert.Equal("dob", error.Location?.AttributeName);
    }

    [Fact]
    public void Migrate_WhenRealMiniDatesFixture_ThenErrorBedDateTypeNotSupported()
    {
        // The real on-disk fixtures/v2/mini-dates .bed (not a synthetic one, closing
        // the M2-exit review's fixture-coverage caveat): its included `dob` attribute
        // is v2 type `d`, deferred from v1 (D-038/D-079), so migration fails honestly
        // rather than silently dropping an included attribute (the D-068 hazard). The
        // c-typed `name`/`gender` attributes migrate cleanly, so this is the sole error.
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", "mini-dates", "mini-dates_triples.bed");
        Assert.True(File.Exists(path), $"expected the copied fixture at {path}");

        var migrated = BedMigrator.Migrate(ReadBed(File.ReadAllText(path)), WideBinding());

        Assert.False(migrated.TryGetValue(out _));
        var error = Assert.Single(migrated.Diagnostics);
        Assert.Equal(DiagnosticCode.BedDateTypeNotSupported, error.Code);
        Assert.Equal("dob", error.Location?.AttributeName);
    }

    [Fact]
    public void Migrate_WhenIncludedTypeUnknown_ThenErrorBedTypeUnrecognized()
    {
        var migrated = BedMigrator.Migrate(ReadBed(Bed(new BedAttr("weird", "x", "a,b"))), WideBinding());

        Assert.False(migrated.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.BedTypeUnrecognized, Assert.Single(migrated.Diagnostics).Code);
    }

    [Fact]
    public void Migrate_WhenIncludedCutTokenUnparseable_ThenErrorBedAttributeConfigInvalid()
    {
        // The document carrier stores cuts as numbers, so an unparseable token is a
        // transcription failure owned by the migrator (not deferrable to the seam).
        var migrated = BedMigrator.Migrate(ReadBed(Bed(new BedAttr("age", "o", "<,thirty,>"))), WideBinding());

        Assert.False(migrated.TryGetValue(out _));
        var error = Assert.Single(migrated.Diagnostics);
        Assert.Equal(DiagnosticCode.BedAttributeConfigInvalid, error.Code);
        Assert.Contains("'thirty'", error.Message);
    }

    [Fact]
    public void Migrate_WhenIncludedNonAscendingCuts_ThenMigratesAndResolveRejectsWithCutDiagnostic()
    {
        // Behavior change vs the M1 migrator (which failed at migrate time): the
        // config is representable, so it carries and the D-056 factory validation
        // fires at its owned phase — the resolve seam (D-067).
        var migrated = BedMigrator.Migrate(ReadBed(Bed(new BedAttr("age", "o", "<,50,30,>"))), WideBinding());

        Assert.True(migrated.TryGetValue(out var document));
        Assert.Empty(migrated.Diagnostics);

        var resolved = SpecResolver.Resolve(document);
        Assert.False(resolved.TryGetValue(out _));
        Assert.Contains(resolved.Diagnostics, d => d.Code == DiagnosticCode.DiscretizerCutsNotAscending);
    }

    [Fact]
    public void Migrate_WhenSeveralAttributesInvalid_ThenAllErrorsAggregateWithAttributeLocations()
    {
        // P-14: the whole document reports in one pass, not first-failure-wins.
        var migrated = BedMigrator.Migrate(
            ReadBed(Bed(
                new BedAttr("dob", "d", "<,01/01/1980,>"),
                new BedAttr("age", "o", "<,thirty,>"),
                new BedAttr("fine", "c", "a,b"))),
            WideBinding());

        Assert.False(migrated.TryGetValue(out _));
        Assert.Equal(2, migrated.Diagnostics.Count);
        Assert.Contains(migrated.Diagnostics,
            d => d.Code == DiagnosticCode.BedDateTypeNotSupported && d.Location?.AttributeName == "dob");
        Assert.Contains(migrated.Diagnostics,
            d => d.Code == DiagnosticCode.BedAttributeConfigInvalid && d.Location?.AttributeName == "age");
    }

    // --- parked config (D-049) -------------------------------------------------

    [Fact]
    public void Migrate_WhenExcludedAttribute_ThenFullConfigParkedWithIncludeFalse()
    {
        // include = false is an authoring toggle (D-049): the parked attribute keeps
        // its full v2 config so it round-trips and can be switched back on.
        var cls = MigrateOk(BedFixtures.MushroomBed).Attributes[0];

        Assert.False(cls.Include);
        Assert.IsType<IdentityDiscretizerSection>(cls.Discretizer);
        Assert.IsType<NominalScaleSection>(cls.Scale);
        Assert.Equal(["e", "p"], cls.DeclaredDomain);
        Assert.NotNull(cls.ValueLabels);
        Assert.Equal("edible", cls.ValueLabels["e"]);
        Assert.Equal("poisonous", cls.ValueLabels["p"]);
    }

    [Fact]
    public void Migrate_WhenExcludedNonAscendingCuts_ThenConfigParkedAndResolvesClean()
    {
        // Better than the M1 migrator, which degraded this to a bare excluded
        // attribute: the config is representable, so it parks verbatim, and the seam
        // skips discretizer resolution while parked — flipping include = true is
        // what surfaces the D-056 validation.
        var migrated = BedMigrator.Migrate(
            ReadBed(Bed(new BedAttr("age", "o", "<,50,30,>", Convert: false))), WideBinding());

        Assert.True(migrated.TryGetValue(out var document));
        Assert.Empty(migrated.Diagnostics);
        var age = document.Attributes[0];
        Assert.False(age.Include);
        Assert.Equal([50.0, 30.0], Assert.IsType<ManualCutsDiscretizerSection>(age.Discretizer).Cuts);

        ResolveOk(document);
    }

    [Theory]
    [InlineData("d", "<,01/01/1980,>")]
    [InlineData("x", "a,b")]
    [InlineData("o", "<,thirty,>")]
    public void Migrate_WhenExcludedConfigUntranscribable_ThenBareExcludedWithParkedConfigDroppedWarning(
        string type, string values)
    {
        // Dormant config never blocks migration (D-049), but it is never dropped
        // silently either: the degrade reports, and the no-silent-drop floor —
        // name, source, include = false — survives.
        var migrated = BedMigrator.Migrate(
            ReadBed(Bed(new BedAttr("parked", type, values, Convert: false, Restrict: "keep,these"))),
            WideBinding());

        Assert.True(migrated.TryGetValue(out var document));
        var warning = Assert.Single(migrated.Diagnostics);
        Assert.Equal(DiagnosticCode.BedParkedConfigDropped, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("parked", warning.Location?.AttributeName);

        var attribute = document.Attributes[0];
        Assert.Equal("parked", attribute.Name);
        Assert.Equal(0, Assert.IsType<ColumnSourceSection>(attribute.Source).Index);
        Assert.False(attribute.Include);
        Assert.Null(attribute.Discretizer);
        Assert.Null(attribute.Scale);
        Assert.Null(attribute.DeclaredDomain);
        Assert.Null(attribute.ValueLabels);
        // restrict_to is live, include-independent config (§10.1/D-076) — it is not
        // parked emitted-shaping, so it survives the degrade.
        Assert.Equal([new RestrictToValue("keep"), new RestrictToValue("these")], attribute.RestrictTo);
    }

    // --- fingerprint equivalence (Slice E baselines, D-077) ---------------------

    [Fact]
    public void Migrate_WhenMiniMushroomBed_ThenFingerprintsMatchSection19Baseline()
    {
        // The migrated .bed and the §19.1 authored TOML must be the same spec: all
        // three fingerprints equal the baseline pinned at Slice E
        // (SpecFingerprintsTests). Authored-vs-defaulted binding fields, parked
        // config, and value_labels are all fingerprint-inert, so the two producers
        // collapse to one hash. A diff here means migration changed the resolved
        // plan — not a baseline to edit.
        var document = MigrateOk(BedFixtures.MushroomBed);
        var spec = ResolveOk(document);
        Assert.True(ConversionPlanner.Plan(spec, new SourceSchema(5)).TryGetValue(out var plan));

        Assert.Equal(
            new ComputedFingerprints(
                "sha256:6b97a3f3fcd2782781fd2420edde29259848281bfa4e887fc91258e435511f05",
                "sha256:6e1507c6735d0d4abcd6b146930d43b33a624746bc0d995accd2eed287b5752e",
                "sha256:2716ab601e2297bd61ee665b8361045ddc2806679498cf819b3464961715a124"),
            SpecFingerprints.ComputeNative(document, spec, plan));
    }

    [Fact]
    public void Migrate_WhenEmploymentOrdinalProgressive_ThenFingerprintsMatchHandAuthoredTwin()
    {
        // The §19.2 pinned baseline is unreachable by migration (it authors
        // has_header = false and mixes nominal age with ordinal employment, which
        // one ScalingMode cannot express), so the progressive migration is locked
        // against a hand-authored TOML twin instead.
        const string twin = """
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true

            [[attribute]]
            name = "age"
            source = { kind = "column", index = 0 }
            discretizer = { kind = "manual_cuts", cuts = [30, 40, 50], ends = "open" }
            scale = { kind = "ordinal", direction = "le" }

            [[attribute]]
            name = "education"
            source = { kind = "column", index = 1 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["Bachelors", "Masters", "11th", "HS-grad"]

            [[attribute]]
            name = "employment"
            source = { kind = "column", index = 2 }
            discretizer = { kind = "ordered_cuts", order = ["Unskilled", "Clerical", "Professional", "Managerial"], cuts = ["Managerial"], ends = "open" }
            scale = { kind = "ordinal", direction = "le" }

            [[attribute]]
            name = "sex"
            source = { kind = "column", index = 3 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain = ["Male", "Female"]

            [[attribute]]
            name = "US-citizen"
            source = { kind = "column", index = 4 }
            discretizer = { kind = "identity" }
            scale = { kind = "dichotomic", true_value = "Yes" }
            declared_domain = ["Yes", "No"]

            [[attribute]]
            name = "class"
            source = { kind = "column", index = 5 }
            include = false
            """;

        var migratedDoc = MigrateOk(BedFixtures.EmploymentOrdinalBed, mode: ScalingMode.Progressive);
        var migratedSpec = ResolveOk(migratedDoc);
        Assert.True(ConversionPlanner.Plan(migratedSpec, new SourceSchema(6)).TryGetValue(out var migratedPlan));

        Assert.True(SpecReader.Read(twin).TryGetValue(out var twinDoc));
        var twinSpec = ResolveOk(twinDoc);
        Assert.True(ConversionPlanner.Plan(twinSpec, new SourceSchema(6)).TryGetValue(out var twinPlan));

        Assert.Equal(
            SpecFingerprints.ComputeNative(twinDoc, twinSpec, twinPlan),
            SpecFingerprints.ComputeNative(migratedDoc, migratedSpec, migratedPlan));
    }
}
