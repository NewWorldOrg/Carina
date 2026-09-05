using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

public static class FfmpegEncodeInvocation
{
    public const string RenderNode = MachineSettings.TheRenderNode;

    private const string SquarePixels = "setsar=1";

    private const string FullHd = "scale=1920:1080:flags=bicubic";

    private const string Hd = "scale=1280:720:flags=bicubic";

    private const string EveryFrame = "bwdif=mode=send_frame";

    private const string EveryField = "bwdif=mode=send_field";

    private const string OntoTheCard = "format=nv12";

    public static IReadOnlyList<string> Arguments(
        ServiceId service,
        EncodeProfile profile,
        EncodeEncoder encoder,
        string source)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostats",
            "-progress",
            "pipe:1",
            "-y",
            .. Device(encoder),
            "-i",
            source,
            .. Mapping(service),
            "-vf",
            Filter(profile, encoder),
            .. Encoding(profile, encoder),
            "-c:a",
            "copy",
            "-bsf:a",
            "aac_adtstoasc",
        ];
    }

    public static IReadOnlyList<string> Delivery(string destination)
    {
        ArgumentException.ThrowIfNullOrEmpty(destination);

        return
        [
            "-f",
            "mp4",
            "-movflags",
            "faststart",
            destination,
        ];
    }

    internal static IReadOnlyList<string> Device(EncodeEncoder encoder)
        => EncodeShapes.Named(encoder) is EncodeEncoder.Vaapi ? ["-vaapi_device", RenderNode] : [];

    internal static IReadOnlyList<string> Mapping(ServiceId service)
    {
        int programNumber = service.Value;

        return
        [
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:v:0"),
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:a:0"),
        ];
    }

    internal static string Filter(EncodeProfile profile, EncodeEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(profile);

        List<string> steps = [];

        if (Undoing(profile.Deinterlace) is { } undone)
        {
            steps.Add(undone);
        }

        if (Scaling(profile.Resolution) is { } scaled)
        {
            steps.Add(scaled);
        }

        steps.Add(SquarePixels);

        if (EncodeShapes.Named(encoder) is EncodeEncoder.Vaapi)
        {
            steps.Add(OntoTheCard);
            steps.Add("hwupload");
        }

        return string.Join(',', steps);
    }

    internal static IReadOnlyList<string> Encoding(EncodeProfile profile, EncodeEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return EncodeShapes.Named(encoder) is EncodeEncoder.Vaapi
            ?
            [
                "-c:v",
                OnTheCard(profile.Codec),
                "-rc_mode",
                "CQP",
                "-qp",
                profile.VaapiRateControl.Quantiser.ToString(CultureInfo.InvariantCulture),
            ]
            :
            [
                "-c:v",
                OnTheProcessor(profile.Codec),
                "-preset",
                "medium",
                "-crf",
                profile.SoftwareRateControl.RateFactor.ToString(CultureInfo.InvariantCulture),
            ];
    }

    private static string OnTheProcessor(EncodeCodec codec)
        => EncodeShapes.Named(codec) is EncodeCodec.H265 ? "libx265" : "libx264";

    private static string OnTheCard(EncodeCodec codec)
        => EncodeShapes.Named(codec) is EncodeCodec.H265 ? "hevc_vaapi" : "h264_vaapi";

    private static string? Undoing(Deinterlace deinterlace)
        => EncodeShapes.Named(deinterlace) switch
        {
            Deinterlace.EveryFrame => EveryFrame,
            Deinterlace.EveryField => EveryField,
            _ => null,
        };

    private static string? Scaling(EncodeResolution resolution)
        => EncodeShapes.Named(resolution) switch
        {
            EncodeResolution.FullHd => FullHd,
            EncodeResolution.Hd => Hd,
            _ => null,
        };
}
