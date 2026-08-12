using System.Text;
using FcaBedrock.Conversion;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Core.Spec;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The <c>calibrate</c> vertical (D-122 part 10; spec §7/§13/§14), end to end through argv.
/// <para>
/// The freeze <em>mappings</em> are library semantics and are already locked by
/// <c>SpecFreezerTests</c>; what is tested here is the command's own orchestration — which
/// outcome reaches the written file, which stream carries which diagnostic and how often, what
/// the publication does, and what the filesystem holds afterwards. Where a byte oracle is
/// needed it is produced by running the <b>library</b> route in the test, never by reading the
/// handler's output back as its own expectation.
/// </para>
/// </summary>
public sealed class CalibrateCommandTests
{
    // ---- the library oracle -----------------------------------------------------------------
    //
    // The freeze write flow, executed here from the public library surface alone: resolve,
    // calibrate, freeze, re-resolve, plan natively, compute, store the three fields, serialize.
    // Nothing below calls a CLI helper, so a handler that drifted from this route would fail
    // rather than agree with itself.

    private static async Task<string> FrozenViaLibraryAsync(string specPath, string dataPath)
    {
        var read = SpecReader.Read(await File.ReadAllTextAsync(specPath, TestContext.Current.CancellationToken), specPath);
        Assert.True(read.TryGetValue(out var document));

        var settings = SpecResolver.ResolveReadSettings(document);
        Assert.True(settings.TryGetValue(out var readSettings));

        var session = new WideCsvSession(() => File.OpenRead(dataPath), readSettings);
        var schema = await session.GetSchemaAsync(TestContext.Current.CancellationToken);

        var resolvedResult = SpecResolver.Resolve(document, schema);
        Assert.True(resolvedResult.TryGetValue(out var resolved));

        CalibratedSpec calibrated;
        if (CalibratedSpec.RequiresData(resolved.Resolved.Spec))
        {
            var outcome = await Calibrator.CalibrateAsync(
                resolved.Resolved, session.Bind(resolved.Resolved), TestContext.Current.CancellationToken);
            Assert.True(outcome.TryGetValue(out var value));
            calibrated = value;
        }
        else
        {
            calibrated = CalibratedSpec.FromFullyDeclared(resolved.Resolved);
        }

        var frozen = SpecFreezer.Freeze(resolved, calibrated);

        var reResolved = SpecResolver.Resolve(frozen, calibrated.Schema);
        Assert.True(reResolved.TryGetValue(out var reResolvedDocument));

        var planned = ConversionPlanner.Plan(
            CalibratedSpec.FromFullyDeclared(reResolvedDocument.Resolved), LabelStyle.Native);
        Assert.True(planned.TryGetValue(out var plan));

        var computed = SpecFingerprints.ComputeNative(reResolvedDocument, plan);
        return SpecWriter.Write(frozen with
        {
            Spec = frozen.Spec! with
            {
                SchemaFingerprint = computed.SchemaFingerprint,
                CxtOutputFingerprint = computed.CxtOutputFingerprint,
                DatOutputFingerprint = computed.DatOutputFingerprint,
            },
        });
    }

    // ---- shared helpers ---------------------------------------------------------------------

    private static bool Residue(string path) =>
        Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal);

    // Preflight only observes; a refused run must not have created, renamed, or removed anything.
    private static void AssertNoMutation(CliTestHarness harness) =>
        Assert.DoesNotContain(
            harness.PublicationFiles.Operations,
            operation => operation.StartsWith("CreateNew:", StringComparison.Ordinal)
                || operation.StartsWith("Confidential:", StringComparison.Ordinal)
                || operation.StartsWith("Move:", StringComparison.Ordinal)
                || operation.StartsWith("Delete:", StringComparison.Ordinal));

    private static Dictionary<string, byte[]> Snapshot(TempDirectory temp) =>
        Directory.GetFiles(temp.Path).ToDictionary(
            path => Path.GetFileName(path), File.ReadAllBytes, StringComparer.Ordinal);

    private static async Task<string> ReadCommittedAsync(string path) =>
        await File.ReadAllTextAsync(path, new UTF8Encoding(false));

    private static SpecDocument Parse(string text)
    {
        var read = SpecReader.Read(text);
        Assert.True(read.TryGetValue(out var document));
        return document;
    }

    private static string[] Lines(string stream) => stream.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // ---- the four canonical freeze mappings (§7/D-122 part 10) --------------------------------

    public static TheoryData<string, string> OutcomeKinds() => new()
    {
        { "cuts", CliFixtures.CalibrateCutsSpec },
        { "observed_domain", CliFixtures.CalibrateObservedSpec },
        { "include_additions", CliFixtures.CalibrateIncludeSpec },
        { "passthrough_bins", CliFixtures.CalibratePassthroughSpec },
    };

    [Theory]
    [MemberData(nameof(OutcomeKinds))]
    public async Task Calibrate_WhenEachOutcomeKindIsFrozen_ThenTheCanonicalMappingIsWritten(
        string kind, string specText)
    {
        // One ISOLATED attribute per row, because every discovery-class mode warns whenever it
        // executes: a combined fixture could not produce a single-warning row at all.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", specText);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);

        var text = await ReadCommittedAsync(target);
        switch (kind)
        {
            case "cuts":
                // A successful min_max calibration is genuinely silent — no Warning, no Info.
                Assert.Equal(string.Empty, harness.StdErr);
                Assert.Contains("kind = \"manual_cuts\"", text, StringComparison.Ordinal);
                Assert.Contains("cuts = [", text, StringComparison.Ordinal);
                Assert.DoesNotContain("ends", text, StringComparison.Ordinal);
                break;

            case "observed_domain":
                Assert.Single(Lines(harness.StdErr));
                Assert.Contains("warning ObservedDomainUsed", harness.StdErr, StringComparison.Ordinal);
                Assert.Contains("declared_domain = [\"red\", \"green\", \"blue\"]", text, StringComparison.Ordinal);
                break;

            case "include_additions":
                Assert.Single(Lines(harness.StdErr));
                Assert.Contains("warning UnknownValuePolicyInclude", harness.StdErr, StringComparison.Ordinal);
                Assert.Contains("declared_domain = [\"red\", \"green\", \"blue\"]", text, StringComparison.Ordinal);
                Assert.Contains("unknown_value_policy = \"warn\"", text, StringComparison.Ordinal);
                break;

            default:
                Assert.Single(Lines(harness.StdErr));
                Assert.Contains(
                    "warning ValueGroupsPassthroughDataDependent", harness.StdErr, StringComparison.Ordinal);

                var groups = Assert.IsType<ValueGroupsDiscretizerSection>(
                    Assert.Single(Parse(text).Attributes).Discretizer);
                Assert.Equal(ValueGroupsUnmatched.Skip, groups.Unmatched);
                Assert.Collection(
                    groups.Groups!,
                    group => Assert.Equal("G", group.Label),
                    group => Assert.Equal("green", group.Label),
                    group => Assert.Equal("blue", group.Label));
                break;
        }
    }

    // ---- legitimately empty outcomes (D-122 part 15) ------------------------------------------

    public static TheoryData<string, string, string> EmptyOutcomes() => new()
    {
        { "observed", CliFixtures.CalibrateEmptyObservedSpec, "ObservedDomainUsed" },
        { "include", CliFixtures.CalibrateEmptyIncludeSpec, "UnknownValuePolicyInclude" },
        { "passthrough", CliFixtures.CalibrateEmptyPassthroughSpec, "ValueGroupsPassthroughDataDependent" },
    };

    [Theory]
    [MemberData(nameof(EmptyOutcomes))]
    public async Task Calibrate_WhenTheOutcomeIsEmpty_ThenTheEmptyRepresentationIsAuthored(
        string kind, string specText, string code)
    {
        // The data-dependence exists regardless of the count, so each mode still warns — and the
        // empty result still has to be written as an explicit representation, never re-omitted.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", specText);
        var data = temp.Write("data.csv", CliFixtures.CalibrateEmptyOutcomesData);
        var target = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Single(Lines(harness.StdErr));
        Assert.Contains($"warning {code}", harness.StdErr, StringComparison.Ordinal);

        var text = await ReadCommittedAsync(target);
        var attribute = Assert.Single(Parse(text).Attributes);
        switch (kind)
        {
            case "observed":
                Assert.Empty(attribute.DeclaredDomain!);
                Assert.Equal(MissingPolicy.AsAttribute, attribute.MissingPolicy);
                break;

            case "include":
                Assert.Equal(["k"], attribute.DeclaredDomain!);
                Assert.Equal(UnknownValuePolicy.Warn, attribute.UnknownValuePolicy);
                break;

            default:
                var groups = Assert.IsType<ValueGroupsDiscretizerSection>(attribute.Discretizer);
                Assert.Equal(ValueGroupsUnmatched.Skip, groups.Unmatched);
                Assert.Equal("G", Assert.Single(groups.Groups!).Label);
                break;
        }
    }

    // ---- the authored-empty 4 × 2 matrix (D-122 part 15) --------------------------------------

    public static TheoryData<string, string> AuthoredEmptyMatrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var unknown in new[] { "warn", "skip", "fail", "include" })
        {
            foreach (var missing in new[] { "skip", "as_attribute" })
            {
                data.Add(unknown, missing);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AuthoredEmptyMatrix))]
    public async Task Calibrate_WhenTheAuthoredEmptyDomainCombinesWithPolicies_ThenTheFrozenSpecIsFullyFrozenAndPreserved(
        string unknown, string missing)
    {
        // An authored [] is COMPLETE, so warn/skip/fail read no rows at all and are
        // indistinguishable at calibrate time; only include is data-dependent, and it folds its
        // observations into the domain and rewrites itself to warn.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateAuthoredEmptySpec(unknown, missing));
        var data = temp.Write("data.csv", CliFixtures.CalibrateAuthoredEmptyData);
        var target = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target);

        var isInclude = string.Equals(unknown, "include", StringComparison.Ordinal);
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);

        if (isInclude)
        {
            Assert.Single(Lines(harness.StdErr));
            Assert.Contains("warning UnknownValuePolicyInclude", harness.StdErr, StringComparison.Ordinal);
        }
        else if (string.Equals(missing, "skip", StringComparison.Ordinal))
        {
            // [] plus skip plans zero columns, so the successful plan warns exactly once — the
            // replan over the frozen document produces the same warning and is suppressed.
            Assert.Single(Lines(harness.StdErr));
            Assert.Contains("warning NoFormalAttributes", harness.StdErr, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(string.Empty, harness.StdErr);
        }

        if (!isInclude)
        {
            // No calibration pass at all: the run opens exactly what `validate SPEC DATA` opens,
            // which acquires the schema and reads no row.
            var validate = new CliTestHarness();
            Assert.Equal(0, await validate.RunAsync("validate", spec, data));
            Assert.Equal(validate.Opened, harness.Opened);
        }

        var text = await ReadCommittedAsync(target);
        var attribute = Assert.Single(Parse(text).Attributes);

        Assert.Equal(
            string.Equals(missing, "as_attribute", StringComparison.Ordinal)
                ? MissingPolicy.AsAttribute
                : MissingPolicy.Skip,
            attribute.MissingPolicy);

        if (isInclude)
        {
            Assert.Equal(["red"], attribute.DeclaredDomain!);
            Assert.Equal(UnknownValuePolicy.Warn, attribute.UnknownValuePolicy);
        }
        else
        {
            Assert.Empty(attribute.DeclaredDomain!);
            Assert.Equal(
                unknown switch
                {
                    "warn" => UnknownValuePolicy.Warn,
                    "skip" => UnknownValuePolicy.Skip,
                    _ => UnknownValuePolicy.Fail,
                },
                attribute.UnknownValuePolicy);
        }

        // The whole point of the freeze: the written spec is no longer data-dependent.
        var reResolved = SpecResolver.Resolve(Parse(text));
        Assert.True(reResolved.TryGetValue(out var resolved));
        Assert.False(CalibratedSpec.RequiresData(resolved.Resolved.Spec));
    }

    [Fact]
    public async Task Calibrate_WhenTheAuthoredEmptyDomainIsAsAttribute_ThenTheMissingColumnSurvivesTheFreeze()
    {
        // Matrix row 4 — ("skip", "as_attribute"). skip is the only unknown-value policy that
        // isolates the missing cross: under warn the non-missing undeclared value would add a
        // Warning, and under fail it would abort the very conversion this proves.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateAuthoredEmptySpec("skip", "as_attribute"));
        var data = temp.Write("data.csv", CliFixtures.CalibrateAuthoredEmptyData);
        var frozen = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", frozen);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);

        var converted = new CliTestHarness();
        var convertExit = await converted.RunAsync(
            "convert", frozen, data, "--out", temp.Resolve("out"), "--format", "cxt", "--no-manifest");

        Assert.Equal(0, convertExit);
        Assert.Equal(string.Empty, converted.StdOut);

        // The missing object crosses the sole column and the non-missing one crosses nothing —
        // a silent no-cross in the unknown-VALUE sense, but still an empty ROW, which the
        // separate object-level aggregate reports (§16.4, D-058). Exactly one line.
        Assert.Equal(
            "warning ObjectHasNoCrosses: 1 emitted object(s) cross no formal attribute (e.g. 0);"
                + " their rows are empty (§16.4).\n",
            converted.StdErr);

        var cxt = (await File.ReadAllTextAsync(temp.Resolve("out.cxt"))).Split('\n');
        Assert.Equal("2", cxt[2].Trim());
        Assert.Equal("1", cxt[3].Trim());
        Assert.Contains("colour-missing", cxt);
    }

    // ---- stored fingerprints (§14) ------------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenTheOutputIsWritten_ThenAllThreeNativeFingerprintsAreStoredAndVerifySilently()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var frozen = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", frozen);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);

        var section = Parse(await ReadCommittedAsync(frozen)).Spec!;
        foreach (var stored in new[]
                 {
                     section.SchemaFingerprint, section.CxtOutputFingerprint, section.DatOutputFingerprint,
                 })
        {
            Assert.Matches("^sha256:[0-9a-f]{64}$", stored!);
        }

        // The independent confirmation that all three are the frozen spec's own native values.
        var report = new CliTestHarness();
        Assert.Equal(0, await report.RunAsync("fingerprint", frozen, data));
        Assert.Equal(string.Empty, report.StdErr);
        Assert.Equal(3, Lines(report.StdOut).Count(line => line.EndsWith("stored=match", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Calibrate_WhenTheInputStoresStaleHashes_ThenTheyWarnOnStderrAndAreCorrectedInTheOutput()
    {
        // The fixture calibrates silently and can produce no resolve diagnostic, so preparation
        // contributes nothing and the three stale warnings are the complete stderr — in the fixed
        // schema → cxt → dat field order VerifyStored appends them.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateStaleHashesSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var frozen = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", frozen);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);

        // Each line carries the root's `file` location prefix; the codes appear in the fixed
        // schema → cxt → dat order VerifyStored appends them.
        var lines = Lines(harness.StdErr);
        Assert.Equal(3, lines.Length);
        Assert.Contains("warning SchemaFingerprintStale", lines[0], StringComparison.Ordinal);
        Assert.Contains("warning CxtOutputFingerprintStale", lines[1], StringComparison.Ordinal);
        Assert.Contains("warning DatOutputFingerprintStale", lines[2], StringComparison.Ordinal);

        var authored = Parse(CliFixtures.CalibrateStaleHashesSpec).Spec!;
        var written = Parse(await ReadCommittedAsync(frozen)).Spec!;
        Assert.NotEqual(authored.SchemaFingerprint, written.SchemaFingerprint);
        Assert.NotEqual(authored.CxtOutputFingerprint, written.CxtOutputFingerprint);
        Assert.NotEqual(authored.DatOutputFingerprint, written.DatOutputFingerprint);

        var report = new CliTestHarness();
        Assert.Equal(0, await report.RunAsync("fingerprint", frozen, data));
        Assert.Equal(string.Empty, report.StdErr);
        Assert.Equal(3, Lines(report.StdOut).Count(line => line.EndsWith("stored=match", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Calibrate_WhenRerunOnItsOwnOutput_ThenTheBytesAreIdentical()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var first = temp.Resolve("first.toml");
        var second = temp.Resolve("second.toml");

        var one = new CliTestHarness();
        Assert.Equal(0, await one.RunAsync("calibrate", spec, data, "--out", first));
        Assert.Equal(string.Empty, one.StdErr);

        var two = new CliTestHarness();
        Assert.Equal(0, await two.RunAsync("calibrate", first, data, "--out", second));
        Assert.Equal(string.Empty, two.StdErr);

        Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
    }

    // ---- composition (§13) ---------------------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenTheSpecExtendsABase_ThenTheOutputIsOneStandaloneFlattenedSpec()
    {
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("base.toml", CliFixtures.CalibrateChainBaseSpec);
        var root = temp.Write("root.toml", CliFixtures.CalibrateChainRootSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var frozen = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", root, data, "--out", frozen);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Single(Lines(harness.StdErr));
        Assert.Contains("warning ObservedDomainUsed", harness.StdErr, StringComparison.Ordinal);

        var text = await ReadCommittedAsync(frozen);
        Assert.DoesNotContain("extends", text, StringComparison.Ordinal);

        // The base contributed the chain's only attribute, so the flattened output must carry it.
        Assert.Equal(["colour"], Parse(text).Attributes.Select(attribute => attribute.Name));

        // Standalone in the strongest sense: the base is gone and the output still resolves.
        File.Delete(basePath);
        var validate = new CliTestHarness();
        Assert.Equal(0, await validate.RunAsync("validate", frozen));
    }

    // ---- auto and frozen conversion are byte-equivalent (§7) ------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Calibrate_WhenTheFrozenSpecIsReconverted_ThenTheBytesMatchTheOnTheFlyRun(bool v2Compat)
    {
        // Freezing changes WHEN a data-dependent decision is resolved, never WHICH decision — so
        // the on-the-fly and frozen conversions agree byte for byte, in both modes.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var frozen = temp.Resolve("frozen.toml");
        string[] mode = v2Compat ? ["--v2-compat"] : [];

        var onTheFly = new CliTestHarness();
        Assert.Equal(0, await onTheFly.RunAsync(
            ["convert", spec, data, "--out", temp.Resolve("baseA"), "--format", "both", "--no-manifest", .. mode]));
        Assert.Equal(string.Empty, onTheFly.StdOut);
        Assert.Equal(string.Empty, onTheFly.StdErr);

        var calibrated = new CliTestHarness();
        Assert.Equal(0, await calibrated.RunAsync("calibrate", spec, data, "--out", frozen));
        Assert.Equal(string.Empty, calibrated.StdOut);
        Assert.Equal(string.Empty, calibrated.StdErr);

        var reconverted = new CliTestHarness();
        Assert.Equal(0, await reconverted.RunAsync(
            ["convert", frozen, data, "--out", temp.Resolve("baseB"), "--format", "both", "--no-manifest", .. mode]));
        Assert.Equal(string.Empty, reconverted.StdOut);
        Assert.Equal(string.Empty, reconverted.StdErr);

        Assert.Equal(
            await File.ReadAllBytesAsync(temp.Resolve("baseA.cxt")),
            await File.ReadAllBytesAsync(temp.Resolve("baseB.cxt")));
        Assert.Equal(
            await File.ReadAllBytesAsync(temp.Resolve("baseA.dat")),
            await File.ReadAllBytesAsync(temp.Resolve("baseB.dat")));
    }

    // ---- the two destinations -------------------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenOutIsStdout_ThenStdoutIsExactlyTheFrozenSpecAndNothingElse()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", "-");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(await FrozenViaLibraryAsync(spec, data), harness.StdOut);

        // The canonical form, restated as bytes rather than inferred from the comparison above.
        Assert.DoesNotContain('\r', harness.StdOut);
        Assert.DoesNotContain('﻿', harness.StdOut);
        Assert.EndsWith("\n", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", harness.StdOut[^2..], StringComparison.Ordinal);

        // Nothing reached the disk at all.
        Assert.Equal(
            [Path.GetFileName(data), Path.GetFileName(spec)],
            Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        AssertNoMutation(harness);
    }

    [Fact]
    public async Task Calibrate_WhenOutIsAFile_ThenStdoutIsEmptyAndTheFileHoldsTheFrozenSpec()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal(await FrozenViaLibraryAsync(spec, data), await ReadCommittedAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    // ---- publication (D-122 part 4) ---------------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenTheTargetExistsWithoutForce_ThenItIsRefusedAndTheOldFileIsUntouched()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Write("frozen.toml", "the old file");
        var before = Snapshot(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{target}' already exists; use --force to replace it."),
            harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        AssertNoMutation(harness);
    }

    [Fact]
    public async Task Calibrate_WhenForceIsSupplied_ThenTheDistinctTargetIsReplaced()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Write("frozen.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target, "--force");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);

        // Renamed aside first, then the stage committed into the freed path — never a copy — and
        // the backup does not survive the commit.
        var moves = harness.PublicationFiles.Operations
            .Where(operation => operation.StartsWith("Move:", StringComparison.Ordinal))
            .Select(RecordingPublicationFileSystem.Fold)
            .ToList();
        Assert.Equal("Move:frozen.toml->frozen.toml.fcabedrock-backup-T", moves[^2]);
        Assert.Equal("Move:frozen.toml.fcabedrock-stage-T->frozen.toml", moves[^1]);
        Assert.NotEqual("the old file", await ReadCommittedAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    public static TheoryData<string> CollisionTargets() => new() { "data", "root", "base" };

    [Theory]
    [MemberData(nameof(CollisionTargets))]
    public async Task Calibrate_WhenTheOutputIsAnInputFile_ThenItIsRefusedEvenWithForce(string which)
    {
        // The complete input set: DATA, the root SPEC, and every composed chain file. The base row
        // is the one the chain threading exists for — the seam cannot derive it from the operands.
        using var temp = TempDirectory.Create();
        var basePath = temp.Write("base.toml", CliFixtures.CalibrateChainBaseSpec);
        var root = temp.Write("root.toml", CliFixtures.CalibrateChainRootSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var before = Snapshot(temp);
        var harness = new CliTestHarness();

        // The base's authored spelling is its referrer-relative extends reference, not its path.
        var (target, spelling) = which switch
        {
            "data" => (data, data),
            "root" => (root, root),
            _ => (basePath, "base.toml"),
        };

        var exit = await harness.RunAsync("calibrate", root, data, "--out", target, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);

        // Exactly one code-less host line, after the preparation Warning the chain's
        // omitted-domain attribute produces — the order HostFailure fixes.
        var errors = Lines(harness.StdErr).Where(line => line.StartsWith("error: ", StringComparison.Ordinal));
        Assert.Equal(
            DiagnosticRenderer.RenderHostError(
                $"the output '{target}' and the input '{spelling}' are the same file.").TrimEnd('\n'),
            Assert.Single(errors));
        Assert.Equal(before, Snapshot(temp));
        AssertNoMutation(harness);
    }

    [Fact]
    public async Task Calibrate_WhenTheCommitRenameFails_ThenTheOldTargetIsRestoredByteIdentically()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Write("frozen.toml", "the old file");
        var before = Snapshot(temp);
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailKind = "Move";
        harness.PublicationFiles.FailMoveTo = "frozen.toml";

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{target}'."), harness.StdErr);
        Assert.Equal(before, Snapshot(temp));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    // ---- deferred diagnostic ordering and cancellation --------------------------------------------

    [Fact]
    public async Task Calibrate_WhenAWarningIsProducedAndTheFileCommits_ThenTheWarningIsRenderedOnceAfterCommitAndExitIsZero()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateObservedSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Single(Lines(harness.StdErr));
        Assert.Contains("warning ObservedDomainUsed", harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Calibrate_WhenAWarningIsProducedAndTheRunIsCancelledAtStageCreation_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateObservedSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Write("frozen.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailCreateNewPrefix = "frozen.toml.fcabedrock-stage-";
        harness.PublicationFiles.FailWith = () =>
        {
            harness.Signals.Cancel();
            return new OperationCanceledException(harness.Signals.Token);
        };

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target, "--force");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task Calibrate_WhenAWarningIsProducedAndTheRunIsCancelledAtTheBackupTransition_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateObservedSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var target = temp.Write("frozen.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.CancelAfterMoveToPrefix = "frozen.toml.fcabedrock-backup-";
        harness.PublicationFiles.CancelAfterMove = harness.Signals.Cancel;

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", target, "--force");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task Calibrate_WhenOutIsStdoutAndTheRunIsCancelled_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateObservedSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var harness = new CliTestHarness();
        harness.OpenInput = path =>
        {
            harness.Signals.Cancel();
            return File.OpenRead(path);
        };

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", "-");

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    // ---- input stability (§17 / D-122 part 5) ------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenTheDataIsReadOnce_ThenNoSecondPassIsOpened()
    {
        // Compared against the merged `plan` command over the same two files: freezing,
        // re-resolving, replanning, and fingerprinting are pure over retained state, so calibrate
        // must open exactly what a terminally reviewed report command opens and nothing more.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateCutsSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", temp.Resolve("frozen.toml"));

        Assert.Equal(0, exit);

        var planned = new CliTestHarness();
        Assert.Equal(0, await planned.RunAsync("plan", spec, data));
        Assert.Equal(planned.Opened, harness.Opened);
    }

    // ---- post-freeze diagnostic composition --------------------------------------------------------

    [Fact]
    public async Task Calibrate_WhenAMatcherBecomesFullyShadowedByTheFreeze_ThenTheWarningIsReportedExactlyOnce()
    {
        // The freeze CREATES this warning: before it the matcher supplied the discretizer and won,
        // after it an explicit manual_cuts shadows the template's only field. Its key is absent
        // from the preparation baseline, so the composition retains it — exactly once.
        using var temp = TempDirectory.Create();
        var spec = temp.Write("spec.toml", CliFixtures.CalibrateShadowedMatcherSpec);
        var data = temp.Write("data.csv", CliFixtures.CalibrateWideData);
        var frozen = temp.Resolve("frozen.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("calibrate", spec, data, "--out", frozen);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Single(Lines(harness.StdErr));
        Assert.Contains("warning MatcherFullyShadowed", harness.StdErr, StringComparison.Ordinal);

        // Shadowing is Warning-only: the template and matcher are retained, and the frozen cuts
        // are explicit.
        var text = await ReadCommittedAsync(frozen);
        Assert.Contains("kind = \"manual_cuts\"", text, StringComparison.Ordinal);
        var document = Parse(text);
        Assert.Single(document.Templates);
        Assert.Single(document.Matchers);

        // Idempotent in both bytes and diagnostics.
        var second = new CliTestHarness();
        var again = temp.Resolve("again.toml");
        Assert.Equal(0, await second.RunAsync("calibrate", frozen, data, "--out", again));
        Assert.Single(Lines(second.StdErr));
        Assert.Contains("warning MatcherFullyShadowed", second.StdErr, StringComparison.Ordinal);
        Assert.Equal(await File.ReadAllBytesAsync(frozen), await File.ReadAllBytesAsync(again));
    }
}
