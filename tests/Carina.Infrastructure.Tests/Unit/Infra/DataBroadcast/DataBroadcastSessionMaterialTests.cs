using System.Runtime.Versioning;

using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Streaming;
using Carina.Infrastructure.Tests.Fixtures.DataBroadcast;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Unit.Infra.DataBroadcast;

/// <summary>
/// Makes the frames of a live data broadcast out of a synthetic broadcast ffmpeg wrote, with a carousel put into it.
/// </summary>
[SupportedOSPlatform("linux")]
[Trait("Category", "Material")]
public sealed class DataBroadcastSessionMaterialTests : IDisposable
{
    private static readonly ServiceId Service = new(SyntheticBroadcast.SomeProgramNumber);

    private readonly string room = Directory.CreateTempSubdirectory("carina-data-broadcast").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "BR-BD-004: the frames made of a broadcast ffmpeg wrote leave the catalog and both modules for a viewer joining later")]
    public async Task TheFramesLeaveTheCatalogAndBothModulesForAViewerJoiningLater()
    {
        byte[] broadcast = await SplicedCarousel.CarryingACarouselAsync(room, TimeSpan.FromHours(13));
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
        byte[] broadcast = await SplicedCarousel.CarryingACarouselAsync(room, TimeSpan.FromHours(13));
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
}
