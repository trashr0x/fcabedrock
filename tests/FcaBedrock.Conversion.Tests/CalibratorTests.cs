using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

public sealed class CalibratorTests
{
    private static AttributeSpec Identity(
        string name, int index, IReadOnlyList<string> domain, UnknownValuePolicy policy = UnknownValuePolicy.Warn) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(),
            new NominalScale(), domain, RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, policy);

    private static async Task<(ResolvedSpec Resolved, WideCsvSource Source)> WidePrep(BedrockSpec spec, string csv)
    {
        var source = ConversionFixtures.SourceOver(csv, spec.Binding);
        var schema = await source.GetSchemaAsync();
        return (ConversionFixtures.ResolveFor(spec, schema), source);
    }

    // --- observed-domain calibration (wide) ---

    [Fact]
    public async Task CalibrateAsync_WhenAbsentDomain_ThenObservedInFirstAppearanceOrderAndWarns()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0, [])]);
        var (resolved, source) = await WidePrep(spec, "a\nb\na\nc");

        var result = await Calibrator.CalibrateAsync(resolved, source);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal(["a", "b", "c"], calibrated.Spec.Attributes[0].DeclaredDomain);
        var observed = Assert.IsType<ObservedDomain>(Assert.Single(calibrated.Calibrations));
        Assert.Equal(["a", "b", "c"], observed.Values);
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ObservedDomainUsed, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task CalibrateAsync_WhenAbsentDomainAndAllMissing_ThenEmptyObservedDomainStillWarns()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0, [])]);
        var (resolved, source) = await WidePrep(spec, "?\n?");

        var result = await Calibrator.CalibrateAsync(resolved, source);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Empty(calibrated.Spec.Attributes[0].DeclaredDomain);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ObservedDomainUsed);
    }

    // --- include-additions calibration (wide) ---

    [Fact]
    public async Task CalibrateAsync_WhenIncludePolicy_ThenAppendsNovelValuesAndWarns()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Identity("g", 0, ["a"], UnknownValuePolicy.Include)]);
        var (resolved, source) = await WidePrep(spec, "a\nb\nc\na\nb");

        var result = await Calibrator.CalibrateAsync(resolved, source);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal(["a", "b", "c"], calibrated.Spec.Attributes[0].DeclaredDomain);
        Assert.Equal(["b", "c"], Assert.IsType<IncludeAdditions>(Assert.Single(calibrated.Calibrations)).Values);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownValuePolicyInclude);
    }

    [Fact]
    public async Task CalibrateAsync_WhenIncludeButNoNovelValues_ThenZeroAdditionsStillWarns()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Identity("g", 0, ["a", "b"], UnknownValuePolicy.Include)]);
        var (resolved, source) = await WidePrep(spec, "a\nb\na");

        var result = await Calibrator.CalibrateAsync(resolved, source);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Empty(Assert.IsType<IncludeAdditions>(Assert.Single(calibrated.Calibrations)).Values);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownValuePolicyInclude);
    }

    // --- no-data fast path ---

    [Fact]
    public async Task CalibrateAsync_WhenFullyDeclared_ThenFastPathReadsSchemaOnly()
    {
        // A rows-throwing fake proves the fast path never enumerates rows for a fully-declared spec.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0, ["a", "b"])]);
        var resolved = ConversionFixtures.ResolveFor(spec, new SourceSchema(1));
        var source = new RowsThrowingSource(new SourceSchema(1));

        var result = await Calibrator.CalibrateAsync(resolved, source);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Empty(calibrated.Calibrations);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task CalibrateAsync_WhenMultipleAbsentDomains_ThenWarningsInSpecAttributeOrder()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false),
            [Identity("first", 0, []), Identity("second", 1, [])]);
        var (resolved, source) = await WidePrep(spec, "a,x\nb,y");

        var result = await Calibrator.CalibrateAsync(resolved, source);

        Assert.True(result.TryGetValue(out _));
        Assert.Collection(
            result.Diagnostics.Where(d => d.Code == DiagnosticCode.ObservedDomainUsed),
            d => Assert.Equal("first", d.Location?.AttributeName),
            d => Assert.Equal("second", d.Location?.AttributeName));
    }

    [Fact]
    public async Task CalibrateAsync_WhenCancelled_ThenPropagatesOperationCanceled()
    {
        // Cancellation propagates; it is never converted to a diagnostic (P-14).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0, [])]);
        var (resolved, source) = await WidePrep(spec, "a\nb");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Calibrator.CalibrateAsync(resolved, source, cts.Token).AsTask());
    }

    // --- provenance pairing guard (before any row) ---

    [Fact]
    public async Task CalibrateAsync_WhenSourceSettingsDiffer_ThenThrows()
    {
        // Same header/arity but a different missing_token — the descriptor settings mismatch is caught
        // before any row is read (D-098).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0, [])]);
        var resolved = ConversionFixtures.ResolveFor(spec, new SourceSchema(1));
        var mismatched = ConversionFixtures.SourceOver("a\nb", ConversionFixtures.Wide(hasHeader: false, missingToken: "NA"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Calibrator.CalibrateAsync(resolved, mismatched).AsTask());
    }

    [Fact]
    public async Task CalibrateAsync_WhenBoundSourceMatchesResolution_ThenTokenPairingSucceeds()
    {
        // A session-bound source carries the resolution token; the guard pairs by reference identity.
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0, [])]);
        var settings = SpecReadSettingsFor(spec.Binding);
        var session = new WideCsvSession(() => Stream("a\nb"), settings);
        var schema = await session.GetSchemaAsync();
        var resolved = ConversionFixtures.ResolveFor(spec, schema);
        var bound = session.Bind(resolved);

        var result = await Calibrator.CalibrateAsync(resolved, bound);

        Assert.True(result.TryGetValue(out var calibrated));
        Assert.Equal(["a", "b"], calibrated.Spec.Attributes[0].DeclaredDomain);
    }

    // --- triple calibration (G-3 structural checks) ---

    [Fact]
    public async Task CalibrateTripleAsync_WhenAbsentDomain_ThenObservesRawOrderForBothOrderings()
    {
        foreach (var ordering in new[] { TripleOrdering.SubjectGrouped, TripleOrdering.Unordered })
        {
            var binding = ConversionFixtures.Triple(ordering);
            var spec = new BedrockSpec(binding,
                [new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
                    new IdentityDiscretizer(), new NominalScale(), [], RestrictTo: [], ConversionFixtures.NoLabels,
                    MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
            // subject_grouped needs contiguous subjects; both observe raw value order a, b, c.
            var data = ordering == TripleOrdering.SubjectGrouped
                ? "s0,p,a\ns0,p,b\ns1,p,c"
                : "s0,p,a\ns1,p,b\ns0,p,c";
            var source = ConversionFixtures.TripleSourceOver(data, binding);
            var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());

            var result = await Calibrator.CalibrateTripleAsync(resolved, source);

            Assert.True(result.TryGetValue(out var calibrated));
            Assert.Equal(["a", "b", "c"], calibrated.Spec.Attributes[0].DeclaredDomain);
        }
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenSubjectGroupedNonContiguous_ThenHaltsWithTripleSubjectNotContiguous()
    {
        var binding = ConversionFixtures.Triple(TripleOrdering.SubjectGrouped);
        var spec = new BedrockSpec(binding,
            [new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        var source = ConversionFixtures.TripleSourceOver("s0,p,a\ns1,p,b\ns0,p,c", binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());

        var result = await Calibrator.CalibrateTripleAsync(resolved, source);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.TripleSubjectNotContiguous);
    }

    [Fact]
    public async Task CalibrateTripleAsync_WhenSubjectUnusable_ThenHaltsWithObjectKeyValueInvalid()
    {
        var binding = ConversionFixtures.Triple(TripleOrdering.Unordered);
        var spec = new BedrockSpec(binding,
            [new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), [], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        // The second row's subject is the missing token → unusable.
        var source = ConversionFixtures.TripleSourceOver("s0,p,a\n?,p,b", binding);
        var resolved = ConversionFixtures.ResolveFor(spec, await source.GetSchemaAsync());

        var result = await Calibrator.CalibrateTripleAsync(resolved, source);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ObjectKeyValueInvalid);
    }

    private static SourceReadSettings SpecReadSettingsFor(Binding binding) =>
        SourceReadSettings.Create(
            binding.Shape, binding.Encoding, binding.Delimiter, binding.QuoteChar,
            binding.HasHeader, binding.MissingToken, binding.Ordering);

    private static Stream Stream(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    // A source whose schema is known but whose rows throw — proves the no-data fast path.
    private sealed class RowsThrowingSource(SourceSchema schema) : IRecordSource
    {
        public SourceProvenance Provenance => SourceProvenance.Unvalidated;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the fast path must not read rows");
    }
}
