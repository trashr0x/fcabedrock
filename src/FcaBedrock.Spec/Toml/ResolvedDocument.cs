using System.Collections.Immutable;
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
    internal ResolvedDocument(SpecDocument documentSnapshot, ResolvedSpec resolved, ImmutableArray<AuthoredDomain> authoredDomains)
    {
        // SpecFreezer pairs each captured domain with its document attribute by position, so the
        // two lists must describe the same attributes in the same order.
        if (authoredDomains.Length != documentSnapshot.Attributes.Count)
        {
            throw new ArgumentException(
                $"{authoredDomains.Length} authored domains were captured for {documentSnapshot.Attributes.Count} attributes.",
                nameof(authoredDomains));
        }

        for (var i = 0; i < authoredDomains.Length; i++)
        {
            if (!string.Equals(authoredDomains[i].AttributeName, documentSnapshot.Attributes[i].Name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The authored domain at position {i} belongs to '{authoredDomains[i].AttributeName}', not to attribute " +
                    $"'{documentSnapshot.Attributes[i].Name}'.",
                    nameof(authoredDomains));
            }
        }

        Document = documentSnapshot;
        Resolved = resolved;
        AuthoredDomains = authoredDomains;
    }

    /// <summary>The deep snapshot of the resolved document, immutable except for its <c>value_labels</c> backing maps.</summary>
    public SpecDocument Document { get; }

    /// <summary>The opaque resolution token.</summary>
    public ResolvedSpec Resolved { get; }

    /// <summary>
    /// Each attribute's winning <c>declared_domain</c> as authored, in <see cref="Document"/>
    /// attribute order. Internal: only <see cref="SpecFreezer"/> reads it, to write an authored
    /// prefix back exactly (spec §7).
    /// </summary>
    internal ImmutableArray<AuthoredDomain> AuthoredDomains { get; }
}
