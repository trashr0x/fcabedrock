using System.Collections;
using System.Globalization;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Export.Tests;

// The `.cxt` size advisory (§8, D-122 part 7 / D-123). The projection is proven EXACT — not merely
// plausible — by the boundary method: for each case the advisory-free writer gives the real emitted
// byte count `actual`, then threshold `actual` must fire (projected >= actual) and threshold
// `actual + 1` must be silent (projected < actual + 1 ⇒ projected <= actual). Together those pin
// projected == actual, so any future drift between the projection and the writer fails a test.
public sealed class CxtSizeAdvisoryTests
{
    private static readonly EmittedObject[] AsciiObjects =
    [
        WriterFixtures.Object("0", 0),
        WriterFixtures.Object("1", 1),
        WriterFixtures.Object("2"),
    ];

    // Multi-byte object names (é = 2 bytes, Ω = 2 bytes) — the projection must count encoded bytes.
    private static readonly EmittedObject[] NonAsciiObjects =
    [
        WriterFixtures.Object("café", 0),
        WriterFixtures.Object("Ω", 1),
        WriterFixtures.Object("naïve"),
    ];

    // Crossless objects, for the zero-formal-attribute plan (each row is zero incidence characters).
    private static readonly EmittedObject[] NoCrossObjects =
    [
        WriterFixtures.Object("r0"),
        WriterFixtures.Object("r1"),
        WriterFixtures.Object("r2"),
    ];

    private static readonly EmittedObject[] NoObjects = [];

    [Fact]
    public Task Projection_WhenLfTrailingNewline_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.Native);

    [Fact]
    public Task Projection_WhenCrlfTrailingNewline_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.V2Compat);

    [Fact]
    public Task Projection_WhenLfNoTrailingNewline_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(
            WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.Native with { TrailingNewline = false });

    [Fact]
    public Task Projection_WhenCrlfNoTrailingNewline_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(
            WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.V2Compat with { TrailingNewline = false });

    [Fact]
    public Task Projection_WhenNonAsciiObjectNames_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(WriterFixtures.TwoColumnPlan(), NonAsciiObjects, WriterOptions.Native);

    [Fact]
    public Task Projection_WhenNonAsciiRenderedAttributeNames_ThenEqualsEmittedBytes() =>
        // Columns render as "a-café" / "a-Ω" — multi-byte rendered formal-attribute names.
        AssertProjectionIsExactAsync(WriterFixtures.NominalPlan("café", "Ω"), AsciiObjects, WriterOptions.Native);

    [Fact]
    public Task Projection_WhenCrlfNonAsciiNamesNoTrailingNewline_ThenEqualsEmittedBytes() =>
        // Combined stress: 2-byte line endings, multi-byte object AND rendered attribute names, and
        // no trailing newline.
        AssertProjectionIsExactAsync(
            WriterFixtures.NominalPlan("café", "Ω"), NonAsciiObjects, WriterOptions.V2Compat with { TrailingNewline = false });

    [Fact]
    public Task Projection_WhenZeroObjectsTrailingNewline_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(WriterFixtures.TwoColumnPlan(), NoObjects, WriterOptions.Native);

    [Fact]
    public Task Projection_WhenZeroObjectsNoTrailingNewline_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(
            WriterFixtures.TwoColumnPlan(), NoObjects, WriterOptions.Native with { TrailingNewline = false });

    [Fact]
    public Task Projection_WhenZeroFormalAttributes_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(WriterFixtures.ZeroAttributePlan(), NoCrossObjects, WriterOptions.Native);

    [Fact]
    public Task Projection_WhenZeroObjectsAndZeroFormalAttributes_ThenEqualsEmittedBytes() =>
        AssertProjectionIsExactAsync(WriterFixtures.ZeroAttributePlan(), NoObjects, WriterOptions.Native);

    [Fact]
    public async Task Advisory_WhenThresholdBelowProjection_ThenFires()
    {
        var diagnostics = new List<BedrockDiagnostic>();
        await WriterFixtures.WriteCxtBytesAsync(
            WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.Native, diagnostics, sizeAdvisoryBytes: 1);

        var advisory = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.OutputCxtSizeAdvisory, advisory.Code);
    }

    [Fact]
    public async Task Advisory_WhenThresholdZero_ThenSilentEvenForNonEmptyOutput()
    {
        var diagnostics = new List<BedrockDiagnostic>();
        var bytes = await WriterFixtures.WriteCxtBytesAsync(
            WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.Native, diagnostics, sizeAdvisoryBytes: 0);

        Assert.NotEmpty(bytes);       // the output itself is non-empty ...
        Assert.Empty(diagnostics);    // ... yet exactly-0 disables the advisory.
    }

    [Fact]
    public async Task Advisory_WhenThresholdNegative_ThenThrowsArgumentOutOfRange() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            WriterFixtures.WriteCxtBytesAsync(
                WriterFixtures.TwoColumnPlan(), AsciiObjects, WriterOptions.Native, new List<BedrockDiagnostic>(), sizeAdvisoryBytes: -1));

    [Fact]
    public async Task Advisory_WhenFired_ThenSingleWarningWithStableMessageAndNoLocationOrContext()
    {
        var plan = WriterFixtures.TwoColumnPlan();
        var options = WriterOptions.Native;
        var actual = (await WriterFixtures.WriteCxtBytesAsync(plan, AsciiObjects, options)).LongLength;

        var diagnostics = new List<BedrockDiagnostic>();
        await WriterFixtures.WriteCxtBytesAsync(plan, AsciiObjects, options, diagnostics, sizeAdvisoryBytes: 1);

        var advisory = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.OutputCxtSizeAdvisory, advisory.Code);
        Assert.Equal(DiagnosticSeverity.Warning, advisory.Severity);
        Assert.Null(advisory.Location);
        Assert.Null(advisory.Context);

        var expected = string.Format(
            CultureInfo.InvariantCulture,
            "The projected .cxt output size is {0} bytes, at or above the {1}-byte size advisory threshold.",
            actual, 1);
        Assert.Equal(expected, advisory.Message);
    }

    [Theory]
    [InlineData(false)] // native LF
    [InlineData(true)]  // v2-compat CRLF
    public async Task Bytes_WhenAdvisoryFiringSilentOrDisabled_ThenIdenticalToAdvisoryFreeOverload(bool v2Compat)
    {
        var plan = WriterFixtures.TwoColumnPlan();
        var options = v2Compat ? WriterOptions.V2Compat : WriterOptions.Native;

        // Ground truth: the four-argument (advisory-free) overload.
        var baseline = await WriterFixtures.WriteCxtBytesAsync(plan, AsciiObjects, options);

        // The advisory-carrying overload must emit the same bytes whether the advisory fires
        // (threshold 1), stays silent (an unreachable threshold), or is disabled (exactly 0).
        var firing = await WriterFixtures.WriteCxtBytesAsync(plan, AsciiObjects, options, new List<BedrockDiagnostic>(), sizeAdvisoryBytes: 1);
        var silent = await WriterFixtures.WriteCxtBytesAsync(plan, AsciiObjects, options, new List<BedrockDiagnostic>(), sizeAdvisoryBytes: long.MaxValue);
        var disabled = await WriterFixtures.WriteCxtBytesAsync(plan, AsciiObjects, options, new List<BedrockDiagnostic>(), sizeAdvisoryBytes: 0);

        Assert.Equal(baseline, firing);
        Assert.Equal(baseline, silent);
        Assert.Equal(baseline, disabled);
    }

    [Fact]
    public async Task Advisory_WhenRaised_ThenAppendedAfterPass1AndBeforeAnyOutputByte()
    {
        var plan = WriterFixtures.TwoColumnPlan();
        var source = new RecordingObjectSource(AsciiObjects);
        using var stream = new CountingStream();
        var diagnostics = new ObservingDiagnostics(source, stream);

        await CxtWriter.WriteAsync(plan, source.Open, WriterOptions.Native, stream, diagnostics, sizeAdvisoryBytes: 1);

        Assert.True(diagnostics.AdvisoryObserved);
        Assert.True(diagnostics.Pass1CompleteAtAdvisory);   // pass 1 finished before the advisory was appended
        Assert.Equal(0L, diagnostics.OutputBytesAtAdvisory); // and no output byte had been written yet
        var advisory = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.OutputCxtSizeAdvisory, advisory.Code);
    }

    // The boundary proof of exactness: the advisory-free writer's real byte count is `actual`;
    // threshold `actual` fires (projected >= actual) and `actual + 1` is silent (projected <= actual).
    private static async Task AssertProjectionIsExactAsync(
        ConversionPlan plan, IReadOnlyList<EmittedObject> objects, WriterOptions options)
    {
        var actual = (await WriterFixtures.WriteCxtBytesAsync(plan, objects, options)).LongLength;

        var atThreshold = new List<BedrockDiagnostic>();
        await WriterFixtures.WriteCxtBytesAsync(plan, objects, options, atThreshold, sizeAdvisoryBytes: actual);
        var advisory = Assert.Single(atThreshold);
        Assert.Equal(DiagnosticCode.OutputCxtSizeAdvisory, advisory.Code);

        var aboveThreshold = new List<BedrockDiagnostic>();
        await WriterFixtures.WriteCxtBytesAsync(plan, objects, options, aboveThreshold, sizeAdvisoryBytes: actual + 1);
        Assert.Empty(aboveThreshold);
    }

    // A replayable object source that records when an enumeration runs to completion. The writer
    // drains pass 1 fully before computing the advisory and before pass 2 begins, so at the
    // advisory-emission instant this reads true exactly because pass 1 completed.
    private sealed class RecordingObjectSource(IReadOnlyList<EmittedObject> objects)
    {
        public bool Pass1Completed { get; private set; }

        public IAsyncEnumerable<EmittedObject> Open() => Enumerate();

        private async IAsyncEnumerable<EmittedObject> Enumerate()
        {
            foreach (var obj in objects)
            {
                yield return obj;
            }

            Pass1Completed = true;
            await Task.CompletedTask;
        }
    }

    // Counts bytes actually written to the sink, across every Stream write entry point StreamWriter
    // might use, so the timing test can observe "zero output bytes" at the advisory instant.
    private sealed class CountingStream : MemoryStream
    {
        public long BytesWritten { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BytesWritten += count;
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BytesWritten += buffer.Length;
            base.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BytesWritten += count;
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    // A diagnostics sink that snapshots the pass-1 completion flag and the sink byte count at the
    // exact instant the advisory is appended — a deterministic ordering seam, not a code-order or
    // test-name assumption.
    private sealed class ObservingDiagnostics(RecordingObjectSource source, CountingStream stream)
        : ICollection<BedrockDiagnostic>
    {
        private readonly List<BedrockDiagnostic> _items = [];

        public bool AdvisoryObserved { get; private set; }
        public bool Pass1CompleteAtAdvisory { get; private set; }
        public long OutputBytesAtAdvisory { get; private set; }

        public void Add(BedrockDiagnostic item)
        {
            if (item.Code == DiagnosticCode.OutputCxtSizeAdvisory)
            {
                AdvisoryObserved = true;
                Pass1CompleteAtAdvisory = source.Pass1Completed;
                OutputBytesAtAdvisory = stream.BytesWritten;
            }

            _items.Add(item);
        }

        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Clear() => _items.Clear();
        public bool Contains(BedrockDiagnostic item) => _items.Contains(item);
        public void CopyTo(BedrockDiagnostic[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public bool Remove(BedrockDiagnostic item) => _items.Remove(item);
        public IEnumerator<BedrockDiagnostic> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
