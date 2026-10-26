using FcaBedrock.Core.Discretization;
using FcaBedrock.Core.Fingerprinting;
using FcaBedrock.Core.Spec;
using FcaBedrock.Export;
using FcaBedrock.Spec.Toml;

namespace FcaBedrock.Orchestration;

/// <summary>
/// The resolved §8 <c>[output]</c> settings a conversion writes with, and the matching §14
/// fingerprint inputs.
/// <para>
/// <b>One owner for the defaults, one owner for the override.</b> The §8/§21 defaults are applied
/// here (<c>lf</c>, trailing newline, <c>base_index = 1</c>, no trailing spaces,
/// <c>bin_label_unicode = false</c>, a 1 GiB <c>.cxt</c> size advisory), and <c>--v2-compat</c>,
/// the sole settled conversion override (D-011), is applied here too, as a whole replacement of
/// the writer settings and the operator presentation rather than a scattering of per-knob
/// conditions.
/// </para>
/// <para>
/// <b>The fingerprint inputs are read back off the very options the writers receive</b>, so
/// "the manifest records the effective values" (§15) is structural: there is no second place a
/// line ending or a base index could be decided differently from the bytes.
/// </para>
/// </summary>
internal sealed record OutputSettings
{
    /// <summary>The §8 default <c>.cxt</c> size advisory: 1 GiB.</summary>
    internal const long DefaultCxtSizeAdvisoryBytes = 1_073_741_824;

    private OutputSettings()
    {
    }

    /// <summary>The <c>.cxt</c> writer settings.</summary>
    public required WriterOptions Cxt { get; init; }

    /// <summary>The <c>.dat</c> writer settings.</summary>
    public required WriterOptions Dat { get; init; }

    /// <summary>
    /// The <c>.cxt</c> size-advisory threshold in bytes: a positive value warns at or above it,
    /// exactly <c>0</c> disables it, and a negative value is invalid configuration the caller
    /// refuses (it is not a fingerprint input either way, D-077).
    /// </summary>
    public required long CxtSizeAdvisoryBytes { get; init; }

    /// <summary>
    /// The resolved <c>bin_label_unicode</c> flag: the presentation the plan renders names with
    /// and the matching <c>.cxt</c> fingerprint input. No writer consults it. Natively it is the
    /// authored value; <see cref="ToV2Compat"/> makes it false, because v2 compatibility renders
    /// ASCII operators (§8).
    /// </summary>
    public required bool BinLabelUnicode { get; init; }

    /// <summary>The native settings authored by <paramref name="document"/>'s <c>[output]</c>.</summary>
    public static OutputSettings Native(SpecDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var output = document.Output;
        return new OutputSettings
        {
            Cxt = new WriterOptions
            {
                LineEnding = Ending(output?.Cxt?.LineEndings ?? LineEndings.Lf),
                TrailingNewline = output?.Cxt?.TrailingNewline ?? true,
            },
            Dat = new WriterOptions
            {
                LineEnding = Ending(output?.Dat?.LineEndings ?? LineEndings.Lf),
                TrailingNewline = output?.Dat?.TrailingNewline ?? true,
                BaseIndex = output?.Dat?.BaseIndex ?? 1,
                NonemptyLineTrailingSpace = output?.Dat?.NonemptyLineTrailingSpace ?? false,
                EmptyLineTrailingSpace = output?.Dat?.EmptyLineTrailingSpace ?? false,
            },
            CxtSizeAdvisoryBytes = output?.Cxt?.SizeAdvisoryBytes ?? DefaultCxtSizeAdvisoryBytes,
            BinLabelUnicode = output?.BinLabelUnicode ?? false,
        };
    }

    /// <summary>
    /// The complete v2 byte convention (§8/D-011), superseding every authored writer setting:
    /// CRLF in both formats, one-based <c>.dat</c> ids with a trailing space on every non-empty
    /// line, and the D-087 shape-dependent <c>.dat</c> final newline: present for a wide source,
    /// absent for a triple one, because v2's triple converter wrote none.
    /// <para>
    /// <c>bin_label_unicode</c> becomes false: v2 compatibility renders ASCII operators, so the
    /// effective plan and the effective <c>.cxt</c> fingerprint input both describe ASCII names
    /// whatever the spec authors (§8/§14). The size advisory is untouched: it is not a v2 byte
    /// convention, and it changes a warning rather than a byte.
    /// </para>
    /// </summary>
    public OutputSettings ToV2Compat(SourceShape shape) => this with
    {
        Cxt = WriterOptions.V2Compat,
        Dat = WriterOptions.V2Compat with { TrailingNewline = shape != SourceShape.Triple },
        BinLabelUnicode = false,
    };

    /// <summary>
    /// The §14 <c>.cxt</c> fingerprint inputs for a plan rendered with <paramref name="style"/> and
    /// planned with <see cref="BinLabelUnicode"/>.
    /// </summary>
    public CxtFingerprintInputs CxtFingerprint(LabelStyle style) =>
        new(style, BinLabelUnicode, Ending(Cxt.LineEnding), Cxt.TrailingNewline);

    /// <summary>The §14 <c>.dat</c> fingerprint inputs.</summary>
    public DatFingerprintInputs DatFingerprint() =>
        new(Dat.BaseIndex, Ending(Dat.LineEnding), Dat.NonemptyLineTrailingSpace, Dat.EmptyLineTrailingSpace)
        {
            TrailingNewline = Dat.TrailingNewline,
        };

    private static string Ending(LineEndings lineEndings) => lineEndings switch
    {
        LineEndings.Lf => "\n",
        LineEndings.Crlf => "\r\n",
        _ => throw new InvalidOperationException($"Unknown line-ending kind {lineEndings}."),
    };

    private static LineEnding Ending(string lineEnding) => lineEnding switch
    {
        "\n" => LineEnding.Lf,
        "\r\n" => LineEnding.Crlf,
        _ => throw new InvalidOperationException("Unknown line ending on the resolved writer options."),
    };
}
