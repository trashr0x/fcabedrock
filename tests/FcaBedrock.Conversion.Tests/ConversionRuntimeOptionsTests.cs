using System.Reflection;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Export;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// S3 (D-122 part 8 / D-123 point 11): the public byte- and fingerprint-neutral --temp-dir capability.
// ConversionRuntimeOptions carries one runtime setting (TempDirectory) and maps it unchanged to the
// internal GroupingOptions(tempDirectory:); the four additive Calibrator/Emitter overloads change only
// where a spool workspace is created — never calibrated state, emitted objects, diagnostics, ordering,
// output bytes, or fingerprints. The spill proof is composite (consensus S3 ruling): a direct
// public->internal mapping assertion plus low-budget internal forced-spill placement/cleanup and
// byte-equality, so no test allocates past the 64 MiB public default to force a spill.
public sealed class ConversionRuntimeOptionsTests
{
    // ---- options shape and validation -------------------------------------------------------------

    [Fact]
    public void ConversionRuntimeOptions_WhenDefaultConstructed_ThenTempDirectoryIsNull() =>
        Assert.Null(new ConversionRuntimeOptions().TempDirectory);

    [Fact]
    public void ConversionRuntimeOptions_WhenTempDirectorySupplied_ThenItIsRetained() =>
        Assert.Equal(@"X:\some\spool", new ConversionRuntimeOptions { TempDirectory = @"X:\some\spool" }.TempDirectory);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   ")]
    public void ConversionRuntimeOptions_WhenTempDirectoryEmptyOrWhitespace_ThenArgumentException(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ConversionRuntimeOptions { TempDirectory = value });
        Assert.Equal("TempDirectory", ex.ParamName);
    }

    [Fact]
    public void ConversionRuntimeOptions_WhenWithExpressionSetsWhitespace_ThenArgumentException()
    {
        var options = new ConversionRuntimeOptions { TempDirectory = @"X:\ok" };
        Assert.Throws<ArgumentException>(() => options with { TempDirectory = "  " });
    }

    [Fact]
    public void ConversionRuntimeOptions_WhenWithExpressionSetsValidValue_ThenOriginalUnchangedAndCopyUpdated()
    {
        var options = new ConversionRuntimeOptions { TempDirectory = @"X:\ok" };

        // The `with` expression is the compile-time proof that this is a record.
        var copy = options with { TempDirectory = @"Y:\other" };

        Assert.Equal(@"X:\ok", options.TempDirectory);
        Assert.Equal(@"Y:\other", copy.TempDirectory);
    }

    [Fact]
    public void ConversionRuntimeOptions_WhenConstructedWithNonExistentPath_ThenNoDirectoryCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), "fcab-s3-noio-" + Guid.NewGuid().ToString("N"));

        _ = new ConversionRuntimeOptions { TempDirectory = path };

        Assert.False(Directory.Exists(path)); // construction performs no directory I/O
    }

    [Fact]
    public void ConversionRuntimeOptions_ShouldBeSealedAndExposeOnlyTempDirectory()
    {
        Assert.True(typeof(ConversionRuntimeOptions).IsSealed);

        // Locks the public instance property set to exactly TempDirectory — a future budget/fan-in knob
        // added to the public surface would fail here (the record's EqualityContract is protected).
        var publicInstanceProperties = typeof(ConversionRuntimeOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();
        Assert.Equal(["TempDirectory"], publicInstanceProperties);
    }

    // ---- direct public->internal mapping (composite proof, half 1) --------------------------------

    [Fact]
    public void ToGroupingOptions_WhenTempDirectoryNull_ThenMapsNullAndProductionDefaults()
    {
        var mapped = new ConversionRuntimeOptions().ToGroupingOptions();

        Assert.Null(mapped.TempDirectory);
        Assert.Equal(GroupingOptions.DefaultMaxBufferedBytes, mapped.MaxBufferedBytes);
        Assert.Equal(GroupingOptions.DefaultMaxMergeFanIn, mapped.MaxMergeFanIn);
        Assert.Same(SpoolFileSystem.Default, mapped.FileSystem);
        Assert.Null(mapped.Observer);
    }

    [Fact]
    public void ToGroupingOptions_WhenTempDirectorySupplied_ThenMapsPathUnchangedAndProductionDefaults()
    {
        var mapped = new ConversionRuntimeOptions { TempDirectory = @"X:\spool" }.ToGroupingOptions();

        Assert.Equal(@"X:\spool", mapped.TempDirectory); // only TempDirectory crosses over
        Assert.Equal(GroupingOptions.DefaultMaxBufferedBytes, mapped.MaxBufferedBytes);
        Assert.Equal(GroupingOptions.DefaultMaxMergeFanIn, mapped.MaxMergeFanIn);
        Assert.Same(SpoolFileSystem.Default, mapped.FileSystem);
        Assert.Null(mapped.Observer);
    }

    // ---- overload coverage: null rejection is eager on all four -----------------------------------

    [Fact]
    public async Task CalibrateOverloads_WhenRuntimeOptionsNull_ThenThrowArgumentNullException()
    {
        var wideSpec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [ObservedIdentity("g", 0)]);
        var (wideResolved, wideSource) = await WidePrep(wideSpec, "a\nb");
        var wideEx = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Calibrator.CalibrateAsync(wideResolved, wideSource, (ConversionRuntimeOptions)null!));
        Assert.Equal("runtimeOptions", wideEx.ParamName);

        var tripleSpec = new BedrockSpec(ConversionFixtures.Triple(), [ObservedPredicate("g", "g")]);
        var (tripleResolved, tripleSource) = await TriplePrep(tripleSpec, "m0,g,a");
        var tripleEx = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Calibrator.CalibrateTripleAsync(tripleResolved, tripleSource, (ConversionRuntimeOptions)null!));
        Assert.Equal("runtimeOptions", tripleEx.ParamName);
    }

    [Fact]
    public async Task EmitOverloads_WhenRuntimeOptionsNull_ThenThrowArgumentNullExceptionEagerly()
    {
        var widePlan = await PlanWideAsync(DedupeSpec(), DedupeCsv);
        var wideEx = Assert.Throws<ArgumentNullException>(
            () => { _ = Emitter.EmitAsync(widePlan, SourceWide(DedupeSpec(), DedupeCsv), new List<BedrockDiagnostic>(), (ConversionRuntimeOptions)null!); });
        Assert.Equal("runtimeOptions", wideEx.ParamName);

        var triplePlan = await PlanTripleAsync(UnorderedTripleSpec(), ConversionFixtures.MushroomTripleDataInterleaved);
        var tripleEx = Assert.Throws<ArgumentNullException>(
            () => { _ = Emitter.EmitTripleAsync(triplePlan, TripleSource(UnorderedTripleSpec(), ConversionFixtures.MushroomTripleData), new List<BedrockDiagnostic>(), (ConversionRuntimeOptions)null!); });
        Assert.Equal("runtimeOptions", tripleEx.ParamName);
    }

    // ---- overload coverage: the existing wide-eager / triple-iterator guard distinction ------------

    [Fact]
    public async Task EmitAsync_Wide_WhenPlanVariantMismatch_ThenShapeGuardIsEager()
    {
        // The wide overload delegates to the non-iterator internal method, so the plan-variant guard
        // throws synchronously — exactly as today for the wide path.
        var triplePlan = await PlanTripleAsync(UnorderedTripleSpec(), ConversionFixtures.MushroomTripleDataInterleaved);
        Assert.Throws<InvalidOperationException>(
            () => { _ = Emitter.EmitAsync(triplePlan, SourceWide(DedupeSpec(), DedupeCsv), new List<BedrockDiagnostic>(), new ConversionRuntimeOptions()); });
    }

    [Fact]
    public async Task EmitTripleAsync_WhenPlanVariantMismatch_ThenShapeGuardIsDeferredUntilEnumeration()
    {
        // The triple overload delegates to the async-iterator internal method, so the plan-variant guard
        // does NOT run at call time; it surfaces only when enumeration advances — the existing triple
        // behaviour, which the new overload must not change.
        var widePlan = await PlanWideAsync(DedupeSpec(), DedupeCsv);
        var enumerable = Emitter.EmitTripleAsync(
            widePlan, TripleSource(UnorderedTripleSpec(), ConversionFixtures.MushroomTripleData), new List<BedrockDiagnostic>(), new ConversionRuntimeOptions());

        // The call above returned without throwing (deferred); enumeration is where it surfaces.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in enumerable)
            {
            }
        });
    }

    // ---- default/null equivalence: the public seam is byte-/fingerprint-neutral (no spill) ---------

    [Fact]
    public async Task CalibrateAsync_Wide_WhenDefaultOrCustomRoot_ThenSameOutcomeAndNativeFingerprint()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [ObservedIdentity("g", 0)]);
        const string csv = "a\nb\na\nc";
        using var root = new TempRoot();

        var legacy = await CalibrateWideAsync(spec, csv, runtimeOptions: null);
        var defaulted = await CalibrateWideAsync(spec, csv, new ConversionRuntimeOptions());
        var custom = await CalibrateWideAsync(spec, csv, new ConversionRuntimeOptions { TempDirectory = root.Root });

        Assert.Equal(["a", "b", "c"], legacy.Domain);
        AssertSameCalibration(legacy, defaulted);
        AssertSameCalibration(legacy, custom);
        Assert.Empty(Directory.GetDirectories(root.Root)); // no spill under the 64 MiB default → no workspace
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenDefaultOrCustomRoot_ThenSameOutcomeAndNativeFingerprint()
    {
        var spec = new BedrockSpec(ConversionFixtures.Triple(), [ObservedPredicate("g", "g")]);
        const string csv = "m0,g,a\nm1,g,b\nm2,g,a\nm3,g,c";
        using var root = new TempRoot();

        var legacy = await CalibrateTripleAsync(spec, csv, runtimeOptions: null);
        var defaulted = await CalibrateTripleAsync(spec, csv, new ConversionRuntimeOptions());
        var custom = await CalibrateTripleAsync(spec, csv, new ConversionRuntimeOptions { TempDirectory = root.Root });

        Assert.Equal(["a", "b", "c"], legacy.Domain);
        AssertSameCalibration(legacy, defaulted);
        AssertSameCalibration(legacy, custom);
        Assert.Empty(Directory.GetDirectories(root.Root));
    }

    [Fact]
    public async Task EmitAsync_Wide_WhenDefaultOrCustomRoot_ThenNeutralEmissionAndNoWorkspace()
    {
        var spec = DedupeSpec();
        var plan = await PlanWideAsync(spec, DedupeSpillCsv);
        using var root = new TempRoot();

        var legacy = await CaptureAsync(plan, sink => Emitter.EmitAsync(plan, SourceWide(spec, DedupeSpillCsv), sink));
        var defaulted = await CaptureAsync(plan, sink => Emitter.EmitAsync(plan, SourceWide(spec, DedupeSpillCsv), sink, new ConversionRuntimeOptions()));
        var custom = await CaptureAsync(plan, sink => Emitter.EmitAsync(plan, SourceWide(spec, DedupeSpillCsv), sink, new ConversionRuntimeOptions { TempDirectory = root.Root }));

        AssertSameEmission(legacy, defaulted);
        AssertSameEmission(legacy, custom);
        Assert.Empty(Directory.GetDirectories(root.Root, "fcabedrock-spool-*")); // default budget → no spill
    }

    [Fact]
    public async Task EmitTripleAsync_WhenDefaultOrCustomRoot_ThenNeutralEmissionAndNoWorkspace()
    {
        var spec = UnorderedTripleSpec();
        var data = ConversionFixtures.MushroomTripleDataInterleaved;
        var plan = await PlanTripleAsync(spec, data);
        using var root = new TempRoot();

        var legacy = await CaptureAsync(plan, sink => Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink));
        var defaulted = await CaptureAsync(plan, sink => Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink, new ConversionRuntimeOptions()));
        var custom = await CaptureAsync(plan, sink => Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink, new ConversionRuntimeOptions { TempDirectory = root.Root }));

        AssertSameEmission(legacy, defaulted);
        AssertSameEmission(legacy, custom);
        Assert.Empty(Directory.GetDirectories(root.Root, "fcabedrock-spool-*"));
    }

    // ---- forced-spill placement + cleanup + byte-neutrality (composite proof, half 2) -------------

    [Fact]
    public async Task EmitAsync_Wide_WhenSpillForcedUnderCustomRoot_ThenWorkspaceUnderRootCleanedUpAndBytesNeutral()
    {
        var spec = DedupeSpec();
        var plan = await PlanWideAsync(spec, DedupeSpillCsv);
        using var root = new TempRoot();
        var observer = new RecordingObserver();

        // Forced spill (budget 1) is only reachable through the internal GroupingOptions seam; the public
        // overload keeps the 64 MiB production default. Combined with ToGroupingOptions() above, this
        // proves the public TempDirectory places the workspace under the supplied root when a spill runs.
        var defaultRoot = await CaptureAsync(plan, sink =>
            Emitter.EmitAsync(plan, SourceWide(spec, DedupeSpillCsv), sink, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2)));
        var customRoot = await CaptureAsync(plan, sink =>
            Emitter.EmitAsync(plan, SourceWide(spec, DedupeSpillCsv), sink, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, tempDirectory: root.Root, observer: observer)));

        Assert.NotEmpty(observer.Written);                                        // a spill actually happened
        AssertWorkspaceUnderRoot(observer, root.Root);                            // placed under the supplied root
        Assert.Empty(Directory.GetDirectories(root.Root, "fcabedrock-spool-*"));  // cleaned up on success
        AssertSameEmission(defaultRoot, customRoot);                              // byte-/object-neutral under spill
    }

    [Fact]
    public async Task EmitTripleAsync_WhenSpillForcedUnderCustomRoot_ThenWorkspaceUnderRootCleanedUpAndBytesNeutral()
    {
        var spec = UnorderedTripleSpec();
        var data = ConversionFixtures.MushroomTripleDataInterleaved;
        var plan = await PlanTripleAsync(spec, data);
        using var root = new TempRoot();
        var observer = new RecordingObserver();

        var defaultRoot = await CaptureAsync(plan, sink =>
            Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2)));
        var customRoot = await CaptureAsync(plan, sink =>
            Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink, new GroupingOptions(maxBufferedBytes: 1, maxMergeFanIn: 2, tempDirectory: root.Root, observer: observer)));

        Assert.NotEmpty(observer.Written);
        AssertWorkspaceUnderRoot(observer, root.Root);
        Assert.Empty(Directory.GetDirectories(root.Root, "fcabedrock-spool-*"));
        AssertSameEmission(defaultRoot, customRoot);
    }

    [Fact]
    public async Task EmitTripleAsync_WhenSpillForcedTwiceUnderSameRoot_ThenByteIdentical()
    {
        var spec = UnorderedTripleSpec();
        var data = ConversionFixtures.MushroomTripleDataInterleaved;
        var plan = await PlanTripleAsync(spec, data);
        using var root = new TempRoot();

        GroupingOptions Spill() => new(maxBufferedBytes: 1, maxMergeFanIn: 2, tempDirectory: root.Root);
        var first = await CaptureAsync(plan, sink => Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink, Spill()));
        var second = await CaptureAsync(plan, sink => Emitter.EmitTripleAsync(plan, TripleSource(spec, data), sink, Spill()));

        AssertSameEmission(first, second); // determinism under a forced spill
    }

    // ---- fixtures / helpers -----------------------------------------------------------------------

    private const string DedupeCsv = "k1,x\nk2,y\nk1,y";
    private const string DedupeSpillCsv = "k1,x\nk2,y\nk3,x\nk1,y\nk2,x\nk3,y";

    // A wide dedupe spec (object key = column 0, dedupe; one nominal attribute over column 1). Built
    // explicitly with WideWithKey — ConversionFixtures.MushroomSpec() is row-index wide, not dedupe.
    private static BedrockSpec DedupeSpec() =>
        new(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Dedupe), [ConversionFixtures.Nominal("a", 1, "x", "y")]);

    // The mushroom triple predicates under an unordered binding — the grouping/spool emit path.
    private static BedrockSpec UnorderedTripleSpec() =>
        new(ConversionFixtures.Triple(TripleOrdering.Unordered),
        [
            ConversionFixtures.PredicateExcluded("class", "class"),
            ConversionFixtures.PredicateDichotomic("bruises?", "bruises?", "t", ["t", "f"]),
            ConversionFixtures.PredicateNominal("gill-size", "gill-size", ["b", "n"]),
            ConversionFixtures.PredicateNominal("veil-type", "veil-type", ["p", "u"]),
            ConversionFixtures.PredicateNominal("ring-number", "ring-number", ["n", "o", "t"]),
        ]);

    // A wide identity attribute with an omitted (null) declared domain → observed-domain calibration.
    private static AttributeSpec ObservedIdentity(string name, int index) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(),
            new NominalScale(), DeclaredDomain: null, RestrictTo: [], ConversionFixtures.NoLabels,
            MissingPolicy.Skip, UnknownValuePolicy.Warn);

    // A triple predicate attribute with an omitted (null) declared domain → observed-domain calibration.
    private static AttributeSpec ObservedPredicate(string name, string predicate) =>
        ConversionFixtures.PredicateNominal(name, predicate, domain: null);

    private static WideCsvSource SourceWide(BedrockSpec spec, string csv) => ConversionFixtures.SourceOver(csv, spec.Binding);

    private static TripleCsvSource TripleSource(BedrockSpec spec, string csv) => ConversionFixtures.TripleSourceOver(csv, spec.Binding);

    private static async Task<(ResolvedSpec Resolved, WideCsvSource Source)> WidePrep(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return (ConversionFixtures.ResolveFor(spec, schema), source);
    }

    private static async Task<(ResolvedSpec Resolved, TripleCsvSource Source)> TriplePrep(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.TripleSourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return (ConversionFixtures.ResolveFor(spec, schema), source);
    }

    private static async Task<ConversionPlan> PlanWideAsync(BedrockSpec spec, string csv)
    {
        var schema = await ConversionFixtures.SourceOver(csv, spec.Binding).GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return plan;
    }

    private static async Task<ConversionPlan> PlanTripleAsync(BedrockSpec spec, string csv)
    {
        var schema = await ConversionFixtures.TripleSourceOver(csv, spec.Binding).GetSchemaAsync();
        Assert.True(ConversionFixtures.PlanFor(spec, schema).TryGetValue(out var plan));
        return plan;
    }

    private static ConversionPlan PlanFrom(CalibratedSpec calibrated)
    {
        Assert.True(ConversionPlanner.Plan(calibrated, LabelStyle.Native).TryGetValue(out var plan));
        return plan;
    }

    private static async Task<CalibrationOutcome> CalibrateWideAsync(BedrockSpec spec, string csv, ConversionRuntimeOptions? runtimeOptions)
    {
        var (resolved, source) = await WidePrep(spec, csv);
        var result = runtimeOptions is null
            ? await Calibrator.CalibrateAsync(resolved, source)
            : await Calibrator.CalibrateAsync(resolved, source, runtimeOptions);
        return Outcome(result);
    }

    private static async Task<CalibrationOutcome> CalibrateTripleAsync(BedrockSpec spec, string csv, ConversionRuntimeOptions? runtimeOptions)
    {
        var (resolved, source) = await TriplePrep(spec, csv);
        var result = runtimeOptions is null
            ? await Calibrator.CalibrateTripleAsync(resolved, source)
            : await Calibrator.CalibrateTripleAsync(resolved, source, runtimeOptions);
        return Outcome(result);
    }

    private static CalibrationOutcome Outcome(Diagnosed<CalibratedSpec> result)
    {
        Assert.True(result.TryGetValue(out var calibrated));
        return new CalibrationOutcome(
            calibrated.Spec.Attributes[0].DeclaredDomain,
            Codes(result.Diagnostics),
            FingerprintCalculator.ComputeSchemaFingerprint(PlanFrom(calibrated)));
    }

    // Drives one conversion three ways: a single-pass drain (objects + data diagnostics), the two-pass
    // .cxt write bracketed by EmitReplay (so the passes do not double-collect diagnostics), and the
    // single-pass .dat write. The emit factory builds a fresh source per pass, so replay is clean.
    private static async Task<Emission> CaptureAsync(
        ConversionPlan plan,
        Func<ICollection<BedrockDiagnostic>, IAsyncEnumerable<EmittedObject>> emit)
    {
        var dataDiagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var obj in emit(dataDiagnostics))
        {
            objects.Add(obj);
        }

        using var cxtStream = new MemoryStream();
        using (var session = EmitReplay.Begin(emit, new List<BedrockDiagnostic>()))
        {
            await CxtWriter.WriteAsync(plan, session.Open, WriterOptions.Native, cxtStream);
        }

        using var datStream = new MemoryStream();
        await DatWriter.WriteAsync(emit(new List<BedrockDiagnostic>()), WriterOptions.Native, datStream);

        return new Emission(objects, Codes(dataDiagnostics), cxtStream.ToArray(), datStream.ToArray());
    }

    private static IReadOnlyList<(DiagnosticCode Code, DiagnosticSeverity Severity, string Message)> Codes(
        IEnumerable<BedrockDiagnostic> diagnostics) =>
        diagnostics.Select(d => (d.Code, d.Severity, d.Message)).ToList();

    private static void AssertSameCalibration(CalibrationOutcome expected, CalibrationOutcome actual)
    {
        Assert.Equal(expected.Domain, actual.Domain);
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.SchemaFingerprint, actual.SchemaFingerprint);
    }

    private static void AssertSameEmission(Emission expected, Emission actual)
    {
        Assert.Equal(expected.Objects.Select(o => o.Name), actual.Objects.Select(o => o.Name));
        Assert.Equal(expected.Objects.Select(o => o.CrossedFormalAttributeIds), actual.Objects.Select(o => o.CrossedFormalAttributeIds));
        Assert.Equal(expected.Diagnostics, actual.Diagnostics);
        Assert.Equal(expected.Cxt, actual.Cxt);
        Assert.Equal(expected.Dat, actual.Dat);
    }

    // Every recorded spool run lives in a fcabedrock-spool-* workspace whose parent is exactly the
    // supplied root — a proper parent/full-path comparison, never a string-prefix match.
    private static void AssertWorkspaceUnderRoot(RecordingObserver observer, string root)
    {
        var expectedParent = Path.GetFullPath(root);
        foreach (var (path, _, _) in observer.Written)
        {
            var workspace = Path.GetDirectoryName(path)!;
            Assert.StartsWith("fcabedrock-spool-", Path.GetFileName(workspace), StringComparison.Ordinal);
            Assert.Equal(expectedParent, Path.GetFullPath(Path.GetDirectoryName(workspace)!));
        }
    }

    private sealed record CalibrationOutcome(
        IReadOnlyList<string>? Domain,
        IReadOnlyList<(DiagnosticCode Code, DiagnosticSeverity Severity, string Message)> Diagnostics,
        string SchemaFingerprint);

    private sealed record Emission(
        IReadOnlyList<EmittedObject> Objects,
        IReadOnlyList<(DiagnosticCode Code, DiagnosticSeverity Severity, string Message)> Diagnostics,
        byte[] Cxt,
        byte[] Dat);

    // A unique temp root created up front and recursively removed on dispose; distinct from the OS temp
    // path so a "no workspace created" assertion is meaningful.
    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "fcab-s3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
