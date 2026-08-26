using System.Globalization;

using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public static class FfmpegInvocation
{
    public static IReadOnlyList<string> Arguments(ThumbnailRequest request, int width)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (width < 2 || width % 2 is not 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "A picture is at least two pixels wide, and an even number of them.");
        }

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-ss",
            request.At.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i",
            request.Source,
            "-frames:v",
            "1",
            "-vf",
            Filter(width),
            request.Destination,
        ];
    }

    private static string Filter(int width)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"scale={width}:trunc({width}/dar/2)*2:flags=bicubic,setsar=1");
}
