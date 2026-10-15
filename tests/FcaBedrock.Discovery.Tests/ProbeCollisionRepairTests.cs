using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery.Tests;

/// <summary>
/// Formal-attribute name collisions between attributes (§7.1, D-108). For names <c>N</c> and
/// <c>N-r</c>, a shorter value <c>v</c> and a longer value <c>w</c> render the same name exactly
/// when <c>v = r + "-" + w</c>, so the pair's flag records whether any shorter value ever
/// started with <c>r + "-"</c>, or the shorter history is unknown because the pair appeared
/// after the shorter domain truncated.
/// <list type="bullet">
/// <item>flag false: no repair, truncated or not;</item>
/// <item>flag true and both complete: repair only an actual collision of saved categories;</item>
/// <item>flag true and either truncated: repair, because a recovered value could collide.</item>
/// </list>
/// The later attribute of every witness is renamed <c>probe_&lt;ordinal&gt;_&lt;k&gt;</c>, chosen
/// together, and source selectors never change. Each repaired draft is also converted, which is
/// where a collision would fail.
/// </summary>
public sealed class ProbeCollisionRepairTests
{
    private static ProbeOptions Limit(int limit) => ProbeOptions.Create(valueRetentionLimit: limit);

    private static IReadOnlyList<string> Names(SpecDocument draft) => [.. draft.Attributes.Select(a => a.Name!)];

    private static async Task<SpecDocument> WideAsync(string csv, int limit = 100_000) =>
        ProbeFixtures.Draft(await ProbeFixtures.ProbeCsvAsync(csv, options: Limit(limit)));

    private static async Task<SpecDocument> TripleAsync(string csv, int limit = 100_000) =>
        ProbeFixtures.Draft(await TripleProbeFixtures.ProbeTripleCsvAsync(csv, options: Limit(limit)));

    private static async Task<IReadOnlyList<string>> WideNamesAsync(int limit, string[] header, params string?[][] rows) =>
        Names(ProbeFixtures.Draft(await Prober.ProbeAsync(
            ProbeFixtures.Fake(new SourceSchema(header.Length, header), rows), ProbeFixtures.WideSettings(), Limit(limit))));

    private static async Task<IReadOnlyList<string>> TripleNamesAsync(
        int limit, params (string? Subject, string? Predicate, string? Value)[] rows) =>
        Names(ProbeFixtures.Draft(await Prober.ProbeTripleAsync(
            TripleProbeFixtures.Fake(rows), TripleProbeFixtures.TripleSettings(), options: Limit(limit))));

    // Each object's crosses as column positions: the incidence, independent of column names.
    private static IReadOnlyList<int[]> Incidence(ConvertedDraft converted) =>
        [.. converted.Objects.Select(o => o.Crosses.Select(name => IndexOf(converted.Columns, name)).ToArray())];

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i] == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"No column {name}.");
    }

    // ---- complete pairs: only an actual collision ------------------------------------------

    [Fact]
    public async Task Probe_WhenTwoCompleteDomainsRenderTheSameName_ThenTheLaterAttributeIsRepaired()
    {
        // a's b-z and a-b's z both render a-b-z.
        const string csv = "a,a-b\nb-z,z\n";

        var result = await ProbeFixtures.ProbeCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["a", "probe_1_0"], Names(draft));
        Assert.Equal("a-b", ((ColumnSourceSection)draft.Attributes[1].Source!).Name);
        Assert.Equal(["z"], draft.Attributes[1].DeclaredDomain);
        Assert.Equal(
            "The probe adjusted 1 attribute name(s) (e.g. probe_1_0); source selectors are unchanged (§7.1).",
            Assert.Single(result.Diagnostics).Message);

        var converted = await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(csv));
        Assert.Equal(["a-b-z", "probe_1_0-z"], converted.Columns);
        Assert.Equal([["a-b-z", "probe_1_0-z"]], converted.Objects.Select(o => o.Crosses));
    }

    [Fact]
    public async Task Probe_WhenTheShorterNameBelongsToTheLaterAttribute_ThenTheShorterIsRepaired()
    {
        // Later means source ordinal, not name length.
        const string csv = "a-b,a\nz,b-z\n";

        var draft = await WideAsync(csv);

        Assert.Equal(["a-b", "probe_1_0"], Names(draft));
        Assert.Equal("a", ((ColumnSourceSection)draft.Attributes[1].Source!).Name);
        await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Probe_WhenACompleteFlaggedPairDoesNotCollide_ThenNothingIsRepaired()
    {
        // a's b-q starts with "b-", but a-b has no category q.
        Assert.Equal(["a", "a-b"], Names(await WideAsync("a,a-b\nb-q,z\nx,z\n")));
    }

    [Fact]
    public async Task Probe_WhenNamesShareOnlyACharacterPrefix_ThenTheyFormNoPair()
    {
        // `ab` does not start with "a-", so no a value can render an ab name.
        var result = await ProbeFixtures.ProbeCsvAsync("a,ab\nb-z,-z\n");

        Assert.Equal(["a", "ab"], Names(ProbeFixtures.Draft(result)));
        Assert.Empty(result.Diagnostics);
    }

    // ---- truncation: the flag decides ------------------------------------------------------

    [Fact]
    public async Task Probe_WhenNoShorterValueStartsWithTheRemainder_ThenTruncationRepairsNothingAndSchemasMatch()
    {
        // y is discarded at limit 1, but it was checked: neither x nor y starts with "b-".
        const string csv = "a,a-b\nx,z\ny,z\n";

        var truncated = await WideAsync(csv, limit: 1);
        var complete = await WideAsync(csv);

        Assert.Equal(["a", "a-b"], Names(truncated));
        Assert.Equal(["a", "a-b"], Names(complete));

        // The names and the recovered schema are the same at both limits. The output
        // fingerprints differ already, because the recovering `include` policy is an output input.
        var fromTruncated = await ProbeConversion.ConvertAsync(truncated, ProbeFixtures.Bytes(csv));
        var fromComplete = await ProbeConversion.ConvertAsync(complete, ProbeFixtures.Bytes(csv));
        Assert.Equal(fromComplete.Columns, fromTruncated.Columns);
        Assert.Equal(Incidence(fromComplete), Incidence(fromTruncated));
        Assert.Equal(fromComplete.Fingerprints.SchemaFingerprint, fromTruncated.Fingerprints.SchemaFingerprint);
        Assert.NotEqual(fromComplete.Fingerprints.DatOutputFingerprint, fromTruncated.Fingerprints.DatOutputFingerprint);
    }

    [Fact]
    public async Task Probe_WhenAFlaggedPairHasATruncatedEndpoint_ThenOnlyThatProbeRepairsAndIncidenceMaps()
    {
        // At limit 2 both domains are complete and do not collide; at limit 1 `a` is truncated,
        // its retained b-q starts with "b-", and a recovered value could collide.
        const string csv = "a,a-b\nb-q,z\nx,z\n";

        var complete = await WideAsync(csv, limit: 2);
        var truncated = await WideAsync(csv, limit: 1);

        Assert.Equal(["a", "a-b"], Names(complete));
        Assert.Equal(["a", "probe_1_0"], Names(truncated));
        Assert.Equal("a-b", ((ColumnSourceSection)truncated.Attributes[1].Source!).Name);

        // The categories, their order, the objects and the incidence are the same under the
        // renamed attribute; only names and the schema identity built from them change.
        var fromComplete = await ProbeConversion.ConvertAsync(complete, ProbeFixtures.Bytes(csv));
        var fromTruncated = await ProbeConversion.ConvertAsync(truncated, ProbeFixtures.Bytes(csv));
        Assert.Equal(["a-b-q", "a-x", "a-b-z"], fromComplete.Columns);
        Assert.Equal(["a-b-q", "a-x", "probe_1_0-z"], fromTruncated.Columns);
        Assert.Equal(fromComplete.Objects.Select(o => o.Name), fromTruncated.Objects.Select(o => o.Name));
        Assert.Equal(Incidence(fromComplete), Incidence(fromTruncated));
        Assert.NotEqual(fromComplete.Fingerprints.SchemaFingerprint, fromTruncated.Fingerprints.SchemaFingerprint);
    }

    [Fact]
    public async Task Probe_WhenThreeNamesNest_ThenWitnessesAreSelectedTogether()
    {
        // a-b's c-z collides with a-b-c's z at every limit, so a-b-c is repaired at both. Only the
        // truncated probe also flags a/a-b. Selecting one repair at a time would instead repair
        // a-b first and then find nothing left to repair on a-b-c.
        const string csv = "a,a-b,a-b-c\nb-q,c-z,z\nx,c-z,z\n";

        var complete = await WideAsync(csv);
        var truncated = await WideAsync(csv, limit: 1);

        Assert.Equal(["a", "a-b", "probe_2_0"], Names(complete));
        Assert.Equal(["a", "probe_1_0", "probe_2_0"], Names(truncated));
        await ProbeConversion.ConvertAsync(complete, ProbeFixtures.Bytes(csv));
        await ProbeConversion.ConvertAsync(truncated, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Probe_WhenOnlyTheLongerDomainIsTruncated_ThenTheCompleteShorterHistoryDecides()
    {
        string[] header = ["a", "a-b"];

        Assert.Equal(["a", "a-b"], await WideNamesAsync(1, header, ["x", "z"], ["x", "w"]));
        Assert.Equal(["a", "probe_1_0"], await WideNamesAsync(1, header, ["b-x", "z"], ["b-x", "w"]));
    }

    public static TheoryData<string, string?[]> ShorterWitnesses() => new()
    {
        { "first value over the limit", ["x", "b-q"] },
        { "later discarded value", ["x", "y", "b-q"] },
        { "repeated discarded value", ["x", "y", "b-q", "y", "b-q"] },
        { "retained value", ["b-q", "x"] },
        { "value equal to the remainder plus a hyphen", ["x", "b-"] },
        { "remainder after the hyphen is the missing token", ["x", "b-?"] },
    };

    [Theory]
    [MemberData(nameof(ShorterWitnesses))]
    public async Task Probe_WhenATruncatedShorterValueStartsWithTheRemainder_ThenTheLaterAttributeIsRepaired(
        string label, string?[] values)
    {
        var rows = values.Select(v => new[] { v, "z" }).ToArray();

        Assert.True(
            (await WideNamesAsync(1, ["a", "a-b"], rows)).SequenceEqual(["a", "probe_1_0"]),
            label);
    }

    [Fact]
    public async Task Probe_WhenAMultilineShorterValueStartsWithTheRemainder_ThenItFlagsThePairDespiteItsOmission()
    {
        // `a` is complete and all-unsafe, so it saves a fixed empty domain; its value still counts
        // for the flag, and a-b is truncated, so a-b is repaired. No fixed-empty exemption.
        const string csv = "a,a-b\n\"b-\nq\",z\n,w\n";

        var draft = await WideAsync(csv, limit: 1);

        Assert.Equal(["a", "probe_1_0"], Names(draft));
        Assert.Empty(draft.Attributes[0].DeclaredDomain!);
        var converted = await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(csv));
        Assert.Equal(["probe_1_0-z", "probe_1_0-w"], converted.Columns);
    }

    [Fact]
    public async Task Probe_WhenTheShorterAttributeIsAllMissing_ThenTheTruncatedLongerKeepsItsName()
    {
        Assert.Equal(["a", "a-b"], await WideNamesAsync(1, ["a", "a-b"], [null, "z"], [null, "w"]));
    }

    [Fact]
    public async Task Probe_WhenOneShorterAttributeFlagsTwoLongerOnes_ThenBothAreRepaired()
    {
        Assert.Equal(
            ["a", "probe_1_0", "probe_2_0"],
            await WideNamesAsync(2, ["a", "a-b", "a-c"], ["b-1", "z", "z"], ["c-2", "z", "z"], ["x", "z", "z"]));
    }

    // ---- the repair names ------------------------------------------------------------------

    [Fact]
    public async Task Probe_WhenARepairNameIsAlreadyABaselineStem_ThenTheNextOneIsChosen()
    {
        // probe_1_0 is a baseline name and probe_1_1 the stem of probe_1_1-x, so attribute 1
        // takes probe_1_2. The candidates depend on the baseline names only.
        var names = await WideNamesAsync(
            100, ["a", "a-b", "probe_1_0", "probe_1_1-x"], ["b-z", "z", "v", "v"]);

        Assert.Equal(["a", "probe_1_2", "probe_1_0", "probe_1_1-x"], names);
    }

    [Fact]
    public async Task Probe_WhenADisambiguatedNameFormsAPair_ThenBothAdjustmentsAreReportedOnce()
    {
        // The duplicate second `a` becomes a#1, which a#1-b extends; then b-z collides.
        const string csv = "a,a,a#1-b\nq,b-z,z\n";

        var result = await ProbeFixtures.ProbeCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["a", "a#1", "probe_2_0"], Names(draft));
        Assert.Equal("a#1-b", ((ColumnSourceSection)draft.Attributes[2].Source!).Name);
        Assert.Equal(1, ((ColumnSourceSection)draft.Attributes[1].Source!).Index);
        Assert.Equal(
            "The probe adjusted 2 attribute name(s) (e.g. a#1, probe_2_0); source selectors are unchanged (§7.1).",
            Assert.Single(result.Diagnostics).Message);
        await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Probe_WhenAnUnusableHeaderFallbackFormsAPair_ThenTheLaterAttributeIsRepaired()
    {
        // The quoted header is unusable, so it becomes column_0, which the real header
        // column_0-b extends; b-z then collides with z.
        const string csv = "\"x\"\"y\",column_0-b\nb-z,z\n";

        var result = await ProbeFixtures.ProbeCsvAsync(csv);
        var draft = ProbeFixtures.Draft(result);

        Assert.Equal(["column_0", "probe_1_0"], Names(draft));
        Assert.Equal(0, ((ColumnSourceSection)draft.Attributes[0].Source!).Index);
        Assert.Equal("column_0-b", ((ColumnSourceSection)draft.Attributes[1].Source!).Name);
        Assert.Contains(
            "adjusted 2 attribute name(s) (e.g. column_0, probe_1_0)", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
        await ProbeConversion.ConvertAsync(draft, ProbeFixtures.Bytes(csv));
    }

    [Fact]
    public async Task Probe_WhenAPairIsHarmless_ThenTheDraftBytesAreTheOrdinaryOnes()
    {
        // A related pair with no witness changes nothing in the saved draft.
        const string expected =
            """
            [spec]
            version = 1
            description = "Draft spec generated by probe."

            [provenance]
            notes = "probe: per-attribute retention limit 100000; truncated attributes: 0."

            [binding]
            shape = "wide"
            encoding = "utf-8"
            delimiter = ","
            quote_char = "\""
            has_header = true
            locale = "invariant"
            missing_token = "?"

            [[attribute]]
            name = "a"
            source = { kind = "column", name = "a", value_type = "string" }
            declared_domain = ["x", "y"]
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }

            [[attribute]]
            name = "a-b"
            source = { kind = "column", name = "a-b", value_type = "string" }
            declared_domain = ["z"]
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }

            """;

        var result = await ProbeFixtures.ProbeCsvAsync("a,a-b\nx,z\ny,z\n");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(expected.ReplaceLineEndings("\n"), ProbeFixtures.Toml(result));
    }

    // ---- triples: late pairs and moving fallbacks ------------------------------------------

    [Fact]
    public async Task ProbeTriple_WhenAPairAppearsAfterTheShorterDomainTruncated_ThenItsUnknownHistoryForcesARepair()
    {
        // At limit 1, `a` discards y before a-b exists, so the pair's history is unknown. At
        // limit 2 the complete history shows no b- value.
        const string csv = "s1,a,x\ns2,a,y\ns3,a-b,z\n";

        var truncated = await TripleAsync(csv, limit: 1);
        var complete = await TripleAsync(csv, limit: 2);

        Assert.Equal(["a", "probe_1_0"], Names(truncated));
        Assert.Equal(["a", "a-b"], Names(complete));
        Assert.Equal("a-b", TripleProbeFixtures.SourceOf(truncated, "probe_1_0").Name);

        var fromTruncated = await ProbeConversion.ConvertAsync(truncated, TripleProbeFixtures.Bytes(csv));
        var fromComplete = await ProbeConversion.ConvertAsync(complete, TripleProbeFixtures.Bytes(csv));
        Assert.Equal(["a-x", "a-y", "probe_1_0-z"], fromTruncated.Columns);
        Assert.Equal(Incidence(fromComplete), Incidence(fromTruncated));
    }

    [Fact]
    public async Task ProbeTriple_WhenTheEarlierCompleteHistoryIsChecked_ThenOnlyAnActualCollisionRepairs()
    {
        Assert.Equal(["a", "probe_1_0"], Names(await TripleAsync("s1,a,b-q\ns2,a-b,q\n")));
        Assert.Equal(["a", "a-b"], Names(await TripleAsync("s1,a,b-q\ns2,a-b,z\n")));
    }

    [Fact]
    public async Task ProbeTriple_WhenOnlyTheLongerDomainTruncatedFirst_ThenTheLaterShorterIsNotForced()
    {
        Assert.Equal(["a-b", "a"], await TripleNamesAsync(1, ("s1", "a-b", "z"), ("s2", "a-b", "w"), ("s3", "a", "x")));
    }

    [Fact]
    public async Task ProbeTriple_WhenAFallbackMovesAfterItsDomainTruncated_ThenTheNewPairIsForced()
    {
        // `bad"` is unusable, so it is named predicate_0 and truncates at y. predicate_0#0-b
        // arrives, then a real predicate_0 moves the fallback to predicate_0#0, which forms a
        // pair with predicate_0#0-b after the truncation.
        var rows = new (string?, string?, string?)[]
        {
            ("s1", "bad\"", "x"), ("s2", "bad\"", "y"), ("s3", "predicate_0#0-b", "z"), ("s4", "predicate_0", "w"),
        };

        Assert.Equal(["predicate_0#0", "probe_1_0", "predicate_0"], await TripleNamesAsync(1, rows));
        Assert.Equal(["predicate_0#0", "predicate_0#0-b", "predicate_0"], await TripleNamesAsync(2, rows));

        var result = await Prober.ProbeTripleAsync(
            TripleProbeFixtures.Fake(rows), TripleProbeFixtures.TripleSettings(), options: Limit(1));
        Assert.Contains(
            "adjusted 2 attribute name(s) (e.g. predicate_0#0, probe_1_0)",
            result.Diagnostics.Single(d => d.Code == DiagnosticCode.ProbeAttributeNameAdjusted).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeTriple_WhenAFallbackMovesBeforeItsDomainTruncates_ThenItsLaterDiscardsDecide()
    {
        // The pair exists from s3 on, before `bad"` truncates at s4, so only discarded values
        // starting with "b-" can flag it.
        Assert.Equal(
            ["predicate_0#0", "predicate_0#0-b", "predicate_0"],
            await TripleNamesAsync(
                1, ("s1", "bad\"", "x"), ("s2", "predicate_0#0-b", "z"), ("s3", "predicate_0", "w"), ("s4", "bad\"", "y")));
        Assert.Equal(
            ["predicate_0#0", "probe_1_0", "predicate_0"],
            await TripleNamesAsync(
                1, ("s1", "bad\"", "x"), ("s2", "predicate_0#0-b", "z"), ("s3", "predicate_0", "w"), ("s4", "bad\"", "b-q")));
    }

    [Fact]
    public async Task ProbeTriple_WhenAHitBelongsToAPairThatNoLongerExists_ThenItRepairsNothing()
    {
        // `bad"` is predicate_0 when it discards b-q, which reaches predicate_0-b. The real
        // predicate_0 then moves it to predicate_0#0, so that pair is gone and the hit is stale.
        var rows = new (string?, string?, string?)[]
        {
            ("s1", "bad\"", "x"), ("s2", "predicate_0-b", "z"), ("s3", "bad\"", "b-q"), ("s4", "predicate_0", "w"),
        };

        Assert.Equal(["predicate_0#0", "predicate_0-b", "predicate_0"], await TripleNamesAsync(1, rows));
    }

    // ---- cancellation inside the name work -------------------------------------------------

    [Theory]
    [InlineData(SourceShape.Wide)]
    [InlineData(SourceShape.Triple)]
    public async Task Probe_WhenCancellationArrivesBeforeARecordsNameWork_ThenThatWorkCancelsBeforeTheGuardBreach(
        SourceShape shape)
    {
        // Without cancellation, the second record breaches the one-value guard.
        var uncanceled = await ProbeTwoRecordsAsync(shape, () => { }, CancellationToken.None);
        Assert.Equal(DiagnosticCode.ProbeLimitExceeded, Assert.Single(uncanceled.Diagnostics).Code);

        // The source requests cancellation after its own check for the second record, so the
        // first code that can see it is the probe's name work on that record. That work must
        // propagate it: no document, and no guard Error in its place (D-112).
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProbeTwoRecordsAsync(shape, cancellation.Cancel, cancellation.Token));
    }

    // Two records. In the second, the probe first walks the discarded b-q from `a` toward `a-b`
    // (wide) or names the new predicate `a-b` (triple), and then its value z breaches the
    // one-value guard. `beforeSecond` runs as the source produces the second record.
    private static Task<Diagnosed<SpecDocument>> ProbeTwoRecordsAsync(
        SourceShape shape, Action beforeSecond, CancellationToken cancellationToken)
    {
        var produced = 0;
        void BeforeEach()
        {
            if (++produced == 2)
            {
                beforeSecond();
            }
        }

        var options = ProbeOptions.Create(valueRetentionLimit: 1, maxTotalRetainedValues: 1);
        return shape == SourceShape.Wide
            ? Prober.ProbeAsync(
                new ProbeFixtures.FakeWideSession(
                    new SourceSchema(3, ["a", "a-b", "c"]),
                    ProbeFixtures.Records(["x", null, null], ["b-q", null, "z"]),
                    beforeEachRecord: BeforeEach),
                ProbeFixtures.WideSettings(),
                options,
                cancellationToken).AsTask()
            : Prober.ProbeTripleAsync(
                new TripleProbeFixtures.FakeTripleSession(
                    new SourceSchema(3),
                    TripleProbeFixtures.Rows(("s1", "a", "x"), ("s2", "a-b", "z")),
                    beforeEachRow: BeforeEach),
                TripleProbeFixtures.TripleSettings(),
                options: options,
                cancellationToken: cancellationToken).AsTask();
    }
}
