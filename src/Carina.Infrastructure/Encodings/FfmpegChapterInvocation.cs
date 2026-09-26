using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The arguments for the two runs that look for the breaks in one source: the first listens to the
/// whole of it and decodes no picture, and the second decodes six seconds of picture around one
/// moment the first found. Neither asks for the card.
/// </summary>
/// <remarks>
/// Both runs keep the source's own clock, which <see cref="ChapterClock"/> converts. Every argument
/// is an option name, a constant written here, or a number rendered the same way in any culture,
/// beside the path of the source.
/// </remarks>
public static class FfmpegChapterInvocation
{
    /// <summary>
    /// Reports a stretch of dark once it lasts longer than the tenth of a second a moment is printed to
    /// on the source's own clock.
    /// </summary>
    public const string Blackness = "blackdetect=d=0.15:pix_th=0.10";

    public const string Printed = "metadata=mode=print:file=-";

    public const string Seconds = FfmpegEncodeInvocation.Seconds;

    public static readonly TimeSpan Before = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(6);

    public static IReadOnlyList<string> Listening(
        string source,
        ServiceId service,
        int cores,
        ChapterSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(cores, 1);

        return
        [
            .. Preamble(cores),
            "-vn",
            "-i",
            source,
            "-map",
            FfmpegEncodeInvocation.OneAudioStream(service, 0),
            "-af",
            Quiet(settings),
            "-f",
            "null",
            "-",
        ];
    }

    public static IReadOnlyList<string> Peeking(
        string source,
        ServiceId service,
        int cores,
        TimeSpan at,
        ChapterSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(cores, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(at, TimeSpan.Zero, nameof(at));

        return
        [
            .. Preamble(cores),
            "-an",
            "-ss",
            Rendered(From(at)),
            "-t",
            Rendered(Window),
            "-i",
            source,
            "-map",
            FfmpegEncodeInvocation.VideoStream(service),
            "-vf",
            Looking(settings),
            "-f",
            "null",
            "-",
        ];
    }

    /// <summary>
    /// The run that watches the whole of the picture for the station's watermark. It decodes only key
    /// frames, keeps one a second, shrinks it to the size a watermark is looked for in and hands it
    /// over in grey on the output, one picture for each line on the error stream giving the moment it
    /// was shown at.
    /// </summary>
    public static IReadOnlyList<string> Watching(string source, ServiceId service, int cores)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfLessThan(cores, 1);

        return
        [
            .. Preamble(cores),
            "-an",
            "-skip_frame",
            "nokey",
            "-i",
            source,
            "-map",
            FfmpegEncodeInvocation.VideoStream(service),
            "-vf",
            Shrunk,
            "-fps_mode",
            "passthrough",
            "-f",
            "rawvideo",
            "-",
        ];
    }

    public const string Shrunk = "fps=1,scale=480:270:flags=area,format=gray,showinfo";

    public static TimeSpan From(TimeSpan at) => at > Before ? at - Before : TimeSpan.Zero;

    internal static string Quiet(ChapterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int under = settings.Noise;
        string lasting = Rendered(settings.ShortestSilence);

        return string.Create(CultureInfo.InvariantCulture, $"silencedetect=n={under}dB:d={lasting}");
    }

    internal static string Looking(ChapterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string changed = settings.Scene.ToString(Seconds, CultureInfo.InvariantCulture);

        return string.Create(CultureInfo.InvariantCulture, $"{Blackness},select=gte(scene\\,{changed}),{Printed}");
    }

    private static IReadOnlyList<string> Preamble(int cores)
    {
        string allowed = cores.ToString(CultureInfo.InvariantCulture);

        return
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "info",
            "-nostats",
            "-copyts",
            "-filter_threads",
            allowed,
            "-threads",
            allowed,
        ];
    }

    private static string Rendered(TimeSpan length)
        => length.TotalSeconds.ToString(Seconds, CultureInfo.InvariantCulture);
}
