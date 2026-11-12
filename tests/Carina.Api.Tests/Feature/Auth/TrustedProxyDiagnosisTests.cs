using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class TrustedProxyDiagnosisTests
{
    [Fact]
    public async Task AnAppTrustingNoProxySaysSoAtStartupRatherThanAtTheFirstFailedSignIn()
    {
        var log = new RecordedStartup();

        await using var factory = new TestingWebApplicationFactory();
        WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TrustedProxies.ProxiesKey, string.Empty);
            builder.UseSetting(TrustedProxies.NetworksKey, string.Empty);
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(log));
        });

        using HttpClient client = wired.CreateClient();

        await Eventually.Happens(
            () => log.By<TrustedProxyDiagnosis>(LogLevel.Warning).Count > 0,
            "the app says it trusts nothing to label a forwarded request");

        SaidLine warned = Assert.Single(log.By<TrustedProxyDiagnosis>(LogLevel.Warning));

        Assert.Contains(TrustedProxies.ProxiesKey, warned.Text, StringComparison.Ordinal);
        Assert.Contains(TrustedProxies.NetworksKey, warned.Text, StringComparison.Ordinal);
    }
}
