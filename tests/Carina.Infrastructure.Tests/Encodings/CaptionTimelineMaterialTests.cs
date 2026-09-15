using System.Globalization;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Encodes a synthetic broadcast with the ffmpeg the application runs and measures that its captions
/// keep their time. A caption is drawn from the source and the picture is played from the artefact,
/// so a caption lands on its picture only while the source's clock less the job's caption shift is the
/// artefact's clock. The broadcast here has a clock that begins hours into the day, sound that runs
/// ahead of its first picture, and a caption shown at the moment the picture goes dark, so that one
/// moment can be found on both clocks.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class CaptionTimelineMaterialTests
{
    private const double DarkLevel = 40;

    private const long TicksOnTheStreamClock = 90_000;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan HoursIntoTheDay = TimeSpan.FromHours(13);

    private static readonly TimeSpan SoundAhead = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan EncoderDelay = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan OneFrame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * 1001 / 30000);

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan CaptionShown = TimeSpan.FromSeconds(SyntheticBroadcast.CaptionShownAtSecond + 0.5);

    [Fact(DisplayName = "A caption the source shows over a picture is over that same picture in the artefact once the job's caption shift is taken off it, and the artefact is as long as the source had left after its head")]
    public async Task ACaptionIsOverTheSamePictureInTheArtefactOnceTheShiftIsTakenOff()
    {
        using EncodeHarness harness = OnThisMachine();
        string broadcast = await new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            Captions = SyntheticCaptions.ShownThenCleared,
            WithSuperimpose = false,
            Length = Whole,
            StartsAt = HoursIntoTheDay,
            PictureLateBy = SoundAhead,
            QuietBreaks = [CaptionShown],
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);
        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.True(ended is EncodeJobStatus.Completed, $"{ended}: {job.Failure?.Failure} {job.Failure?.Note}");
        EncodeTimeline timeline = job.Timeline!;
        string artefact = harness.ArtefactPathOf(job);

        Assert.True(timeline.SourceStart >= HoursIntoTheDay, $"the source's clock began at {timeline.SourceStart}, not hours into the day");
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

    private static EncodeHarness OnThisMachine()
    {
        var harness = new EncodeHarness();
        harness.Programmes = new MachineSettings();
        harness.MachineReader = new MachineCapabilityReader(harness.Programmes, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(harness.Programmes, TimeProvider.System);
        harness.HeadReader = new FfprobeSourceHead(harness.Programmes, TimeProvider.System);

        return harness;
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
}
