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

    [Theory]
    [InlineData(ServiceCategory.Television, true)]
    [InlineData(ServiceCategory.Radio, true)]
    [InlineData(ServiceCategory.Temporary, true)]
    [InlineData(ServiceCategory.Other, true)]
    [InlineData(ServiceCategory.OneSeg, false)]
    [InlineData(ServiceCategory.Data, false)]
    public void NeitherAOneSegSimulcastNorACarouselEarnsAColumnInTheGuide(
        ServiceCategory category,
        bool listed)
    {
        var service = BroadcastService.Discover(
            new NetworkId(4), new ServiceId(101), "Fixture Service", category, At);

        Assert.Equal(listed, service.ListedInTheGuide);
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

    [Fact]
    public void AServiceNobodyHasReadTheDescriptionOfHasNeitherALogoNorTheClaimThatItHasNone()
    {
        BroadcastService service = Discovered();

        Assert.Null(service.LogoId);
        Assert.Equal(StationLogoDeclaration.NotYetRead, service.LogoDeclaration);
    }

    [Fact]
    public void AServiceThatNamesALogoSaysWhichOneAndWhereItComesFrom()
    {
        BroadcastService service = Discovered();

        Assert.True(service.NamesTheLogo(new LogoId(261)));
        Assert.Equal(new LogoId(261), service.LogoId);
        Assert.Equal(StationLogoDeclaration.InTheCommonDataTable, service.LogoDeclaration);
    }

    [Fact]
    public void AServiceNamingTheLogoItAlreadyNamesIsNoChangeAtAll()
    {
        BroadcastService service = Discovered();
        service.NamesTheLogo(new LogoId(261));

        Assert.False(service.NamesTheLogo(new LogoId(261)));
    }

    [Fact]
    public void AStationThatBroadcastsNoPictureIsToldApartFromOneNobodyHasAskedYet()
    {
        BroadcastService service = Discovered();

        Assert.True(service.BroadcastsNoLogo());
        Assert.Null(service.LogoId);
        Assert.Equal(StationLogoDeclaration.NoPictureIsBroadcast, service.LogoDeclaration);
        Assert.False(service.BroadcastsNoLogo());
    }

    [Fact]
    public void AStationThatStopsBroadcastingAPictureLetsGoOfTheLogoItUsedToName()
    {
        BroadcastService service = Discovered();
        service.NamesTheLogo(new LogoId(261));

        service.BroadcastsNoLogo();

        Assert.Null(service.LogoId);
    }

    [Fact]
    public void AServiceCannotBeRehydratedNamingALogoItAlsoSaysItDoesNotHave()
    {
        Assert.Throws<ArgumentException>(() => BroadcastService.Rehydrate(
            new NetworkId(4),
            new ServiceId(101),
            "Fixture Service",
            ServiceCategory.Television,
            At,
            At,
            logoId: new LogoId(261),
            logoDeclaration: StationLogoDeclaration.NoPictureIsBroadcast));
    }

    private static BroadcastService Discovered()
        => BroadcastService.Discover(
            new NetworkId(4), new ServiceId(101), "Fixture Service", ServiceCategory.Television, At);
}
