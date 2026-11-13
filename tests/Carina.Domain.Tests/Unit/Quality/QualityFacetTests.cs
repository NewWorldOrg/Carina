using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityFacetTests
{
    [Fact]
    public void AFacetNamesTheChannelTheTunerTheHourAndTheKind()
    {
        QualityFacet facet = QualityFactory.Facet(network: 32_736, service: 1_024, tuner: "adapter0", hourOfDay: 21);

        Assert.Equal(32_736, facet.Network.Value);
        Assert.Equal(1_024, facet.Service.Value);
        Assert.Equal("adapter0", facet.Tuner!.Value);
        Assert.Equal(21, facet.HourOfDay);
        Assert.Equal(TuneSystem.IsdbT, facet.Kind);
    }

    [Fact]
    public void AnHourOfTheDayIsOneOfTheTwentyFourThereAre()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityFactory.Facet(hourOfDay: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityFactory.Facet(hourOfDay: 24));
        Assert.Equal(0, QualityFactory.Facet(hourOfDay: 0).HourOfDay);
        Assert.Equal(23, QualityFactory.Facet(hourOfDay: 23).HourOfDay);
    }

    [Fact]
    public void AFacetComesFromABroadcastOfAKindTheDriverNamed()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityFactory.Facet(kind: TuneSystem.Unspecified));

    [Fact]
    public void AFacetNamesAChannelATunerAndAKindRatherThanNothing()
    {
        Assert.Throws<ArgumentNullException>(
            () => QualityFacet.Of(TuneSystem.IsdbT, null!, new ServiceId(1_024), new TunerDeviceId("adapter0"), 21));
        Assert.Throws<ArgumentNullException>(
            () => QualityFacet.Of(TuneSystem.IsdbT, new NetworkId(32_736), null!, new TunerDeviceId("adapter0"), 21));
        Assert.Throws<ArgumentNullException>(
            () => QualityFacet.Of(TuneSystem.IsdbT, new NetworkId(32_736), new ServiceId(1_024), null!, 21));
    }

    [Fact]
    public void TwoFacetsNamingTheSameThingAreTheSameFacet()
        => Assert.Equal(QualityFactory.Facet(), QualityFactory.Facet());
}
