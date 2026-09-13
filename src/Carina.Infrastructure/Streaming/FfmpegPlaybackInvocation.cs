using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegPlaybackInvocation
{
    public const string Seconds = "0.###";

    public const string TheLeftChannelInBothEars = "pan=stereo|c0=c0|c1=c0";

    public const string TheRightChannelInBothEars = "pan=stereo|c0=c1|c1=c1";

    public static IReadOnlyList<string> Arguments(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
        LiveEncoder encoder,
        StreamSource source,
        TimeSpan from,
        SoundPlacement sound)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sound);
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
            .. Mapping(service, sound),
            "-vf",
            FfmpegLiveInvocation.Filter(profile, attributes, encoder),
            .. FfmpegLiveInvocation.Encoding(profile, encoder),
            .. FfmpegLiveInvocation.Sound(),
            .. Panning(sound),
        ];
    }

    internal static IReadOnlyList<string> Mapping(ServiceId service, SoundPlacement sound)
    {
        int programNumber = service.Value;
        int ordinal = sound.Ordinal;

        return
        [
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:v:0"),
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:a:{ordinal}"),
        ];
    }

    internal static IReadOnlyList<string> Panning(SoundPlacement sound)
        => sound.Channel switch
        {
            SoundChannel.Left => ["-af", TheLeftChannelInBothEars],
            SoundChannel.Right => ["-af", TheRightChannelInBothEars],
            _ => [],
        };
}
