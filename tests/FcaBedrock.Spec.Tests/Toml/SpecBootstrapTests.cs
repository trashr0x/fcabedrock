using FcaBedrock.Core.Spec;
using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

// The two-stage bootstrap resolver surface (D-098/G-1): ResolveReadSettings prefix gates and the
// full-resolve success gate (strict factories run only behind a clean pass, so aggregation never
// throws).
public sealed class SpecBootstrapTests
{
    private static SpecDocument Doc(string toml)
    {
        Assert.True(SpecReader.Read(toml).TryGetValue(out var document),
            string.Join("; ", SpecReader.Read(toml).Diagnostics.Select(d => d.Code)));
        return document;
    }

    private const string MinimalWide =
        "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
        "[[attribute]]\nname = \"g\"\nsource = { kind = \"column\", index = 0 }\n" +
        "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"x\"]\n";

    // --- ResolveReadSettings prefix gates ---

    [Fact]
    public void ResolveReadSettings_WhenValidWide_ThenReturnsSettings()
    {
        var result = SpecResolver.ResolveReadSettings(Doc(MinimalWide));

        Assert.True(result.TryGetValue(out var settings));
        Assert.Equal(SourceShape.Wide, settings.Shape);
        Assert.Null(settings.Ordering);
    }

    [Fact]
    public void ResolveReadSettings_WhenExtendsAuthored_ThenThrows()
    {
        // The document must be composed before resolving (D-078); the bootstrap enforces the same
        // prefix gate as full resolve.
        var toml = "[spec]\nversion = 1\nextends = \"base.toml\"\n[binding]\nshape = \"wide\"\n";
        var document = Doc(toml);

        Assert.Throws<ArgumentException>(() => SpecResolver.ResolveReadSettings(document));
    }

    [Fact]
    public void ResolveReadSettings_WhenVersionMissing_ThenSpecVersionUnsupportedAndNoSettings()
    {
        var document = Doc("[binding]\nshape = \"wide\"\n");

        var result = SpecResolver.ResolveReadSettings(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecVersionUnsupported && d.Severity == DiagnosticSeverity.Fatal);
    }

    [Fact]
    public void ResolveReadSettings_WhenVersionUnsupported_ThenSpecVersionUnsupportedAndNoSettings()
    {
        var document = Doc("[spec]\nversion = 2\n[binding]\nshape = \"wide\"\n");

        var result = SpecResolver.ResolveReadSettings(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.SpecVersionUnsupported && d.Severity == DiagnosticSeverity.Fatal);
    }

    [Fact]
    public void ResolveReadSettings_WhenTriple_ThenCarriesOrdering()
    {
        var toml = "[spec]\nversion = 1\n[binding]\nshape = \"triple\"\nordering = \"unordered\"\n";
        var result = SpecResolver.ResolveReadSettings(Doc(toml));

        Assert.True(result.TryGetValue(out var settings));
        Assert.Equal(TripleOrdering.Unordered, settings.Ordering);
    }

    // --- full-resolve success gate: aggregation without throwing ---

    [Fact]
    public void Resolve_WhenQuoteUnsupportedAndDelimiterConflict_ThenFailsWithBothAndNoThrow()
    {
        // Two independent binding errors aggregate on the diagnostic channel; the strict factories
        // never run behind the failing gate, so no exception escapes (round-7 High-1).
        var toml = "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\ndelimiter = \"|\"\nquote_char = \"|\"\n";
        var document = Doc(toml);

        var result = SpecResolver.Resolve(document);

        Assert.False(result.TryGetValue(out _));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.QuoteCharNotSupportedV1);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.BindingDelimiterQuoteConflict);
    }

    [Fact]
    public void Resolve_WhenMultipleOutOfRangeBindings_ThenFailsWithAllAndNoThrow()
    {
        var toml =
            "[spec]\nversion = 1\n[binding]\nshape = \"wide\"\n" +
            "[[attribute]]\nname = \"a\"\nsource = { kind = \"column\", index = 5 }\n" +
            "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"x\"]\n" +
            "[[attribute]]\nname = \"b\"\nsource = { kind = \"column\", index = 6 }\n" +
            "discretizer = { kind = \"identity\" }\nscale = { kind = \"nominal\" }\ndeclared_domain = [\"y\"]\n";

        var result = SpecResolver.Resolve(Doc(toml), new SourceSchema(2));

        Assert.False(result.TryGetValue(out _));
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code == DiagnosticCode.SourceBindingInvalid));
    }
}
