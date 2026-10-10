using System.Runtime.Versioning;
using System.Text;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.DataBroadcast;

/// <summary>
/// Reads the data broadcast out of a synthetic broadcast ffmpeg wrote, with a carousel put into it, on the clock
/// ffmpeg stamped it with, across that clock coming around.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class LiveDataBroadcastMaterialTests : IDisposable
{
    private const int CarouselEvery = 40;

    private const int Mouthful = 64 * 1024;

    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private static readonly TimeSpan WhenTheClockComesAround =
        TimeSpan.FromTicks((long)(LivePts.ComesAroundAt * TimeSpan.TicksPerSecond / LivePts.Hertz));

    private static readonly TimeSpan Length = TimeSpan.FromSeconds(4);

    private static readonly CarouselModule Startup = new(
        0x0000,
        1,
        CarouselBroadcast.Resource("startup.bml", EntityWriter.BmlType, Encoding.ASCII.GetBytes("<bml><body><p>CARINA</p></body></bml>")));

    private static readonly CarouselModule Logo = new(
        0x0001,
        1,
        CarouselBroadcast.Resource("logo.png", EntityWriter.PngType, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));

    private readonly string room = Directory.CreateTempSubdirectory("carina-data-broadcast").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "BR-BD-004: the carousel of a broadcast ffmpeg wrote is read on the clock ffmpeg stamped, followed through its wrap")]
    public async Task TheCarouselIsReadOnTheClockFfmpegStampedFollowedThroughItsWrap()
    {
        TimeSpan startsAt = WhenTheClockComesAround - TimeSpan.FromSeconds(2);
        byte[] broadcast = await CarryingACarouselAsync(startsAt);
        CarouselReader reader = new(Service);
        List<CarouselSignalRead> reads = [];

        for (int at = 0; at < broadcast.Length; at += Mouthful)
        {
            reads.AddRange(reader.Read(broadcast.AsSpan(at, Math.Min(Mouthful, broadcast.Length - at))));
        }

        long from = Ticks(startsAt - TimeSpan.FromSeconds(2));
        long until = Ticks(startsAt + Length + TimeSpan.FromSeconds(2));

        Assert.IsType<CarouselSignal.Carried>(reads[0].Signal);
        Assert.Contains(reads, read => read.Signal is CarouselSignal.CatalogUpdated);
        Assert.Equal(
            [0, 1],
            reads.Select(read => read.Signal).OfType<CarouselSignal.ModuleCompleted>().Select(module => module.ModuleId).Distinct().Order());
        Assert.All(reads, read => Assert.InRange(read.At, from, until));
        Assert.Equal(reads.Select(read => read.At).Order(), reads.Select(read => read.At));
        EventMessage[] fired = [.. reads.Select(read => read.Signal).OfType<CarouselSignal.EventTimed>().Select(timed => timed.Message)];
        Assert.Contains(fired, message => message.FiresAt > (long)LivePts.ComesAroundAt);
        Assert.Contains(fired, message => message.FiresAt < (long)LivePts.ComesAroundAt);
        Assert.Equal(fired.Select(message => message.FiresAt).Order(), fired.Select(message => message.FiresAt));
        Assert.Equal(0, reader.RefusedChanges);
    }

    [Fact(DisplayName = "BR-BD-004: the frames made of a broadcast ffmpeg wrote leave the catalog and both modules for a viewer joining later")]
    public async Task TheFramesLeaveTheCatalogAndBothModulesForAViewerJoiningLater()
    {
        byte[] broadcast = await CarryingACarouselAsync(TimeSpan.FromHours(13));
        DataBroadcastSession session = new(Service, NullLogger.Instance);
        LiveFanout fanout = new(new LiveFanoutSettings());
        session.Show(fanout);

        await session.Seat.WriteAsync(broadcast);

        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            fanout.Kept.Select(SideChannelReading.KindOf));
        Assert.Equal(["startup.bml", "logo.png"], fanout.Kept.Skip(1).Select(frame => SideChannelReading.Module(frame).Resources[0].Path));
    }

    [Fact(DisplayName = "BR-BD-004: a viewer watching a broadcast ffmpeg wrote is handed the catalog when the service is found carrying it and when its download info is read, and not again as its modules arrive round after round")]
    public async Task AViewerWatchingIsHandedTheCatalogOnlyWhenTheListingChanges()
    {
        byte[] broadcast = await CarryingACarouselAsync(TimeSpan.FromHours(13));
        DataBroadcastSession session = new(Service, NullLogger.Instance);
        LiveFanout fanout = new(new LiveFanoutSettings());
        session.Show(fanout);
        await using ILiveViewing watching = await fanout.JoinAsync(CancellationToken.None) ?? throw new InvalidOperationException("A viewer joins.");

        await session.Seat.WriteAsync(broadcast);

        List<byte> kinds = [];

        while (watching.Frames.TryRead(out LiveFrame? frame))
        {
            kinds.Add(SideChannelReading.KindOf(frame));
        }

        Assert.Equal(
            [DataBroadcastFrames.CatalogKind, DataBroadcastFrames.CatalogKind, DataBroadcastFrames.ModuleKind, DataBroadcastFrames.ModuleKind],
            kinds.Where(kind => kind is not DataBroadcastFrames.EventKind));
        Assert.Contains(DataBroadcastFrames.EventKind, kinds);
    }

    private async Task<byte[]> CarryingACarouselAsync(TimeSpan startsAt)
    {
        string written = await (SyntheticBroadcast.AsMeasured() with
        {
            Length = Length,
            StartsAt = startsAt,
            WithCaptions = false,
            WithSuperimpose = false,
        }).WriteAsync(Path.Combine(room, "broadcast.m2ts"));

        byte[][] carousel =
        [
            new DiiWriter
            {
                Modules =
                [
                    DiiModule.Of(Startup.Id, Startup.Body.Length, Startup.Version),
                    DiiModule.Of(Logo.Id, Logo.Body.Length, Logo.Version),
                ],
            }.ToSection().ToBytes(),
            .. DsmCcWriter.Blocks(1, Startup.Id, Startup.Version, Startup.Body, DsmCcWriter.LargestBlock).Select(block => block.ToBytes()),
            .. DsmCcWriter.Blocks(1, Logo.Id, Logo.Version, Logo.Body, DsmCcWriter.LargestBlock).Select(block => block.ToBytes()),
        ];

        return CarouselSplice.Into(
            await File.ReadAllBytesAsync(written),
            SyntheticBroadcast.SomeProgramNumber,
            SyntheticBroadcast.PmtPid,
            round => [.. carousel, Fired(round)],
            CarouselEvery);
    }

    private static byte[] Fired(int round)
        => new StreamDescriptorWriter
        {
            DataEventId = CarouselBroadcast.DataEventId,
            EventMessageGroupId = 1,
            VersionNumber = round % 32,
            Descriptors = StreamDescriptorWriter.GeneralEvent(1, StreamDescriptorWriter.Immediate, 0, 1, round & 0xFFFF),
        }.ToSection().ToBytes();

    private static long Ticks(TimeSpan at) => (long)(at.Ticks * (Int128)StreamClock.Hertz / TimeSpan.TicksPerSecond);
}
