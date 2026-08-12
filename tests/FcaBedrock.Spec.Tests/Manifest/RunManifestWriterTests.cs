using System.Reflection;
using FcaBedrock.Core.Calibration;
using FcaBedrock.Spec.Manifest;

namespace FcaBedrock.Spec.Tests.Manifest;

// RunManifest + RunManifestWriter (M7 Slice E / S5; §15, D-122 part 6, D-123 point 8).
// The expected documents are literal text oracles written out by hand — never produced by a
// test-side serializer — so a writer change cannot move the expectation with it. Boundary
// cases build their expected line in test code and assert its length independently, the
// DeclaredDomainWrappingTests convention, so an off-by-one in the shared D-113 helper cannot
// hide behind a fixture the writer itself produced.
public sealed class RunManifestWriterTests
{
    // ---- API shape, immutability, purity ---------------------------------------------------

    [Fact]
    public void Manifest_ExposesExactlyTheApprovedPublicTypes()
    {
        var types = typeof(RunManifestWriter).Assembly
            .GetExportedTypes()
            .Where(t => t.Namespace == "FcaBedrock.Spec.Manifest")
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // No reader/parser, no filesystem or stream writer, no clock, no hash calculator, no
        // builder, no serializer-options or formatting-knob type joined the surface.
        Assert.Equal(
            [
                nameof(RunCalibration),
                nameof(RunManifest),
                nameof(RunManifestWriter),
                nameof(RunOutput),
                nameof(RunOutputFormat),
                nameof(RunSection),
                nameof(SpecFileEntry),
            ],
            types);
    }

    [Fact]
    public void Writer_ExposesExactlyOnePublicWriteOperationAndNoOtherKnobs()
    {
        var type = typeof(RunManifestWriter);
        Assert.True(type is { IsAbstract: true, IsSealed: true, IsPublic: true }); // C# static class

        var publicMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();
        var write = Assert.Single(publicMethods);
        Assert.Equal(nameof(RunManifestWriter.Write), write.Name);
        Assert.Equal(typeof(string), write.ReturnType);
        Assert.Equal([typeof(RunManifest)], write.GetParameters().Select(p => p.ParameterType).ToArray());

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(type.GetNestedTypes(BindingFlags.Public));
        Assert.Empty(type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void Write_WhenManifestNull_ThenThrows() =>
        Assert.Throws<ArgumentNullException>(() => RunManifestWriter.Write(null!));

    [Fact]
    public void RunSection_WhenRequiredValueNull_ThenThrows()
    {
        // Constructed directly rather than through the Run fixture, whose `??` defaults would
        // swallow a null argument before it reached the constructor.
        string[] argv = ["fcabedrock", "convert"];

        Assert.Throws<ArgumentNullException>(() => new RunSection(
            null!, Stamp, argv, "s.toml", "sha256:s", "sha256:f", CxtFingerprint, null, "d.csv", "sha256:i"));
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, null!, "s.toml", "sha256:s", "sha256:f", CxtFingerprint, null, "d.csv", "sha256:i"));
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, argv, null!, "sha256:s", "sha256:f", CxtFingerprint, null, "d.csv", "sha256:i"));
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, argv, "s.toml", null!, "sha256:f", CxtFingerprint, null, "d.csv", "sha256:i"));
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, argv, "s.toml", "sha256:s", null!, CxtFingerprint, null, "d.csv", "sha256:i"));
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, argv, "s.toml", "sha256:s", "sha256:f", CxtFingerprint, null, null!, "sha256:i"));
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, argv, "s.toml", "sha256:s", "sha256:f", CxtFingerprint, null, "d.csv", null!));

        // A null element in the audit argv.
        Assert.Throws<ArgumentNullException>(() => new RunSection(
            "v", Stamp, ["fcabedrock", null!], "s.toml", "sha256:s", "sha256:f",
            CxtFingerprint, null, "d.csv", "sha256:i"));
    }

    [Fact]
    public void Model_WhenEntryValueNull_ThenThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new RunOutput(RunOutputFormat.Cxt, null!, "sha256:h"));
        Assert.Throws<ArgumentNullException>(() => new RunOutput(RunOutputFormat.Cxt, "a.cxt", null!));
        Assert.Throws<ArgumentNullException>(() => new SpecFileEntry(null!, "sha256:h"));
        Assert.Throws<ArgumentNullException>(() => new SpecFileEntry("a.toml", null!));
        Assert.Throws<ArgumentNullException>(() => new RunCalibration(null!, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunOutput((RunOutputFormat)7, "a.cxt", "sha256:h"));
    }

    [Fact]
    public void RunManifest_WhenArgumentOrElementNull_ThenThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new RunManifest(null!, [CxtOutput], [], []));
        Assert.Throws<ArgumentNullException>(() => new RunManifest(Run(), null!, [], []));
        Assert.Throws<ArgumentNullException>(() => new RunManifest(Run(), [CxtOutput], null!, []));
        Assert.Throws<ArgumentNullException>(() => new RunManifest(Run(), [CxtOutput], [], null!));
        Assert.Throws<ArgumentNullException>(() => new RunManifest(Run(), [CxtOutput, null!], [], []));
        Assert.Throws<ArgumentNullException>(() => new RunManifest(Run(), [CxtOutput], [null!], []));
        Assert.Throws<ArgumentNullException>(() => new RunManifest(Run(), [CxtOutput], [], [null!]));
    }

    [Fact]
    public void RunManifest_WhenCallerCollectionsMutatedAfterConstruction_ThenOutputUnchanged()
    {
        var outputs = new List<RunOutput> { CxtOutput };
        var specFiles = new List<SpecFileEntry> { new("adult.toml", "sha256:root") };
        var calibrations = new List<RunCalibration> { new(new ObservedDomain("education", ["HS-grad"]), null) };
        var commandLine = new List<string> { "fcabedrock", "convert" };

        var manifest = new RunManifest(Run(commandLine: commandLine), outputs, specFiles, calibrations);
        var before = RunManifestWriter.Write(manifest);

        outputs.Add(DatOutput);
        specFiles.Add(new SpecFileEntry("base.toml", "sha256:base"));
        calibrations.Add(new RunCalibration(new PassthroughBins("industry", ["Self-emp"]), null));
        commandLine.Add("--force");

        Assert.Single(manifest.Outputs);
        Assert.Single(manifest.SpecFiles);
        Assert.Single(manifest.Calibrations);
        Assert.Equal(2, manifest.Run.CommandLine.Count);
        Assert.Equal(before, RunManifestWriter.Write(manifest));
    }

    [Fact]
    public void Write_WhenCalledRepeatedly_ThenIdenticalAndInputsUnmutated()
    {
        var values = new List<string> { "Bachelors", "HS-grad" };
        var cuts = new List<double> { 38, 49, 52 };
        var observed = new ObservedDomain("education", values);
        var calibrated = new CalibratedCuts("age", cuts);
        var manifest = Manifest(calibrations:
        [
            new RunCalibration(calibrated, "equal_frequency"),
            new RunCalibration(observed, null),
        ]);

        var first = RunManifestWriter.Write(manifest);

        Assert.Equal(first, RunManifestWriter.Write(manifest));
        Assert.Equal(first, RunManifestWriter.Write(manifest));

        // The retained outcomes are consumed, never rewritten.
        Assert.Equal(["Bachelors", "HS-grad"], observed.Values);
        Assert.Equal([38d, 49d, 52d], calibrated.Cuts);
        Assert.Equal(["Bachelors", "HS-grad"], values);
        Assert.Equal([38d, 49d, 52d], cuts);
    }

    // ---- Output coherence ------------------------------------------------------------------

    [Fact]
    public void RunManifest_WhenNoOutputs_ThenThrows() =>
        Assert.Throws<ArgumentException>(() => new RunManifest(Run(), [], [], []));

    [Fact]
    public void RunManifest_WhenAFormatRepeats_ThenThrows()
    {
        Assert.Throws<ArgumentException>(() => new RunManifest(
            Run(), [CxtOutput, new RunOutput(RunOutputFormat.Cxt, "other.cxt", "sha256:o")], [], []));
        Assert.Throws<ArgumentException>(() => new RunManifest(
            Run(cxt: null, dat: DatFingerprint),
            [DatOutput, new RunOutput(RunOutputFormat.Dat, "other.dat", "sha256:o")],
            [],
            []));
    }

    [Fact]
    public void RunManifest_WhenOutputFingerprintDoesNotMatchTheWrittenFormats_ThenThrows()
    {
        // Missing.
        Assert.Throws<ArgumentException>(() => new RunManifest(Run(cxt: null), [CxtOutput], [], []));
        Assert.Throws<ArgumentException>(() => new RunManifest(
            Run(cxt: CxtFingerprint, dat: null), [CxtOutput, DatOutput], [], []));

        // Extraneous.
        Assert.Throws<ArgumentException>(() => new RunManifest(
            Run(cxt: CxtFingerprint, dat: DatFingerprint), [CxtOutput], [], []));
        Assert.Throws<ArgumentException>(() => new RunManifest(
            Run(cxt: CxtFingerprint, dat: null), [DatOutput], [], []));
    }

    // ---- Full canonical documents ----------------------------------------------------------

    [Fact]
    public void Write_WhenMinimalRun_ThenExactCanonicalDocument()
    {
        var manifest = Manifest();

        Assert.Equal(
            Lines(
                "[run]",
                "tool_version = \"fcabedrock-vnext 1.0.0\"",
                "timestamp = 2026-07-22T12:34:56Z",
                "command_line = [\"fcabedrock\", \"convert\", \"adult.toml\", \"adult.csv\", \"--out\", \"adult\", \"--format\", \"cxt\"]",
                "spec_path = \"adult.toml\"",
                "spec_file_hash = \"sha256:spec\"",
                "schema_fingerprint = \"sha256:schema\"",
                "cxt_output_fingerprint = \"sha256:cxtfp\"",
                "input_path = \"adult.csv\"",
                "input_hash = \"sha256:input\"",
                "",
                "[[run.outputs]]",
                "format = \"cxt\"",
                "path = \"adult.cxt\"",
                "hash = \"sha256:cxtart\""),
            RunManifestWriter.Write(manifest));
    }

    [Fact]
    public void Write_WhenAllFourCalibrationKinds_ThenExactCanonicalDocument()
    {
        // Deliberately not alphabetical: alphabetical order would be age, education,
        // empty_*, industry, workclass. The retained CalibratedSpec.Calibrations order is
        // reproduced exactly — no sorting, grouping, or deduplication.
        var manifest = Manifest(calibrations:
        [
            new RunCalibration(new IncludeAdditions("workclass", ["Assoc"]), null),

            // -0 renders 0 and 90.0 renders 90 through the existing TomlLiteral rules; 49.5
            // pins that a genuinely non-integral cut keeps its float form.
            new RunCalibration(new CalibratedCuts("age", [-0.0, 38, 49.5, 90.0]), "equal_frequency"),

            new RunCalibration(new PassthroughBins("industry", ["Self-emp"]), null),
            new RunCalibration(new ObservedDomain("education", ["Bachelors", "HS-grad"]), null),

            // The legitimate zero-discovery outcomes of every empty-capable kind
            // (D-122 part 15) serialize as an explicit empty array.
            new RunCalibration(new ObservedDomain("empty_observed", []), null),
            new RunCalibration(new IncludeAdditions("empty_include", []), null),
            new RunCalibration(new PassthroughBins("empty_passthrough", []), null),
        ]);

        Assert.Equal(
            Lines(
                "[run]",
                "tool_version = \"fcabedrock-vnext 1.0.0\"",
                "timestamp = 2026-07-22T12:34:56Z",
                "command_line = [\"fcabedrock\", \"convert\", \"adult.toml\", \"adult.csv\", \"--out\", \"adult\", \"--format\", \"cxt\"]",
                "spec_path = \"adult.toml\"",
                "spec_file_hash = \"sha256:spec\"",
                "schema_fingerprint = \"sha256:schema\"",
                "cxt_output_fingerprint = \"sha256:cxtfp\"",
                "input_path = \"adult.csv\"",
                "input_hash = \"sha256:input\"",
                "",
                "[[run.outputs]]",
                "format = \"cxt\"",
                "path = \"adult.cxt\"",
                "hash = \"sha256:cxtart\"",
                "",
                "[[run.calibrations]]",
                "attribute = \"workclass\"",
                "kind = \"include_additions\"",
                "values = [\"Assoc\"]",
                "",
                "[[run.calibrations]]",
                "attribute = \"age\"",
                "kind = \"cuts\"",
                "discretizer = \"equal_frequency\"",
                "cuts = [0, 38, 49.5, 90]",
                "",
                "[[run.calibrations]]",
                "attribute = \"industry\"",
                "kind = \"passthrough_bins\"",
                "values = [\"Self-emp\"]",
                "",
                "[[run.calibrations]]",
                "attribute = \"education\"",
                "kind = \"observed_domain\"",
                "values = [\"Bachelors\", \"HS-grad\"]",
                "",
                "[[run.calibrations]]",
                "attribute = \"empty_observed\"",
                "kind = \"observed_domain\"",
                "values = []",
                "",
                "[[run.calibrations]]",
                "attribute = \"empty_include\"",
                "kind = \"include_additions\"",
                "values = []",
                "",
                "[[run.calibrations]]",
                "attribute = \"empty_passthrough\"",
                "kind = \"passthrough_bins\"",
                "values = []"),
            RunManifestWriter.Write(manifest));
    }

    [Fact]
    public void Write_WhenNoCalibrationsRetained_ThenTheWholeSectionFamilyIsAbsent()
    {
        var toml = RunManifestWriter.Write(Manifest());

        Assert.DoesNotContain("[[run.calibrations]]", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("attribute", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("kind", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenBothFormats_ThenCxtBeforeDatWithBothFingerprints()
    {
        // Supplied DAT-first: the emitted order is the writer's canonical format order, not
        // the order the caller happened to assemble the outputs in.
        var manifest = new RunManifest(
            Run(cxt: CxtFingerprint, dat: DatFingerprint), [DatOutput, CxtOutput], [], []);

        var toml = RunManifestWriter.Write(manifest);

        Assert.Contains(
            Lines(
                "cxt_output_fingerprint = \"sha256:cxtfp\"",
                "dat_output_fingerprint = \"sha256:datfp\""),
            toml,
            StringComparison.Ordinal);
        Assert.Contains(
            Lines(
                "[[run.outputs]]",
                "format = \"cxt\"",
                "path = \"adult.cxt\"",
                "hash = \"sha256:cxtart\"",
                "",
                "[[run.outputs]]",
                "format = \"dat\"",
                "path = \"adult.dat\"",
                "hash = \"sha256:datart\""),
            toml,
            StringComparison.Ordinal);
        Assert.True(
            toml.IndexOf("\"cxt\"", StringComparison.Ordinal) < toml.IndexOf("\"dat\"", StringComparison.Ordinal),
            "cxt must precede dat");
    }

    [Fact]
    public void Write_WhenCxtOnly_ThenOnlyTheCxtEntryAndFingerprint()
    {
        var toml = RunManifestWriter.Write(Manifest());

        Assert.Contains("cxt_output_fingerprint = ", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("dat_output_fingerprint", toml, StringComparison.Ordinal);
        Assert.Contains("format = \"cxt\"", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("format = \"dat\"", toml, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(toml, "[[run.outputs]]"));
    }

    [Fact]
    public void Write_WhenDatOnly_ThenOnlyTheDatEntryAndFingerprint()
    {
        var toml = RunManifestWriter.Write(
            new RunManifest(Run(cxt: null, dat: DatFingerprint), [DatOutput], [], []));

        Assert.DoesNotContain("cxt_output_fingerprint", toml, StringComparison.Ordinal);
        Assert.Contains("dat_output_fingerprint = \"sha256:datfp\"\n", toml, StringComparison.Ordinal);
        Assert.Contains(
            Lines("[[run.outputs]]", "format = \"dat\"", "path = \"adult.dat\"", "hash = \"sha256:datart\""),
            toml,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOf(toml, "[[run.outputs]]"));
    }

    [Fact]
    public void Write_WhenNoExtendsChain_ThenTheWholeSpecFilesFamilyIsAbsent()
    {
        var toml = RunManifestWriter.Write(Manifest());

        Assert.DoesNotContain("[[run.spec_files]]", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenMultilevelChain_ThenRootFirstThenBasesWithPathBeforeHash()
    {
        var manifest = Manifest(specFiles:
        [
            new SpecFileEntry("adult.toml", "sha256:root"),
            new SpecFileEntry("base/common.toml", "sha256:base1"),
            new SpecFileEntry("../shared/core.toml", "sha256:base2"),
        ]);

        Assert.Contains(
            Lines(
                "[[run.spec_files]]",
                "path = \"adult.toml\"",
                "hash = \"sha256:root\"",
                "",
                "[[run.spec_files]]",
                "path = \"base/common.toml\"",
                "hash = \"sha256:base1\"",
                "",
                "[[run.spec_files]]",
                "path = \"../shared/core.toml\"",
                "hash = \"sha256:base2\""),
            RunManifestWriter.Write(manifest),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ThenSectionsAreOrderedOutputsThenSpecFilesThenCalibrations()
    {
        var manifest = Manifest(
            specFiles: [new SpecFileEntry("adult.toml", "sha256:root")],
            calibrations: [new RunCalibration(new ObservedDomain("education", ["HS-grad"]), null)]);

        var toml = RunManifestWriter.Write(manifest);

        var run = toml.IndexOf("[run]", StringComparison.Ordinal);
        var outputs = toml.IndexOf("[[run.outputs]]", StringComparison.Ordinal);
        var specFiles = toml.IndexOf("[[run.spec_files]]", StringComparison.Ordinal);
        var calibrations = toml.IndexOf("[[run.calibrations]]", StringComparison.Ordinal);

        Assert.True(run < outputs && outputs < specFiles && specFiles < calibrations);
    }

    [Fact]
    public void Write_ThenLfOnlyFinalLfNoBomNoCommentsAndSingleBlankLinesBetweenTables()
    {
        var manifest = Manifest(
            specFiles: [new SpecFileEntry("adult.toml", "sha256:root")],
            calibrations: [new RunCalibration(new ObservedDomain("education", ["HS-grad"]), null)]);

        var toml = RunManifestWriter.Write(manifest);

        Assert.DoesNotContain('\r', toml);
        Assert.DoesNotContain('\uFEFF', toml);
        Assert.DoesNotContain('#', toml);
        Assert.EndsWith("\n", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", toml, StringComparison.Ordinal);
        Assert.False(toml.StartsWith('\n'));

        // Exactly one blank line precedes each table after the first, and none is stray.
        var blanks = toml.Split('\n').Count(line => line.Length == 0);
        Assert.Equal(3 + 1, blanks); // three inter-table blanks + the final LF's trailing split
    }

    // ---- Literals, timestamp, paths, argv ---------------------------------------------------

    [Fact]
    public void Write_WhenArgvCarriesHostPathUnicodeAndControlCharacters_ThenPreservedElementForElement()
    {
        string[] argv =
        [
            @"C:\tools\fcabedrock.exe", // argv[0] is not "fcabedrock" — preserved as element zero
            "convert",
            "",                          // an empty argument stays a distinct element
            "tab\there",
            "nl\nhere",
            "\u0001",
            "quote\"q",
            "na\u00EFve",
        ];

        var toml = RunManifestWriter.Write(Manifest(run: Run(commandLine: argv)));

        var expected =
            "command_line = [" +
            "\"C:\\\\tools\\\\fcabedrock.exe\", " +
            "\"convert\", " +
            "\"\", " +
            "\"tab\\there\", " +
            "\"nl\\nhere\", " +
            "\"\\u0001\", " +
            "\"quote\\\"q\", " +
            "\"na\u00EFve\"]";

        Assert.Contains(expected + "\n", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenCommandLineIsLong_ThenStillInline()
    {
        var argv = new List<string> { @"C:\tools\fcabedrock.exe", "convert" };
        argv.AddRange(Enumerable.Range(0, 20).Select(i => $"--option-with-a-long-name-{i:D2}"));

        var toml = RunManifestWriter.Write(Manifest(run: Run(commandLine: argv)));

        var line = Assert.Single(
            toml.Split('\n'), l => l.StartsWith("command_line = ", StringComparison.Ordinal));
        Assert.True(line.Length > 100, "the control line must exceed the shared wrapping cutoff");
        Assert.StartsWith("command_line = [\"", line, StringComparison.Ordinal);
        Assert.EndsWith("\"]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenPathsVersionAndValuesCarryEscapesAndUnicode_ThenEscapedNotNormalized()
    {
        var manifest = new RunManifest(
            Run(
                toolVersion: "fcabedrock-vnext 1.0.0+\u00E9\tbuild",
                specPath: @".\specs\adult.toml",
                inputPath: "..\\data\\\\dou\u00DFle.csv"),
            [new RunOutput(RunOutputFormat.Cxt, "out\\ad\u00E8s.cxt", "sha256:cxtart")],
            [new SpecFileEntry(@"..\base\core.toml", "sha256:base")],
            [new RunCalibration(new ObservedDomain("education", ["a\"b", "c\\d", "e\u0007f", "\u00F1"]), null)]);

        var toml = RunManifestWriter.Write(manifest);

        Assert.Contains("tool_version = \"fcabedrock-vnext 1.0.0+\u00E9\\tbuild\"\n", toml, StringComparison.Ordinal);
        Assert.Contains("spec_path = \".\\\\specs\\\\adult.toml\"\n", toml, StringComparison.Ordinal);
        Assert.Contains("input_path = \"..\\\\data\\\\\\\\dou\u00DFle.csv\"\n", toml, StringComparison.Ordinal);
        Assert.Contains("path = \"out\\\\ad\u00E8s.cxt\"\n", toml, StringComparison.Ordinal);
        Assert.Contains("path = \"..\\\\base\\\\core.toml\"\n", toml, StringComparison.Ordinal);
        Assert.Contains(
            "values = [\"a\\\"b\", \"c\\\\d\", \"e\\u0007f\", \"\u00F1\"]\n", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSection_WhenTimestampIsNotWholeSecondUtc_ThenThrows()
    {
        // Rejected rather than normalized: §15 names timestamp an audit field, so silently
        // shifting or truncating it would rewrite the record. Supplying a whole-second UTC
        // instant is the clock seam's obligation.
        Assert.Throws<ArgumentException>(() =>
            Run(timestamp: new DateTimeOffset(2026, 7, 22, 12, 34, 56, TimeSpan.FromHours(2))));
        Assert.Throws<ArgumentException>(() =>
            Run(timestamp: new DateTimeOffset(2026, 7, 22, 12, 34, 56, TimeSpan.FromHours(-5))));
        Assert.Throws<ArgumentException>(() =>
            Run(timestamp: new DateTimeOffset(2026, 7, 22, 12, 34, 56, 250, TimeSpan.Zero)));
        Assert.Throws<ArgumentException>(() => Run(timestamp: Stamp.AddTicks(1)));
    }

    [Fact]
    public void Write_ThenTimestampIsWholeSecondUtcZAndFractionFree()
    {
        var toml = RunManifestWriter.Write(Manifest(run: Run(timestamp: new DateTimeOffset(
            2026, 1, 2, 3, 4, 5, TimeSpan.Zero))));

        var line = Assert.Single(
            toml.Split('\n'), l => l.StartsWith("timestamp = ", StringComparison.Ordinal));
        Assert.Equal("timestamp = 2026-01-02T03:04:05Z", line);
        Assert.DoesNotContain('.', line);
        Assert.DoesNotContain('+', line);
        Assert.DoesNotContain('"', line);
    }

    [Fact]
    public void Write_WhenModelsDifferOnlyInOneField_ThenOnlyThatSerializedFieldDiffers()
    {
        // §15 names timestamp and command_line the audit-variable fields; tool_version is
        // included here only as a third single-field perturbation, not as an audit field.
        AssertSingleLineDiffers(
            Manifest(),
            Manifest(run: Run(timestamp: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero))),
            "timestamp = ");

        AssertSingleLineDiffers(
            Manifest(),
            Manifest(run: Run(commandLine: ["fcabedrock", "convert", "other.toml"])),
            "command_line = ");

        AssertSingleLineDiffers(
            Manifest(),
            Manifest(run: Run(toolVersion: "fcabedrock-vnext 9.9.9")),
            "tool_version = ");
    }

    [Fact]
    public void Write_WhenPathSpellingsAreUnusual_ThenCarriedAsDataNotNormalized()
    {
        var manifest = new RunManifest(
            Run(specPath: "./adult.toml", inputPath: "data//sub///adult.csv"),
            [new RunOutput(RunOutputFormat.Cxt, "../out/adult.cxt", "sha256:cxtart")],
            [new SpecFileEntry("./adult.toml", "sha256:a"), new SpecFileEntry("./adult.toml", "sha256:b")],
            []);

        var toml = RunManifestWriter.Write(manifest);

        Assert.Contains("spec_path = \"./adult.toml\"\n", toml, StringComparison.Ordinal);
        Assert.Contains("input_path = \"data//sub///adult.csv\"\n", toml, StringComparison.Ordinal);
        Assert.Contains("path = \"../out/adult.cxt\"\n", toml, StringComparison.Ordinal);

        // Two chain entries spelling the same file are two entries: no deduplication, no
        // substitution of a canonical filesystem identity key. Anchored at a line start so
        // the `spec_path` line's own `path = …` suffix is not counted.
        Assert.Equal(2, CountOf(toml, "\npath = \"./adult.toml\""));
    }

    // ---- Retained outcome mapping -----------------------------------------------------------

    [Fact]
    public void Write_ThenEachCoreOutcomeSubtypeMapsToItsCanonicalKindAndPayload()
    {
        AttributeCalibration[] outcomes =
        [
            new CalibratedCuts("a", [1, 2]),
            new ObservedDomain("b", ["x"]),
            new IncludeAdditions("c", ["y"]),
            new PassthroughBins("d", ["z"]),
        ];

        var toml = RunManifestWriter.Write(Manifest(calibrations:
        [
            new RunCalibration(outcomes[0], "equal_width"),
            new RunCalibration(outcomes[1], null),
            new RunCalibration(outcomes[2], null),
            new RunCalibration(outcomes[3], null),
        ]));

        Assert.Contains(
            Lines("attribute = \"a\"", "kind = \"cuts\"", "discretizer = \"equal_width\"", "cuts = [1, 2]"),
            toml,
            StringComparison.Ordinal);
        Assert.Contains(
            Lines("attribute = \"b\"", "kind = \"observed_domain\"", "values = [\"x\"]"), toml, StringComparison.Ordinal);
        Assert.Contains(
            Lines("attribute = \"c\"", "kind = \"include_additions\"", "values = [\"y\"]"), toml, StringComparison.Ordinal);
        Assert.Contains(
            Lines("attribute = \"d\"", "kind = \"passthrough_bins\"", "values = [\"z\"]"), toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ThenRetainedValueAndCutOrderIsUnchanged()
    {
        // Neither list is sorted: the retained first-observation / ascending-cut order is the
        // order the manifest records.
        var toml = RunManifestWriter.Write(Manifest(calibrations:
        [
            new RunCalibration(new ObservedDomain("e", ["zeta", "alpha", "Mu", "10", "2"]), null),
            new RunCalibration(new CalibratedCuts("f", [-5.5, 0, 3, 1000]), "equal_width"),
        ]));

        Assert.Contains("values = [\"zeta\", \"alpha\", \"Mu\", \"10\", \"2\"]\n", toml, StringComparison.Ordinal);
        Assert.Contains("cuts = [-5.5, 0, 3, 1000]\n", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ThenCutsDiscriminatorComesFromTheSuppliedFactNotTheCuts()
    {
        // Identical cuts, different authored kinds: the spelling cannot be reconstructed from
        // the payload, so it must be carried.
        var frequency = RunManifestWriter.Write(Manifest(calibrations:
            [new RunCalibration(new CalibratedCuts("age", [38, 49, 52]), "equal_frequency")]));
        var width = RunManifestWriter.Write(Manifest(calibrations:
            [new RunCalibration(new CalibratedCuts("age", [38, 49, 52]), "equal_width")]));

        Assert.Contains("discretizer = \"equal_frequency\"\n", frequency, StringComparison.Ordinal);
        Assert.Contains("discretizer = \"equal_width\"\n", width, StringComparison.Ordinal);
        Assert.Contains("cuts = [38, 49, 52]\n", frequency, StringComparison.Ordinal);
        Assert.Contains("cuts = [38, 49, 52]\n", width, StringComparison.Ordinal);
        Assert.Equal(
            frequency.Replace("equal_frequency", "equal_width", StringComparison.Ordinal), width);
    }

    [Fact]
    public void RunCalibration_WhenDiscretizerDoesNotMatchTheOutcomeKind_ThenThrows()
    {
        Assert.Throws<ArgumentException>(() => new RunCalibration(new CalibratedCuts("a", [1]), null));
        Assert.Throws<ArgumentException>(() => new RunCalibration(new ObservedDomain("a", ["x"]), "equal_width"));
        Assert.Throws<ArgumentException>(() => new RunCalibration(new IncludeAdditions("a", ["x"]), "equal_width"));
        Assert.Throws<ArgumentException>(() => new RunCalibration(new PassthroughBins("a", ["x"]), "equal_width"));
    }

    [Fact]
    public void RunCalibration_WhenCalibratedCutsAreEmpty_ThenThrows()
    {
        // §15 records one entry per *successfully* calibrated attribute. An empty calibrated-cut
        // list is not one — the calibrated-state boundary rejects any count other than bins - 1
        // (D-102) — so the carrier refuses it outright rather than emitting a `cuts = []` entry
        // for a run that cannot exist. The explicit empty-array form stays required for the three
        // legitimate zero-discovery non-cut kinds, which the exact-document evidence above pins.
        Assert.Throws<ArgumentException>(() => new RunCalibration(new CalibratedCuts("age", []), "equal_width"));
        Assert.Throws<ArgumentException>(() => new RunCalibration(new CalibratedCuts("age", []), "equal_frequency"));
        Assert.Throws<ArgumentException>(() => new RunCalibration(new CalibratedCuts("age", []), null));

        // A single cut is the smallest successful outcome and remains representable.
        Assert.Contains(
            "cuts = [38]\n",
            RunManifestWriter.Write(Manifest(calibrations:
                [new RunCalibration(new CalibratedCuts("age", [38]), "equal_width")])),
            StringComparison.Ordinal);
    }

    // ---- D-113 wrapping ----------------------------------------------------------------------

    [Fact]
    public void Write_WhenValuesLineIsExactlyAtCutoff_ThenInline()
    {
        // 9 (key) + 1 ([) + 44 + 2 (, ) + 43 + 1 (]) = 100.
        var values = new[] { new string('a', 42), new string('b', 41) };
        var expected = ValuesKeyPrefix + "[\"" + values[0] + "\", \"" + values[1] + "\"]";
        Assert.Equal(Cutoff, expected.Length);

        Assert.Contains(expected + "\n", WriteObserved(values), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenValuesLineIsOneOverCutoff_ThenWrapped()
    {
        var values = new[] { new string('a', 43), new string('b', 41) };
        var wouldBeInline = ValuesKeyPrefix + "[\"" + values[0] + "\", \"" + values[1] + "\"]";
        Assert.Equal(Cutoff + 1, wouldBeInline.Length);

        var toml = WriteObserved(values);

        Assert.DoesNotContain(wouldBeInline, toml, StringComparison.Ordinal);
        Assert.Contains(
            "values = [\n  \"" + values[0] + "\",\n  \"" + values[1] + "\",\n]\n",
            toml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenEscapingPushesValuesLineOverCutoff_ThenWrapped()
    {
        // Same raw lengths (42 and 41); only the escaped rendering differs, so this pins that
        // the measurement is taken over the final escaped text, not the raw values.
        var plain = new[] { new string('a', 42), new string('b', 41) };
        var quoted = new[] { new string('a', 41) + "\"", new string('b', 41) };
        Assert.Equal(plain[0].Length, quoted[0].Length);

        var quotedInline = ValuesKeyPrefix + "[\"" + new string('a', 41) + "\\\"\", \"" + quoted[1] + "\"]";
        Assert.Equal(Cutoff + 1, quotedInline.Length);

        Assert.Contains(ValuesKeyPrefix + "[\"", WriteObserved(plain), StringComparison.Ordinal);
        Assert.Contains(
            "values = [\n  \"" + new string('a', 41) + "\\\"\",\n  \"" + quoted[1] + "\",\n]\n",
            WriteObserved(quoted),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenValuesAreEmptyOrShort_ThenInline()
    {
        Assert.Contains("values = []\n", WriteObserved([]), StringComparison.Ordinal);
        Assert.Contains("values = [\"red\", \"blue\"]\n", WriteObserved(["red", "blue"]), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenSingleValueExceedsCutoff_ThenWrappedButNotSplit()
    {
        var value = new string('x', 120);

        var toml = WriteObserved([value]);

        Assert.Contains("values = [\n  \"" + value + "\",\n]\n", toml, StringComparison.Ordinal);
        Assert.Contains("\"" + value + "\"", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenCutsLineExceedsCutoff_ThenStillInline()
    {
        var cuts = Enumerable.Range(1, 40).Select(i => i * 1000.5).ToArray();

        var toml = RunManifestWriter.Write(Manifest(calibrations:
            [new RunCalibration(new CalibratedCuts("age", cuts), "equal_frequency")]));

        var line = Assert.Single(toml.Split('\n'), l => l.StartsWith("cuts = ", StringComparison.Ordinal));
        Assert.True(line.Length > Cutoff, "the control line must exceed the shared wrapping cutoff");
        Assert.StartsWith("cuts = [1000.5, ", line, StringComparison.Ordinal);
        Assert.EndsWith("40020]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WhenValuesAreLarge_ThenDeterministicAndFullyWrapped()
    {
        var values = Enumerable.Range(0, 1000).Select(i => $"value-{i:D4}").ToArray();

        var toml = WriteObserved(values);

        Assert.Equal(toml, WriteObserved(values));
        Assert.Contains("values = [\n", toml, StringComparison.Ordinal);
        Assert.Equal(1000, toml.Split('\n').Count(line => line.StartsWith("  \"value-", StringComparison.Ordinal)));
        Assert.Contains("  \"value-0000\",\n", toml, StringComparison.Ordinal);
        Assert.Contains("  \"value-0999\",\n]\n", toml, StringComparison.Ordinal);
    }

    // ---- Fixtures ----------------------------------------------------------------------------

    private const int Cutoff = 100;

    // `values = ` — the key, spaces, and equals sign every measurement includes. Spelled out
    // here rather than imported so the tests do not inherit the writer's own arithmetic.
    private const string ValuesKeyPrefix = "values = ";

    private const string CxtFingerprint = "sha256:cxtfp";

    private const string DatFingerprint = "sha256:datfp";

    private static readonly DateTimeOffset Stamp = new(2026, 7, 22, 12, 34, 56, TimeSpan.Zero);

    private static readonly RunOutput CxtOutput = new(RunOutputFormat.Cxt, "adult.cxt", "sha256:cxtart");

    private static readonly RunOutput DatOutput = new(RunOutputFormat.Dat, "adult.dat", "sha256:datart");

    private static RunSection Run(
        string toolVersion = "fcabedrock-vnext 1.0.0",
        DateTimeOffset? timestamp = null,
        IReadOnlyList<string>? commandLine = null,
        string specPath = "adult.toml",
        string specFileHash = "sha256:spec",
        string schemaFingerprint = "sha256:schema",
        string? cxt = CxtFingerprint,
        string? dat = null,
        string inputPath = "adult.csv",
        string inputHash = "sha256:input") =>
        new(toolVersion,
            timestamp ?? Stamp,
            commandLine ?? ["fcabedrock", "convert", "adult.toml", "adult.csv", "--out", "adult", "--format", "cxt"],
            specPath,
            specFileHash,
            schemaFingerprint,
            cxt,
            dat,
            inputPath,
            inputHash);

    private static RunManifest Manifest(
        RunSection? run = null,
        IReadOnlyList<RunOutput>? outputs = null,
        IReadOnlyList<SpecFileEntry>? specFiles = null,
        IReadOnlyList<RunCalibration>? calibrations = null) =>
        new(run ?? Run(), outputs ?? [CxtOutput], specFiles ?? [], calibrations ?? []);

    private static string WriteObserved(IReadOnlyList<string> values) =>
        RunManifestWriter.Write(Manifest(calibrations:
            [new RunCalibration(new ObservedDomain("a", values), null)]));

    private static void AssertSingleLineDiffers(RunManifest left, RunManifest right, string keyPrefix)
    {
        var leftLines = RunManifestWriter.Write(left).Split('\n');
        var rightLines = RunManifestWriter.Write(right).Split('\n');
        Assert.Equal(leftLines.Length, rightLines.Length);

        var differing = Enumerable.Range(0, leftLines.Length)
            .Where(i => !string.Equals(leftLines[i], rightLines[i], StringComparison.Ordinal))
            .ToArray();

        var index = Assert.Single(differing);
        Assert.StartsWith(keyPrefix, leftLines[index], StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";
}
