using System.Globalization;
using System.Text;
using FcaBedrock.Core.Planning;

namespace FcaBedrock.Export;

/// <summary>
/// Writes the FIMI <c>.dat</c> format (spec §18.2): one line per object listing
/// the <see cref="WriterOptions.BaseIndex"/>-based ids of the formal attributes it
/// crosses. Single-pass and dumb — it serializes the emitted objects in order and
/// makes no semantic decisions (P-15).
/// </summary>
public static class DatWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Streams the <c>.dat</c> output for <paramref name="objects"/> to <paramref name="output"/>.</summary>
    public static async Task WriteAsync(
        IAsyncEnumerable<EmittedObject> objects,
        WriterOptions options,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        using var writer = new StreamWriter(output, Utf8NoBom, bufferSize: 1024, leaveOpen: true);

        var first = true;
        await foreach (var obj in objects.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!first)
            {
                writer.Write(options.LineEnding);
            }

            first = false;
            WriteLine(writer, obj, options);
        }

        if (!first && options.TrailingNewline)
        {
            writer.Write(options.LineEnding);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteLine(TextWriter writer, EmittedObject obj, WriterOptions options)
    {
        var ids = obj.CrossedFormalAttributeIds;
        if (ids.Count == 0)
        {
            if (options.EmptyLineTrailingSpace)
            {
                writer.Write(' ');
            }

            return;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(' ');
            }

            writer.Write((ids[i] + options.BaseIndex).ToString(CultureInfo.InvariantCulture));
        }

        if (options.NonemptyLineTrailingSpace)
        {
            writer.Write(' ');
        }
    }
}
