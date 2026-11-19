using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegCaptionInvocation
{
    public const int ClockLiftedBySeconds = 100_000;

    public const string CarriedTag = "arib";

    private static readonly string ClockLift = ClockLiftedBySeconds.ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> Arguments(ServiceId service, StreamAttributes attributes, StreamSource source)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-copyts",
            .. FfmpegLiveInvocation.Decoding(attributes, CaptionOutlet.Drawn),
            "-i",
            source.Value,
            "-output_ts_offset",
            ClockLift,
            .. FfmpegLiveInvocation.CaptionOutput(service, FfmpegLiveInvocation.Output, Carried(service)),
        ];
    }

    public static IReadOnlyList<string> Carried(ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(service);

        int programNumber = service.Value;

        return
        [
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"0:p:{programNumber}:s:0"),
            "-c:s",
            "copy",
            "-tag:s",
            CarriedTag,
        ];
    }
}
