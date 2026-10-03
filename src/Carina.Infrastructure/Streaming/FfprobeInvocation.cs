using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfprobeInvocation
{
    public const string Format = "default=nw=1";

    public const string Entries =
        "stream=codec_type,codec_name,width,height,field_order,r_frame_rate,channels,channel_layout";

    public const string AsJson = "json";

    public const string ProgrammeEntries = "program=program_id:stream=codec_type";

    public const string StartEntries = "format=start_time";

    public static IReadOnlyList<string> Arguments(StreamSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            Format,
            "-show_entries",
            Entries,
            "-i",
            source.Value,
        ];
    }

    /// <summary>
    /// Where the file's own clock begins, as ffmpeg reads it: negative when the file begins shortly before
    /// the 33-bit clock comes around.
    /// </summary>
    public static IReadOnlyList<string> Start(StreamSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            Format,
            "-show_entries",
            StartEntries,
            "-i",
            source.Value,
        ];
    }

    public static IReadOnlyList<string> Programmes(StreamSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-of",
            AsJson,
            "-show_programs",
            "-show_entries",
            ProgrammeEntries,
            "-i",
            source.Value,
        ];
    }
}
