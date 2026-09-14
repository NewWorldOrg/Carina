using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

/// <summary>
/// Runs the detector against a broadcast synthesised with the ffmpeg the application itself runs,
/// so that what its filters say and where they say it happened is measured rather than believed.
/// One broadcast carries a pod of advertisements — two stretches quiet and dark at once, a whole
/// number of grid steps apart — and one carries a single such stretch, which is the shape a
/// programme without advertisements has and which must be marked nowhere.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class ChapterDetectionMaterialTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    private readonly TempTree tree = new();

    public void Dispose() => tree.Dispose();

    [Fact(DisplayName = "a pod of advertisements sixty seconds long is found where it was put, and the programme lies either side of it with no gap")]
    public async Task APodOfAdvertisementsIsFoundWhereItWasPut()
    {
        TimeSpan opens = TimeSpan.FromSeconds(30);
        TimeSpan closes = TimeSpan.FromSeconds(90);
        TimeSpan whole = TimeSpan.FromSeconds(130);
        string broadcast = await Broadcasting(whole, opens, closes);
        EncodeTimeline timeline = await AlignedTo(broadcast);

        ChapterDetection read = await Detecting().MarkAsync(broadcast, Service, timeline, Cancel);

        Assert.Equal(ChapterVerdict.Marked, read.Verdict);
        Assert.Equal(3, read.Segments.Count);
        Assert.Equal(1, read.Breaks);

        ChapterSegment gap = Assert.Single(read.Segments, segment => segment.Kind is ChapterKind.Break);
        Assert.InRange(gap.Starts, opens - Tolerance, opens + Tolerance);
        Assert.InRange(gap.Ends, closes - Tolerance, closes + Tolerance);
        Assert.InRange(gap.Length, closes - opens - Tolerance, closes - opens + Tolerance);
        Assert.Equal(TimeSpan.Zero, read.Segments[0].Starts);
        Assert.Equal(timeline.Expected, read.Segments[^1].Ends);
        Assert.InRange(read.BreakShare, 0.4, 0.5);
    }

    [Fact(DisplayName = "a programme carrying no advertisements goes quiet too, and one stretch on its own is marked nowhere")]
    public async Task AProgrammeCarryingNoAdvertisementsIsMarkedNowhere()
    {
        string broadcast = await Broadcasting(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(20));
        EncodeTimeline timeline = await AlignedTo(broadcast);

        ChapterDetection read = await Detecting().MarkAsync(broadcast, Service, timeline, Cancel);

        Assert.Equal(ChapterVerdict.NothingFound, read.Verdict);
        Assert.Empty(read.Segments);
        Assert.Equal(0d, read.BreakShare);
    }

    private static FfmpegChapterDetector Detecting()
        => new(new MachineSettings(), new EncodeSettings(), TimeProvider.System);

    private Task<string> Broadcasting(TimeSpan whole, params TimeSpan[] quiet)
        => new SyntheticBroadcast
        {
            Picture = SyntheticPicture.StandardDefinition,
            WithCaptions = false,
            WithSuperimpose = false,
            Length = whole,
            QuietBreaks = quiet,
        }.WriteAsync(tree.Under("broadcast" + SyntheticBroadcast.TransportStream), Cancel);

    private static async Task<EncodeTimeline> AlignedTo(string broadcast)
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
