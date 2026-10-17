using System.Text.Json;
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

    // Bit values: file 1, line 2, column 4, attribute 8, record 16; all 32 subsets.
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
        // value and must not vanish: losing it would silently change the grammar.
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

    // The prose transport over the complete escape set and its neighbours: the input text,
    // then its exact escaped spelling. A double quote stays literal; a backslash doubles; the
    // five short escapes stay; every other C0, DEL, C1, LS and PS becomes a lowercase \u
    // escape; the printable characters on either side of each range pass through.
    private static readonly (string Value, string Prose)[] EscapeTable =
    [
        ("a\"b", "a\"b"),
        ("a\\b", "a\\\\b"),
        ("a/b", "a/b"),
        ("a\\nb", "a\\\\nb"),
        ("a\nb", "a\\nb"),
        ("a\tb", "a\\tb"),
        ("a\rb", "a\\rb"),
        ("a\bb", "a\\bb"),
        ("a\fb", "a\\fb"),
        ("a\u0000b", "a\\u0000b"),
        ("a\u0001b", "a\\u0001b"),
        ("a\u000bb", "a\\u000bb"),
        ("a\u001bb", "a\\u001bb"),
        ("a\u001fb", "a\\u001fb"),
        ("a\u0020b", "a\u0020b"),
        ("a\u007eb", "a\u007eb"),
        ("a\u007fb", "a\\u007fb"),
        ("a\u0080b", "a\\u0080b"),
        ("a\u0085b", "a\\u0085b"),
        ("a\u009fb", "a\\u009fb"),
        ("a\u00a0b", "a\u00a0b"),
        ("a\u2027b", "a\u2027b"),
        ("a\u2028b", "a\\u2028b"),
        ("a\u2029b", "a\\u2029b"),
        ("a\u202fb", "a\u202fb"),
        ("a\U0001D11Eb", "a\U0001D11Eb"),
        (string.Empty, string.Empty),
    ];

    public static TheoryData<string, string> ProseEscapes()
    {
        var data = new TheoryData<string, string>();
        foreach (var (value, prose) in EscapeTable)
        {
            data.Add(value, prose);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProseEscapes))]
    public void Render_WhenMessageCarriesSpecials_ThenTheProseTransportEscapesThem(string message, string expected)
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.NoObjectsEmitted, DiagnosticSeverity.Error, message));

        Assert.Equal($"error NoObjectsEmitted: {expected}\n", rendered);
    }

    [Fact]
    public void Render_WhenALibrarySampleIsAlreadyQuotedAndEscaped_ThenTheTransportEscapesItOnce()
    {
        // FormalAttributeNameInvalid's producer quotes each sample and escapes its backslash,
        // quote, CR and LF (D-120). The transport then escapes the whole message once more:
        // the producer's quotes stay literal and each producer backslash doubles, so an
        // actual line break in a name ends up as backslash, backslash, n on the line.
        const string message =
            "Attribute 'x' renders 3 invalid formal-attribute name(s), empty or containing CR/LF: "
            + "\"x-bad\\nline\", \"q\\\"t\", \"\" (§10.7).";

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.FormalAttributeNameInvalid, DiagnosticSeverity.Error, message,
            new DiagnosticLocation(AttributeName: "x")));

        Assert.Equal(
            "attribute=\"x\": error FormalAttributeNameInvalid: Attribute 'x' renders 3 invalid formal-attribute "
            + "name(s), empty or containing CR/LF: \"x-bad\\\\nline\", \"q\\\\\"t\", \"\" (§10.7).\n",
            rendered);
    }

    [Fact]
    public void Render_WhenAnAggregatedSampleIsQuoted_ThenTheTransportEscapesTheWholeMessageOnce()
    {
        // An aggregated sample escapes only backslash, quote, CR and LF itself (§16.4), so a tab, a
        // C1 control and a line separator reach the renderer raw and this one pass escapes them. The
        // producer's backslashes double, so an actual line break and a typed backslash-n stay apart.
        var message =
            "Attribute 'v' had 4 occurrence(s) of values that are not in its declared domain (e.g. "
            + "\"a\\nb\", \"a\\\\nb\", \"q\\\"" + (char)0x85 + (char)0x2028 + "\t\").";

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Warning, message,
            new DiagnosticLocation(AttributeName: "v")));

        Assert.Equal(
            "attribute=\"v\": warning UnknownValueObserved: Attribute 'v' had 4 occurrence(s) of values that are not in "
            + "its declared domain (e.g. \"a\\\\nb\", \"a\\\\\\\\nb\", \"q\\\\\"" + Unicode("0085") + Unicode("2028")
            + "\\t\").\n",
            rendered);
    }

    [Fact]
    public void Render_WhenTemporaryStorageStopsTheRun_ThenTheTempDirOptionIsNamedForANewRun()
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.GroupingStorageFailed, DiagnosticSeverity.Error, "Temporary storage failed."));

        Assert.Equal(
            "error GroupingStorageFailed: Temporary storage failed. "
            + "For a new run, --temp-dir DIR chooses the directory for temporary storage.\n",
            rendered);
    }

    [Fact]
    public void Render_WhenTemporaryStorageCleanupFails_ThenTheAdviceSaysAnotherDirectoryRemovesNothing()
    {
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.GroupingStorageFailed, DiagnosticSeverity.Warning, "Temporary storage cleanup failed."));

        Assert.Equal(
            "warning GroupingStorageFailed: Temporary storage cleanup failed. "
            + "Choosing another --temp-dir for a new run does not remove storage that remains from this one.\n",
            rendered);
    }

    [Theory]
    [InlineData(DiagnosticCode.SourceValueUnparseable, DiagnosticSeverity.Error)]
    [InlineData(DiagnosticCode.UnknownValueObserved, DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticCode.GroupingStorageFailed, DiagnosticSeverity.Info)]
    public void Render_WhenTheCodeAndSeverityAreNotAStorageFailure_ThenNoAdviceIsAdded(DiagnosticCode code, DiagnosticSeverity severity)
    {
        // The advice keys on the code and severity alone, never on the message text.
        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(code, severity, "Temporary storage failed."));

        Assert.EndsWith($"{code}: Temporary storage failed.\n", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenACommandAddsAdvice_ThenItFollowsTheMessageThroughTheSameTransport()
    {
        var rendered = DiagnosticRenderer.Render(
            new BedrockDiagnostic(
                DiagnosticCode.SourceBindingInvalid, DiagnosticSeverity.Error, "Attribute 'c\\d' failed.",
                new DiagnosticLocation(AttributeName: "c")),
            "Then do \"this\".");

        Assert.Equal("attribute=\"c\": error SourceBindingInvalid: Attribute 'c\\\\d' failed. Then do \"this\".\n", rendered);
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

    // The quoted-location form over the same set: identical to the prose transport except
    // that a double quote is escaped, so the field stays one valid JSON string literal.
    public static TheoryData<string, string> LocationEscapes()
    {
        var data = new TheoryData<string, string>();
        foreach (var (value, prose) in EscapeTable)
        {
            data.Add(value, value == "a\"b" ? "a\\\"b" : prose);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LocationEscapes))]
    public void Render_WhenStringLocationsCarrySpecials_ThenEachIsAJsonLiteralOfTheOriginal(string value, string expected)
    {
        var location = new DiagnosticLocation(File: value, AttributeName: value);

        var rendered = DiagnosticRenderer.Render(new BedrockDiagnostic(
            DiagnosticCode.SpecFieldInvalid, DiagnosticSeverity.Error, "m", location));

        Assert.Equal($"file=\"{expected}\" attribute=\"{expected}\": error SpecFieldInvalid: m\n", rendered);

        // Read back from the rendered line itself: each literal is valid JSON and decodes to
        // exactly the original string.
        var file = ReadLiteral(rendered, "file=");
        var attribute = ReadLiteral(rendered, " attribute=");
        Assert.Equal(value, JsonDocument.Parse(file).RootElement.GetString());
        Assert.Equal(value, JsonDocument.Parse(attribute).RootElement.GetString());
    }

    // The JSON string literal that starts right after the first occurrence of label: from its
    // opening quote to the first quote not preceded by an escaping backslash.
    private static string ReadLiteral(string line, string label)
    {
        var start = line.IndexOf(label, StringComparison.Ordinal) + label.Length;
        Assert.Equal('"', line[start]);
        var end = start + 1;
        while (line[end] != '"')
        {
            end += line[end] == '\\' ? 2 : 1;
        }

        return line[start..(end + 1)];
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
        Assert.Equal("error: a\\nb \"c\" d\\\\e\n", DiagnosticRenderer.RenderHostError("a\nb \"c\" d\\e"));
    }

    [Theory]
    [MemberData(nameof(ProseEscapes))]
    public void RenderHostError_WhenMessageCarriesSpecials_ThenTheProseTransportEscapesThem(string message, string expected)
    {
        Assert.Equal($"error: {expected}\n", DiagnosticRenderer.RenderHostError(message));
    }

    [Fact]
    public void RenderHostWarning_WhenCalled_ThenTheCodeLessWarningFormIsUsed()
    {
        Assert.Equal("warning: a code-less warning about 'out.cxt'.\n",
            DiagnosticRenderer.RenderHostWarning("a code-less warning about 'out.cxt'."));
    }

    [Fact]
    public void RenderHostWarning_WhenMessageIsEmpty_ThenOnlyThePrefixAndTheLineEndAreWritten()
    {
        Assert.Equal("warning: \n", DiagnosticRenderer.RenderHostWarning(string.Empty));
    }

    [Theory]
    [MemberData(nameof(ProseEscapes))]
    public void RenderHostWarning_WhenMessageCarriesSpecials_ThenTheProseTransportEscapesThem(string message, string expected)
    {
        Assert.Equal($"warning: {expected}\n", DiagnosticRenderer.RenderHostWarning(message));
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

    // A backslash, u and four hex digits, built rather than typed so the expectation stays text.
    private static string Unicode(string hex) => "\\" + "u" + hex;
}
