using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegCaptionInvocation
{
    public const string Input = "pipe:0";

    public const string Output = "pipe:1";

    public const string Drawn = "[c]";

    public static IReadOnlyList<string> Arguments(ServiceId service, VideoSize canvas)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(canvas);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "info",
            "-nostats",
            "-copyts",
            "-sub_type",
            "bitmap",
            "-canvas_size",
            Canvas(canvas),
            "-i",
            Input,
            "-filter_complex",
            Drawing(service),
            "-map",
            Drawn,
            "-fps_mode",
            "passthrough",
        ];
    }

    public static IReadOnlyList<string> Delivery()
        =>
        [
            "-flush_packets",
            "1",
            "-f",
            "rawvideo",
            Output,
        ];

    internal static string Canvas(VideoSize size)
        => string.Create(CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}");

    internal static string Drawing(ServiceId service)
    {
        int programNumber = service.Value;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[0:p:{programNumber}:s:0]format=bgra,showinfo[c]");
    }
}
