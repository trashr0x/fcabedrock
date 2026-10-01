namespace FcaBedrock.Sources.Tests;

/// <summary>
/// The owned read chain (D-041): settings the provider refuses never acquire a stream; a factory
/// failure owns nothing; and the fixed close order (provider, decoder, stream), each attempted once
/// and continued after a failure, with the primary-failure and cleanup-failure precedence.
/// </summary>
public sealed class DelimitedSourceReaderTests
{
    private const string SecondaryKey = DelimitedSourceReader.SecondaryCleanupFailuresKey;

    [Fact]
    public void SecondaryCleanupFailuresKey_IsTheAgreedName() =>
        Assert.Equal("FcaBedrock.Sources.SecondaryCleanupFailures", SecondaryKey);

    [Fact]
    public void Open_WhenTheProviderRefusesTheDelimiter_ThenTheFactoryNeverRuns()
    {
        var opens = 0;

        Assert.ThrowsAny<ArgumentException>(() =>
            DelimitedSourceReader.Open(() => { opens++; return new MemoryStream(); }, '#', CancellationToken.None));
        Assert.Equal(0, opens);
    }

    [Fact]
    public void Open_WhenTheFactoryReturnsNull_ThenArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => DelimitedSourceReader.Open(() => null!, ',', CancellationToken.None));

    [Fact]
    public void Close_WhenCalledTwice_ThenTheResourcesAreClosedOnce()
    {
        var stream = ScriptedStream.Utf8("a,b\n");
        var reader = DelimitedSourceReader.Open(() => stream, ',', CancellationToken.None);

        reader.Close();
        reader.Close();

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void CloseInOrder_WhenEveryCloseSucceeds_ThenEachResourceIsClosedOnceInOrder()
    {
        var log = new List<string>();

        DelimitedSourceReader.CloseInOrder([new Recording("provider", log), new Recording("decoder", log), new Recording("stream", log)], primary: null);

        Assert.Equal(["provider", "decoder", "stream"], log);
    }

    [Fact]
    public void CloseInOrder_WhenAResourceWasNeverAcquired_ThenItIsSkipped()
    {
        var log = new List<string>();

        DelimitedSourceReader.CloseInOrder([null, new Recording("decoder", log), new Recording("stream", log)], primary: null);

        Assert.Equal(["decoder", "stream"], log);
    }

    [Fact]
    public void CloseInOrder_WhenThereIsAPrimary_ThenNothingIsThrownAndEveryCloseFailureRidesOnItInAttemptOrder()
    {
        var log = new List<string>();
        var primary = new IOException("primary");
        var first = new InvalidOperationException("provider close");
        var second = new IOException("decoder close");
        var third = new IOException("stream close");

        DelimitedSourceReader.CloseInOrder(
            [new Recording("provider", log, first), new Recording("decoder", log, second), new Recording("stream", log, third)], primary);

        Assert.Equal(["provider", "decoder", "stream"], log);
        Assert.Equal<Exception>([first, second, third], Assert.IsAssignableFrom<IReadOnlyList<Exception>>(primary.Data[SecondaryKey]));
    }

    [Fact]
    public void CloseInOrder_WhenThereIsNoPrimary_ThenTheFirstCloseFailureIsThrownAsItselfCarryingTheLaterOnes()
    {
        var log = new List<string>();
        var first = new InvalidOperationException("provider close");
        var second = new IOException("decoder close");
        var third = new IOException("stream close");

        var thrown = Assert.Throws<InvalidOperationException>(() => DelimitedSourceReader.CloseInOrder(
            [new Recording("provider", log, first), new Recording("decoder", log, second), new Recording("stream", log, third)], primary: null));

        Assert.Same(first, thrown);
        Assert.Equal(["provider", "decoder", "stream"], log);
        Assert.Equal<Exception>([second, third], Assert.IsAssignableFrom<IReadOnlyList<Exception>>(thrown.Data[SecondaryKey]));
    }

    [Fact]
    public void CloseInOrder_WhenOnlyOneCloseFailsAndThereIsNoPrimary_ThenItIsThrownWithNoDetail()
    {
        var failure = new IOException("stream close");

        var thrown = Assert.Throws<IOException>(() => DelimitedSourceReader.CloseInOrder(
            [new Recording("provider", []), new Recording("decoder", []), new Recording("stream", [], failure)], primary: null));

        Assert.Same(failure, thrown);
        Assert.False(thrown.Data.Contains(SecondaryKey));
    }

    /// <summary>A disposable that records its disposal in a shared log and can fail there.</summary>
    private sealed class Recording(string name, List<string> log, Exception? failure = null) : IDisposable
    {
        public void Dispose()
        {
            log.Add(name);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    /// <summary>
    /// The raw candidates of the opened stream as the owned chain hands them to the reading rules:
    /// each candidate's fields as untouched text, before blank skipping or decoding.
    /// </summary>
    internal static List<string[]> RawCandidates(Func<Stream> open, char delimiter = ',')
    {
        var candidates = new List<string[]>();
        var reader = DelimitedSourceReader.Open(open, delimiter, CancellationToken.None);
        try
        {
            while (reader.MoveNext())
            {
                var row = reader.Reader.Current;
                var fields = new string[row.ColCount];
                for (var i = 0; i < fields.Length; i++)
                {
                    fields[i] = new string(row[i].Span);
                }

                candidates.Add(fields);
            }
        }
        catch (Exception ex)
        {
            reader.Fail(ex);
            throw;
        }
        finally
        {
            reader.Close();
        }

        return candidates;
    }
}
