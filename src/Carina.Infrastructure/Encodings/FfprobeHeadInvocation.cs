using Carina.Domain.Channels;

namespace Carina.Infrastructure.Encodings;

public static class FfprobeHeadInvocation
{
    public const string Format = FfprobeLengthInvocation.Format;

    public const string Entries = "format=start_time:frame=best_effort_timestamp_time";

    public const string ReadInterval = "%+6";

    public static readonly TimeSpan ReadFor = TimeSpan.FromSeconds(6);

    public static IReadOnlyList<string> Arguments(string source, ServiceId service)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            Format,
            "-select_streams",
            FfmpegEncodeInvocation.VideoStream(service),
            "-show_entries",
            Entries,
            "-read_intervals",
            ReadInterval,
            "-i",
            source,
        ];
    }
}
