using Tomlyn.Syntax;

namespace FcaBedrock.Spec.Toml;

/// <summary>Small Tomlyn CST accessors shared by the reader.</summary>
internal static class TomlSyntaxHelpers
{
    /// <summary>The text of a bare or quoted key node; null for malformed nodes.</summary>
    internal static string? KeyText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text,
        StringValueSyntax text => text.Value,
        _ => null,
    };

    /// <summary>All parts of a possibly-dotted key, in order.</summary>
    internal static string[] KeyParts(KeySyntax name)
    {
        var parts = new List<string>(1 + (name.DotKeys?.ChildrenCount ?? 0));
        if (KeyText(name.Key) is { } head)
        {
            parts.Add(head);
        }

        if (name.DotKeys is { } dotKeys)
        {
            foreach (var item in dotKeys)
            {
                if (KeyText(item.Key) is { } part)
                {
                    parts.Add(part);
                }
            }
        }

        return [.. parts];
    }

    /// <summary>A numeric node's value — integer and float nodes both map to double; null otherwise.</summary>
    internal static double? AsDouble(ValueSyntax? value) => value switch
    {
        IntegerValueSyntax integer => integer.Value,
        FloatValueSyntax floating => floating.Value,
        _ => null,
    };
}
