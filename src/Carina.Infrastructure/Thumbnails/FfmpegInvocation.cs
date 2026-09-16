using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public static class FfmpegInvocation
{
    public const int FramesLookedAt = 100;

    public const int BestPictureMjpegDraws = 2;

    public const int CoarsestPictureMjpegDraws = 31;

    public static IReadOnlyList<string> Arguments(ThumbnailRequest request, int width, int stepsFromPerfect)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            .. Reading(request.Source, request.Service, request.At, MostTypicalOf(width), stepsFromPerfect),
            request.Destination,
        ];
    }

    public static IReadOnlyList<string> FrameArguments(ThumbnailFrameRequest request, int width, int stepsFromPerfect)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            .. Reading(request.Source, request.Service, request.At, Scaled(width), stepsFromPerfect),
            "-f",
            "image2pipe",
            "-c:v",
            "mjpeg",
            "-",
        ];
    }

    private static IReadOnlyList<string> Reading(
        string source,
        ServiceId service,
        TimeSpan at,
        string filter,
        int stepsFromPerfect)
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
            "-q:v",
            Steps(stepsFromPerfect),
        ];

    private static string Steps(int stepsFromPerfect)
    {
        if (stepsFromPerfect < BestPictureMjpegDraws || stepsFromPerfect > CoarsestPictureMjpegDraws)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepsFromPerfect),
                stepsFromPerfect,
                "A picture is drawn between two steps from the best mjpeg can do and thirty-one.");
        }

        return stepsFromPerfect.ToString(CultureInfo.InvariantCulture);
    }

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
