using System.Globalization;
using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The migrate vertical (D-122 part 10; D-079), end to end through argv.
/// <para>
/// Migration is transcription, so the canonical-bytes cases compare the command's output
/// against an <b>independently executed</b> <see cref="BedReader"/> + <see cref="BedMigrator"/>
/// + <see cref="SpecWriter"/> route built here with the binding §10.2's presence rule
/// produces. The rest is the CLI's own contract: which settings are authored, which stream
/// carries what, when a diagnostic becomes visible, what the ruled decoder accepts, and what
/// the filesystem holds afterwards.
/// </para>
/// </summary>
public sealed class MigrateCommandTests
{
    // A hand-authored .bed: every section is positional, one line per attribute, and every
    // attribute converts with no restriction (lineage.md). The four fixtures below differ only
    // in their category, value, and type lines, so they are stated as exactly that difference.
    private static string Bed(string[] names, string[] categories, string[] values, string[] types)
    {
        var convert = new string[names.Length];
        Array.Fill(convert, "True");
        var restrict = new string[names.Length];
        Array.Fill(restrict, string.Empty);

        return string.Join(
            '\n',
            [
                "[Number of Attributes]", names.Length.ToString(CultureInfo.InvariantCulture), "",
                "[Attributes]", .. names, "",
                "[Attribute Categories]", .. categories, "",
                "[Category Values]", .. values, "",
                "[Convert Attribute]", .. convert, "",
                "[Attribute Type]", .. types, "",
                "[Restrict To Values]", .. restrict, "",
                "[End]", "",
            ]);
    }

    // The only category IS the missing token, unlabelled: the migrated domain is authored
    // EMPTY and the missing column is still planned (D-122 part 15). No warning.
    private static readonly string EmptyDomainBed = Bed(["colour"], ["?"], ["?"], ["c"]);

    // The missing-token category carries a display label with no v1 carrier: exactly one
    // BedMissingTokenLabelDropped Warning, and a document all the same.
    private static readonly string WarningBed = Bed(["colour"], ["red,green,unknown"], ["r,g,?"], ["c"]);

    // The same condition on two attributes, so the two Warnings have an order to preserve.
    private static readonly string TwoWarningBed =
        Bed(["colour", "size"], ["red,unknown", "big,unknown"], ["r,?", "b,?"], ["c", "c"]);

    // v2's date type is deferred (D-038) and has no v1 carrier, so an INCLUDED attribute of
    // that type cannot be transcribed: the migrator's own Error, and no document.
    private static readonly string DateBed = Bed(["born"], ["d"], ["d"], ["d"]);

    // Column 0 is the only bound column; both rows are the missing token.
    private const string MissingOnlyData = "colour\n?\n?\n";

    // ---- the library oracle -------------------------------------------------------------

    private static BindingSection Wide(char? delimiter = null, bool? hasHeader = null) =>
        new(SourceShape.Wide, null, delimiter, null, hasHeader, null, null, null, null, null);

    private static string LibraryMigrate(
        string text, string bedPath, BindingSection binding, ScalingMode mode = ScalingMode.Discrete)
    {
        var read = BedReader.Read(text, bedPath);
        Assert.True(read.TryGetValue(out var bed));
        var migrated = BedMigrator.Migrate(bed, binding, mode, derivedFrom: bedPath);
        Assert.True(migrated.TryGetValue(out var document));
        return SpecWriter.Write(document);
    }

    // A real v2 fixture, so the canonical-bytes cases run over something richer than a
    // hand-authored stub. It is read, never written.
    private static string BedText() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "v2", "mini-mushroom", "mini-mushroom.bed"));

    private static string WriteMushroomBed(TempDirectory temp) => temp.Write("mini.bed", BedText());

    // ---- canonical bytes through argv ----------------------------------------------------

    [Fact]
    public async Task Migrate_WhenABedIsMigrated_ThenStdoutIsExactlyTheLibraryCanonicalText()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "migrate", bed, "--out", "-", "--shape", "wide", "--delimiter", ",", "--header", "true");

        Assert.Equal(0, exit);
        Assert.Equal(LibraryMigrate(BedText(), bed, Wide(',', true)), harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
    }

    [Fact]
    public async Task Migrate_WhenOnlySomeReadSettingsAreSupplied_ThenOnlyThoseAndTheShapeAreAuthored()
    {
        // The presence rule (§10.2), asserted line by line: an unsupplied setting is ABSENT,
        // never defaulted into the document, because omitted-versus-authored is exactly what
        // the canonical writer exists to preserve.
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide", "--delimiter", ";");

        Assert.Equal(["shape = \"wide\"", "delimiter = \";\""], Section(harness.StdOut, "[binding]"));
    }

    [Fact]
    public async Task Migrate_WhenShapeIsOmitted_ThenWideIsAuthored()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", "-");

        Assert.Equal(0, exit);
        Assert.Equal(["shape = \"wide\""], Section(harness.StdOut, "[binding]"));
        Assert.Equal(LibraryMigrate(BedText(), bed, Wide()), harness.StdOut);
    }

    [Fact]
    public async Task Migrate_WhenShapeIsTriple_ThenOrderingUnorderedIsAlwaysAuthored()
    {
        // There is no --ordering option anywhere in the grammar (D-123 point 14): triple output
        // states unordered because migration must produce a resolvable spec, not because the
        // invocation asked for it.
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "triple");

        Assert.Equal(0, exit);
        Assert.Equal(["shape = \"triple\"", "ordering = \"unordered\""], Section(harness.StdOut, "[binding]"));
    }

    [Fact]
    public async Task Migrate_WhenScalingIsOmitted_ThenDiscreteNominalScalesAreAuthored()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide");

        Assert.Equal(LibraryMigrate(BedText(), bed, Wide(), ScalingMode.Discrete), harness.StdOut);
        Assert.DoesNotContain("kind = \"ordinal\"", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_WhenScalingIsProgressive_ThenOrdinalScalesAreAuthored()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide", "--scaling", "progressive");

        Assert.Equal(LibraryMigrate(BedText(), bed, Wide(), ScalingMode.Progressive), harness.StdOut);
    }

    [Fact]
    public async Task Migrate_WhenABedIsMigrated_ThenDerivedFromIsTheInvokedOperandVerbatim()
    {
        // A redundant `.` segment survives: the operand is authored provenance, so it is
        // neither normalized nor made absolute.
        using var temp = TempDirectory.Create();
        temp.Write("mini.bed", BedText());
        var spelled = Path.Combine(temp.Path, ".", "mini.bed");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", spelled, "--out", "-");

        Assert.Equal(0, exit);
        Assert.Equal(LibraryMigrate(BedText(), spelled, Wide()), harness.StdOut);
        Assert.Contains($"derived_from = \"{Escaped(spelled)}\"", harness.StdOut, StringComparison.Ordinal);
    }

    // ---- object key and roles -------------------------------------------------------------

    [Fact]
    public async Task Migrate_WhenObjectKeyIsOmitted_ThenNoObjectKeySectionIsAuthored()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide");

        Assert.DoesNotContain("[binding.object_key]", harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_WhenObjectKeyIsRowIndex_ThenTheRowIndexModeIsAuthored()
    {
        // It resolves identically to the wide default, and it is still authored: silently
        // dropping what the user typed would be a special case with no rule behind it.
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "migrate", bed, "--out", "-", "--shape", "wide", "--object-key", "row_index");

        Assert.Equal(0, exit);
        Assert.Equal(["mode = \"row_index\""], Section(harness.StdOut, "[binding.object_key]"));
    }

    [Theory]
    [InlineData("0", "column = 0")]
    [InlineData("id", "column = \"id\"")]
    public async Task Migrate_WhenObjectKeyIsColumnWithItsColumn_ThenTheKeySectionIsAuthored(
        string spelling, string expected)
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "migrate", bed, "--out", "-", "--shape", "wide",
            "--object-key", "column", "--object-key-column", spelling);

        Assert.Equal(0, exit);
        Assert.Equal(["mode = \"column\"", expected], Section(harness.StdOut, "[binding.object_key]"));
    }

    [Theory]
    [InlineData("0", "1", "2", "columns = { subject = 0, predicate = 1, value = 2 }")]
    [InlineData("s", "p", "v", "columns = { subject = \"s\", predicate = \"p\", value = \"v\" }")]
    public async Task Migrate_WhenRolesAreSupplied_ThenTheAddressingModeIsPreservedVerbatim(
        string subject, string predicate, string value, string expected)
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();
        string[] header = subject == "0" ? [] : ["--header", "true"];

        var exit = await harness.RunAsync(
            [
                "migrate", bed, "--out", "-", "--shape", "triple",
                "--subject", subject, "--predicate", predicate, "--value", value, .. header,
            ]);

        Assert.Equal(0, exit);
        Assert.Contains(expected, Section(harness.StdOut, "[binding]"));
    }

    // ---- authored-empty domains (D-122 part 15) --------------------------------------------

    [Fact]
    public async Task Migrate_WhenTheWholeDomainIsTheMissingToken_ThenDeclaredDomainIsAuthoredEmptyThroughArgv()
    {
        // The authored [] survives as a FIXED EMPTY domain, and `missing_policy` still adds the
        // missing column — so a follow-on convert plans exactly one formal attribute.
        using var temp = TempDirectory.Create();
        var bed = temp.Write("empty.bed", EmptyDomainBed);
        var spec = temp.Resolve("spec.toml");
        var data = temp.Write("data.csv", MissingOnlyData);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync(
            "migrate", bed, "--out", spec, "--shape", "wide", "--header", "true");

        var text = await File.ReadAllTextAsync(spec, new UTF8Encoding(false));
        Assert.Equal(0, exit);
        Assert.Contains("declared_domain = []", text, StringComparison.Ordinal);
        Assert.Contains("missing_policy = \"as_attribute\"", text, StringComparison.Ordinal);

        var converted = new CliTestHarness();
        Assert.Equal(0, await converted.RunAsync(
            "convert", spec, data, "--out", temp.Resolve("out"), "--format", "cxt", "--no-manifest"));

        var cxt = (await File.ReadAllTextAsync(temp.Resolve("out.cxt"))).Split('\n');
        Assert.Equal("2", cxt[2].Trim());
        Assert.Equal("1", cxt[3].Trim());
        Assert.Contains("colour-missing", cxt);
    }

    [Fact]
    public async Task Migrate_WhenTheOutputIsRereadAndRewritten_ThenTheCanonicalTextIsIdempotent()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide", "--header", "true");

        var read = SpecReader.Read(harness.StdOut, "spec.toml");
        Assert.True(read.TryGetValue(out var document));
        Assert.Equal(harness.StdOut, SpecWriter.Write(document));
    }

    // ---- failures the CLI owns, and failures the library owns -------------------------------

    [Fact]
    public async Task Migrate_WhenTheBedFileIsMissing_ThenItIsACodeLessHostErrorAndExitIsOne()
    {
        using var temp = TempDirectory.Create();
        var absent = temp.Resolve("absent.bed");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", absent, "--out", "-");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError($"cannot read the .bed file '{absent}'."), harness.StdErr);
    }

    [Fact]
    public async Task Migrate_WhenAnIncludedAttributeIsADeferredValueType_ThenTheMigratorsOwnDiagnosticIsReportedAndNothingIsWritten()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("date.bed", DateBed);
        var target = temp.Resolve("spec.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("error BedDateTypeNotSupported:", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("error: ", harness.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(target), "an Error must publish nothing");
    }

    // ---- delivery and the publication matrix -------------------------------------------------

    [Fact]
    public async Task Migrate_WhenOutIsStdout_ThenStdoutIsExactlyTheDocumentAndNothingElse()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("warn.bed", WarningBed);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide");

        Assert.Equal(0, exit);
        Assert.Equal(LibraryMigrate(WarningBed, bed, Wide()), harness.StdOut);
        Assert.Contains("warning BedMissingTokenLabelDropped:", harness.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_WhenTheTargetExistsWithoutForce_ThenItIsRefusedAndTheOldFileIsUntouched()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var target = temp.Write("spec.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", target);

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"the output '{target}' already exists; use --force to replace it."),
            harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Migrate_WhenForceIsSupplied_ThenTheDistinctTargetIsReplaced()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var target = temp.Write("spec.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide", "--force");

        Assert.Equal(0, exit);
        Assert.Equal(
            LibraryMigrate(BedText(), bed, Wide()),
            await File.ReadAllTextAsync(target, new UTF8Encoding(false)));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Fact]
    public async Task Migrate_WhenTheOutputIsTheBedFile_ThenItIsRefusedEvenWithForce()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteMushroomBed(temp);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", bed, "--force");

        Assert.Equal(1, exit);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"the output '{bed}' and the input '{bed}' are the same file."),
            harness.StdErr);
        Assert.Equal(BedText(), await File.ReadAllTextAsync(bed));
    }

    // ---- the deferred-diagnostic ordering ----------------------------------------------------

    [Fact]
    public async Task Migrate_WhenAWarningIsProducedAndTheFileCommits_ThenTheWarningIsRenderedOnceAfterCommitAndExitIsZero()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("warn.bed", WarningBed);
        var target = temp.Resolve("spec.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Single(Lines(harness.StdErr));
        Assert.Contains("warning BedMissingTokenLabelDropped:", harness.StdErr, StringComparison.Ordinal);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Migrate_WhenAWarningIsProducedAndPublicationFails_ThenTheWarningAndOneHostLineAreRenderedOnceAndExitIsOne()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("warn.bed", WarningBed);
        var target = temp.Write("spec.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.FailKind = "Move";
        harness.PublicationFiles.FailMoveTo = "spec.toml";

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide", "--force");

        var lines = Lines(harness.StdErr);
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(
            "attribute=\"colour\": warning BedMissingTokenLabelDropped:", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            DiagnosticRenderer.RenderHostError($"cannot publish the output '{target}'.").TrimEnd('\n'), lines[1]);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Migrate_WhenSeveralWarningsAreProduced_ThenTheyRenderExactlyOnceInLibraryOrder()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("two.bed", TwoWarningBed);
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", temp.Resolve("spec.toml"), "--shape", "wide");

        var lines = Lines(harness.StdErr);
        Assert.Equal(0, exit);
        Assert.Equal(2, lines.Length);
        Assert.Contains("attribute=\"colour\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("attribute=\"size\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_WhenAWarningIsProducedAndTheRunIsCancelledAtStageCreation_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("warn.bed", WarningBed);
        var target = temp.Write("spec.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.MutateBefore = "Confidential:spec.toml.fcabedrock-stage-T";
        harness.PublicationFiles.Mutate = harness.Signals.Cancel;

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide", "--force");

        Assert.Equal(3, exit);
        await AssertSilentCancellationAsync(harness, temp, target);
    }

    [Fact]
    public async Task Migrate_WhenAWarningIsProducedAndTheRunIsCancelledAtTheBackupTransition_ThenExitIsThreeWithNoOutputAtAll()
    {
        using var temp = TempDirectory.Create();
        var bed = temp.Write("warn.bed", WarningBed);
        var target = temp.Write("spec.toml", "the old file");
        var harness = new CliTestHarness();
        harness.PublicationFiles.CancelAfterMoveToPrefix = "spec.toml.fcabedrock-backup-";
        harness.PublicationFiles.CancelAfterMove = harness.Signals.Cancel;

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide", "--force");

        Assert.Equal(3, exit);
        await AssertSilentCancellationAsync(harness, temp, target);
    }

    // ---- the ruled BED decoding contract (strict UTF-8, one optional UTF-8 BOM) ---------------
    //
    // Every vector is assembled from byte literals, so no source-file or editor encoding can
    // alter what the decoder is actually handed.

    private const string Replacement = "�";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    // "é,ü" — U+00E9 encodes as C3 A9 and U+00FC as C3 BC.
    private static readonly byte[] NonAscii = [0xC3, 0xA9, 0x2C, 0xC3, 0xBC];

    // The skeleton with `values` standing where authored category values go, so a malformed
    // sequence meets the decoder exactly where real content would.
    private static byte[] BedWith(byte[] categories, byte[] values) =>
    [
        .. Encoding.ASCII.GetBytes("[Number of Attributes]\n1\n\n[Attributes]\ncolour\n\n[Attribute Categories]\n"),
        .. categories,
        .. Encoding.ASCII.GetBytes("\n\n[Category Values]\n"),
        .. values,
        .. Encoding.ASCII.GetBytes(
            "\n\n[Convert Attribute]\nTrue\n\n[Attribute Type]\nc\n\n[Restrict To Values]\n\n\n[End]\n"),
    ];

    private static byte[] NonAsciiBed() => BedWith(NonAscii, NonAscii);

    public static TheoryData<string, byte[]> RejectedBytes() => new()
    {
        { "invalid lead byte", BedWith([0x72], [0xFF]) },
        { "truncated sequence", BedWith([0x72], [0xC3]) },
        { "overlong encoding", BedWith([0x72], [0xC0, 0xAF]) },
        { "UTF-16 LE BOM", [0xFF, 0xFE, .. NonAsciiBed()] },
        { "UTF-16 BE BOM", [0xFE, 0xFF, .. NonAsciiBed()] },
        { "UTF-32 LE BOM", [0xFF, 0xFE, 0x00, 0x00, .. NonAsciiBed()] },
        { "UTF-32 BE BOM", [0x00, 0x00, 0xFE, 0xFF, .. NonAsciiBed()] },
    };

    [Fact]
    public async Task Migrate_WhenTheBedIsValidUtf8WithoutABom_ThenItMigratesAndTheCanonicalBytesRoundTrip()
    {
        using var temp = TempDirectory.Create();
        var bed = WriteBytes(temp, "nonascii.bed", NonAsciiBed());
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", "-", "--shape", "wide");

        Assert.Equal(0, exit);
        Assert.Contains("declared_domain = [\"é\", \"ü\"]", harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(Replacement, harness.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_WhenTheBedCarriesOneUtf8Bom_ThenTheBomIsRemovedAndTheOutputIsIdenticalToTheBomFreeInput()
    {
        // The strongest statement of "that one mark is removed and nothing else changes": the
        // two runs differ only in the operand their provenance legitimately records.
        using var temp = TempDirectory.Create();
        var plain = WriteBytes(temp, "plain.bed", NonAsciiBed());
        var marked = WriteBytes(temp, "marked.bed", [.. Utf8Bom, .. NonAsciiBed()]);
        var first = new CliTestHarness();
        var second = new CliTestHarness();

        Assert.Equal(0, await first.RunAsync("migrate", plain, "--out", "-", "--shape", "wide"));
        Assert.Equal(0, await second.RunAsync("migrate", marked, "--out", "-", "--shape", "wide"));

        // derived_from legitimately differs, and reaches the document as a TOML basic string,
        // so the operand is normalized out in its escaped spelling.
        Assert.Equal(
            first.StdOut.Replace(Escaped(plain), "BED", StringComparison.Ordinal),
            second.StdOut.Replace(Escaped(marked), "BED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migrate_WhenTheBedCarriesASecondBomSequence_ThenOnlyTheFirstIsRemovedAndTheRestIsContent()
    {
        // Exactly one mark is consumed, so the second becomes the first character of the
        // authored text — which is then not a section header, so the READER refuses it. That
        // is a registry diagnostic, never the decoder's code-less rejection: keeping the two
        // apart is the whole point of this row.
        using var temp = TempDirectory.Create();
        var bed = WriteBytes(temp, "twobom.bed", [.. Utf8Bom, .. Utf8Bom, .. NonAsciiBed()]);
        var target = temp.Resolve("spec.toml");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Contains("BedStructureInvalid:", harness.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("error: ", harness.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    [Theory]
    [MemberData(nameof(RejectedBytes))]
    public async Task Migrate_WhenTheBedIsNotAcceptedUtf8_ThenItIsRefusedWithOneSanitizedLine(
        string reason, byte[] bytes)
    {
        // Every rejection is identical and total: exit 1, empty stdout, exactly one sanitized
        // code-less line naming the authored operand, no target created, and a pre-existing
        // target byte-identical EVEN WITH --force, because the refusal precedes publication.
        Assert.NotEmpty(reason);
        using var temp = TempDirectory.Create();
        var bed = WriteBytes(temp, "input.bed", bytes);
        var target = temp.Write("spec.toml", "the old file");
        var harness = new CliTestHarness();

        var exit = await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide", "--force");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(DiagnosticRenderer.RenderHostError($"cannot read the .bed file '{bed}'."), harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    [Theory]
    [MemberData(nameof(RejectedBytes))]
    public async Task Migrate_WhenAnyInputIsRejected_ThenNoReplacementCharacterReachesAnyOutput(
        string reason, byte[] bytes)
    {
        // No repair, no guess, no fallback: U+FFFD must reach neither stream, and no file may
        // be left behind for it to reach.
        Assert.NotEmpty(reason);
        using var temp = TempDirectory.Create();
        var bed = WriteBytes(temp, "input.bed", bytes);
        var target = temp.Resolve("spec.toml");
        var harness = new CliTestHarness();

        await harness.RunAsync("migrate", bed, "--out", target, "--shape", "wide");

        Assert.DoesNotContain(Replacement, harness.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(Replacement, harness.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string WriteBytes(TempDirectory temp, string name, byte[] bytes)
    {
        var path = temp.Resolve(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // The non-blank lines of one canonical section, up to the next section header — the
    // presence lock's reading of "only these keys are authored".
    private static string[] Section(string document, string header)
    {
        var lines = document.Split('\n');
        var start = Array.IndexOf(lines, header);
        Assert.True(start >= 0, $"{header} is absent");

        var body = new List<string>();
        for (var i = start + 1; i < lines.Length && !lines[i].StartsWith('['); i++)
        {
            if (lines[i].Trim() is { Length: > 0 } line)
            {
                body.Add(line);
            }
        }

        return [.. body];
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // A path as a TOML basic string spells its separators escaped (D-075).
    private static string Escaped(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static async Task AssertSilentCancellationAsync(
        CliTestHarness harness, TempDirectory temp, string target)
    {
        Assert.Equal(string.Empty, harness.StdOut);
        Assert.Equal(string.Empty, harness.StdErr);
        Assert.Equal("the old file", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(Directory.GetFiles(temp.Path), Residue);
    }

    private static bool Residue(string path) =>
        Path.GetFileName(path).Contains(".fcabedrock-", StringComparison.Ordinal);
}
