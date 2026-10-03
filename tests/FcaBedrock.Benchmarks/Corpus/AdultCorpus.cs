using System.IO.Compression;

namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>
/// The <b>UCI Adult</b> external corpus: the original census-derived training split, acquired rather
/// than generated.
/// <para>
/// Every other family in this suite is synthetic, which means every one of them was designed by the
/// same person who wrote the expectations. Real data is the check on that: its value distributions,
/// its missing cells, and its domain sizes were not chosen to make anything convenient, and the v2
/// lineage used exactly this dataset, so a measurement over it is comparable with what the tool was
/// built for.
/// </para>
/// <para>
/// <b>Acquisition is explicit, separate, and outside every measured interval.</b> It happens only
/// under the <c>prepare</c> verb and never as a side effect of a run; the downloaded bytes are
/// written verbatim, checked against the identity pinned below, and recorded in the catalog, and
/// the file itself never enters Git. No ordinary test and no benchmark measurement performs any
/// network access.
/// </para>
/// <para>
/// <b>It is not in routine CI.</b> The Adult cases carry the opt-in
/// <c>External</c> category, so no bare run and no native CI job depends on a third-party host
/// being reachable. A successful <c>--anyCategories External</c> run on the final Windows x64
/// candidate is instead a blocking acceptance obligation, recorded in D-124 and the roadmap: the
/// real-data evidence is required, but it is required of the candidate rather than of every job.
/// </para>
/// <para>
/// <b>Attribution.</b> Becker, B. and Kohavi, R. (1996). <i>Adult</i>. UCI Machine Learning
/// Repository. <c>https://doi.org/10.24432/C5XW20</c>. Distributed under the Creative Commons
/// Attribution 4.0 International licence. The committed spec beside it is authored FcaBedrock
/// material, not part of the dataset; see <c>Adult.attribution.md</c>.
/// </para>
/// </summary>
internal static class AdultCorpus
{
    /// <summary>
    /// The acquisition revision. <b>Bump this whenever the source, the selected entry, the written
    /// bytes, or the way the recorded measurements are derived changes</b>: it is recorded in the
    /// catalog, so a corpus acquired under an older definition is refused rather than silently
    /// reused.
    /// <para>
    /// Revision 2 corrected <see cref="CountRecords"/> to count the published file's empty final
    /// line, which the reader then yielded as a record (32,562). Revision 3 follows the reader
    /// again: it skips blank records (spec §5.1.1), so that line is not a record and the count is
    /// 32,561. The bytes are unchanged, but a stale entry would still carry the superseded count —
    /// and the count is a denominator, so an entry recorded under an older rule must be refused
    /// exactly as a changed corpus would be.
    /// </para>
    /// <para>
    /// The <see cref="DataSha256"/> pin, added under revision 2, records the identity of the bytes
    /// every revision has written, so adding it did not change the revision. <b>Changing the
    /// accepted identity is a different matter and bumps this</b> — see <see cref="DataSha256"/>.
    /// </para>
    /// </summary>
    public const int AcquisitionRevision = 3;

    /// <summary>The physical column count of <c>adult.data</c>.</summary>
    public const int ColumnCount = 15;

    /// <summary>The repository archive the training split is published in.</summary>
    public const string ArchiveUrl = "https://archive.ics.uci.edu/static/public/2/adult.zip";

    /// <summary>The entry inside the archive: the training split, headerless.</summary>
    public const string EntryName = "adult.data";

    /// <summary>
    /// The exact byte length of the <see cref="EntryName"/> entry this suite accepts.
    /// </summary>
    public const long DataByteLength = 3_974_305L;

    /// <summary>
    /// The exact SHA-256 of the <see cref="EntryName"/> entry this suite accepts.
    /// <para>
    /// <b>Why the entry and not the archive.</b> What a measurement consumes is the training
    /// split's bytes; the zip around them can be repacked without changing a single one of them,
    /// and pinning the container would refuse a download that is in fact identical. So the pin is
    /// on the consumed entry.
    /// </para>
    /// <para>
    /// <b>What it does and does not establish.</b> It fixes the bytes as <em>the bytes the recorded
    /// measurements used</em> (the ones recorded in <c>docs/benchmarks.md</c>), so a changed upstream
    /// file is refused rather than silently adopted and quietly re-based on. It is not a signature and
    /// establishes nothing about publisher authenticity: no attestation for this dataset exists to
    /// check against.
    /// </para>
    /// <para>
    /// <b>Changing it bumps <see cref="AcquisitionRevision"/>.</b> The accepted identity and the
    /// revision are two halves of one fact: the identity refuses different bytes, and the revision
    /// refuses a catalog entry written under the previous definition. Moving one without the other
    /// would let an already-prepared corpus survive a change of what "prepared" means.
    /// </para>
    /// </summary>
    public const string DataSha256 = "5b00264637dbfec36bdeaab5676b0b309ff9eb788d63554ca0a249491c86603d";

    /// <summary>
    /// The pinned identity of the consumed entry, enforced on a fresh acquisition and on reuse —
    /// the second independently of the catalog's own recorded digest, so a changed file beside a
    /// rewritten catalog that agrees with it is still refused.
    /// </summary>
    public static CorpusIdentity Identity { get; } = new(DataByteLength, DataSha256);

    /// <summary>The dataset citation, recorded beside every measurement over it.</summary>
    public const string Citation =
        "Becker, B. and Kohavi, R. (1996). Adult. UCI Machine Learning Repository. "
        + "https://doi.org/10.24432/C5XW20. CC BY 4.0.";

    /// <summary>
    /// Downloads the archive and writes <see cref="EntryName"/> to <paramref name="destination"/>
    /// verbatim — no re-encoding, no line-ending change, no trimming. The written bytes are the
    /// published bytes, which is what makes the recorded digest mean something.
    /// </summary>
    /// <param name="destination">The stream the entry is copied to.</param>
    /// <param name="records">Ignored: an external corpus's size is a fact, not a parameter.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    public static void Acquire(Stream destination, long records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        _ = records;

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        byte[] archive;
        try
        {
            archive = client.GetByteArrayAsync(ArchiveUrl, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"""
                The UCI Adult archive could not be downloaded from {ArchiveUrl}.

                This corpus is acquired, not generated, so there is no offline fallback: without the
                download there is no Adult case, and no Adult measurement may be reported. Every
                other family in this suite prepares without a network.

                Cause: {exception.Message}
                """,
                exception);
        }

        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var entry = zip.GetEntry(EntryName)
            ?? throw new InvalidOperationException(
                $"'{EntryName}' is not in {ArchiveUrl}; it contains: "
                + string.Join(", ", zip.Entries.Select(candidate => candidate.FullName)));

        using var source = entry.Open();
        source.CopyTo(destination);
    }

    /// <summary>
    /// The number of records the file carries: every line that is not blank.
    /// <para>
    /// <b>The published training split ends with a doubled newline</b>, so after its 32,561 census
    /// rows it holds one empty final line. That line is a blank record, which the reader skips
    /// (spec §5.1.1), so it is not counted and the file carries 32,561 records.
    /// </para>
    /// <para>
    /// The record count is the denominator every Adult rate is divided by, so it has to be the
    /// number of records the pipeline reads, by the reader's own blank rule. The file's
    /// property is documented in <c>Adult.attribution.md</c> rather than smoothed away by altering
    /// the bytes.
    /// </para>
    /// </summary>
    public static long CountRecords(string dataPath)
    {
        ArgumentNullException.ThrowIfNull(dataPath);

        var records = 0L;
        using var reader = new StreamReader(dataPath);
        while (reader.ReadLine() is { } line)
        {
            if (!IsBlank(line))
            {
                records++;
            }
        }

        return records;
    }

    /// <summary>
    /// Spec §5.1.1's blank record for this quote-free, comma-delimited file: a line of whitespace
    /// only (a comma is not whitespace, so such a line has no delimiter either). The drain oracle
    /// applies the same rule.
    /// </summary>
    internal static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);
}
