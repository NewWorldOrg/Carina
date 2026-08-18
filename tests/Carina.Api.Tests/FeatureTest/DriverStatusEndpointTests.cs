using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

using Carina.Api.Tests.Unit;
using Carina.Contracts;
using Carina.Domain.DriverStatus;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DriverStatusEndpointTests
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
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "tester")],
                SchemeName);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private static HttpClient ClientReading(IDriverStatusReader? reader)
    {
        WebApplicationFactory<Program> factory = new TestingWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication(SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        SchemeName,
                        _ => { });

                if (reader is not null)
                {
                    services.AddSingleton(reader);
                }
            }));

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            SchemeName,
            "anything");

        return client;
    }

    private static async Task<JsonDocument> StatusBody(HttpClient client)
        => JsonDocument.Parse(await client.GetStringAsync("/api/driver/status"));

    [Fact]
    public async Task AConnectedDriverIsSurfacedAsData()
    {
        var observation = DriverObservation.Of(
            new DriverHello(DriverProtocol.Version, "instance-a", ["recording", "live"]),
            []);
        using HttpClient client = ClientReading(new StubDriverStatusReader(observation));

        using JsonDocument body = await StatusBody(client);
        JsonElement data = body.RootElement.GetProperty("data");

        Assert.True(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal("connected", data.GetProperty("connection").GetString());
        Assert.Equal(
            DriverProtocol.Version,
            data.GetProperty("hello").GetProperty("protocolVersion").GetInt32());
        Assert.Equal(
            "instance-a",
            data.GetProperty("hello").GetProperty("instanceId").GetString());
        Assert.Equal(DriverProtocol.Version, data.GetProperty("appProtocolVersion").GetInt32());
        Assert.False(data.GetProperty("driverUpdateRequired").GetBoolean());
        Assert.Empty(data.GetProperty("missingCapabilities").EnumerateArray());
    }

    [Fact]
    public async Task ADrainingDriverIsSurfaced()
    {
        DriverObservation observation = DriverObservation.Of(
                new DriverHello(DriverProtocol.Version, "instance-a", ["recording", "live"]),
                [])
            .WhileDraining();
        using HttpClient client = ClientReading(new StubDriverStatusReader(observation));

        using JsonDocument body = await StatusBody(client);

        Assert.Equal(
            "draining",
            body.RootElement.GetProperty("data").GetProperty("connection").GetString());
    }

    [Fact]
    public async Task AMissingCapabilityIsSurfacedAsDriverUpdateRequired()
    {
        var observation = DriverObservation.Of(
            new DriverHello(DriverProtocol.Version, "instance-old", ["recording"]),
            ["live"]);
        using HttpClient client = ClientReading(new StubDriverStatusReader(observation));

        using JsonDocument body = await StatusBody(client);
        JsonElement data = body.RootElement.GetProperty("data");

        Assert.True(data.GetProperty("driverUpdateRequired").GetBoolean());
        Assert.Equal(
            ["live"],
            [.. data.GetProperty("missingCapabilities").EnumerateArray().Select(e => e.GetString()!)]);
    }

    [Fact]
    public async Task WithoutADriverTheAnswerIsStillOkAndNotConnected()
    {
        using HttpClient client = ClientReading(null);

        using HttpResponseMessage response = await client.GetAsync("/api/driver/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement data = body.RootElement.GetProperty("data");

        Assert.Equal("notConnected", data.GetProperty("connection").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("hello").ValueKind);
    }

    [Fact]
    public async Task ABrokenReaderIsAGenuine503()
    {
        using HttpClient client = ClientReading(new ThrowingDriverStatusReader());

        using HttpResponseMessage response = await client.GetAsync("/api/driver/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(
            "The driver status is unavailable.",
            body.RootElement.GetProperty("message").GetString());
    }
}
