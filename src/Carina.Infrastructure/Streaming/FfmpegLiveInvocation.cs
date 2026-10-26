using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfmpegLiveInvocation
{
    public const string Input = "pipe:0";

    public const string Output = "pipe:1";

    /// <summary>
    /// The faces the captions are drawn with, in the order the decoder tries them: Denpa Font, which keeps
    /// every character of a caption to half or the whole of its em, and Noto for what it does not have.
    /// </summary>
    public const string Font = "Denpa Font,Noto Sans CJK JP";

    public const string DrawnCaptions = "[c]";

    public const int SoundKilobitsPerSecond = 192;

    private const int KeyframeSeconds = 2;

    private const string LightestCompression = "1";

    private const int BufferedSeconds = 2;

    private const string LiveFragmentMicroseconds = "50000";

    private const string RecordedFragmentMicroseconds = "200000";

    /// <summary>
    /// How much of the broadcast is read before decoding starts. Every service's map is repeated at least every
    /// tenth of a second, so a quarter of a second holds the map of the service asked for after the first map of
    /// any service in the multiplex.
    /// </summary>
    private const string ProbedMicroseconds = "250000";

    /// <summary>
    /// The most that is read before decoding starts. ffmpeg ends the quarter of a second only once one stream has
    /// told it that much of its own length, and the data streams of the other services can take several times as
    /// long, so this is what usually ends the reading. It still reaches past the service's map in a multiplex sent
    /// at twice the terrestrial rate.
    /// </summary>
    private const string ProbedBytes = "500000";

    private static readonly SoundPlacement TheWholeFirstSound = SoundPlacement.WholeStream(0);

    public static IReadOnlyList<string> Arguments(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
        LiveEncoder encoder,
        MachineSettings machine,
        CaptionOutlet captions,
        SoundPlacement? sound = null,
        VideoCodec format = VideoCodecs.Unasked)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentException.ThrowIfNullOrEmpty(machine.RenderNode);

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

        SoundPlacement taken = sound ?? TheWholeFirstSound;

        return
        [
            "-nostdin",
            "-hide_banner",
            "-nostats",
            "-loglevel",
            "level+info",
            "-probesize",
            ProbedBytes,
            "-analyzeduration",
            ProbedMicroseconds,
            "-copyts",
            .. Device(encoder, machine.RenderNode),
            .. Decoding(attributes, captions),
            "-i",
            Input,
            .. Mapping(service, taken),
            "-vf",
            Filter(profile, attributes, encoder),
            "-r",
            Pace(profile),
            .. Encoding(profile, encoder, format),
            .. Sound(),
            .. FfmpegPlaybackInvocation.Panning(taken),
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
            LiveFragmentMicroseconds,
            Output,
        ];

    public static IReadOnlyList<string> CaptionDelivery(ServiceId service, int descriptor)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfLessThan(descriptor, 3);

        return CaptionOutput(service, Pipe(descriptor));
    }

    public static IReadOnlyList<string> DeliveryFromTheStart()
        =>
        [
            "-f",
            "mp4",
            "-movflags",
            "empty_moov+default_base_moof+delay_moov",
            "-frag_duration",
            RecordedFragmentMicroseconds,
            Output,
        ];

    internal static IReadOnlyList<string> CaptionOutput(ServiceId service, string target)
        => CaptionOutput(service, target, []);

    /// <summary>
    /// The caption output, with <paramref name="alongside"/> mapped into the same container after the
    /// pictures.
    /// </summary>
    internal static IReadOnlyList<string> CaptionOutput(ServiceId service, string target, IReadOnlyList<string> alongside)
        =>
        [
            "-filter_complex",
            Drawing(service),
            "-map",
            DrawnCaptions,
            .. alongside,
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
            target,
        ];

    internal static IReadOnlyList<string> Device(LiveEncoder encoder, string renderNode)
        => encoder is LiveEncoder.Vaapi ? ["-vaapi_device", renderNode] : [];

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

    internal static IReadOnlyList<string> Mapping(ServiceId service, SoundPlacement sound)
    {
        int programNumber = service.Value;
        int ordinal = sound.Ordinal;

        return
        [
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:v:0"),
            "-map",
            string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:a:{ordinal}"),
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

    /// <summary>
    /// The rate the picture is written at. A probe this short can end before the stream has said its own rate,
    /// and a rate guessed from the clock's ticks would have every frame repeated thousands of times a second.
    /// </summary>
    internal static string Pace(LiveProfile profile)
    {
        FrameRate rate = profile.Rate;

        return string.Create(CultureInfo.InvariantCulture, $"{rate.Numerator}/{rate.Denominator}");
    }

    /// <summary>
    /// The picture encoded in <paramref name="format"/>. The processor encodes H.264 alone, so another format is
    /// asked of the card or of nothing.
    /// </summary>
    internal static IReadOnlyList<string> Encoding(LiveProfile profile, LiveEncoder encoder, VideoCodec format)
    {
        VideoCodec asked = VideoCodecs.Named(format);

        if (encoder is not LiveEncoder.Vaapi && asked is not VideoCodecs.Unasked)
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The processor encodes H.264 alone; another format is encoded on the card.");
        }

        return encoder is LiveEncoder.Vaapi
            ?
            [
                "-c:v",
                OnTheCard(asked),
                "-g",
                Keyframes(profile),
                "-rc_mode",
                "CQP",
                .. Quality(profile.VaapiRateControl, asked),
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
    }

    internal static IReadOnlyList<string> Sound()
        =>
        [
            "-c:a",
            "aac",
            "-b:a",
            Kilobits(SoundKilobitsPerSecond),
        ];

    private static string OnTheCard(VideoCodec format) => format switch
    {
        VideoCodec.H265 => "hevc_vaapi",
        VideoCodec.Av1 => "av1_vaapi",
        _ => "h264_vaapi",
    };

    /// <summary>
    /// What the card is handed for the format: H.264 and H.265 a quantiser, AV1 the index it quantises by, which
    /// is the only quality its encoder takes. H.265 is tagged hvc1, the tag Safari plays.
    /// </summary>
    private static IReadOnlyList<string> Quality(CardRateControl handed, VideoCodec format) => format switch
    {
        VideoCodec.H265 => ["-qp", Number(handed.H265.Quantiser), "-tag:v", ArtefactCodecReading.TagSafariPlays],
        VideoCodec.Av1 => ["-global_quality", Number(handed.Av1.Index)],
        _ => ["-qp", Number(handed.H264.Quantiser)],
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool WantsEveryField(LiveProfile profile)
        => profile.Rate.PerSecond > FrameRate.BroadcastFrames.PerSecond;

    private static string Scaling(VideoSize size)
        => string.Create(CultureInfo.InvariantCulture, $"scale={size.Width}:{size.Height}:flags=bicubic");

    private static string Kilobits(int kilobitsPerSecond)
        => string.Create(CultureInfo.InvariantCulture, $"{kilobitsPerSecond}k");

    private static string Keyframes(LiveProfile profile)
        => Math.Round(profile.Rate.PerSecond * KeyframeSeconds).ToString(CultureInfo.InvariantCulture);
}
