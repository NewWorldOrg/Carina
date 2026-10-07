using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// The arguments for the run that decodes a recording fed on its standard input into what its
/// learning data is made from, and nothing more: the programme's first picture, decoded at half its
/// size, shrunk to grey 64 by 36 at every frame, and to grey 480 by 270 at the first frame of each
/// second, and its first sound at 8 kHz in two channels of 16-bit samples. All three go into Matroska
/// on the standard output, every block carrying the recording's own time lifted by
/// <see cref="Lift"/>. One thread, and no card.
/// </summary>
public static class FfmpegLearningInvocation
{
    public const string Fed = "pipe:0";

    public const string HandedBack = "pipe:1";

    /// <summary>
    /// How far every time handed back stands above the recording's own time, in whole seconds. It is
    /// more than the 33-bit clock holds, so nothing handed back falls below zero.
    /// </summary>
    public const int ClockLiftedBySeconds = 100_000;

    private const string Microseconds = "0.000000";

    public static readonly TimeSpan Lift = TimeSpan.FromSeconds(ClockLiftedBySeconds);

    /// <summary>
    /// The arguments for a file whose own clock begins at <paramref name="fileBegins"/>, the
    /// <c>format.start_time</c> ffprobe reads from its head.
    /// </summary>
    public static IReadOnlyList<string> Arguments(ServiceId service, TimeSpan fileBegins)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fileBegins, -EncodeTimeline.OneTurnOfTheClock, nameof(fileBegins));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fileBegins, EncodeTimeline.OneTurnOfTheClock, nameof(fileBegins));

        int programNumber = service.Value;

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "warning",
            "-nostats",
            "-threads",
            "1",
            "-filter_threads",
            "1",
            "-filter_complex_threads",
            "1",
            "-copyts",
            "-itsoffset",
            Offset(fileBegins),
            "-lowres:v",
            "1",
            "-i",
            Fed,
            "-filter_complex",
            string.Create(
                CultureInfo.InvariantCulture,
                $"[0:p:{programNumber}:v:0]split=2[frames][seconds];[frames]scale=64:36:flags=area,format=gray[f];[seconds]select=isnan(prev_selected_t)+gte(floor(t)\\,floor(prev_selected_t)+1),scale=480:270:flags=area,format=gray[c];[0:p:{programNumber}:a:0]aresample=8000,aformat=sample_fmts=s16:channel_layouts=stereo[a]"),
            "-map",
            "[f]",
            "-map",
            "[c]",
            "-map",
            "[a]",
            "-c:v",
            "rawvideo",
            "-c:a",
            "pcm_s16le",
            "-fps_mode",
            "passthrough",
            "-f",
            "matroska",
            HandedBack,
        ];
    }

    /// <summary>
    /// Where a time handed back falls on the recording's own time.
    /// </summary>
    public static TimeSpan OnTheRecording(TimeSpan handedBack) => handedBack - Lift;

    private static string Offset(TimeSpan fileBegins)
        => ((decimal)(Lift - fileBegins).Ticks / TimeSpan.TicksPerSecond).ToString(Microseconds, CultureInfo.InvariantCulture);
}
