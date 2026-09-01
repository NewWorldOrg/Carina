using System.Globalization;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegPlaybackInvocation
{
    public const string Seconds = "0.###";

    public static IReadOnlyList<string> Arguments(
        LiveProfile profile,
        StreamAttributes attributes,
        LiveEncoder encoder,
        StreamSource source,
        TimeSpan from)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by one of the two the benchmark compared.");
        }

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            .. FfmpegLiveInvocation.Device(encoder),
            "-ss",
            from.TotalSeconds.ToString(Seconds, CultureInfo.InvariantCulture),
            "-i",
            source.Value,
            "-sn",
            "-dn",
            "-vf",
            FfmpegLiveInvocation.Filter(profile, attributes, encoder),
            .. FfmpegLiveInvocation.Encoding(profile, encoder),
            "-c:a",
            "copy",
            "-bsf:a",
            "aac_adtstoasc",
        ];
    }
}
