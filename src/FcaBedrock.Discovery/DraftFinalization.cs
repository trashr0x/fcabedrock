using FcaBedrock.Diagnostics;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Discovery;

/// <summary>One attribute's baseline: its name from the naming matrix, before any collision repair.</summary>
/// <param name="Name">The baseline logical name.</param>
/// <param name="NameAdjusted">Whether the naming matrix already adjusted it.</param>
/// <param name="Source">Its selector, which nothing after the matrix changes.</param>
internal sealed record BaselineAttribute(string Name, bool NameAdjusted, SourceSection Source);

/// <summary>
/// Turns a completed record pass into the probe's result, for both shapes (§7.1). It runs only
/// after the pass and the session's disposal succeeded, so every read, structural, guard and
/// disposal failure has already been returned, and none of the warnings below is ever paired
/// with one of them.
/// <para>
/// In order: a CR/LF category in a truncated domain refuses the draft; collision repair fixes
/// the final names; then the adjustment, truncation and omission warnings are built, in that
/// order and only when they have something to report, and the draft is assembled.
/// </para>
/// <para>
/// Cancellation is checked in every loop here, in the repair, omission and draft loops this
/// calls, at every character of a name walk, and again immediately before each return. A
/// cancellation requested during the refusal scan therefore propagates instead of becoming the
/// refusal (D-112).
/// </para>
/// </summary>
internal static class DraftFinalization
{
    /// <summary>
    /// Finishes a probe whose current names in <paramref name="names"/> are the baseline names,
    /// by ordinal, and whose domains are <paramref name="domains"/>, by the same ordinal.
    /// </summary>
    public static Diagnosed<SpecDocument> Finish(
        BindingSection binding,
        ProbeOptions options,
        IReadOnlyList<BaselineAttribute> baseline,
        IReadOnlyList<RetainedDomain> domains,
        NameRelations names,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var refused = new List<string>(ProbeSample.Cap);
        var refusedCount = 0;
        for (var ordinal = 0; ordinal < domains.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (domains[ordinal].Truncated && domains[ordinal].HasUnsafeValue)
            {
                refusedCount++;
                if (refused.Count < ProbeSample.Cap)
                {
                    refused.Add(baseline[ordinal].Name);
                }
            }
        }

        if (refusedCount > 0)
        {
            var refusal = ProbeDiagnostics.UnsafeTruncatedDomain(refusedCount, refused);
            cancellationToken.ThrowIfCancellationRequested();
            return Diagnosed<SpecDocument>.Failed([refusal]);
        }

        var renamed = CollisionRepair.Apply(names, domains, cancellationToken);

        var attributes = new List<ProbedAttribute>(domains.Count);
        var adjusted = new ProbeTally();
        var truncated = new ProbeTally();
        var omittedAttributes = 0;
        var omittedCategories = 0L;
        var omittedSample = new List<(string, string)>(ProbeSample.Cap);
        for (var ordinal = 0; ordinal < domains.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = names.NameOf(ordinal);
            var domain = domains[ordinal];
            var omission = DomainOmission.Of(domain, cancellationToken);
            attributes.Add(new ProbedAttribute(name, baseline[ordinal].Source, domain, omission));

            if (baseline[ordinal].NameAdjusted || renamed[ordinal])
            {
                adjusted.Record(name);
            }

            if (domain.Truncated)
            {
                truncated.Record(name);
            }

            if (omission is not null)
            {
                omittedAttributes++;
                omittedCategories = checked(omittedCategories + omission.Omitted);
                foreach (var category in omission.Sample)
                {
                    if (omittedSample.Count < ProbeSample.Cap)
                    {
                        omittedSample.Add((name, category));
                    }
                }
            }
        }

        // Flushed in attribute order, so counts, order and samples depend only on the schema and
        // the record sequence (D-112).
        var warnings = new List<BedrockDiagnostic>(3);
        if (adjusted.Any)
        {
            warnings.Add(ProbeDiagnostics.AttributeNameAdjusted(adjusted));
        }

        if (truncated.Any)
        {
            warnings.Add(ProbeDiagnostics.DomainTruncated(truncated, options.ValueRetentionLimit));
        }

        if (omittedAttributes > 0)
        {
            warnings.Add(ProbeDiagnostics.ValueOmitted(omittedCategories, omittedAttributes, omittedSample));
        }

        var draft = ProbeDraft.Build(binding, options, attributes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Diagnosed<SpecDocument>.Ok(draft, warnings);
    }
}
