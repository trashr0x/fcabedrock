using System.Globalization;
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

    private sealed record UnknownObjectKey : ObjectKey;
}
