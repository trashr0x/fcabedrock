using System.Text;

namespace FcaBedrock.Cli;

/// <summary>
/// Help and usage text, generated from <see cref="CommandTable"/> so it describes exactly
/// the grammar <see cref="CommandLineParser"/> enforces: an option added to the table
/// appears here automatically, and one removed disappears (D-123 part 2).
/// <para>
/// Plain and terminal-independent (D-122 part 3): no colour, no width detection, no
/// culture. LF line endings with a final LF, so the bytes are the same on every host and
/// can be locked.
/// </para>
/// </summary>
internal static class UsageText
{
    private const string Tool = "fcabedrock";

    /// <summary>The full <c>--help</c> document.</summary>
    public static string Help { get; } = BuildHelp();

    /// <summary>The short usage shown when no command, or no known command, was given.</summary>
    public static string General { get; } = BuildGeneral();

    /// <summary>The usage block for <paramref name="command"/>: its signature and its conditional-grammar notes.</summary>
    public static string For(string command)
    {
        if (CommandTable.Find(command) is not { } found)
        {
            return General;
        }

        var builder = new StringBuilder();
        AppendUsage(builder, found, indent: string.Empty);
        return builder.ToString();
    }

    /// <summary>
    /// The command's operand/option signature: operands in order, then options in table
    /// order, required bare and optional bracketed, with a shared usage group rendered
    /// inside one bracket.
    /// </summary>
    internal static string Signature(CliCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var builder = new StringBuilder(command.Name);

        foreach (var positional in command.Positionals)
        {
            builder.Append(' ');
            builder.Append(positional.Required ? positional.Label : $"[{positional.Label}]");
        }

        for (var i = 0; i < command.Options.Count; i++)
        {
            var option = command.Options[i];
            builder.Append(' ');

            if (option.UsageGroup == 0)
            {
                var spelled = Spell(option);
                builder.Append(option.Required ? spelled : $"[{spelled}]");
                continue;
            }

            // Grouped options are contiguous in the table and render as one bracketed
            // unit, because that is how they are usable: `[--write --out NEW_SPEC|-]`.
            builder.Append('[');
            builder.Append(Spell(option));
            while (i + 1 < command.Options.Count && command.Options[i + 1].UsageGroup == option.UsageGroup)
            {
                builder.Append(' ');
                builder.Append(Spell(command.Options[++i]));
            }

            builder.Append(']');
        }

        return builder.ToString();
    }

    // The advertised value form is derived from the option's KIND wherever the kind fixes it:
    // an enumerated option advertises exactly the set the parser accepts, and the constrained
    // kinds advertise their own contract. Only free text and paths carry a hand-written
    // placeholder, so help cannot advertise a form the parser does not accept (D-122 part 10
    // spellings).
    private static string Spell(CliOption option) => option.Kind switch
    {
        OptionValueKind.Flag => option.Name,
        OptionValueKind.Enumerated => $"{option.Name} {string.Join("|", option.AllowedValues!)}",
        OptionValueKind.Boolean => $"{option.Name} true|false",
        OptionValueKind.PositiveInteger => $"{option.Name} N",
        OptionValueKind.Delimiter => $"{option.Name} CHAR|tab",
        OptionValueKind.Locale => $"{option.Name} TAG|invariant",
        _ => $"{option.Name} {option.ValueLabel}",
    };

    private static void AppendUsage(StringBuilder builder, CliCommand command, string indent)
    {
        builder.Append(indent).Append("usage: ").Append(Tool).Append(' ').Append(Signature(command)).Append('\n');
        foreach (var note in command.Notes)
        {
            builder.Append(indent).Append("  note: ").Append(note).Append('\n');
        }
    }

    private static string BuildGeneral()
    {
        var builder = new StringBuilder();
        builder.Append("usage: ").Append(Tool).Append(" <command> [operands] [options]\n\n");
        builder.Append("commands:\n");
        AppendCommandList(builder);
        builder.Append("\nrun '").Append(Tool).Append(" --help' for the full grammar.\n");
        return builder.ToString();
    }

    private static string BuildHelp()
    {
        var builder = new StringBuilder();
        builder.Append(Tool).Append(": Formal Concept Analysis preprocessing (FcaBedrock vNext)\n\n");
        builder.Append("usage: ").Append(Tool).Append(" <command> [operands] [options]\n\n");
        builder.Append("option values: --name VALUE or --name=VALUE; only --name=VALUE passes a VALUE that starts with --.\n\n");
        builder.Append("commands:\n");
        AppendCommandList(builder);

        foreach (var command in CommandTable.Commands)
        {
            builder.Append('\n').Append(command.Name).Append(": ").Append(command.Summary).Append('\n');
            AppendUsage(builder, command, indent: "  ");
            if (string.Equals(command.Name, "stats", StringComparison.Ordinal))
            {
                AppendStatsOutput(builder);
            }
        }

        builder.Append("\nglobal options:\n");
        builder.Append("  --help     Print this help and exit.\n");
        builder.Append("  --version  Print the tool version and exit.\n");
        return builder.ToString();
    }

    // The stats field definitions belong to the full help only. They are not command notes,
    // because AppendUsage also prints notes with every stats usage error.
    private static void AppendStatsOutput(StringBuilder builder)
    {
        builder.Append("  Output describes the resulting formal context:\n");
        builder.Append("    objects            Context rows after grouping and restrictions.\n");
        builder.Append("    formal_attributes  Formal-attribute columns generated by the spec.\n");
        builder.Append("    crosses            Marked cells where an object has an attribute.\n");
        builder.Append("    density            crosses / (objects * formal_attributes).\n");
        builder.Append("    crossless_objects  Rows with no marked cells.\n");
        builder.Append("    empty_attributes   Columns with no marked cells.\n");
        builder.Append("  Density is a fraction rounded to six decimal places.\n");
        builder.Append("  With zero rows or columns, density is n/a (0 cells).\n");
    }

    private static void AppendCommandList(StringBuilder builder)
    {
        var width = 0;
        foreach (var command in CommandTable.Commands)
        {
            width = Math.Max(width, command.Name.Length);
        }

        foreach (var command in CommandTable.Commands)
        {
            builder.Append("  ").Append(command.Name.PadRight(width + 2)).Append(command.Summary).Append('\n');
        }
    }
}
