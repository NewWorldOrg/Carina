using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public static class FfmpegInvocation
{
    public static IReadOnlyList<string> Arguments(ThumbnailRequest request, int width)
    {
        ArgumentNullException.ThrowIfNull(request);

        return [.. Reading(request.Source, request.Service, request.At, width), request.Destination];
    }

    private static IReadOnlyList<string> Reading(string source, ServiceId service, TimeSpan at, int width)
    {
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
            at.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i",
            source,
            "-map",
            Programme(service),
            "-frames:v",
            "1",
            "-vf",
            Filter(width),
        ];
    }

    private static string Programme(ServiceId service)
        => string.Create(CultureInfo.InvariantCulture, $"p:{service.Value}:v:0");

    private static string Filter(int width)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"scale={width}:trunc({width}/dar/2)*2:flags=bicubic,setsar=1");
}
