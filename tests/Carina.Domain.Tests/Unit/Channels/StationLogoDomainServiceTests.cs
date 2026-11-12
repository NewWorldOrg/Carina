using Carina.Domain.Channels;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Channels;

[Trait("Category", "Unit")]
public sealed class StationLogoDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkId Network = new(4);

    private readonly HeldServices services = new();

    private StationLogoDomainService Links => new(services);

    [Fact(DisplayName = "BR-TD-009: a link to a station not held is dropped")]
    public async Task ALinkToAStationNotHeldIsDropped()
        => Assert.Null(await Links.LinkAsync(Network, new ServiceId(101), new LogoId(5), Cancel));

    [Fact(DisplayName = "BR-TD-009: a link naming a logo moves the station onto it, once")]
    public async Task ALinkNamingALogoMovesTheStation()
    {
        BroadcastService station = Held();

        BroadcastService? moved = await Links.LinkAsync(Network, station.ServiceId, new LogoId(5), Cancel);
        BroadcastService? again = await Links.LinkAsync(Network, station.ServiceId, new LogoId(5), Cancel);

        Assert.Same(station, moved);
        Assert.Equal(new LogoId(5), station.LogoId);
        Assert.Null(again);
    }

    [Fact(DisplayName = "BR-TD-009: a link naming no logo says the station broadcasts no picture, once")]
    public async Task ALinkNamingNoLogoSaysNoPictureIsBroadcast()
    {
        BroadcastService station = Held();

        BroadcastService? moved = await Links.LinkAsync(Network, station.ServiceId, null, Cancel);
        BroadcastService? again = await Links.LinkAsync(Network, station.ServiceId, null, Cancel);

        Assert.Same(station, moved);
        Assert.Equal(StationLogoDeclaration.NoPictureIsBroadcast, station.LogoDeclaration);
        Assert.Null(again);
    }

    private BroadcastService Held()
    {
        BroadcastService station = BroadcastService.Discover(Network, new ServiceId(101), "Fixture Service", ServiceCategory.Television, At);
        services.Services.Add(station);

        return station;
    }
}
