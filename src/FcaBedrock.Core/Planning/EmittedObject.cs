namespace FcaBedrock.Core.Planning;

/// <summary>
/// One emitted formal object: its name and the global formal-attribute ids it
/// crosses, in ascending order (§17 rule 8). The unit the emitter streams and the
/// writers serialize; the full incidence matrix is never materialized (P-15).
/// </summary>
public sealed record EmittedObject(string Name, IReadOnlyList<int> CrossedFormalAttributeIds);
