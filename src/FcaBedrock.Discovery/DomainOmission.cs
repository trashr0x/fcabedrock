namespace FcaBedrock.Discovery;

/// <summary>
/// The categories a complete domain's draft leaves out because they contain CR or LF, and the
/// categories it keeps (§7.1).
/// <para>
/// Built from the retained list, which for a complete domain is every distinct value observed,
/// so the count is exact. It counts distinct categories of this one attribute: a category
/// repeated in the data counts once, and the same text in two attributes counts once in each.
/// The kept categories stay in first-observation order, and the sample holds the first omitted
/// ones seen, compared by their full text.
/// </para>
/// </summary>
internal sealed class DomainOmission
{
    private DomainOmission(IReadOnlyList<string> kept, int omitted, IReadOnlyList<string> sample)
    {
        Kept = kept;
        Omitted = omitted;
        Sample = sample;
    }

    /// <summary>The representable categories, in first-observation order; empty for an all-unsafe domain.</summary>
    public IReadOnlyList<string> Kept { get; }

    /// <summary>The number of distinct categories left out.</summary>
    public int Omitted { get; }

    /// <summary>The first <see cref="ProbeSample.Cap"/> omitted categories, in first-observation order.</summary>
    public IReadOnlyList<string> Sample { get; }

    /// <summary>
    /// The omission for <paramref name="domain"/>, or null when it observed no category with CR
    /// or LF. A truncated domain with such a category never reaches a draft, so it is refused
    /// here as a defect. Cancellation is checked before each retained value.
    /// </summary>
    public static DomainOmission? Of(RetainedDomain domain, CancellationToken cancellationToken)
    {
        if (!domain.HasUnsafeValue)
        {
            return null;
        }

        if (domain.Truncated)
        {
            throw new InvalidOperationException("A truncated domain with a CR/LF category reached the draft.");
        }

        var kept = new List<string>(domain.Values.Count);
        var sample = new List<string>(ProbeSample.Cap);
        var omitted = 0;
        foreach (var value in domain.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RetainedDomain.ContainsLineBreak(value))
            {
                kept.Add(value);
                continue;
            }

            omitted++;
            if (sample.Count < ProbeSample.Cap)
            {
                sample.Add(value);
            }
        }

        return new DomainOmission(kept, omitted, sample);
    }
}
