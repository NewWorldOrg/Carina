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

    private const int WatchedFrom = 1290;

    private const int PicturesWatched = 80;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(1040);

    private static readonly Func<RunningProgramme, Task> Unwatched = _ => Task.CompletedTask;

    private static readonly WatermarkMask? NothingLearned = null;

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
        ChapterDetection read = await Looking(APodOfAdvertisements).MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(3, read.Segments.Count);
        Assert.Equal(1, read.Breaks);

        ChapterSegment gap = Assert.Single(read.Segments, segment => segment.Kind is ChapterKind.Break);
        Assert.Equal(TimeSpan.FromSeconds(300), gap.Starts);
        Assert.Equal(TimeSpan.FromSeconds(360), gap.Ends);
        Assert.Equal(TimeSpan.Zero, read.Segments[0].Starts);
        Assert.Equal(Aligned.Expected, read.Segments[^1].Ends);
    }

    [Fact(DisplayName = "the whole of the sound is listened to once, the picture looked at once for each quiet stretch, and watched once for the watermark, in that order")]
    public async Task TheSoundIsHeardOnceThePictureLookedAtOncePerQuietStretchAndWatchedOnce()
    {
        string calls = tree.Under("calls");

        await Looking($"printf '%s\\n' \"$*\" >> \"{calls}\"\n{APodOfAdvertisements}")
            .MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        string[] ran = File.ReadAllLines(calls);

        Assert.Equal(4, ran.Length);
        Assert.Contains("silencedetect", ran[0], StringComparison.Ordinal);
        Assert.Contains("-vn", ran[0], StringComparison.Ordinal);
        Assert.All(ran[1..3], line => Assert.Contains("blackdetect", line, StringComparison.Ordinal));
        Assert.Contains("-ss 297.5 ", ran[1], StringComparison.Ordinal);
        Assert.Contains("-ss 357.5 ", ran[2], StringComparison.Ordinal);
        Assert.Contains("-skip_frame nokey", ran[3], StringComparison.Ordinal);
        Assert.Contains("rawvideo", ran[3], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a source nothing went quiet in was looked at and had nothing to mark, which is not the same as nobody having looked")]
    public async Task ASourceNothingWentQuietInHasNothingToMark()
    {
        ChapterDetection read = await Looking("exit 0").MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Empty(read.Segments);
    }

    [Fact(DisplayName = "a programme that refused leaves a reading that could not be made, carrying the code and no word of what it said")]
    public async Task AProgrammeThatRefusedLeavesAReadingThatCouldNotBeMade()
    {
        ChapterDetection read = await Looking($"echo 'cannot open {Source}' >&2; exit 3")
            .MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Unreadable, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Contains("exited 3", read.Note, StringComparison.Ordinal);
        Assert.Contains("listening to the whole of the sound", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot open", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("/srv/recordings", read.Note, StringComparison.Ordinal);
        Assert.Null(read.Learned);
    }

    [Fact(DisplayName = "one look at the picture that refused leaves that quiet stretch uncorroborated and the rest of the reading standing, and the reading says so")]
    public async Task OneLookThatRefusedLeavesTheRestOfTheReadingStanding()
    {
        string calls = tree.Under("calls");
        ChapterDetection read = await Looking($$"""
            printf '%s\n' "$*" >> "{{calls}}"
            case "$*" in
                *silencedetect*)
                    echo '[silencedetect @ 0x1] silence_start: 1300.25' >&2
                    echo '[silencedetect @ 0x1] silence_end: 1300.75 | silence_duration: 0.5' >&2
                    echo '[silencedetect @ 0x1] silence_start: 1360.25' >&2
                    echo '[silencedetect @ 0x1] silence_end: 1360.75 | silence_duration: 0.5' >&2
                    ;;
                *"-ss 297.5 "*)
                    exit 218
                    ;;
                *)
                    echo '[blackdetect @ 0x1] black_start:1300.2 black_end:1300.8 black_duration:0.6' >&2
                    echo '[blackdetect @ 0x1] black_start:1360.2 black_end:1360.8 black_duration:0.6' >&2
                    printf 'frame:0    pts:0 pts_time:1300.25\nlavfi.scene_score=0.900000\n'
                    printf 'frame:1    pts:0 pts_time:1360.25\nlavfi.scene_score=0.900000\n'
                    ;;
            esac
            """).MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        Assert.Equal(4, File.ReadAllLines(calls).Length);
        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(1, read.Breaks);
        Assert.Contains("1 of the looks refused", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("218", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "only so many quiet stretches are worth looking at the picture around, and the ones looked at are the longest, longest first")]
    public async Task OnlySoManyQuietStretchesAreLookedAtAndTheLongestComeFirst()
    {
        string calls = tree.Under("calls");
        FfmpegChapterDetector detector = new(
            new MachineSettings
            {
                Programme = Standing($$"""
                    printf '%s\n' "$*" >> "{{calls}}"
                    case "$*" in
                        *silencedetect*)
                            echo '[silencedetect @ 0x1] silence_start: 1100' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1100.1 | silence_duration: 0.1' >&2
                            echo '[silencedetect @ 0x1] silence_start: 1200' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1200.5 | silence_duration: 0.5' >&2
                            echo '[silencedetect @ 0x1] silence_start: 1300' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1300.2 | silence_duration: 0.2' >&2
                            echo '[silencedetect @ 0x1] silence_start: 1400' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1400.4 | silence_duration: 0.4' >&2
                            echo '[silencedetect @ 0x1] silence_start: 1500' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1500.3 | silence_duration: 0.3' >&2
                            ;;
                    esac
                    """),
            },
            new EncodeSettings { Chapters = new ChapterSettings { MostChapters = 1 } },
            TimeProvider.System);

        ChapterDetection read = await detector.MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        string[] ran = File.ReadAllLines(calls);

        Assert.Equal(2 + (FfmpegChapterDetector.MostLooksPerMark * 1), ran.Length);
        Assert.Contains("-ss 197.25 ", ran[1], StringComparison.Ordinal);
        Assert.Contains("-ss 397.2 ", ran[2], StringComparison.Ordinal);
        Assert.Contains("-ss 497.15 ", ran[3], StringComparison.Ordinal);
        Assert.Contains("-ss 297.1 ", ran[4], StringComparison.Ordinal);
        Assert.Contains("rawvideo", ran[5], StringComparison.Ordinal);
        Assert.DoesNotContain(ran, line => line.Contains("-ss 97.05 ", StringComparison.Ordinal));
        Assert.Contains("4 longest of the 5", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a look that runs out of time part way through the picture is still a reading of what it did see, not a reading thrown away")]
    public async Task ALookThatRunsOutOfTimePartWayThroughIsStillAReadingOfWhatItSaw()
    {
        FfmpegChapterDetector detector = new(
            new MachineSettings
            {
                Programme = Standing("""
                    case "$*" in
                        *silencedetect*)
                            echo '[silencedetect @ 0x1] silence_start: 1300.25' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1300.75 | silence_duration: 0.5' >&2
                            echo '[silencedetect @ 0x1] silence_start: 1360.25' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1360.75 | silence_duration: 0.5' >&2
                            echo '[silencedetect @ 0x1] silence_start: 1500' >&2
                            echo '[silencedetect @ 0x1] silence_end: 1500.1 | silence_duration: 0.1' >&2
                            ;;
                        *"-ss 497.05 "*)
                            sleep 30
                            ;;
                        *)
                            echo '[blackdetect @ 0x1] black_start:1300.2 black_end:1300.8 black_duration:0.6' >&2
                            echo '[blackdetect @ 0x1] black_start:1360.2 black_end:1360.8 black_duration:0.6' >&2
                            printf 'frame:0    pts:0 pts_time:1300.25\nlavfi.scene_score=0.900000\n'
                            printf 'frame:1    pts:0 pts_time:1360.25\nlavfi.scene_score=0.900000\n'
                            ;;
                    esac
                    """),
            },
            new EncodeSettings(),
            TimeProvider.System,
            TimeSpan.FromSeconds(3));

        ChapterDetection read = await detector.MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(1, read.Breaks);
        Assert.Contains("was stopped after", read.Note, StringComparison.Ordinal);
        Assert.Contains("2 of the 3", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a programme that is not on this machine is a reading that could not be made, not a job that failed")]
    public async Task AProgrammeThatIsNotOnThisMachineIsAReadingThatCouldNotBeMade()
    {
        ChapterDetection read = await new FfmpegChapterDetector(
            new MachineSettings { Programme = tree.Under("no-such-programme") },
            new EncodeSettings(),
            TimeProvider.System).MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

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
            TimeSpan.FromMilliseconds(300)).MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

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
            """).MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

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
            NothingLearned,
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
            .MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, stopping.Token));

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
            TimeProvider.System).MarkAsync(Source, Service, Aligned, NothingLearned, 3, Unwatched, Cancel);

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
            () => Looking(APodOfAdvertisements).MarkAsync(Source, Service, Aligned, NothingLearned, 0, Unwatched, Cancel));

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
                NothingLearned,
                Cores,
                spawned =>
                {
                    handedOver.Add(spawned);

                    return Task.CompletedTask;
                },
                Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(4, handedOver.Count);
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
            NothingLearned,
            Cores,
            _ => throw new IOException("the ledger refused"),
            Cancel));

        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "BR-ED2-007: a source with no watermark learned ahead is judged without one, and the watermark it carries is learned from it and handed back beside the reading")]
    public async Task ASourceWithNoWatermarkLearnedAheadIsJudgedWithoutOneAndTeachesItsOwn()
    {
        ChapterDetection read = await Looking(WatchedThrough(unbrandedFrom: 0, unbrandedUntil: 0))
            .MarkAsync(Source, Service, Aligned, NothingLearned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(1, read.Breaks);
        Assert.NotNull(read.Learned);
        Assert.True(read.Learned.Covers(MarkLeft - 1, 20));
        Assert.Contains("no watermark had been learned ahead", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: a pod the watermark learned ahead stayed on screen through is programme, and the reading says it was taken away")]
    public async Task APodTheWatermarkLearnedAheadStayedOnScreenThroughIsProgramme()
    {
        ChapterDetection read = await Looking(WatchedThrough(unbrandedFrom: WatchedFrom, unbrandedUntil: WatchedFrom + 9))
            .MarkAsync(Source, Service, Aligned, LearnedAhead(), Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Contains("1 of the 1 candidate breaks were taken away", read.Note, StringComparison.Ordinal);
        Assert.NotNull(read.Learned);
    }

    [Fact(DisplayName = "BR-ED2-007: a pod the watermark learned ahead was taken off for stands as a break")]
    public async Task APodTheWatermarkWasTakenOffForStands()
    {
        ChapterDetection read = await Looking(WatchedThrough(unbrandedFrom: 1300, unbrandedUntil: 1361))
            .MarkAsync(Source, Service, Aligned, LearnedAhead(), Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        ChapterSegment gap = Assert.Single(read.Segments, segment => segment.Kind is ChapterKind.Break);
        Assert.Equal(TimeSpan.FromSeconds(300), gap.Starts);
        Assert.Equal(TimeSpan.FromSeconds(360), gap.Ends);
        Assert.DoesNotContain("taken away", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: a machine told not to watch for the watermark runs no watch, learns nothing and uses none, whatever it is handed")]
    public async Task AMachineToldNotToWatchRunsNoWatch()
    {
        string calls = tree.Under("calls");
        FfmpegChapterDetector detector = new(
            new MachineSettings { Programme = Standing($"printf '%s\\n' \"$*\" >> \"{calls}\"\n{WatchedThrough(0, 0)}") },
            new EncodeSettings { Chapters = new ChapterSettings { Watermark = false } },
            TimeProvider.System);

        ChapterDetection read = await detector.MarkAsync(Source, Service, Aligned, LearnedAhead(), Cores, Unwatched, Cancel);

        string[] ran = File.ReadAllLines(calls);
        Assert.Equal(3, ran.Length);
        Assert.DoesNotContain(ran, line => line.Contains("rawvideo", StringComparison.Ordinal));
        Assert.Equal(1, read.Breaks);
        Assert.Null(read.Learned);
        Assert.DoesNotContain("watermark", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: a watch that refused leaves the reading made without a watermark, learns nothing, and says so without a word of what the programme said")]
    public async Task AWatchThatRefusedLeavesTheReadingMadeWithoutAWatermark()
    {
        ChapterDetection read = await Looking($$"""
            case "$*" in
                *rawvideo*)
                    echo 'cannot decode {{Source}}' >&2
                    exit 5
                    ;;
            esac
            {{APodOfAdvertisements}}
            """).MarkAsync(Source, Service, Aligned, LearnedAhead(), Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(1, read.Breaks);
        Assert.Null(read.Learned);
        Assert.Contains("watching the picture for the station's watermark ended without a reading (5)", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot decode", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: a watch that would outlive the whole look costs the looks at the picture none of their time, so the breaks are still found and the reading says how long the watch was given")]
    public async Task AWatchThatWouldOutliveTheWholeLookCostsTheLooksNoneOfTheirTime()
    {
        var clock = new HandTurnedClock();
        FfmpegChapterDetector detector = new(
            new MachineSettings { Programme = Standing($"case \"$*\" in *rawvideo*) sleep 30; exit 0 ;; esac\n{APodOfAdvertisements}") },
            new EncodeSettings(),
            clock);

        ChapterDetection read = await detector.MarkAsync(
            Source,
            Service,
            Aligned,
            LearnedAhead(),
            Cores,
            spawned =>
            {
                clock.Turn(AskedOf(spawned).Contains("rawvideo", StringComparison.Ordinal)
                    ? FfmpegChapterDetector.Patience
                    : TimeSpan.FromMinutes(1));

                return Task.CompletedTask;
            },
            Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(1, read.Breaks);
        Assert.Null(read.Learned);
        Assert.Contains("watching the picture for the station's watermark was stopped after 00:02:00", read.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("looking at the picture was stopped", read.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-007: looks at the picture that used up the whole look leave no watch started at all, and the reading is made without a watermark")]
    public async Task LooksThatUsedUpTheWholeLookLeaveNoWatchStarted()
    {
        string calls = tree.Under("calls");
        var clock = new HandTurnedClock();
        FfmpegChapterDetector detector = new(
            new MachineSettings { Programme = Standing($"printf '%s\\n' \"$*\" >> \"{calls}\"\ncase \"$*\" in *blackdetect*) sleep 30; exit 0 ;; esac\n{APodOfAdvertisements}") },
            new EncodeSettings(),
            clock);

        ChapterDetection read = await detector.MarkAsync(
            Source,
            Service,
            Aligned,
            LearnedAhead(),
            Cores,
            spawned =>
            {
                if (AskedOf(spawned).Contains("blackdetect", StringComparison.Ordinal))
                {
                    clock.Turn(FfmpegChapterDetector.Patience);
                }

                return Task.CompletedTask;
            },
            Cancel);

        Assert.DoesNotContain(File.ReadAllLines(calls), line => line.Contains("rawvideo", StringComparison.Ordinal));
        Assert.Null(read.Learned);
        Assert.Contains("no time was left to watch the picture for the station's watermark, so no watermark was learned or used", read.Note, StringComparison.Ordinal);
    }

    private static string AskedOf(RunningProgramme spawned)
    {
        try
        {
            return File.ReadAllText($"/proc/{spawned.ProcessId.ToString(CultureInfo.InvariantCulture)}/cmdline");
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private const int MarkLeft = 400;

    private string WatchedThrough(int unbrandedFrom, int unbrandedUntil)
    {
        string branded = Pictured("branded", marked: true);
        string plain = Pictured("plain", marked: false);

        return $$"""
            case "$*" in
                *silencedetect*)
                    echo '[silencedetect @ 0x1] silence_start: 1300.25' >&2
                    echo '[silencedetect @ 0x1] silence_end: 1300.75 | silence_duration: 0.5' >&2
                    echo '[silencedetect @ 0x1] silence_start: 1360.25' >&2
                    echo '[silencedetect @ 0x1] silence_end: 1360.75 | silence_duration: 0.5' >&2
                    ;;
                *rawvideo*)
                    i=0
                    while [ "$i" -lt {{PicturesWatched}} ]; do
                        at=$(({{WatchedFrom}} + i))
                        if [ "$at" -ge {{unbrandedFrom}} ] && [ "$at" -le {{unbrandedUntil}} ]; then cat "{{plain}}"; else cat "{{branded}}"; fi
                        echo "[Parsed_showinfo_3 @ 0x1] n: $i pts: 0 pts_time:$at" >&2
                        i=$((i + 1))
                    done
                    ;;
                *)
                    echo '[blackdetect @ 0x1] black_start:1300.2 black_end:1300.8 black_duration:0.6' >&2
                    echo '[blackdetect @ 0x1] black_start:1360.2 black_end:1360.8 black_duration:0.6' >&2
                    printf 'frame:0    pts:0 pts_time:1300.25\nlavfi.scene_score=0.900000\n'
                    printf 'frame:1    pts:0 pts_time:1360.25\nlavfi.scene_score=0.900000\n'
                    ;;
            esac
            """;
    }

    private string Pictured(string name, bool marked)
    {
        string path = tree.Under($"{name}.gray");

        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, Picture(marked));
        }

        return path;
    }

    private static byte[] Picture(bool marked)
    {
        byte[] picture = new byte[WatermarkFrame.Pixels];
        Array.Fill(picture, (byte)128);

        if (!marked)
        {
            return picture;
        }

        for (int y = 10; y <= 30; y++)
        {
            for (int x = MarkLeft; x <= 440; x++)
            {
                if (x <= MarkLeft + 1 || x >= 439 || y <= 11 || y >= 29)
                {
                    picture[WatermarkFrame.At(x, y)] = 255;
                }
            }
        }

        return picture;
    }

    private static WatermarkMask LearnedAhead()
    {
        var learner = new WatermarkLearner();
        byte[] branded = Picture(marked: true);

        for (int picture = 0; picture < WatermarkLearner.FewestFrames; picture++)
        {
            learner.Pictured(branded);
        }

        return learner.Learned() ?? throw new InvalidOperationException("The watermark drawn in the corner was not learned.");
    }

    private FfmpegChapterDetector Looking(string body)
        => new(
            new MachineSettings { Programme = Standing(body) },
            new EncodeSettings(),
            TimeProvider.System);

    private string Standing(string body)
        => StandInProgramme.Written(tree.Under($"ffmpeg-{Guid.NewGuid():N}"), body);
}
