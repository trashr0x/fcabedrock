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
    /// Checks the plan's shape: the curated spec's attribute count, and that every one of them
    /// produced at least one formal attribute.
    /// <para>
    /// It is a weaker claim than a byte expectation and is meant to be. What it catches is the
    /// failure a digest comparison against a self-recorded baseline cannot: an attribute that
    /// silently resolved to nothing, which would make every later run agree with a wrong first one.
    /// </para>
    /// </summary>
    public static void RequirePlanShape(ConversionPlan plan, string what)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Attributes.Count != SpecAttributeCount)
        {
            throw new InvalidOperationException(
                $"{what}: the plan carries {plan.Attributes.Count} attributes, expected {SpecAttributeCount}.");
        }

        if (plan.FormalAttributes.Count < SpecAttributeCount)
        {
            throw new InvalidOperationException(
                $"{what}: {plan.FormalAttributes.Count} formal attributes for {SpecAttributeCount} spec "
                + "attributes means at least one resolved to no column at all.");
        }
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
