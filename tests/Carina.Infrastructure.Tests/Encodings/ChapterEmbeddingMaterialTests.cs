using System.Globalization;
using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Bakes chapters into an artefact with the ffmpeg the application itself runs, and reads them back
/// off it, because what ffmpeg does to a chapter it copies is a thing to measure rather than to
/// believe. Its <c>copy_chapters</c> moves every chapter back by the output seek and throws away
/// what that puts outside the output, so a file written on the artefact's own clock would arrive a
/// head skip early. This is where that is measured: a synthetic broadcast with a pod of
/// advertisements in it is encoded with a head of its own to skip, and the artefact has to carry
/// its chapters where they were meant to be, with the picture and the sound still in it.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class ChapterEmbeddingMaterialTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan HeadOfItsOwn = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(40);

    private static readonly TimeSpan PodOpens = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PodCloses = TimeSpan.FromSeconds(25);

    [Fact(DisplayName = "A-エンコード-057: an artefact carries its chapters where the ledger says they are on its own clock, and still carries its picture and its sound")]
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
            Length = Whole,
            QuietBreaks = [PodOpens, PodCloses],
        }.WriteAsync(harness.Room.Under($"broadcast{SyntheticBroadcast.TransportStream}"), Cancel);

        SourceLengthReading source = await harness.LengthReader.ReadAsync(broadcast, Cancel);
        Assert.True(source.Measured, source.Note);

        TimeSpan artefactLength = source.Length!.Value - HeadOfItsOwn;
        TimeSpan opens = PodOpens - HeadOfItsOwn;
        TimeSpan closes = PodCloses - HeadOfItsOwn;
        ChapterDetection pod = ChapterDetection.Marked(
            [
                new ChapterSegment(TimeSpan.Zero, opens, ChapterKind.Programme),
                new ChapterSegment(opens, closes, ChapterKind.Break),
                new ChapterSegment(closes, artefactLength, ChapterKind.Programme),
            ],
            artefactLength,
            (closes - opens) / artefactLength);
        harness.ChapterDetector = new ScriptedChapters { Answers = () => pod };

        Recording recording = harness.RecordedFrom(broadcast, SyntheticBroadcast.SomeProgramNumber);
        EncodeProfile profile = harness.Defined();
        EncodeJob job = harness.Running(recording.Id, profile.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(HeadOfItsOwn, job.Timeline!.HeadSkip);
        Assert.Equal(ChapterVerdict.Marked, job.Chapters!.Verdict);
        Assert.Equal(ChapterDetectorName.Ffmpeg, job.Chapters.Detector);

        string artefact = harness.ArtefactPathOf(job);
        IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)> carried = await ChaptersOfAsync(artefact);

        Assert.Equal(3, carried.Count);
        Assert.Equal(["本編", "CM", "本編"], carried.Select(chapter => chapter.Title));
        Assert.Equal(TimeSpan.Zero, carried[0].Starts);
        Assert.InRange(carried[0].Ends, opens - Tolerance, opens + Tolerance);
        Assert.InRange(carried[1].Starts, opens - Tolerance, opens + Tolerance);
        Assert.InRange(carried[1].Ends, closes - Tolerance, closes + Tolerance);
        Assert.InRange(carried[2].Starts, closes - Tolerance, closes + Tolerance);
        Assert.InRange(carried[2].Ends, artefactLength - Tolerance, artefactLength + Tolerance);

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

    [Fact(DisplayName = "A-エンコード-057: an artefact of a run that marked nothing carries no chapters at all, and the streams in it are the ones a run without chapters always made")]
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

    private static double Rounded(double seconds) => Math.Round(seconds, 1);

    private static async Task<IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, string Title)>> ChaptersOfAsync(string file)
        => [.. Probed(await AskedAsync(["-show_chapters", "-i", file]))
            .Where(record => record.Holds("start_time"))
            .Select(record => (
                Seconds(record.Value("start_time")),
                Seconds(record.Value("end_time")),
                record.Value("TAG:title") ?? string.Empty))];

    private static async Task<IReadOnlyList<string>> CodecsOfAsync(string file)
        => [.. Probed(await AskedAsync(["-of", "default=nw=1", "-show_entries", "stream=codec_name", "-i", file]))
            .Select(record => record.Value("codec_name"))
            .OfType<string>()];

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
    /// A source whose first picture is declared to lie a fixed way into it. A broadcast synthesised
    /// here starts its picture where the container starts, which would leave nothing for the encode
    /// to seek past and nothing for ffmpeg to move the chapters by — and the shift is what is being
    /// measured. Where the source's clock begins is still read off the file itself.
    /// </summary>
    private sealed class AHeadOfItsOwn(ISourceHeadReader read, TimeSpan skip) : ISourceHeadReader
    {
        public async Task<SourceHeadReading> ReadAsync(string source, ServiceId service, CancellationToken cancellationToken)
        {
            SourceHeadReading head = await read.ReadAsync(source, service, cancellationToken);

            return head.Start is { } start ? SourceHeadReading.Read(start, start + skip) : head;
        }
    }
}
