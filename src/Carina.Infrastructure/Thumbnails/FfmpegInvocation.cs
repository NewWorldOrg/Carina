using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public static class FfmpegInvocation
{
    public const int FramesLookedAt = 100;

    public static IReadOnlyList<string> Arguments(ThumbnailRequest request, int width)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            .. Reading(request.Source, request.Service, request.At, MostTypicalOf(width)),
            request.Destination,
        ];
    }

    public static IReadOnlyList<string> FrameArguments(ThumbnailFrameRequest request, int width)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            .. Reading(request.Source, request.Service, request.At, Scaled(width)),
            "-f",
            "image2pipe",
            "-c:v",
            "mjpeg",
            "-",
        ];
    }

    private static IReadOnlyList<string> Reading(string source, ServiceId service, TimeSpan at, string filter)
        =>
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
            Selecting(service),
            "-frames:v",
            "1",
            "-vf",
            filter,
        ];

    private static string Selecting(ServiceId service)
    {
        int programNumber = service.Value;

        return string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:v:0");
    }

    private static string MostTypicalOf(int width)
        => string.Create(CultureInfo.InvariantCulture, $"thumbnail={FramesLookedAt},{Scaled(width)}");

    private static string Scaled(int width)
    {
        if (width < 2 || width % 2 is not 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "A picture is at least two pixels wide, and an even number of them.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"scale={width}:trunc({width}/dar/2)*2:flags=bicubic,setsar=1");
    }
}
