using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Byte locks on the generated usage and help text, and on the version string. These are
/// the CLI's own bytes — the reason a library-owned parser was rejected (D-123 part 2).
/// <para>
/// <b>The expectations are authored, not derived.</b> Nothing here calls
/// <see cref="UsageText.Signature"/>, enumerates <see cref="CommandTable"/>, or reuses a
/// summary, note, or allowed-value collection: an expectation built from the production
/// table would only prove that the renderer copied its input, and would move in lockstep
/// with any wording or grammar edit. The line array below is the reviewed help document,
/// written out; it is joined with explicit LF so the lock is independent of how this source
/// file happens to be checked out.
/// </para>
/// </summary>
public sealed class UsageTextTests
{
    private static readonly string[] ExpectedHelpLines =
    [
        "fcabedrock - Formal Concept Analysis preprocessing (FcaBedrock vNext)",
        "",
        "usage: fcabedrock <command> [operands] [options]",
        "",
        "commands:",
        "  convert      Convert a data source into a formal context using a Bedrock spec.",
        "  validate     Validate a Bedrock spec, optionally against a data source's schema.",
        "  plan         Print the conversion plan and the three native fingerprints; write nothing.",
        "  stats        Print formal-context statistics; write nothing.",
        "  calibrate    Freeze every data-dependent outcome into a standalone spec.",
        "  probe        Generate a draft spec from a data source.",
        "  migrate      Migrate a v2 .bed file to a Bedrock spec.",
        "  fingerprint  Report the three native fingerprints and each stored field's state.",
        "",
        "convert - Convert a data source into a formal context using a Bedrock spec.",
        "  usage: fcabedrock convert SPEC DATA --out BASE --format cxt|dat|both [--v2-compat] [--no-manifest] [--force] [--temp-dir DIR]",
        "    note: --out names a base; the ruled extension is appended, and there is no default or inferred format.",
        "    note: --force authorizes replacing an existing distinct destination.",
        "",
        "validate - Validate a Bedrock spec, optionally against a data source's schema.",
        "  usage: fcabedrock validate SPEC [DATA]",
        "    note: With DATA, only the source schema is acquired; no data rows are read and nothing is written.",
        "",
        "plan - Print the conversion plan and the three native fingerprints; write nothing.",
        "  usage: fcabedrock plan SPEC DATA [--temp-dir DIR]",
        "    note: DATA is required for every plan; rows are read only when calibration requires them.",
        "",
        "stats - Print formal-context statistics; write nothing.",
        "  usage: fcabedrock stats SPEC DATA [--temp-dir DIR]",
        "",
        "calibrate - Freeze every data-dependent outcome into a standalone spec.",
        "  usage: fcabedrock calibrate SPEC DATA --out PATH|- [--force] [--temp-dir DIR]",
        "    note: --out - writes to stdout; --force is a file-target option and is rejected with --out -.",
        "",
        "probe - Generate a draft spec from a data source.",
        "  usage: fcabedrock probe DATA --shape wide|triple --out PATH|- [--force] [--delimiter CHAR] [--header true|false] [--missing-token TOKEN] [--locale TAG|invariant] [--limit N] [--ordering subject_grouped|unordered] [--subject N|NAME] [--predicate N|NAME] [--value N|NAME]",
        "    note: --ordering, --subject, --predicate and --value require --shape triple.",
        "    note: --subject, --predicate and --value are supplied together in one addressing mode: all zero-based indices, or all header names, and names require --header true.",
        "    note: --out - writes to stdout; --force is a file-target option and is rejected with --out -.",
        "",
        "migrate - Migrate a v2 .bed file to a Bedrock spec.",
        "  usage: fcabedrock migrate BED --out PATH|- [--force] [--shape wide|triple] [--delimiter CHAR] [--header true|false] [--locale TAG|invariant] [--missing-token TOKEN] [--scaling discrete|progressive] [--object-key row_index|column] [--object-key-column N|NAME] [--subject N|NAME] [--predicate N|NAME] [--value N|NAME]",
        "    note: --shape defaults to wide and --scaling defaults to discrete.",
        "    note: --object-key and --object-key-column require the wide shape, and --object-key-column is required exactly when --object-key column.",
        "    note: --subject, --predicate and --value require --shape triple and follow probe's addressing rules.",
        "    note: Triple output always authors ordering = \"unordered\"; there is no --ordering option.",
        "    note: --out - writes to stdout; --force is a file-target option and is rejected with --out -.",
        "",
        "fingerprint - Report the three native fingerprints and each stored field's state.",
        "  usage: fcabedrock fingerprint SPEC DATA [--write --out NEW_SPEC|-] [--force] [--temp-dir DIR]",
        "    note: --out and --force are valid only with --write, and --write requires --out.",
        "    note: There is no --v2-compat: v2 byte compatibility is a convert-only override.",
        "",
        "global options:",
        "  --help     Print this help and exit.",
        "  --version  Print the tool version and exit.",
    ];

    private static readonly string[] ExpectedGeneralLines =
    [
        "usage: fcabedrock <command> [operands] [options]",
        "",
        "commands:",
        "  convert      Convert a data source into a formal context using a Bedrock spec.",
        "  validate     Validate a Bedrock spec, optionally against a data source's schema.",
        "  plan         Print the conversion plan and the three native fingerprints; write nothing.",
        "  stats        Print formal-context statistics; write nothing.",
        "  calibrate    Freeze every data-dependent outcome into a standalone spec.",
        "  probe        Generate a draft spec from a data source.",
        "  migrate      Migrate a v2 .bed file to a Bedrock spec.",
        "  fingerprint  Report the three native fingerprints and each stored field's state.",
        "",
        "run 'fcabedrock --help' for the full grammar.",
    ];

    private static string Document(string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void Help_WhenRendered_ThenItIsExactlyTheReviewedDocument()
    {
        Assert.Equal(Document(ExpectedHelpLines), UsageText.Help);
    }

    [Fact]
    public void General_WhenRendered_ThenItIsExactlyTheReviewedDocument()
    {
        Assert.Equal(Document(ExpectedGeneralLines), UsageText.General);
    }

    [Theory]
    [InlineData("convert", "convert SPEC DATA --out BASE --format cxt|dat|both [--v2-compat] [--no-manifest] [--force] [--temp-dir DIR]")]
    [InlineData("validate", "validate SPEC [DATA]")]
    [InlineData("plan", "plan SPEC DATA [--temp-dir DIR]")]
    [InlineData("stats", "stats SPEC DATA [--temp-dir DIR]")]
    [InlineData("calibrate", "calibrate SPEC DATA --out PATH|- [--force] [--temp-dir DIR]")]
    [InlineData("probe", "probe DATA --shape wide|triple --out PATH|- [--force] [--delimiter CHAR] [--header true|false] [--missing-token TOKEN] [--locale TAG|invariant] [--limit N] [--ordering subject_grouped|unordered] [--subject N|NAME] [--predicate N|NAME] [--value N|NAME]")]
    [InlineData("migrate", "migrate BED --out PATH|- [--force] [--shape wide|triple] [--delimiter CHAR] [--header true|false] [--locale TAG|invariant] [--missing-token TOKEN] [--scaling discrete|progressive] [--object-key row_index|column] [--object-key-column N|NAME] [--subject N|NAME] [--predicate N|NAME] [--value N|NAME]")]
    [InlineData("fingerprint", "fingerprint SPEC DATA [--write --out NEW_SPEC|-] [--force] [--temp-dir DIR]")]
    public void Signature_WhenGenerated_ThenItIsTheSettledGrammar(string command, string expected)
    {
        Assert.Equal(expected, UsageText.Signature(CommandTable.Find(command)!));
    }

    [Fact]
    public void For_WhenACommandIsNamed_ThenItsUsageLineAndNotesRender()
    {
        Assert.Equal(
            "usage: fcabedrock validate SPEC [DATA]\n"
            + "  note: With DATA, only the source schema is acquired; no data rows are read and nothing is written.\n",
            UsageText.For("validate"));
    }

    [Fact]
    public void For_WhenTheCommandIsUnknown_ThenTheGeneralUsageIsReturned()
    {
        Assert.Equal(UsageText.General, UsageText.For("nope"));
    }

    // ---- supplemental structural coverage (deliberately table-derived) ------------------
    //
    // These do NOT lock bytes — the literals above do. They prove the generated document
    // stays structurally complete as the table changes, which a literal alone cannot say.

    [Fact]
    public void Help_WhenRendered_ThenEveryCommandBlockAppearsExactlyOnce()
    {
        foreach (var command in CommandTable.Commands)
        {
            var block = $"\n{command.Name} - {command.Summary}\n  usage: fcabedrock {UsageText.Signature(command)}\n";
            var first = UsageText.Help.IndexOf(block, StringComparison.Ordinal);

            Assert.True(first >= 0, $"help does not contain the block for '{command.Name}'");
            Assert.Equal(-1, UsageText.Help.IndexOf(block, first + 1, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Help_WhenRendered_ThenEveryConditionalNoteIsCarried()
    {
        foreach (var command in CommandTable.Commands)
        {
            foreach (var note in command.Notes)
            {
                Assert.Contains($"    note: {note}\n", UsageText.Help, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Help_WhenRendered_ThenEveryEnumeratedOptionAdvertisesExactlyItsAcceptedValues()
    {
        // The advertised form is derived from the accepted set, so help cannot name a value
        // the parser rejects, or omit one it accepts (CX-M7F-011).
        foreach (var command in CommandTable.Commands)
        {
            foreach (var option in command.Options)
            {
                if (option.Kind != OptionValueKind.Enumerated)
                {
                    continue;
                }

                Assert.Contains(
                    $"{option.Name} {string.Join("|", option.AllowedValues!)}",
                    UsageText.Signature(command),
                    StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("--sample")]
    [InlineData("--gzip")]
    [InlineData("--color")]
    [InlineData("--progress")]
    [InlineData("--machine")]
    [InlineData("--stats")]
    public void Help_WhenRendered_ThenItAdvertisesNoExcludedOrDeferredFlag(string flag)
    {
        Assert.DoesNotContain(flag, UsageText.Help, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_WhenRendered_ThenMigrateAdvertisesNoOrdering()
    {
        // FBL-M7P-001: triple migrate always authors ordering = "unordered"; the option
        // must not exist, and must not be advertised. (probe's --ordering is real, so this
        // is asserted on migrate's own block.)
        Assert.Null(CommandTable.Find("migrate")!.Option("--ordering"));
        Assert.DoesNotContain("--ordering", UsageText.Signature(CommandTable.Find("migrate")!), StringComparison.Ordinal);
    }

    [Fact]
    public void Help_WhenRendered_ThenFingerprintAdvertisesNoV2Compat()
    {
        Assert.Null(CommandTable.Find("fingerprint")!.Option("--v2-compat"));
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("migrate")]
    public void CommandTable_WhenInspected_ThenTempDirIsAbsentFromNonGroupingCommands(string command)
    {
        // D-122 part 8: --temp-dir exists only on the five grouping-capable commands.
        Assert.Null(CommandTable.Find(command)!.Option("--temp-dir"));
    }

    [Theory]
    [InlineData("convert")]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("calibrate")]
    [InlineData("fingerprint")]
    public void CommandTable_WhenInspected_ThenTempDirIsPresentOnGroupingCommands(string command)
    {
        Assert.NotNull(CommandTable.Find(command)!.Option("--temp-dir"));
    }

    [Fact]
    public void CommandTable_WhenInspected_ThenProbeHasNoTempDir()
    {
        // probe has no spill machinery (D-110), so it takes no --temp-dir.
        Assert.Null(CommandTable.Find("probe")!.Option("--temp-dir"));
    }

    [Theory]
    [InlineData("help")]
    [InlineData("general")]
    public void Text_WhenRendered_ThenLineEndingsAreLfWithAFinalLfAndNoBomOrNonAscii(string which)
    {
        var text = which == "help" ? UsageText.Help : UsageText.General;

        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);

        // Pure ASCII, which also rules out a U+FEFF byte-order mark anywhere in the text.
        Assert.All(text, character => Assert.True(character < 0x80, $"non-ASCII character U+{(int)character:X4}"));

        // No BOM at the byte level either, on the encoding the CLI writes with.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        Assert.NotEqual<byte>([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
    }

    [Fact]
    public void ToolVersion_WhenComposed_ThenItCarriesTheProductPrefix()
    {
        Assert.Equal("fcabedrock-vnext 1.0.0", ToolVersion.Current);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abc1234", "1.2.3")]
    [InlineData("1.0.0-preview.1+build.99", "1.0.0-preview.1")]
    [InlineData("1.0.0+", "1.0.0")]
    [InlineData("+meta", "")]
    public void StripBuildMetadata_WhenAPlusSuffixIsPresent_ThenItIsRemoved(string version, string expected)
    {
        Assert.Equal(expected, ToolVersion.StripBuildMetadata(version));
    }

    [Fact]
    public void ToolVersion_WhenComposedFromAnAssembly_ThenItMatchesThatAssemblysStrippedInformationalVersion()
    {
        var assembly = typeof(CliHost).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single()
            .InformationalVersion;

        Assert.Equal(ToolVersion.Prefix + ToolVersion.StripBuildMetadata(informational), ToolVersion.Compose(assembly));
    }
}
