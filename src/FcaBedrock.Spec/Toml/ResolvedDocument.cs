using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The Spec-layer pairing wrapper (D-098/G-1): a resolved <see cref="ResolvedSpec"/>
/// token paired with an immutable deep snapshot of the document it was resolved
/// from. Sealed with an <b>internal</b> constructor — only <see cref="SpecResolver"/>
/// (and Spec.Tests via the existing IVT) can mint one, so an unrelated document can
/// never be paired with a resolution, and post-resolve mutation of the caller's
/// document cannot reach fingerprinting (<c>SpecFingerprints.ComputeNative</c>
/// reads output settings from <see cref="Document"/>).
/// </summary>
public sealed class ResolvedDocument
{
    internal ResolvedDocument(SpecDocument documentSnapshot, ResolvedSpec resolved)
    {
        Document = documentSnapshot;
        Resolved = resolved;
    }

    /// <summary>The immutable deep snapshot of the resolved document.</summary>
    public SpecDocument Document { get; }

    /// <summary>The opaque resolution token.</summary>
    public ResolvedSpec Resolved { get; }
}
