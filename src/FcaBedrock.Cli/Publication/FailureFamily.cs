namespace FcaBedrock.Cli.Publication;

/// <summary>
/// Marks a failure as having come from the <b>output</b> side (CX-M7H-006).
/// <para>
/// An exporter writes a staged artifact while enumerating the data source, so one
/// <see cref="IOException"/> escaping that call could equally mean "the source could not be read"
/// or "the stage could not be written" — and the two get opposite messages, one naming the DATA
/// operand and one naming the output. Tagging the failure where it happens is what keeps a full
/// disk from being reported as a broken input file.
/// </para>
/// <para>It never escapes the CLI: publication converts it to a sanitized code-less host error.</para>
/// </summary>
internal sealed class PublicationStreamException : Exception
{
    /// <summary>Wraps <paramref name="inner"/>, the failure the output stream actually raised.</summary>
    public PublicationStreamException(Exception inner)
        : base("The publication output stream failed.", inner)
    {
    }
}

/// <summary>
/// Marks a failure as a <b>contract or state defect at a publication boundary</b> (CX-M7H-034/035).
/// <para>
/// An <see cref="ObjectDisposedException"/>, <see cref="ArgumentException"/>, or
/// <see cref="NotSupportedException"/> raised by an already-open publication stream — or by an
/// internal residue read — is a product bug, not an environment failure, and belongs on the
/// sanitized unexpected-fault exit. Two of those types would otherwise be indistinguishable from
/// something else: the host maps a bare <see cref="ObjectDisposedException"/> to "cannot write to
/// standard output", and preflight maps a bare <see cref="ArgumentException"/> to "the output
/// operand is not a usable path". Wrapping at the origin is what keeps both of those readings for
/// the cases they are actually about.
/// </para>
/// <para>It never escapes the CLI as itself: the host renders one fixed sanitized line.</para>
/// </summary>
internal sealed class PublicationFaultException : Exception
{
    /// <summary>Wraps <paramref name="inner"/>, the contract defect the boundary actually raised.</summary>
    public PublicationFaultException(Exception inner)
        : base("A publication boundary violated its contract.", inner)
    {
    }
}

/// <summary>
/// What kind of thing a failure at a publication boundary is — the one place that decides it.
/// <para>
/// Every predicate is a flat test over the exception's own type: none inspects an inner exception,
/// unwraps a tag, or reads any state. Deciding the kind is all this type does — <em>where</em> a
/// kind is admitted is a property of each catch site, not of the family.
/// </para>
/// <para>
/// <see cref="PublicationStreamException"/> and <see cref="PublicationFaultException"/> tag a
/// failure's <b>origin</b> at the moment it happens and carry the original as their inner
/// exception. Neither is itself in any family, so a failure that has already been tagged passes
/// every filter here untouched and keeps the reading its origin gave it.
/// </para>
/// </summary>
internal static class FailureFamily
{
    /// <summary>
    /// The broad family an unusable output <b>operand</b> can raise. Admitted at exactly one
    /// boundary — preflight's own path resolution — where an <see cref="ArgumentException"/> or
    /// <see cref="NotSupportedException"/> genuinely describes what the user typed. Everywhere
    /// else the same types are contract defects and must reach the unexpected-fault exit (P-14,
    /// CX-M7H-041).
    /// </summary>
    internal static bool IsPublicationFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ObjectDisposedException
            or ArgumentException;

    /// <summary>
    /// The failures that are genuinely the <b>environment's</b>: the disk filled, the handle was
    /// revoked, access was withdrawn, the file is not there.
    /// <para>
    /// Deliberately narrow (CX-M7H-015/021/041). It governs every publication-filesystem call the
    /// transaction makes — create, confidential create, flush, rename, delete, bounded read — and
    /// every already-open stream it owns: artifact stages, the transaction record, its evidence,
    /// and the phase markers. At each of those, an <see cref="ArgumentException"/> means an invalid
    /// range or a path this code composed wrongly, and an <see cref="ObjectDisposedException"/>
    /// means a closed stream or a disposed seam. Those are product bugs; disguising one as an
    /// environment failure would send the user to check disk space.
    /// </para>
    /// </summary>
    internal static bool IsEnvironmentFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    /// <summary>
    /// The contract and state defects a publication boundary can raise (CX-M7H-034/035/041): a
    /// write to a disposed stream, an invalid range, an unsupported operation. They are product
    /// bugs, so they are tagged at their origin and reach the sanitized unexpected-fault exit
    /// rather than being read as a full disk, an unusable output operand, or — for
    /// <see cref="ObjectDisposedException"/>, which the host otherwise attributes to its own
    /// writers — a failure of standard output.
    /// </summary>
    internal static bool IsContractFault(Exception exception) =>
        exception is ObjectDisposedException or ArgumentException or NotSupportedException;
}
