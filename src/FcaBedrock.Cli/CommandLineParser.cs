using System.Globalization;
using FcaBedrock.Core.Spec;

namespace FcaBedrock.Cli;

/// <summary>What parsing produced.</summary>
internal abstract record ParseOutcome;

/// <summary><c>--help</c> was requested: help on stdout, exit 0.</summary>
internal sealed record HelpRequested : ParseOutcome;

/// <summary><c>--version</c> was requested: the version on stdout, exit 0.</summary>
internal sealed record VersionRequested : ParseOutcome;

/// <summary>A grammar/usage failure: the reason plus the command whose usage to show (null = the general usage).</summary>
internal sealed record UsageFailure(string Message, string? Command) : ParseOutcome;

/// <summary>A well-formed invocation: the command, its operands in order, and its options.</summary>
internal sealed record CommandInvocation(
    CliCommand Command,
    IReadOnlyList<string> Operands,
    IReadOnlyDictionary<string, string?> Options) : ParseOutcome
{
    /// <summary>True when <paramref name="option"/> was supplied.</summary>
    public bool Has(string option) => Options.ContainsKey(option);

    /// <summary>The value of <paramref name="option"/>, or null when it was not supplied (or is a flag).</summary>
    public string? Value(string option) => Options.TryGetValue(option, out var value) ? value : null;

    /// <summary>The operand at <paramref name="index"/>, or null when it was not supplied.</summary>
    public string? Operand(int index) => index < Operands.Count ? Operands[index] : null;
}

/// <summary>
/// The hand-written declarative parser (D-123 part 2). It reads <see cref="CommandTable"/>
/// and nothing else, so parsing and the byte-locked usage text cannot describe different
/// grammars.
/// <para>
/// <b>Why hand-written.</b> <c>System.CommandLine</c>'s help text, usage phrasing, and
/// implicit behaviours (response files, aliases, suggestions) are library-owned bytes
/// sitting inside M7's byte-locked stdout/stderr surface, and would drift with an upgrade —
/// the same hazard that kept Tomlyn's serializer out of the canonical writer (D-075).
/// </para>
/// <para>
/// <b>The grammar is exactly what D-122 settled and nothing more.</b> No aliases, no
/// abbreviations, no <c>--name=value</c> form, no response files, no environment
/// fallbacks, no hidden options, no permissive coercions. Unknown options — including the
/// excluded surface (<c>--sample</c>, <c>--gzip</c>, colour/progress/machine spellings) —
/// are usage errors (D-122 part 12).
/// </para>
/// </summary>
internal static class CommandLineParser
{
    private const string HelpOption = "--help";
    private const string VersionOption = "--version";

    private static readonly string[] RoleOptions = ["--subject", "--predicate", "--value"];

    /// <summary>Parses <paramref name="argv"/> — the ordinary application arguments, not the audit argv.</summary>
    public static ParseOutcome Parse(IReadOnlyList<string> argv)
    {
        ArgumentNullException.ThrowIfNull(argv);

        // Intercepted before anything else, so `--help` always explains rather than
        // complaining about whatever else is malformed on the line.
        foreach (var token in argv)
        {
            if (string.Equals(token, HelpOption, StringComparison.Ordinal))
            {
                return new HelpRequested();
            }
        }

        foreach (var token in argv)
        {
            if (string.Equals(token, VersionOption, StringComparison.Ordinal))
            {
                return new VersionRequested();
            }
        }

        if (argv.Count == 0)
        {
            return new UsageFailure("no command given.", null);
        }

        if (CommandTable.Find(argv[0]) is not { } command)
        {
            return new UsageFailure($"unknown command '{argv[0]}'.", null);
        }

        var operands = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var i = 1; i < argv.Count; i++)
        {
            var token = argv[i];

            if (string.Equals(token, "-", StringComparison.Ordinal))
            {
                // `-` is only ever an --out value (stdout). As an operand it would mean a
                // stdin SPEC/DATA, which D-122 part 4 does not provide.
                return new UsageFailure("'-' is not accepted as an operand; there is no stdin source.", command.Name);
            }

            if (!IsOptionToken(token))
            {
                operands.Add(token);
                continue;
            }

            if (command.Option(token) is not { } option)
            {
                return new UsageFailure($"unknown option '{token}' for command '{command.Name}'.", command.Name);
            }

            if (options.ContainsKey(option.Name))
            {
                return new UsageFailure($"option '{option.Name}' is specified more than once.", command.Name);
            }

            if (option.Kind == OptionValueKind.Flag)
            {
                options.Add(option.Name, null);
                continue;
            }

            // A value is the NEXT token, taken verbatim — an empty one is legal
            // (`--missing-token ""` disables token detection, §5.1). A token that begins
            // with `--` is never a value: it is the next option, so the option before it
            // is missing its value. There is no `--name=value` escape.
            if (i + 1 >= argv.Count || argv[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return new UsageFailure($"option '{option.Name}' requires a value.", command.Name);
            }

            var value = argv[++i];
            if (Validate(option, value) is { } invalid)
            {
                return new UsageFailure(invalid, command.Name);
            }

            options.Add(option.Name, value);
        }

        // Conditional rules run in a fixed order, so one malformed line always reports
        // the same reason.
        var failure = CheckOperands(command, operands)
            ?? CheckRequiredOptions(command, options)
            ?? CheckShapeScopedOptions(command, options)
            ?? CheckRoleOptions(command, options)
            ?? CheckObjectKeyOptions(command, options)
            ?? CheckWriteCoupling(command, options)
            ?? CheckForceAgainstStdout(command, options);

        return failure ?? (ParseOutcome)new CommandInvocation(command, operands, options);
    }

    // A lone `-` is handled by the caller; anything else that starts with `-` is
    // option-shaped, so a mistyped short flag reports as an unknown option rather than
    // being silently swallowed as an operand.
    private static bool IsOptionToken(string token) => token.Length > 1 && token[0] == '-';

    // Every non-text kind carries its real value contract, so an invalid option is a usage
    // error here rather than an exception (or an invalid generated spec) in the handler.
    // Accepted values are never rewritten: the supplied spelling survives verbatim.
    private static string? Validate(CliOption option, string value) => option.Kind switch
    {
        OptionValueKind.Enumerated when !Contains(option.AllowedValues, value) =>
            $"option '{option.Name}' accepts {string.Join("|", option.AllowedValues!)}, not '{value}'.",
        OptionValueKind.Boolean when value is not ("true" or "false") =>
            $"option '{option.Name}' accepts true or false, not '{value}'.",
        OptionValueKind.PositiveInteger when !IsPositiveInteger(value) =>
            $"option '{option.Name}' accepts a whole number of at least 1, not '{value}'.",
        OptionValueKind.Delimiter when !IsDelimiter(value) =>
            $"option '{option.Name}' accepts a single non-newline character other than the quote character"
                + $" '\"', not '{value}'.",
        OptionValueKind.Locale when !IsLocale(value) =>
            $"option '{option.Name}' accepts invariant or a known culture name, not '{value}'.",
        _ => null,
    };

    private static bool Contains(IReadOnlyList<string>? values, string value)
    {
        if (values is null)
        {
            return false;
        }

        foreach (var candidate in values)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // NumberStyles.None: no sign, no whitespace, no thousands separators, no exponent —
    // "5" parses, " 5", "+5", "-5", "5.0" and "1e3" do not. Overflow fails too.
    private static bool IsIndex(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    // The retention limit reaches ProbeOptions.Create, whose validated boundary is 1 (D-108),
    // so zero is a caller mistake to report here — not an exception in ProbeOptions.Create.
    private static bool IsPositiveInteger(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1;

    // §5.1: a single non-newline character that differs from the quote character, which v1
    // fixes at '"'. Exactly one UTF-16 unit, because the resolved setting is a `char`.
    private static bool IsDelimiter(string value) =>
        value.Length == 1 && value[0] is not ('\r' or '\n' or '"');

    // The predicate SpecResolver and ProbeOptions both apply (§5.1): "invariant"
    // case-insensitively, or a PREDEFINED culture — predefinedOnly matters, because under ICU
    // GetCultureInfo synthesizes a culture for almost any well-formed tag, which would make
    // acceptance OS-dependent (P-7/P-11) and let a draft fail its own reread.
    private static bool IsLocale(string value)
    {
        if (string.Equals(value, "invariant", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(value, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static UsageFailure? CheckOperands(CliCommand command, List<string> operands)
    {
        if (operands.Count > command.Positionals.Count)
        {
            return new UsageFailure(
                $"command '{command.Name}' accepts at most {command.Positionals.Count} operand(s);"
                + $" '{operands[command.Positionals.Count]}' is extra.",
                command.Name);
        }

        for (var i = operands.Count; i < command.Positionals.Count; i++)
        {
            if (command.Positionals[i].Required)
            {
                return new UsageFailure(
                    $"command '{command.Name}' requires the {command.Positionals[i].Label} operand.", command.Name);
            }
        }

        return null;
    }

    private static UsageFailure? CheckRequiredOptions(CliCommand command, Dictionary<string, string?> options)
    {
        foreach (var option in command.Options)
        {
            if (option.Required && !options.ContainsKey(option.Name))
            {
                return new UsageFailure(
                    $"command '{command.Name}' requires the option '{option.Name}'.", command.Name);
            }
        }

        return null;
    }

    private static UsageFailure? CheckShapeScopedOptions(CliCommand command, Dictionary<string, string?> options)
    {
        if (EffectiveShape(command, options) is not { } shape)
        {
            return null;
        }

        foreach (var option in command.Options)
        {
            if (option.Shape is { } required && required != shape && options.ContainsKey(option.Name))
            {
                return new UsageFailure(
                    $"option '{option.Name}' is valid only with --shape {Spell(required)}.", command.Name);
            }
        }

        return null;
    }

    // probe requires --shape; migrate defaults to wide (D-122 part 10). A command with no
    // --shape option has no shape concept and no shape-scoped options.
    private static SourceShape? EffectiveShape(CliCommand command, Dictionary<string, string?> options)
    {
        if (command.Option("--shape") is null)
        {
            return null;
        }

        return options.TryGetValue("--shape", out var value) && string.Equals(value, "triple", StringComparison.Ordinal)
            ? SourceShape.Triple
            : SourceShape.Wide;
    }

    private static string Spell(SourceShape shape) => shape == SourceShape.Triple ? "triple" : "wide";

    // §5.3 / D-122 part 10: the three roles are supplied together, in ONE addressing mode —
    // all zero-based indices or all header names — and name addressing requires an
    // explicit --header true (the shape-specific default is not enough to bind by name).
    private static UsageFailure? CheckRoleOptions(CliCommand command, Dictionary<string, string?> options)
    {
        if (command.Option("--subject") is null)
        {
            return null;
        }

        var supplied = 0;
        var indexed = 0;
        foreach (var role in RoleOptions)
        {
            if (options.TryGetValue(role, out var value))
            {
                supplied++;
                if (value is not null && IsIndex(value))
                {
                    indexed++;
                }
            }
        }

        if (supplied == 0)
        {
            return null;
        }

        if (supplied != RoleOptions.Length)
        {
            return new UsageFailure(
                "options --subject, --predicate and --value are supplied together.", command.Name);
        }

        if (indexed is not (0 or 3))
        {
            return new UsageFailure(
                "options --subject, --predicate and --value use one addressing mode:"
                + " all zero-based indices, or all header names.",
                command.Name);
        }

        if (indexed == 0
            && !(options.TryGetValue("--header", out var header) && string.Equals(header, "true", StringComparison.Ordinal)))
        {
            return new UsageFailure(
                "header-name addressing for --subject, --predicate and --value requires --header true.", command.Name);
        }

        return null;
    }

    // D-122 part 10: --object-key-column is required exactly when --object-key column,
    // and invalid otherwise. Both directions, so neither a silently ignored column nor a
    // silently dropped key mode is possible.
    private static UsageFailure? CheckObjectKeyOptions(CliCommand command, Dictionary<string, string?> options)
    {
        if (command.Option("--object-key-column") is null)
        {
            return null;
        }

        var columnMode = options.TryGetValue("--object-key", out var mode)
            && string.Equals(mode, "column", StringComparison.Ordinal);
        var hasColumn = options.ContainsKey("--object-key-column");

        return (columnMode, hasColumn) switch
        {
            (true, false) => new UsageFailure(
                "option '--object-key-column' is required when --object-key column.", command.Name),
            (false, true) => new UsageFailure(
                "option '--object-key-column' is valid only with --object-key column.", command.Name),
            _ => null,
        };
    }

    // fingerprint's write mode is one unit: --out and --force belong to it and mean
    // nothing without it, and --write cannot choose a destination on its own (D-122 part 10).
    private static UsageFailure? CheckWriteCoupling(CliCommand command, Dictionary<string, string?> options)
    {
        if (command.Option("--write") is null)
        {
            return null;
        }

        var write = options.ContainsKey("--write");

        if (!write && options.ContainsKey("--out"))
        {
            return new UsageFailure("option '--out' is valid only with --write.", command.Name);
        }

        if (!write && options.ContainsKey("--force"))
        {
            return new UsageFailure("option '--force' is valid only with --write.", command.Name);
        }

        if (write && !options.ContainsKey("--out"))
        {
            return new UsageFailure("option '--write' requires '--out'.", command.Name);
        }

        return null;
    }

    // CX-M7P-005: --force authorizes replacing an existing FILE. With --out - there is no
    // file to replace, so the combination is a usage error rather than a silent no-op.
    private static UsageFailure? CheckForceAgainstStdout(CliCommand command, Dictionary<string, string?> options)
    {
        if (!options.ContainsKey("--force")
            || command.Option("--out") is not { AllowsStdout: true }
            || !options.TryGetValue("--out", out var target)
            || !string.Equals(target, "-", StringComparison.Ordinal))
        {
            return null;
        }

        return new UsageFailure(
            "option '--force' is not valid with --out -; it authorizes replacing an existing file.", command.Name);
    }
}
