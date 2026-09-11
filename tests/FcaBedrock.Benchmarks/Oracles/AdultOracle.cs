using FcaBedrock.Core.Planning;

namespace FcaBedrock.Benchmarks.Oracles;

/// <summary>
/// What can honestly be asserted about a conversion of real UCI Adult data.
/// <para>
/// Every other family in this suite has a generator, so its expected output is derivable. Adult does
/// not, and six of its curated attributes discover their domains from the data, so deriving the
/// expected bytes would mean re-implementing calibration — an oracle that cannot disagree with the
/// code proves nothing. What this file supplies instead is the set of checks that <em>are</em>
/// independent: a second reader for the drain summary, the plan's shape, and the object count.
/// </para>
/// </summary>
internal static class AdultOracle
{
    /// <summary>The number of attributes the curated spec declares.</summary>
    public const int SpecAttributeCount = 14;

    /// <summary>
    /// The expected drain summary, derived by a <b>second, independent reader</b>: split on the
    /// delimiter, trim each field, and treat an empty field or the missing token as missing (§5.1).
    /// <para>
    /// A naive split is a legitimate reader here only because Adult carries no quoted fields, and
    /// that is checked rather than assumed: a quote anywhere in the file makes this oracle wrong,
    /// and it says so instead of quietly disagreeing with the production reader.
    /// </para>
    /// </summary>
    public static DrainSummary ExpectedDrain(string dataPath)
    {
        ArgumentNullException.ThrowIfNull(dataPath);

        var summary = default(DrainSummary);
        using var reader = new StreamReader(dataPath);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;

            // Blank lines are NOT skipped: the published file ends with a doubled newline, so its
            // final empty row is a record the reader yields, and an oracle that dropped it would be
            // shaped to disagree with correct behaviour. An empty line splits to one empty field,
            // which cleans to missing and therefore contributes no present field and no characters.
            if (line.Contains('"', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"adult drain oracle: line {lineNumber} contains a quote character, so a naive split is "
                    + "not a valid reader for this file and this oracle would be wrong rather than independent.");
            }

            summary = summary.AddRecord();
            foreach (var field in line.Split(','))
            {
                var cleaned = field.Trim();
                summary = summary.AddField(
                    cleaned.Length == 0 || string.Equals(cleaned, MissingToken, StringComparison.Ordinal)
                        ? null
                        : cleaned);
            }
        }

        return summary;
    }

    /// <summary>
    /// Checks the plan's shape: the curated spec's attribute count, and that <b>every one of those
    /// attributes</b> contributed at least one formal attribute of its own.
    /// <para>
    /// It is a weaker claim than a byte expectation and is meant to be. What it catches is the
    /// failure a digest comparison against a self-recorded baseline cannot: an attribute that
    /// silently resolved to nothing, which would make every later run agree with a wrong first one.
    /// </para>
    /// <para>
    /// <b>Why the total column count cannot make that claim.</b> Adult's nominal attributes
    /// discover domains of a dozen values or more, so the total sits far above fourteen whatever
    /// any single attribute did: one <see cref="PlannedAttribute"/> could carry an empty
    /// <see cref="PlannedAttribute.CrossesByBin"/> and no
    /// <see cref="PlannedAttribute.MissingFormalAttributeId"/> while the total stayed comfortably
    /// over the threshold, and the check would pass on an attribute that emits nothing. So the
    /// proof is per attribute, and it is <em>attributed</em>: every id an attribute claims must
    /// resolve to a real column whose <see cref="FormalAttributeIdentity.AttributeName"/> is that
    /// attribute's own, which is what stops another attribute's columns from standing in for a
    /// missing one.
    /// </para>
    /// <para>
    /// A recognized bin that crosses nothing is not a defect — the false pole of <c>sex</c> and of
    /// <c>class</c> is exactly that — so what must be non-empty is the <em>union</em> over an
    /// attribute's bins plus its missing column, never each bin.
    /// </para>
    /// </summary>
    public static void RequirePlanShape(ConversionPlan plan, string what)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequirePlanShape(plan.Attributes, plan.FormalAttributes, what);
    }

    /// <summary>
    /// The same check over the two lists a plan carries, so the per-attribute proof can be
    /// exercised directly against a hand-built shape: <see cref="ConversionPlan"/> is planner-owned
    /// and cannot be constructed outside Core, and a check nothing can fail is not a check.
    /// </summary>
    public static void RequirePlanShape(
        IReadOnlyList<PlannedAttribute> attributes,
        IReadOnlyList<FormalAttribute> formalAttributes,
        string what)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(formalAttributes);

        if (attributes.Count != SpecAttributeCount)
        {
            throw new InvalidOperationException(
                $"{what}: the plan carries {attributes.Count} attributes, expected {SpecAttributeCount}.");
        }

        var byId = new Dictionary<int, FormalAttribute>(formalAttributes.Count);
        foreach (var formal in formalAttributes)
        {
            if (!byId.TryAdd(formal.Id, formal))
            {
                throw new InvalidOperationException(
                    $"{what}: formal attribute id {formal.Id} appears twice in the schema, so no "
                    + "attribute's contribution can be attributed to it.");
            }
        }

        // Ownership is tracked across attributes as well as within one: two spec attributes that
        // both claimed the same column would mean one of them contributed nothing of its own.
        var owner = new Dictionary<int, string>(formalAttributes.Count);

        foreach (var attribute in attributes)
        {
            var contributed = Contributions(attribute);
            if (contributed.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{what}: attribute '{attribute.Name}' crosses no formal attribute in any bin and "
                    + "carries no missing column, so it resolved to nothing at all.");
            }

            foreach (var id in contributed)
            {
                if (!byId.TryGetValue(id, out var formal))
                {
                    throw new InvalidOperationException(
                        $"{what}: attribute '{attribute.Name}' crosses formal attribute id {id}, which "
                        + "the plan's schema does not contain.");
                }

                if (!string.Equals(formal.Identity.AttributeName, attribute.Name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{what}: attribute '{attribute.Name}' crosses formal attribute id {id}, which "
                        + $"belongs to '{formal.Identity.AttributeName}'.");
                }

                if (!owner.TryAdd(id, attribute.Name))
                {
                    throw new InvalidOperationException(
                        $"{what}: formal attribute id {id} is claimed by both '{owner[id]}' and "
                        + $"'{attribute.Name}'.");
                }
            }
        }

        // An additional guard, not the proof: fourteen attributes each owning at least one
        // distinct column cannot produce fewer than fourteen columns, so a smaller total means the
        // schema and the emit pipelines disagree about what was planned.
        if (formalAttributes.Count < SpecAttributeCount)
        {
            throw new InvalidOperationException(
                $"{what}: {formalAttributes.Count} formal attributes for {SpecAttributeCount} spec "
                + "attributes means at least one resolved to no column at all.");
        }
    }

    /// <summary>
    /// The distinct formal-attribute ids one planned attribute claims: every id crossed by any of
    /// its bins, plus its missing column when it authors one. Ordered, so a failure names the same
    /// id whichever run produced it.
    /// </summary>
    private static SortedSet<int> Contributions(PlannedAttribute attribute)
    {
        var contributed = new SortedSet<int>();
        foreach (var crosses in attribute.CrossesByBin.Values)
        {
            foreach (var id in crosses)
            {
                contributed.Add(id);
            }
        }

        if (attribute.MissingFormalAttributeId is { } missingId)
        {
            contributed.Add(missingId);
        }

        return contributed;
    }

    /// <summary>
    /// The exact number of lines a <c>.cxt</c> of this context carries: five header lines (the
    /// <c>B</c> magic, a blank, the two counts, a blank), one line per object name, one per
    /// formal-attribute name, and one per matrix row (§18.1).
    /// </summary>
    public static long CxtLineCount(long objects, ConversionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return 5 + objects + plan.FormalAttributes.Count + objects;
    }

    private const string MissingToken = "?";
}
