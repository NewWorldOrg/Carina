using System.Net;

using Carina.Api.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class PeerAddressFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (context, proceed) =>
            {
                context.Connection.RemoteIpAddress = address;

                await proceed();
            });

            next(app);
        };
    }
}

internal static class RequestOrigin
{
    public const string ProxyNetwork = "10.42.0.0/24";

    public const string ProxyAddress = "10.42.0.7";

    public const string PublicAddress = "203.0.113.9";

    public static WebApplicationFactory<Program> BehindTheProxy(
        this WebApplicationFactory<Program> factory)
        => factory.ArrivingFrom(ProxyAddress);

    public static WebApplicationFactory<Program> ArrivingFrom(
        this WebApplicationFactory<Program> factory,
        string address)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TrustedProxyNetworks.SettingKey, ProxyNetwork);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(
                    new PeerAddressFilter(IPAddress.Parse(address))));
        });
    }
}
