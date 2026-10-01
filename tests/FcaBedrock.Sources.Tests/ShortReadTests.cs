using FcaBedrock.Core.Spec;
using nietras.SeparatedValues;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// Spec §5.1.1 keeps CR and LF inside quoted content exactly, in order, and a stream may return
/// fewer bytes than requested on any read. These product regressions read legal CR CR content and
/// CR record terminators through the sources over short-reading streams and compare with
/// expectations built from each input's own parts. They do not depend on how the reading code
/// achieves this (D-137), so they stay whatever happens to the whole-span reader.
/// <para>
/// Where a test needs the provider's request boundaries (its first fill and the fills after its
/// buffer grows), it observes them by reading the same input through Sep itself over a recording
/// reader with the read's own options. The request sizes are observations of the pinned provider,
/// not product constants.
/// </para>
/// </summary>
public sealed class ShortReadTests
{
    private static string?[] Decoded(params string[] fields) => [.. fields.Select(f => f.Length == 0 ? null : f)];

    // The raw candidate sequence the reading rules receive, before blank skipping: every candidate,
    // its fields in order, each as untouched text (quotes and CR content included).
    private static string[] Raw(Func<Stream> open) => [.. DelimitedSourceReaderTests.RawCandidates(open).Select(Render)];

    private static string[] Raw(string text, int readSize) => Raw(Opener(text, readSize));

    private static string[] Expected(params string[][] candidates) => [.. candidates.Select(Render)];

    // ---- short-read scans ------------------------------------------------------------------

    // "h", then a record whose quoted value holds CR CR after `pad` units, then "tail,1".
    private static string Padded(int pad) => "h\n" + new string('x', pad) + ",\"a\r\rb\",c\ntail,1\n";

    private static string[] PaddedExpected(int pad) =>
    [
        Render(Decoded("h")),
        Render(Decoded(new string('x', pad), "a\r\rb", "c")),
        Render(Decoded("tail", "1")),
    ];

    [Fact]
    public async Task Read_WhenAStreamReturnsShortReadsAcrossACarriageReturnPair_ThenQuotedContentAndRecordOrderAreExact()
    {
        // 1,000-byte reads over 3,000 paddings place the pair across every read boundary alignment.
        for (var pad = 1; pad <= 3_000; pad++)
        {
            var text = Padded(pad);
            var wide = await DrainAsync(WideSession(Opener(text, 1_000)).ReadAsync());
            var triple = await DrainAsync(TripleSession(Opener(text, 1_000)).ReadRowsAsync(new TripleColumns(0, 1, 2)));

            Assert.Equal(PaddedExpected(pad), wide.Select(r => Render(Fields(r))));
            Assert.Equal(
                [Render(Decoded("h", string.Empty, string.Empty)), Render(Decoded(new string('x', pad), "a\r\rb", "c")), Render(Decoded("tail", "1", string.Empty))],
                triple.Select(r => Render([r.Subject, r.Predicate, r.Value])));
            Assert.Equal(Expected(["h"], [new string('x', pad), "\"a\r\rb\"", "c"], ["tail", "1"]), Raw(text, 1_000));
        }
    }

    [Fact]
    public async Task Read_WhenTheStreamsReadSizesAreIrregular_ThenQuotedContentAndRecordOrderAreExact()
    {
        var sizes = new[] { 1_001, 1, 7, 2, 1_000, 3, 4_093, 5 };
        for (var pad = 1; pad <= 600; pad++)
        {
            var text = Padded(pad);
            Func<Stream> open = () => new ScriptedStream(Utf8(text)) { ChunkAt = call => sizes[call % sizes.Length] };

            var records = await DrainAsync(WideSession(open).ReadAsync());

            Assert.Equal(PaddedExpected(pad), records.Select(r => Render(Fields(r))));
            Assert.Equal(Expected(["h"], [new string('x', pad), "\"a\r\rb\"", "c"], ["tail", "1"]), Raw(open));
        }
    }

    [Fact]
    public async Task Read_WhenCarriageReturnPairsTerminateRecordsAcrossShortReads_ThenTheRecordsKeepTheirOrder()
    {
        // Unquoted CR CR is two terminators: the empty record between them is blank and skipped.
        for (var pad = 1; pad <= 1_000; pad++)
        {
            var text = new string('x', pad) + "\r\rb,c\rd\r\re\n";

            var records = await DrainAsync(WideSession(Opener(text, 97)).ReadAsync());

            Assert.Equal(
                [Render(Decoded(new string('x', pad))), Render(Decoded("b", "c")), Render(Decoded("d")), Render(Decoded("e"))],
                records.Select(r => Render(Fields(r))));
            Assert.Equal(
                Expected([new string('x', pad)], [string.Empty], ["b", "c"], ["d"], [string.Empty], ["e"]),
                Raw(text, 97));
        }
    }

    // ---- the provider's request boundaries -------------------------------------------------

    private static SepReaderOptions ProviderOptions(char delimiter = ',') =>
        Sep.New(delimiter).Reader(o => o with { HasHeader = false, Unescape = false, Trim = SepTrim.None, DisableColCountCheck = true });

    /// <summary>Each request the provider makes while reading <paramref name="text"/> with full reads.</summary>
    internal static List<(int Requested, int Returned, char Last)> ObserveRequests(string text)
    {
        var recorder = new RecordingReader(text);
        using (var sep = ProviderOptions().From(recorder))
        {
            while (sep.MoveNext())
            {
            }
        }

        return recorder.Calls;
    }

    /// <summary>
    /// Whether a request of more than one unit ended exactly at <paramref name="end"/>, came back full
    /// and ended in CR, and was followed by a one-unit read that returned CR: the provider's own
    /// path for a CR that fills its request.
    /// </summary>
    internal static bool ReachesFullRequestCarriageReturn(List<(int Requested, int Returned, char Last)> calls, int end)
    {
        var position = 0;
        for (var i = 0; i < calls.Count; i++)
        {
            position += calls[i].Returned;
            if (position == end && calls[i].Requested > 1)
            {
                return calls[i].Returned == calls[i].Requested && calls[i].Last == '\r'
                    && i + 1 < calls.Count && calls[i + 1] is { Requested: 1, Returned: 1, Last: '\r' };
            }
        }

        return false;
    }

    /// <summary>"a," + a quoted value of <paramref name="length"/> units with CR at the given value offsets + ",z", then "b,c".</summary>
    internal static (string Text, string Value) QuotedRecord(string prefix, int length, params int[] carriageReturns)
    {
        var value = new string('x', length).ToCharArray();
        foreach (var offset in carriageReturns)
        {
            value[offset] = '\r';
        }

        return (prefix + "a,\"" + new string(value) + "\",z\nb,c\n", new string(value));
    }

    /// <summary>The value offsets of a CR pair whose first CR is the last unit of the provider's request <paramref name="request"/>.</summary>
    internal static (int First, int Second) PairAtRequestEnd(string prefix, int length, int request)
    {
        var (shape, _) = QuotedRecord(prefix, length);
        var calls = ObserveRequests(shape);
        var end = calls.Take(request + 1).Sum(c => c.Returned);
        var valueStart = prefix.Length + 3;
        return (end - 1 - valueStart, end - valueStart);
    }

    public static TheoryData<string, int, int> Boundaries() => new()
    {
        // The first fill: the record starts the input.
        { string.Empty, 40_000, 0 },
        // After the buffer grows: a short first record is read during initialization, then a quoted
        // record longer than the initial buffer forces growth before the later requests.
        { "h\n", 90_000, 1 },
        { "h\n", 400_000, 2 },
        { "h\n", 400_000, 3 },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public async Task Read_WhenACarriageReturnPairStraddlesTheProvidersRequestBoundary_ThenTheQuotedValueIsExact(
        string prefix, int length, int request)
    {
        var (first, second) = PairAtRequestEnd(prefix, length, request);
        var (text, value) = QuotedRecord(prefix, length, first, second);
        Assert.True(
            ReachesFullRequestCarriageReturn(ObserveRequests(text), prefix.Length + 3 + second),
            "the input must reach the provider's full-request carriage-return path");

        var expected = (prefix.Length == 0 ? [] : new[] { Render(Decoded(prefix.TrimEnd('\n'))) })
            .Concat([Render(Decoded("a", value, "z")), Render(Decoded("b", "c"))])
            .ToArray();
        foreach (var readSize in new[] { int.MaxValue, 1_000, 97 })
        {
            var records = await DrainAsync(WideSession(Opener(text, readSize)).ReadAsync());
            Assert.Equal(expected, records.Select(r => Render(Fields(r))));
            Assert.Equal(
                (prefix.Length == 0 ? [] : Expected([prefix.TrimEnd('\n')])).Concat(Expected(["a", "\"" + value + "\"", "z"], ["b", "c"])),
                Raw(text, readSize));
        }
    }

    [Fact]
    public async Task Read_WhenUnquotedCarriageReturnTerminatorsStraddleTheFirstFill_ThenTheRecordsKeepTheirOrder()
    {
        var p = ObserveRequests(new string('x', 40_000))[0].Requested - 1;
        var text = new string('x', p) + "\r\rb,c\n";
        Assert.True(ReachesFullRequestCarriageReturn(ObserveRequests(text), p + 1));

        foreach (var readSize in new[] { int.MaxValue, 1_000, 97 })
        {
            var records = await DrainAsync(WideSession(Opener(text, readSize)).ReadAsync());
            Assert.Equal([Render(Decoded(new string('x', p))), Render(Decoded("b", "c"))], records.Select(r => Render(Fields(r))));
            Assert.Equal(Expected([new string('x', p)], [string.Empty], ["b", "c"]), Raw(text, readSize));
        }
    }

    [Fact]
    public async Task Read_WhenTheInputEndsRightAfterACarriageReturnPairAtTheFirstFill_ThenNoRecordIsLostOrInvented()
    {
        var p = ObserveRequests(new string('x', 40_000))[0].Requested - 1;
        var unquoted = new string('x', p) + "\r\r";
        var quoted = "\"" + new string('x', p - 2) + "\"\r\r";
        Assert.True(ReachesFullRequestCarriageReturn(ObserveRequests(unquoted), p + 1));
        Assert.True(ReachesFullRequestCarriageReturn(ObserveRequests(quoted), p + 1));

        foreach (var readSize in new[] { int.MaxValue, 1_000, 97 })
        {
            Assert.Equal([Render(Decoded(new string('x', p)))], (await DrainAsync(WideSession(Opener(unquoted, readSize)).ReadAsync())).Select(r => Render(Fields(r))));
            Assert.Equal([Render(Decoded(new string('x', p - 2)))], (await DrainAsync(WideSession(Opener(quoted, readSize)).ReadAsync())).Select(r => Render(Fields(r))));
            Assert.Equal(Expected([new string('x', p)], [string.Empty]), Raw(unquoted, readSize));
            Assert.Equal(Expected(["\"" + new string('x', p - 2) + "\""], [string.Empty]), Raw(quoted, readSize));
        }
    }

    [Fact]
    public async Task Schema_WhenAQuotedHeaderCellCarriesACarriageReturnPairAtTheFirstFill_ThenTheHeaderIsExact()
    {
        var p = ObserveRequests(new string('x', 40_000))[0].Requested - 1;
        var cell = new string('h', p - 1) + "\r\rend";
        var text = "\"" + cell + "\",h2\n1,2\n";
        Assert.True(ReachesFullRequestCarriageReturn(ObserveRequests(text), p + 1));

        foreach (var readSize in new[] { int.MaxValue, 1_000, 97 })
        {
            var schema = await WideSession(Opener(text, readSize), hasHeader: true).GetSchemaAsync();
            Assert.Equal([cell, "h2"], schema.Header);
            Assert.Equal(Expected(["\"" + cell + "\"", "h2"], ["1", "2"]), Raw(text, readSize));
        }
    }

    [Fact]
    public async Task ReadRows_WhenAQuotedTripleValueCarriesACarriageReturnPairAtTheFirstFill_ThenTheValueIsExact()
    {
        var p = ObserveRequests(new string('x', 40_000))[0].Requested - 1;
        var value = new string('v', p - 5) + "\r\rw";
        var text = "s,p,\"" + value + "\"\ns2,p,v\n";
        Assert.True(ReachesFullRequestCarriageReturn(ObserveRequests(text), p + 1));

        foreach (var readSize in new[] { int.MaxValue, 1_000, 97 })
        {
            var rows = await DrainAsync(TripleSession(Opener(text, readSize)).ReadRowsAsync(new TripleColumns(0, 1, 2)));
            Assert.Equal([Render(Decoded("s", "p", value)), Render(Decoded("s2", "p", "v"))], rows.Select(r => Render([r.Subject, r.Predicate, r.Value])));
            Assert.Equal(Expected(["s", "p", "\"" + value + "\""], ["s2", "p", "v"]), Raw(text, readSize));
        }
    }

    /// <summary>A full-read reader over a string that records each span request, what came back and its last unit.</summary>
    internal sealed class RecordingReader(string text) : TextReader
    {
        private int _position;

        public List<(int Requested, int Returned, char Last)> Calls { get; } = [];

        public override int Read(Span<char> buffer)
        {
            var n = Math.Min(buffer.Length, text.Length - _position);
            text.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            Calls.Add((buffer.Length, n, n > 0 ? buffer[n - 1] : '\0'));
            return n;
        }

        public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));

        public override int Peek() => _position < text.Length ? text[_position] : -1;

        public override int Read() => _position < text.Length ? text[_position++] : -1;
    }
}
