using FcaBedrock.Diagnostics;
using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// A strict, consumption-tracking view over one TOML table's key/value pairs
/// (a section body or an inline table). Section readers take their known keys
/// through the typed accessors — each type-checks the CST node and raises
/// <c>SpecFieldInvalid</c> on mismatch — and then call
/// <see cref="Finish"/>, which classifies every unconsumed key: a member of the
/// closed deferred-surface set (D-075) raises the transitional
/// <c>SpecSurfaceNotYetSupported</c>; anything else raises
/// <c>SpecKeyUnrecognized</c>. The allow-list is therefore exactly the set of
/// keys a reader takes — there is no separate list to drift.
/// </summary>
internal sealed class TomlTableCursor
{
    private static readonly string[] NoDeferredKeys = [];

    private readonly TomlReadContext _context;
    private readonly string _label;
    private readonly List<(string Key, bool Dotted, KeyValueSyntax Node)> _items = [];
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    /// <summary>Wraps a section body (<c>[section]</c> / <c>[[section]]</c>).</summary>
    public TomlTableCursor(TomlReadContext context, string label, TableSyntaxBase table)
        : this(context, label, table.Items)
    {
    }

    /// <summary>Wraps an inline table value (<c>{ key = value, … }</c>).</summary>
    public TomlTableCursor(TomlReadContext context, string label, InlineTableSyntax table)
        : this(context, label, Pairs(table))
    {
    }

    private TomlTableCursor(TomlReadContext context, string label, IEnumerable<KeyValueSyntax> items)
    {
        _context = context;
        _label = label;
        foreach (var item in items)
        {
            if (item.Key is not { } key || TomlSyntaxHelpers.KeyText(key.Key) is not { } text)
            {
                continue; // malformed keys are Tomlyn's syntax errors, already reported
            }

            // A dotted key inside a section (a.b = 1) is not part of the v1
            // authoring surface; carry the full dotted name so Finish reports it.
            var dotted = key.DotKeys is { ChildrenCount: > 0 };
            if (dotted)
            {
                text = string.Join('.', TomlSyntaxHelpers.KeyParts(key));
            }

            _items.Add((text, dotted, item));
        }
    }

    /// <summary>The raw pair for <paramref name="key"/>, marking it consumed; null when not authored.</summary>
    public KeyValueSyntax? Take(string key)
    {
        foreach (var (text, dotted, node) in _items)
        {
            if (!dotted && string.Equals(text, key, StringComparison.Ordinal))
            {
                _consumed.Add(key);
                return node;
            }
        }

        return null;
    }

    /// <summary>An authored string, or null when absent (or reported invalid).</summary>
    public string? TakeString(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is StringValueSyntax { Value: { } value })
        {
            return value;
        }

        Invalid(pair, key, "a string");
        return null;
    }

    /// <summary>An authored integer, or null when absent (or reported invalid).</summary>
    public long? TakeLong(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is IntegerValueSyntax integer)
        {
            return integer.Value;
        }

        Invalid(pair, key, "an integer");
        return null;
    }

    /// <summary>An authored 32-bit integer, or null when absent (or reported invalid).</summary>
    public int? TakeInt(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is IntegerValueSyntax { Value: >= int.MinValue and <= int.MaxValue } integer)
        {
            return (int)integer.Value;
        }

        Invalid(pair, key, "a 32-bit integer");
        return null;
    }

    /// <summary>An authored boolean, or null when absent (or reported invalid).</summary>
    public bool? TakeBool(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is BooleanValueSyntax boolean)
        {
            return boolean.Value;
        }

        Invalid(pair, key, "a boolean");
        return null;
    }

    /// <summary>An authored number — integer or float node — or null when absent (or reported invalid).</summary>
    public double? TakeDouble(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (TomlSyntaxHelpers.AsDouble(pair.Value) is { } value)
        {
            return value;
        }

        Invalid(pair, key, "a number");
        return null;
    }

    /// <summary>An authored single-character string, or null when absent (or reported invalid).</summary>
    public char? TakeChar(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is StringValueSyntax { Value: { Length: 1 } value })
        {
            return value[0];
        }

        Invalid(pair, key, "a single-character string");
        return null;
    }

    /// <summary>
    /// An authored date-time as a zero-offset-normalized <see cref="DateTimeOffset"/>,
    /// or null when absent. Offset forms are taken verbatim; local forms coerce to
    /// zero offset (deterministic across machines — D-075; the field is inert
    /// provenance, §4).
    /// </summary>
    public DateTimeOffset? TakeDateTime(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is DateTimeValueSyntax dateTime)
        {
            return dateTime.Value.DateTime;
        }

        Invalid(pair, key, "a TOML date-time");
        return null;
    }

    /// <summary>An authored value against a spelling table, or null when absent (or reported invalid).</summary>
    public T? TakeEnum<T>(string key, (string Text, T Value)[] spellings)
        where T : struct
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is StringValueSyntax { Value: { } text } &&
            TomlSpellings.TryParse(spellings, text, out var value))
        {
            return value;
        }

        Invalid(pair, key, TomlSpellings.Allowed(spellings));
        return null;
    }

    /// <summary>
    /// An authored string array, or null when absent. An authored empty array
    /// returns an empty list — the omitted-vs-<c>[]</c> distinction survives (D-071).
    /// </summary>
    public IReadOnlyList<string>? TakeStringArray(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is not ArraySyntax array)
        {
            Invalid(pair, key, "an array of strings");
            return null;
        }

        var values = new List<string>();
        foreach (var item in array.Items)
        {
            if (item.Value is StringValueSyntax { Value: { } value })
            {
                values.Add(value);
            }
            else if (item.Value is { } node)
            {
                _context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"{_label} key '{key}' expects an array of strings.",
                    node.Span);
            }
        }

        return values;
    }

    /// <summary>An authored numeric array (integer and float nodes mix), or null when absent.</summary>
    public IReadOnlyList<double>? TakeDoubleArray(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is not ArraySyntax array)
        {
            Invalid(pair, key, "an array of numbers");
            return null;
        }

        var values = new List<double>();
        foreach (var item in array.Items)
        {
            if (item.Value is { } node && TomlSyntaxHelpers.AsDouble(node) is { } value)
            {
                values.Add(value);
            }
            else if (item.Value is { } invalid)
            {
                _context.Error(
                    DiagnosticCode.SpecFieldInvalid,
                    $"{_label} key '{key}' expects an array of numbers.",
                    invalid.Span);
            }
        }

        return values;
    }

    /// <summary>An authored inline table, or null when absent (or reported invalid).</summary>
    public InlineTableSyntax? TakeInlineTable(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is InlineTableSyntax table)
        {
            return table;
        }

        Invalid(pair, key, "an inline table");
        return null;
    }

    /// <summary>An authored array node for element-wise parsing, or null when absent (or reported invalid).</summary>
    public ArraySyntax? TakeArray(string key)
    {
        if (Take(key) is not { } pair)
        {
            return null;
        }

        if (pair.Value is ArraySyntax array)
        {
            return array;
        }

        Invalid(pair, key, "an array");
        return null;
    }

    /// <summary>
    /// Classifies every key no reader consumed: a member of
    /// <paramref name="deferredKeys"/> (the table's closed D-075 set) raises the
    /// transitional <c>SpecSurfaceNotYetSupported</c>; anything else raises
    /// <c>SpecKeyUnrecognized</c>.
    /// </summary>
    public void Finish(string[]? deferredKeys = null)
    {
        deferredKeys ??= NoDeferredKeys;
        foreach (var (key, dotted, node) in _items)
        {
            if (_consumed.Contains(key))
            {
                continue;
            }

            var span = node.Key?.Span ?? node.Span;
            if (!dotted && TomlSpellings.IsIn(deferredKeys, key))
            {
                _context.Error(
                    DiagnosticCode.SpecSurfaceNotYetSupported,
                    $"{_label} key '{key}' is recognized v1 surface not yet supported by this build; it will land in a later M2 slice (D-075).",
                    span);
            }
            else
            {
                _context.Error(
                    DiagnosticCode.SpecKeyUnrecognized,
                    $"{_label} key '{key}' is not recognized.",
                    span);
            }
        }
    }

    private void Invalid(KeyValueSyntax pair, string key, string expected) =>
        _context.Error(
            DiagnosticCode.SpecFieldInvalid,
            $"{_label} key '{key}' expects {expected}.",
            pair.Value?.Span ?? pair.Span);

    private static IEnumerable<KeyValueSyntax> Pairs(InlineTableSyntax table)
    {
        foreach (var item in table.Items)
        {
            if (item.KeyValue is { } pair)
            {
                yield return pair;
            }
        }
    }
}
