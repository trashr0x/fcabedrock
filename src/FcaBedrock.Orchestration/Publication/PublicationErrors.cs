namespace FcaBedrock.Orchestration.Publication;

/// <summary>
/// A publication refusal or failure as the step that met it classified it: the condition, the
/// unchanged message its builder wrote, and the structured facts the builder received (D-140).
/// </summary>
internal sealed record PublicationError(
    HostErrorKind Kind,
    string Message,
    string? Subject = null,
    string? Counterpart = null,
    PublicationCause? Cause = null,
    CleanupFact? Blocker = null)
{
    /// <summary>The public form a host reads.</summary>
    public HostError ToHostError() => new(Kind, Message, Subject, Counterpart, Cause, Blocker);
}

/// <summary>
/// One factory per <see cref="PublicationMessages"/> error builder. Each takes exactly the
/// builder's arguments, produces the message from the unchanged builder, and records the condition
/// that builder names. A kind names its builder, not its site: the <c>RecordFailed</c> text says
/// "cannot start publication" even where a seal's marker could not be created, so its kind is
/// <see cref="HostErrorKind.PublicationControlFailed"/>, which claims only what the text claims.
/// A cause is the one the failed step established; an unknown cause stays unknown or absent.
/// </summary>
internal static class PublicationErrors
{
    /// <summary><see cref="PublicationMessages.ExistingTarget"/>.</summary>
    public static PublicationError ExistingTarget(string spelling) =>
        new(HostErrorKind.TargetOccupied, PublicationMessages.ExistingTarget(spelling), spelling);

    /// <summary><see cref="PublicationMessages.StaleManifest"/>.</summary>
    public static PublicationError StaleManifest(string spelling) =>
        new(HostErrorKind.StaleManifest, PublicationMessages.StaleManifest(spelling), spelling);

    /// <summary><see cref="PublicationMessages.TargetDirectory"/>.</summary>
    public static PublicationError TargetDirectory(string spelling) =>
        new(HostErrorKind.TargetIsDirectory, PublicationMessages.TargetDirectory(spelling), spelling);

    /// <summary><see cref="PublicationMessages.ReadOnlyTarget"/>: the held object's read-only attribute is the cause.</summary>
    public static PublicationError ReadOnlyTarget(string spelling) =>
        new(HostErrorKind.TargetReadOnly, PublicationMessages.ReadOnlyTarget(spelling), spelling, Cause: PublicationCause.ReadOnly);

    /// <summary><see cref="PublicationMessages.ReadOnlyManifest"/>: the held object's read-only attribute is the cause.</summary>
    public static PublicationError ReadOnlyManifest(string spelling) =>
        new(HostErrorKind.ManifestReadOnly, PublicationMessages.ReadOnlyManifest(spelling), spelling, Cause: PublicationCause.ReadOnly);

    /// <summary><see cref="PublicationMessages.InputCollision"/>.</summary>
    public static PublicationError InputCollision(string output, string input) =>
        new(HostErrorKind.InputCollision, PublicationMessages.InputCollision(output, input), output, input);

    /// <summary><see cref="PublicationMessages.OutputCollision"/>.</summary>
    public static PublicationError OutputCollision(string first, string second) =>
        new(HostErrorKind.OutputCollision, PublicationMessages.OutputCollision(first, second), first, second);

    /// <summary><see cref="PublicationMessages.UnknownResidue"/>.</summary>
    public static PublicationError UnknownResidue(string baseSpelling) =>
        new(HostErrorKind.UnknownResidue, PublicationMessages.UnknownResidue(baseSpelling), baseSpelling);

    /// <summary><see cref="PublicationMessages.RecoveryFailed"/>, with its first unfinished step where the recovery recorded one.</summary>
    public static PublicationError RecoveryFailed(string baseSpelling, CleanupFact? blocker = null) =>
        new(HostErrorKind.RecoveryFailed, PublicationMessages.RecoveryFailed(baseSpelling, blocker), baseSpelling, Blocker: blocker);

    /// <summary>
    /// <see cref="PublicationMessages.RecordFailed"/>. A null <paramref name="cause"/> means the step
    /// established none, and the generic text is written.
    /// </summary>
    public static PublicationError RecordFailed(string baseSpelling, PublicationCause? cause = null) =>
        new(
            HostErrorKind.PublicationControlFailed,
            PublicationMessages.RecordFailed(baseSpelling, cause ?? PublicationCause.Unknown),
            baseSpelling,
            Cause: cause);

    /// <summary><see cref="PublicationMessages.StageFailed"/>.</summary>
    public static PublicationError StageFailed(string spelling) =>
        new(HostErrorKind.StageFailed, PublicationMessages.StageFailed(spelling), spelling);

    /// <summary>
    /// <see cref="PublicationMessages.CommitFailed"/>. A null <paramref name="cause"/> means the step
    /// established none, and the generic text is written.
    /// </summary>
    public static PublicationError CommitFailed(string spelling, PublicationCause? cause = null) =>
        new(
            HostErrorKind.CommitFailed,
            PublicationMessages.CommitFailed(spelling, cause ?? PublicationCause.Unknown),
            spelling,
            Cause: cause);
}
