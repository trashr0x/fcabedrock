namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The grammar matrix for all eight settled commands (D-122 part 1/part 10). Every
/// accepted option is exercised, every conditional rule is asserted in both directions,
/// and the excluded surface is asserted rejected.
/// </summary>
public sealed class CommandLineParserTests
{
    // One fully-loaded invocation per command (two per shape-bearing command). The
    // coverage test below proves this list touches EVERY option in the table, so a new
    // option cannot be added without appearing here.
    private static readonly string[][] FullyLoaded =
    [
        ["convert", "s.toml", "d.csv", "--out", "base", "--format", "both", "--v2-compat", "--no-manifest", "--force", "--temp-dir", "t"],
        ["validate", "s.toml", "d.csv"],
        ["plan", "s.toml", "d.csv", "--temp-dir", "t"],
        ["stats", "s.toml", "d.csv", "--temp-dir", "t"],
        ["calibrate", "s.toml", "d.csv", "--out", "o.toml", "--force", "--temp-dir", "t"],
        ["probe", "d.csv", "--shape", "wide", "--out", "o.toml", "--force", "--delimiter", ";", "--header", "true", "--missing-token", "NA", "--locale", "fr-FR", "--limit", "500"],
        ["probe", "d.csv", "--shape", "triple", "--out", "o.toml", "--ordering", "unordered", "--header", "true", "--subject", "s", "--predicate", "p", "--value", "v"],
        ["migrate", "x.bed", "--out", "o.toml", "--force", "--shape", "wide", "--delimiter", ",", "--header", "true", "--locale", "invariant", "--missing-token", "?", "--scaling", "progressive", "--object-key", "column", "--object-key-column", "0"],
        ["migrate", "x.bed", "--out", "-", "--shape", "triple", "--subject", "0", "--predicate", "1", "--value", "2"],
        ["fingerprint", "s.toml", "d.csv", "--write", "--out", "n.toml", "--force", "--temp-dir", "t"],
    ];

    public static TheoryData<string[]> AcceptedInvocations()
    {
        var data = new TheoryData<string[]>();
        foreach (var argv in FullyLoaded)
        {
            data.Add(argv);
        }

        return data;
    }

    private static CommandInvocation Accept(params string[] argv)
    {
        var outcome = CommandLineParser.Parse(argv);
        Assert.IsType<CommandInvocation>(outcome);
        return (CommandInvocation)outcome;
    }

    private static UsageFailure Reject(params string[] argv)
    {
        var outcome = CommandLineParser.Parse(argv);
        Assert.IsType<UsageFailure>(outcome);
        return (UsageFailure)outcome;
    }

    [Theory]
    [MemberData(nameof(AcceptedInvocations))]
    public void Parse_WhenEveryOptionOfACommandIsSupplied_ThenItParses(string[] argv)
    {
        var invocation = Accept(argv);

        Assert.Equal(argv[0], invocation.Command.Name);
    }

    [Fact]
    public void Grammar_ShouldExerciseEveryDeclaredOptionOfEveryCommand()
    {
        // Without this the matrix above could quietly stop covering the table.
        foreach (var command in CommandTable.Commands)
        {
            var exercised = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argv in FullyLoaded)
            {
                if (!string.Equals(argv[0], command.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var token in argv)
                {
                    if (token.StartsWith("--", StringComparison.Ordinal))
                    {
                        exercised.Add(token);
                    }
                }
            }

            var declared = command.Options.Select(option => option.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(declared.OrderBy(n => n, StringComparer.Ordinal), exercised.OrderBy(n => n, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Commands_ShouldBeExactlyTheEightSettledOnes()
    {
        Assert.Equal(
            ["convert", "validate", "plan", "stats", "calibrate", "probe", "migrate", "fingerprint"],
            CommandTable.Commands.Select(command => command.Name));
    }

    // ---- operands -------------------------------------------------------------------

    [Fact]
    public void Parse_WhenOptionalOperandIsOmitted_ThenItParses()
    {
        var invocation = Accept("validate", "s.toml");

        Assert.Equal("s.toml", invocation.Operand(0));
        Assert.Null(invocation.Operand(1));
    }

    [Theory]
    [InlineData("convert")]
    [InlineData("validate")]
    [InlineData("plan")]
    [InlineData("stats")]
    [InlineData("calibrate")]
    [InlineData("probe")]
    [InlineData("migrate")]
    [InlineData("fingerprint")]
    public void Parse_WhenARequiredOperandIsMissing_ThenUsageFailure(string command)
    {
        var failure = Reject(command);

        Assert.Equal(command, failure.Command);
        Assert.Contains("requires the", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenTooManyOperandsAreSupplied_ThenUsageFailure()
    {
        var failure = Reject("validate", "s.toml", "d.csv", "extra");

        Assert.Contains("'extra' is extra", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validate", "-")]
    [InlineData("convert", "-")]
    [InlineData("probe", "-")]
    [InlineData("migrate", "-")]
    public void Parse_WhenAnOperandIsADash_ThenUsageFailureBecauseThereIsNoStdinSource(string command, string operand)
    {
        var failure = Reject(command, operand);

        Assert.Contains("no stdin source", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenSecondOperandIsADash_ThenUsageFailure()
    {
        var failure = Reject("plan", "s.toml", "-");

        Assert.Contains("no stdin source", failure.Message, StringComparison.Ordinal);
    }

    // ---- option shape ---------------------------------------------------------------

    [Fact]
    public void Parse_WhenAnOptionIsRepeated_ThenUsageFailure()
    {
        var failure = Reject("plan", "s.toml", "d.csv", "--temp-dir", "a", "--temp-dir", "b");

        Assert.Contains("more than once", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAFlagIsRepeated_ThenUsageFailure()
    {
        var failure = Reject(
            "convert", "s.toml", "d.csv", "--out", "b", "--format", "cxt", "--force", "--force");

        Assert.Contains("more than once", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAnOptionValueIsMissingAtTheEnd_ThenUsageFailure()
    {
        var failure = Reject("plan", "s.toml", "d.csv", "--temp-dir");

        Assert.Contains("requires a value", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAnOptionValueWouldBeAnotherOption_ThenUsageFailure()
    {
        // The next token starting with `--` is the next option, never a value: this must
        // not silently consume --format as the --out path.
        var failure = Reject("convert", "s.toml", "d.csv", "--out", "--format", "cxt");

        Assert.Contains("'--out' requires a value", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAnOptionValueIsEmpty_ThenItIsAccepted()
    {
        // §5.1: missing_token = "" disables token-based missing detection, so an empty
        // value is legal and must survive parsing.
        var invocation = Accept(
            "probe", "d.csv", "--shape", "wide", "--out", "o.toml", "--missing-token", string.Empty);

        Assert.Equal(string.Empty, invocation.Value("--missing-token"));
    }

    [Fact]
    public void Parse_WhenAValueLooksLikeAShortFlag_ThenItIsStillAValue()
    {
        // Only `--` prefixes are treated as options in value position, so a single-dash
        // delimiter value is data.
        var invocation = Accept("probe", "d.csv", "--shape", "wide", "--out", "o.toml", "--delimiter", "-");

        Assert.Equal("-", invocation.Value("--delimiter"));
    }

    [Fact]
    public void Parse_WhenAnUnknownOptionIsSupplied_ThenUsageFailure()
    {
        var failure = Reject("validate", "s.toml", "--strict");

        Assert.Equal("validate", failure.Command);
        Assert.Contains("unknown option '--strict'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAShortFlagIsSupplied_ThenUsageFailureRatherThanBeingTakenAsAnOperand()
    {
        var failure = Reject("validate", "s.toml", "-v");

        Assert.Contains("unknown option '-v'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAnOptionUsesTheEqualsForm_ThenUsageFailure()
    {
        // There is no --name=value spelling; the settled grammar is --name VALUE.
        var failure = Reject("plan", "s.toml", "d.csv", "--temp-dir=t");

        Assert.Contains("unknown option '--temp-dir=t'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenAnOptionIsAbbreviated_ThenUsageFailure()
    {
        var failure = Reject("plan", "s.toml", "d.csv", "--temp");

        Assert.Contains("unknown option '--temp'", failure.Message, StringComparison.Ordinal);
    }

    // ---- options on the wrong command ------------------------------------------------

    [Theory]
    [InlineData("validate", "s.toml", "d.csv", "--temp-dir", "t")]
    [InlineData("fingerprint", "s.toml", "d.csv", "--v2-compat")]
    [InlineData("migrate", "x.bed", "--out", "o.toml", "--ordering", "unordered")]
    [InlineData("plan", "s.toml", "d.csv", "--force")]
    [InlineData("stats", "s.toml", "d.csv", "--out", "o")]
    [InlineData("validate", "s.toml", "--force")]
    [InlineData("probe", "d.csv", "--shape", "wide", "--out", "o", "--v2-compat")]
    public void Parse_WhenAnOptionBelongsToAnotherCommand_ThenUsageFailure(params string[] argv)
    {
        var failure = Reject(argv);

        Assert.Contains("unknown option", failure.Message, StringComparison.Ordinal);
    }

    // ---- value forms -----------------------------------------------------------------

    [Theory]
    [InlineData("cxt")]
    [InlineData("dat")]
    [InlineData("both")]
    public void Parse_WhenFormatIsValid_ThenItParses(string format)
    {
        var invocation = Accept("convert", "s.toml", "d.csv", "--out", "b", "--format", format);

        Assert.Equal(format, invocation.Value("--format"));
    }

    [Theory]
    [InlineData("CXT")]
    [InlineData("Both")]
    [InlineData("csv")]
    [InlineData("")]
    public void Parse_WhenFormatIsInvalid_ThenUsageFailure(string format)
    {
        var failure = Reject("convert", "s.toml", "d.csv", "--out", "b", "--format", format);

        Assert.Contains("accepts cxt|dat|both", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Parse_WhenHeaderIsValid_ThenItParses(string header)
    {
        var invocation = Accept("probe", "d.csv", "--shape", "wide", "--out", "o", "--header", header);

        Assert.Equal(header, invocation.Value("--header"));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("1")]
    public void Parse_WhenHeaderIsNotExactlyTrueOrFalse_ThenUsageFailure(string header)
    {
        var failure = Reject("probe", "d.csv", "--shape", "wide", "--out", "o", "--header", header);

        Assert.Contains("accepts true or false", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("100000")]
    [InlineData("2147483647")]
    public void Parse_WhenLimitIsPositive_ThenItParsesAndKeepsTheSuppliedSpelling(string limit)
    {
        var invocation = Accept("probe", "d.csv", "--shape", "wide", "--out", "o", "--limit", limit);

        Assert.Equal(limit, invocation.Value("--limit"));
    }

    [Theory]
    [InlineData("0")]        // ProbeOptions.Create's validated boundary is 1 (D-108).
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,000")]
    [InlineData("many")]
    [InlineData("")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    public void Parse_WhenLimitIsNotAPositiveInteger_ThenUsageFailure(string limit)
    {
        var failure = Reject("probe", "d.csv", "--shape", "wide", "--out", "o", "--limit", limit);

        Assert.Contains("a whole number of at least 1", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("probe", ",")]
    [InlineData("probe", ";")]
    [InlineData("probe", "|")]
    [InlineData("probe", "\t")]
    [InlineData("probe", "-")]
    [InlineData("probe", " ")]
    [InlineData("probe", "é")]
    [InlineData("migrate", ",")]
    [InlineData("migrate", "\t")]
    [InlineData("migrate", "|")]
    public void Parse_WhenDelimiterIsOneUsableCharacter_ThenItParses(string command, string delimiter)
    {
        // §5.1: a single non-newline character other than the quote. Tab and pipe are the
        // named common alternatives, and a lone dash must keep working.
        var invocation = command == "probe"
            ? Accept("probe", "d.csv", "--shape", "wide", "--out", "o", "--delimiter", delimiter)
            : Accept("migrate", "x.bed", "--out", "o", "--delimiter", delimiter);

        Assert.Equal(delimiter, invocation.Value("--delimiter"));
    }

    [Theory]
    [InlineData("probe", "||")]
    [InlineData("probe", ",,")]
    [InlineData("probe", "")]
    [InlineData("probe", "\n")]
    [InlineData("probe", "\r")]
    [InlineData("probe", "\"")]
    [InlineData("probe", "tab")]
    [InlineData("probe", "\U0001F600")]
    [InlineData("migrate", "||")]
    [InlineData("migrate", "")]
    [InlineData("migrate", "\n")]
    [InlineData("migrate", "\"")]
    public void Parse_WhenDelimiterIsNotOneUsableCharacter_ThenUsageFailure(string command, string delimiter)
    {
        // A multi-character value cannot reach the char-typed source settings at all, and the
        // quote character is excluded by §5.1's delimiter/quote conflict rule. A non-BMP
        // character is two UTF-16 units and likewise cannot be a delimiter.
        var failure = command == "probe"
            ? Reject("probe", "d.csv", "--shape", "wide", "--out", "o", "--delimiter", delimiter)
            : Reject("migrate", "x.bed", "--out", "o", "--delimiter", delimiter);

        Assert.Equal(command, failure.Command);
        Assert.Contains("a single non-newline character", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("probe", "invariant")]
    [InlineData("probe", "INVARIANT")]
    [InlineData("probe", "Invariant")]
    [InlineData("probe", "fr-FR")]
    [InlineData("probe", "de-DE")]
    [InlineData("migrate", "invariant")]
    [InlineData("migrate", "fr-FR")]
    public void Parse_WhenLocaleIsAcceptedByTheResolveSeam_ThenItParsesAndKeepsTheSuppliedSpelling(
        string command, string locale)
    {
        var invocation = command == "probe"
            ? Accept("probe", "d.csv", "--shape", "wide", "--out", "o", "--locale", locale)
            : Accept("migrate", "x.bed", "--out", "o", "--locale", locale);

        Assert.Equal(locale, invocation.Value("--locale"));
    }

    [Theory]
    [InlineData("probe", "definitely-not-a-culture")]
    [InlineData("probe", "zz-ZZ-invalid")]
    [InlineData("probe", "not a tag at all")]
    [InlineData("migrate", "definitely-not-a-culture")]
    [InlineData("migrate", "zz-ZZ-invalid")]
    public void Parse_WhenLocaleIsNotAKnownCulture_ThenUsageFailure(string command, string locale)
    {
        // The same predicate SpecResolver and ProbeOptions apply, so the CLI cannot accept a
        // locale that would make a generated draft fail its own reread.
        var failure = command == "probe"
            ? Reject("probe", "d.csv", "--shape", "wide", "--out", "o", "--locale", locale)
            : Reject("migrate", "x.bed", "--out", "o", "--locale", locale);

        Assert.Equal(command, failure.Command);
        Assert.Contains("invariant or a known culture name", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("en-GB")]
    [InlineData("invariant")]
    [InlineData("INVARIANT")]
    [InlineData("")]
    [InlineData("definitely-not-a-culture")]
    [InlineData("zz-ZZ-invalid")]
    [InlineData("not a tag at all")]
    public void Parse_WhenALocaleIsJudged_ThenItAgreesWithTheDiscoveryBoundary(string locale)
    {
        // Cross-check: the CLI's acceptance set must be the one ProbeOptions.Create enforces,
        // so drift fails here rather than at the S9 mapping.
        var parserAccepts = CommandLineParser.Parse(
            ["probe", "d.csv", "--shape", "wide", "--out", "o", "--locale", locale]) is CommandInvocation;

        bool discoveryAccepts;
        try
        {
            _ = FcaBedrock.Discovery.ProbeOptions.Create(locale: locale);
            discoveryAccepts = true;
        }
        catch (ArgumentException)
        {
            discoveryAccepts = false;
        }

        Assert.Equal(discoveryAccepts, parserAccepts);
    }

    [Fact]
    public void Parse_WhenALimitIsJudged_ThenItAgreesWithTheDiscoveryBoundary()
    {
        foreach (var limit in new[] { 0, 1, 2, 100_000 })
        {
            var text = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var parserAccepts = CommandLineParser.Parse(
                ["probe", "d.csv", "--shape", "wide", "--out", "o", "--limit", text]) is CommandInvocation;

            bool discoveryAccepts;
            try
            {
                _ = FcaBedrock.Discovery.ProbeOptions.Create(valueRetentionLimit: limit);
                discoveryAccepts = true;
            }
            catch (ArgumentOutOfRangeException)
            {
                discoveryAccepts = false;
            }

            Assert.Equal(discoveryAccepts, parserAccepts);
        }
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("Wide")]
    public void Parse_WhenTheShapeValueIsInvalid_ThenUsageFailure(string shape)
    {
        var failure = Reject("probe", "d.csv", "--shape", shape, "--out", "o");

        Assert.Contains("accepts wide|triple", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("grouped")]
    [InlineData("Unordered")]
    public void Parse_WhenTheOrderingValueIsInvalid_ThenUsageFailure(string ordering)
    {
        var failure = Reject("probe", "d.csv", "--shape", "triple", "--out", "o", "--ordering", ordering);

        Assert.Contains("accepts subject_grouped|unordered", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--scaling", "ordinal")]
    [InlineData("--object-key", "composite")]
    public void Parse_WhenAnEnumeratedMigrateValueIsInvalid_ThenUsageFailure(string option, string value)
    {
        var failure = Reject("migrate", "x.bed", "--out", "o", option, value);

        Assert.Contains("accepts", failure.Message, StringComparison.Ordinal);
    }

    // ---- shape-scoped options ---------------------------------------------------------

    [Theory]
    [InlineData("--ordering", "unordered")]
    [InlineData("--subject", "0")]
    [InlineData("--predicate", "1")]
    [InlineData("--value", "2")]
    public void Parse_WhenATripleOnlyProbeOptionIsUsedUnderWide_ThenUsageFailure(string option, string value)
    {
        var failure = Reject("probe", "d.csv", "--shape", "wide", "--out", "o", option, value);

        Assert.Contains("valid only with --shape triple", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--object-key", "row_index")]
    [InlineData("--object-key-column", "3")]
    public void Parse_WhenAWideOnlyMigrateOptionIsUsedUnderTriple_ThenUsageFailure(string option, string value)
    {
        var failure = Reject("migrate", "x.bed", "--out", "o", "--shape", "triple", option, value);

        Assert.Contains("valid only with --shape wide", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenATripleOnlyMigrateOptionIsUsedUnderTheDefaultShape_ThenUsageFailure()
    {
        // migrate's shape defaults to wide, so an omitted --shape is a WIDE invocation and
        // the role options are out of scope.
        var failure = Reject("migrate", "x.bed", "--out", "o", "--subject", "0", "--predicate", "1", "--value", "2");

        Assert.Contains("valid only with --shape triple", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenProbeOmitsShape_ThenUsageFailure()
    {
        var failure = Reject("probe", "d.csv", "--out", "o");

        Assert.Contains("requires the option '--shape'", failure.Message, StringComparison.Ordinal);
    }

    // ---- triple role trio --------------------------------------------------------------

    [Theory]
    [InlineData("--subject", "0")]
    [InlineData("--predicate", "1")]
    [InlineData("--value", "2")]
    public void Parse_WhenOnlyOneRoleOptionIsSupplied_ThenUsageFailure(string option, string value)
    {
        var failure = Reject("probe", "d.csv", "--shape", "triple", "--out", "o", option, value);

        Assert.Contains("supplied together", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenTwoRoleOptionsAreSupplied_ThenUsageFailure()
    {
        var failure = Reject(
            "probe", "d.csv", "--shape", "triple", "--out", "o", "--subject", "0", "--predicate", "1");

        Assert.Contains("supplied together", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenRoleOptionsAreAllIndices_ThenItParses()
    {
        var invocation = Accept(
            "probe", "d.csv", "--shape", "triple", "--out", "o", "--subject", "0", "--predicate", "1", "--value", "2");

        Assert.Equal("0", invocation.Value("--subject"));
    }

    [Fact]
    public void Parse_WhenRoleOptionsAreAllNamesWithHeaderTrue_ThenItParses()
    {
        var invocation = Accept(
            "probe", "d.csv", "--shape", "triple", "--out", "o", "--header", "true",
            "--subject", "s", "--predicate", "p", "--value", "v");

        Assert.Equal("s", invocation.Value("--subject"));
    }

    [Theory]
    [InlineData("0", "p", "v")]
    [InlineData("s", "1", "v")]
    [InlineData("s", "p", "2")]
    [InlineData("0", "1", "v")]
    public void Parse_WhenRoleOptionsMixIndicesAndNames_ThenUsageFailure(string s, string p, string v)
    {
        var failure = Reject(
            "probe", "d.csv", "--shape", "triple", "--out", "o", "--header", "true",
            "--subject", s, "--predicate", p, "--value", v);

        Assert.Contains("one addressing mode", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenRoleNamesAreUsedWithoutHeaderTrue_ThenUsageFailure()
    {
        var failure = Reject(
            "probe", "d.csv", "--shape", "triple", "--out", "o",
            "--subject", "s", "--predicate", "p", "--value", "v");

        Assert.Contains("requires --header true", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenRoleNamesAreUsedWithHeaderFalse_ThenUsageFailure()
    {
        var failure = Reject(
            "probe", "d.csv", "--shape", "triple", "--out", "o", "--header", "false",
            "--subject", "s", "--predicate", "p", "--value", "v");

        Assert.Contains("requires --header true", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenRoleIndicesAreUsedWithoutHeader_ThenItParses()
    {
        // Index addressing needs no header at all.
        Accept("migrate", "x.bed", "--out", "o", "--shape", "triple",
            "--subject", "0", "--predicate", "1", "--value", "2");
    }

    // ---- migrate object-key coupling ----------------------------------------------------

    [Fact]
    public void Parse_WhenObjectKeyIsColumnWithItsColumn_ThenItParses()
    {
        var invocation = Accept(
            "migrate", "x.bed", "--out", "o", "--object-key", "column", "--object-key-column", "name");

        Assert.Equal("name", invocation.Value("--object-key-column"));
    }

    [Fact]
    public void Parse_WhenObjectKeyIsColumnWithoutItsColumn_ThenUsageFailure()
    {
        var failure = Reject("migrate", "x.bed", "--out", "o", "--object-key", "column");

        Assert.Contains("required when --object-key column", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("migrate", "x.bed", "--out", "o", "--object-key-column", "3")]
    [InlineData("migrate", "x.bed", "--out", "o", "--object-key", "row_index", "--object-key-column", "3")]
    public void Parse_WhenObjectKeyColumnIsSuppliedWithoutColumnMode_ThenUsageFailure(params string[] argv)
    {
        var failure = Reject(argv);

        Assert.Contains("valid only with --object-key column", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenObjectKeyIsRowIndexAlone_ThenItParses()
    {
        Accept("migrate", "x.bed", "--out", "o", "--object-key", "row_index");
    }

    // ---- fingerprint write coupling ------------------------------------------------------

    [Fact]
    public void Parse_WhenFingerprintReportsOnly_ThenItParses()
    {
        var invocation = Accept("fingerprint", "s.toml", "d.csv");

        Assert.False(invocation.Has("--write"));
    }

    [Fact]
    public void Parse_WhenFingerprintWritesWithOut_ThenItParses()
    {
        var invocation = Accept("fingerprint", "s.toml", "d.csv", "--write", "--out", "n.toml");

        Assert.True(invocation.Has("--write"));
        Assert.Equal("n.toml", invocation.Value("--out"));
    }

    [Fact]
    public void Parse_WhenFingerprintWritesToStdout_ThenItParses()
    {
        Accept("fingerprint", "s.toml", "d.csv", "--write", "--out", "-");
    }

    [Fact]
    public void Parse_WhenFingerprintSuppliesOutWithoutWrite_ThenUsageFailure()
    {
        var failure = Reject("fingerprint", "s.toml", "d.csv", "--out", "n.toml");

        Assert.Contains("'--out' is valid only with --write", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenFingerprintSuppliesForceWithoutWrite_ThenUsageFailure()
    {
        var failure = Reject("fingerprint", "s.toml", "d.csv", "--force");

        Assert.Contains("'--force' is valid only with --write", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenFingerprintWritesWithoutOut_ThenUsageFailure()
    {
        var failure = Reject("fingerprint", "s.toml", "d.csv", "--write");

        Assert.Contains("'--write' requires '--out'", failure.Message, StringComparison.Ordinal);
    }

    // ---- --force across the writing commands ----------------------------------------------

    [Theory]
    [InlineData("calibrate", "s.toml", "d.csv", "--out", "o.toml", "--force")]
    [InlineData("probe", "d.csv", "--shape", "wide", "--out", "o.toml", "--force")]
    [InlineData("migrate", "x.bed", "--out", "o.toml", "--force")]
    [InlineData("fingerprint", "s.toml", "d.csv", "--write", "--out", "n.toml", "--force")]
    [InlineData("convert", "s.toml", "d.csv", "--out", "b", "--format", "cxt", "--force")]
    public void Parse_WhenForceTargetsAFile_ThenItParses(params string[] argv)
    {
        Assert.True(Accept(argv).Has("--force"));
    }

    [Theory]
    [InlineData("calibrate", "s.toml", "d.csv", "--out", "-", "--force")]
    [InlineData("probe", "d.csv", "--shape", "wide", "--out", "-", "--force")]
    [InlineData("migrate", "x.bed", "--out", "-", "--force")]
    [InlineData("fingerprint", "s.toml", "d.csv", "--write", "--out", "-", "--force")]
    public void Parse_WhenForceIsCombinedWithStdout_ThenUsageFailure(params string[] argv)
    {
        var failure = Reject(argv);

        Assert.Contains("not valid with --out -", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("calibrate", "s.toml", "d.csv", "--out", "-")]
    [InlineData("probe", "d.csv", "--shape", "wide", "--out", "-")]
    [InlineData("migrate", "x.bed", "--out", "-")]
    public void Parse_WhenOutIsStdoutWithoutForce_ThenItParses(params string[] argv)
    {
        Assert.Equal("-", Accept(argv).Value("--out"));
    }

    // ---- excluded surface ------------------------------------------------------------------

    [Theory]
    [InlineData("--sample")]
    [InlineData("--gzip")]
    [InlineData("--color")]
    [InlineData("--colour")]
    [InlineData("--no-color")]
    [InlineData("--progress")]
    [InlineData("--machine")]
    [InlineData("--json")]
    [InlineData("--quiet")]
    [InlineData("--verbose")]
    public void Parse_WhenAnExcludedFlagIsSupplied_ThenUsageFailure(string flag)
    {
        // D-122 part 12: sampling, compression, colour, progress and machine-readable
        // modes do not exist in M7, and unknown flags are usage errors.
        var failure = Reject("convert", "s.toml", "d.csv", "--out", "b", "--format", "cxt", flag);

        Assert.Contains("unknown option", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenPlanIsGivenStats_ThenUsageFailureBecauseStatsIsItsOwnCommand()
    {
        var failure = Reject("plan", "s.toml", "d.csv", "--stats");

        Assert.Contains("unknown option '--stats'", failure.Message, StringComparison.Ordinal);
    }

    // ---- interception and unknown commands ---------------------------------------------------

    [Fact]
    public void Parse_WhenNoArgumentsAreGiven_ThenUsageFailureWithTheGeneralUsage()
    {
        var failure = Reject();

        Assert.Null(failure.Command);
        Assert.Equal("no command given.", failure.Message);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("Convert")]
    [InlineData("CONVERT")]
    [InlineData("valid")]
    public void Parse_WhenTheCommandIsUnknown_ThenUsageFailureWithTheGeneralUsage(string command)
    {
        var failure = Reject(command, "a", "b");

        Assert.Null(failure.Command);
        Assert.Contains($"unknown command '{command}'", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("convert", "--help")]
    [InlineData("nonsense", "--help", "--also-nonsense")]
    public void Parse_WhenHelpAppears_ThenHelpIsRequested(params string[] argv)
    {
        Assert.IsType<HelpRequested>(CommandLineParser.Parse(argv));
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("convert", "--version")]
    public void Parse_WhenVersionAppears_ThenVersionIsRequested(params string[] argv)
    {
        Assert.IsType<VersionRequested>(CommandLineParser.Parse(argv));
    }

    [Fact]
    public void Parse_WhenBothHelpAndVersionAppear_ThenHelpWins()
    {
        Assert.IsType<HelpRequested>(CommandLineParser.Parse(["--version", "--help"]));
    }
}
