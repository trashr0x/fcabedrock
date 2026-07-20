using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace FcaBedrock.Core.Spec;

/// <summary>
/// A validated <c>formal_attribute_format</c> (§10.7): the authored string plus
/// the token sequence the planner renders. One grammar implementation serves both
/// the Spec reader (which validates every authored format, wherever it is
/// authored) and <c>ConversionPlanner</c> (which renders through it) — two copies
/// would be two chances to disagree about bytes (P-5, D-117).
/// <para>
/// The placeholder set is <b>closed and case-sensitive</b> — exactly
/// <c>{name}</c>, its alias <c>{column}</c>, <c>{display_name}</c>,
/// <c>{value}</c>, and <c>{scale_op}</c>; there is no <c>{scale}</c> and no
/// extension point. <c>{{</c> and <c>}}</c> render literal braces.
/// </para>
/// <para>
/// Parsing is a <b>single left-to-right pass</b> into literal and placeholder
/// tokens, and rendering walks those tokens — so substituted text is
/// <b>structurally</b> incapable of being rescanned as format syntax. That is a
/// determinism requirement, not a nicety: §10.1 explicitly permits an attribute
/// <c>name</c> containing brace-like text, which a second pass would re-expand
/// (D-117).
/// </para>
/// </summary>
public sealed class NameFormat
{
    private readonly ImmutableArray<Token> _tokens;

    private NameFormat(string text, ImmutableArray<Token> tokens)
    {
        Text = text;
        _tokens = tokens;
    }

    /// <summary>
    /// The authored format string, verbatim. Round-trip fidelity is why the
    /// <c>{column}</c> alias survives here even though it collapses to
    /// <see cref="Placeholder.Name"/> in the token model (D-075 canonical write).
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Parses and validates <paramref name="format"/> against §10.7's grammar.
    /// The three validity rules are deliberately distinct: the format string
    /// <b>as a whole</b> must be non-empty, an individual literal span may be
    /// empty (so <c>"{name}"</c> and <c>"{name}{value}"</c> are valid), and
    /// literal text may contain neither CR nor LF.
    /// </summary>
    /// <param name="format">The authored format string.</param>
    /// <param name="parsed">The validated format on success.</param>
    /// <param name="error">
    /// On failure, the reason — a sentence fragment the caller folds into its own
    /// <c>SpecFieldInvalid</c> message (P-14; §10.7 mints no format-specific code).
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="format"/> is valid.</returns>
    public static bool TryCreate(
        string format,
        [NotNullWhen(true)] out NameFormat? parsed,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(format);

        parsed = null;
        if (format.Length == 0)
        {
            // The whole-format rule (§10.7): an empty format would render every formal
            // attribute of the attribute as the empty name — rejected here rather than
            // left for the plan-time backstop, so the author gets a parse-time span.
            error = "the format string is empty";
            return false;
        }

        var tokens = ImmutableArray.CreateBuilder<Token>();
        var literal = new StringBuilder();
        var index = 0;
        while (index < format.Length)
        {
            var current = format[index];
            switch (current)
            {
                case '{' when index + 1 < format.Length && format[index + 1] == '{':
                    literal.Append('{');
                    index += 2;
                    continue;

                case '}' when index + 1 < format.Length && format[index + 1] == '}':
                    literal.Append('}');
                    index += 2;
                    continue;

                case '}':
                    error = $"an unmatched '}}' at position {index} (write '}}}}' for a literal brace)";
                    return false;

                case '{':
                    if (!TakePlaceholder(format, index, out var placeholder, out var next, out error))
                    {
                        return false;
                    }

                    Flush(tokens, literal);
                    tokens.Add(Token.Of(placeholder));
                    index = next;
                    continue;

                case '\r':
                case '\n':
                    error = $"a CR or LF in literal text at position {index}";
                    return false;

                default:
                    literal.Append(current);
                    index++;
                    continue;
            }
        }

        Flush(tokens, literal);
        parsed = new NameFormat(format, tokens.ToImmutable());
        error = null;
        return true;
    }

    /// <summary>
    /// Renders one formal-attribute name by walking the tokens once. The
    /// substituted text is never rescanned — there is no string to rescan, only
    /// tokens to concatenate (§10.7).
    /// </summary>
    /// <param name="name">The logical attribute name (<c>{name}</c>/<c>{column}</c>).</param>
    /// <param name="displayName">The resolved display name (<c>{display_name}</c>).</param>
    /// <param name="value">The value-side text (<c>{value}</c>); the literal <c>missing</c> for the missing column.</param>
    /// <param name="scaleOp">The ordinal operator (<c>{scale_op}</c>); empty for non-ordinal shapes.</param>
    internal string Render(string name, string displayName, string value, string scaleOp)
    {
        var rendered = new StringBuilder();
        foreach (var token in _tokens)
        {
            rendered.Append(token.Literal ?? token.Placeholder switch
            {
                // {column} collapsed to Name at parse: a documented alias renders
                // identically by construction rather than by two matching branches.
                Placeholder.Name => name,
                Placeholder.DisplayName => displayName,
                Placeholder.Value => value,
                Placeholder.ScaleOp => scaleOp,
                _ => throw new InvalidOperationException($"Unknown name-format placeholder {token.Placeholder}."),
            });
        }

        return rendered.ToString();
    }

    // Reads one {placeholder} starting at the '{' in `start`. A '{' before the closing
    // brace is malformed rather than a nested placeholder — placeholders do not nest, and
    // treating it as literal text is exactly the silent-rendering trap §10.7 rejects.
    private static bool TakePlaceholder(
        string format,
        int start,
        out Placeholder placeholder,
        out int next,
        [NotNullWhen(false)] out string? error)
    {
        placeholder = default;
        next = start;

        var close = -1;
        for (var scan = start + 1; scan < format.Length; scan++)
        {
            if (format[scan] == '}')
            {
                close = scan;
                break;
            }

            if (format[scan] == '{')
            {
                error = $"a malformed placeholder at position {start} ('{{' inside a placeholder)";
                return false;
            }
        }

        if (close < 0)
        {
            error = $"an unmatched '{{' at position {start} (write '{{{{' for a literal brace)";
            return false;
        }

        var name = format[(start + 1)..close];
        if (name.Length == 0)
        {
            error = $"an empty placeholder '{{}}' at position {start}";
            return false;
        }

        // Ordinal, case-sensitive (P-12): {Value} is a typo, not a synonym — the closed
        // set fails fast at parse rather than silently rendering different bytes.
        switch (name)
        {
            case "name":
            case "column":
                placeholder = Placeholder.Name;
                break;
            case "display_name":
                placeholder = Placeholder.DisplayName;
                break;
            case "value":
                placeholder = Placeholder.Value;
                break;
            case "scale_op":
                placeholder = Placeholder.ScaleOp;
                break;
            default:
                error =
                    $"an unknown placeholder '{{{name}}}' at position {start}; " +
                    "the closed set is {name}, {column}, {display_name}, {value}, {scale_op}";
                return false;
        }

        next = close + 1;
        error = null;
        return true;
    }

    // Empty literal spans are valid but carry nothing, so they are never emitted as
    // tokens — "{name}{value}" is two placeholders, not five tokens (§10.7).
    private static void Flush(ImmutableArray<Token>.Builder tokens, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        tokens.Add(Token.Of(literal.ToString()));
        literal.Clear();
    }

    /// <summary>The closed placeholder set; <c>{column}</c> is an alias of <see cref="Name"/>.</summary>
    private enum Placeholder
    {
        Name,
        DisplayName,
        Value,
        ScaleOp,
    }

    /// <summary>One parsed segment: literal text, or a placeholder to substitute.</summary>
    private readonly record struct Token(string? Literal, Placeholder Placeholder)
    {
        public static Token Of(string literal) => new(literal, default);

        public static Token Of(Placeholder placeholder) => new(null, placeholder);
    }
}
