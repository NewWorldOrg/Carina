using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegChapterInvocationTests
{
    private const string Source = "/srv/recordings/0f8c.ts";

    private const int Cores = 2;

    private const double PrintedInStepsOf = 0.1;

    private static readonly ServiceId Service = new(1040);

    private static readonly ChapterSettings AsItStands = new();

    public static TheoryData<int, int, double, string, string> EveryWayOfLooking
        => new()
        {
            { -50, 150, 0.30, "silencedetect=n=-50dB:d=0.15", "blackdetect=d=0.15:pix_th=0.10,select=gte(scene\\,0.3),metadata=mode=print:file=-" },
            { -100, 1, 1, "silencedetect=n=-100dB:d=0.001", "blackdetect=d=0.15:pix_th=0.10,select=gte(scene\\,1),metadata=mode=print:file=-" },
            { -1, 2500, 0.001, "silencedetect=n=-1dB:d=2.5", "blackdetect=d=0.15:pix_th=0.10,select=gte(scene\\,0.001),metadata=mode=print:file=-" },
            { -42, 400, 0.45, "silencedetect=n=-42dB:d=0.4", "blackdetect=d=0.15:pix_th=0.10,select=gte(scene\\,0.45),metadata=mode=print:file=-" },
        };

    [Fact(DisplayName = "the run that listens to the whole of the sound decodes no picture and asks for exactly these")]
    public void TheArgumentsForListeningToTheWholeOfTheSoundAreExactlyThese()
        => Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "info",
                "-nostats",
                "-copyts",
                "-filter_threads",
                "2",
                "-threads",
                "2",
                "-vn",
                "-i",
                Source,
                "-map",
                "p:1040:a:0",
                "-af",
                "silencedetect=n=-50dB:d=0.15",
                "-f",
                "null",
                "-",
            ],
            FfmpegChapterInvocation.Listening(Source, Service, Cores, AsItStands));

    [Fact(DisplayName = "the run that looks at the picture around one moment reads six seconds from three seconds before it and asks for exactly these")]
    public void TheArgumentsForLookingAroundOneMomentAreExactlyThese()
        => Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "info",
                "-nostats",
                "-copyts",
                "-filter_threads",
                "2",
                "-threads",
                "2",
                "-an",
                "-ss",
                "1297.25",
                "-t",
                "6",
                "-i",
                Source,
                "-map",
                "p:1040:v:0",
                "-vf",
                "blackdetect=d=0.15:pix_th=0.10,select=gte(scene\\,0.3),metadata=mode=print:file=-",
                "-f",
                "null",
                "-",
            ],
            FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(1300.25), AsItStands));

    [Fact(DisplayName = "a moment within the first three seconds of the source is looked at from the beginning rather than from before it")]
    public void AMomentNearTheBeginningIsLookedAtFromTheBeginning()
    {
        Assert.Equal(TimeSpan.Zero, FfmpegChapterInvocation.From(TimeSpan.FromSeconds(1.5)));
        Assert.Equal(TimeSpan.Zero, FfmpegChapterInvocation.From(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(1), FfmpegChapterInvocation.From(TimeSpan.FromSeconds(4)));
        Assert.Equal(
            "0",
            After(FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(1.5), AsItStands), "-ss"));
    }

    [Theory(DisplayName = "BR-EV-002: each of the numbers the look is made of is written into the filter it is for the same way whatever language the machine is set to")]
    [MemberData(nameof(EveryWayOfLooking))]
    public void EachNumberIsWrittenIntoTheFilterItIsFor(int noise, int silence, double scene, string heard, string seen)
    {
        ChapterSettings settings = Looking(noise, silence, scene);

        Assert.Equal(heard, After(FfmpegChapterInvocation.Listening(Source, Service, Cores, settings), "-af"));
        Assert.Equal(
            seen,
            After(FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(300.25), settings), "-vf"));
    }

    [Fact(DisplayName = "BR-ED2-005: neither run reaches for the card, so the one this machine has stays between the encode and whoever is watching")]
    public void NeitherRunReachesForTheCard()
        => Assert.All(
            Both(AsItStands),
            argument =>
            {
                Assert.DoesNotContain("vaapi", argument, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("/dev/dri", argument, StringComparison.Ordinal);
            });

    [Fact(DisplayName = "BR-ED2-005: neither run is allowed more of the machine than an encode is")]
    public void NeitherRunIsAllowedMoreOfTheMachineThanAnEncodeIs()
    {
        Assert.Equal("3", After(FfmpegChapterInvocation.Listening(Source, Service, 3, AsItStands), "-threads"));
        Assert.Equal("3", After(FfmpegChapterInvocation.Listening(Source, Service, 3, AsItStands), "-filter_threads"));
        Assert.Equal(
            "3",
            After(FfmpegChapterInvocation.Peeking(Source, Service, 3, TimeSpan.FromSeconds(100), AsItStands), "-threads"));
        Assert.Equal(
            "3",
            After(FfmpegChapterInvocation.Peeking(Source, Service, 3, TimeSpan.FromSeconds(100), AsItStands), "-filter_threads"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FfmpegChapterInvocation.Listening(Source, Service, 0, AsItStands));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FfmpegChapterInvocation.Peeking(Source, Service, 0, TimeSpan.FromSeconds(100), AsItStands));
    }

    [Fact(DisplayName = "the dark the run looks for outlasts the tenth of a second that keeping the source's clock costs to print, so a marginal one comes back as a stretch rather than as one moment")]
    public void TheDarkTheRunLooksForOutlastsWhatKeepingTheClockCostsToPrint()
    {
        const string Named = "blackdetect=d=";

        Assert.StartsWith(Named, FfmpegChapterInvocation.Blackness, StringComparison.Ordinal);

        string asked = FfmpegChapterInvocation.Blackness[Named.Length..].Split(':')[0];

        Assert.True(
            double.Parse(asked, CultureInfo.InvariantCulture) > PrintedInStepsOf,
            $"a dark stretch of {asked} s is looked for where a moment is printed in steps of {PrintedInStepsOf} s, so a marginal one would begin and end at the same printed moment and be dropped");
    }

    [Fact(DisplayName = "both runs keep the source's own clock, because what ffmpeg takes off a reported moment otherwise depends on where a seek landed")]
    public void BothRunsKeepTheSourcesOwnClock()
    {
        Assert.Contains("-copyts", FfmpegChapterInvocation.Listening(Source, Service, Cores, AsItStands));
        Assert.Contains(
            "-copyts",
            FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(100), AsItStands));
    }

    [Fact(DisplayName = "both runs ask the filters to talk, the ones that find the quiet and the dark saying nothing at any quieter level")]
    public void BothRunsAskTheFiltersToTalk()
    {
        Assert.Equal("info", After(FfmpegChapterInvocation.Listening(Source, Service, Cores, AsItStands), "-loglevel"));
        Assert.Equal(
            "info",
            After(FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(100), AsItStands), "-loglevel"));
    }

    [Theory(DisplayName = "BR-EV-002: nothing that reaches an argument was written by anyone but this repository")]
    [MemberData(nameof(EveryWayOfLooking))]
    public void EveryArgumentIsAnOptionNameAConstantOrThePathItWasHandedIn(
        int noise,
        int silence,
        double scene,
        string heard,
        string seen)
    {
        ChapterSettings settings = Looking(noise, silence, scene);
        string[] known =
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "info",
            "-nostats",
            "-copyts",
            "-filter_threads",
            "2",
            "-threads",
            "2",
            "-vn",
            "-an",
            "-ss",
            "297.25",
            "-t",
            "6",
            "-i",
            Source,
            "-map",
            "p:1040:a:0",
            "p:1040:v:0",
            "-af",
            heard,
            "-vf",
            seen,
            "-f",
            "null",
            "-",
        ];

        Assert.All(Both(settings), argument => Assert.Contains(argument, known, StringComparer.Ordinal));
    }

    [Theory(DisplayName = "BR-EV-002: an argument is never one piece of text carrying another")]
    [MemberData(nameof(EveryWayOfLooking))]
    public void AnArgumentIsNeverOnePieceOfTextCarryingAnother(
        int noise,
        int silence,
        double scene,
        string heard,
        string seen)
    {
        Assert.NotEmpty(heard);
        Assert.NotEmpty(seen);

        string[] whatAShellWouldReadAgain = [" ", ";", "|", "&", "`", "$(", "\n"];

        Assert.All(
            Both(Looking(noise, silence, scene))
                .Where(argument => !string.Equals(argument, Source, StringComparison.Ordinal)),
            argument => Assert.DoesNotContain(
                whatAShellWouldReadAgain,
                mark => argument.Contains(mark, StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "BR-ED2-007: the run that watches for the watermark decodes only the pictures that stand on their own, keeps one a second, and hands them over in grey, asking for exactly these")]
    public void TheArgumentsForWatchingForTheWatermarkAreExactlyThese()
        => Assert.Equal(
            [
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "info",
                "-nostats",
                "-copyts",
                "-filter_threads",
                "2",
                "-threads",
                "2",
                "-an",
                "-skip_frame",
                "nokey",
                "-i",
                Source,
                "-map",
                "p:1040:v:0",
                "-vf",
                "fps=1,scale=480:270:flags=area,format=gray,showinfo",
                "-fps_mode",
                "passthrough",
                "-f",
                "rawvideo",
                "-",
            ],
            FfmpegChapterInvocation.Watching(Source, Service, Cores));

    [Fact(DisplayName = "BR-ED2-007: the pictures the watch hands over are the size a watermark is looked for in, one byte a pixel")]
    public void ThePicturesTheWatchHandsOverAreTheSizeAWatermarkIsLookedForIn()
    {
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"scale={WatermarkFrame.Width}:{WatermarkFrame.Height}:"),
            FfmpegChapterInvocation.Shrunk,
            StringComparison.Ordinal);
        Assert.Contains("format=gray", FfmpegChapterInvocation.Shrunk, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-005: the watch neither reaches for the card nor is allowed more of the machine than an encode is, and it keeps the source's own clock")]
    public void TheWatchStaysWithinWhatTheLookIsAllowed()
    {
        IReadOnlyList<string> watching = FfmpegChapterInvocation.Watching(Source, Service, 3);

        Assert.Equal("3", After(watching, "-threads"));
        Assert.Equal("3", After(watching, "-filter_threads"));
        Assert.Contains("-copyts", watching);
        Assert.All(
            watching,
            argument =>
            {
                Assert.DoesNotContain("vaapi", argument, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("/dev/dri", argument, StringComparison.Ordinal);
            });
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegChapterInvocation.Watching(Source, Service, 0));
    }

    [Fact(DisplayName = "BR-EV-002: no argument of the watch is one piece of text carrying another")]
    public void NoArgumentOfTheWatchCarriesAnother()
    {
        string[] whatAShellWouldReadAgain = [" ", ";", "|", "&", "`", "$(", "\n"];

        Assert.All(
            FfmpegChapterInvocation.Watching(Source, Service, Cores)
                .Where(argument => !string.Equals(argument, Source, StringComparison.Ordinal)),
            argument => Assert.DoesNotContain(
                whatAShellWouldReadAgain,
                mark => argument.Contains(mark, StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(TextAShellWouldReadAgain.Every), MemberType = typeof(TextAShellWouldReadAgain))]
    public void ASourceCarryingTextAShellWouldReadAgainIsHandedOverAsOneArgumentInItsOwnPlace(string slipped)
    {
        string source = "/srv/recordings/" + slipped + ".ts";

        string[] plain =
        [
            .. FfmpegChapterInvocation.Listening(Source, Service, Cores, AsItStands),
            .. FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(300.25), AsItStands),
            .. FfmpegChapterInvocation.Watching(Source, Service, Cores),
        ];
        string[] carrying =
        [
            .. FfmpegChapterInvocation.Listening(source, Service, Cores, AsItStands),
            .. FfmpegChapterInvocation.Peeking(source, Service, Cores, TimeSpan.FromSeconds(300.25), AsItStands),
            .. FfmpegChapterInvocation.Watching(source, Service, Cores),
        ];

        Assert.Equal([.. plain.Select(argument => argument == Source ? source : argument)], carrying);
    }

    private static ChapterSettings Looking(int noise, int silence, double scene)
        => new()
        {
            Noise = noise,
            ShortestSilence = TimeSpan.FromMilliseconds(silence),
            Scene = scene,
        };

    private static IReadOnlyList<string> Both(ChapterSettings settings)
        =>
        [
            .. FfmpegChapterInvocation.Listening(Source, Service, Cores, settings),
            .. FfmpegChapterInvocation.Peeking(Source, Service, Cores, TimeSpan.FromSeconds(300.25), settings),
        ];

    private static string After(IReadOnlyList<string> arguments, string option)
    {
        for (int at = 0; at < arguments.Count - 1; at++)
        {
            if (string.Equals(arguments[at], option, StringComparison.Ordinal))
            {
                return arguments[at + 1];
            }
        }

        throw new InvalidOperationException($"nothing among the arguments is '{option}'.");
    }
}
