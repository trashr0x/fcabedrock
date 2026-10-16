using System.Text;

namespace FcaBedrock.Cli.Tests;

/// <summary>
/// The input explanation at its own seam: each established cause for each input file, the decoding
/// causes only where the spec text decoder owns them, and the generic sentence for everything else.
/// No exception's own message ever reaches the result.
/// </summary>
public sealed class InputReadExplanationTests
{
    public static TheoryData<string, Exception, string> Explained() => new()
    {
        { nameof(InputFile.Spec), new FileNotFoundException("SECRET"), "cannot read the spec file 'in': it does not exist. Check the path." },
        { nameof(InputFile.Spec), new DirectoryNotFoundException("SECRET"), "cannot read the spec file 'in': it does not exist. Check the path." },
        { nameof(InputFile.Spec), new UnauthorizedAccessException("SECRET"), "cannot read the spec file 'in': access to it was denied. Check that it is a file you can read." },
        { nameof(InputFile.Spec), new InvalidDataException("SECRET"), "cannot read the spec file 'in': it starts with a UTF-16 or UTF-32 byte order mark, but a spec must be UTF-8. Save it as UTF-8." },
        { nameof(InputFile.Spec), new DecoderFallbackException("SECRET"), "cannot read the spec file 'in': it is not valid UTF-8. Save it as UTF-8." },
        { nameof(InputFile.Bed), new FileNotFoundException("SECRET"), "cannot read the .bed file 'in': it does not exist. Check the path." },
        { nameof(InputFile.Bed), new UnauthorizedAccessException("SECRET"), "cannot read the .bed file 'in': access to it was denied. Check that it is a file you can read." },
        { nameof(InputFile.Bed), new InvalidDataException("SECRET"), "cannot read the .bed file 'in': it starts with a UTF-16 or UTF-32 byte order mark, but a .bed file must be UTF-8. Save it as UTF-8." },
        { nameof(InputFile.Bed), new DecoderFallbackException("SECRET"), "cannot read the .bed file 'in': it is not valid UTF-8. Save it as UTF-8." },
        { nameof(InputFile.Data), new DirectoryNotFoundException("SECRET"), "cannot read the data file 'in': it does not exist. Check the path." },
        { nameof(InputFile.Data), new UnauthorizedAccessException("SECRET"), "cannot read the data file 'in': access to it was denied. Check that it is a file you can read." },
    };

    [Theory]
    [MemberData(nameof(Explained))]
    public void Message_WhenTheFailedOperationEstablishedTheCause_ThenItSaysWhyAndWhatToDo(string file, Exception cause, string expected) =>
        Assert.Equal(expected, InputReadExplanation.Message(Enum.Parse<InputFile>(file), "in", cause));

    public static TheoryData<string, Exception, string> Generic() => new()
    {
        { nameof(InputFile.Spec), new IOException("SECRET"), "cannot read the spec file 'in'." },
        { nameof(InputFile.Spec), new PathTooLongException("SECRET"), "cannot read the spec file 'in'." },
        { nameof(InputFile.Spec), new ObjectDisposedException("SECRET"), "cannot read the spec file 'in'." },
        { nameof(InputFile.Spec), new ArgumentException("SECRET"), "cannot read the spec file 'in'." },
        { nameof(InputFile.Bed), new NotSupportedException("SECRET"), "cannot read the .bed file 'in'." },

        // DATA decoding belongs to the data explanation, so these two are not the spec text rules.
        { nameof(InputFile.Data), new InvalidDataException("SECRET"), "cannot read the data file 'in'." },
        { nameof(InputFile.Data), new DecoderFallbackException("SECRET"), "cannot read the data file 'in'." },
    };

    [Theory]
    [MemberData(nameof(Generic))]
    public void Message_WhenNoCauseIsEstablished_ThenItIsTheGenericSentenceAndDisclosesNothing(string file, Exception cause, string expected)
    {
        var message = InputReadExplanation.Message(Enum.Parse<InputFile>(file), "in", cause);

        Assert.Equal(expected, message);
        Assert.DoesNotContain("SECRET", message, StringComparison.Ordinal);
    }
}
