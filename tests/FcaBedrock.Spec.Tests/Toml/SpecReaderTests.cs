using FcaBedrock.Core.Scaling;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// Reader fidelity tests (D-075): presence tracking per section (authored vs
/// omitted, including authored-equals-default), enum spellings, the D-071
/// omitted-vs-<c>[]</c> distinction, D-057 restrict_to forms, and the D-072
/// triple carrier.
/// </summary>
public sealed class SpecReaderTests
{
    [Fact]
    public void Read_WhenMinimalSpecOnly_ThenEverythingElseIsUnauthored()
    {
        var document = ReadOk("[spec]\nversion = 1\n");

        Assert.NotNull(document.Spec);
        Assert.Equal(1, document.Spec!.Version);
        Assert.Null(document.Spec.Extends);
        Assert.Null(document.Spec.Description);
        Assert.Null(document.Provenance);
        Assert.Null(document.Binding);
        Assert.Null(document.Defaults);
        Assert.Null(document.Output);
        Assert.Empty(document.Templates);
        Assert.Empty(document.Matchers);
        Assert.Empty(document.Attributes);
    }

    [Fact]
    public void Read_WhenMiniMushroom_ThenDocumentMirrorsTheAuthoredSpec()
    {
        var document = ReadOk(TomlFixtures.MiniMushroom);

        Assert.Equal("v2 mini-mushroom spec, modernized", document.Spec?.Description);
        Assert.Equal(SourceShape.Wide, document.Binding?.Shape);
        Assert.True(document.Binding?.HasHeader);
        Assert.Equal("?", document.Binding?.MissingToken);
        Assert.Equal(ObjectKeyMode.RowIndex, document.Binding?.ObjectKey?.Mode);
        Assert.Equal(5, document.Attributes.Count);

        var parked = document.Attributes[0];
        Assert.Equal("class", parked.Name);
        Assert.False(parked.Include);
        Assert.Null(parked.Discretizer);

        var bruises = document.Attributes[1];
        Assert.Equal("bruises?", bruises.Name);
        Assert.Null(bruises.Include); // not authored; the default merges at resolve
        Assert.IsType<IdentityDiscretizerSection>(bruises.Discretizer);
        Assert.Equal("t", Assert.IsType<DichotomicScaleSection>(bruises.Scale).TrueValue);
        Assert.Equal(["t", "f"], bruises.DeclaredDomain);

        var gillSize = document.Attributes[2];
        Assert.Equal(["b", "n"], gillSize.ValueLabels!.Keys); // authored order preserved
        Assert.Equal("broad", gillSize.ValueLabels["b"]);
    }

    [Fact]
    public void Read_WhenExtendsAuthored_ThenSpecSectionCarriesIt()
    {
        var document = ReadOk("[spec]\nversion = 1\nextends = \"../base/emage.toml\"\n");

        Assert.Equal("../base/emage.toml", document.Spec?.Extends);
    }

    [Fact]
    public void Read_WhenTemplateAuthored_ThenCarrierHoldsIdAndConfig()
    {
        // The §9.1 example verbatim.
        var document = ReadOk(
            "[[template]]\n" +
            "id = \"boolean_yes_no\"\n" +
            "discretizer    = { kind = \"identity\" }\n" +
            "scale          = { kind = \"dichotomic\", true_value = \"Yes\" }\n" +
            "declared_domain = [\"Yes\", \"No\"]\n");

        var template = Assert.Single(document.Templates);
        Assert.Equal("boolean_yes_no", template.Id);
        Assert.IsType<IdentityDiscretizerSection>(template.Discretizer);
        Assert.Equal("Yes", Assert.IsType<DichotomicScaleSection>(template.Scale).TrueValue);
        Assert.Equal(["Yes", "No"], template.DeclaredDomain);
        Assert.Null(template.Include);
    }

    [Fact]
    public void Read_WhenMatchersAuthored_ThenBothMatchFormsCarry()
    {
        // The §9.2 example verbatim: one name_regex matcher, one range matcher.
        var document = ReadOk(
            "[[matcher]]\n" +
            "match    = { name_regex = \"^feature_\\\\d+$\" }\n" +
            "template = \"boolean_yes_no\"\n" +
            "\n" +
            "[[matcher]]\n" +
            "match    = { source_index_range = [10, 1553] }\n" +
            "template = \"boolean_yes_no\"\n");

        Assert.Equal(2, document.Matchers.Count);
        Assert.Equal("^feature_\\d+$", document.Matchers[0].Match?.NameRegex);
        Assert.Null(document.Matchers[0].Match?.SourceIndexRange);
        Assert.Equal("boolean_yes_no", document.Matchers[0].Template);
        Assert.Equal([10L, 1553L], document.Matchers[1].Match?.SourceIndexRange);
        Assert.Null(document.Matchers[1].Match?.NameRegex);
    }

    [Fact]
    public void Read_WhenAttributeReferencesTemplate_ThenCarried()
    {
        var document = ReadOk(Attribute("template = \"boolean_yes_no\""));

        Assert.Equal("boolean_yes_no", document.Attributes[0].Template);
    }

    [Fact]
    public void Read_WhenValueEqualsItsDefault_ThenPresenceIsStillAuthored()
    {
        // D-049/§6: an authored missing_policy = "skip" is provenance, not noise.
        var document = ReadOk(Attribute("missing_policy = \"skip\""));

        Assert.Equal(MissingPolicy.Skip, document.Attributes[0].MissingPolicy);
    }

    [Fact]
    public void Read_WhenDeclaredDomainOmitted_ThenNull()
    {
        var document = ReadOk(Attribute(""));

        Assert.Null(document.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Read_WhenDeclaredDomainAuthoredEmpty_ThenEmptyList()
    {
        // D-071: [] and omitted both resolve absent, but the authored form survives.
        var document = ReadOk(Attribute("declared_domain = []"));

        Assert.NotNull(document.Attributes[0].DeclaredDomain);
        Assert.Empty(document.Attributes[0].DeclaredDomain!);
    }

    [Fact]
    public void Read_WhenAsAttributeAuthoredAtBothLevels_ThenBothCarry()
    {
        var document = ReadOk(
            "[spec]\nversion = 1\n[defaults]\nmissing_policy = \"as_attribute\"\n" +
            "[[attribute]]\nname = \"a\"\nmissing_policy = \"as_attribute\"\n");

        Assert.Equal(MissingPolicy.AsAttribute, document.Defaults?.MissingPolicy);
        Assert.Equal(MissingPolicy.AsAttribute, document.Attributes[0].MissingPolicy);
    }

    [Fact]
    public void Read_WhenRestrictToMixesAllForms_ThenEntriesCarryInOrder()
    {
        var document = ReadOk(Attribute(
            "restrict_to = [\"Bachelors\", { from = 10, to = 20 }, { from = 90 }, { to = 5 }, {}]"));

        var entries = document.Attributes[0].RestrictTo!;
        Assert.Equal("Bachelors", Assert.IsType<RestrictToValue>(entries[0]).Value);
        Assert.Equal(new RestrictToRange(10, 20), entries[1]);
        Assert.Equal(new RestrictToRange(90, null), entries[2]);
        Assert.Equal(new RestrictToRange(null, 5), entries[3]);
        Assert.Equal(new RestrictToRange(null, null), entries[4]);
    }

    [Fact]
    public void Read_WhenCutsMixIntegerAndFloatNodes_ThenAllBecomeDoubles()
    {
        var document = ReadOk(Attribute(
            "discretizer = { kind = \"manual_cuts\", cuts = [30, 40.5, 5e1], ends = \"closed\" }"));

        var manual = Assert.IsType<ManualCutsDiscretizerSection>(document.Attributes[0].Discretizer);
        Assert.Equal([30d, 40.5d, 50d], manual.Cuts);
        Assert.Equal(Core.Discretization.BinEnds.Closed, manual.Ends);
    }

    [Fact]
    public void Read_WhenTripleBinding_ThenCarrierFieldsSurvive()
    {
        var document = ReadOk(TomlFixtures.MiniAdultTriples);

        Assert.Equal(SourceShape.Triple, document.Binding?.Shape);
        Assert.Equal(TripleOrdering.Unordered, document.Binding?.Ordering); // §19.3 uses unordered
        Assert.Equal(
            new TripleColumnsSection(new IndexColumnRef(0), new IndexColumnRef(1), new IndexColumnRef(2)),
            document.Binding?.Columns);

        var age = Assert.IsType<PredicateSourceSection>(document.Attributes[0].Source);
        Assert.Equal("age", age.Name);
    }

    [Fact]
    public void Read_WhenObjectKeyColumnByIndex_ThenIndexRef()
    {
        var document = ReadOk("[binding.object_key]\nmode = \"column\"\ncolumn = 2\n");

        Assert.Equal(new IndexColumnRef(2), document.Binding?.ObjectKey?.Column);
    }

    [Fact]
    public void Read_WhenObjectKeyColumnByName_ThenNameRef()
    {
        var document = ReadOk("[binding.object_key]\nmode = \"column\"\ncolumn = \"id\"\n");

        Assert.Equal(new NameColumnRef("id"), document.Binding?.ObjectKey?.Column);
        Assert.NotNull(document.Binding); // object_key alone yields a binding section
        Assert.Null(document.Binding!.Shape);
    }

    [Fact]
    public void Read_WhenSourceBoundByHeaderName_ThenNameCarries()
    {
        var document = ReadOk(Attribute(string.Empty,
            source: "{ kind = \"column\", name = \"age\", value_type = \"number\" }"));

        var column = Assert.IsType<ColumnSourceSection>(document.Attributes[0].Source);
        Assert.Null(column.Index);
        Assert.Equal("age", column.Name);
        Assert.Equal(SourceValueType.Number, column.ValueType);
    }

    [Fact]
    public void Read_WhenOrdinalScaleFullyAuthored_ThenAllFieldsCarry()
    {
        var document = ReadOk(Attribute(
            "scale = { kind = \"ordinal\", direction = \"le\", boundary = \"strict\", order = [\"a\", \"b\"], drop_top = true }"));

        var ordinal = Assert.IsType<OrdinalScaleSection>(document.Attributes[0].Scale);
        Assert.Equal(OrdinalDirection.Le, ordinal.Direction);
        Assert.Equal(OrdinalBoundary.Strict, ordinal.Boundary);
        Assert.Equal(["a", "b"], ordinal.Order);
        Assert.True(ordinal.DropTop);
    }

    [Fact]
    public void Read_WhenScaleKindIsDeferred_ThenKindOnlyCarrier()
    {
        // D-010: parses into the kind-only carrier; the planner owns the reject.
        var document = ReadOk(Attribute("scale = { kind = \"contranominal\" }"));

        Assert.Equal("contranominal", Assert.IsType<DeferredScaleSection>(document.Attributes[0].Scale).Kind);
    }

    [Fact]
    public void Read_WhenNonStandardQuoteChar_ThenCarriedWithoutDiagnostics()
    {
        // D-054: quote_char is a carrier here; standardness is the validation slice.
        var result = SpecReader.Read("[binding]\nshape = \"wide\"\nquote_char = \"'\"\n");

        Assert.True(result.TryGetValue(out var document));
        Assert.Empty(result.Diagnostics);
        Assert.Equal('\'', document.Binding?.QuoteChar);
    }

    [Fact]
    public void Read_WhenOrdinalOrderAuthoredWithCuts_ThenParsesClean()
    {
        // D-060 boundary: ordinal-over-cuts checks are the validation slice, not
        // the reader — the document admits the possibly-invalid state (D-066).
        var result = SpecReader.Read(Attribute(
            "discretizer = { kind = \"manual_cuts\", cuts = [30] }\n" +
            "scale = { kind = \"ordinal\", order = [\"a\"] }"));

        Assert.True(result.IsOk);
    }

    [Fact]
    public void Read_WhenProvenanceUsesOffsetForm_ThenTakenVerbatim()
    {
        var document = ReadOk("[provenance]\ncreated_at = 2026-05-09T10:00:00+02:00\n");

        Assert.Equal(new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.FromHours(2)), document.Provenance?.CreatedAt);
    }

    [Fact]
    public void Read_WhenProvenanceUsesLocalForm_ThenCoercedToZeroOffset()
    {
        // D-075: deterministic across machines; the field is inert provenance (§4).
        var document = ReadOk("[provenance]\ncreated_at = 2026-05-09\n");

        Assert.Equal(new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero), document.Provenance?.CreatedAt);
    }

    [Fact]
    public void Read_WhenOutputSectionsAuthored_ThenAllFieldsCarry()
    {
        var document = ReadOk(
            "[output]\nbin_label_unicode = true\n" +
            "[output.cxt]\nline_endings = \"crlf\"\ntrailing_newline = false\nsize_advisory_bytes = 0\n" +
            "[output.dat]\nline_endings = \"lf\"\ntrailing_newline = false\nbase_index = 0\nnonempty_line_trailing_space = true\nempty_line_trailing_space = true\n");

        Assert.True(document.Output?.BinLabelUnicode);
        Assert.Equal(new CxtOutputSection(LineEndings.Crlf, false, 0), document.Output?.Cxt);
        Assert.Equal(new DatOutputSection(LineEndings.Lf, 0, true, true) { TrailingNewline = false }, document.Output?.Dat);
    }

    [Fact]
    public void Read_WhenKitchenSink_ThenNoDiagnostics()
    {
        var result = SpecReader.Read(TomlFixtures.KitchenSink);

        Assert.True(result.IsOk);
        Assert.Empty(result.Diagnostics);
    }

    private static FcaBedrock.Spec.Toml.SpecDocument ReadOk(string toml)
    {
        var result = SpecReader.Read(toml);
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return document;
    }

    private static string Attribute(string body, string source = "{ kind = \"column\", index = 0 }") =>
        $"[spec]\nversion = 1\n[[attribute]]\nname = \"a\"\nsource = {source}\n{body}\n";
}
