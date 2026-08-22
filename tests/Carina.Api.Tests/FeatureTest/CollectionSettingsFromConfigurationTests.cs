using Carina.Domain.Programmes;
using Carina.Infrastructure.Configuration;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

public sealed class CollectionSettingsFromConfigurationTests
{
    [Fact]
    public void RidingAlongIsTurnedOffByTheSettingThatSaysSo()
        => Assert.False(Served(("RidesAlong", "false")).RidesAlong);

    [Fact]
    public void RidingAlongStaysOnWhenNobodySaysOtherwise()
        => Assert.True(Served().RidesAlong);

    [Fact]
    public void HowLongASweepWaitsIsTakenFromTheSettings()
        => Assert.Equal(TimeSpan.FromMinutes(5), Served(("BetweenSweeps", "00:05:00")).BetweenSweeps);

    [Fact]
    public void HowFarAheadIsWantedIsTakenFromTheSettings()
        => Assert.Equal(TimeSpan.FromDays(6), Served(("WantedCoverage", "6.00:00:00")).WantedCoverage);

    [Fact]
    public void TheBackOffForAFullTunerIsTakenFromTheSettings()
        => Assert.Equal(
            TimeSpan.FromSeconds(45),
            Served(("WhenTunersAreFull:FirstDelay", "00:00:45")).WhenTunersAreFull.FirstDelay);

    private static CollectionSettings Served(params (string Name, string Value)[] settings)
    {
        using TestingWebApplicationFactory factory = new();
        using WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
        {
            foreach ((string name, string value) in settings)
            {
                builder.UseSetting($"{CollectionOptions.Section}:{name}", value);
            }
        });

        return wired.Services.GetRequiredService<CollectionSettings>();
    }
}
