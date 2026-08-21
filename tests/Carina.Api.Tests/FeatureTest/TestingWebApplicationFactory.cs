using Carina.Api.Authentication;
using Carina.Infrastructure.Configuration;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ConnectionStringKey = "ConnectionStrings:Carina";

    public static IReadOnlyList<string> SettingsNamedHere { get; } =
    [
        ConnectionStringKey,
        DriverOptions.SocketPathKey,
        PublicOrigin.Key,
        TrustedProxies.ProxiesKey,
        TrustedProxies.NetworksKey,
        AnonymousNetworks.Key,
    ];

    public string DriverSocketPath { get; init; } =
        Path.Combine(Path.GetTempPath(), "carina-feature-tests", "no-driver.sock");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(
            ConnectionStringKey,
            "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");
        builder.UseSetting(DriverOptions.SocketPathKey, DriverSocketPath);
        builder.UseSetting(PublicOrigin.Key, string.Empty);
        builder.UseSetting(TrustedProxies.ProxiesKey, string.Empty);
        builder.UseSetting(TrustedProxies.NetworksKey, string.Empty);
        builder.UseSetting(AnonymousNetworks.Key, string.Empty);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        base.ConfigureClient(client);

        client.DefaultRequestHeaders.Add(
            HeaderNames.Origin,
            client.BaseAddress!.GetLeftPart(UriPartial.Authority));
    }
}
