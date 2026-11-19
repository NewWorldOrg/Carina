using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Segments;

public static class FfmpegLearningInvocation
{
    public const string Fed = "pipe:0";

    public const string HandedBack = "pipe:1";

    public const int ClockLiftedBySeconds = 100_000;

    private const string Microseconds = "0.000000";

    private const string Stereo = "pan=stereo|FL=FL+0.707*FC+0.707*BL+0.707*SL|FR=FR+0.707*FC+0.707*BR+0.707*SR";

    public static readonly TimeSpan Lift = TimeSpan.FromSeconds(ClockLiftedBySeconds);

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
                $"[0:p:{programNumber}:v:0]split=2[frames][seconds];[frames]scale=64:36:flags=area,format=gray[f];[seconds]select=isnan(prev_selected_t)+gte(floor(t)\\,floor(prev_selected_t)+1),scale=480:270:flags=area,format=gray[c];[0:p:{programNumber}:a:0]{Stereo},aresample=8000,aformat=sample_fmts=s16:channel_layouts=stereo[a]"),
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

    public static TimeSpan OnTheRecording(TimeSpan handedBack) => handedBack - Lift;

    private static string Offset(TimeSpan fileBegins)
        => ((decimal)(Lift - fileBegins).Ticks / TimeSpan.TicksPerSecond).ToString(Microseconds, CultureInfo.InvariantCulture);
}
