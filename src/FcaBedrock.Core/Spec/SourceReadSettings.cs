namespace FcaBedrock.Core.Spec;

/// <summary>
/// The §5.1 schema-independent read settings (the stage-1 half of the two-stage
/// source bootstrap, D-098/G-1): everything a source session needs to open and
/// tokenize a stream before any attribute index is resolved. Sealed and
/// non-positional so it cannot be subclassed or copy-constructed with a mutated
/// field; value equality over the settings drives session binding and descriptor
/// provenance validation.
/// <para>
/// <b>Failure contract.</b> The resolve seam diagnoses authored errors (missing
/// shape, unsupported quote, delimiter/quote conflict, unsupported encoding)
/// <em>before</em> calling <see cref="Create"/>; <see cref="Create"/> is the P-10
/// programmer-error backstop and throws exactly:
/// <list type="bullet">
/// <item><see cref="ArgumentNullException"/> for a null <c>encoding</c> or
/// <c>missingToken</c> (an empty <c>missingToken</c> is <em>valid</em> — §5.1
/// disables token-based missing detection; only null is rejected);</item>
/// <item><see cref="ArgumentException"/> for an inconsistent shape/ordering pair
/// (<c>ordering</c> must be non-null exactly when <c>shape == Triple</c>), a
/// <c>delimiter == quoteChar</c>, or an <c>encoding</c> outside the accepted UTF-8
/// spellings (which normalize to <c>"utf-8"</c>, mirroring the seam's
/// <c>ResolveEncoding</c>);</item>
/// <item><see cref="NotSupportedException"/> for a <c>quoteChar</c> other than the
/// standard double quote.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SourceReadSettings
{
    private SourceReadSettings(
        SourceShape shape, string encoding, char delimiter, char quoteChar,
        bool hasHeader, string missingToken, TripleOrdering? ordering)
    {
        Shape = shape;
        Encoding = encoding;
        Delimiter = delimiter;
        QuoteChar = quoteChar;
        HasHeader = hasHeader;
        MissingToken = missingToken;
        Ordering = ordering;
    }

    /// <summary>The resolved source shape (§5.1).</summary>
    public SourceShape Shape { get; }

    /// <summary>The canonical text encoding (§5.1); always <c>"utf-8"</c> in v1.</summary>
    public string Encoding { get; }

    /// <summary>The field delimiter (§5.1).</summary>
    public char Delimiter { get; }

    /// <summary>The quote character (§5.1); always the standard double quote in v1.</summary>
    public char QuoteChar { get; }

    /// <summary>Whether the first record is a header (§5.1).</summary>
    public bool HasHeader { get; }

    /// <summary>The token marking a missing value (§5.1); may be empty.</summary>
    public string MissingToken { get; }

    /// <summary>The triple row ordering (§5.3); non-null exactly for a triple shape.</summary>
    public TripleOrdering? Ordering { get; }

    /// <summary>
    /// Builds validated read settings (the P-10 backstop; see the type remarks for
    /// the exact exception contract). Recognized UTF-8 spellings normalize to
    /// <c>"utf-8"</c>.
    /// </summary>
    public static SourceReadSettings Create(
        SourceShape shape, string encoding, char delimiter, char quoteChar,
        bool hasHeader, string missingToken, TripleOrdering? ordering)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(missingToken);

        if (quoteChar != '"')
        {
            throw new NotSupportedException(
                $"SourceReadSettings supports only the '\"' quote character; got '{quoteChar}'.");
        }

        if (delimiter == quoteChar)
        {
            throw new ArgumentException(
                "delimiter must differ from quoteChar.", nameof(delimiter));
        }

        var normalizedEncoding = NormalizeEncoding(encoding);

        var orderingRequired = shape == SourceShape.Triple;
        if (orderingRequired != (ordering is not null))
        {
            throw new ArgumentException(
                "ordering must be non-null exactly when shape is Triple.", nameof(ordering));
        }

        return new SourceReadSettings(
            shape, normalizedEncoding, delimiter, quoteChar, hasHeader, missingToken, ordering);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SourceReadSettings other
        && Shape == other.Shape
        && string.Equals(Encoding, other.Encoding, StringComparison.Ordinal)
        && Delimiter == other.Delimiter
        && QuoteChar == other.QuoteChar
        && HasHeader == other.HasHeader
        && string.Equals(MissingToken, other.MissingToken, StringComparison.Ordinal)
        && Ordering == other.Ordering;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Shape);
        hash.Add(Encoding, StringComparer.Ordinal);
        hash.Add(Delimiter);
        hash.Add(QuoteChar);
        hash.Add(HasHeader);
        hash.Add(MissingToken, StringComparer.Ordinal);
        hash.Add(Ordering);
        return hash.ToHashCode();
    }

    // §5.1/D-082: only UTF-8 spellings are accepted, canonicalized to "utf-8" so
    // casing/spelling never perturbs value equality (or, downstream, the hash).
    private static string NormalizeEncoding(string encoding)
    {
        var normalized = encoding.Trim();
        if (string.Equals(normalized, "utf-8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "utf8", StringComparison.OrdinalIgnoreCase))
        {
            return "utf-8";
        }

        throw new ArgumentException(
            $"encoding '{encoding}' is not a supported UTF-8 spelling.", nameof(encoding));
    }
}
