using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Draws the captions of one service out of a recorded file with the decoding and the caption output live
/// viewing builds in <see cref="FfmpegLiveInvocation"/>, with no picture decoded, on the file's own clock
/// lifted by <see cref="ClockLiftedBySeconds"/>, onto standard output.
/// </summary>
public static class FfmpegCaptionInvocation
{
    /// <summary>
    /// How far a recorded file's own timestamps are lifted before they are written, in whole seconds so the
    /// lift is exact in microseconds and in 90 kHz ticks alike. It is more than the 33-bit clock holds, so a
    /// timestamp ffmpeg reads as negative before the clock came around is still written as it was read.
    /// </summary>
    public const int ClockLiftedBySeconds = 100_000;

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
            .. FfmpegLiveInvocation.CaptionOutput(service, FfmpegLiveInvocation.Output),
        ];
    }
}
