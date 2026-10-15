using System.Runtime.CompilerServices;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// Categories containing CR or LF (§7.1): a complete domain keeps its representable categories
/// and leaves those out, with a warning and a saved notice; a complete domain with none left
/// authors a fixed empty domain; a truncated domain that observed one, retained or not, yields
/// no draft, because <c>include</c> would recover it at conversion.
/// <para>
/// Every expected text is spelled out here, independently of the production formatter, because
/// the notes and descriptions are saved bytes and the messages are public contract.
/// </para>
/// </summary>
public sealed class ProbeOmissionTests
{
    // Row 2 and row 4 repeat one multiline category; row 5 carries a CR. Through the real reader,
    // so quoting and line breaks inside fields are part of the claim.
    private const string MixedCsv = "id,note\n1,a\n2,\"x\ny\"\n3,b\n4,\"x\ny\"\n5,\"c\rd\"\n";

    private const string MixedNotes =
        "probe: WARNING: omitted 2 distinct categories containing CR/LF across 1 attributes (counts are per attribute). "
        + "These categories produce no crosses; objects are kept. Review the affected attribute descriptions.\n"
        + "probe: per-attribute retention limit 100000; truncated attributes: 0.";

    private const string MixedDescription =
        "probe: omitted 2 distinct categories containing CR/LF; retained 2 representable categories. "
        + "Omitted examples: \"x\\ny\", \"c\\rd\".";

    private static ProbeOptions Limit(int limit) => ProbeOptions.Create(valueRetentionLimit: limit);

    private static AttributeSection Attribute(SpecDocument draft, string name) =>
        draft.Attributes.Single(a => a.Name == name);

    private static async Task<Diagnosed<SpecDocument>> ProbeColumnAsync(int limit, params string?[] values) =>
        await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(1, ["note"]), [.. values.Select(v => new[] { v })]),
            ProbeFixtures.WideSettings(),
            Limit(limit));

    // ---- complete domains keep their safe categories ---------------------------------------

    [Fact]
    public async Task Probe_WhenACompleteDomainMixesSafeAndMultilineCategories_ThenItKeepsTheSafeOnesAndWarns()
    {
        var result = await ProbeFixtures.ProbeCsvAsync(MixedCsv);
        var draft = ProbeFixtures.Draft(result);

        var note = Attribute(draft, "note");
        Assert.Equal(["a", "b"], note.DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Warn, note.UnknownValuePolicy);
        Assert.Equal(MixedDescription, note.Description);

        // The other attribute and every object are untouched.
        var id = Attribute(draft, "id");
        Assert.Equal(["1", "2", "3", "4", "5"], id.DeclaredDomain);
        Assert.Null(id.Description);
        Assert.Null(id.UnknownValuePolicy);
        Assert.Equal(MixedNotes, draft.Provenance!.Notes);

        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ProbeValueOmitted, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(
            "The probe omitted 2 distinct categories containing CR/LF across 1 attribute(s) (counts are per attribute; "
            + "e.g. \"note\": \"x\\ny\", \"note\": \"c\\rd\"); they produce no crosses, every object is kept, and the draft "
            + "records the omission in its notes and the affected attribute descriptions (§7.1).",
            warning.Message);
    }

    [Fact]
    public async Task Probe_WhenADomainOmitsCategories_ThenTheDraftConvertsEveryObjectWithTheSafeCrosses()
    {
        var draft = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(MixedCsv));

        var converted = await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(MixedCsv));

        Assert.Equal(["id-1", "id-2", "id-3", "id-4", "id-5", "note-a", "note-b"], converted.Columns);
        Assert.Equal(
            [["id-1", "note-a"], ["id-2"], ["id-3", "note-b"], ["id-4"], ["id-5"]],
            converted.Objects.Select(o => o.Crosses));

        // The omitted categories reach conversion as unknown values under the authored warn.
        Assert.Contains(converted.Diagnostics, d => d.Code == DiagnosticCode.UnknownValueObserved
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Probe_WhenEveryCategoryOfACompleteDomainIsMultiline_ThenItAuthorsAFixedEmptyDomainWithWarn()
    {
        const string csv = "k,v\n1,\"p\nq\"\n2,\"r\ns\"\n";

        var result = await ProbeFixtures.ProbeCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        var v = Attribute(draft, "v");
        Assert.NotNull(v.DeclaredDomain);
        Assert.Empty(v.DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Warn, v.UnknownValuePolicy);
        Assert.Equal(
            "probe: omitted 2 distinct categories containing CR/LF; retained 0 representable categories. "
            + "Omitted examples: \"p\\nq\", \"r\\ns\". The declared domain is fixed empty; unknown values warn.",
            v.Description);

        var toml = SpecWriter.Write(draft);
        Assert.Contains("declared_domain = []", toml, StringComparison.Ordinal);
        Assert.Contains("unknown_value_policy = \"warn\"", toml, StringComparison.Ordinal);

        var converted = await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(csv));
        Assert.Equal(["k-1", "k-2"], converted.Columns);
        Assert.Equal([["k-1"], ["k-2"]], converted.Objects.Select(o => o.Crosses));
    }

    [Fact]
    public async Task Probe_WhenAColumnIsAllMissing_ThenItsDomainStaysOmittedWithNoNotice()
    {
        // The contrast with the fixed empty domain above: nothing was observed, so the domain is
        // left for calibration and no omission is reported.
        var result = await ProbeFixtures.ProbeCsvAsync("k,v\n1,?\n2,\n");
        var draft = ProbeFixtures.Draft(result);

        var v = Attribute(draft, "v");
        Assert.Null(v.DeclaredDomain);
        Assert.Null(v.UnknownValuePolicy);
        Assert.Null(v.Description);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(ProbeDraftExpectations.NotesFor(100_000, 0), draft.Provenance!.Notes);
    }

    [Fact]
    public async Task Probe_WhenTheSameMultilineCategoryAppearsInTwoAttributes_ThenItCountsOncePerAttribute()
    {
        // Distinct memberships per attribute: three occurrences in each attribute count once
        // there, and the same text in two attributes counts in both.
        var rows = Enumerable.Repeat(new string?[] { "z\nz", "z\nz" }, 3).Append(["ok", "ok"]).ToArray();
        var result = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(2, ["a", "b"]), rows), ProbeFixtures.WideSettings());
        var draft = ProbeFixtures.Draft(result);

        Assert.StartsWith(
            "probe: WARNING: omitted 2 distinct categories containing CR/LF across 2 attributes (counts are per attribute).",
            draft.Provenance!.Notes,
            StringComparison.Ordinal);
        Assert.All(draft.Attributes, a => Assert.StartsWith(
            "probe: omitted 1 distinct categories containing CR/LF; retained 1 representable categories.",
            a.Description,
            StringComparison.Ordinal));
        Assert.Contains(
            "omitted 2 distinct categories containing CR/LF across 2 attribute(s)",
            Assert.Single(result.Diagnostics).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenAMultilineCategoryFitsTheLimitExactly_ThenTheDomainIsCompleteAndOmitsIt()
    {
        // At the limit, not over it: the domain is complete, so the category is left out rather
        // than refusing the draft.
        var result = await ProbeColumnAsync(3, "a", "b", "x\ny");
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["a", "b"], draft.Attributes[0].DeclaredDomain);
        Assert.Equal(DiagnosticCode.ProbeValueOmitted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Probe_WhenASafeTruncatedDomainSitsBesideAnOmittingOne_ThenEachKeepsItsOwnMarker()
    {
        var rows = new[]
        {
            new string?[] { "t1", "x\ny" },
            ["t2", "a"],
            ["t3", "a"],
        };
        var result = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(2, ["long", "short"]), rows), ProbeFixtures.WideSettings(), Limit(2));
        var draft = ProbeFixtures.Draft(result);

        var truncated = Attribute(draft, "long");
        Assert.Equal(["t1", "t2"], truncated.DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Include, truncated.UnknownValuePolicy);
        Assert.Equal(ProbeDraftExpectations.MarkerFor(2), truncated.Description);

        var omitting = Attribute(draft, "short");
        Assert.Equal(["a"], omitting.DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Warn, omitting.UnknownValuePolicy);

        Assert.Equal(
            "probe: WARNING: omitted 1 distinct categories containing CR/LF across 1 attributes (counts are per attribute). "
            + "These categories produce no crosses; objects are kept. Review the affected attribute descriptions.\n"
            + ProbeDraftExpectations.NotesFor(2, 1),
            draft.Provenance!.Notes);

        // Success order: truncation before omission (and naming before both).
        Assert.Equal(
            [DiagnosticCode.ProbeDomainTruncated, DiagnosticCode.ProbeValueOmitted],
            result.Diagnostics.Select(d => d.Code));
    }

    // ---- truncated domains refuse ----------------------------------------------------------

    public static TheoryData<string, string?[]> UnsafeTruncations() => new()
    {
        // The first value over the limit is the multiline one.
        { "first over the limit", ["a", "b", "x\ny"] },
        // A retained multiline value, then a safe value truncates the domain.
        { "retained, then truncated by a safe value", ["x\ny", "a", "b"] },
        // Truncated by a safe value, then a later discarded multiline value.
        { "later discarded", ["a", "b", "c", "x\ny"] },
        // The discarded multiline value repeats; nothing about it was remembered.
        { "repeated discarded", ["a", "b", "c", "x\ny", "x\ny"] },
        // A CR alone counts as much as an LF.
        { "carriage return", ["a", "b", "c\rd"] },
    };

    [Theory]
    [MemberData(nameof(UnsafeTruncations))]
    public async Task Probe_WhenATruncatedDomainObservedAMultilineCategory_ThenThereIsNoDraft(string label, string?[] values)
    {
        var result = await ProbeColumnAsync(2, values);

        Assert.False(result.TryGetValue(out _), label);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCode.ProbeUnsafeTruncatedDomain, error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(
            "The probe observed categories containing CR/LF in 1 truncated attribute(s) (e.g. \"note\"); a truncated "
            + "domain cannot omit them, because converting the draft would recover them through "
            + "unknown_value_policy = \"include\", so no draft was produced (§7.1).",
            error.Message);
    }

    [Fact]
    public async Task Probe_WhenFourTruncatedDomainsAreUnsafe_ThenOneErrorCountsAllAndSamplesThree()
    {
        // Wide names cover attributes that also have adjustments and truncation warnings to give:
        // none of the success aggregates survives a refusal.
        var rows = new[]
        {
            new string?[] { "a\n", "b\n", "c\n", "d\n", "ok" },
            ["a", "b", "c", "d", "ok"],
        };
        var result = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(5, ["w", "x", "y", "z", "z"]), rows), ProbeFixtures.WideSettings(), Limit(1));

        Assert.False(result.TryGetValue(out _));
        var error = Assert.Single(result.Diagnostics);
        Assert.StartsWith(
            "The probe observed categories containing CR/LF in 4 truncated attribute(s) (e.g. \"w\", \"x\", \"y\");",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_WhenTheTripleShapeTruncatesAnUnsafeDomain_ThenThereIsNoDraftEither()
    {
        var result = await Prober.ProbeTripleAsync(
            TripleProbeFixtures.Fake(("s1", "p", "a"), ("s2", "p", "b"), ("s3", "p", "x\ny"), ("s4", "q", "fine")),
            TripleProbeFixtures.TripleSettings(),
            options: Limit(2));

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.ProbeUnsafeTruncatedDomain, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Probe_WhenATripleDomainIsCompleteAndMixed_ThenItOmitsLikeWide()
    {
        const string csv = "s1,note,\"x\ny\"\ns2,note,a\ns3,size,big\n";

        var result = await TripleProbeFixtures.ProbeTripleCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["a"], Attribute(draft, "note").DeclaredDomain);
        Assert.Equal(UnknownValuePolicy.Warn, Attribute(draft, "note").UnknownValuePolicy);
        Assert.Equal(["big"], Attribute(draft, "size").DeclaredDomain);
        Assert.Equal(DiagnosticCode.ProbeValueOmitted, Assert.Single(result.Diagnostics).Code);

        var converted = await ProbeConversion.ConvertAsync(draft, TripleProbeFixtures.Bytes(csv));
        Assert.Equal(["note-a", "size-big"], converted.Columns);
        Assert.Equal(["s1", "s2", "s3"], converted.Objects.Select(o => o.Name));
        Assert.Equal([[], ["note-a"], ["size-big"]], converted.Objects.Select(o => o.Crosses));
    }

    // ---- earlier failures keep precedence --------------------------------------------------

    [Fact]
    public async Task Probe_WhenAGuardBreachesAfterAnUnsafeTruncation_ThenTheGuardIsTheOnlyDiagnostic()
    {
        // `note` retains a and b, then discards the multiline value: truncated and unsafe. Then
        // `other` retains q, the third charged value, and r breaches the values guard.
        var rows = new[]
        {
            new string?[] { "a", null }, ["b", null], ["x\ny", null], [null, "q"], [null, "r"],
        };
        var result = await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(2, ["note", "other"]), rows),
            ProbeFixtures.WideSettings(),
            ProbeOptions.Create(valueRetentionLimit: 2, maxTotalRetainedValues: 3));

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.ProbeLimitExceeded, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Probe_WhenTheSourceFailsAfterAnUnsafeTruncation_ThenTheReadFailureIsTheOnlyDiagnostic()
    {
        var session = new ProbeFixtures.FakeWideSession(
            new SourceSchema(1, ["note"]),
            ProbeFixtures.Records(["a"], ["b"], ["x\ny"], ["c"]),
            recordFailure: () => new IOException("disk"),
            failAfterRecords: 3);

        var result = await Prober.ProbeAsync(session, ProbeFixtures.WideSettings(), Limit(1));

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(DiagnosticCode.ProbeSourceReadFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Probe_WhenCancellationArrivesAfterTheLastRecord_ThenFinalizationCancelsWithNoDocument()
    {
        // The session's enumeration ends normally, and its closing requests cancellation: the
        // finishing work must observe it rather than hand back a draft (D-112).
        using var cancellation = new CancellationTokenSource();
        var session = new CancelOnCloseSession(cancellation, ["a", "x\ny", "b"]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Prober.ProbeAsync(session, ProbeFixtures.WideSettings(), Limit(10), cancellation.Token));
    }

    // ---- saved text: escapes, clipping and bounds ------------------------------------------

    [Fact]
    public async Task Draft_WhenItSavesOmissionText_ThenTheTomlRereadKeepsLiteralEscapesAndRealLineBreaks()
    {
        var draft = ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(MixedCsv));
        var toml = SpecWriter.Write(draft);

        // The notes hold a real LF between their two lines; the description holds the sample's
        // own backslash-n text, which TOML writes with a doubled backslash.
        Assert.Contains("descriptions.\\nprobe: per-attribute", toml, StringComparison.Ordinal);
        Assert.Contains("Omitted examples: \\\"x\\\\ny\\\", \\\"c\\\\rd\\\".", toml, StringComparison.Ordinal);

        var reread = SpecReader.Read(toml);
        Assert.True(reread.TryGetValue(out var document), ProbeFixtures.Describe(reread.Diagnostics));
        Assert.Equal(MixedNotes, document.Provenance!.Notes);
        Assert.Equal(MixedDescription, Attribute(document, "note").Description);
    }

    [Fact]
    public async Task Draft_WhenAnOmittedCategoryIsLongOrHoldsControls_ThenItsSampleIsClippedAndEscaped()
    {
        var pair = char.ConvertFromUtf32(0x1F600);
        var splitPair = new string('a', 127) + pair + "b";        // the pair straddles unit 128
        var nel = "\n" + new string((char)0x85, 199);              // 200 units, mostly C1 controls
        var mixed = "\\\"\t" + (char)0x01 + (char)0x7F + (char)0x2028 + "\r";

        var result = await ProbeColumnAsync(10, splitPair + "\n", nel, mixed, "safe");
        var description = ProbeFixtures.Draft(result).Attributes[0].Description!;

        var expectedSplit = "prefix \"" + new string('a', 127) + "\" (length 131 UTF-16 code units)";
        var expectedNel = "prefix \"\\n" + string.Concat(Enumerable.Repeat("\\u0085", 127)) + "\" (length 200 UTF-16 code units)";
        const string expectedMixed = "\"\\\\\\\"\\t\\u0001\\u007f\\u2028\\r\"";
        Assert.Equal(
            "probe: omitted 3 distinct categories containing CR/LF; retained 1 representable categories. Omitted examples: "
            + expectedSplit + ", " + expectedNel + ", " + expectedMixed + ".",
            description);
    }

    [Fact]
    public async Task Draft_WhenEverySampleIsAtItsLongest_ThenTheSavedTextStaysWithinItsBounds()
    {
        // Three distinct 200-unit samples of C1 controls, each escaped to six units, in an
        // all-unsafe domain, so the description carries every optional part.
        var values = Enumerable.Range(0, 3).Select(i => "\n" + new string((char)(0x80 + i), 199)).ToArray();

        var draft = ProbeFixtures.Draft(await ProbeColumnAsync(int.MaxValue, values));

        Assert.InRange(draft.Attributes[0].Description!.Length, 2_000, 3_072);
        Assert.InRange(draft.Provenance!.Notes!.Length, 1, 512);
    }

    [Fact]
    public async Task Probe_WhenRunTwiceOverOmittingInput_ThenBytesAndDiagnosticsRepeat()
    {
        var first = await ProbeFixtures.ProbeCsvAsync(MixedCsv);
        var second = await ProbeFixtures.ProbeCsvAsync(MixedCsv);

        Assert.Equal(ProbeFixtures.Toml(first), ProbeFixtures.Toml(second));
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    // A wide session over one column whose enumeration requests cancellation as it finishes.
    private sealed class CancelOnCloseSession(CancellationTokenSource cancellation, string[] values) : IWideSourceSession
    {
        public SourceShape Shape => SourceShape.Wide;

        public ValueTask<SourceSchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SourceSchema(1, ["note"]));

        public async IAsyncEnumerable<ObjectRecord> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            try
            {
                for (var i = 0; i < values.Length; i++)
                {
                    yield return new ObjectRecord(i.ToString(System.Globalization.CultureInfo.InvariantCulture), [values[i]]);
                }
            }
            finally
            {
                await cancellation.CancelAsync();
            }
        }
    }
}
