using System.Globalization;
using System.Text;
using FcaBedrock.Core.Spec;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The Bedrock delimited-text grammar of spec §5.1.1, through all four record-reading surfaces
/// (unbound and bound, wide and triple), headerless and with a header, and at whole, 7-byte and
/// 1-byte stream reads. Every expectation is written by hand from the grammar, never derived
/// from the reader under test: decoded records (missing token disabled, so an empty value is
/// null), or a refusal with the exact message and the records yielded before it.
/// </summary>
public sealed class DelimitedGrammarConformanceTests
{
    internal enum Defect
    {
        None,
        QuoteInUnquotedField,
        TextAfterClosingQuote,
        UnterminatedQuote,
    }

    internal sealed record Case(
        string Id, string Input, char Delimiter, string?[][]? Records,
        Defect Fault = Defect.None, int? FaultRecord = null, int FaultColumn = 0,
        long StartLine = 1, long FaultLine = 1, int RecordsBeforeFault = 0, bool Header = false,
        bool Triple = false, string Note = "");

    private static string?[] R(params string?[] fields) => fields;

    internal static readonly Case[] Cases =
    [
        new("V01", "a,b\nc,d", ',', [R("a", "b"), R("c", "d")], Note: "plain, final EOF without terminator"),
        new("V02", "a,b\n", ',', [R("a", "b")], Note: "terminal terminator makes no phantom record"),
        new("V03", "a,b\r\nc,d\r\n", ',', [R("a", "b"), R("c", "d")], Note: "CRLF"),
        new("V04", "a,b\rc,d", ',', [R("a", "b"), R("c", "d")], Note: "lone CR terminates"),
        new("V05", "\t a \t, b\u00A0", ',', [R("a", "b")], Note: "TAB/space/NBSP outer W trimmed"),
        new("V06", "\u2000x\u3000,\u202Fy\u205F", ',', [R("x", "y")], Note: "U+2000/U+3000/U+202F/U+205F outer W"),
        new("V07", " \"x\" ,y", ',', [R("x", "y")], Note: "space outside quotes"),
        new("V08", "\t\"x\"\t,y", ',', [R("x", "y")], Note: "TAB outside quotes"),
        new("V09", "\"  x  \",y", ',', [R("  x  ", "y")], Note: "quoted inner spaces preserved"),
        new("V10", "\"a,b\",c", ',', [R("a,b", "c")], Note: "quoted delimiter"),
        new("V11", "\"a\"\"b\",c", ',', [R("a\"b", "c")], Note: "doubled quote"),
        new("V12", "\"\"\"\",c", ',', [R("\"", "c")], Note: "only an escaped quote"),
        new("V13", "\"\",c", ',', [R(null, "c")], Note: "quoted empty is empty (missing)"),
        new("V14", "\" \",c", ',', [R(" ", "c")], Note: "quoted space is present"),
        new("V15", "\"p\r\n\r\nq\",r", ',', [R("p\r\n\r\nq", "r")], Note: "quoted CRLF and blank physical line"),
        new("V16", "\"p\rq\",r\n\"s\nt\",u", ',', [R("p\rq", "r"), R("s\nt", "u")], Note: "quoted CR and LF"),
        new("V17", "a b,c  d", ',', [R("a b", "c  d")], Note: "interior whitespace kept"),
        new("V18", "caf\u00E9,na\u00EFve,\u65E5\u672C", ',', [R("caf\u00E9", "na\u00EFve", "\u65E5\u672C")], Note: "Unicode text"),
        new("V19", "x\u200By,\uFEFFz", ',', [R("x\u200By", "\uFEFFz")], Note: "U+200B/U+FEFF are content"),
        new("V20", "\u200Bx\u180E", ',', [R("\u200Bx\u180E")], Note: "U+200B/U+180E at edges are not trimmed"),
        new("V21", ",,", ',', [R(null, null, null)], Note: "delimiter-only record retained"),
        new("V22", "\t\t", '\t', [R(null, null, null)], Note: "TAB-delimited TAB TAB = three empty cells"),
        new("V23", " a ", ' ', [R(null, "a", null)], Note: "space-delimited space-a-space = three cells"),
        new("V24", "\t\"x y\"\t b", ' ', [R("x y", "b")], Note: "space delimiter, TAB is outer W"),
        new("V25", " a \t b ", '\t', [R("a", "b")], Note: "TAB delimiter, spaces are outer W"),
        new("V26", "\"a\" ,\"b\"\t", ',', [R("a", "b")], Note: "outer W after closing quotes"),
        new("V27", "a;\"b;c\";d", ';', [R("a", "b;c", "d")], Note: "semicolon"),
        new("V28", "a|b\n\"c|d\"|e", '|', [R("a", "b"), R("c|d", "e")], Note: "pipe"),
        new("V29", "a\u001Fb\u001F\"c\u001Fd\"", '\u001F', [R("a", "b", "c\u001Fd")], Note: "U+001F delimiter"),
        new("V30", "a:\"b:c\"", ':', [R("a", "b:c")], Note: "colon"),
        new("V31", "\"a\"\"\" ,b", ',', [R("a\"", "b")], Note: "escape then close then outer W"),
        new("V32", "\u3000\"\u3000x\u3000\"\u3000", ',', [R("\u3000x\u3000")], Note: "U+3000 outside kept out, inside kept"),
        new("V33", "\"p\r\rq\",r\n\"\r\r\",s\na\r\rb,c", ',', [R("p\r\rq", "r"), R("\r\r", "s"), R("a"), R("b", "c")], Note: "consecutive CRs inside quotes and as terminators; short reads can straddle the pair"),
        new("B01", "\n\na,b\n\n  \nc,d\n\n", ',', [R("a", "b"), R("c", "d")], Note: "blank runs before, between and after"),
        new("B02", "  \t \r\n", ',', [], Note: "only a blank candidate"),
        new("B03", "\"\"\n", ',', [R([null])], Note: "quoted empty record retained"),
        new("B04", "\"\r\n\"\n", ',', [R("\r\n")], Note: "quoted multiline blank retained"),
        new("B05", "\u00A0\u3000\na", ',', [R("a")], Note: "Unicode-whitespace-only candidate is blank"),
        new("B06", "\t\n", '\t', [R(null, null)], Note: "TAB-delimited TAB line is a delimiter-only record"),
        new("B07", " \n\t\n", ' ', [R(null, null)], Note: "space delimiter: space line retained, TAB line blank"),
        new("B08", "", ',', [], Note: "empty input has no records"),
        new("B09", "\n", ',', [], Note: "single empty line"),
        new("B10", "\r\n\r\n\r\n", ',', [], Note: "CRLF blank run"),
        new("M01", "a\"b,c", ',', null, Defect.QuoteInUnquotedField, 0, 0, Note: "stray quote mid-value (swallows rest)"),
        new("M02", "a\"b\",c", ',', null, Defect.QuoteInUnquotedField, 0, 0, Note: "balanced stray quotes"),
        new("M03", "\"a\"x,c", ',', null, Defect.TextAfterClosingQuote, 0, 0, Note: "suffix after close"),
        new("M04", "\"a\" \"b\",c", ',', null, Defect.TextAfterClosingQuote, 0, 0, Note: "reopen after close+space; split escape"),
        new("M05", "\"\"\"", ',', null, Defect.UnterminatedQuote, 0, 0, Note: "EOF inside quotes after an escape"),
        new("M06", "a,\"b", ',', null, Defect.UnterminatedQuote, 0, 1, Note: "EOF-open second column"),
        new("M07", "\"a\nb\nc", ',', null, Defect.UnterminatedQuote, 0, 0, Note: "multiline EOF-open"),
        new("M08", "x\"y,z\",q", ',', null, Defect.QuoteInUnquotedField, 0, 0, Note: "balanced stray pair hides a separator"),
        new("M09", "x\"y,z\nw,q\n", ',', null, Defect.QuoteInUnquotedField, 0, 0, Note: "stray quote hides separators and newlines"),
        new("M10", "a,b\nc,x\"y\"\nd,e", ',', null, Defect.QuoteInUnquotedField, 1, 1, StartLine: 2, FaultLine: 2, RecordsBeforeFault: 1, Note: "fault in record 1, record 0 yielded first"),
        new("M11", "s,p,v,x\"y\n", ',', null, Defect.QuoteInUnquotedField, 0, 3, Triple: true, Note: "malformed unselected triple column"),
        new("M12", "\"a\"\"\"b\",c", ',', null, Defect.TextAfterClosingQuote, 0, 0, Note: "escape, close, then text"),
        new("M13", "a,\"b\nc", ',', null, Defect.UnterminatedQuote, null, 1, Header: true, Note: "malformed header"),
        new("M14", "\"a\"\u00A0x", ',', null, Defect.TextAfterClosingQuote, 0, 0, Note: "NBSP then text after close"),
        new("M15", "ok\n\n\"x\n\ny\"z,1", ',', null, Defect.TextAfterClosingQuote, 1, 0, StartLine: 3, FaultLine: 5, RecordsBeforeFault: 1, Note: "line accounting over a blank and a multiline field"),
        new("M16", "a,b,c,d,e,x\"y", ',', null, Defect.QuoteInUnquotedField, 0, 5, Note: "malformed last column of a wide record (all columns validated)"),
    ];

    private static readonly int[] ReadSizes = [int.MaxValue, 7, 1];

    public static TheoryData<string> CaseIds() => new(Cases.Select(c => c.Id));

    /// <summary>The spec's refusal message for a malformed field, stated independently of the reader.</summary>
    internal static string Message(Defect defect, int? record, long startLine, int column, long faultLine)
    {
        var where = record is { } index
            ? string.Create(CultureInfo.InvariantCulture, $"data record {index}")
            : "the header record";
        var what = defect switch
        {
            Defect.QuoteInUnquotedField => "a quote appears inside an unquoted field",
            Defect.TextAfterClosingQuote => "text follows the closing quote of a quoted field",
            Defect.UnterminatedQuote => "a quoted field is not closed before the end of the input",
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The source could not be read: {where}, column {column}, starting on line {startLine} (the defect is on line {faultLine}): {what}.");
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Read_WhenTheInputIsAGrammarCase_ThenEverySurfaceDecodesOrRefusesItExactly(string id)
    {
        var c = Cases.Single(x => x.Id == id);
        foreach (var withHeader in c.Header ? [true] : new[] { false, true })
        {
            // A non-header case is also read behind a prepended header line: the data records and
            // their indices are unchanged, and every physical line moves down by one.
            var input = c.Header || !withHeader ? c.Input : "h1\n" + c.Input;
            var shift = c.Header || !withHeader ? 0 : 1;
            foreach (var size in ReadSizes)
            {
                foreach (var surface in Surfaces)
                {
                    var outcome = await surface.ReadAsync(input, c.Delimiter, withHeader, size);
                    var expected = Expected(c, surface.Triple, shift);
                    Assert.True(
                        outcome == expected,
                        $"{c.Id} ({c.Note}) via {surface.Name}, header={withHeader}, read size={size}:\n  got      {outcome}\n  expected {expected}");
                }
            }
        }
    }

    private static string Expected(Case c, bool triple, int shift)
    {
        if (c.Records is { } records)
        {
            return "records " + string.Join(" ", records.Select(r => Render(triple ? Project(r) : r)));
        }

        var message = Message(c.Fault, c.FaultRecord, c.StartLine + shift, c.FaultColumn, c.FaultLine + shift);
        return $"after {c.RecordsBeforeFault} record(s): SourceReadException(no inner) {message}";
    }

    private static string?[] Project(string?[] fields) =>
        [fields.Length > 0 ? fields[0] : null, fields.Length > 1 ? fields[1] : null, fields.Length > 2 ? fields[2] : null];

    // ---- the four reading surfaces -------------------------------------------------------

    internal sealed record Surface(string Name, bool Triple, Func<string, char, bool, int, Task<string>> ReadAsync);

    internal static readonly Surface[] Surfaces =
    [
        new("unbound wide", false, (text, delimiter, header, size) =>
            Outcome(WideSession(Opener(text, size), delimiter, header, string.Empty).ReadAsync(), Fields)),
        new("bound wide", false, (text, delimiter, header, size) =>
            Outcome(new WideCsvSource(Opener(text, size), WideBinding(delimiter, header, string.Empty)).ReadAsync(), Fields)),
        new("unbound triple", true, (text, delimiter, header, size) =>
            Outcome(TripleSession(Opener(text, size), delimiter, header, string.Empty).ReadRowsAsync(new TripleColumns(0, 1, 2)), Roles)),
        new("bound triple", true, (text, delimiter, header, size) =>
            Outcome(new TripleCsvSource(Opener(text, size), TripleBinding(delimiter, header, string.Empty)).ReadRowsAsync(), Roles)),
    ];

    private static string?[] Roles(TripleRow row) => [row.Subject, row.Predicate, row.Value];

    internal static async Task<string> Outcome<T>(IAsyncEnumerable<T> source, Func<T, string?[]> fields)
    {
        var (items, error) = await DrainCapturingAsync(source);
        if (error is null)
        {
            return "records " + string.Join(" ", items.Select(i => Render(fields(i))));
        }

        return error is SourceReadException read
            ? $"after {items.Count} record(s): SourceReadException({(read.InnerException is null ? "no inner" : read.InnerException.GetType().Name)}) {read.Message}"
            : $"after {items.Count} record(s): {error.GetType().Name} {error.Message}";
    }

    // ---- the quote-free boundaries -------------------------------------------------------

    [Fact]
    public async Task Read_WhenQuoteFreeFieldsHaveWhitespaceAtTheirEdges_ThenOnlyWhitespaceIsRemovedAndEmptyFieldsAreMissing()
    {
        var text = U(0xA0) + "a" + U(0xA0) + ",\tb\t," + U(0x3000) + "c\n,,\n";

        Assert.Equal("records [<a>,<b>,<c>] [null,null,null]", await WideAsync(text, ','));
    }

    [Fact]
    public async Task Read_WhenTheDelimiterIsTab_ThenAQuoteFreeFieldLosesNoBreakSpacesButNeverTheDelimiter()
    {
        var text = "a" + U(0xA0) + "\t" + U(0xA0) + "b\n\t\n";

        Assert.Equal("records [<a>,<b>] [null,null]", await WideAsync(text, '\t'));
    }

    [Fact]
    public async Task Read_WhenTheOnlyQuoteIsInTheLastField_ThenEveryFieldIsDecodedByTheGrammar()
    {
        Assert.Equal("records [<a>,<b>,<c,d>] [<e>,<f>,<g>]", await WideAsync("a, b ,\"c,d\"\ne,f,g\n", ','));
    }

    [Fact]
    public async Task Read_WhenOneColumnCandidatesAreBlankOrNot_ThenOnlyTheBlankOnesAreSkipped()
    {
        // NBSP-only and TAB-only are blank; NBSP then text is not; a quoted empty field is retained.
        var text = U(0xA0) + "\n\t\n" + U(0xA0) + "x\n\"\"\n";

        Assert.Equal("records [<x>] [null]", await WideAsync(text, ','));
    }

    [Fact]
    public async Task Read_WhenTheHeadersOnlyQuoteIsInOneCell_ThenTheHeaderIsValidatedAndConsumed()
    {
        var records = await DrainAsync(WideSession(Opener("h1,\"h,2\"\n1,2\n"), hasHeader: true).ReadAsync());

        Assert.Equal(["0:[<1>,<2>]"], records.Select(Render));
    }

    [Fact]
    public async Task Read_WhenATripleCandidatesOnlyQuoteIsInAnUnusedColumn_ThenItIsValidatedButNotMaterialized()
    {
        var rows = await DrainAsync(TripleSession(Opener("s,p,v,\"u,u\"\ns2,p2,v2,u\n")).ReadRowsAsync(new TripleColumns(0, 1, 2)));

        Assert.Equal(["0:[<s>,<p>,<v>]", "1:[<s2>,<p2>,<v2>]"], rows.Select(Render));
    }

    [Fact]
    public async Task Read_WhenATripleCandidatesOnlyQuoteIsMalformedInAnUnusedColumn_ThenItIsRefusedAtThatColumn()
    {
        var outcome = await Outcome(
            TripleSession(Opener("s,p,v,u\ns2,p2,v2,u\"x\n")).ReadRowsAsync(new TripleColumns(0, 1, 2)), Roles);

        Assert.Equal(
            "after 1 record(s): SourceReadException(no inner) " + Message(Defect.QuoteInUnquotedField, 1, 2, 3, 2),
            outcome);
    }

    [Fact]
    public async Task Read_WhenQuotedContentHasEscapedQuotesInEveryPosition_ThenEachDecodesToOneQuote()
    {
        var q = "\"";
        var pairs = string.Concat(Enumerable.Repeat(q + q, 50));
        var text = string.Join(
            ",",
            q + q + q + q,
            q + "a" + q + q + "b" + q,
            q + q + q + "a" + q + q + q,
            q + "x" + q + q + q + q + "y" + q,
            q + new string('x', 200) + q + q + new string('y', 200) + q,
            q + pairs + q,
            " " + q + "c" + q + q + q + " ") + "\n";
        var expected = Render(
        [
            q,
            "a" + q + "b",
            q + "a" + q,
            "x" + q + q + "y",
            new string('x', 200) + q + new string('y', 200),
            new string('"', 50),
            "c" + q,
        ]);

        Assert.Equal("records " + expected, await WideAsync(text, ','));
    }

    [Theory]
    [InlineData(0, 1, 5, "s,p,u1,u2,u3,v\ns2,p2,,,,v2\n")]
    [InlineData(0, 1, 2, "s,p,v,u,u,u,u,u,u,u,u,u,u,u,u,u,u,u,u,u,u\n s2\t,p2 , v2,,,,,,,,,,,,,,,,,,,\n")]
    public async Task Read_WhenAQuoteFreeTripleCandidateHasUnusedColumnsAroundItsRoles_ThenOnlyTheRolesAreDecoded(
        int subject, int predicate, int value, string text)
    {
        var rows = await DrainAsync(TripleSession(Opener(text)).ReadRowsAsync(new TripleColumns(subject, predicate, value)));

        Assert.Equal(["0:[<s>,<p>,<v>]", "1:[<s2>,<p2>,<v2>]"], rows.Select(Render));
    }

    private static Task<string> WideAsync(string text, char delimiter) =>
        Outcome(WideSession(Opener(text), delimiter).ReadAsync(), Fields);

    // ---- a seeded property over valid text, and stray-quote mutations -------------------

    [Fact]
    public async Task Read_WhenSeededValidFilesAreReadWithRandomChunking_ThenEveryRecordDecodesExactly()
    {
        var random = new Random(20260930);
        var delimiters = new[] { ',', '\t', ' ', ';', '|', (char)0x1F, ':' };
        var alphabet = ("ab xyz" + U(0xE9) + U(0x65E5) + ",;|:\t\"\r\n" + U(0xA0) + U(0x3000) + U(0x200B) + U(0x1F)).ToCharArray();
        var outer = new[] { ' ', '\t', (char)0xA0, (char)0x3000, (char)0x2000 };
        for (var file = 0; file < 300; file++)
        {
            var delimiter = delimiters[file % delimiters.Length];
            var builder = new StringBuilder();
            var expected = new List<string?[]>();
            var unquotedSites = new List<(int Record, int Column, int Start, int Length)>();
            var recordCount = random.Next(1, 12);
            for (var r = 0; r < recordCount; r++)
            {
                var fieldCount = random.Next(1, 6);
                var fields = new string?[fieldCount];
                for (var f = 0; f < fieldCount; f++)
                {
                    if (f > 0)
                    {
                        builder.Append(delimiter);
                    }

                    var value = new string([.. Enumerable.Range(0, random.Next(0, 7)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
                    var canUnquote = value.Length > 0 && value.IndexOfAny(['"', delimiter, '\r', '\n']) < 0
                        && !char.IsWhiteSpace(value[0]) && !char.IsWhiteSpace(value[^1]);
                    var padLeft = random.Next(3) == 0 ? Pad(random, outer, delimiter) : string.Empty;
                    var padRight = random.Next(3) == 0 ? Pad(random, outer, delimiter) : string.Empty;
                    if (canUnquote && random.Next(2) == 0)
                    {
                        builder.Append(padLeft);
                        unquotedSites.Add((r, f, builder.Length, value.Length));
                        builder.Append(value).Append(padRight);
                    }
                    else
                    {
                        builder.Append(padLeft).Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"').Append(padRight);
                    }

                    fields[f] = value.Length == 0 ? null : value;
                }

                expected.Add(fields);
                if (r < recordCount - 1 || random.Next(2) == 0)
                {
                    builder.Append(random.Next(3) switch { 0 => "\n", 1 => "\r\n", _ => "\r" });
                }
            }

            var text = builder.ToString();
            var chunk = random.Next(1, 3) == 1 ? 1 : 64;
            var got = await Outcome(WideSession(Opener(text, chunk), delimiter, missingToken: string.Empty).ReadAsync(), Fields);
            Assert.Equal("records " + string.Join(" ", expected.Select(Render)), got);

            // A stray quote inserted inside a non-empty unquoted value (never at its start) is refused at
            // its own record and column, after every earlier record was yielded.
            var site = unquotedSites.Count > 0 ? unquotedSites[random.Next(unquotedSites.Count)] : default;
            if (site.Length > 0)
            {
                var mutated = text.Insert(site.Start + random.Next(1, site.Length + 1), "\"");
                var outcome = await Outcome(WideSession(Opener(mutated, 64), delimiter, missingToken: string.Empty).ReadAsync(), Fields);
                Assert.StartsWith($"after {site.Record} record(s): SourceReadException(no inner)", outcome, StringComparison.Ordinal);
                Assert.Contains($"data record {site.Record}, column {site.Column}", outcome, StringComparison.Ordinal);
                Assert.EndsWith("a quote appears inside an unquoted field.", outcome, StringComparison.Ordinal);
            }
        }
    }

    private static string Pad(Random random, char[] outer, char delimiter)
    {
        var chars = outer.Where(o => o != delimiter).ToArray();
        return new string([.. Enumerable.Range(0, random.Next(1, 3)).Select(_ => chars[random.Next(chars.Length)])]);
    }
}
