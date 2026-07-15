using System.Text;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;

namespace FcaBedrock.Conversion.Tests;

// The preparation ↔ source pairing guard state matrix (D-098): token / descriptor / unvalidated,
// at both calibrate and emit, plus the guard running before any row is read.
public sealed class ProvenancePairingTests
{
    private static AttributeSpec Identity(string name, int index) =>
        new(name, new ColumnSource(index, SourceValueType.String), Include: true, new IdentityDiscretizer(),
            new NominalScale(), ["a", "b"], RestrictTo: [], ConversionFixtures.NoLabels, MissingPolicy.Skip, UnknownValuePolicy.Warn);

    private static Stream Open(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static ConversionPlan Plan(ResolvedSpec resolved) =>
        ConversionPlanner.Plan(CalibratedSpec.FromFullyDeclared(resolved)).Value!;

    private static async Task EmitAll(ConversionPlan plan, IRecordSource source)
    {
        await foreach (var _ in Emitter.EmitAsync(plan, source, new List<BedrockDiagnostic>()))
        {
        }
    }

    [Fact]
    public async Task Calibrate_WhenBoundSourceTokenIsADifferentResolution_ThenThrows()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0)]);
        var settings = ConversionFixtures.ResolveFor(spec, new SourceSchema(1)).Settings;
        var session = new WideCsvSession(() => Open("a\nb"), settings);
        var schema = await session.GetSchemaAsync();
        var boundToA = session.Bind(ConversionFixtures.ResolveFor(spec, schema));
        var resolvedB = ConversionFixtures.ResolveFor(spec, schema); // a distinct token instance

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Calibrator.CalibrateAsync(resolvedB, boundToA).AsTask());
    }

    [Fact]
    public async Task Calibrate_WhenDescriptorTripleRolesDiffer_ThenThrows()
    {
        // Same scalars, different resolved role map — caught by the role-map equality check.
        var sourceBinding = ConversionFixtures.Triple(TripleOrdering.Unordered); // roles (0,1,2)
        var source = ConversionFixtures.TripleSourceOver("s,p,a", sourceBinding);
        var resolvedBinding = new Binding(SourceShape.Triple, "utf-8", ',', '"', HasHeader: false, "invariant", "?",
            new ColumnObjectKey(2, DuplicateObjectPolicy.Fail), new TripleColumns(2, 1, 0), TripleOrdering.Unordered);
        var spec = new BedrockSpec(resolvedBinding,
            [new AttributeSpec("g", new PredicateSource("p", SourceValueType.String), Include: true,
                new IdentityDiscretizer(), new NominalScale(), ["a"], RestrictTo: [], ConversionFixtures.NoLabels,
                MissingPolicy.Skip, UnknownValuePolicy.Warn)]);
        var resolved = ConversionFixtures.ResolveFor(spec, new SourceSchema(3));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Calibrator.CalibrateTripleAsync(resolved, source).AsTask());
    }

    [Fact]
    public async Task Calibrate_WhenUnvalidatedSchemaDiffers_ThenThrowsBeforeReadingRows()
    {
        // An unvalidated source is checked by schema value only; a mismatch throws before ReadAsync
        // (the fake's rows throw, proving pre-enumeration).
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0)]);
        var resolved = ConversionFixtures.ResolveFor(spec, new SourceSchema(1));
        var source = new RowsThrowingSource(new SourceSchema(3)); // wrong column count

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Calibrator.CalibrateAsync(resolved, source).AsTask());
    }

    [Fact]
    public async Task Emit_WhenBoundSourceTokenIsADifferentResolution_ThenThrows()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0)]);
        var settings = ConversionFixtures.ResolveFor(spec, new SourceSchema(1)).Settings;
        var session = new WideCsvSession(() => Open("a\nb"), settings);
        var schema = await session.GetSchemaAsync();
        var boundToA = session.Bind(ConversionFixtures.ResolveFor(spec, schema));
        var planForB = Plan(ConversionFixtures.ResolveFor(spec, schema)); // a distinct token

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmitAll(planForB, boundToA));
    }

    [Fact]
    public async Task Emit_WhenUnvalidatedSchemaDiffers_ThenThrows()
    {
        var spec = new BedrockSpec(ConversionFixtures.Wide(hasHeader: false), [Identity("g", 0)]);
        var plan = Plan(ConversionFixtures.ResolveFor(spec, new SourceSchema(1)));
        var source = new RowsThrowingSource(new SourceSchema(3));

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmitAll(plan, source));
    }

    // A source whose schema is known but whose rows throw — proves a mismatch is caught before ReadAsync.
    private sealed class RowsThrowingSource(SourceSchema schema) : IRecordSource
    {
        public SourceProvenance Provenance => SourceProvenance.Unvalidated;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(schema);

        public IAsyncEnumerable<ObjectRecord> ReadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the guard must reject before reading rows");
    }
}
