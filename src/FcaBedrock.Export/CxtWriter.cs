using System.Globalization;
using System.Text;
using FcaBedrock.Core.Planning;
using FcaBedrock.Diagnostics;

namespace FcaBedrock.Export;

/// <summary>
/// Writes the Burmeister <c>.cxt</c> format (spec §18.1): the <c>B</c> header, the
/// object and formal-attribute counts and names, then the incidence matrix. The
/// layout needs the object names before any row, so the writer makes two passes
/// over the replayable object stream, buffering only the (bounded) object names —
/// never the matrix (P-16). Dumb: it emits the planner's order and names verbatim (P-15).
/// <para>
/// <b>Object-name sequence invariant (§18.1, D-082).</b> Pass 2 must replay the same object-name
/// sequence (count and order) as pass 1; each pass-2 object's <c>Name</c> is checked against the pass-1
/// name at its position, and a mismatch, overflow, or shortfall throws
/// <see cref="InvalidOperationException"/> — a divergent replay cannot silently misalign names and rows.
/// On any throw the output is partial and the caller must discard it (atomic publication is M7).
/// </para>
/// <para>
/// <b>Size advisory (§8, D-122 part 7 / D-123).</b> The advisory-carrying overload projects the
/// <em>exact</em> final serialized size after the pass-1 name collection and <em>before any output
/// byte</em>, emitting <see cref="DiagnosticCode.OutputCxtSizeAdvisory"/> when it is at or above the
/// supplied threshold. The projection is byte-level bookkeeping only (P-15): it changes a warning,
/// never output bytes, and is a non-input to every fingerprint (D-077).
/// </para>
/// </summary>
public static class CxtWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the <c>.cxt</c> output for <paramref name="plan"/> over a replayable object stream.</summary>
    public static Task WriteAsync(
        ConversionPlan plan,
        Func<IAsyncEnumerable<EmittedObject>> openObjects,
        WriterOptions options,
        Stream output,
        CancellationToken cancellationToken = default) =>
        // The advisory-free public overload: existing callers stay source-compatible and
        // byte-identical. It delegates with the advisory disabled (sizeAdvisoryBytes: 0), so the
        // projection is never computed and the throwaway sink is never written to (D-123).
        WriteAsync(plan, openObjects, options, output, new List<BedrockDiagnostic>(), sizeAdvisoryBytes: 0, cancellationToken);

    /// <summary>
    /// Writes the <c>.cxt</c> output for <paramref name="plan"/>, additionally appending
    /// <see cref="DiagnosticCode.OutputCxtSizeAdvisory"/> (Warning) to <paramref name="diagnostics"/>
    /// when the exact projected output size is <b>at or above</b> <paramref name="sizeAdvisoryBytes"/>
    /// (§8, D-122 part 7). A threshold of exactly <c>0</c> disables the advisory; a negative threshold
    /// is rejected. The projection is computed after the object-name pass but <b>before any output
    /// byte</b>, and the advisory changes diagnostics only — never output bytes, never a fingerprint
    /// (D-077). The write path is otherwise identical to the advisory-free overload.
    /// </summary>
    public static async Task WriteAsync(
        ConversionPlan plan,
        Func<IAsyncEnumerable<EmittedObject>> openObjects,
        WriterOptions options,
        Stream output,
        ICollection<BedrockDiagnostic> diagnostics,
        long sizeAdvisoryBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(openObjects);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeAdvisoryBytes);

        // Pass 1: object names + count (bounded metadata only — §18.1, P-16).
        var objectNames = new List<string>();
        await foreach (var obj in openObjects().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            objectNames.Add(obj.Name);
        }

        var attributeCount = plan.FormalAttributes.Count;
        var lineEnding = options.LineEnding;

        // Size advisory (§8, D-122 part 7): projected from the pass-1 names and the planned formal
        // attributes and appended BEFORE any output byte is written. Only a positive threshold can
        // fire — exactly 0 disables, and a negative threshold already threw above — and the warning
        // fires when the exact serialized size is at or above it. A warning only: it never changes
        // output bytes or any fingerprint (D-077).
        if (sizeAdvisoryBytes > 0)
        {
            var projectedBytes = ProjectSerializedLength(
                objectNames, plan.FormalAttributes, lineEnding, options.TrailingNewline);
            if (projectedBytes >= sizeAdvisoryBytes)
            {
                diagnostics.Add(SizeAdvisory(projectedBytes, sizeAdvisoryBytes));
            }
        }

        using var writer = new StreamWriter(output, Utf8NoBom, bufferSize: 1024, leaveOpen: true);

        writer.Write('B');
        writer.Write(lineEnding);
        writer.Write(lineEnding);
        writer.Write(objectNames.Count.ToString(CultureInfo.InvariantCulture));
        writer.Write(lineEnding);
        writer.Write(attributeCount.ToString(CultureInfo.InvariantCulture));
        writer.Write(lineEnding);
        writer.Write(lineEnding);

        foreach (var name in objectNames)
        {
            writer.Write(name);
            writer.Write(lineEnding);
        }

        foreach (var formalAttribute in plan.FormalAttributes)
        {
            writer.Write(formalAttribute.RenderedName);
            writer.Write(lineEnding);
        }

        // Pass 2: incidence rows — replay the source rather than buffering the matrix. Each replayed
        // object's Name must match pass 1's at the same position (§18.1, D-082): a replay that yields a
        // different object-name sequence (count or order) would misalign the header names and the rows,
        // so it fails the write. (Full producer/content determinism is a P-7 concern, not the writer's.)
        var first = true;
        var objectIndex = 0;
        await foreach (var obj in openObjects().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (objectIndex >= objectNames.Count)
            {
                throw NameSequenceMismatch(objectIndex, expected: null, actual: obj.Name);
            }

            if (!string.Equals(obj.Name, objectNames[objectIndex], StringComparison.Ordinal))
            {
                throw NameSequenceMismatch(objectIndex, objectNames[objectIndex], obj.Name);
            }

            if (!first)
            {
                writer.Write(lineEnding);
            }

            first = false;
            WriteIncidenceRow(writer, obj, attributeCount);
            objectIndex++;
        }

        if (objectIndex != objectNames.Count)
        {
            throw NameSequenceMismatch(objectIndex, objectNames[objectIndex], actual: null);
        }

        if (!first && options.TrailingNewline)
        {
            writer.Write(lineEnding);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // The exact final serialized `.cxt` length in UTF-8 bytes under `options`, mirroring the writes
    // above piece-for-piece (§8): the 'B' header byte, the five header line endings, the two
    // invariant-culture count lines, every object name, every rendered formal-attribute name, the
    // objects×attributes single-byte incidence characters, and the trailing-newline rule. It counts
    // ENCODED bytes (a non-ASCII name or a CRLF ending counts at its real width), needs no pass-2
    // information (so it precedes the first output byte — D-122 part 7), and runs under `checked`
    // 64-bit arithmetic so an objects×attributes product cannot silently overflow. The
    // projection == emitted-bytes equality is a tested property (Export.Tests), which permanently
    // couples this to the writer above.
    private static long ProjectSerializedLength(
        IReadOnlyList<string> objectNames,
        IReadOnlyList<FormalAttribute> formalAttributes,
        string lineEnding,
        bool trailingNewline)
    {
        checked
        {
            long lineEndingWidth = Utf8NoBom.GetByteCount(lineEnding);
            long objectCount = objectNames.Count;
            long attributeCount = formalAttributes.Count;

            // Header: 'B' + LE + LE + <objects> + LE + <attributes> + LE + LE (five line endings).
            long total = 1
                + (5 * lineEndingWidth)
                + Utf8NoBom.GetByteCount(objectCount.ToString(CultureInfo.InvariantCulture))
                + Utf8NoBom.GetByteCount(attributeCount.ToString(CultureInfo.InvariantCulture));

            // Object-name lines, then formal-attribute-name lines: each name plus one line ending.
            foreach (var name in objectNames)
            {
                total += Utf8NoBom.GetByteCount(name);
            }

            total += objectCount * lineEndingWidth;

            foreach (var formalAttribute in formalAttributes)
            {
                total += Utf8NoBom.GetByteCount(formalAttribute.RenderedName);
            }

            total += attributeCount * lineEndingWidth;

            // Incidence rows: `attributeCount` single-byte ('.'/'X') characters per object, rows
            // separated by one line ending, and a trailing line ending only when there is at least
            // one row and TrailingNewline is set. A zero-object context writes no rows and no
            // trailing newline (matches the writer's `first`-guarded emission).
            if (objectCount > 0)
            {
                total += objectCount * attributeCount;
                total += (objectCount - 1) * lineEndingWidth;

                if (trailingNewline)
                {
                    total += lineEndingWidth;
                }
            }

            return total;
        }
    }

    // The size-advisory diagnostic (§8 / §16.4, D-122 part 7 / D-123): Warning, export-phase, no
    // location (the writer has no file/attribute/record context) and no structured context — the
    // message carries both the projected size and the threshold, formatted in the invariant culture
    // so the bytes are deterministic (P-11/P-12).
    private static BedrockDiagnostic SizeAdvisory(long projectedBytes, long thresholdBytes) =>
        new(
            DiagnosticCode.OutputCxtSizeAdvisory,
            DiagnosticSeverity.Warning,
            string.Format(
                CultureInfo.InvariantCulture,
                "The projected .cxt output size is {0} bytes, at or above the {1}-byte size advisory threshold.",
                projectedBytes,
                thresholdBytes));

    // The object-name sequence invariant failed: pass 2's replay diverged from pass 1's names. A
    // structural error — the caller discards the partial output (atomic publication is M7).
    private static InvalidOperationException NameSequenceMismatch(int position, string? expected, string? actual) =>
        new($"The .cxt replay produced a different object-name sequence at position {position}: pass 1 had {Describe(expected)}, pass 2 had {Describe(actual)}. The object stream must replay identically (§18.1).");

    private static string Describe(string? name) => name is null ? "no object" : $"'{name}'";

    private static void WriteIncidenceRow(TextWriter writer, EmittedObject obj, int attributeCount)
    {
        var row = new char[attributeCount];
        Array.Fill(row, '.');
        foreach (var id in obj.CrossedFormalAttributeIds)
        {
            row[id] = 'X';
        }

        writer.Write(row);
    }
}
