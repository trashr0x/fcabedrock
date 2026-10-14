using System.Collections.Immutable;
using FcaBedrock.Core.Spec;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Spec.Tests.Toml;

// ResolvedDocument pairs each captured authored domain with its document attribute by position
// (spec §7); SpecFreezer relies on that pairing, so construction refuses any other shape.
public sealed class ResolvedDocumentTests
{
    [Fact]
    public void Constructor_WhenTheAuthoredDomainCountDiffers_ThenItThrows()
    {
        var resolved = Resolved();

        Assert.Throws<ArgumentException>(() => new ResolvedDocument(resolved.Document, resolved.Resolved, []));
    }

    [Fact]
    public void Constructor_WhenAnAuthoredDomainNamesAnotherAttribute_ThenItThrows()
    {
        var resolved = Resolved();
        ImmutableArray<AuthoredDomain> swapped = [resolved.AuthoredDomains[1], resolved.AuthoredDomains[0]];

        Assert.Throws<ArgumentException>(() => new ResolvedDocument(resolved.Document, resolved.Resolved, swapped));
    }

    private static ResolvedDocument Resolved()
    {
        var document = DocumentFixtures.Document(
            [DocumentFixtures.Nominal("first", 0, ["a"]), DocumentFixtures.Nominal("second", 1, ["b"])]);
        var resolved = SpecResolver.Resolve(document, new SourceSchema(2));
        Assert.True(resolved.TryGetValue(out var doc));
        return doc!;
    }
}
