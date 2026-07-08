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

        // Pass 2: incidence rows — replay the source rather than buffering the matrix.
        var first = true;
        await foreach (var obj in openObjects().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!first)
            {
                writer.Write(lineEnding);
            }

            first = false;
            WriteIncidenceRow(writer, obj, attributeCount);
        }

        if (!first && options.TrailingNewline)
        {
            writer.Write(lineEnding);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

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
