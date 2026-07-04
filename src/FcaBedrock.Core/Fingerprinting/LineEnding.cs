namespace FcaBedrock.Core.Fingerprinting;

/// <summary>
/// Line-ending convention as a fingerprint input (spec §8/§14). Core-owned:
/// Export's <c>WriterOptions</c> is not referenceable from Core (the package
/// dependency rule), and the canonical encoding hashes the <c>"lf"</c>/<c>"crlf"</c>
/// tokens, never raw control characters (D-077). Callers map their writer
/// settings to this at the edge.
/// </summary>
public enum LineEnding
{
    /// <summary>Unix <c>\n</c> — hashes as <c>"lf"</c>.</summary>
    Lf,

    /// <summary>Windows <c>\r\n</c> — hashes as <c>"crlf"</c>.</summary>
    Crlf,
}
