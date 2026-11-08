using System.Text.RegularExpressions;

using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

/// <summary>
/// Reads what an ffmpeg build was compiled with out of its own listing, and puts that together with
/// what the card was found to accept.
/// </summary>
public static partial class FfmpegFaculties
{
    public const string H264OnTheProcessor = "libx264";

    public const string H265OnTheProcessor = "libx265";

    public const string H264OnVaapi = "h264_vaapi";

    public const string H265OnVaapi = "hevc_vaapi";

    public const string Av1OnVaapi = "av1_vaapi";

    public const string H264OnNvenc = "h264_nvenc";

    public const string H265OnNvenc = "hevc_nvenc";

    public const string Av1OnNvenc = "av1_nvenc";

    public const string AribCaptions = "libaribcaption";

    private const string BelowTheDashes = "------";

    public static IReadOnlyList<string> Listed(string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        List<string> named = [];
        bool below = false;

        foreach (string line in said.Split('\n'))
        {
            string trimmed = line.Trim('\r', ' ', '\t');

            if (!below)
            {
                below = string.Equals(trimmed, BelowTheDashes, StringComparison.Ordinal);

                continue;
            }

            if (Entry().Match(line) is { Success: true } entry)
            {
                named.Add(entry.Groups["name"].Value);
            }
        }

        return named;
    }

    public static string H264On(CardKind card) => CardKinds.Named(card) is CardKind.Nvenc ? H264OnNvenc : H264OnVaapi;

    public static string H265On(CardKind card) => CardKinds.Named(card) is CardKind.Nvenc ? H265OnNvenc : H265OnVaapi;

    public static string Av1On(CardKind card) => CardKinds.Named(card) is CardKind.Nvenc ? Av1OnNvenc : Av1OnVaapi;

    /// <summary>
    /// The faculties of this machine, whose card is of <paramref name="card"/>'s kind. Each of the card's three
    /// faculties is answered by a frame actually encoded with that card's encoder, not by the build listing it.
    /// </summary>
    public static IReadOnlyList<Faculty> Of(
        IReadOnlyList<string> encoders,
        IReadOnlyList<string> decoders,
        CardKind card,
        bool cardEncodesH264,
        bool cardEncodesH265,
        bool cardEncodesAv1)
    {
        ArgumentNullException.ThrowIfNull(encoders);
        ArgumentNullException.ThrowIfNull(decoders);

        List<Faculty> can = [];

        Add(can, Faculty.EncodeH264OnTheProcessor, encoders.Contains(H264OnTheProcessor, StringComparer.Ordinal));
        Add(can, Faculty.EncodeH265OnTheProcessor, encoders.Contains(H265OnTheProcessor, StringComparer.Ordinal));
        Add(can, Faculty.EncodeH264OnTheCard, cardEncodesH264 && encoders.Contains(H264On(card), StringComparer.Ordinal));
        Add(can, Faculty.EncodeH265OnTheCard, cardEncodesH265 && encoders.Contains(H265On(card), StringComparer.Ordinal));
        Add(can, Faculty.DecodeAribCaptions, decoders.Contains(AribCaptions, StringComparer.Ordinal));
        Add(can, Faculty.EncodeAv1OnTheCard, cardEncodesAv1 && encoders.Contains(Av1On(card), StringComparer.Ordinal));

        return can;
    }

    private static void Add(List<Faculty> can, Faculty faculty, bool it)
    {
        if (it)
        {
            can.Add(faculty);
        }
    }

    [GeneratedRegex(@"^\s[A-Z.]{6}\s+(?<name>\S+)", RegexOptions.None, 5000)]
    private static partial Regex Entry();
}
