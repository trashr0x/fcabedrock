using System.Globalization;
using System.Text;
using FcaBedrock.Core.Planning;

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
/// </summary>
public static class CxtWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the <c>.cxt</c> output for <paramref name="plan"/> over a replayable object stream.</summary>
    public static async Task WriteAsync(
        ConversionPlan plan,
        Func<IAsyncEnumerable<EmittedObject>> openObjects,
        WriterOptions options,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(openObjects);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        // Pass 1: object names + count (bounded metadata only — §18.1, P-16).
        var objectNames = new List<string>();
        await foreach (var obj in openObjects().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            objectNames.Add(obj.Name);
        }

        var attributeCount = plan.FormalAttributes.Count;
        var lineEnding = options.LineEnding;

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
