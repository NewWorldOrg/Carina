using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class SessionCookiePolicyTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task ACookieLeavesWithSameSiteLaxAndOutOfReachOfScripts()
    {
        WebApplicationFactory<Program> probed = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services
                    .AddAuthentication(CookieAppendingHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CookieAppendingHandler>(
                        CookieAppendingHandler.SchemeName,
                        _ => { })));
        using HttpClient client = probed.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        string cookie = Assert.Single(response.Headers.GetValues(HeaderNames.SetCookie));

        Assert.StartsWith(CookieAppendingHandler.CookieName, cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
