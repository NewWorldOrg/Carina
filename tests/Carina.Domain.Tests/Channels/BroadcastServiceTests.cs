using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class BroadcastServiceTests
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AServiceIsIdentifiedByItsNetworkAndServiceIdAlone()
    {
        var service = BroadcastService.Discover(
            new NetworkId(4), new ServiceId(101), "Fixture Service", ServiceCategory.Television, At);

        Assert.Equal(new NetworkId(4), service.NetworkId);
        Assert.Equal(new ServiceId(101), service.ServiceId);
        Assert.Equal(At, service.DiscoveredAt);
        Assert.Equal(At, service.LastSeenAt);
    }

    [Fact]
    public void SeeingAServiceAgainUpdatesItsNameWithoutTouchingWhenItWasFound()
    {
        var service = BroadcastService.Discover(
            new NetworkId(4), new ServiceId(101), "Fixture Service", ServiceCategory.Television, At);

        service.Describe("Fixture Service Renamed", ServiceCategory.Television, At.AddDays(1));

        Assert.Equal("Fixture Service Renamed", service.Name);
        Assert.Equal(At, service.DiscoveredAt);
        Assert.Equal(At.AddDays(1), service.LastSeenAt);
    }

    [Theory]
    [InlineData(ServiceCategory.Television, true)]
    [InlineData(ServiceCategory.Radio, true)]
    [InlineData(ServiceCategory.OneSeg, false)]
    [InlineData(ServiceCategory.Data, false)]
    [InlineData(ServiceCategory.Temporary, false)]
    public void OnlySoundAndPictureServicesAreOfferedForBookingByDefault(
        ServiceCategory category,
        bool reservable)
    {
        var service = BroadcastService.Discover(
            new NetworkId(4), new ServiceId(101), "Fixture Service", category, At);

        Assert.Equal(reservable, service.ReservableByDefault);
    }

    [Fact]
    public void ANameLongerThanTheColumnIsRefusedBeforeItReachesTheDatabase()
    {
        Assert.Throws<ArgumentException>(() => BroadcastService.Discover(
            new NetworkId(4),
            new ServiceId(101),
            new string('x', BroadcastService.NameMaxLength + 1),
            ServiceCategory.Television,
            At));
    }

    [Fact]
    public void TimesArriveInUtcOrNotAtAll()
    {
        Assert.Throws<ArgumentException>(() => BroadcastService.Discover(
            new NetworkId(4),
            new ServiceId(101),
            "Fixture Service",
            ServiceCategory.Television,
            new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Unspecified)));
    }
}
