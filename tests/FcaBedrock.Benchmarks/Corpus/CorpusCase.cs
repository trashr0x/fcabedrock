using System.Text;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>Where a corpus's bytes come from.</summary>
internal enum CorpusOrigin
{
    /// <summary>Produced here, from pinned integer arithmetic, at a declared record count.</summary>
    Generated,

    /// <summary>
    /// Acquired from outside the repository. Its record count and byte length are <b>measured</b>
    /// at preparation rather than declared, and acquisition may touch the network — which is why it
    /// happens only under the explicit <c>prepare</c> verb.
    /// </summary>
    External,
}

/// <summary>
/// One preparable corpus: what it is called, how much data it carries, how its bytes are produced,
/// and which spec it is converted under.
/// <para>
/// A single description type for every family is what keeps preparation, cataloguing, staleness
/// checking, and the report's denominators from drifting apart per family — each of those reads the
/// same record, so a new family gains all of them by being described rather than by being
/// special-cased.
/// </para>
/// </summary>
internal sealed record CorpusCase(
    string Family,
    string Variant,
    CorpusTier Tier,
    int GeneratorRevision,
    int Columns,
    string SpecText,
    Action<Stream, long, CancellationToken> Write)
{
    /// <summary>Where the bytes come from.</summary>
    public CorpusOrigin Origin { get; init; } = CorpusOrigin.Generated;

    /// <summary>
    /// Measures the record count of a prepared external corpus. Null for a generated case, whose
    /// count is declared by its tier.
    /// </summary>
    public Func<string, long>? CountRecords { get; init; }

    /// <summary>
    /// The catalog key and file-name stem: <c>family[-variant][-tier]</c>. Stable, lowercase, and
    /// filesystem-safe, because it names files an operator will see. An external case omits the
    /// tier, which for it would name a record count nobody chose.
    /// </summary>
    public string Id
    {
        get
        {
            var stem = Variant.Length == 0 ? Family : $"{Family}-{Variant}";
            return Origin == CorpusOrigin.External ? stem : $"{stem}-{CorpusTiers.Token(Tier)}";
        }
    }

    /// <summary>True when the record count is fixed by the tier rather than measured after acquisition.</summary>
    public bool RecordsDeclared => Origin == CorpusOrigin.Generated;

    /// <summary>
    /// The number of input records this case's data file carries, or <c>0</c> for an external case
    /// whose count is not known until it has been prepared — read the prepared catalog entry for
    /// the measured value.
    /// </summary>
    public long Records => RecordsDeclared ? CorpusTiers.Records(Tier) : 0L;

    /// <summary>The benchmark tier category a case of this tier must carry.</summary>
    public string TierCategory => CorpusTiers.Category(Tier);

    /// <summary>The spec exactly as it is written to disk: UTF-8, no BOM.</summary>
    public byte[] SpecBytes() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(SpecText);

    /// <inheritdoc/>
    public override string ToString() => Id;
}

/// <summary>
/// Every corpus the suite can prepare. The registry is the single place a family's tiers, layouts,
/// and specs are named, so the <c>prepare</c> verb, the benchmarks, and the report's denominator
/// columns cannot disagree about what exists.
/// </summary>
internal static class CorpusCases
{
    /// <summary>The synthetic wide family (16 columns).</summary>
    public const string W16Family = "w16";

    /// <summary>The synthetic triple family (10 rows per subject).</summary>
    public const string T10Family = "t10";

    /// <summary>The keyed-wide dedupe pressure family (17 columns, repeating object key).</summary>
    public const string KeyedFamily = "w16keyed";

    /// <summary>The synthetic Ads-width family (1,559 columns).</summary>
    public const string AdsFamily = "ads";

    /// <summary>The long-string pressure family (4 columns, large values).</summary>
    public const string LongTextFamily = "longtext";

    /// <summary>The externally acquired UCI Adult corpus.</summary>
    public const string AdultFamily = "adult";

    /// <summary>The W16 case for a tier, converted under the fully declared spec.</summary>
    public static CorpusCase W16(CorpusTier tier) => new(
        W16Family,
        Variant: string.Empty,
        tier,
        W16Corpus.GeneratorRevision,
        W16Corpus.ColumnCount,
        W16Specs.Declared,
        W16Corpus.Write);

    /// <summary>The T10 case for a tier and physical layout.</summary>
    public static CorpusCase T10(CorpusTier tier, TripleLayout layout) => new(
        T10Family,
        layout == TripleLayout.Grouped ? "grouped" : "unordered",
        tier,
        T10Corpus.GeneratorRevision,
        Columns: 3,
        // The grouped file is the one a `subject_grouped` spec may read; the interleaved file needs
        // `unordered`. Pairing each layout with the ordering that accepts it is what makes the two
        // a byte-equality comparison instead of one of them simply failing contiguity.
        layout == TripleLayout.Grouped ? T10Specs.AsGrouped(T10Specs.Declared) : T10Specs.Declared,
        (stream, records, token) => T10Corpus.Write(stream, records, layout, token));

    /// <summary>The keyed-wide dedupe case for a tier.</summary>
    public static CorpusCase Keyed(CorpusTier tier) => new(
        KeyedFamily,
        Variant: string.Empty,
        tier,
        KeyedW16Corpus.GeneratorRevision,
        KeyedW16Corpus.ColumnCount,
        KeyedW16Specs.Declared,
        KeyedW16Corpus.Write);

    /// <summary>The Ads-width case for a tier.</summary>
    public static CorpusCase Ads(CorpusTier tier) => new(
        AdsFamily,
        Variant: string.Empty,
        tier,
        AdsCorpus.GeneratorRevision,
        AdsCorpus.ColumnCount,
        AdsSpecs.Declared,
        AdsCorpus.Write);

    /// <summary>The long-text case for a tier.</summary>
    public static CorpusCase LongText(CorpusTier tier) => new(
        LongTextFamily,
        Variant: string.Empty,
        tier,
        LongTextCorpus.GeneratorRevision,
        LongTextCorpus.ColumnCount,
        LongTextSpecs.Declared,
        LongTextCorpus.Write);

    /// <summary>The externally acquired UCI Adult case.</summary>
    public static CorpusCase Adult { get; } = new(
        AdultFamily,
        Variant: string.Empty,
        CorpusTier.External,
        AdultCorpus.AcquisitionRevision,
        AdultCorpus.ColumnCount,
        AdultSpecs.Declared,
        AdultCorpus.Acquire)
    {
        Origin = CorpusOrigin.External,
        CountRecords = AdultCorpus.CountRecords,
    };

    // Declared BEFORE `All`, deliberately: static field initializers run in textual order, so a
    // tier list declared after it would still be null when BuildAll reads it.
    private static IReadOnlyList<CorpusTier> SyntheticTiers { get; } =
        [CorpusTier.Small, CorpusTier.Working, CorpusTier.Scale7M, CorpusTier.Scale73M];

    private static IReadOnlyList<CorpusTier> KeyedTiers { get; } =
        [CorpusTier.Small, CorpusTier.Working, CorpusTier.Scale7M];

    /// <summary>Every case the suite knows how to prepare, in a stable order.</summary>
    public static IReadOnlyList<CorpusCase> All { get; } = BuildAll();

    /// <summary>Every case a tier defines, in a stable order.</summary>
    public static IReadOnlyList<CorpusCase> ForTier(CorpusTier tier) =>
        [.. All.Where(candidate => candidate.Tier == tier)];

    /// <summary>Resolves a case by its family, variant, and tier; used by the report columns.</summary>
    public static CorpusCase? Find(string family, string variant, CorpusTier tier) =>
        All.FirstOrDefault(candidate =>
            string.Equals(candidate.Family, family, StringComparison.Ordinal)
            && string.Equals(candidate.Variant, variant, StringComparison.Ordinal)
            && candidate.Tier == tier);

    /// <summary>Resolves a case by its catalog id; used by the <c>prepare</c> verb.</summary>
    public static CorpusCase? Find(string id) =>
        All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<CorpusCase> BuildAll()
    {
        // Size-ascending within each family, families in the order the matrix introduces them, so
        // `prepare` output and the report's rows read the same way every time.
        var cases = new List<CorpusCase>();

        foreach (var tier in SyntheticTiers)
        {
            cases.Add(W16(tier));
            cases.Add(T10(tier, TripleLayout.Grouped));
            cases.Add(T10(tier, TripleLayout.Interleaved));
        }

        // Keyed dedupe stops at 7.3M: the matrix asks for it at the working and 7.3M tiers, and a
        // 73M keyed corpus would cost a tier's storage for a case nothing consumes.
        foreach (var tier in KeyedTiers)
        {
            cases.Add(Keyed(tier));
        }

        // Ads-width is a WIDTH case, deliberately outside the target-scale matrix: 1,559 columns
        // pressure the planner, the probe's accounting, and the reader's per-record array, none of
        // which needs seventy-three million rows to show.
        cases.Add(Ads(CorpusTier.Micro));
        cases.Add(Ads(CorpusTier.Small));

        cases.Add(LongText(CorpusTier.Small));
        cases.Add(LongText(CorpusTier.Working));
        cases.Add(Adult);
        return cases;
    }
}
