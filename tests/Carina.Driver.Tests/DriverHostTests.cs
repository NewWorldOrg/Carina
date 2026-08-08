using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Tests;

public sealed class DriverHostTests
{
    [Fact]
    public void BuildsTheHost()
    {
        using var host = DriverHost.Create([]);

        Assert.NotNull(host.Services.GetService(typeof(IHostApplicationLifetime)));
    }
}
