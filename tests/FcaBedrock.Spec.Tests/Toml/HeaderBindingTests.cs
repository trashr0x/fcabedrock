using System.Text;
using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Sources;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

// §10.2's duplicate-header rule, finally exercised over a REAL read (M5-IP-011). Until the
// header-tolerant open landed, a duplicate or multiply-blank header threw inside the tokenizer
// at reader construction, so the spec-specified outcomes below were unreachable: index binding
// could not succeed, and the name-ambiguity SourceBindingInvalid could not be reached from
// actual bytes. Existing resolver tests fabricate a SourceSchema directly and never touched
// this path. No new diagnostic code — the outcomes are the ones §10.2/D-067 already specify.
public sealed class HeaderBindingTests
{
    private static async Task<SourceSchema> ReadSchemaAsync(string text)
    {
        var session = new WideCsvSession(
            () => new MemoryStream(Encoding.UTF8.GetBytes(text)),
            SourceReadSettings.CreateWide(hasHeader: true));
        return await session.GetSchemaAsync();
    }

    private static Diagnosed<BedrockSpec> Resolve(SpecDocument document, SourceSchema schema)
    {
        var resolved = SpecResolver.Resolve(document, schema);
        return resolved.TryGetValue(out var doc)
            ? Diagnosed<BedrockSpec>.Ok(doc.Resolved.Spec, resolved.Diagnostics)
            : Diagnosed<BedrockSpec>.Failed(resolved.Diagnostics);
    }

    [Fact]
    public async Task Resolve_WhenDuplicateHeaderBoundByIndex_ThenResolves()
    {
        // The duplicate name makes NAME binding ambiguous, not the whole source unusable:
        // an index-addressed column still resolves (§5.3/§10.2).
        var schema = await ReadSchemaAsync("colour,colour\nred,blue\n");
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("first", 0, ["red"])]);

        var result = Resolve(document, schema);

        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(["colour", "colour"], schema.Header);
        Assert.Equal(0, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).Index);
    }

    [Fact]
    public async Task Resolve_WhenNameMatchesMoreThanOneColumn_ThenSourceBindingInvalid()
    {
        var schema = await ReadSchemaAsync("colour,colour\nred,blue\n");
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("c", DocumentFixtures.NamedColumn("colour"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["red"]),
        ]);

        var result = Resolve(document, schema);

        // The existing code, at its existing phase — not a tokenizer exception, and not a new code.
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SourceBindingInvalid);
        Assert.False(result.TryGetValue(out _));
    }

    [Fact]
    public async Task Resolve_WhenBlankHeaderCellsBoundByIndex_ThenResolves()
    {
        // Multiply-blank headers were the other tokenizer-construction throw.
        var schema = await ReadSchemaAsync("a,,\nx,y,z\n");
        var document = DocumentFixtures.Document([DocumentFixtures.Nominal("third", 2, ["z"])]);

        var result = Resolve(document, schema);

        Assert.True(result.TryGetValue(out _), string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(["a", "", ""], schema.Header);
    }

    [Fact]
    public async Task Resolve_WhenUniqueHeaderBoundByName_ThenStillResolvesUnchanged()
    {
        var schema = await ReadSchemaAsync("colour,size\nred,big\n");
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("s", DocumentFixtures.NamedColumn("size"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["big"]),
        ]);

        var result = Resolve(document, schema);

        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(1, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).Index);
    }

    [Fact]
    public async Task Resolve_WhenHeaderCellEqualsMissingToken_ThenBindsByThatName()
    {
        // A header equal to missing_token is a literal, unique, name-bindable header: header cells
        // are never missing-normalized.
        var schema = await ReadSchemaAsync("?,size\nred,big\n");
        var document = DocumentFixtures.Document(
        [
            DocumentFixtures.Attribute("q", DocumentFixtures.NamedColumn("?"),
                discretizer: new IdentityDiscretizerSection(), scale: new NominalScaleSection(),
                declaredDomain: ["red"]),
        ]);

        var result = Resolve(document, schema);

        Assert.True(result.TryGetValue(out var spec), string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        Assert.Equal(0, Assert.IsType<ColumnSource>(Assert.Single(spec.Attributes).Source).Index);
    }
}
