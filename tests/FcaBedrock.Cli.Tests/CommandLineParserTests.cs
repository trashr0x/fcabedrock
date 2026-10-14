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

    // ---- attached values (--name=value) -----------------------------------------------

    [Theory]
    [InlineData("convert s.toml d.csv --format cxt", "--out", "base")]
    [InlineData("convert s.toml d.csv --out base", "--format", "dat")]
    [InlineData("plan s.toml d.csv", "--temp-dir", "spill")]
    [InlineData("probe d.csv --out o", "--shape", "triple")]
    [InlineData("probe d.csv --shape wide --out o", "--header", "false")]
    [InlineData("probe d.csv --shape wide --out o", "--limit", "5")]
    [InlineData("probe d.csv --shape wide --out o", "--delimiter", ";")]
    [InlineData("probe d.csv --shape wide --out o", "--locale", "fr-FR")]
    [InlineData("probe d.csv --shape wide --out o", "--missing-token", "NA")]
    [InlineData("migrate x.bed --out o --object-key column", "--object-key-column", "0")]
    public void Parse_WhenAValueIsAttachedWithEquals_ThenItParsesExactlyAsTheSeparatedForm(
        string prefix, string option, string value)
    {
        // One row per value kind: text, enumerated, path, boolean, positive integer,
        // delimiter and locale.
        string[] head = prefix.Split(' ');

        var separated = Accept([.. head, option, value]);
        var attached = Accept([.. head, $"{option}={value}"]);

        Assert.Equal(value, separated.Value(option));
        Assert.Equal(value, attached.Value(option));
        Assert.Equal(separated.Options.Count, attached.Options.Count);
    }

    [Theory]
    [InlineData("--missing-token=a=b", "a=b")]
    [InlineData("--missing-token==", "=")]
    [InlineData("--missing-token=", "")]
    [InlineData("--missing-token=--", "--")]
    [InlineData("--missing-token=--help", "--help")]
    [InlineData("--missing-token=--version", "--version")]
    [InlineData("--missing-token= -- ", " -- ")]
    public void Parse_WhenAValueIsAttached_ThenTheWholeSuffixAfterTheFirstEqualsIsTheValue(
        string token, string expected)
    {
        // The split happens once, at the first '=': a further '=', an empty remainder and a
        // leading "--" are all data, and an attached --help or --version is not intercepted.
        var invocation = Accept("probe", "d.csv", "--shape", "wide", "--out", "o", token);

        Assert.Equal(expected, invocation.Value("--missing-token"));
    }

    [Fact]
    public void Parse_WhenASeparatedValueStartsWithTwoDashes_ThenItIsStillMissing()
    {
        // The separated form keeps its rule: the next "--" token is an option, not a value.
        var failure = Reject("probe", "d.csv", "--shape", "wide", "--out", "o", "--missing-token", "--");

        Assert.Equal("option '--missing-token' requires a value.", failure.Message);
    }

    [Theory]
    [InlineData("--limit=0", "a whole number of at least 1")]
    [InlineData("--limit=", "a whole number of at least 1")]
    [InlineData("--header=yes", "accepts true or false")]
    [InlineData("--header=", "accepts true or false")]
    [InlineData("--locale=nowhere-at-all", "invariant or a known culture name")]
    [InlineData("--delimiter=", "one character from U+001F to U+007E")]
    [InlineData("--delimiter=;;", "one character from U+001F to U+007E")]
    public void Parse_WhenAnAttachedValueIsInvalid_ThenTheOptionsOwnValidationRejectsIt(
        string token, string expected)
    {
        var failure = Reject("probe", "d.csv", "--shape", "wide", "--out", "o", token);

        Assert.Equal("probe", failure.Command);
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--force=")]
    [InlineData("--force=true")]
    [InlineData("--force==")]
    public void Parse_WhenAFlagHasAnAttachedValue_ThenUsageFailure(string token)
    {
        var failure = Reject("calibrate", "s.toml", "d.csv", "--out", "o.toml", token);

        Assert.Equal("option '--force' does not take a value.", failure.Message);
    }

    [Theory]
    [InlineData("--out", "a", "--out=b")]
    [InlineData("--out=a", "--out", "b")]
    [InlineData("--out=a", "--out=b")]
    public void Parse_WhenAnOptionIsRepeatedInEitherForm_ThenUsageFailure(params string[] tail)
    {
        // Duplicates are keyed by the option's name, whichever form supplied each value.
        var failure = Reject(["calibrate", "s.toml", "d.csv", .. tail]);

        Assert.Equal("option '--out' is specified more than once.", failure.Message);
    }

    [Theory]
    [InlineData("--bogus=1", "--bogus")]
    [InlineData("--temp=t", "--temp")]
    [InlineData("--=x", "--")]
    public void Parse_WhenAnUnknownOptionHasAnAttachedValue_ThenTheUnknownNameIsReported(
        string token, string name)
    {
        var failure = Reject("plan", "s.toml", "d.csv", token);

        Assert.Equal($"unknown option '{name}' for command 'plan'.", failure.Message);
    }

    [Fact]
    public void Parse_WhenAnAttachedOptionBelongsToAnotherCommand_ThenUsageFailure()
    {
        var failure = Reject("validate", "s.toml", "--temp-dir=t");

        Assert.Equal("unknown option '--temp-dir' for command 'validate'.", failure.Message);
    }

    [Theory]
    [InlineData("probe", "--bogus=1", "--limit=0", "--help")]
    [InlineData("convert", "--out=", "--help", "--version")]
    [InlineData("--missing-token=--version", "--help")]
    public void Parse_WhenAStandaloneHelpAppearsAmidMalformedArguments_ThenHelpIsRequested(params string[] argv)
    {
        Assert.IsType<HelpRequested>(CommandLineParser.Parse(argv));
    }

    [Theory]
    [InlineData("probe", "--bogus=1", "--limit=0", "--version")]
    [InlineData("--missing-token=--help", "--version")]
    public void Parse_WhenAStandaloneVersionAppearsAmidMalformedArguments_ThenVersionIsRequested(params string[] argv)
    {
        Assert.IsType<VersionRequested>(CommandLineParser.Parse(argv));
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
    [InlineData("probe", "\u001F")]
    [InlineData("probe", ":")]
    [InlineData("migrate", ",")]
    [InlineData("migrate", "\t")]
    [InlineData("migrate", "|")]
    public void Parse_WhenDelimiterIsOneUsableCharacter_ThenItParses(string command, string delimiter)
    {
        // §5.1.1: one character of the v1 alphabet other than the quote. Tab and pipe are the
        // named common alternatives, and a lone dash, a space, U+001F and ':' must keep working.
        var invocation = command == "probe"
            ? Accept("probe", "d.csv", "--shape", "wide", "--out", "o", "--delimiter", delimiter)
            : Accept("migrate", "x.bed", "--out", "o", "--delimiter", delimiter);

        Assert.Equal(delimiter, invocation.Value("--delimiter"));
    }

    [Theory]
    [InlineData("probe", "tab")]
    [InlineData("probe", "TAB")]
    [InlineData("probe", "Tab")]
    [InlineData("probe", "tAb")]
    [InlineData("migrate", "tab")]
    [InlineData("migrate", "TAB")]
    public void Parse_WhenDelimiterIsTheWordTabInAnyCase_ThenItParsesAndKeepsTheSuppliedSpelling(
        string command, string delimiter)
    {
        // A tab cannot be typed in cmd.exe, so the word is the CLI's spelling of TAB. The parser
        // does not rewrite it: the supplied spelling survives, and each command decodes it.
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
    [InlineData("probe", "\\t")]
    [InlineData("probe", "tabs")]
    [InlineData("probe", "ta")]
    [InlineData("probe", " tab")]
    [InlineData("probe", "\U0001F600")]
    [InlineData("probe", "é")]
    [InlineData("probe", "#")]
    [InlineData("probe", "\u00A0")]
    [InlineData("probe", "\u007F")]
    [InlineData("probe", "\u001E")]
    [InlineData("migrate", "#")]
    [InlineData("migrate", "||")]
    [InlineData("migrate", "")]
    [InlineData("migrate", "\n")]
    [InlineData("migrate", "\"")]
    [InlineData("migrate", "\\t")]
    public void Parse_WhenDelimiterIsNotOneUsableCharacter_ThenUsageFailure(string command, string delimiter)
    {
        // A multi-character value cannot reach the char-typed source settings at all, and the
        // quote character is excluded by §5.1's delimiter/quote conflict rule. A non-BMP
        // character is two UTF-16 units and likewise cannot be a delimiter, and a character
        // outside the v1 alphabet (§5.1.1) is refused. The word tab is the only spelling longer
        // than one character; a backslash followed by t is not a tab.
        var failure = command == "probe"
            ? Reject("probe", "d.csv", "--shape", "wide", "--out", "o", "--delimiter", delimiter)
            : Reject("migrate", "x.bed", "--out", "o", "--delimiter", delimiter);

        Assert.Equal(command, failure.Command);
        Assert.Equal(
            "option '--delimiter' accepts a tab character, the word tab, or one character from U+001F to U+007E"
                + $" other than '#' and the quote character '\"', not '{delimiter}'.",
            failure.Message);
    }

    [Fact]
    public void Parse_WhenDelimiterIsAnyOfThe95UsableCharacters_ThenItParses()
    {
        // All 95 delimiters that were usable before the §5.1.1 alphabet was fixed stay usable:
        // TAB, and U+001F through U+007E without '#' and the quote.
        var accepted = 0;
        for (var code = 0; code <= 0x7E; code++)
        {
            if (code != 0x09 && (code < 0x1F || code == 0x22 || code == 0x23))
            {
                continue;
            }

            var delimiter = ((char)code).ToString();
            Assert.Equal(delimiter, Accept("probe", "d.csv", "--shape", "wide", "--out", "o", "--delimiter", delimiter).Value("--delimiter"));
            Assert.Equal(delimiter, Accept("migrate", "x.bed", "--out", "o", "--delimiter", delimiter).Value("--delimiter"));
            accepted++;
        }

        Assert.Equal(95, accepted);
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
        // so drift fails here rather than in the probe command's ProbeOptions mapping.
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

    [Theory]
    [InlineData("--object-key-column", "id", "--header", "false")]
    [InlineData("--header", "false", "--object-key-column", "id")]
    [InlineData("--object-key-column=id", "--header=false")]
    [InlineData("--object-key-column", "-1", "--header", "false")]
    public void Parse_WhenANamedObjectKeyColumnMeetsHeaderFalse_ThenUsageFailure(params string[] tail)
    {
        // §5.4: a column bound by name needs a header, so this spec could never bind its key.
        // "-1" is not all digits, so it is a name, not an index.
        var failure = Reject(["migrate", "x.bed", "--out", "o", "--object-key", "column", .. tail]);

        Assert.Equal("migrate", failure.Command);
        Assert.Equal(
            "header-name addressing for --object-key-column needs a header row, so it is not valid with --header false.",
            failure.Message);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("id", "--header", "true")]
    [InlineData("0", "--header", "false")]
    [InlineData("0")]
    public void Parse_WhenTheObjectKeyColumnHasAHeaderOrIsAnIndex_ThenItParses(params string[] tail)
    {
        // The effective wide header suffices: unlike the triple roles, a name needs no explicit
        // --header true. An index needs no header at all.
        var invocation = Accept(["migrate", "x.bed", "--out", "o", "--object-key", "column", "--object-key-column", .. tail]);

        Assert.Equal(tail[0], invocation.Value("--object-key-column"));
    }

    [Fact]
    public void Parse_WhenTripleRoleNamesOmitHeader_ThenTheExplicitHeaderRuleStillApplies()
    {
        // Control: the triple roles keep their own rule, an explicit --header true, which the
        // object-key rule deliberately does not copy.
        var failure = Reject(
            "migrate", "x.bed", "--out", "o", "--shape", "triple", "--subject", "s", "--predicate", "p", "--value", "v");

        Assert.Equal(
            "header-name addressing for --subject, --predicate and --value requires --header true.", failure.Message);
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

    // ---- --temp-dir text ---------------------------------------------------------------------

    // The five commands with --temp-dir, each otherwise complete.
    public static TheoryData<string> TempDirCommands() =>
    [
        "convert s.toml d.csv --out b --format cxt",
        "plan s.toml d.csv",
        "stats s.toml d.csv",
        "calibrate s.toml d.csv --out o.toml",
        "fingerprint s.toml d.csv",
    ];

    [Theory]
    [MemberData(nameof(TempDirCommands))]
    public void Parse_WhenTempDirIsBlankInEitherForm_ThenUsageFailure(string prefix)
    {
        string[] head = prefix.Split(' ');

        foreach (var blank in new[] { string.Empty, " ", "\t", " \t " })
        {
            foreach (var tail in new[] { new[] { "--temp-dir", blank }, new[] { $"--temp-dir={blank}" } })
            {
                var failure = Reject([.. head, .. tail]);

                Assert.Equal(head[0], failure.Command);
                Assert.Equal("the --temp-dir value must be a non-empty, non-whitespace path.", failure.Message);
            }
        }
    }

    [Theory]
    [InlineData(" spill ")]
    [InlineData("does-not-exist")]
    [InlineData(".")]
    public void Parse_WhenTempDirIsNotBlank_ThenItIsAcceptedVerbatimAndUnchecked(string value)
    {
        // Text only: the directory is not looked up, created or normalized here (D-122 part 8).
        Assert.Equal(value, Accept("plan", "s.toml", "d.csv", "--temp-dir", value).Value("--temp-dir"));
    }

    [Fact]
    public void Parse_WhenTempDirIsBlank_ThenItIsReportedWhenItsValueIsAcquired()
    {
        // In argv order, before an unknown option that follows it and before the conditional
        // rules (here, convert's missing required options); after an unknown option before it.
        Assert.Equal(
            "the --temp-dir value must be a non-empty, non-whitespace path.",
            Reject("convert", "s.toml", "d.csv", "--temp-dir", " ", "--bogus").Message);
        Assert.Equal(
            "unknown option '--bogus' for command 'convert'.",
            Reject("convert", "s.toml", "d.csv", "--bogus", "--temp-dir", " ").Message);
    }

    // ---- --out text --------------------------------------------------------------------------

    // The five commands that write, each otherwise complete, with --out last.
    private static readonly string[][] Writers =
    [
        ["convert", "s.toml", "d.csv", "--format", "cxt", "--out"],
        ["calibrate", "s.toml", "d.csv", "--out"],
        ["probe", "d.csv", "--shape", "wide", "--out"],
        ["migrate", "x.bed", "--out"],
        ["fingerprint", "s.toml", "d.csv", "--write", "--out"],
    ];

    private static void AssertEveryWriterRejects(string value, string expected)
    {
        foreach (var writer in Writers)
        {
            foreach (var argv in new string[][] { [.. writer, value], [.. writer[..^1], $"--out={value}"] })
            {
                var failure = Reject(argv);

                Assert.Equal(writer[0], failure.Command);
                Assert.Equal(expected, failure.Message);
            }
        }
    }

    private static void AssertEveryWriterAccepts(string value)
    {
        foreach (var writer in Writers)
        {
            Assert.Equal(value, Accept([.. writer, value]).Value("--out"));
            Assert.Equal(value, Accept([.. writer[..^1], $"--out={value}"]).Value("--out"));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t ")]
    public void Parse_WhenOutIsBlank_ThenEveryWriterRejectsIt(string value)
    {
        AssertEveryWriterRejects(value, "the --out value must be a non-empty, non-whitespace path.");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("sub/.")]
    [InlineData("sub/..")]
    [InlineData("a/b/..")]
    [InlineData("../..")]
    [InlineData("../sub/.")]
    [InlineData("out/")]
    [InlineData("/")]
    public void Parse_WhenOutEndsInADirectoryOnEveryPlatform_ThenEveryWriterRejectsIt(string value)
    {
        // '/' separates on every platform, so these name a directory everywhere: the final
        // component is empty, '.' or '..', at any nesting depth.
        AssertEveryWriterRejects(value, $"the --out value '{value}' names a directory; it must end in a file name.");
    }

    [Theory]
    [InlineData(@"out\")]
    [InlineData(@"sub\.")]
    [InlineData(@"sub\..")]
    [InlineData(@"..\..\sub\.")]
    [InlineData("C:")]
    [InlineData(@"C:\")]
    [InlineData("C:.")]
    [InlineData(@"C:\work\..")]
    [InlineData(@"\")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"\\server")]
    [InlineData(@"\\?\C:")]
    [InlineData(@"\\?\C:\")]
    [InlineData(@"\\?\C:\work\.")]
    [InlineData(@"\\?\C:\work\..")]
    [InlineData(@"\\?\UNC\server\share")]
    public void Parse_WhenOutEndsInAWindowsDirectory_ThenWritersRejectItOnWindowsOnly(string value)
    {
        // A backslash separates only on Windows, where a drive or share root and the extended
        // forms' roots are directories too. On other platforms each is an ordinary file name.
        if (OperatingSystem.IsWindows())
        {
            AssertEveryWriterRejects(value, $"the --out value '{value}' names a directory; it must end in a file name.");
        }
        else
        {
            AssertEveryWriterAccepts(value);
        }
    }

    [Theory]
    [InlineData("...")]
    [InlineData("....")]
    [InlineData("foo.")]
    [InlineData("foo ")]
    [InlineData(@"sub\...")]
    [InlineData("sub/report. ")]
    [InlineData(@"C:\work\draft.")]
    [InlineData(@"\\server\share\draft.")]
    public void Parse_WhenOutEndsInADotOrSpace_ThenWritersRejectItOnWindowsOnly(string value)
    {
        // Windows removes a trailing dot or space from an ordinary file name, so the file
        // written would not be the one named. Elsewhere these are ordinary file names.
        if (OperatingSystem.IsWindows())
        {
            AssertEveryWriterRejects(
                value, $"the --out value '{value}' ends in a dot or a space, which Windows removes from a file name.");
        }
        else
        {
            AssertEveryWriterAccepts(value);
        }
    }

    [Theory]
    [InlineData("report v1.data")]
    [InlineData("a..b")]
    [InlineData(".hidden")]
    [InlineData("sub/out.toml")]
    [InlineData("../out")]
    [InlineData("C:driveleaf")]
    [InlineData(@"C:\work\out.toml")]
    [InlineData(@"\\server\share\out")]
    [InlineData(@"\\?\C:\work\draft.")]
    [InlineData(@"\\?\C:\work\draft ")]
    [InlineData(@"\\?\C:\work\...")]
    [InlineData(@"\\?\UNC\server\share\draft.")]
    [InlineData(@"back\slash")]
    public void Parse_WhenOutEndsInAFileName_ThenEveryWriterAcceptsIt(string value)
    {
        // Internal dots, spaces and extensions stay, as does a drive-relative leaf. The explicit
        // extended forms (\\?\) keep a final dot or space, which that namespace preserves.
        AssertEveryWriterAccepts(value);
    }

    [Fact]
    public void Parse_WhenOutIsADash_ThenOnlyTheWritersWithStdoutAcceptIt()
    {
        foreach (var writer in Writers[1..])
        {
            Assert.Equal("-", Accept([.. writer, "-"]).Value("--out"));
            Assert.Equal("-", Accept([.. writer[..^1], "--out=-"]).Value("--out"));
        }

        foreach (var argv in new string[][] { [.. Writers[0], "-"], [.. Writers[0][..^1], "--out=-"] })
        {
            Assert.Equal(
                "command 'convert' has no stdout output, so --out cannot be '-'.", Reject(argv).Message);
        }
    }

    [Fact]
    public void Parse_WhenOutIsInvalidBesideAnotherMistake_ThenTheOtherMistakeIsReported()
    {
        // The --out text is the last rule checked, so every earlier reason keeps its priority.
        Assert.Equal(
            "unknown option '--bogus' for command 'calibrate'.",
            Reject("calibrate", "s.toml", "d.csv", "--out", ".", "--bogus").Message);
        Assert.Equal(
            "command 'convert' requires the option '--format'.",
            Reject("convert", "s.toml", "d.csv", "--out", ".").Message);
        Assert.Equal(
            "option '--object-key-column' is required when --object-key column.",
            Reject("migrate", "x.bed", "--out", ".", "--object-key", "column").Message);
        Assert.Equal(
            "option '--out' is valid only with --write.",
            Reject("fingerprint", "s.toml", "d.csv", "--out", ".").Message);
        Assert.Equal(
            "option '--force' is not valid with --out -; it authorizes replacing an existing file.",
            Reject("calibrate", "s.toml", "d.csv", "--out", "-", "--force").Message);
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
        // modes do not exist in the CLI, and unknown flags are usage errors.
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
