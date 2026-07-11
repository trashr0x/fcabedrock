using FcaBedrock.Core.Planning;

namespace FcaBedrock.Export.Tests;

// The .cxt object-name sequence invariant (§18.1, D-082): pass 2 must replay the same object-name
// sequence (count and order) as pass 1; a divergent replay fails the write rather than misaligning the
// header names and the incidence rows.
public sealed class CxtWriterNameSequenceTests
{
    [Fact]
    public async Task WriteAsync_WhenReplayReordersNames_ThenThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteWithReplays([Obj("o0"), Obj("o1")], [Obj("o1"), Obj("o0")]));
    }

    [Fact]
    public async Task WriteAsync_WhenReplayHasFewerNames_ThenThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteWithReplays([Obj("o0"), Obj("o1")], [Obj("o0")]));
    }

    [Fact]
    public async Task WriteAsync_WhenReplayHasMoreNames_ThenThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteWithReplays([Obj("o0")], [Obj("o0"), Obj("o1")]));
    }

    [Fact]
    public async Task WriteAsync_WhenReplayIsIdentical_ThenSucceeds()
    {
        using var output = new MemoryStream();
        await WriteWithReplays(output, [Obj("o0"), Obj("o1")], [Obj("o0"), Obj("o1")]);
        Assert.True(output.Length > 0);
    }

    private static Task WriteWithReplays(EmittedObject[] pass1, EmittedObject[] pass2) =>
        WriteWithReplays(new MemoryStream(), pass1, pass2);

    private static async Task WriteWithReplays(Stream output, EmittedObject[] pass1, EmittedObject[] pass2)
    {
        var call = 0;
        await CxtWriter.WriteAsync(
            WriterFixtures.TwoColumnPlan(),
            () => ToAsync(call++ == 0 ? pass1 : pass2),
            WriterOptions.Native,
            output);
    }

    private static EmittedObject Obj(string name) => new(name, []);

    private static async IAsyncEnumerable<EmittedObject> ToAsync(EmittedObject[] objects)
    {
        foreach (var obj in objects)
        {
            yield return obj;
        }

        await Task.CompletedTask;
    }
}
