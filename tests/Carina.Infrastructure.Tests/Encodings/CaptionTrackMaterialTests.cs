using System.Globalization;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Puts the text of a synthetic broadcast's captions into its artefact with the ffmpeg the application runs,
/// once while the artefact is encoded and once into an artefact already made, and reads the track back off
/// the file: one Japanese text track, off by default, whose cues change where the broadcast's captions
/// change on the artefact's clock, with the picture, the sound and the chapters as they were.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class CaptionTrackMaterialTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan HoursIntoTheDay = TimeSpan.FromHours(13);

    private static readonly TimeSpan SoundAhead = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    private static readonly DateTime TakenAt = EncodeHarness.Queued.AddMinutes(20);

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

    [Fact(DisplayName = "an artefact made before the captions were taken has the text track put in afterwards, and its picture, sound and chapters are the same bytes as before")]
    public async Task AnArtefactMadeBeforeTheCaptionsWereTakenHasTheTrackPutInAfterwards()
    {
        using EncodeHarness harness = OnThisMachine();
        (Recording recording, CaptionRecord record) = await CaptionedAsync(harness);
        harness.ChapterDetector = new ScriptedChapters
        {
            Answers = () => ChapterDetection.Marked(
                [
                    new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(4), ChapterKind.Programme),
                    new ChapterSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), ChapterKind.Break),
                    new ChapterSegment(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(10), ChapterKind.Programme),
                ],
                TimeSpan.FromSeconds(10),
                0.2),
        };
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));
        Assert.Null(job.CaptionTrack);

        string artefact = harness.ArtefactPathOf(job);
        string pictureAndSound = await PictureAndSoundAsync(artefact);
        string chapters = await ChaptersAsync(artefact);

        Assert.DoesNotContain("subtitle", await StreamsAsync(artefact), StringComparison.Ordinal);

        recording.Caption(CaptionState.Ready, record.Pictures, TakenAt);
        ArtefactCaptioningRound round = await Tracks(harness, job).CaptionAsync(4, _ => Task.FromResult(false), Cancel);

        Assert.Equal((1, 1), (round.Read, round.Added));
        Assert.Equal(EncodeCaptionTrack.Added, job.CaptionTrack);
        Assert.Equal(pictureAndSound, await PictureAndSoundAsync(artefact));
        Assert.Contains("subtitle", await StreamsAsync(artefact), StringComparison.Ordinal);
        Assert.Equal(chapters, await ChaptersAsync(artefact));
        Assert.Contains("start_time", chapters, StringComparison.Ordinal);

        await AssertCarriesTheTextAsync(artefact, record, job.Timeline!);
        Assert.Empty(Leftovers(harness));
    }

    private static async Task AssertCarriesTheTextAsync(string artefact, CaptionRecord record, EncodeTimeline timeline)
    {
        CaptionTrackCarried carried = Assert.IsType<CaptionTrackCarried>(
            CaptionTrackCarried.Read(await SaidAsync("ffprobe", FfmpegCaptionTrackInvocation.Carried(artefact))));
        CarriedSubtitle subtitle = Assert.Single(carried.Subtitles);

        Assert.Equal(new CarriedSubtitle("mov_text", "jpn", false), subtitle);

        IReadOnlyList<(TimeSpan From, TimeSpan Until, string Text)> cues = Cues(
            await SaidAsync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-i", artefact, "-map", "0:s:0", "-f", "webvtt", "-"]));
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

    private static ArtefactCaptionTracks Tracks(EncodeHarness harness, EncodeJob job)
        => new(
            new OneArtefact(job),
            harness.Jobs,
            harness.CaptionShelf,
            harness.CaptionTracks,
            harness.Places,
            harness.Placer,
            harness.Cleaner,
            new ArtefactOpenings(TimeProvider.System),
            TimeProvider.System,
            NullLogger<ArtefactCaptionTracks>.Instance);

    private static IEnumerable<string> Leftovers(EncodeHarness harness)
        => Directory.EnumerateFiles(harness.WorkDirectory)
            .Where(file => file.EndsWith(EncodeFileName.CaptionTrackExtension, StringComparison.Ordinal)
                           || file.EndsWith(EncodeFileName.CaptionedExtension, StringComparison.Ordinal));

    private static async Task<string> StreamsAsync(string file)
        => await SaidAsync("ffprobe", ["-hide_banner", "-loglevel", "error", "-show_entries", "stream=index,codec_type,codec_name", "-of", "csv=p=0", "-i", file]);

    private static async Task<string> ChaptersAsync(string file)
        => await SaidAsync("ffprobe", ["-hide_banner", "-loglevel", "error", "-show_chapters", "-of", "default=nw=1", "-i", file]);

    private static async Task<string> PictureAndSoundAsync(string artefact)
        => await SaidAsync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-i", artefact, "-map", "0:v", "-map", "0:a", "-c", "copy", "-f", "streamhash", "-"]);

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

    private static async Task<string> SaidAsync(string programme, IReadOnlyList<string> arguments)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(programme, arguments, TimeSpan.FromMinutes(1), TimeProvider.System, Cancel);

        Assert.True(said.ExitCode is 0, said.Complained);

        return said.Said;
    }

    private static EncodeHarness OnThisMachine()
    {
        EncodeHarness harness = new();
        harness.Programmes = new MachineSettings();
        harness.MachineReader = new MachineCapabilityReader(harness.Programmes, TimeProvider.System);
        harness.LengthReader = new FfprobeSourceLength(harness.Programmes, TimeProvider.System);
        harness.HeadReader = new FfprobeSourceHead(harness.Programmes, TimeProvider.System);

        return harness;
    }

    private sealed class OneArtefact(EncodeJob job) : ICaptionTrackWorklist
    {
        public Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CaptionTrackSubject>>(
                job.AwaitsCaptionTrack(TakenAt) ? [new CaptionTrackSubject(job, TakenAt)] : []);

        public Task<bool> StandsWithNothingInHandAsync(EncodeJob held, CancellationToken cancellationToken)
            => Task.FromResult(held.StandsAsTheArtefact);
    }
}
