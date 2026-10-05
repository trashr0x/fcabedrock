using FcaBedrock.Core.Spec;

namespace FcaBedrock.Spec.Toml;

/// <summary>
/// The Spec-layer pairing wrapper (D-098): a resolved <see cref="ResolvedSpec"/>
/// token paired with the resolver's own deep snapshot of the document it was resolved
/// from. The snapshot is immutable except that each non-empty <c>value_labels</c> map is a
/// read-only wrapper whose <c>SyncRoot</c> is its copied, mutable backing map. Sealed with an
/// <b>internal</b> constructor: only <see cref="SpecResolver"/>
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

    /// <summary>The deep snapshot of the resolved document, immutable except for its <c>value_labels</c> backing maps.</summary>
    public SpecDocument Document { get; }

    /// <summary>The opaque resolution token.</summary>
    public ResolvedSpec Resolved { get; }
}
