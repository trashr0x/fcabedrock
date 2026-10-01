using nietras.SeparatedValues;
using static FcaBedrock.Sources.Tests.SourceTestSupport;

namespace FcaBedrock.Sources.Tests;

/// <summary>
/// A characterization of the pinned Sep 0.15.0 reader, read directly with no Sources code between
/// it and a reader that returns short reads. It records the defect the whole-span reader exists for
/// (D-137): after a short read that ends in a carriage return, Sep holds back a following carriage
/// return and restores it after the characters read in between. Review this test at every Sep
/// version change, including a change after which it stops reproducing: that is the evidence the
/// removal of the whole-span reader needs, together with <see cref="ShortReadTests"/> staying green
/// without it.
/// </summary>
public sealed class PinnedSepShortReadTests
{
    [Fact]
    public void SepReader_WhenAShortReadEndsInCarriageReturnBeforeAnother_ThenTheCarriageReturnIsRestoredOutOfOrder()
    {
        // The input is one record whose quoted value holds CR CR; three-character reads end the first
        // read after the first CR.
        var reader = new ChunkedTextReader("\"a\r\rb\",c\n", 3);
        var records = new List<string>();
        using (var sep = Sep.New(',')
            .Reader(o => o with { HasHeader = false, Unescape = false, Trim = SepTrim.None, DisableColCountCheck = true })
            .From(reader))
        {
            while (sep.MoveNext())
            {
                var row = sep.Current;
                var fields = new string[row.ColCount];
                for (var i = 0; i < fields.Length; i++)
                {
                    fields[i] = new string(row[i].Span);
                }

                records.Add(Render(fields));
            }
        }

        // The generator's sequence is one record ["\"a\r\rb\"", "c"]; Sep returns the quoted value with
        // one CR and a phantom empty record built from the held-back CR.
        Assert.Equal([Render(["\"a\rb\"", "c"]), Render([string.Empty])], records);
    }

    /// <summary>A reader over a string whose span reads return at most <paramref name="chunk"/> characters.</summary>
    internal sealed class ChunkedTextReader(string text, int chunk) : TextReader
    {
        private int _position;

        public override int Read(Span<char> buffer)
        {
            var n = Math.Min(Math.Min(buffer.Length, chunk), text.Length - _position);
            text.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));

        public override int Peek() => _position < text.Length ? text[_position] : -1;

        public override int Read() => _position < text.Length ? text[_position++] : -1;
    }
}
