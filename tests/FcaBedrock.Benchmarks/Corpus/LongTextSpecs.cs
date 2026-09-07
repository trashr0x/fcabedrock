using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The authored spec the long-text benchmarks convert under, and the frozen formal-attribute layout
/// its oracle indexes into.
/// <para>
/// Only <c>tag</c> and <c>blob</c> are analyzed. <c>note</c> is deliberately left unbound: it is
/// there to be <em>read</em> — every one of its rows still costs a cleaned string of hundreds of
/// characters — but binding a near-unique column as a nominal attribute would produce one formal
/// attribute per row, which is a different (and already covered) pathology. Leaving it unanalyzed is
/// what isolates text volume from attribute explosion.
/// </para>
/// </summary>
internal static class LongTextSpecs
{
    /// <summary>The declared long-text spec.</summary>
    public static string Declared { get; } = BuildDeclared();

    /// <summary>First id of <c>tag</c>: eight nominal value bins.</summary>
    public const int TagBase = 0;

    /// <summary>First id of <c>blob</c>: eight nominal value bins over the 512-character values.</summary>
    public const int BlobBase = TagBase + 8;

    /// <summary>The spec's total formal-attribute count.</summary>
    public const int FormalAttributeCount = BlobBase + 8;

    private static string BuildDeclared()
    {
        var spec = new StringBuilder(1 << 13);
        spec.Append(
            """
            # Long text: few distinct values, each large. Fully declared, so no calibration pass runs
            # and the emit measurement is emission over long strings alone.
            [spec]
            version = 1

            [binding]
            shape = "wide"
            has_header = true
            delimiter = ","

            [[attribute]]
            name = "tag"
            source = { kind = "column", index = 1 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(LongTextCorpus.TagDomain));
        spec.Append(
            """


            [[attribute]]
            name = "blob"
            source = { kind = "column", index = 2 }
            discretizer = { kind = "identity" }
            scale = { kind = "nominal" }
            declared_domain =
            """);
        spec.Append(' ').Append(TomlArray(LongTextCorpus.BlobDomain));
        spec.Append('\n');
        return spec.ToString();
    }

    // The domains are generated from a fixed lowercase alphanumeric alphabet, so no TOML escaping
    // is reachable; the assertion states that rather than trusting it.
    private static string TomlArray(IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            if (value.AsSpan().IndexOfAny('"', '\\') >= 0)
            {
                throw new InvalidOperationException("a long-text domain value would need TOML escaping.");
            }
        }

        return "[" + string.Join(", ", values.Select(value => "\"" + value + "\"")) + "]";
    }
}
