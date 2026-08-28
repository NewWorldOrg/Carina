using System.Globalization;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class PinnedCultureTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public void TheApplicationDecidesItsOwnCultureRatherThanTakingOneFromTheEnvironment()
    {
        using HttpClient client = factory.CreateClient();

        Assert.Same(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
        Assert.Same(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
    }
}
