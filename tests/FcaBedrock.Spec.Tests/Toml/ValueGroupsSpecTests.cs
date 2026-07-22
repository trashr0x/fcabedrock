using System.Collections.Immutable;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

/// <summary>
/// The <c>value_groups</c> TOML surface (§11.6, M4 Slice E / D-090/D-104): the document carriers,
/// the reader's parse-phase field gates (including the regex compile check and the G-11 matcher
/// predicate), canonical writing, authored-presence round-trip, the deep document snapshot, and
/// the resolve seam with its two spec-validate diagnostics.
/// <para>
/// <c>skip</c>/<c>other</c> are spec-determined and resolve straight to the executable
/// discretizer; <c>passthrough</c> discovers its bins from the data, so it resolves to the
/// <c>CalibrationPending</c> carrier the Calibrate phase replaces (§7/D-093).
/// </para>
/// </summary>
public sealed class ValueGroupsSpecTests
{
    private const string SchoolGroups = "groups = [{ label = \"School\", values = [\"11th\", \"HS-grad\"] }]";

    private static string Attribute(string discretizer, string? scale = null, string? extra = null) =>
        "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n[[attribute]]\nname = \"a\"\n"
            + "source = { kind = \"column\", index = 0 }\n"
            + $"discretizer = {discretizer}\n"
            + $"scale = {scale ?? "{ kind = \"nominal\" }"}\n"
            + (extra ?? string.Empty);

    private static SpecDocument ReadOk(string toml)
    {
        var result = SpecReader.Read(toml);
        Assert.True(result.TryGetValue(out var document),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Empty(result.Diagnostics);
        return document!;
    }

    private static ValueGroupsDiscretizerSection ReadDiscretizer(string discretizer) =>
        Assert.IsType<ValueGroupsDiscretizerSection>(ReadOk(Attribute(discretizer)).Attributes[0].Discretizer);

    private static void AssertFieldInvalid(string discretizer)
    {
        var result = SpecReader.Read(Attribute(discretizer));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecFieldInvalid);
    }

    private static Diagnosed<BedrockSpec> Resolve(string toml, SourceSchema? schema = null)
    {
        var resolved = SpecResolver.Resolve(ReadOk(toml), schema ?? new SourceSchema(1));
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<BedrockSpec>.Failed(resolved.Diagnostics);
    }

    private static Discretizer ResolveDiscretizer(string toml)
    {
        var result = Resolve(toml);
        Assert.True(result.TryGetValue(out var spec),
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return spec!.Attributes[0].Discretizer!;
    }

    // --- Reading: the document carriers ---------------------------------------

    [Fact]
    public void Read_WhenKindIsValueGroups_ThenItCarriesTheGroupsInDeclarationOrder()
    {
        var section = ReadDiscretizer(
            "{ kind = \"value_groups\", groups = ["
                + "{ label = \"School\", values = [\"11th\", \"HS-grad\"] }, "
                + "{ label = \"Cardiac\", pattern = \"^I[0-9]{2}\" }] }");

        Assert.Equal(["School", "Cardiac"], section.Groups!.Select(g => g.Label));
        Assert.Equal(["11th", "HS-grad"], section.Groups![0].Values);
        Assert.Equal("^I[0-9]{2}", section.Groups![1].Pattern);
    }

    [Fact]
    public void Read_WhenUnmatchedOmitted_ThenItStaysUnauthored() =>
        // Presence tracking (D-049): the §11.6 default is "skip", but an omitted field must stay
        // omitted so the writer does not invent authored config; the default resolves at the seam.
        Assert.Null(ReadDiscretizer("{ kind = \"value_groups\", " + SchoolGroups + " }").Unmatched);

    [Theory]
    [InlineData("skip", ValueGroupsUnmatched.Skip)]
    [InlineData("other", ValueGroupsUnmatched.Other)]
    [InlineData("passthrough", ValueGroupsUnmatched.Passthrough)]
    public void Read_WhenUnmatchedAuthored_ThenEverySpellingIsRecognized(string spelling, ValueGroupsUnmatched expected) =>
        Assert.Equal(
            expected,
            ReadDiscretizer($"{{ kind = \"value_groups\", {SchoolGroups}, unmatched = \"{spelling}\" }}").Unmatched);

    [Fact]
    public void Read_WhenValuesOnly_ThenPatternStaysNull()
    {
        var group = ReadDiscretizer("{ kind = \"value_groups\", " + SchoolGroups + " }").Groups![0];

        Assert.Equal(["11th", "HS-grad"], group.Values);
        Assert.Null(group.Pattern);
    }

    [Fact]
    public void Read_WhenPatternOnly_ThenValuesStaysNullMeaningOmitted()
    {
        var group = ReadDiscretizer("{ kind = \"value_groups\", groups = [{ label = \"C\", pattern = \"^I\" }] }").Groups![0];

        Assert.Null(group.Values);
        Assert.Equal("^I", group.Pattern);
    }

    [Fact]
    public void Read_WhenValuesAuthoredEmptyWithAPattern_ThenTheEmptyListIsRetainedNotNull()
    {
        // The G-11 presence distinction, at the document boundary: authored [] must be
        // distinguishable from omitted, because the §14 encoding writes `values` only when
        // authored (D-094).
        var group = ReadDiscretizer("{ kind = \"value_groups\", groups = [{ label = \"C\", values = [], pattern = \"^I\" }] }").Groups![0];

        Assert.NotNull(group.Values);
        Assert.Empty(group.Values!);
    }

    [Fact]
    public void Read_WhenValuesAndPatternCombined_ThenBothCarried()
    {
        var group = ReadDiscretizer(
            "{ kind = \"value_groups\", groups = [{ label = \"C\", values = [\"x\"], pattern = \"^I\" }] }").Groups![0];

        Assert.Equal(["x"], group.Values);
        Assert.Equal("^I", group.Pattern);
    }

    [Fact]
    public void Read_WhenGroupsAuthoredEmpty_ThenItCarriesANonNullEmptyList()
    {
        // §11.6/D-104: `groups` is required but MAY be an empty array — it declares no explicit
        // groups, so every usable value is unmatched and follows the `unmatched` policy. The
        // carrier must therefore be an EMPTY list, never null: null is the omitted-key state the
        // parse gate rejects (Read_WhenGroupsOmitted_ThenSpecFieldInvalid), and collapsing the two
        // would turn a coherent spec into a parse error.
        var section = ReadDiscretizer("{ kind = \"value_groups\", groups = [] }");

        Assert.NotNull(section.Groups);
        Assert.Empty(section.Groups!);
    }

    [Fact]
    public void Read_WhenValuesHaveDuplicatesAndUnsortedOrder_ThenPreservedVerbatim() =>
        // Authored configuration, not a canonicalized set (§14).
        Assert.Equal(
            ["b", "a", "b"],
            ReadDiscretizer("{ kind = \"value_groups\", groups = [{ label = \"G\", values = [\"b\", \"a\", \"b\"] }] }")
                .Groups![0].Values);

    // --- Reading: the parse-phase gates (§11.6 → SpecFieldInvalid) -------------

    [Fact]
    public void Read_WhenGroupsOmitted_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\" }");

    [Fact]
    public void Read_WhenGroupsIsNotAnArray_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = 4 }");

    [Fact]
    public void Read_WhenAGroupIsNotATable_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [\"School\"] }");

    [Fact]
    public void Read_WhenGroupLabelMissing_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ values = [\"a\"] }] }");

    [Fact]
    public void Read_WhenGroupLabelEmpty_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ label = \"\", values = [\"a\"] }] }");

    [Fact]
    public void Read_WhenGroupLabelIsNotAString_ThenExactlyOneFieldErrorNotAlsoMissing()
    {
        // D-067, one condition → one code: a malformed field reports its own type error once and
        // is NOT additionally reported as missing or matcher-less.
        var result = SpecReader.Read(Attribute("{ kind = \"value_groups\", groups = [{ label = 4, values = [\"a\"] }] }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("label", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenValuesIsNotAnArray_ThenExactlyOneFieldErrorNotAlsoMatcherless()
    {
        var result = SpecReader.Read(Attribute("{ kind = \"value_groups\", groups = [{ label = \"G\", values = 4 }] }"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("values", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenAValueEntryIsNotAString_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ label = \"G\", values = [\"a\", 4] }] }");

    [Fact]
    public void Read_WhenAnExplicitValueIsEmpty_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ label = \"G\", values = [\"a\", \"\"] }] }");

    [Fact]
    public void Read_WhenPatternEmpty_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ label = \"G\", pattern = \"\" }] }");

    [Fact]
    public void Read_WhenPatternIsNotAValidRegex_ThenSpecFieldInvalidWithNoDedicatedRegexCode()
    {
        // D-090: the compile check happens at parse, and an uncompilable pattern is ONE
        // SpecFieldInvalid condition — there is deliberately no regex-error code of its own.
        var result = SpecReader.Read(Attribute("{ kind = \"value_groups\", groups = [{ label = \"G\", pattern = \"^I[0-9\" }] }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.SpecFieldInvalid, diagnostic.Code);
        Assert.Contains("pattern", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_WhenValuesAuthoredEmptyAndNoPattern_ThenSpecFieldInvalid() =>
        // The G-11 predicate at the reader: `values = []` alone is authored-but-matches-nothing.
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ label = \"G\", values = [] }] }");

    [Fact]
    public void Read_WhenGroupHasNeitherMatcher_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", groups = [{ label = \"G\" }] }");

    [Fact]
    public void Read_WhenUnmatchedSpellingUnrecognized_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", " + SchoolGroups + ", unmatched = \"wibble\" }");

    [Fact]
    public void Read_WhenUnmatchedIsNotAString_ThenSpecFieldInvalid() =>
        AssertFieldInvalid("{ kind = \"value_groups\", " + SchoolGroups + ", unmatched = 4 }");

    [Fact]
    public void Read_WhenUnknownKeyInsideTheDiscretizer_ThenSpecKeyUnrecognized()
    {
        // D-075: unknown keys stay ordinary key errors — value_groups gains no special surface.
        var result = SpecReader.Read(Attribute("{ kind = \"value_groups\", " + SchoolGroups + ", wibble = 1 }"));

        Assert.Equal(DiagnosticCode.SpecKeyUnrecognized, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Read_WhenUnknownKeyInsideAGroup_ThenSpecKeyUnrecognized() =>
        Assert.Equal(
            DiagnosticCode.SpecKeyUnrecognized,
            Assert.Single(SpecReader.Read(Attribute(
                "{ kind = \"value_groups\", groups = [{ label = \"G\", values = [\"a\"], wibble = 1 }] }")).Diagnostics).Code);

    [Fact]
    public void Read_WhenSeveralIndependentProblems_ThenAllAggregate()
    {
        // P-14: independent failures report together rather than stopping at the first — a bad
        // group AND a bad unmatched must both surface.
        var result = SpecReader.Read(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"G\" }], unmatched = \"wibble\" }"));

        Assert.False(result.IsOk);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SpecFieldInvalid));
    }

    [Fact]
    public void Read_WhenMalformed_ThenNothingEscapesAsAnException()
    {
        // The strict Core factories must never see malformed input: the reader gates first, so
        // every authored error leaves on the diagnostic channel (P-14).
        foreach (var discretizer in new[]
        {
            "{ kind = \"value_groups\" }",
            "{ kind = \"value_groups\", groups = [{ label = \"G\" }] }",
            "{ kind = \"value_groups\", groups = [{ label = \"G\", pattern = \"^I[0-9\" }] }",
            "{ kind = \"value_groups\", groups = [{ label = \"G\", values = [] }] }",
            "{ kind = \"value_groups\", groups = [{ label = \"\", values = [\"a\"] }] }",
        })
        {
            var result = SpecReader.Read(Attribute(discretizer));
            Assert.False(result.IsOk);
        }
    }

    // --- Writing --------------------------------------------------------------

    private static string WriteDiscretizerLine(string toml) =>
        SpecWriter.Write(ReadOk(toml)).Split('\n').Single(l => l.StartsWith("discretizer = ", StringComparison.Ordinal));

    [Fact]
    public void Write_WhenValueGroups_ThenCanonicalPresentationOrderKindGroupsUnmatched() =>
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"School\", values = [\"11th\", \"HS-grad\"] }], unmatched = \"other\" }",
            WriteDiscretizerLine(Attribute("{ kind = \"value_groups\", " + SchoolGroups + ", unmatched = \"other\" }")));

    [Fact]
    public void Write_WhenGroupHasBothMatchers_ThenLabelValuesPattern() =>
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"C\", values = [\"x\"], pattern = \"^I\" }] }",
            WriteDiscretizerLine(Attribute("{ kind = \"value_groups\", groups = [{ label = \"C\", values = [\"x\"], pattern = \"^I\" }] }")));

    [Fact]
    public void Write_WhenUnmatchedOmitted_ThenTheSkipDefaultIsNotInjected() =>
        // D-049: writing the resolved default into the author's text would change the document.
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"School\", values = [\"11th\", \"HS-grad\"] }] }",
            WriteDiscretizerLine(Attribute("{ kind = \"value_groups\", " + SchoolGroups + " }")));

    [Fact]
    public void Write_WhenValuesOmitted_ThenStaysOmitted() =>
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"C\", pattern = \"^I\" }] }",
            WriteDiscretizerLine(Attribute("{ kind = \"value_groups\", groups = [{ label = \"C\", pattern = \"^I\" }] }")));

    [Fact]
    public void Write_WhenValuesAuthoredEmpty_ThenWritesTheEmptyArray() =>
        // The other half of the G-11 presence rule: authored [] must be written back as [].
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"C\", values = [], pattern = \"^I\" }] }",
            WriteDiscretizerLine(Attribute("{ kind = \"value_groups\", groups = [{ label = \"C\", values = [], pattern = \"^I\" }] }")));

    [Fact]
    public void Write_WhenGroupsAuthoredEmpty_ThenWritesTheEmptyArrayRatherThanOmittingTheKey() =>
        // The writer's half of the D-104 empty-groups rule: omitting the key would produce a
        // document the strict reader rejects, so an authored [] must survive the write.
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [], unmatched = \"other\" }",
            WriteDiscretizerLine(Attribute("{ kind = \"value_groups\", groups = [], unmatched = \"other\" }")));

    [Fact]
    public void Write_WhenGroupsUnsorted_ThenDeclarationOrderIsNotCanonicalSorted() =>
        // Declaration order is semantic (first match wins), so the writer must not sort it.
        Assert.Equal(
            "discretizer = { kind = \"value_groups\", groups = [{ label = \"Zeta\", values = [\"z\"] }, { label = \"Alpha\", values = [\"a\"] }] }",
            WriteDiscretizerLine(Attribute(
                "{ kind = \"value_groups\", groups = [{ label = \"Zeta\", values = [\"z\"] }, { label = \"Alpha\", values = [\"a\"] }] }")));

    // --- Round-trip -----------------------------------------------------------

    [Theory]
    [InlineData("{ kind = \"value_groups\", groups = [{ label = \"School\", values = [\"11th\", \"HS-grad\"] }] }")]
    [InlineData("{ kind = \"value_groups\", groups = [{ label = \"C\", pattern = \"^I[0-9]{2}\" }], unmatched = \"skip\" }")]
    [InlineData("{ kind = \"value_groups\", groups = [{ label = \"C\", values = [], pattern = \"^I\" }], unmatched = \"other\" }")]
    [InlineData("{ kind = \"value_groups\", groups = [{ label = \"C\", values = [\"b\", \"a\", \"b\"] }], unmatched = \"passthrough\" }")]
    [InlineData("{ kind = \"value_groups\", groups = [], unmatched = \"other\" }")]
    public void ParseWriteParse_WhenAuthored_ThenIdempotentAndPresencePreserved(string discretizer)
    {
        var first = SpecWriter.Write(ReadOk(Attribute(discretizer)));
        var second = SpecWriter.Write(ReadOk(first));

        Assert.Equal(first, second);
        Assert.Contains(discretizer, first, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_WhenValuesOmittedVersusAuthoredEmpty_ThenTheTwoStayDistinct()
    {
        // The end-to-end G-11 lock at the document layer: the two authored forms must never
        // converge on one text, because their §14 bytes differ.
        var omitted = SpecWriter.Write(ReadOk(Attribute("{ kind = \"value_groups\", groups = [{ label = \"C\", pattern = \"^I\" }] }")));
        var authoredEmpty = SpecWriter.Write(ReadOk(Attribute("{ kind = \"value_groups\", groups = [{ label = \"C\", values = [], pattern = \"^I\" }] }")));

        Assert.NotEqual(omitted, authoredEmpty);
        Assert.Equal(omitted, SpecWriter.Write(ReadOk(omitted)));
        Assert.Equal(authoredEmpty, SpecWriter.Write(ReadOk(authoredEmpty)));
    }

    // --- Document snapshot (D-098) --------------------------------------------

    [Fact]
    public void Resolve_WhenCallerMutatesTheDocumentAfterwards_ThenTheSnapshotIsUnaffected()
    {
        // The snapshot must be DEEP: value_groups nests one list inside another, so copying only
        // the outer groups list would leave every inner values list caller-owned (D-098).
        var innerValues = new List<string> { "11th" };
        var groups = new List<ValueGroupSection> { new("School", innerValues, null) };
        var document = DocumentFixtures.Document([
            DocumentFixtures.Nominal("a", 0, []) with { Discretizer = new ValueGroupsDiscretizerSection(groups, null) },
        ]);

        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var doc),
            string.Join("; ", resolved.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        groups.Add(new ValueGroupSection("Injected", ["x"], null));
        innerValues.Add("HS-grad");

        var section = Assert.IsType<ValueGroupsDiscretizerSection>(doc!.Document.Attributes[0].Discretizer);
        Assert.Equal(["School"], section.Groups!.Select(g => g.Label));
        Assert.Equal(["11th"], section.Groups![0].Values);
    }

    [Fact]
    public void Resolve_WhenSnapshotted_ThenGroupListsAreNotCastableToMutableCollections()
    {
        var document = ReadOk(Attribute("{ kind = \"value_groups\", " + SchoolGroups + " }"));
        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var doc));

        var section = Assert.IsType<ValueGroupsDiscretizerSection>(doc!.Document.Attributes[0].Discretizer);
        Assert.IsType<ImmutableArray<ValueGroupSection>>(section.Groups);
        Assert.IsType<ImmutableArray<string>>(section.Groups![0].Values);
    }

    [Fact]
    public void Resolve_WhenSnapshotted_ThenAuthoredNullAndAuthoredEmptyValuesBothSurvive()
    {
        // The snapshot must preserve the presence distinction: `?.ToImmutableArray()` keeps null
        // null and [] empty, rather than collapsing either.
        var document = ReadOk(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"P\", pattern = \"^x\" }, { label = \"E\", values = [], pattern = \"^y\" }] }"));
        var resolved = SpecResolver.Resolve(document, new SourceSchema(1));
        Assert.True(resolved.TryGetValue(out var doc));

        var groups = Assert.IsType<ValueGroupsDiscretizerSection>(doc!.Document.Attributes[0].Discretizer).Groups!;
        Assert.Null(groups[0].Values);
        Assert.NotNull(groups[1].Values);
        Assert.Empty(groups[1].Values!);
    }

    // --- Resolving ------------------------------------------------------------

    [Theory]
    [InlineData("skip", ValueGroupsUnmatched.Skip)]
    [InlineData("other", ValueGroupsUnmatched.Other)]
    public void Resolve_WhenSkipOrOther_ThenAnExecutableDiscretizer(string spelling, ValueGroupsUnmatched expected)
    {
        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(
            ResolveDiscretizer(Attribute($"{{ kind = \"value_groups\", {SchoolGroups}, unmatched = \"{spelling}\" }}")));

        Assert.Equal(expected, discretizer.Unmatched);
        Assert.Equal(["School"], discretizer.Groups.Select(g => g.Label));
    }

    [Fact]
    public void Resolve_WhenUnmatchedOmitted_ThenItDefaultsToSkip() =>
        Assert.Equal(
            ValueGroupsUnmatched.Skip,
            Assert.IsType<ValueGroupsDiscretizer>(
                ResolveDiscretizer(Attribute("{ kind = \"value_groups\", " + SchoolGroups + " }"))).Unmatched);

    [Fact]
    public void Resolve_WhenPassthrough_ThenTheCalibrationPendingCarrier()
    {
        // Passthrough cannot resolve without data (§7/D-093), so it must NOT become executable
        // here — ValueGroupsDiscretizer.Create would refuse it anyway.
        var pending = Assert.IsType<CalibrationPending>(
            ResolveDiscretizer(Attribute("{ kind = \"value_groups\", " + SchoolGroups + ", unmatched = \"passthrough\" }")));

        Assert.Equal("value_groups", pending.Kind);
        var config = Assert.IsType<PendingValueGroupsPassthrough>(pending.Config);
        Assert.Equal(["School"], config.Groups.Select(g => g.Label));
    }

    [Theory]
    [InlineData("skip", ValueGroupsUnmatched.Skip)]
    [InlineData("other", ValueGroupsUnmatched.Other)]
    public void Resolve_WhenGroupsAuthoredEmpty_ThenAnExecutableDiscretizerWithNoGroups(
        string spelling, ValueGroupsUnmatched expected)
    {
        // D-104 rejected a non-empty-groups requirement: each *group* is the unit of validity, and
        // an empty group list is coherent — under `skip` nothing is recognized, under `other`
        // everything falls into the synthetic bin. Neither is an error at the seam.
        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(
            ResolveDiscretizer(Attribute($"{{ kind = \"value_groups\", groups = [], unmatched = \"{spelling}\" }}")));

        Assert.Equal(expected, discretizer.Unmatched);
        Assert.Empty(discretizer.Groups);
    }

    [Fact]
    public void Resolve_WhenGroupsAuthoredEmptyAndPassthrough_ThenThePendingCarrierWithNoGroups()
    {
        // The empty-groups form is data-dependent in exactly the same way as any other
        // passthrough: it resolves to the pending carrier, and every usable value is eligible for
        // discovery because no group can claim one.
        var pending = Assert.IsType<CalibrationPending>(
            ResolveDiscretizer(Attribute("{ kind = \"value_groups\", groups = [], unmatched = \"passthrough\" }")));

        Assert.Equal("value_groups", pending.Kind);
        Assert.Empty(Assert.IsType<PendingValueGroupsPassthrough>(pending.Config).Groups);
    }

    [Fact]
    public void Resolve_WhenResolved_ThenAuthoredPresenceSurvivesIntoCore()
    {
        var discretizer = Assert.IsType<ValueGroupsDiscretizer>(ResolveDiscretizer(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"P\", pattern = \"^x\" }, { label = \"E\", values = [], pattern = \"^y\" }] }")));

        Assert.Null(discretizer.Groups[0].Values);
        Assert.NotNull(discretizer.Groups[1].Values);
        Assert.Empty(discretizer.Groups[1].Values!);
    }

    // --- value_type is string-fixing (D-061) ----------------------------------

    [Fact]
    public void Resolve_WhenValueTypeAbsent_ThenStringByDefault()
    {
        var result = Resolve(Attribute("{ kind = \"value_groups\", " + SchoolGroups + " }"));

        Assert.True(result.TryGetValue(out var spec));
        Assert.Equal(SourceValueType.String, Assert.IsType<ColumnSource>(spec!.Attributes[0].Source).ValueType);
    }

    [Fact]
    public void Resolve_WhenValueTypeAuthoredString_ThenAccepted()
    {
        var toml = "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n[[attribute]]\nname = \"a\"\n"
            + "source = { kind = \"column\", index = 0, value_type = \"string\" }\n"
            + "discretizer = { kind = \"value_groups\", " + SchoolGroups + " }\nscale = { kind = \"nominal\" }\n";

        Assert.True(Resolve(toml).IsOk);
    }

    [Fact]
    public void Resolve_WhenValueTypeAuthoredNumber_ThenSourceValueTypeInvalid()
    {
        // §10.2/D-061: value_groups matches raw value SPELLINGS, so it is string-fixing.
        var toml = "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n[[attribute]]\nname = \"a\"\n"
            + "source = { kind = \"column\", index = 0, value_type = \"number\" }\n"
            + "discretizer = { kind = \"value_groups\", " + SchoolGroups + " }\nscale = { kind = \"nominal\" }\n";

        var result = Resolve(toml);

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueTypeInvalid);
    }

    [Fact]
    public void Resolve_WhenTriplePredicateSourceDeclaresNumber_ThenSourceValueTypeInvalidToo()
    {
        // The rule is source-kind agnostic: value_type is a source-level property of both column
        // and predicate sources (§10.2).
        var toml = "[spec]\nversion = 1\n[binding]\nshape = \"triple\"\n[[attribute]]\nname = \"a\"\n"
            + "source = { kind = \"predicate\", name = \"p\", value_type = \"number\" }\n"
            + "discretizer = { kind = \"value_groups\", " + SchoolGroups + " }\nscale = { kind = \"nominal\" }\n";

        var result = Resolve(toml, new SourceSchema(3));

        Assert.False(result.IsOk);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceValueTypeInvalid);
    }

    // --- ValueGroupsLabelDuplicate (spec validate, D-090) ---------------------

    [Fact]
    public void Resolve_WhenDuplicateGroupLabels_ThenValueGroupsLabelDuplicateNotSpecFieldInvalid()
    {
        // D-090: duplicates own their code and never surface as SpecFieldInvalid — they are a
        // cross-group rule the reader cannot see, so the seam owns them.
        var result = Resolve(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"G\", values = [\"a\"] }, { label = \"G\", values = [\"b\"] }] }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ValueGroupsLabelDuplicate, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenLabelCollidesWithSyntheticOther_ThenValueGroupsLabelDuplicate()
    {
        var result = Resolve(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"Other\", values = [\"a\"] }], unmatched = \"other\" }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ValueGroupsLabelDuplicate, diagnostic.Code);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Fact]
    public void Resolve_WhenLabelIsOtherButPolicyIsSkip_ThenNoDiagnosticBecauseNoSyntheticBinExists() =>
        Assert.True(Resolve(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"Other\", values = [\"a\"] }], unmatched = \"skip\" }")).IsOk);

    [Fact]
    public void Resolve_WhenLabelIsLowercaseOtherUnderOther_ThenNoDiagnosticBecauseComparisonIsOrdinal() =>
        // P-12: "Other" collides; "other" does not.
        Assert.True(Resolve(Attribute(
            "{ kind = \"value_groups\", groups = [{ label = \"other\", values = [\"a\"] }], unmatched = \"other\" }")).IsOk);

    [Fact]
    public void Resolve_WhenLabelDuplicatedOnAnExcludedAttribute_ThenParkedConfigDoesNotBlock()
    {
        // D-049: an excluded attribute's config is parked — never validated, never an error.
        var toml = "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n[[attribute]]\nname = \"a\"\ninclude = false\n"
            + "source = { kind = \"column\", index = 0 }\n"
            + "discretizer = { kind = \"value_groups\", groups = [{ label = \"G\", values = [\"a\"] }, { label = \"G\", values = [\"b\"] }] }\n"
            + "scale = { kind = \"nominal\" }\n";

        Assert.True(Resolve(toml).IsOk);
    }

    // --- OrdinalNotAllowedWithValueGroupsPassthrough (spec validate, D-090) ---

    [Fact]
    public void Resolve_WhenOrdinalOverPassthrough_ThenOrdinalNotAllowedWithValueGroupsPassthrough()
    {
        // §11.6/§12.3: a data-discovered bin set can never be a full authored permutation, so the
        // combination is rejected statically rather than failing later at plan.
        var result = Resolve(Attribute(
            "{ kind = \"value_groups\", " + SchoolGroups + ", unmatched = \"passthrough\" }",
            scale: "{ kind = \"ordinal\", order = [\"School\"] }"));

        Assert.False(result.IsOk);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.OrdinalNotAllowedWithValueGroupsPassthrough, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("a", diagnostic.Location?.AttributeName);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("other")]
    public void Resolve_WhenOrdinalOverSkipOrOther_ThenAllowed(string spelling)
    {
        var order = spelling == "other" ? "[\"School\", \"Other\"]" : "[\"School\"]";

        Assert.True(Resolve(Attribute(
            $"{{ kind = \"value_groups\", {SchoolGroups}, unmatched = \"{spelling}\" }}",
            scale: $"{{ kind = \"ordinal\", order = {order} }}")).IsOk);
    }

    [Fact]
    public void Resolve_WhenNominalOverPassthrough_ThenAllowed() =>
        // Only ORDINAL conflicts with passthrough; nominal/dichotomic are fine over discovered bins.
        Assert.True(Resolve(Attribute("{ kind = \"value_groups\", " + SchoolGroups + ", unmatched = \"passthrough\" }")).IsOk);

    // --- scale.order shape (existing OrderDomainInvalid, D-081) ---------------

    [Fact]
    public void Resolve_WhenOrderHasDuplicateEntries_ThenOrderDomainInvalid() =>
        // The existing value-bin order-shape rule applies unchanged: value_groups is not a cut
        // discretizer, so its order is validated for internal shape here and for permutation at plan.
        Assert.Contains(
            Resolve(Attribute(
                "{ kind = \"value_groups\", " + SchoolGroups + " }",
                scale: "{ kind = \"ordinal\", order = [\"School\", \"School\"] }")).Diagnostics,
            d => d.Code == DiagnosticCode.OrderDomainInvalid);

    [Fact]
    public void Resolve_WhenOrderHasAnEmptyEntry_ThenOrderDomainInvalid() =>
        Assert.Contains(
            Resolve(Attribute(
                "{ kind = \"value_groups\", " + SchoolGroups + " }",
                scale: "{ kind = \"ordinal\", order = [\"School\", \"\"] }")).Diagnostics,
            d => d.Code == DiagnosticCode.OrderDomainInvalid);

    [Fact]
    public void Resolve_WhenOrderAuthoredOverValueGroups_ThenNotOrdinalOrderNotAllowedWithCuts() =>
        // value_groups bins are VALUE bins, so scale.order is legitimate over them — the
        // cut-discretizer prohibition must not extend here (§12.3).
        Assert.DoesNotContain(
            Resolve(Attribute(
                "{ kind = \"value_groups\", " + SchoolGroups + " }",
                scale: "{ kind = \"ordinal\", order = [\"School\"] }")).Diagnostics,
            d => d.Code == DiagnosticCode.OrdinalOrderNotAllowedWithCuts);

    // --- declared_domain / value_labels stay dormant (D-055/D-049) ------------

    [Fact]
    public void Resolve_WhenDeclaredDomainAuthoredUnderValueGroups_ThenDormantAndNeverAnError()
    {
        // D-055: value_groups does not consult declared_domain, so an authored one is inert —
        // no domain validation is triggered by it.
        var result = Resolve(Attribute(
            "{ kind = \"value_groups\", " + SchoolGroups + " }",
            extra: "declared_domain = [\"totally\", \"unrelated\"]\n"));

        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => d.Code.ToString())));
        Assert.Empty(result.Diagnostics);

        // It resolves onto the attribute (parked, round-trippable) but the discretizer ignores it.
        Assert.Equal(["totally", "unrelated"], spec!.Attributes[0].DeclaredDomain);
    }

    [Fact]
    public void Resolve_WhenValueLabelsAuthoredUnderValueGroups_ThenDormantAndNeverAnError()
    {
        // §10.8/D-049: value_groups does not consult value_labels (a group label already IS the
        // display label), so no ValueLabelKeyNotInDomain fires even though no key is in any domain.
        var result = Resolve(Attribute(
            "{ kind = \"value_groups\", " + SchoolGroups + " }",
            extra: "value_labels = { School = \"Secondary\", nope = \"x\" }\n"));

        Assert.True(result.IsOk);
        Assert.Empty(result.Diagnostics);
    }
}
