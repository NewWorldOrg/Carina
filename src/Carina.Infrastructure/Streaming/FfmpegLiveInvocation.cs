using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegLiveInvocation
{
    public const string Input = "pipe:0";

    public const string Output = "pipe:1";

    public const string RenderNode = MachineSettings.TheRenderNode;

    public const string Font = "Noto Sans CJK JP";

    public const string DrawnCaptions = "[c]";

    private const int KeyframeSeconds = 2;

    private const string LightestCompression = "1";

    private const int BufferedSeconds = 2;

    private const string FragmentMicroseconds = "200000";

    public static IReadOnlyList<string> Arguments(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
        LiveEncoder encoder,
        CaptionOutlet captions)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);

        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by one of the two the benchmark compared.");
        }

        if (!Enum.IsDefined(captions))
        {
            throw new ArgumentOutOfRangeException(
                nameof(captions),
                captions,
                "The captions are either drawn beside the picture or left out.");
        }

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-fflags",
            "nobuffer",
            "-copyts",
            .. Device(encoder),
            .. Decoding(attributes, captions),
            "-i",
            Input,
            .. Mapping(service),
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
            "empty_moov+default_base_moof+delay_moov+frag_discont",
            "-frag_duration",
            FragmentMicroseconds,
            Output,
        ];

    public static IReadOnlyList<string> CaptionDelivery(ServiceId service, int descriptor)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfLessThan(descriptor, 3);

        return
        [
            "-filter_complex",
            Drawing(service),
            "-map",
            DrawnCaptions,
            "-fps_mode",
            "passthrough",
            "-c:v",
            "png",
            "-pix_fmt",
            "rgba",
            "-compression_level",
            LightestCompression,
            "-threads",
            "1",
            "-flush_packets",
            "1",
            "-f",
            "nut",
            Pipe(descriptor),
        ];
    }

    public static IReadOnlyList<string> DeliveryFromTheStart()
        =>
        [
            "-f",
            "mp4",
            "-movflags",
            "empty_moov+default_base_moof+delay_moov",
            "-frag_duration",
            FragmentMicroseconds,
            Output,
        ];

    internal static IReadOnlyList<string> Device(LiveEncoder encoder)
        => encoder is LiveEncoder.Vaapi ? ["-vaapi_device", RenderNode] : [];

    internal static IReadOnlyList<string> Decoding(StreamAttributes attributes, CaptionOutlet captions)
        => captions is CaptionOutlet.Drawn
            ?
            [
                "-sub_type",
                "bitmap",
                "-canvas_size",
                Canvas(attributes.Size),
                "-font",
                Font,
            ]
            : [];

    internal static string Canvas(VideoSize size)
        => string.Create(CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}");

    internal static string Drawing(ServiceId service)
    {
        int programNumber = service.Value;

        return string.Create(CultureInfo.InvariantCulture, $"[0:p:{programNumber}:s:0]null[c]");
    }

    internal static string Pipe(int descriptor)
        => string.Create(CultureInfo.InvariantCulture, $"pipe:{descriptor}");

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
