using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class CarouselCatalogTests
{
    [Fact]
    public void ACatalogHoldsEachCarouselOnceAndEachCarouselEachModuleOnce()
    {
        CatalogModule module = new(0, 1, 100, false, []);

        Assert.Throws<ArgumentException>(() => new CatalogCarousel(0x40, 1, [module, module]));
        Assert.Throws<ArgumentException>(() => new CarouselCatalog(
            new ServiceId(1),
            0x40,
            false,
            [new CatalogCarousel(0x40, 1, []), new CatalogCarousel(0x40, 2, [])]));
    }
}
