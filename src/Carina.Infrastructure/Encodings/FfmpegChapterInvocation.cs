using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The arguments for the three runs that look for the breaks in one source:
/// <see cref="Listening"/> hears the whole of the sound and decodes no picture,
/// <see cref="Peeking"/> decodes <see cref="Window"/> of picture from <see cref="Before"/> ahead of
/// one moment the first found, and <see cref="Watching"/> watches the key frames for the station's
/// watermark. None of them asks for the card.
/// </summary>
/// <remarks>
/// All three keep the source's own clock, which <see cref="ChapterClock"/> converts. Every argument
/// is an option name, a constant written here, or a number rendered the same way in any culture,
/// beside the path of the source.
/// </remarks>
public static class FfmpegChapterInvocation
{
    /// <summary>
    /// The dark detector: a stretch of at least 0.15 seconds (<c>d</c>) in which the picture is black,
    /// a pixel counting as black at or below 0.10 of full brightness (<c>pix_th</c>).
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
