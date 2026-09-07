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
/// actually built for.
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
    /// Revision 2 corrects <see cref="CountRecords"/>: revision 1 skipped the published file's
    /// empty final row and therefore recorded 32,561 records where the reader yields 32,562. The
    /// bytes are unchanged, but a stale entry would still carry the superseded count — and the
    /// count is a denominator, so an entry recorded under the old rule must be refused exactly as a
    /// changed corpus would be.
    /// </para>
    /// <para>
    /// It is still 2 after the <see cref="DataSha256"/> pin was added: the pin records the identity
    /// of the bytes this revision already wrote and already catalogued, so nothing prepared under
    /// it became stale. <b>Changing the accepted identity is a different matter and bumps this</b>
    /// — see <see cref="DataSha256"/>.
    /// </para>
    /// </summary>
    public const int AcquisitionRevision = 2;

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
    /// <b>What it does and does not establish.</b> It fixes the bytes as <em>the same bytes M8
    /// measured</em> — the ones recorded in <c>docs/benchmarks.md</c> — so a changed upstream file
    /// is refused rather than silently adopted and quietly re-based on. It is not a signature and
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
    /// The number of records the file carries: <b>every</b> line, blank ones included.
    /// <para>
    /// <b>The published training split ends with a doubled newline</b>, so it holds 32,561 census
    /// rows and one empty final row — 32,562 records. That empty row is really in the file, and a
    /// reader that yields it is right to: under RFC 4180 a doubled line break ends one record and
    /// begins another. Converting it produces a 32,562nd object with no crosses.
    /// </para>
    /// <para>
    /// This counter deliberately does <em>not</em> skip it. The record count is the denominator every
    /// Adult rate is divided by, so it has to be the number of records the pipeline actually reads;
    /// a count that quietly dropped a row the reader yields would make every rate slightly wrong and
    /// hide a real property of the published file. The property is documented in
    /// <c>Adult.attribution.md</c> and in the evidence pack rather than smoothed away here.
    /// </para>
    /// </summary>
    public static long CountRecords(string dataPath)
    {
        ArgumentNullException.ThrowIfNull(dataPath);

        var records = 0L;
        using var reader = new StreamReader(dataPath);
        while (reader.ReadLine() is not null)
        {
            records++;
        }

        return records;
    }
}
