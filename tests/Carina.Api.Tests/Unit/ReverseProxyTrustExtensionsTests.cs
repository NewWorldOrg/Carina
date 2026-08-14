using System.Net;

using Carina.Api.Authentication;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Unit;

public sealed class ReverseProxyTrustExtensionsTests
{
    [Fact]
    public void TheDeclaredProxyNetworkIsWhatTheSeamAsksAtRequestTime()
    {
        using var provider = Build("10.42.0.0/24");

        var trusted = provider.GetRequiredService<TrustedProxyNetworks>();

        Assert.True(trusted.Admits(IPAddress.Parse("10.42.0.7")));
        Assert.False(trusted.Admits(IPAddress.Parse("203.0.113.9")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("0.0.0.0/0")]
    public void StartupFailsWithTheSettingNamedWhenTheTrustBoundaryIsNotDeclarable(string setting)
    {
        using var provider = Build(setting);

        var failure = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ReverseProxyTrustOptions>>().Value);

        Assert.Contains(TrustedProxyNetworks.SettingKey, failure.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(string setting)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TrustedProxyNetworks.SettingKey] = setting,
            })
            .Build();

        return new ServiceCollection()
            .AddReverseProxyTrust(configuration)
            .BuildServiceProvider();
    }
}
