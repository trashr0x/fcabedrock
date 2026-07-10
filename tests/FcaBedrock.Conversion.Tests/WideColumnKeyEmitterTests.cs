using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Conversion.Tests;

// Wide object_key.mode = "column" execution (§5.4/§6.1, D-083): fail + keep, driven end-to-end
// through WideCsvSource so key cleaning, the plan-time index range-check, and ragged tolerance are
// exercised the way conversion sees them. Object order is source-row order (§17 rule 4).
public sealed class WideColumnKeyEmitterTests
{
    // --- fail ---------------------------------------------------------------

    [Fact]
    public async Task EmitAsync_WhenFailUniqueKeys_ThenObjectsNamedByCleanedKey()
    {
        var spec = KeyedSpec(DuplicateObjectPolicy.Fail);

        var (objects, diagnostics) = await RunAsync(spec, "k1,x\nk2,y\nk3,x", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail));

        Assert.Empty(diagnostics);
        Assert.Equal(["k1", "k2", "k3"], objects.Select(o => o.Name));
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds); // a-x
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds); // a-y
    }

    [Fact]
    public async Task EmitAsync_WhenFailDuplicateKey_ThenDuplicateObjectKeyErrorAndStops()
    {
        var spec = KeyedSpec(DuplicateObjectPolicy.Fail);

        var (objects, diagnostics) = await RunAsync(spec, "k1,x\nk1,y", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail));

        Assert.Equal(["k1"], objects.Select(o => o.Name)); // the first object was emitted before the halt
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DuplicateObjectKey, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Location?.RecordIndex); // the duplicate is the second record
    }

    // --- keep ---------------------------------------------------------------

    [Fact]
    public async Task EmitAsync_WhenKeepUniqueKeys_ThenObjectsNamedByCleanedKey()
    {
        var spec = KeyedSpec(DuplicateObjectPolicy.Keep);

        var (objects, diagnostics) = await RunAsync(spec, "k1,x\nk2,y", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));

        Assert.Empty(diagnostics);
        Assert.Equal(["k1", "k2"], objects.Select(o => o.Name));
    }

    [Fact]
    public async Task EmitAsync_WhenKeepDuplicateKey_ThenLaterRowSuffixedAndAggregatedWarning()
    {
        var spec = KeyedSpec(DuplicateObjectPolicy.Keep);

        var (objects, diagnostics) = await RunAsync(spec, "P001,x\nP002,y\nP001,x", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));

        // Source-row order; the later P001 becomes P001#<record-index> (0-based) = P001#2.
        Assert.Equal(["P001", "P002", "P001#2"], objects.Select(o => o.Name));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.DuplicateObjectKey, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("1 object", diagnostic.Message, StringComparison.Ordinal); // aggregated count
        Assert.Contains("P001", diagnostic.Message, StringComparison.Ordinal);      // bounded sample
    }

    [Fact]
    public async Task EmitAsync_WhenKeepLaterCandidateCollidesWithLiteralKey_ThenEscalatesAndReportsBothCodes()
    {
        // [P001#2, P001, P001]: the third row is a duplicate of P001, so its candidate name P001#2
        // (record index 2) collides with the literal first row and escalates to P001#2#1.
        var spec = KeyedSpec(DuplicateObjectPolicy.Keep);

        var (objects, diagnostics) = await RunAsync(spec, "P001#2,x\nP001,y\nP001,x", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));

        Assert.Equal(["P001#2", "P001", "P001#2#1"], objects.Select(o => o.Name));
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey).Severity);
        var disambiguated = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectKeyNameDisambiguated);
        Assert.Contains("P001→P001#2#1", disambiguated.Message, StringComparison.Ordinal); // cleaned key → assigned name
    }

    [Fact]
    public async Task EmitAsync_WhenKeepFirstOccurrenceCollidesWithGeneratedName_ThenDisambiguatedNotCountedDuplicate()
    {
        // [P001, P001, P001#1]: the third row is a DISTINCT first-occurrence key that happens to equal
        // a generated name (P001#1 from the second row), so it is disambiguated (→ P001#1#1) but is NOT
        // a duplicate cleaned key — one DuplicateObjectKey, one ObjectKeyNameDisambiguated.
        var spec = KeyedSpec(DuplicateObjectPolicy.Keep);

        var (objects, diagnostics) = await RunAsync(spec, "P001,x\nP001,y\nP001#1,x", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));

        Assert.Equal(["P001", "P001#1", "P001#1#1"], objects.Select(o => o.Name));
        Assert.Contains("1 object", Assert.Single(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey).Message, StringComparison.Ordinal);
        Assert.Contains("1 object", Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ObjectKeyNameDisambiguated).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmitAsync_WhenKeepStructuralHalt_ThenPendingWarningsSuppressed()
    {
        // A later invalid key (missing_token) halts; the pending keep duplicate warning from the
        // partial stream is suppressed (the aggregated flush runs only on normal completion).
        var spec = KeyedSpec(DuplicateObjectPolicy.Keep);

        var (_, diagnostics) = await RunAsync(spec, "P001,x\nP001,y\n?,z", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.DuplicateObjectKey);
    }

    // --- invalid / absent keys (§5.4/§16.4, D-085) --------------------------

    [Theory]
    [InlineData(",x")]          // empty key cell
    [InlineData("?,x")]         // key cell is the missing token
    [InlineData("\" \",x")]     // quoted whitespace-only key
    [InlineData("\"a\nb\",x")]  // key bears a newline (would corrupt the .cxt)
    public async Task EmitAsync_WhenKeyValueUnusable_ThenObjectKeyValueInvalid(string csv)
    {
        var spec = KeyedSpec(DuplicateObjectPolicy.Fail);

        var (_, diagnostics) = await RunAsync(spec, csv, ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(0, diagnostic.Location?.RecordIndex);
    }

    [Fact]
    public async Task EmitAsync_WhenKeyCellAbsentFromRaggedRow_ThenObjectKeyValueInvalid()
    {
        // The key column (index 1) is in range for the schema (first row is 2-wide) but absent from a
        // later short row — an absent mapped cell is ObjectKeyValueInvalid at emit, not a plan error.
        var spec = new BedrockSpec(ConversionFixtures.WideWithKey(1, DuplicateObjectPolicy.Fail),
            [ConversionFixtures.Nominal("a", 0, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "x,k1\ny", ConversionFixtures.WideWithKey(1, DuplicateObjectPolicy.Fail));

        Assert.Equal(["k1"], objects.Select(o => o.Name)); // the full first row emitted
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.ObjectKeyValueInvalid, diagnostic.Code);
        Assert.Equal(1, diagnostic.Location?.RecordIndex);
    }

    [Fact]
    public async Task EmitAsync_WhenAttributeCellAbsentFromRaggedRow_ThenTreatedAsMissing()
    {
        // An absent ORDINARY attribute cell behaves as missing (Q1): under as_attribute it crosses
        // -missing, and the key is still valid so no ObjectKeyValueInvalid. The first (full) row sets
        // the schema width so the attribute index is in range at plan; the second row is short.
        var spec = new BedrockSpec(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail),
            [ConversionFixtures.Nominal("a", 1, UnknownValuePolicy.Warn, MissingPolicy.AsAttribute, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "k1,x\nk2", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail));

        Assert.Empty(diagnostics);
        Assert.Equal(["k1", "k2"], objects.Select(o => o.Name));
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds); // a-x (present)
        Assert.Equal([2], objects[1].CrossedFormalAttributeIds); // a-missing (absent cell → missing)
    }

    // --- key column doubling as an attribute (D-033) ------------------------

    [Fact]
    public async Task EmitAsync_WhenKeyColumnAlsoBoundAsAttribute_ThenBothNamesAndCrosses()
    {
        // D-033: the key column may also be an [[attribute]] source — the same field names the object
        // and is analysed as an attribute.
        var spec = new BedrockSpec(ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail),
            [ConversionFixtures.Nominal("a", 0, "x", "y")]);

        var (objects, diagnostics) = await RunAsync(spec, "x\ny", ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Fail));

        Assert.Empty(diagnostics);
        Assert.Equal(["x", "y"], objects.Select(o => o.Name));   // key = the col-0 value
        Assert.Equal([0], objects[0].CrossedFormalAttributeIds); // a-x
        Assert.Equal([1], objects[1].CrossedFormalAttributeIds); // a-y
    }

    // --- determinism --------------------------------------------------------

    [Fact]
    public async Task EmitAsync_WhenKeepRunTwice_ThenIdenticalNamesAndCrosses()
    {
        var spec = KeyedSpec(DuplicateObjectPolicy.Keep);
        const string csv = "P001,x\nP002,y\nP001,x\nP001,y";

        var (first, _) = await RunAsync(spec, csv, ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));
        var (second, _) = await RunAsync(spec, csv, ConversionFixtures.WideWithKey(0, DuplicateObjectPolicy.Keep));

        Assert.Equal(first.Select(o => o.Name), second.Select(o => o.Name));
        Assert.Equal(first.Select(o => o.CrossedFormalAttributeIds), second.Select(o => o.CrossedFormalAttributeIds));
    }

    // Key on column 0, a nominal attribute "a" on column 1 (domain x, y → a-x id 0, a-y id 1).
    private static BedrockSpec KeyedSpec(DuplicateObjectPolicy policy) =>
        new(ConversionFixtures.WideWithKey(0, policy), [ConversionFixtures.Nominal("a", 1, "x", "y")]);

    private static async Task<(List<EmittedObject> Objects, List<BedrockDiagnostic> Diagnostics)> RunAsync(
        BedrockSpec spec, string csv, Binding binding)
    {
        var source = ConversionFixtures.SourceOver(csv, binding);
        var schema = await source.GetSchemaAsync();
        Assert.True(ConversionPlanner.Plan(spec, schema).TryGetValue(out var plan));

        var diagnostics = new List<BedrockDiagnostic>();
        var objects = new List<EmittedObject>();
        await foreach (var emitted in Emitter.EmitAsync(plan, source, diagnostics))
        {
            objects.Add(emitted);
        }

        return (objects, diagnostics);
    }
}
