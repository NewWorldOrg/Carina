using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class CatalogModuleTests
{
    [Fact]
    public void ACatalogModuleCarriesResourcesOnceItHasArrivedAndNoneBefore()
    {
        Assert.Throws<ArgumentException>(() => new CatalogModule(0, 1, 100, false, [new CatalogResource("startup.bml", "text/X-arib-bml")]));
        Assert.Throws<ArgumentException>(() => new CatalogModule(0, 1, 100, true, []));
    }

    [Fact]
    public void ASizeBelowNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ListedModule(0, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogModule(0, 1, -1, false, []));
    }
}
