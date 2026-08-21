using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

public sealed class FeatureTestHostTests
{
    [Fact]
    public void TheHostNamesEverySettingItselfRatherThanLeavingOneToTheMachine()
    {
        string[] left = [];

        using TestingWebApplicationFactory factory = new();
        using WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
            left = [.. TestingWebApplicationFactory.SettingsNamedHere
                .Where(setting => builder.GetSetting(setting) is null)]);

        wired.CreateClient().Dispose();

        Assert.Empty(left);
    }
}
