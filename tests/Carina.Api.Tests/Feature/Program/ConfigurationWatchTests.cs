using Carina.Api.Tests.Support;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Program;

[Trait("Category", "Feature")]
public sealed class ConfigurationWatchTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public void NoConfigurationSourceOfAStartedApplicationWatchesItsFile()
    {
        IConfigurationRoot configuration =
            Assert.IsAssignableFrom<IConfigurationRoot>(factory.Services.GetRequiredService<IConfiguration>());
        IReadOnlyList<FileConfigurationProvider> watching =
            [.. configuration.Providers.OfType<FileConfigurationProvider>()];

        Assert.NotEmpty(watching);
        Assert.All(watching, provider => Assert.False(provider.Source.ReloadOnChange));
    }
}
