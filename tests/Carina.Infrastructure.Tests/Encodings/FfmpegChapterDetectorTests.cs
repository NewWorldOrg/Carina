using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegChapterDetectorTests : IDisposable
{
    private const string Source = "/srv/recordings/0f8c.ts";

    private const int Cores = 2;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(1040);

    private static readonly Func<RunningProgramme, Task> Unwatched = _ => Task.CompletedTask;

    private static readonly EncodeTimeline Aligned = new(
        TimeSpan.FromSeconds(1000),
        TimeSpan.FromSeconds(0.5),
        TimeSpan.FromSeconds(1800),
        null);

    private static readonly string APodOfAdvertisements = """
        case "$*" in
            *silencedetect*)
                echo '[silencedetect @ 0x1] silence_start: 1300.25' >&2
                echo '[silencedetect @ 0x1] silence_end: 1300.75 | silence_duration: 0.5' >&2
                echo '[silencedetect @ 0x1] silence_start: 1360.25' >&2
                echo '[silencedetect @ 0x1] silence_end: 1360.75 | silence_duration: 0.5' >&2
                ;;
            *)
                echo '[blackdetect @ 0x1] black_start:1300.2 black_end:1300.8 black_duration:0.6' >&2
                echo '[blackdetect @ 0x1] black_start:1360.2 black_end:1360.8 black_duration:0.6' >&2
                printf 'frame:0    pts:0 pts_time:1300.25\nlavfi.scene_score=0.900000\n'
                printf 'frame:1    pts:0 pts_time:1360.25\nlavfi.scene_score=0.900000\n'
                ;;
        esac
        """;

    private readonly TempTree tree = new();

    public void Dispose() => tree.Dispose();

    [Fact(DisplayName = "a pod of advertisements is heard as two quiet stretches, seen as two dark ones, and comes back marked")]
    public async Task APodOfAdvertisementsComesBackMarked()
    {
        ChapterDetection read = await Looking(APodOfAdvertisements).MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(3, read.Segments.Count);
        Assert.Equal(1, read.Breaks);

        ChapterSegment gap = Assert.Single(read.Segments, segment => segment.Kind is ChapterKind.Break);
        Assert.Equal(TimeSpan.FromSeconds(300), gap.Starts);
        Assert.Equal(TimeSpan.FromSeconds(360), gap.Ends);
        Assert.Equal(TimeSpan.Zero, read.Segments[0].Starts);
        Assert.Equal(Aligned.Expected, read.Segments[^1].Ends);
    }

    [Fact(DisplayName = "the whole of the sound is listened to once and the picture is looked at once for each quiet stretch, and never the other way round")]
    public async Task TheSoundIsHeardOnceAndThePictureLookedAtOncePerQuietStretch()
    {
        string calls = tree.Under("calls");

        await Looking($"printf '%s\\n' \"$*\" >> \"{calls}\"\n{APodOfAdvertisements}")
            .MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        string[] ran = File.ReadAllLines(calls);

        Assert.Equal(3, ran.Length);
        Assert.Contains("silencedetect", ran[0], StringComparison.Ordinal);
        Assert.Contains("-vn", ran[0], StringComparison.Ordinal);
        Assert.All(ran[1..], line => Assert.Contains("blackdetect", line, StringComparison.Ordinal));
        Assert.Contains("-ss 297.5 ", ran[1], StringComparison.Ordinal);
        Assert.Contains("-ss 357.5 ", ran[2], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a source nothing went quiet in was looked at and had nothing to mark, which is not the same as nobody having looked")]
    public async Task ASourceNothingWentQuietInHasNothingToMark()
    {
        ChapterDetection read = await Looking("exit 0").MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Empty(read.Segments);
    }

    [Fact(DisplayName = "a programme that refused leaves a reading that could not be made, carrying the code and no word of what it said")]
    public async Task AProgrammeThatRefusedLeavesAReadingThatCouldNotBeMade()
    {
        ChapterDetection read = await Looking($"echo 'cannot open {Source}' >&2; exit 3")
            .MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Contains("exited 3", read.Note, StringComparison.Ordinal);
        Assert.Contains("listening to the whole of the sound", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot open", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("/srv/recordings", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a programme that refused while looking at the picture leaves the same answer, named for what it was doing")]
    public async Task AProgrammeThatRefusedWhileLookingAtThePictureLeavesTheSameAnswer()
    {
        ChapterDetection read = await Looking($"""
            case "$*" in
                *silencedetect*)
                    echo '[silencedetect @ 0x1] silence_start: 1300.25' >&2
                    echo '[silencedetect @ 0x1] silence_end: 1300.75 | silence_duration: 0.5' >&2
                    ;;
                *)
                    exit 218
                    ;;
            esac
            """).MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Contains("exited 218", read.Note, StringComparison.Ordinal);
        Assert.Contains("looking at the picture", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a programme that is not on this machine is a reading that could not be made, not a job that failed")]
    public async Task AProgrammeThatIsNotOnThisMachineIsAReadingThatCouldNotBeMade()
    {
        ChapterDetection read = await new FfmpegChapterDetector(
            new MachineSettings { Programme = tree.Under("no-such-programme") },
            new EncodeSettings(),
            TimeProvider.System).MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.DoesNotContain(tree.Root, read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a run that outlives what the whole look was allowed is stopped and leaves a reading that could not be made")]
    public async Task ARunThatOutlivesWhatItWasAllowedIsStopped()
    {
        string marker = tree.Under("woke");

        ChapterDetection read = await new FfmpegChapterDetector(
            new MachineSettings { Programme = Standing($"sleep 30\nprintf woke > \"{marker}\"") },
            new EncodeSettings(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(300)).MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Contains("was stopped", read.Note, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "moments reported against some clock this source is not on throw the whole reading away rather than leaving the few that landed believed")]
    public async Task MomentsAgainstSomeOtherClockThrowTheWholeReadingAway()
    {
        ChapterDetection read = await Looking("""
            echo '[silencedetect @ 0x1] silence_start: 1300.25' >&2
            echo '[silencedetect @ 0x1] silence_end: 1300.75 | silence_duration: 0.5' >&2
            echo '[silencedetect @ 0x1] silence_start: 99000' >&2
            echo '[silencedetect @ 0x1] silence_end: 99000.5 | silence_duration: 0.5' >&2
            echo '[silencedetect @ 0x1] silence_start: 99100' >&2
            echo '[silencedetect @ 0x1] silence_end: 99100.5 | silence_duration: 0.5' >&2
            echo '[silencedetect @ 0x1] silence_start: 99200' >&2
            echo '[silencedetect @ 0x1] silence_end: 99200.5 | silence_duration: 0.5' >&2
            """).MarkAsync(Source, Service, Aligned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Contains("3 of the 4", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a source nothing measured the length of is a reading that could not be made, because no moment in it could be placed")]
    public async Task ASourceNothingMeasuredTheLengthOfCannotBePlaced()
    {
        ChapterDetection read = await Looking(APodOfAdvertisements).MarkAsync(
            Source,
            Service,
            new EncodeTimeline(TimeSpan.FromSeconds(1000), TimeSpan.FromSeconds(0.5), null, null),
            Cores,
            Unwatched,
            Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Contains("how long the source is", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a stop the caller asked for is thrown rather than answered, so the caller knows nothing was read")]
    public async Task AStopTheCallerAskedForIsThrown()
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        string marker = tree.Under("woke");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Looking($"sleep 30\nprintf woke > \"{marker}\"")
            .MarkAsync(Source, Service, Aligned, Cores, Unwatched, stopping.Token));

        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "BR-ED2-005: the runs are allowed the cores the caller worked out, which is the cap the ledger holds, and nothing here reads the machine or the settings for a number of its own")]
    public async Task TheRunsAreAllowedTheCoresTheCallerWorkedOut()
    {
        string calls = tree.Under("calls");

        await new FfmpegChapterDetector(
            new MachineSettings
            {
                Programme = Standing($"printf '%s\\n' \"$*\" >> \"{calls}\"\n{APodOfAdvertisements}"),
                Cores = 8,
            },
            new EncodeSettings { MostCores = 6 },
            TimeProvider.System).MarkAsync(Source, Service, Aligned, 3, Unwatched, Cancel);

        Assert.All(
            File.ReadAllLines(calls),
            line =>
            {
                Assert.Contains("-threads 3 ", line, StringComparison.Ordinal);
                Assert.Contains("-filter_threads 3 ", line, StringComparison.Ordinal);
            });
    }

    [Fact(DisplayName = "a look allowed no core at all is asked for by a caller that has not worked one out, and is refused rather than run")]
    public async Task ALookAllowedNoCoreAtAllIsRefused()
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Looking(APodOfAdvertisements).MarkAsync(Source, Service, Aligned, 0, Unwatched, Cancel));

    [Fact(DisplayName = "BR-ED2-011: every programme the look starts is handed over before it says anything, so a process that dies mid-look leaves nothing nobody can find")]
    public async Task EveryProgrammeTheLookStartsIsHandedOverBeforeItSaysAnything()
    {
        string ownIds = tree.Under("own-ids");
        List<RunningProgramme> handedOver = [];

        ChapterDetection read = await Looking($"printf '%s\\n' \"$$\" >> \"{ownIds}\"\n{APodOfAdvertisements}")
            .MarkAsync(
                Source,
                Service,
                Aligned,
                Cores,
                spawned =>
                {
                    handedOver.Add(spawned);

                    return Task.CompletedTask;
                },
                Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(
            File.ReadAllLines(ownIds),
            handedOver.Select(spawned => spawned.ProcessId.ToString(CultureInfo.InvariantCulture)));
        Assert.All(handedOver, spawned => Assert.InRange(
            spawned.StartedAt,
            DateTime.UtcNow - TimeSpan.FromMinutes(5),
            DateTime.UtcNow + TimeSpan.FromMinutes(5)));
    }

    [Fact(DisplayName = "BR-ED2-011: a programme whose identity cannot be written down is stopped rather than left running unrecorded")]
    public async Task AProgrammeWhoseIdentityCannotBeWrittenDownIsStopped()
    {
        string marker = tree.Under("woke");

        await Assert.ThrowsAsync<IOException>(() => Looking($"sleep 30\nprintf woke > \"{marker}\"").MarkAsync(
            Source,
            Service,
            Aligned,
            Cores,
            _ => throw new IOException("the ledger refused"),
            Cancel));

        Assert.False(File.Exists(marker));
    }

    private FfmpegChapterDetector Looking(string body)
        => new(
            new MachineSettings { Programme = Standing(body) },
            new EncodeSettings(),
            TimeProvider.System);

    private string Standing(string body)
        => StandInProgramme.Written(tree.Under($"ffmpeg-{Guid.NewGuid():N}"), body);
}
