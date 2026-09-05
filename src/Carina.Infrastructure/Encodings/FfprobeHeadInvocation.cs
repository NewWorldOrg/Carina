using Carina.Domain.Channels;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Asks ffprobe where a source begins and for the timestamps of the pictures it can decode from the
/// head of the very video stream the run maps, reading a little past the longest head skip a run
/// accepts, so a first picture just beyond it is read and refused with its number rather than not
/// read at all. Like the other probes it fills nothing into the command line itself: the stream is
/// named by the encode invocation's own mapping and handed in whole.
/// </summary>
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
