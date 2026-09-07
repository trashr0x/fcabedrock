namespace FcaBedrock.Benchmarks.Corpus;

/// <summary>A prepared corpus on disk: its recorded identity plus the two absolute paths.</summary>
internal sealed record PreparedCorpus(CorpusEntry Entry, string DataPath, string SpecPath)
{
    /// <summary>The exact number of input records the data file carries.</summary>
    public long Records => Entry.Records;

    /// <summary>The exact input byte length — the denominator every MiB/s figure is derived from.</summary>
    public long InputBytes => Entry.Data.ByteLength;
}

/// <summary>
/// Prepares and verifies the synthetic corpora.
/// <para>
/// <b>Preparation is explicit.</b> Nothing here runs implicitly from a benchmark: a run
/// <see cref="Require">requires</see> an already-prepared case and fails with the exact command to
/// prepare it. That keeps a 73M-record generation from starting because someone typed a broad
/// filter, and it keeps generation firmly outside every measured interval.
/// </para>
/// <para>
/// <b>Cached inputs are verified, never assumed.</b> A case is reused only when its catalog entry
/// parses, its generator revision and geometry match today's definition, its spec is byte-identical
/// to the committed one, and both files still have exactly the recorded length and digest. Anything
/// else is refused — a stale corpus silently compared against a fresh one is the failure mode this
/// exists to prevent.
/// </para>
/// <para>
/// <b>An acquired corpus is additionally pinned.</b> For a generated case the catalog's recorded
/// digest is a complete check, because the bytes are a function of a committed generator at a
/// recorded revision. For an <see cref="CorpusCase.DataIdentity">acquired</see> one it is not: the
/// catalog records whatever arrived, so a changed upstream file and a catalog rewritten beside it
/// agree with each other perfectly. Such a case therefore carries the identity in its own source,
/// and it is checked <em>independently of the catalog</em> — on a fresh acquisition, before an
/// entry is written at all, and again on every reuse.
/// </para>
/// </summary>
internal static class CorpusPreparer
{
    /// <summary>
    /// Ensures <paramref name="corpus"/> is present and current, generating it when it is not.
    /// </summary>
    public static PreparedCorpus Prepare(CorpusCase corpus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var expected = Describe(corpus);
        if (TryLoad(expected) is { } current)
        {
            return current;
        }

        BenchmarkPaths.EnsureDirectories();
        var dataPath = Path.Combine(BenchmarkPaths.CorpusDirectory, expected.Data.FileName);
        var specPath = Path.Combine(BenchmarkPaths.CorpusDirectory, expected.Spec.FileName);

        // The catalog entry is written LAST, so an interrupted generation leaves files with no
        // entry — which TryLoad refuses, so the next run regenerates rather than measuring a
        // truncated corpus.
        Delete(CorpusCatalog.PathFor(corpus.Id));

        WriteAtomically(dataPath, stream => corpus.Write(stream, corpus.Records, cancellationToken));
        WriteAtomically(specPath, stream =>
        {
            var bytes = corpus.SpecBytes();
            stream.Write(bytes, 0, bytes.Length);
        });

        var actual = new CorpusIdentity(new FileInfo(dataPath).Length, CorpusCatalog.HashFile(dataPath));
        RequireIdentity(corpus, actual, dataPath);

        var prepared = expected with
        {
            // An external corpus's size is a fact about the download, not a parameter: its record
            // count is measured from the acquired file, exactly like its byte length and digest.
            Records = corpus.CountRecords is { } count ? count(dataPath) : expected.Records,
            Data = expected.Data with { ByteLength = actual.ByteLength, Sha256 = actual.Sha256 },
        };

        CorpusCatalog.Write(prepared);
        return new PreparedCorpus(prepared, dataPath, specPath);
    }

    /// <summary>
    /// Returns the prepared <paramref name="corpus"/>, or throws with the exact preparation
    /// command. Benchmarks call this from setup; they never generate.
    /// </summary>
    public static PreparedCorpus Require(CorpusCase corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        return TryLoad(Describe(corpus))
            ?? throw new InvalidOperationException(
                $"""
                The benchmark corpus '{corpus.Id}' is absent, incomplete, or no longer matches its recorded
                identity, so no measurement may be taken against it.

                Prepare it with:
                    dotnet run -c Release --project tests/FcaBedrock.Benchmarks -- prepare {PrepareToken(corpus)}

                Corpus root: {BenchmarkPaths.CorpusDirectory}
                (redirect it with the {BenchmarkPaths.RootVariable} environment variable)
                """);
    }

    /// <summary>
    /// Loads <paramref name="expected"/> from disk when every recorded fact still holds.
    /// <see langword="null"/> otherwise — an absent, unreadable, superseded, or altered corpus.
    /// </summary>
    public static PreparedCorpus? TryLoad(CorpusEntry expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var recorded = CorpusCatalog.Read(expected.Id);
        if (recorded is null
            || recorded.GeneratorRevision != expected.GeneratorRevision
            // A declared count must match exactly; a measured one only has to exist, because
            // nothing here knows what it should be until the corpus has been acquired.
            || (expected.RecordsDeclared ? recorded.Records != expected.Records : recorded.Records <= 0)
            || recorded.Columns != expected.Columns
            || !string.Equals(recorded.Family, expected.Family, StringComparison.Ordinal)
            || !string.Equals(recorded.Tier, expected.Tier, StringComparison.Ordinal)
            || !string.Equals(recorded.Data.FileName, expected.Data.FileName, StringComparison.Ordinal)
            || !string.Equals(recorded.Spec.FileName, expected.Spec.FileName, StringComparison.Ordinal)
            // The spec is committed source: a corpus prepared under a different spec text is a
            // different case, however identical its data happens to be.
            || !string.Equals(recorded.Spec.Sha256, expected.Spec.Sha256, StringComparison.Ordinal)
            // ...and for a pinned case, so is its data. The expectation comes from the corpus's own
            // source, so a catalog claiming a different digest is refused however self-consistent
            // it is with the file beside it.
            || (Pinned(expected)
                && (recorded.Data.ByteLength != expected.Data.ByteLength
                    || !string.Equals(recorded.Data.Sha256, expected.Data.Sha256, StringComparison.Ordinal))))
        {
            return null;
        }

        var dataPath = Path.Combine(BenchmarkPaths.CorpusDirectory, recorded.Data.FileName);
        var specPath = Path.Combine(BenchmarkPaths.CorpusDirectory, recorded.Spec.FileName);

        // For a pinned case the file is checked against the pin rather than against the entry. The
        // two are equal by the check above, and saying it here keeps the guarantee from resting on
        // a later reader noticing that.
        var requiredData = Pinned(expected)
            ? recorded.Data with { ByteLength = expected.Data.ByteLength, Sha256 = expected.Data.Sha256 }
            : recorded.Data;
        if (!Matches(dataPath, requiredData) || !Matches(specPath, recorded.Spec))
        {
            return null;
        }

        return new PreparedCorpus(recorded, dataPath, specPath);
    }

    /// <summary>
    /// The identity a freshly prepared case must have. The spec's digest is known up front because
    /// the spec is committed source; the data's is known up front only for a
    /// <see cref="CorpusCase.DataIdentity">pinned</see> case, and is otherwise left unset — the
    /// <c>-1</c> length is the sentinel for "recorded after generation, not required in advance".
    /// </summary>
    public static CorpusEntry Describe(CorpusCase corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var specBytes = corpus.SpecBytes();
        return new CorpusEntry(
            corpus.Id,
            corpus.Family,
            CorpusTiers.Token(corpus.Tier),
            corpus.GeneratorRevision,
            corpus.Records,
            corpus.Columns,
            new CorpusFile(
                corpus.Id + ".csv",
                corpus.DataIdentity?.ByteLength ?? -1,
                corpus.DataIdentity?.Sha256 ?? string.Empty),
            new CorpusFile(corpus.Id + ".toml", specBytes.Length, CorpusCatalog.HashBytes(specBytes)))
        {
            RecordsDeclared = corpus.RecordsDeclared,
        };
    }

    /// <summary>
    /// The <c>prepare</c> argument that prepares <paramref name="corpus"/>: its tier for a
    /// generated case, its own id for an external one, whose tier names no record count.
    /// </summary>
    public static string PrepareToken(CorpusCase corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        return corpus.RecordsDeclared ? CorpusTiers.Token(corpus.Tier) : corpus.Id;
    }

    /// <summary>Whether <paramref name="expected"/> carries a pinned data identity.</summary>
    private static bool Pinned(CorpusEntry expected) => expected.Data.ByteLength >= 0;

    // A freshly prepared file that is not the pinned one is refused outright: no catalog entry is
    // written, and the file is removed so nothing on disk looks prepared. Recording the new digest
    // instead - "it downloaded, so it must be right" - is exactly the silent re-basing the pin
    // exists to prevent: it would rewrite the identity every recorded measurement is stated
    // against, and the next run would compare two different corpora without saying so.
    private static void RequireIdentity(CorpusCase corpus, CorpusIdentity actual, string dataPath)
    {
        if (corpus.DataIdentity is not { } expected
            || (actual.ByteLength == expected.ByteLength
                && string.Equals(actual.Sha256, expected.Sha256, StringComparison.Ordinal)))
        {
            return;
        }

        Delete(dataPath);
        throw new InvalidOperationException(
            $"""
            The acquired '{corpus.Id}' corpus is not the input this suite accepts, so it was refused and
            removed. This is an input-identity failure, not an outage: the bytes arrived, and they are
            not the bytes every measurement recorded against this corpus was stated against.

            expected  {expected.ByteLength:N0} bytes, sha256 {expected.Sha256}
            actual    {actual.ByteLength:N0} bytes, sha256 {actual.Sha256}

            If the upstream change is real and intended, update the pinned identity and bump the
            acquisition revision together, then re-establish the evidence that depended on the old
            bytes. Do not adopt the new digest on its own.
            """);
    }

    private static bool Matches(string path, CorpusFile file)
    {
        var info = new FileInfo(path);
        return info.Exists
            && info.Length == file.ByteLength
            && string.Equals(CorpusCatalog.HashFile(path), file.Sha256, StringComparison.Ordinal);
    }

    // Generate beside the target and rename into place, so a partially written file is never
    // mistaken for a complete one even if the catalog check were ever relaxed.
    private static void WriteAtomically(string path, Action<Stream> write)
    {
        var staging = path + ".partial";
        Delete(staging);
        using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            write(stream);
            stream.Flush(flushToDisk: true);
        }

        File.Move(staging, path, overwrite: true);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
