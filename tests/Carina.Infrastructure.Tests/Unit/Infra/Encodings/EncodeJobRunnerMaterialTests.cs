using System.Globalization;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Playback;
using Carina.Infrastructure.Streaming;
using Carina.Infrastructure.Tests.Fixtures.Encodings;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Unit.Infra.Encodings;

/// <summary>
/// Runs a job through the ffmpeg the application runs, against a synthetic broadcast, and reads the
/// artefact back off the file: where it is placed and how long it is, that its captions keep their time
/// once the job's caption shift is taken off them, that it carries the chapters of a detection, and that
/// captions taken before the run go in as one Japanese text track, off by default.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class EncodeJobRunnerMaterialTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(500);

    private const double DarkLevel = 40;

    private const long TicksOnTheStreamClock = 90_000;

    private static readonly TimeSpan HoursIntoTheDay = TimeSpan.FromHours(13);

    private static readonly TimeSpan SoundAhead = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan EncoderDelay = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan OneFrame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * 1001 / 30000);

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan CaptionShown = TimeSpan.FromSeconds(SyntheticBroadcast.CaptionShownAtSecond + 0.5);

    private static readonly TimeSpan WhatTheMuxerAddsToTheHead = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan WhenTheClockComesAround =
        TimeSpan.FromTicks((long)(LivePts.ComesAroundAt * TimeSpan.TicksPerSecond / LivePts.Hertz));

    private static readonly TimeSpan ToleranceForChapters = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan HeadOfItsOwn = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan WholeForChapters = TimeSpan.FromSeconds(40);

    private static readonly TimeSpan PodOpens = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PodCloses = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    private static readonly DateTime TakenAt = EncodeHarness.Queued.AddMinutes(20);

    [Fact(DisplayName = "a synthetic broadcast is encoded to an artefact as long as the source, named for the recording and the profile, and the job completes")]
    public async Task ASyntheticBroadcastIsEncodedToAnArtefactAsLongAsTheSource()
    {
        using EncodeHarness harness = OnThisMachine();
        string broadcast = await SyntheticBroadcast.AsMeasured().WriteAsync(harness.Room.Under("broadcast.m2ts"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeProfile profile = harness.Defined();
        EncodeJob job = harness.Running(recording.Id, profile.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeFileName.Artefact(recording.Id, profile.Id), job.ArtefactName);
        string artefact = harness.ArtefactPathOf(job);
        Assert.True(File.Exists(artefact));
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, Assert.Single(harness.Scratch.Files).Fate);

        SourceLengthReading source = await new FfprobeSourceLength(new MachineSettings(), TimeProvider.System).ReadAsync(harness.SourcePathOf(recording), Cancel);
        SourceLengthReading made = await new FfprobeSourceLength(new MachineSettings(), TimeProvider.System).ReadAsync(artefact, Cancel);

        Assert.True(source.Measured, source.Note);
        Assert.True(made.Measured, made.Note);
        Assert.InRange(made.Length!.Value, source.Length!.Value - Tolerance, source.Length.Value + Tolerance);
        Assert.Contains(harness.RunnerLog.Said, line => line.Contains("100% of the way through", StringComparison.Ordinal));
        Assert.Equal(["h264", "aac"], await CodecsOfAsync(artefact));
        Assert.Equal(EncodeCodec.H264, (await CodecReadFromAsync(job, artefact)).Codec);

        EncodeTimeline timeline = job.Timeline!;
        Assert.NotNull(timeline);
        Assert.True(EncodeTimeline.WithinReach(timeline.HeadSkip));
        Assert.Equal(source.Length.Value, timeline.SourceLength);
        Assert.Equal(made.Length.Value, timeline.ArtefactLength);
        Assert.True(timeline.LengthsAgree, $"the artefact came out {timeline.Drift} from what the source had left");
    }

    [Fact(DisplayName = "a file that is no broadcast at all fails the job with ffmpeg's exit code and its words, without a path in them, and leaves nothing behind but the recording")]
    public async Task AFileThatIsNoBroadcastFailsTheJobWithFfmpegsWords()
    {
        using EncodeHarness harness = OnThisMachine();
        Recording recording = harness.Recorded("this is not a transport stream");
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.FfmpegExitedNonZero, job.Failure!.Failure);
        Assert.Contains("the programme exited", job.Failure.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Room.Root, job.Failure.Note, StringComparison.Ordinal);
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.False(File.Exists(harness.ArtefactPathOf(job)));
        Assert.True(File.Exists(harness.SourcePathOf(recording)));
        Assert.Equal([recording.FileName.Value], Directory.EnumerateFiles(harness.Room.Root).Select(Path.GetFileName));
        Assert.Empty(harness.Shelf.Snapshot());
    }

    [Fact(DisplayName = "H.265 runs on the card when this machine has one and is refused as capability unavailable when it has not, without the profile being touched")]
    public async Task H265RunsOnTheCardOrIsRefusedAsCapabilityUnavailable()
    {
        using EncodeHarness harness = OnThisMachine();
        MachineCapabilities can = await harness.MachineReader.ReadAsync(Cancel);
        string broadcast = await SyntheticBroadcast.AsMeasured().WriteAsync(harness.Room.Under("broadcast.m2ts"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeJob job = harness.Running(recording.Id, harness.Defined(EncodeCodec.H265).Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        if (can.Has(Faculty.EncodeH265OnTheCard))
        {
            Assert.True(ended is EncodeJobStatus.Completed, $"{ended}: {job.Failure?.Failure} {job.Failure?.Note}");
            Assert.Equal(["hevc", "aac"], await CodecsOfAsync(harness.ArtefactPathOf(job)));
        }
        else
        {
            Assert.Equal(EncodeJobStatus.Failed, ended);
            Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        }
    }

    [Fact(DisplayName = "A caption the source shows over a picture is over that same picture in the artefact once the job's caption shift is taken off it, and the artefact is as long as the source had left after its head")]
    public async Task ACaptionIsOverTheSamePictureInTheArtefactOnceTheShiftIsTakenOff()
    {
        EncodeTimeline timeline = await CaptionedAndEncodedFromAsync(HoursIntoTheDay);

        Assert.True(timeline.SourceStart >= HoursIntoTheDay, $"the source's clock began at {timeline.SourceStart}, not hours into the day");
    }

    [Theory(DisplayName = "A source whose clock begins shortly before it comes around is encoded, begins before zero on the timeline, and keeps its captions over the same pictures whether or not the clock comes around inside it")]
    [InlineData(5)]
    [InlineData(50)]
    public async Task ASourceBeginningShortlyBeforeTheClockComesAroundIsEncodedWithItsCaptionsInPlace(int secondsBefore)
    {
        EncodeTimeline timeline = await CaptionedAndEncodedFromAsync(WhenTheClockComesAround - TimeSpan.FromSeconds(secondsBefore));

        Assert.InRange(timeline.SourceStart, -TimeSpan.FromSeconds(secondsBefore), -TimeSpan.FromSeconds(secondsBefore) + WhatTheMuxerAddsToTheHead);
    }

    [Theory(DisplayName = "A broadcast whose sound runs further ahead of its first picture than a run skips is refused as head too far off the file itself, before any encode is started")]
    [InlineData(5.5)]
    [InlineData(8)]
    public async Task SoundFurtherAheadThanARunSkipsIsRefusedAsHeadTooFar(double ahead)
    {
        using EncodeHarness harness = OnThisMachine();
        string broadcast = await new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            WithCaptions = false,
            WithSuperimpose = false,
            Length = Whole,
            StartsAt = HoursIntoTheDay,
            PictureLateBy = TimeSpan.FromSeconds(ahead),
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.HeadTooFar, job.Failure!.Failure);
        Assert.Null(job.Timeline);
        Assert.Empty(harness.Scratch.Files);
        Assert.False(File.Exists(harness.ArtefactPathOf(job)));
        Assert.True(File.Exists(harness.SourcePathOf(recording)));
    }

    [Fact(DisplayName = "an artefact carries its chapters where the ledger says they are on its own clock, and still carries its picture and its sound")]
    public async Task AnArtefactCarriesItsChaptersWhereTheLedgerSaysTheyAre()
    {
        using var harness = new EncodeHarness();
        var machine = new MachineSettings();
        harness.Programmes = machine;
        harness.MachineReader = new MachineCapabilityReader(machine, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(machine, TimeProvider.System);
        harness.HeadReader = new AHeadOfItsOwn(new FfprobeSourceHead(machine, TimeProvider.System), HeadOfItsOwn);

        string broadcast = await new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            WithCaptions = false,
            WithSuperimpose = false,
            Length = WholeForChapters,
            QuietBreaks = [PodOpens, PodCloses],
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);

        SourceLengthReading source = await harness.LengthReader.ReadAsync(broadcast, Cancel);
        Assert.True(source.Measured, source.Note);

        TimeSpan artefactLength = source.Length!.Value - HeadOfItsOwn;
        TimeSpan opens = PodOpens - HeadOfItsOwn;
        TimeSpan closes = PodCloses - HeadOfItsOwn;

        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        harness.Detected(
            recording,
            SegmentDetectionState.Marked,
            new SegmentSpan(TimeSpan.Zero, PodOpens, SegmentKind.Programme, 0.9),
            new SegmentSpan(PodOpens, PodCloses, SegmentKind.Break, 0.9),
            new SegmentSpan(PodCloses, source.Length.Value, SegmentKind.Programme, 0.9));
        EncodeProfile profile = harness.Defined();
        EncodeJob job = harness.Running(recording.Id, profile.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(HeadOfItsOwn, job.Timeline!.HeadSkip);
        Assert.Equal(ChapterVerdict.Marked, job.Chapters!.Verdict);
        Assert.Equal(ChapterDetectorName.Segments, job.Chapters.Detector);

        string artefact = harness.ArtefactPathOf(job);
        IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)> carried = await ChaptersOfAsync(artefact);

        Assert.Equal(3, carried.Count);
        Assert.Equal(["本編", "CM", "本編"], carried.Select(chapter => chapter.Title));
        Assert.Equal(TimeSpan.Zero, carried[0].Starts);
        Assert.InRange(carried[0].Ends, opens - ToleranceForChapters, opens + ToleranceForChapters);
        Assert.InRange(carried[1].Starts, opens - ToleranceForChapters, opens + ToleranceForChapters);
        Assert.InRange(carried[1].Ends, closes - ToleranceForChapters, closes + ToleranceForChapters);
        Assert.InRange(carried[2].Starts, closes - ToleranceForChapters, closes + ToleranceForChapters);
        Assert.InRange(carried[2].Ends, artefactLength - ToleranceForChapters, artefactLength + ToleranceForChapters);

        Assert.Equal(["h264", "aac", "bin_data"], await CodecsOfAsync(artefact));
        Assert.Equal(
            [ChapterKind.Programme, ChapterKind.Break, ChapterKind.Programme],
            harness.Chapters.Chapters.Select(chapter => chapter.Kind));
        Assert.Equal(
            carried.Select(chapter => chapter.Starts.TotalSeconds).Select(Rounded),
            harness.Chapters.Chapters.Select(chapter => chapter.StartsAt.TotalSeconds).Select(Rounded));

        SourceLengthReading made = await harness.LengthReader.ReadAsync(artefact, Cancel);
        Assert.True(made.Measured, made.Note);
        Assert.InRange(made.Length!.Value, artefactLength - TimeSpan.FromSeconds(1), artefactLength + TimeSpan.FromSeconds(1));
        Assert.False(File.Exists(harness.ChaptersPathOf(job)), "the chapters file is swept once the job has ended");
    }

    [Fact(DisplayName = "an artefact of a recording no version has detected carries no chapters at all, and the streams in it are the ones a run without chapters always made")]
    public async Task AnArtefactOfARunThatMarkedNothingCarriesNoChapters()
    {
        using var harness = new EncodeHarness();
        var machine = new MachineSettings();
        harness.Programmes = machine;
        harness.MachineReader = new MachineCapabilityReader(machine, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(machine, TimeProvider.System);
        harness.HeadReader = new FfprobeSourceHead(machine, TimeProvider.System);

        string broadcast = await SyntheticBroadcast.AsMeasured()
            .WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        Assert.Empty(await ChaptersOfAsync(harness.ArtefactPathOf(job)));
        Assert.Equal(["h264", "aac"], await CodecsOfAsync(harness.ArtefactPathOf(job)));
        Assert.Equal(ChapterVerdict.NotAsked, job.Chapters!.Verdict);
        Assert.Empty(harness.Chapters.Chapters);
    }

    [Fact(DisplayName = "BR-CD-010: an artefact encoded from a detection carries its stretches at the recording's own time less the head skip, titled for the programme and the break")]
    public async Task AnArtefactEncodedFromADetectionCarriesItsStretchesWhereTheyWereDetected()
    {
        using var harness = new EncodeHarness();
        (EncodeJob job, TimeSpan length) = await EncodedFromADetectionAsync(harness, PodOpens, PodCloses);
        TimeSpan artefactLength = length - HeadOfItsOwn;

        IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)> carried = await ChaptersOfAsync(harness.ArtefactPathOf(job));

        Assert.Equal(["本編", "CM", "本編"], carried.Select(chapter => chapter.Title));
        AssertNear([(TimeSpan.Zero, PodOpens - HeadOfItsOwn), (PodOpens - HeadOfItsOwn, PodCloses - HeadOfItsOwn), (PodCloses - HeadOfItsOwn, artefactLength)], carried);
        Assert.Equal((ChapterDetectorName.Segments, ChapterVerdict.Marked, 1), (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
        Assert.Equal(
            carried.Select(chapter => chapter.Starts.TotalSeconds).Select(Rounded),
            harness.Chapters.Chapters.Select(chapter => chapter.StartsAt.TotalSeconds).Select(Rounded));
        Assert.Equal(["h264", "aac", "bin_data"], await CodecsOfAsync(harness.ArtefactPathOf(job)));
    }

    [Fact(DisplayName = "an artefact encoded after the captions were taken carries them as one Japanese text track, off by default, on its own clock")]
    public async Task AnArtefactEncodedAfterTheCaptionsWereTakenCarriesThemAsAText()
    {
        using EncodeHarness harness = OnThisMachine();
        (Recording recording, CaptionRecord record) = await CaptionedAsync(harness);
        recording.Caption(CaptionState.Ready, record.Pictures, TakenAt);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.True(ended is EncodeJobStatus.Completed, $"{ended}: {job.Failure?.Failure} {job.Failure?.Note}");
        Assert.Equal((EncodeCaptionTrack.Added, TakenAt), (job.CaptionTrack!.Value, job.CaptionTrackFrom!.Value));

        string artefact = harness.ArtefactPathOf(job);

        await AssertCarriesTheTextAsync(artefact, record, job.Timeline!);
        Assert.Empty(Leftovers(harness));
    }

    private static Task<ArtefactCodecReading> CodecReadFromAsync(EncodeJob job, string artefact)
        => new FfprobeArtefactCodecs(
                new LocalPlaybackFileStore(
                    new IntegritySettings(),
                    new EncodeSettings { OutputRoots = [new StorageRootPath(job.OutputRoot, Path.GetDirectoryName(artefact)!)] },
                    NullLogger<LocalPlaybackFileStore>.Instance),
                new MachineSettings(),
                TimeProvider.System)
            .ReadAsync(
                new PlaybackFile(job.OutputRoot, new RecordingFileName(job.ArtefactName!.Value), new FileInfo(artefact).Length),
                Cancel);

    private static EncodeHarness OnThisMachine()
    {
        var harness = new EncodeHarness();
        harness.Programmes = new MachineSettings();
        harness.MachineReader = new MachineCapabilityReader(harness.Programmes, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(harness.Programmes, TimeProvider.System);
        harness.HeadReader = new FfprobeSourceHead(harness.Programmes, TimeProvider.System);

        return harness;
    }

    private static async Task<IReadOnlyList<string>> CodecsOfAsync(string file)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            "ffprobe",
            ["-hide_banner", "-loglevel", "error", "-of", "default=nw=1", "-show_entries", "stream=codec_name", "-i", file],
            TimeSpan.FromSeconds(30),
            TimeProvider.System,
            Cancel);

        Assert.Equal(0, said.ExitCode);

        return [.. FfprobeRecords.From(said.Said).Select(record => record.Value("codec_name")).OfType<string>()];
    }

    private static async Task<EncodeTimeline> CaptionedAndEncodedFromAsync(TimeSpan startsAt)
    {
        using EncodeHarness harness = OnThisMachine();
        string broadcast = await new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            Captions = SyntheticCaptions.ShownThenCleared,
            WithSuperimpose = false,
            Length = Whole,
            StartsAt = startsAt,
            PictureLateBy = SoundAhead,
            QuietBreaks = [CaptionShown],
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.True(ended is EncodeJobStatus.Completed, $"{ended}: {job.Failure?.Failure} {job.Failure?.Note}");
        EncodeTimeline timeline = job.Timeline!;
        string artefact = harness.ArtefactPathOf(job);

        Assert.InRange(timeline.HeadSkip, SoundAhead, SoundAhead + EncoderDelay);

        IReadOnlyList<TimeSpan> statements = await CaptionStatementsAsync(broadcast);
        Assert.Equal(2, statements.Count);
        TimeSpan shown = statements[0];
        TimeSpan cleared = statements[1];
        TimeSpan darkInTheSource = await FirstDarkPictureAsync(broadcast);
        TimeSpan darkInTheArtefact = await FirstDarkPictureAsync(artefact);

        Assert.InRange(darkInTheSource - shown, TimeSpan.Zero, OneFrame);
        Assert.InRange(darkInTheArtefact - (darkInTheSource - timeline.CaptionShift), -(OneFrame / 2), OneFrame / 2);
        Assert.InRange(darkInTheArtefact - (shown - timeline.CaptionShift), -(OneFrame / 2), OneFrame * 1.5);

        SourceLengthReading made = await harness.LengthReader.ReadAsync(artefact, Cancel);
        Assert.True(made.Measured, made.Note);
        Assert.Equal(made.Length!.Value, timeline.ArtefactLength);
        Assert.InRange(cleared - timeline.CaptionShift, shown - timeline.CaptionShift, made.Length.Value);
        Assert.True(timeline.LengthsAgree, $"the artefact came out {timeline.Drift} from what the source had left");
        Assert.InRange(timeline.Drift!.Value, -OneFrame, OneFrame);

        return timeline;
    }

    private static async Task<IReadOnlyList<TimeSpan>> CaptionStatementsAsync(string broadcast)
    {
        IReadOnlyList<string> said = await SaidAsync(["-select_streams", "s:0", "-show_entries", "packet=pts", "-of", "csv=p=0", "-i", broadcast]);
        long[] stamped = [.. said.Select(line => long.Parse(line.TrimEnd(','), CultureInfo.InvariantCulture))];
        long management = stamped.Min();

        return
        [
            .. stamped
                .Where(pts => (pts - management) % TicksOnTheStreamClock == TicksOnTheStreamClock / 2)
                .Order()
                .Select(pts => TimeSpan.FromTicks(pts * TimeSpan.TicksPerSecond / TicksOnTheStreamClock)),
        ];
    }

    private static async Task<TimeSpan> FirstDarkPictureAsync(string file)
    {
        Assert.True(file.IndexOfAny([':', ',', ';', '\'', '\\', '[', ']']) < 0, $"'{file}' cannot be named in a filter graph as it stands");

        IReadOnlyList<string> said = await SaidAsync(
        [
            "-f",
            "lavfi",
            "-i",
            $"movie={file},signalstats",
            "-show_entries",
            "frame=best_effort_timestamp_time:frame_tags=lavfi.signalstats.YAVG",
            "-of",
            "csv=p=0",
        ]);
        TimeSpan? dark = said.Select(Dark).FirstOrDefault(moment => moment is not null);

        Assert.True(dark is not null, $"no picture in {Path.GetFileName(file)} went dark");

        return dark.Value;
    }

    private static TimeSpan? Dark(string line)
    {
        string[] fields = line.Split(',');

        return fields.Length >= 2
            && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double level)
            && level < DarkLevel
                ? TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond))
                : null;
    }

    private static async Task<IReadOnlyList<string>> SaidAsync(IReadOnlyList<string> arguments)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            "ffprobe",
            ["-hide_banner", "-loglevel", "error", .. arguments],
            TimeSpan.FromMinutes(2),
            TimeProvider.System,
            Cancel);

        Assert.True(said.ExitCode is 0, said.Complained);

        return [.. said.Said.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static async Task<(EncodeJob Job, TimeSpan Length)> EncodedFromADetectionAsync(EncodeHarness harness, TimeSpan opens, TimeSpan closes)
    {
        var machine = new MachineSettings();
        harness.Programmes = machine;
        harness.MachineReader = new MachineCapabilityReader(machine, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(machine, TimeProvider.System);
        harness.HeadReader = new AHeadOfItsOwn(new FfprobeSourceHead(machine, TimeProvider.System), HeadOfItsOwn);

        string broadcast = await new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            WithCaptions = false,
            WithSuperimpose = false,
            Length = WholeForChapters,
            QuietBreaks = [opens, closes],
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);

        SourceLengthReading source = await harness.LengthReader.ReadAsync(broadcast, Cancel);
        Assert.True(source.Measured, source.Note);
        TimeSpan length = source.Length!.Value;

        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        harness.Detected(
            recording,
            SegmentDetectionState.Marked,
            new SegmentSpan(TimeSpan.Zero, opens, SegmentKind.Programme, 0.9),
            new SegmentSpan(opens, closes, SegmentKind.Break, 0.9),
            new SegmentSpan(closes, length, SegmentKind.Programme, 0.9));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));
        Assert.Equal(HeadOfItsOwn, job.Timeline!.HeadSkip);

        return (job, length);
    }

    private static void AssertNear(IReadOnlyList<(TimeSpan Starts, TimeSpan Ends)> meant, IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)> carried)
    {
        Assert.Equal(meant.Count, carried.Count);

        foreach (((TimeSpan Starts, TimeSpan Ends) wanted, (TimeSpan Starts, TimeSpan Ends, string Title) read) in meant.Zip(carried))
        {
            Assert.InRange(read.Starts, wanted.Starts - ToleranceForChapters, wanted.Starts + ToleranceForChapters);
            Assert.InRange(read.Ends, wanted.Ends - ToleranceForChapters, wanted.Ends + ToleranceForChapters);
        }
    }

    private static double Rounded(double seconds) => Math.Round(seconds, 1);

    private static async Task<IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)>> ChaptersOfAsync(string file)
        => [.. Probed(await AskedAsync(["-show_chapters", "-i", file]))
            .Where(record => record.Holds("start_time"))
            .Select(record => (
                Seconds(record.Value("start_time")),
                Seconds(record.Value("end_time")),
                record.Value("TAG:title") ?? string.Empty))];

    private static IReadOnlyList<FfprobeRecord> Probed(string said) => FfprobeRecords.From(said);

    private static TimeSpan Seconds(string? reported)
        => TimeSpan.FromSeconds(double.Parse(reported!, CultureInfo.InvariantCulture));

    private static async Task<string> AskedAsync(IReadOnlyList<string> arguments)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            "ffprobe",
            ["-hide_banner", "-loglevel", "error", .. arguments],
            TimeSpan.FromSeconds(30),
            TimeProvider.System,
            Cancel);

        Assert.Equal(0, said.ExitCode);

        return said.Said;
    }

    /// <summary>
    /// A source whose first picture is declared to lie a fixed way into it. Where the source's clock
    /// begins is still read off the file itself.
    /// </summary>
    private sealed class AHeadOfItsOwn(ISourceHeadReader read, TimeSpan skip) : ISourceHeadReader
    {
        public async Task<SourceHeadReading> ReadAsync(string source, ServiceId service, CancellationToken cancellationToken)
        {
            SourceHeadReading head = await read.ReadAsync(source, service, cancellationToken);

            return head.Start is { } start ? SourceHeadReading.Read(start, start + skip) : head;
        }
    }

    private static async Task AssertCarriesTheTextAsync(string artefact, CaptionRecord record, EncodeTimeline timeline)
    {
        CaptionTrackCarried carried = Assert.IsType<CaptionTrackCarried>(
            CaptionTrackCarried.Read(await SaidAsyncForCaptionTracks("ffprobe", FfmpegCaptionTrackInvocation.Carried(artefact))));
        CarriedSubtitle subtitle = Assert.Single(carried.Subtitles);

        Assert.Equal(new CarriedSubtitle("mov_text", "jpn", false), subtitle);

        IReadOnlyList<(TimeSpan From, TimeSpan Until, string Text)> cues = Cues(
            await SaidAsyncForCaptionTracks("ffmpeg", ["-hide_banner", "-loglevel", "error", "-i", artefact, "-map", "0:s:0", "-f", "webvtt", "-"]));
        IReadOnlyList<(TimeSpan From, TimeSpan Until, string Text)> expected = Cues(
            CaptionTrackFile.Written(record.Lines!, timeline.CaptionShift, timeline.ArtefactLength)!);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(cue => cue.Text), cues.Select(cue => cue.Text));
        Assert.All(
            expected.Zip(cues),
            pair => Assert.InRange((pair.Second.From - pair.First.From).Duration(), TimeSpan.Zero, OneMillisecond));

        TimeSpan firstWithin = record.Lines!
            .Where(line => !line.Clears)
            .Select(line => line.At - timeline.CaptionShift)
            .First(at => at > TimeSpan.Zero);

        Assert.Contains(cues, cue => (cue.From - firstWithin).Duration() <= OneMillisecond);
    }

    private static async Task<(Recording Recording, CaptionRecord Record)> CaptionedAsync(EncodeHarness harness)
    {
        string broadcast = await new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            Captions = SyntheticCaptions.EverySecond,
            WithSuperimpose = false,
            Length = Whole,
            StartsAt = HoursIntoTheDay,
            PictureLateBy = SoundAhead,
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        MachineSettings machine = new();
        FfmpegCaptionTranscriber transcriber = new(
            machine,
            new CaptionSettings { LongestTranscription = TimeSpan.FromMinutes(2) },
            new FfprobeStreamAttributeReader(new StreamAttributeSettings(), TimeProvider.System),
            TimeProvider.System);
        CaptionTranscription taken = await transcriber.TranscribeAsync(
            harness.SourcePathOf(recording),
            new ServiceId(SyntheticBroadcast.SomeProgramNumber),
            Cancel);
        CaptionRecord record = Assert.IsType<CaptionRecord>(taken.Record);

        Assert.NotNull(record.Lines);
        await harness.CaptionShelf.KeepAsync(recording.Id, record, Cancel);

        return (recording, record);
    }

    private static IEnumerable<string> Leftovers(EncodeHarness harness)
        => Directory.EnumerateFiles(harness.WorkDirectory)
            .Where(file => file.EndsWith(EncodeFileName.CaptionTrackExtension, StringComparison.Ordinal)
                           || file.EndsWith(EncodeFileName.CaptionedExtension, StringComparison.Ordinal));

    private static IReadOnlyList<(TimeSpan From, TimeSpan Until, string Text)> Cues(string vtt)
    {
        string[] blocks = vtt.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return
        [
            .. blocks
                .Select(block => block.Split('\n'))
                .Where(lines => lines.Length > 1 && lines[0].Contains("-->", StringComparison.Ordinal))
                .Select(lines => (Moment(lines[0].Split("-->")[0]), Moment(lines[0].Split("-->")[1]), string.Join('\n', lines[1..]))),
        ];
    }

    private static TimeSpan Moment(string written)
    {
        string trimmed = written.Trim();
        string[] parts = trimmed.Split(':');
        string padded = parts.Length is 2 ? "00:" + trimmed : trimmed;

        return TimeSpan.ParseExact(padded, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static async Task<string> SaidAsyncForCaptionTracks(string programme, IReadOnlyList<string> arguments)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(programme, arguments, TimeSpan.FromMinutes(1), TimeProvider.System, Cancel);

        Assert.True(said.ExitCode is 0, said.Complained);

        return said.Said;
    }
}
