using BenchmarkDotNet.Attributes;
using FcaBedrock.Benchmarks.Configuration;
using FcaBedrock.Benchmarks.Corpus;
using FcaBedrock.Benchmarks.Oracles;

namespace FcaBedrock.Benchmarks;

// The lexical drains: the wide source drain over each grammar-stressing variant (LexicalCorpus).
// The measured interval is the wide source drain's; each class only names its corpus and derives
// the expected summary from the variant's own value definition, outside timing.

/// <summary>Six quoted fields per record with doubled quotes and line breaks: the quote-heavy path.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "quoted-multiline", CorpusTier.Small)]
public class QuotedMultilineSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("quoted-multiline");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("quoted-multiline"), prepared.Records);
}

/// <summary>Sixteen plain fields separated by TAB.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "tab", CorpusTier.Small)]
public class TabDelimitedSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("tab");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("tab"), prepared.Records);
}

/// <summary>Sixteen plain fields separated by a space.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "space", CorpusTier.Small)]
public class SpaceDelimitedSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("space");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("space"), prepared.Records);
}

/// <summary>2,000 records of 200 short fields: the per-field cost.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "many-fields", CorpusTier.Small)]
public class ManyFieldsSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("many-fields");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("many-fields"), prepared.Records);
}

/// <summary>500 records of four 5,000-character fields: long-field scanning.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "long-fields", CorpusTier.Small)]
public class LongFieldsSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("long-fields");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("long-fields"), prepared.Records);
}

/// <summary>Sixteen fields padded with a no-break space and an ideographic space, which the reader removes.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "unicode-whitespace", CorpusTier.Small)]
public class UnicodeWhitespaceSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("unicode-whitespace");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("unicode-whitespace"), prepared.Records);
}

/// <summary>Sixteen plain fields per record and a blank line after every record, which the reader skips.</summary>
[BenchmarkCategory(BenchmarkCategories.Small)]
[BenchmarkCorpus(CorpusCases.LexicalFamily, "blank-runs", CorpusTier.Small)]
public class BlankRunsSourceDrainSmall : WideSourceDrainBenchmark
{
    private protected override CorpusCase Corpus => CorpusCases.Lexical("blank-runs");

    private protected override DrainSummary Expected(PreparedCorpus prepared) =>
        LexicalDrainOracle.Expected(LexicalCorpus.Variant("blank-runs"), prepared.Records);
}
