using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class ModuleVersionTests
{
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0x100, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0x1_0000, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 0x100)]
    public void AVersionOutsideTheNumbersACarouselCarriesIsRefused(int tag, int moduleId, int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModuleVersion(tag, moduleId, version, 0, 0, [Carousels.Resource("a")]));
    }

    [Fact]
    public void AVersionIsNeverLastSeenBeforeItIsFirstSeen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModuleVersion(0x40, 0, 0, 10, 9, [Carousels.Resource("a")]));
    }

    [Fact]
    public void AVersionSeenAgainEarlierThanLastKeepsItsLastSighting()
    {
        ModuleVersion version = new(0x40, 0, 0, 10, 50, [Carousels.Resource("a")]);

        Assert.Same(version, version.SeenAt(30));
        Assert.Equal(70, version.SeenAt(70).LastSeen);
    }

    [Fact]
    public void AModuleWithNoResourcesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ModuleVersion(0x40, 0, 0, 0, 0, []));
        Assert.Throws<ArgumentException>(() => new CarouselSignal.ModuleCompleted(0x40, 0, 0, []));
    }

    [Fact]
    public void AVersionKeepsItsOwnCopyOfItsResources()
    {
        List<CarouselResource> resources = [Carousels.Resource("startup.bml")];
        ModuleVersion version = new(0x40, 0, 0, 0, 0, resources);

        resources.Clear();

        Assert.Single(version.Resources);
    }

    [Fact]
    public void TwoVersionsCarryTheSameWhenEveryResourceIsTheSame()
    {
        ModuleVersion version = new(0x40, 0, 0, 0, 0, [Carousels.Resource("a", 3)]);

        Assert.True(version.CarriesTheSameAs(new ModuleVersion(0x40, 0, 0, 50, 50, [Carousels.Resource("a", 3)])));
        Assert.False(version.CarriesTheSameAs(new ModuleVersion(0x40, 0, 0, 0, 0, [Carousels.Resource("a", 4)])));
        Assert.False(version.CarriesTheSameAs(new ModuleVersion(0x40, 0, 0, 0, 0, [Carousels.Resource("b", 3)])));
        Assert.False(version.CarriesTheSameAs(new ModuleVersion(0x40, 0, 0, 0, 0, [Carousels.Resource("a", 3), Carousels.Resource("b", 3)])));
    }
}
