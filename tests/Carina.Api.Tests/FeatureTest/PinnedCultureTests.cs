using System.Globalization;

using Carina.Api.Tests.Support;

namespace Carina.Api.Tests.FeatureTest;

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
