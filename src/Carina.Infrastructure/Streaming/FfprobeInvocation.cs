using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfprobeInvocation
{
    public const string Format = "default=nw=1";

    public const string Entries =
        "stream=codec_type,codec_name,width,height,field_order,r_frame_rate,channels,channel_layout";

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
}
