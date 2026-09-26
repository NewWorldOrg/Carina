using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Streaming;

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

    public const string Seconds = "0.######";

    /// <summary>
    /// Where the chapters stand among the inputs, which is what says which input the run is told
    /// to take them from.
    /// </summary>
    public const string ChaptersInput = "1";

    /// <summary>
    /// The arguments for one run. The core cap is written three times because ffmpeg counts
    /// threads per stage: once before the input for the decoder, once for the filters, and once
    /// for the encoder. The stages are a pipeline, so the run as a whole is bounded
    /// by the slowest of them rather than by their sum.
    /// <para>
    /// Chapters to bake into the artefact come in as a second input, and it goes <em>after</em> the
    /// recording. A stream is asked for here by its programme — <c>p:1040:v:0</c> — and a specifier
    /// that opens with no file number is read as naming the first input, so a metadata file put in
    /// front of the recording would quietly take the picture and the sound with it. What holds the
    /// order is a test, not this paragraph.
    /// </para>
    /// <para>
    /// With nothing to bake in, not one argument is added and the run is the run it was before,
    /// which is what makes the looking switchable off.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Arguments(
        ServiceId service,
        EncodeProfile profile,
        EncodeEncoder encoder,
        string source,
        int cores,
        TimeSpan headSkip,
        EncodeSound sound,
        string? chapters = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(cores, 1);

        if (!EncodeTimeline.WithinReach(headSkip))
        {
            throw new ArgumentOutOfRangeException(
                nameof(headSkip),
                headSkip,
                "A head is skipped by between nothing and the longest skip a timeline holds; anything longer is refused before a run is built.");
        }

        string threads = cores.ToString(CultureInfo.InvariantCulture);

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
            "-filter_threads",
            threads,
            .. Device(encoder),
            "-threads",
            threads,
            "-i",
            source,
            .. ReadingChapters(chapters),
            "-ss",
            headSkip.TotalSeconds.ToString(Seconds, CultureInfo.InvariantCulture),
            .. Mapping(service, sound),
            .. BakingChapters(chapters),
            .. Filtering(profile, encoder),
            .. Encoding(profile, encoder),
            "-threads",
            threads,
            .. Audio(sound),
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

    internal static IReadOnlyList<string> ReadingChapters(string? chapters)
        => string.IsNullOrEmpty(chapters) ? [] : ["-f", "ffmetadata", "-i", chapters];

    internal static IReadOnlyList<string> BakingChapters(string? chapters)
        => string.IsNullOrEmpty(chapters) ? [] : ["-map_chapters", ChaptersInput];

    internal static IReadOnlyList<string> Device(EncodeEncoder encoder)
        => EncodeShapes.Named(encoder) is EncodeEncoder.Vaapi ? ["-vaapi_device", RenderNode] : [];

    internal static IReadOnlyList<string> Mapping(ServiceId service, EncodeSound sound)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(sound);

        return
        [
            "-map",
            VideoStream(service),
            "-map",
            sound.OneChannel is { } placement
                ? OneAudioStream(service, placement.Ordinal)
                : AudioStreams(service),
        ];
    }

    internal static IReadOnlyList<string> Audio(EncodeSound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);

        return sound.OneChannel is { } placement
            ?
            [
                .. FfmpegPlaybackInvocation.Panning(placement),
                .. FfmpegLiveInvocation.Sound(),
            ]
            :
            [
                "-c:a",
                "copy",
                "-bsf:a",
                "aac_adtstoasc",
            ];
    }

    public static string VideoStream(ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(service);

        int programNumber = service.Value;

        return string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:v:0");
    }

    private static string AudioStreams(ServiceId service)
    {
        int programNumber = service.Value;

        return string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:a");
    }

    internal static string OneAudioStream(ServiceId service, int ordinal)
    {
        int programNumber = service.Value;

        return string.Create(CultureInfo.InvariantCulture, $"p:{programNumber}:a:{ordinal}");
    }

    /// <summary>
    /// The picture is handed to a filter only where there is something to do to it. A profile that
    /// keeps the source's size, leaves the fields alone and encodes on the processor has nothing to
    /// do, and an empty <c>-vf</c> is not an empty filter chain to ffmpeg but a filter it cannot
    /// find, which ends the run.
    /// </summary>
    internal static IReadOnlyList<string> Filtering(EncodeProfile profile, EncodeEncoder encoder)
        => Filter(profile, encoder) is { Length: > 0 } filter ? ["-vf", filter] : [];

    /// <summary>
    /// The samples are squared up only where the picture has just been resized. Broadcast HD comes
    /// in 1440 by 1080 with samples 4:3 wide, which is how it fills a 16:9 screen; squaring those
    /// samples without resizing keeps the 1440 by 1080 and throws the width away, so the artefact
    /// plays back 4:3 and a 16:9 player pads it either side. A resize to 1920 by 1080 or 1280 by 720
    /// carries the display aspect across on its own, and squaring up after it holds that result.
    /// </summary>
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
            steps.Add(SquarePixels);
        }

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
