using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The arguments for the two runs that look for the breaks in one source. The first listens to the
/// whole of it and decodes no picture at all, which is what makes looking affordable on a machine
/// that is also recording; the second decodes six seconds of picture around one moment the first
/// found, and there are only ever as many of those as there were quiet stretches. Neither asks for
/// the card: the encode itself has it, and a third user of one render node is not something the
/// promise to give the card up to a viewer covers.
/// <para>
/// Both runs keep the source's own clock. What ffmpeg takes off a reported moment otherwise
/// depends on where a seek happened to land, so a reading measured against it moves with the file;
/// keeping the clock makes every moment mean the same thing, and <see cref="ChapterClock"/> is
/// then the only place that converts one.
/// </para>
/// <para>
/// Every argument is an option name, a constant written here, or a number this repository holds
/// rendered the same way whatever language the machine is set to, beside the path of the source
/// (BR-EV-002). Nothing a broadcaster wrote reaches one, and there is no setting a filter could be
/// written in: the filter chains below are built from the numbers and from nothing else.
/// </para>
/// </summary>
public static class FfmpegChapterInvocation
{
    public const string Blackness = "blackdetect=d=0.05:pix_th=0.10";

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
        =>
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "info",
            "-nostats",
            "-copyts",
            "-threads",
            cores.ToString(CultureInfo.InvariantCulture),
        ];

    private static string Rendered(TimeSpan length)
        => length.TotalSeconds.ToString(Seconds, CultureInfo.InvariantCulture);
}
