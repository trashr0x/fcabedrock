using FcaBedrock.Diagnostics;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The <c>extends</c> a root written to another place gets, by the existing rule (D-122 part 10,
/// §13). A closed family: the only constructor is <see langword="private protected"/>, so a host's
/// <c>switch</c> over the members Orchestration declares is complete.
/// </summary>
public abstract class RelocationDecision
{
    private protected RelocationDecision(string reference, string authored, string destination)
    {
        Reference = reference;
        Authored = authored;
        Destination = destination;
    }

    /// <summary>The <c>extends</c> text to write.</summary>
    public string Reference { get; }

    /// <summary>The root's <c>extends</c>, as authored.</summary>
    public string Authored { get; }

    /// <summary>The destination spelling the decision was made for.</summary>
    public string Destination { get; }
}

/// <summary>
/// From the destination's folder the reference reaches the loaded base's resolution path, and a
/// fresh identity service found the loaded object there. It claims that path and that object, not
/// that the chain composes: relocation proves a path and an object, never bytes (D-139 part 7).
/// </summary>
public sealed class VerifiedRelocation : RelocationDecision
{
    internal VerifiedRelocation(string reference, string authored, string destination, SpecChainFile immediateBase)
        : base(reference, authored, destination)
    {
        ImmediateBase = immediateBase;
    }

    /// <summary>The loaded base the reference was verified against.</summary>
    public SpecChainFile ImmediateBase { get; }
}

/// <summary>
/// Decides and rechecks a relocated <c>extends</c> through the host's identity seam. Relocation
/// produces no diagnostics: it returns no diagnostic list.
/// </summary>
public static class SpecRelocation
{
    /// <summary>
    /// Keeps the authored reference when it verifies; otherwise rebuilds a relative reference and
    /// verifies it the same way; refuses a destination on another drive or share
    /// (<see cref="HostErrorKind.RelocationOtherRoot"/>) and any reference that does not verify
    /// (<see cref="HostErrorKind.RelocationUnverified"/>). The decision depends only on the
    /// destination's folder. Reads no spec and holds nothing open.
    /// </summary>
    /// <param name="authored">The root's <c>extends</c>, as authored.</param>
    /// <param name="destination">The destination, as the host spelled it.</param>
    /// <param name="immediateBase">The chain file that reference loaded: its resolution path and load-time identity.</param>
    /// <param name="host">The host whose identity seam loaded the chain.</param>
    public static Result<VerifiedRelocation, HostError> Relocate(
        string authored, string destination, SpecChainFile immediateBase, OrchestrationHost host)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(immediateBase);
        ArgumentNullException.ThrowIfNull(host);

        return ExtendsRelocation.Relocate(authored, destination, immediateBase, host.SpecIdentities) switch
        {
            RelocatedReference(var reference) => Result<VerifiedRelocation, HostError>.Ok(
                new VerifiedRelocation(reference, authored, destination, immediateBase)),
            RelocationRefused refused => Result<VerifiedRelocation, HostError>.Err(
                Refusal(refused.Kind, refused.Message, destination, authored)),
            _ => throw new InvalidOperationException("Unrecognized relocation result."),
        };
    }

    /// <summary>
    /// Checks a decision again before the first write. A verified decision is probed afresh with a
    /// new identity service: completed with the same instance while it holds, otherwise the
    /// <see cref="HostErrorKind.RelocationUnverified"/> refusal.
    /// </summary>
    public static Result<RelocationDecision, HostError> Recheck(RelocationDecision decision, OrchestrationHost host)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(host);

        if (decision is not VerifiedRelocation verified)
        {
            throw new ArgumentException("Unrecognized relocation decision.", nameof(decision));
        }

        return ExtendsRelocation.Recheck(
                verified.Reference, verified.Authored, verified.Destination, verified.ImmediateBase, host.SpecIdentities)
            is { } message
            ? Result<RelocationDecision, HostError>.Err(
                Refusal(HostErrorKind.RelocationUnverified, message, verified.Destination, verified.Authored))
            : Result<RelocationDecision, HostError>.Ok(decision);
    }

    private static HostError Refusal(HostErrorKind kind, string message, string destination, string authored) =>
        new(kind, message, subject: destination, counterpart: authored);
}
