using FcaBedrock.Cli.Commands;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Cli;

/// <summary>
/// What an option carries after its name. Every kind except <see cref="Text"/> fixes both
/// the accepted values <em>and</em> the advertised spelling, so the grammar the parser
/// enforces and the grammar help prints cannot drift apart.
/// </summary>
internal enum OptionValueKind
{
    /// <summary>No value; presence is the whole meaning.</summary>
    Flag,

    /// <summary>An arbitrary text value; its usage label comes from <see cref="CliOption.ValueLabel"/>.</summary>
    Text,

    /// <summary>One of <see cref="CliOption.AllowedValues"/>, compared ordinally.</summary>
    Enumerated,

    /// <summary>Exactly <c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A whole invariant number of at least 1 (§7.1 retention limits are positive).</summary>
    PositiveInteger,

    /// <summary>A single §5.1 delimiter character: one non-newline character other than the quote.</summary>
    Delimiter,

    /// <summary>§5.1 <c>binding.locale</c>: <c>invariant</c>, or a predefined culture name.</summary>
    Locale,
}

/// <summary>One positional operand.</summary>
/// <param name="Label">The name shown in usage text (<c>SPEC</c>, <c>DATA</c>, <c>BED</c>).</param>
/// <param name="Required">Whether the operand must be supplied.</param>
internal sealed record CliPositional(string Label, bool Required);

/// <summary>
/// One named option. The same descriptor drives parsing, validation, and the generated
/// usage text, so the grammar the CLI enforces and the grammar it advertises cannot drift
/// (D-123 part 2).
/// </summary>
/// <param name="Name">The exact spelling, including the leading <c>--</c>. No aliases, no abbreviations.</param>
/// <param name="Kind">What follows the name.</param>
/// <param name="ValueLabel">The usage placeholder — <see cref="OptionValueKind.Text"/> only; every other kind derives its own.</param>
/// <param name="AllowedValues">The closed value set for <see cref="OptionValueKind.Enumerated"/>, which is also its advertised spelling.</param>
/// <param name="Required">Whether the option must be supplied.</param>
/// <param name="UsageGroup">Options sharing a non-zero group render inside one bracket, in table order.</param>
/// <param name="AllowsStdout">True for an <c>--out</c> whose literal <c>-</c> means stdout.</param>
/// <param name="Shape">Set when the option is valid only under that source shape.</param>
internal sealed record CliOption(
    string Name,
    OptionValueKind Kind,
    string? ValueLabel = null,
    IReadOnlyList<string>? AllowedValues = null,
    bool Required = false,
    int UsageGroup = 0,
    bool AllowsStdout = false,
    SourceShape? Shape = null);

/// <summary>Runs one parsed command.</summary>
internal delegate Task<int> CommandHandler(CommandInvocation invocation, CliEnvironment environment);

/// <summary>
/// One command: its operands, its options, the conditional rules worth stating in usage
/// text, and — once its slice lands — its handler.
/// </summary>
/// <param name="Name">The command word.</param>
/// <param name="Summary">One line, shown in <c>--help</c>.</param>
/// <param name="Positionals">Operands, in order.</param>
/// <param name="Options">Options, in usage order.</param>
/// <param name="Notes">Conditional-grammar notes the signature alone cannot express.</param>
/// <param name="Handler">The implementation, or <see langword="null"/> until its slice lands.</param>
internal sealed record CliCommand(
    string Name,
    string Summary,
    IReadOnlyList<CliPositional> Positionals,
    IReadOnlyList<CliOption> Options,
    IReadOnlyList<string> Notes,
    CommandHandler? Handler)
{
    /// <summary>The option descriptor for <paramref name="name"/>, or null when this command has no such option.</summary>
    public CliOption? Option(string name)
    {
        foreach (var option in Options)
        {
            if (string.Equals(option.Name, name, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return null;
    }
}

/// <summary>
/// The eight settled commands (D-122 part 1) as data. This table is the single source
/// for the parser, the conditional rules, and the byte-locked usage/help text.
/// <para>
/// <b><c>validate</c>, <c>plan</c>, <c>stats</c>, <c>fingerprint</c>, <c>convert</c>,
/// <c>probe</c>, and <c>migrate</c> execute.</b> Only <c>calibrate</c> still parses under its
/// complete final grammar — so the grammar is settled and tested once, at the argv boundary —
/// and then reports a deterministic code-less host error and exit 1 without doing any work.
/// That is temporary shell behaviour, not an output contract: its handler arrives with its
/// own slice (S10).
/// </para>
/// </summary>
internal static class CommandTable
{
    private static readonly string[] FormatValues = ["cxt", "dat", "both"];
    private static readonly string[] ShapeValues = ["wide", "triple"];
    private static readonly string[] OrderingValues = ["subject_grouped", "unordered"];
    private static readonly string[] ScalingValues = ["discrete", "progressive"];
    private static readonly string[] ObjectKeyValues = ["row_index", "column"];

    /// <summary>The commands, in the D-122 part 1 inventory order.</summary>
    public static IReadOnlyList<CliCommand> Commands { get; } =
    [
        new CliCommand(
            "convert",
            "Convert a data source into a formal context using a Bedrock spec.",
            [new CliPositional("SPEC", Required: true), new CliPositional("DATA", Required: true)],
            [
                new CliOption("--out", OptionValueKind.Text, "BASE", Required: true),
                new CliOption("--format", OptionValueKind.Enumerated, AllowedValues: FormatValues, Required: true),
                new CliOption("--v2-compat", OptionValueKind.Flag),
                new CliOption("--no-manifest", OptionValueKind.Flag),
                new CliOption("--force", OptionValueKind.Flag),
                new CliOption("--temp-dir", OptionValueKind.Text, "DIR"),
            ],
            [
                "--out names a base; the ruled extension is appended, and there is no default or inferred format.",
                "--force authorizes replacing an existing distinct destination.",
            ],
            ConvertCommand.RunAsync),

        new CliCommand(
            "validate",
            "Validate a Bedrock spec, optionally against a data source's schema.",
            [new CliPositional("SPEC", Required: true), new CliPositional("DATA", Required: false)],
            [],
            [
                "With DATA, only the source schema is acquired; no data rows are read and nothing is written.",
            ],
            ValidateCommand.RunAsync),

        new CliCommand(
            "plan",
            "Print the conversion plan and the three native fingerprints; write nothing.",
            [new CliPositional("SPEC", Required: true), new CliPositional("DATA", Required: true)],
            [new CliOption("--temp-dir", OptionValueKind.Text, "DIR")],
            ["DATA is required for every plan; rows are read only when calibration requires them."],
            PlanCommand.RunAsync),

        new CliCommand(
            "stats",
            "Print formal-context statistics; write nothing.",
            [new CliPositional("SPEC", Required: true), new CliPositional("DATA", Required: true)],
            [new CliOption("--temp-dir", OptionValueKind.Text, "DIR")],
            [],
            StatsCommand.RunAsync),

        new CliCommand(
            "calibrate",
            "Freeze every data-dependent outcome into a standalone spec.",
            [new CliPositional("SPEC", Required: true), new CliPositional("DATA", Required: true)],
            [
                new CliOption("--out", OptionValueKind.Text, "PATH|-", Required: true, AllowsStdout: true),
                new CliOption("--force", OptionValueKind.Flag),
                new CliOption("--temp-dir", OptionValueKind.Text, "DIR"),
            ],
            ["--out - writes to stdout; --force is a file-target option and is rejected with --out -."],
            Handler: null),

        new CliCommand(
            "probe",
            "Generate a draft spec from a data source.",
            [new CliPositional("DATA", Required: true)],
            [
                new CliOption("--shape", OptionValueKind.Enumerated, AllowedValues: ShapeValues, Required: true),
                new CliOption("--out", OptionValueKind.Text, "PATH|-", Required: true, AllowsStdout: true),
                new CliOption("--force", OptionValueKind.Flag),
                new CliOption("--delimiter", OptionValueKind.Delimiter),
                new CliOption("--header", OptionValueKind.Boolean),
                new CliOption("--missing-token", OptionValueKind.Text, "TOKEN"),
                new CliOption("--locale", OptionValueKind.Locale),
                new CliOption("--limit", OptionValueKind.PositiveInteger),
                new CliOption("--ordering", OptionValueKind.Enumerated, AllowedValues: OrderingValues, Shape: SourceShape.Triple),
                new CliOption("--subject", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Triple),
                new CliOption("--predicate", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Triple),
                new CliOption("--value", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Triple),
            ],
            [
                "--ordering, --subject, --predicate and --value require --shape triple.",
                "--subject, --predicate and --value are supplied together in one addressing mode:"
                    + " all zero-based indices, or all header names, and names require --header true.",
                "--out - writes to stdout; --force is a file-target option and is rejected with --out -.",
            ],
            ProbeCommand.RunAsync),

        new CliCommand(
            "migrate",
            "Migrate a v2 .bed file to a Bedrock spec.",
            [new CliPositional("BED", Required: true)],
            [
                new CliOption("--out", OptionValueKind.Text, "PATH|-", Required: true, AllowsStdout: true),
                new CliOption("--force", OptionValueKind.Flag),
                new CliOption("--shape", OptionValueKind.Enumerated, AllowedValues: ShapeValues),
                new CliOption("--delimiter", OptionValueKind.Delimiter),
                new CliOption("--header", OptionValueKind.Boolean),
                new CliOption("--locale", OptionValueKind.Locale),
                new CliOption("--missing-token", OptionValueKind.Text, "TOKEN"),
                new CliOption("--scaling", OptionValueKind.Enumerated, AllowedValues: ScalingValues),
                new CliOption("--object-key", OptionValueKind.Enumerated, AllowedValues: ObjectKeyValues, Shape: SourceShape.Wide),
                new CliOption("--object-key-column", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Wide),
                new CliOption("--subject", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Triple),
                new CliOption("--predicate", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Triple),
                new CliOption("--value", OptionValueKind.Text, "N|NAME", Shape: SourceShape.Triple),
            ],
            [
                "--shape defaults to wide and --scaling defaults to discrete.",
                "--object-key and --object-key-column require the wide shape, and --object-key-column"
                    + " is required exactly when --object-key column.",
                "--subject, --predicate and --value require --shape triple and follow probe's addressing rules.",
                "Triple output always authors ordering = \"unordered\"; there is no --ordering option.",
                "--out - writes to stdout; --force is a file-target option and is rejected with --out -.",
            ],
            MigrateCommand.RunAsync),

        new CliCommand(
            "fingerprint",
            "Report the three native fingerprints and each stored field's state.",
            [new CliPositional("SPEC", Required: true), new CliPositional("DATA", Required: true)],
            [
                new CliOption("--write", OptionValueKind.Flag, UsageGroup: 1),
                new CliOption("--out", OptionValueKind.Text, "NEW_SPEC|-", UsageGroup: 1, AllowsStdout: true),
                new CliOption("--force", OptionValueKind.Flag),
                new CliOption("--temp-dir", OptionValueKind.Text, "DIR"),
            ],
            [
                "--out and --force are valid only with --write, and --write requires --out.",
                "There is no --v2-compat: v2 byte compatibility is a convert-only override.",
            ],
            FingerprintCommand.RunAsync),
    ];

    /// <summary>The command named <paramref name="name"/>, or null when there is no such command.</summary>
    public static CliCommand? Find(string name)
    {
        foreach (var command in Commands)
        {
            if (string.Equals(command.Name, name, StringComparison.Ordinal))
            {
                return command;
            }
        }

        return null;
    }
}
