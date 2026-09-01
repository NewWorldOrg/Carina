using System.Globalization;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegLiveInvocation
{
    public const string Input = "pipe:0";

    public const string Output = "pipe:1";

    public const string RenderNode = "/dev/dri/renderD128";

    private const int KeyframeSeconds = 2;

    private const int BufferedSeconds = 2;

    public static IReadOnlyList<string> Arguments(
        LiveProfile profile,
        StreamAttributes attributes,
        LiveEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);

        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by one of the two the benchmark compared.");
        }

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-fflags",
            "nobuffer",
            "-flags",
            "low_delay",
            "-copyts",
            .. Device(encoder),
            "-i",
            Input,
            "-vf",
            Filter(profile, attributes, encoder),
            .. Encoding(profile, encoder),
            "-c:a",
            "copy",
            "-bsf:a",
            "aac_adtstoasc",
        ];
    }

    public static IReadOnlyList<string> Delivery()
        =>
        [
            "-f",
            "mp4",
            "-movflags",
            "empty_moov+default_base_moof",
            Output,
        ];

    internal static IReadOnlyList<string> Device(LiveEncoder encoder)
        => encoder is LiveEncoder.Vaapi ? ["-vaapi_device", RenderNode] : [];

    internal static string Filter(LiveProfile profile, StreamAttributes attributes, LiveEncoder encoder)
    {
        List<string> steps = [];

        if (attributes.Scan is not ScanType.Progressive)
        {
            steps.Add(WantsEveryField(profile) ? "bwdif=mode=send_field" : "bwdif=mode=send_frame");
        }

        steps.Add(Scaling(profile.Size));
        steps.Add("setsar=1");

        if (encoder is LiveEncoder.Vaapi)
        {
            steps.Add("format=nv12");
            steps.Add("hwupload");
        }

        return string.Join(',', steps);
    }

    internal static IReadOnlyList<string> Encoding(LiveProfile profile, LiveEncoder encoder)
        => encoder is LiveEncoder.Vaapi
            ?
            [
                "-c:v",
                "h264_vaapi",
                "-g",
                Keyframes(profile),
                "-rc_mode",
                "CQP",
                "-qp",
                profile.VaapiRateControl.Quantiser.ToString(CultureInfo.InvariantCulture),
            ]
            :
            [
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-tune",
                "zerolatency",
                "-g",
                Keyframes(profile),
                "-b:v",
                Kilobits(profile.SoftwareRateControl.KilobitsPerSecond),
                "-maxrate",
                Kilobits(profile.SoftwareRateControl.KilobitsPerSecond),
                "-bufsize",
                Kilobits(profile.SoftwareRateControl.KilobitsPerSecond * BufferedSeconds),
            ];

    private static bool WantsEveryField(LiveProfile profile)
        => profile.Rate.PerSecond > FrameRate.BroadcastFrames.PerSecond;

    private static string Scaling(VideoSize size)
        => string.Create(CultureInfo.InvariantCulture, $"scale={size.Width}:{size.Height}:flags=bicubic");

    private static string Kilobits(int kilobitsPerSecond)
        => string.Create(CultureInfo.InvariantCulture, $"{kilobitsPerSecond}k");

    private static string Keyframes(LiveProfile profile)
        => Math.Round(profile.Rate.PerSecond * KeyframeSeconds).ToString(CultureInfo.InvariantCulture);
}
