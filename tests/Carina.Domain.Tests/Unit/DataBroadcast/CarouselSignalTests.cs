using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class CarouselSignalTests
{
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
    public void ACarouselLeftOutForAReasonNotNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarouselSignal.Dropped(0x40, (CarouselDropReason)0));
    }
}
