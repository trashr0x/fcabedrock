using FcaBedrock.Diagnostics;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// Byte locks on the §16.4 / D-122 part 3 stderr grammar. These expectations are written
/// out literally rather than computed, so they cannot agree with the renderer by sharing
/// its logic.
/// </summary>
public sealed class DiagnosticRendererTests
{
    private const string File = "f.toml";
    private const int Line = 12;
    private const int Column = 7;
    private const string Attribute = "age";
    private const long Record = 41;
    private const string Tail = "warning UnknownValueObserved: message\n";

    // Bit values: file 1, line 2, column 4, attribute 8, record 16 — all 32 subsets.
    public static TheoryData<int, string> LocationCombinations() => new()
    {
        { 0, "" },
        { 1, "file=\"f.toml\": " },
        { 2, "line=12: " },
        { 3, "file=\"f.toml\" line=12: " },
        { 4, "column=7: " },
        { 5, "file=\"f.toml\" column=7: " },
        { 6, "line=12 column=7: " },
        { 7, "file=\"f.toml\" line=12 column=7: " },
        { 8, "attribute=\"age\": " },
        { 9, "file=\"f.toml\" attribute=\"age\": " },
        { 10, "line=12 attribute=\"age\": " },
        { 11, "file=\"f.toml\" line=12 attribute=\"age\": " },
        { 12, "column=7 attribute=\"age\": " },
        { 13, "file=\"f.toml\" column=7 attribute=\"age\": " },
        { 14, "line=12 column=7 attribute=\"age\": " },
        { 15, "file=\"f.toml\" line=12 column=7 attribute=\"age\": " },
        { 16, "record=41: " },
        { 17, "file=\"f.toml\" record=41: " },
        { 18, "line=12 record=41: " },
        { 19, "file=\"f.toml\" line=12 record=41: " },
        { 20, "column=7 record=41: " },
        { 21, "file=\"f.toml\" column=7 record=41: " },
        { 22, "line=12 column=7 record=41: " },
        { 23, "file=\"f.toml\" line=12 column=7 record=41: " },
        { 24, "attribute=\"age\" record=41: " },
        { 25, "file=\"f.toml\" attribute=\"age\" record=41: " },
        { 26, "line=12 attribute=\"age\" record=41: " },
        { 27, "file=\"f.toml\" line=12 attribute=\"age\" record=41: " },
        { 28, "column=7 attribute=\"age\" record=41: " },
        { 29, "file=\"f.toml\" column=7 attribute=\"age\" record=41: " },
        { 30, "line=12 column=7 attribute=\"age\" record=41: " },
        { 31, "file=\"f.toml\" line=12 column=7 attribute=\"age\" record=41: " },
    };

    [Theory]
    [MemberData(nameof(LocationCombinations))]
    public void Render_WhenLocationFieldsArePresent_ThenOnlyThoseFieldsRenderInTheFixedOrder(int mask, string expectedPrefix)
    {
        var location = new DiagnosticLocation(
            File: (mask & 1) != 0 ? File : null,
            Line: (mask & 2) != 0 ? Line : null,
            Column: (mask & 4) != 0 ? Column : null,
            AttributeName: (mask & 8) != 0 ? Attribute : null,
            RecordIndex: (mask & 16) != 0 ? Record : null);

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Warning, "message", location));

        Assert.Equal(expectedPrefix + Tail, rendered);
    }

    [Fact]
    public void Render_WhenLocationIsAbsent_ThenTheLineStartsAtTheSeverity()
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning, "nothing was emitted"));

        Assert.Equal("warning NoObjectsEmitted: nothing was emitted\n", rendered);
    }

    [Fact]
    public void Render_WhenLocationIsPresentButEmpty_ThenNoPrefixIsWritten()
    {
        // A DiagnosticLocation with every field null is "no populated field", exactly as
        // a null location is: the prefix and its ": " are both omitted.
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning, "m", new DiagnosticLocation()));

        Assert.Equal("warning NoObjectsEmitted: m\n", rendered);
    }

    [Fact]
    public void Render_WhenStringLocationsAreEmpty_ThenTheyStillRenderAsPresentFields()
    {
        // Populated means NON-NULL. An empty file or attribute name is a real, if odd,
        // value and must not vanish — losing it would silently change the grammar.
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Warning, "m",
            new DiagnosticLocation(File: string.Empty, AttributeName: string.Empty)));

        Assert.Equal("file=\"\" attribute=\"\": warning UnknownValueObserved: m\n", rendered);
    }

    [Fact]
    public void Render_WhenLocationIsBuiltOutOfOrder_ThenFieldOrderIsUnchanged()
    {
        // Named arguments supplied back-to-front: the rendered order is the renderer's,
        // never the construction site's.
        var location = new DiagnosticLocation(
            RecordIndex: 2, AttributeName: "a", Column: 3, Line: 4, File: "f");

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Warning, "m", location));

        Assert.Equal("file=\"f\" line=4 column=3 attribute=\"a\" record=2: warning UnknownValueObserved: m\n", rendered);
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Info, "info")]
    [InlineData(DiagnosticSeverity.Warning, "warning")]
    [InlineData(DiagnosticSeverity.Error, "error")]
    [InlineData(DiagnosticSeverity.Fatal, "fatal")]
    public void Render_WhenSeverityVaries_ThenItIsSpelledLowercase(DiagnosticSeverity severity, string expected)
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.NoObjectsEmitted, severity, "m"));

        Assert.Equal($"{expected} NoObjectsEmitted: m\n", rendered);
    }

    [Fact]
    public void Render_WhenCodeIsGiven_ThenTheExactMemberNameIsUsed()
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.SpecExtendsCycle, DiagnosticSeverity.Fatal, "m"));

        Assert.Equal("fatal SpecExtendsCycle: m\n", rendered);
    }

    [Theory]
    [InlineData("a\"b", "a\\\"b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("a/b", "a/b")]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\tb", "a\\tb")]
    [InlineData("a\rb", "a\\rb")]
    [InlineData("a\bb", "a\\bb")]
    [InlineData("a\fb", "a\\fb")]
    [InlineData("a\u0000b", "a\\u0000b")]
    [InlineData("a\u001fb", "a\\u001fb")]
    [InlineData("a\u0001b", "a\\u0001b")]
    public void Render_WhenMessageCarriesSpecials_ThenJsonEscapingIsAppliedWithoutQuotes(string message, string expected)
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Error, message));

        Assert.Equal($"error NoObjectsEmitted: {expected}\n", rendered);
    }

    [Fact]
    public void Render_WhenMessageCarriesPrintableUnicode_ThenItIsPreservedVerbatim()
    {
        // Printable Unicode is never \u-escaped and never normalized: "é" stays one
        // character, and the decomposed spelling stays decomposed.
        const string message = "caf\u00e9 / cafe\u0301 \u4e2d\u6587";

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Error, message));

        Assert.Equal($"error NoObjectsEmitted: {message}\n", rendered);
    }

    [Fact]
    public void Render_WhenStringLocationsCarrySpecials_ThenTheyRenderAsJsonStringLiterals()
    {
        var location = new DiagnosticLocation(File: "C:\\dir\\a\"b.toml", AttributeName: "a\tb\u00e9");

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.SpecFieldInvalid, DiagnosticSeverity.Error, "m", location));

        Assert.Equal(
            "file=\"C:\\\\dir\\\\a\\\"b.toml\" attribute=\"a\\tb\u00e9\": error SpecFieldInvalid: m\n", rendered);
    }

    [Fact]
    public void Render_WhenIntegerLocationsAreLarge_ThenTheyRenderInvariantAndUngrouped()
    {
        var location = new DiagnosticLocation(Line: 1234567, Column: 1000, RecordIndex: 9876543210L);

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Info, "m", location));

        Assert.Equal("line=1234567 column=1000 record=9876543210: info UnknownValueObserved: m\n", rendered);
    }

    [Fact]
    public void RenderHostError_WhenCalled_ThenTheCodeLessFormIsUsed()
    {
        Assert.Equal("error: cannot read the spec file 'a.toml'.\n",
            DiagnosticRenderer.RenderHostError("cannot read the spec file 'a.toml'."));
    }

    [Fact]
    public void RenderHostError_WhenMessageCarriesSpecials_ThenItIsEscaped()
    {
        Assert.Equal("error: a\\nb \\\"c\\\" d\\\\e\n", DiagnosticRenderer.RenderHostError("a\nb \"c\" d\\e"));
    }

    [Fact]
    public void Write_WhenManyDiagnosticsAreSupplied_ThenOrderIsPreservedOneLineEach()
    {
        // The library's order is the CLI's order: no sorting, grouping, deduplication, or
        // suppression. The duplicate pair proves nothing is collapsed.
        BedrockDiagnostic[] diagnostics =
        [
            new(DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning, "z"),
            new(DiagnosticCode.SpecFieldInvalid, DiagnosticSeverity.Error, "a"),
            new(DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning, "z"),
            new(DiagnosticCode.SpecExtendsCycle, DiagnosticSeverity.Fatal, "m"),
        ];

        var writer = new StringWriter();
        DiagnosticRenderer.Write(writer, diagnostics);

        Assert.Equal(
            "warning NoObjectsEmitted: z\n"
            + "error SpecFieldInvalid: a\n"
            + "warning NoObjectsEmitted: z\n"
            + "fatal SpecExtendsCycle: m\n",
            writer.ToString());
    }

    [Fact]
    public void HasErrors_WhenOnlyWarningsAndInfo_ThenFalse()
    {
        BedrockDiagnostic[] diagnostics =
        [
            new(DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning, "w"),
            new(DiagnosticCode.ObservedDomainUsed, DiagnosticSeverity.Info, "i"),
        ];

        Assert.False(DiagnosticRenderer.HasErrors(diagnostics));
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Error)]
    [InlineData(DiagnosticSeverity.Fatal)]
    public void HasErrors_WhenErrorOrFatalIsPresent_ThenTrue(DiagnosticSeverity severity)
    {
        BedrockDiagnostic[] diagnostics =
        [
            new(DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Warning, "w"),
            new(DiagnosticCode.SpecFieldInvalid, severity, "e"),
        ];

        Assert.True(DiagnosticRenderer.HasErrors(diagnostics));
    }
}
