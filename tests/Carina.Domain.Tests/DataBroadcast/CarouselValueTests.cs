using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class CarouselValueTests
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModuleVersion(tag, moduleId, version, 0, 0, []));
    }

    [Fact]
    public void AVersionIsNeverLastSeenBeforeItIsFirstSeen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModuleVersion(0x40, 0, 0, 10, 9, []));
    }

    [Fact]
    public void AVersionSeenAgainEarlierThanLastKeepsItsLastSighting()
    {
        ModuleVersion version = new(0x40, 0, 0, 10, 50, []);

        Assert.Same(version, version.SeenAt(30));
        Assert.Equal(70, version.SeenAt(70).LastSeen);
    }

    [Theory]
    [InlineData(0x1000, 0, 0)]
    [InlineData(0, 0x1_0000, 0)]
    [InlineData(0, 0, 0x100)]
    public void AnEventMessageOutsideItsFieldsIsRefused(int group, int id, int messageType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventMessage(group, id, messageType, EventTiming.Immediate, 0, ReadOnlyMemory<byte>.Empty));
    }

    [Fact(DisplayName = "BR-BD-003: an event message is either immediate or timed by the programme's clock")]
    public void AnEventMessageIsImmediateOrTimedByTheProgramme()
    {
        Assert.True(new EventMessage(1, 2, 3, EventTiming.Immediate, 0, ReadOnlyMemory<byte>.Empty).IsImmediate);
        Assert.False(new EventMessage(1, 2, 3, EventTiming.Npt, 0, ReadOnlyMemory<byte>.Empty).IsImmediate);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventMessage(1, 2, 3, (EventTiming)0, 0, ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ACatalogModuleThatHasNotArrivedCarriesNoResources()
    {
        Assert.Throws<ArgumentException>(() => new CatalogModule(0, 1, 100, false, [new CatalogResource("startup.bml", "text/X-arib-bml")]));
    }

    [Fact]
    public void AListingThatNamesAModuleTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new CarouselSignal.CatalogUpdated(
            0x40,
            1,
            [new ListedModule(0, 1, 10), new ListedModule(0, 2, 10)],
            []));
    }

    [Fact]
    public void AResourceInAFormNotNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarouselResource("a", "image/png", (ResourceForm)9, ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void ACarouselLeftOutForAReasonNotNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarouselSignal.Dropped(0x40, (CarouselDropReason)0));
    }

    [Fact]
    public void AVersionKeepsItsOwnCopyOfItsResources()
    {
        List<CarouselResource> resources = [Carousels.Resource("startup.bml")];
        ModuleVersion version = new(0x40, 0, 0, 0, 0, resources);

        resources.Clear();

        Assert.Single(version.Resources);
    }
}
