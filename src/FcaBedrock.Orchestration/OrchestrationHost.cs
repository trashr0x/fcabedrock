using FcaBedrock.Orchestration.Publication;

namespace FcaBedrock.Orchestration;

/// <summary>
/// Everything a host lends Orchestration. Every file an operation reads (the root spec, each
/// <c>extends</c> base, every data pass and the validate schema read) opens through
/// <see cref="OpenInput"/>, which is also where a host may observe opens and closes.
/// <para>
/// Immutable, so concurrent operations may share one host; <see cref="OpenInput"/> must then be
/// safe to call concurrently, which is the host's responsibility.
/// </para>
/// </summary>
public sealed class OrchestrationHost
{
    /// <summary>Creates a host that opens every input through <paramref name="openInput"/>.</summary>
    public OrchestrationHost(Func<string, Stream> openInput)
    {
        ArgumentNullException.ThrowIfNull(openInput);
        OpenInput = openInput;
    }

    /// <summary>Opens a named input for reading.</summary>
    public Func<string, Stream> OpenInput { get; }

    /// <summary>
    /// Every filesystem operation publication performs. Production is the real filesystem; tests
    /// substitute one that fails a chosen boundary through the friend grant, which is a test seam,
    /// not a production option (EP-6).
    /// </summary>
    internal IPublicationFileSystem PublicationFiles { get; init; } = PublicationFileSystem.Instance;

    /// <summary>
    /// Creates the identity service that loads a spec chain and that relocation checks against, so
    /// a check compares keys from the same kind of probe. Production is
    /// <see cref="FileIdentity.CreateDefault"/>; tests inject a probe that reports OS identity
    /// unavailable. Validation and publication keep their own identity services (D-125, D-140).
    /// </summary>
    internal Func<FileIdentity> SpecIdentities { get; init; } = FileIdentity.CreateDefault;
}
