using FcaBedrock.Core.Spec;

namespace FcaBedrock.Core.Tests.Spec;

/// <summary>
/// The §10.7 naming grammar (D-117/D-120): the closed placeholder set and its
/// documented alias, brace escaping, the three distinct validity rules, and the
/// single-pass/no-rescan guarantee. This is the one grammar owner, so these are
/// the tests that pin what both the Spec reader and the planner accept.
/// </summary>
public sealed class NameFormatTests
{
    [Theory]
    [InlineData("{name}", "age")]
    [InlineData("{column}", "age")]                         // the documented alias of {name}
    [InlineData("{display_name}", "Age")]
    [InlineData("{value}", "30")]
    [InlineData("{scale_op}", ">=")]
    public void Render_WhenSinglePlaceholder_ThenItsOwnSubstitution(string format, string expected) =>
        Assert.Equal(expected, Render(format));

    [Fact]
    public void Render_WhenColumnAndName_ThenTheAliasIsIndistinguishable() =>
        // §10.7 calls {column} "same as name" — so the two must render identically, not
        // merely similarly. Asserted on one format carrying both.
        Assert.Equal("age/age", Render("{column}/{name}"));

    [Fact]
    public void Render_WhenAllFivePlaceholders_ThenEachSubstitutesInPlace() =>
        Assert.Equal("age|age|Age|30|>=", Render("{name}|{column}|{display_name}|{value}|{scale_op}"));

    [Fact]
    public void Render_WhenLiteralsSurroundPlaceholders_ThenLiteralTextIsPreserved() =>
        Assert.Equal("<<age :: 30>>", Render("<<{name} :: {value}>>"));

    [Theory]
    [InlineData("{{", "{")]
    [InlineData("}}", "}")]
    [InlineData("{{}}", "{}")]
    [InlineData("{{{name}}}", "{age}")]                     // §10.7's own escaping example
    public void Render_WhenBracesDoubled_ThenLiteralBraces(string format, string expected) =>
        Assert.Equal(expected, Render(format));

    [Fact]
    public void Render_WhenSubstitutedTextContainsBraces_ThenItIsNotRescanned()
    {
        // The determinism rule (§10.7/D-117): substitution is a SINGLE left-to-right pass,
        // and §10.1 explicitly permits a name containing brace-like text. A second pass
        // would re-expand the injected "{value}" into the real value — the exact hole this
        // guarantee closes. Structural here: rendering walks tokens, never a string.
        Assert.True(NameFormat.TryCreate("{{{column}}}-{value}", out var format, out _));

        Assert.Equal("{x{value}y}-30", format.Render("x{value}y", "Display", "30", ""));
    }

    [Theory]
    [InlineData("{name}")]
    [InlineData("{value}")]
    [InlineData("{name}{value}")]                           // adjacent placeholders: empty literal spans
    [InlineData("{name}{column}{display_name}{value}{scale_op}")]
    public void TryCreate_WhenLiteralSpansAreEmpty_ThenValid(string format)
    {
        // §10.7's explicitly separated rules: the non-empty requirement belongs to the
        // format string AS A WHOLE and to display_name — never to an individual literal
        // span. Requiring non-empty literals would reject every one of these.
        Assert.True(NameFormat.TryCreate(format, out var parsed, out var error));
        Assert.Null(error);
        Assert.Equal(format, parsed.Text);
    }

    [Theory]
    [InlineData("")]                                        // the empty whole format
    [InlineData("{}")]                                      // empty placeholder
    [InlineData("{nope}")]                                  // unknown placeholder
    [InlineData("{scale}")]                                 // there is deliberately no {scale}
    [InlineData("{Name}")]                                  // case variants are typos, not synonyms
    [InlineData("{VALUE}")]
    [InlineData("{Display_Name}")]
    [InlineData("{ name }")]                                // padding is not trimmed into a match
    [InlineData("{name")]                                   // unmatched {
    [InlineData("name}")]                                   // unmatched }
    [InlineData("{name}}")]                                 // trailing unmatched }
    [InlineData("{{name}")]                                 // escaped brace then unmatched }
    [InlineData("{na{me}")]                                 // '{' inside a placeholder
    [InlineData("a\rb")]                                    // CR in literal text
    [InlineData("a\nb")]                                    // LF in literal text
    [InlineData("{name}\n")]
    public void TryCreate_WhenMalformed_ThenFailsWithAReason(string format)
    {
        Assert.False(NameFormat.TryCreate(format, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryCreate_WhenValid_ThenTextIsTheAuthoredStringVerbatim() =>
        // Round-trip fidelity (D-075): the alias survives in Text even though the token
        // model collapsed it to {name}, so a write-back cannot silently rewrite the
        // author's spelling.
        Assert.Equal("{column}-{value}", Parse("{column}-{value}").Text);

    [Fact]
    public void Render_WhenCalledTwice_ThenIdenticalBytes()
    {
        // P-7: rendering is pure over its inputs — same format + same substitutions ⇒ same
        // name, so a rendered .cxt name cannot drift between two runs of one plan.
        var format = Parse("{display_name}-{scale_op}{value}");

        Assert.Equal(
            format.Render("age", "Age", "30", ">="),
            format.Render("age", "Age", "30", ">="));
    }

    [Fact]
    public void TryCreate_WhenFormatIsNull_ThenThrows() =>
        // A null format is a programmer error, not authored input (P-14): the reader only
        // calls this with a string it actually read.
        Assert.Throws<ArgumentNullException>(() => NameFormat.TryCreate(null!, out _, out _));

    private static string Render(string format) => Parse(format).Render("age", "Age", "30", ">=");

    private static NameFormat Parse(string format)
    {
        Assert.True(NameFormat.TryCreate(format, out var parsed, out var error), error);
        return parsed;
    }
}
