using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class AuthenticatedRequestTests
{
    private const string SchemeName = "Test";

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "tester")],
                SchemeName
            );
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                SchemeName
            );

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private static HttpClient ClientWithTestScheme()
    {
        var factory = new TestingWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services
                    .AddAuthentication(SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        SchemeName,
                        _ => { }
                    )
            )
        );

        return factory.CreateClient();
    }

    [Fact]
    public async Task AnAuthenticatedRequestReachesTheWorkedExample()
    {
        using var client = ClientWithTestScheme();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            SchemeName,
            "anything"
        );

        using var response = await client.GetAsync("/api/driver/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(
            "notConnected",
            body.RootElement.GetProperty("data").GetProperty("connection").GetString()
        );
    }

    [Fact]
    public async Task AnUnauthenticatedRequestIsStillDeniedWhenASchemeExists()
    {
        using var client = ClientWithTestScheme();

        using var response = await client.GetAsync("/api/driver/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
