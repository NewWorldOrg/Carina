using System.Text.RegularExpressions;

using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

/// <summary>
/// Reads what an ffmpeg build was compiled with out of its own listing. A name in the listing is
/// a property of the build alone: the card being listed is not the card being reachable, so the
/// two are put together here and nowhere else.
/// </summary>
public static partial class FfmpegFaculties
{
    public const string H264OnTheProcessor = "libx264";

    public const string H265OnTheProcessor = "libx265";

    public const string H264OnTheCard = "h264_vaapi";

    public const string H265OnTheCard = "hevc_vaapi";

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

    /// <summary>
    /// The card's two faculties are each answered by a frame actually encoded with that encoder:
    /// the build listing <c>hevc_vaapi</c> says nothing about the driver behind the node, which on
    /// one measured machine encodes H.264 and has no entrypoint for H.265 at all.
    /// </summary>
    public static IReadOnlyList<Faculty> Of(
        IReadOnlyList<string> encoders,
        IReadOnlyList<string> decoders,
        bool cardEncodesH264,
        bool cardEncodesH265)
    {
        ArgumentNullException.ThrowIfNull(encoders);
        ArgumentNullException.ThrowIfNull(decoders);

        List<Faculty> can = [];

        Add(can, Faculty.EncodeH264OnTheProcessor, encoders.Contains(H264OnTheProcessor, StringComparer.Ordinal));
        Add(can, Faculty.EncodeH265OnTheProcessor, encoders.Contains(H265OnTheProcessor, StringComparer.Ordinal));
        Add(can, Faculty.EncodeH264OnTheCard, cardEncodesH264 && encoders.Contains(H264OnTheCard, StringComparer.Ordinal));
        Add(can, Faculty.EncodeH265OnTheCard, cardEncodesH265 && encoders.Contains(H265OnTheCard, StringComparer.Ordinal));
        Add(can, Faculty.DecodeAribCaptions, decoders.Contains(AribCaptions, StringComparer.Ordinal));

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
