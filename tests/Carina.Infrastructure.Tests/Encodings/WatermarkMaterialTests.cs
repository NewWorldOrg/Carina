using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Runs the detector against broadcasts synthesised with the ffmpeg the application itself runs, so
/// that the watch for a station's watermark is measured rather than believed: that a mark drawn in a
/// corner is learned from one recording, and that in the next recording of the same service — one
/// whose clock starts seventeen hours into the day — the pictures it is watched in land on the
/// artefact where they were shown. The next recording carries two pods that the sound and the dark
/// alone both take for breaks; the station took its mark off for one of them and left it on through
/// the other, so only the first is still a break once the mark learned ahead is watched for.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class WatermarkMaterialTests : IDisposable
{
    private const int Cores = 2;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LateInTheDay = TimeSpan.FromSeconds(61200);

    private static readonly Func<RunningProgramme, Task> Unwatched = _ => Task.CompletedTask;

    private readonly TempTree tree = new();

    public void Dispose() => tree.Dispose();

    [Fact(DisplayName = "a watermark learned from one recording takes away, in the next recording of the service, the pod it stayed on screen through, and leaves the pod it was taken off for")]
    public async Task AWatermarkLearnedFromOneRecordingJudgesTheNext()
    {
        string taught = await WritingAsync("taught", new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            WithCaptions = false,
            WithSuperimpose = false,
            Plain = true,
            Watermarked = true,
            Length = TimeSpan.FromSeconds(90),
        });

        ChapterDetection first = await Detecting().MarkAsync(taught, Service, await AlignedToAsync(taught), null, Cores, Unwatched, Cancel);

        Assert.NotNull(first.Learned);
        Assert.Contains("no watermark had been learned ahead", first.Note, StringComparison.Ordinal);

        TimeSpan opens = TimeSpan.FromSeconds(30);
        TimeSpan closes = TimeSpan.FromSeconds(90);
        string judged = await WritingAsync("judged", new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            WithCaptions = false,
            WithSuperimpose = false,
            Plain = true,
            Watermarked = true,
            Length = TimeSpan.FromSeconds(200),
            StartsAt = LateInTheDay,
            QuietBreaks = [opens, closes, TimeSpan.FromSeconds(130), TimeSpan.FromSeconds(160)],
            Unbranded = [(opens, closes + SyntheticBroadcast.QuietBreakLasts)],
        });
        EncodeTimeline timeline = await AlignedToAsync(judged);

        ChapterDetection blind = await Detecting().MarkAsync(judged, Service, timeline, null, Cores, Unwatched, Cancel);
        ChapterDetection seeing = await Detecting().MarkAsync(judged, Service, timeline, first.Learned, Cores, Unwatched, Cancel);

        Assert.Equal(ChapterVerdict.Marked, blind.Verdict);
        Assert.Equal(2, blind.Breaks);

        Assert.Equal(ChapterVerdict.Marked, seeing.Verdict);
        ChapterSegment gap = Assert.Single(seeing.Segments, segment => segment.Kind is ChapterKind.Break);
        Assert.InRange(gap.Starts, opens - Tolerance, opens + Tolerance);
        Assert.InRange(gap.Ends, closes - Tolerance, closes + Tolerance);
        Assert.Contains("1 of the 2 candidate breaks were taken away", seeing.Note, StringComparison.Ordinal);
        Assert.NotNull(seeing.Learned);
    }

    private static FfmpegChapterDetector Detecting()
        => new(new MachineSettings(), new EncodeSettings(), TimeProvider.System);

    private Task<string> WritingAsync(string name, SyntheticBroadcast broadcast)
        => broadcast.WriteAsync(tree.Under($"{name}{SyntheticBroadcast.TransportStream}"), Cancel);

    private static async Task<EncodeTimeline> AlignedToAsync(string broadcast)
    {
        var machine = new MachineSettings();
        SourceLengthReading whole =
            await new FfprobeSourceLength(machine, TimeProvider.System).ReadAsync(broadcast, Cancel);
        SourceHeadReading head =
            await new FfprobeSourceHead(machine, TimeProvider.System).ReadAsync(broadcast, Service, Cancel);

        Assert.True(whole.Measured, whole.Note);
        Assert.True(head.Measured, head.Note);

        return new EncodeTimeline(head.Start!.Value, head.HeadSkip!.Value, whole.Length, null);
    }
}
