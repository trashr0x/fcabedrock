using FcaBedrock.Benchmarks.Corpus;

namespace FcaBedrock.Benchmarks.Configuration;

/// <summary>
/// Declares which prepared corpus a benchmark class reads.
/// <para>
/// It exists so the denominator is a <em>fact about the case</em> rather than something a reader
/// infers from a class name: the report's columns read it to state input records and input bytes
/// beside every timing, and a conformance test reads it to prove each case's tier category matches
/// the corpus it actually reads. A number without its denominator is not evidence, so the
/// denominator travels with the case.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class BenchmarkCorpusAttribute(string family, string variant, CorpusTier tier) : Attribute
{
    /// <summary>Convenience for the families with a single layout.</summary>
    public BenchmarkCorpusAttribute(string family, CorpusTier tier)
        : this(family, string.Empty, tier)
    {
    }

    /// <summary>The corpus family this class reads.</summary>
    public string Family { get; } = family;

    /// <summary>The variant within the family, or empty when the family has one layout.</summary>
    public string Variant { get; } = variant;

    /// <summary>The tier this class reads.</summary>
    public CorpusTier Tier { get; } = tier;

    /// <summary>The registry entry this attribute names, or <see langword="null"/> if there is none.</summary>
    public CorpusCase? Resolve() => CorpusCases.Find(Family, Variant, Tier);
}
