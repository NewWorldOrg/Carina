using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Tests.FeatureTest;
using Carina.Api.Tests.Unit;
using Carina.Domain.DriverStatus;
using Carina.TestSupport;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.DriverStatus;

[SupportedOSPlatform("linux")]
[Trait("Category", "Feature")]
public sealed class GetDriverStatusActionTests : IDisposable
{
    private const string SchemeName = "Test";

    private const string HelloFromABuildWithoutDraining =
        """{"protocolVersion":0,"instanceId":"instance-old","capabilities":["recording"]}""";

    private const string SessionsFromANewerBuild =
        """
        [{"sessionId":"rec-9","purpose":"epgNow","deviceId":"a0","state":"draining","startedAt":"2026-08-08T21:04:00+09:00","priority":8}]
        """;

    private const string SessionsThatAreNotJson = "{ this is not a driver answer";

    private static readonly TimeSpan LongEnoughToAskAgain = TimeSpan.FromMilliseconds(500);

    private readonly TestingWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

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

    [Fact]
    public async Task AnAuthenticatedRequestReachesTheDriverAcrossARealSocket()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        JsonElement data = await feature.UntilConnectionIs("connected");
        JsonElement hello = data.GetProperty("hello");

        Assert.Equal("instance-a", hello.GetProperty("instanceId").GetString());
        Assert.Equal(DriverProtocol.Version, hello.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(["recording", "live"], StringsOf(hello.GetProperty("capabilities")));
        Assert.False(hello.GetProperty("draining").GetBoolean());
        Assert.Equal(DriverProtocol.Version, data.GetProperty("appProtocolVersion").GetInt32());
        Assert.False(data.GetProperty("driverUpdateRequired").GetBoolean());
        Assert.Empty(data.GetProperty("missingCapabilities").EnumerateArray());

        await feature.UntilReadoptions(1);

        Assert.Equal("rec-1", Assert.Single(feature.Hook.LastSessions!).SessionId.Value);
    }

    [Fact]
    public async Task ADriverThatGoesAwayIsReportedAsNotConnectedAndNeverAsAFault()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(FakeDriver.HelloFor("instance-a"));

        await feature.UntilConnectionIs("connected");
        await feature.StopDriverAsync();

        JsonElement data = await feature.UntilConnectionIs("notConnected");

        Assert.Equal(JsonValueKind.Null, data.GetProperty("hello").ValueKind);
        Assert.False(data.GetProperty("driverUpdateRequired").GetBoolean());
    }

    [Fact]
    public async Task ADriverThatComesBackAsANewInstanceFiresTheResyncHook()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        await feature.UntilReadoptions(1);
        await feature.StopDriverAsync();
        await feature.UntilConnectionIs("notConnected");

        await feature.StartDriverAsync(
            FakeDriver.HelloFor("instance-b"),
            driver => driver.Sessions = [Recording("rec-2", "fake-satellite")]);

        await feature.UntilReadoptions(2);

        JsonElement data = await feature.UntilConnectionIs("connected");

        Assert.Equal(
            "instance-b",
            data.GetProperty("hello").GetProperty("instanceId").GetString());
        Assert.Equal("rec-2", Assert.Single(feature.Hook.LastSessions!).SessionId.Value);
    }

    [Fact]
    public async Task ADriverThatComesBackAsTheSameInstanceIsNotReadoptedAgain()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        await feature.UntilReadoptions(1);
        await feature.StopDriverAsync();
        await feature.UntilConnectionIs("notConnected");

        await feature.StartDriverAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        await feature.UntilConnectionIs("connected");
        await Eventually.Happens(
            () => feature.Driver.ListenerCount > 0,
            "the event feed is subscribed, which the supervisor only reaches past the readoption");

        Assert.Equal(1, feature.Hook.CallCount);
    }

    [Fact]
    public async Task TheAppStartsWithoutADriverAndAdoptsOneWhenItAppears()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync();

        JsonElement missing = await feature.UntilConnectionIs("notConnected");

        Assert.Equal(JsonValueKind.Null, missing.GetProperty("hello").ValueKind);

        await feature.StartDriverAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.Sessions = [Recording("rec-1", "fake-terrestrial")]);

        JsonElement data = await feature.UntilConnectionIs("connected");

        Assert.Equal(
            "instance-a",
            data.GetProperty("hello").GetProperty("instanceId").GetString());

        await feature.UntilReadoptions(1);

        Assert.Equal("rec-1", Assert.Single(feature.Hook.LastSessions!).SessionId.Value);
    }

    [Fact]
    public async Task AnOlderDriverKeepsWorkingAndTellsTheOperatorToUpdateIt()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-old", capabilities: ["recording"], protocolVersion: 0),
            driver => driver.RawBodyByPath[DriverEndpoints.Health] = HelloFromABuildWithoutDraining);

        JsonElement data = await feature.UntilConnectionIs("connected");
        JsonElement hello = data.GetProperty("hello");

        Assert.Equal(0, hello.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("instance-old", hello.GetProperty("instanceId").GetString());
        Assert.Equal(["recording"], StringsOf(hello.GetProperty("capabilities")));
        Assert.False(hello.GetProperty("draining").GetBoolean());
        Assert.Equal(DriverProtocol.Version, data.GetProperty("appProtocolVersion").GetInt32());
        Assert.True(data.GetProperty("driverUpdateRequired").GetBoolean());
        Assert.Equal(["live"], StringsOf(data.GetProperty("missingCapabilities")));
        Assert.NotEqual(default, data.GetProperty("observedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task AnOlderProtocolOnItsOwnAlreadyAsksForAnUpdate()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-old", protocolVersion: 0));

        JsonElement data = await feature.UntilConnectionIs("connected");

        Assert.Equal(0, data.GetProperty("hello").GetProperty("protocolVersion").GetInt32());
        Assert.Empty(data.GetProperty("missingCapabilities").EnumerateArray());
        Assert.True(data.GetProperty("driverUpdateRequired").GetBoolean());
    }

    [Fact]
    public async Task AValueThisAppDoesNotKnowArrivesAsUnspecifiedRatherThanBreakingTheCall()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.RawBodyByPath[DriverEndpoints.Sessions] = SessionsFromANewerBuild);

        await feature.UntilConnectionIs("connected");
        await feature.UntilReadoptions(1);

        SessionSnapshot session = Assert.Single(feature.Hook.LastSessions!);

        Assert.Equal("rec-9", session.SessionId.Value);
        Assert.Equal("a0", session.DeviceId);
        Assert.Equal(SessionPurpose.Unspecified, session.Purpose);
        Assert.Equal(SessionState.Unspecified, session.State);

        JsonElement data = await feature.StatusAsync();

        Assert.Equal("connected", DriverFeature.ConnectionOf(data));
    }

    [Fact]
    public async Task AnAnswerThatIsNotAContractAtAllIsNotReadoptedAsIfItWere()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(
            FakeDriver.HelloFor("instance-a"),
            driver => driver.RawBodyByPath[DriverEndpoints.Sessions] = SessionsThatAreNotJson);

        await Eventually.Happens(
            () => feature.Driver.RequestsFor(DriverEndpoints.Sessions) >= 2,
            "the supervisor asks a second time, so the first round is past its readoption");

        Assert.Equal(0, feature.Hook.CallCount);

        await feature.StatusAsync();
    }

    [Fact]
    public async Task AnEventNameThisAppDoesNotKnowDoesNotCostItTheFeed()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(FakeDriver.HelloFor("instance-a"));

        await feature.UntilConnectionIs("connected");
        await Eventually.Happens(
            () => feature.Driver.ListenerCount > 0,
            "the event feed is subscribed");

        feature.Driver.Signal("somethingFromTheFuture");
        feature.Driver.Signal(DriverEvents.Draining);

        JsonElement data = await feature.UntilConnectionIs("draining");

        Assert.Equal("instance-a", data.GetProperty("hello").GetProperty("instanceId").GetString());
    }

    [Fact(DisplayName = "a driver the app asked while it was on its way down, and heard was draining, is reported gone soon after it is gone")]
    public async Task ADriverHeardDrainingOnItsWayDownIsReportedGoneSoonAfterItIsGone()
    {
        var slow = new SlowToStop();

        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(slow)));

        Task stopping = feature.Driver.BeginStop();

        try
        {
            await feature.App.UntilConnectionIs("draining");
            await Task.Delay(LongEnoughToAskAgain);
            await feature.App.UntilConnectionIs("draining");
        }
        finally
        {
            slow.Release();
        }

        await stopping;
        await feature.Driver.PutDownAsync();

        Stopwatch waited = Stopwatch.StartNew();

        await feature.App.UntilConnectionIs("notConnected");

        Assert.True(
            waited.Elapsed < Eventually.Patience / 3,
            $"The app took {waited.Elapsed} to notice the driver was gone, which leaves a test that waits "
            + $"{Eventually.Patience} for it no room on a busy machine.");
    }

    [Fact]
    public async Task TheDriverStatusCarriesTheDriversVersionStartAndName()
    {
        DriverHello hello = HardwareStock.Capable() with { Version = "9.8.7", StartedAt = HardwareStock.Moment, Name = "living-room" };
        await using DriverFeature feature = await DriverFeature.StartAsync(hello, HardwareStock.Stocked);

        JsonElement data = await feature.UntilConnectionIs("connected");
        JsonElement greeting = data.GetProperty("hello");

        Assert.Equal("9.8.7", greeting.GetProperty("version").GetString());
        Assert.Equal(HardwareStock.Moment, greeting.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal("living-room", greeting.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnOldDriverLeavesTheVersionStartAndNameEmpty()
    {
        await using DriverFeature feature = await DriverFeature.StartAsync(HardwareStock.Capable(), HardwareStock.Stocked);

        JsonElement greeting = (await feature.UntilConnectionIs("connected")).GetProperty("hello");

        Assert.Equal(JsonValueKind.Null, greeting.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, greeting.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, greeting.GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task TheRefusalCarriesNoBodyBecauseTheMiddlewareSendsNone()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        using HttpClient client = factory.WithTestScheme().CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(
            document["paths"]!["/api/driver/status"]!["get"]!["responses"]!["401"]!["content"]
        );
    }

    [Fact]
    public async Task TheConnectionEnumIsSpelledTheWayTheEndpointSpellsIt()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? connection = body.RootElement.GetProperty("data").GetProperty("connection").GetString();

        JsonNode document = await ServedOpenApi.FetchAsync(factory);
        string[] spellings = document["components"]!["schemas"]!["DriverConnection"]!["enum"]!
            .AsArray()
            .Select(value => value!.GetValue<string>())
            .ToArray();

        Assert.Contains(connection, spellings);
    }

    private HttpClient ClientReading(IDriverStatusReader? reader)
    {
        WebApplicationFactory<Program> reading = factory.WithWebHostBuilder(builder =>
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

        HttpClient client = reading.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            SchemeName,
            "anything");

        return client;
    }

    private static async Task<JsonDocument> StatusBody(HttpClient client)
        => JsonDocument.Parse(await client.GetStringAsync("/api/driver/status"));

    private static SessionSnapshot Recording(string id, string deviceId)
        => new(
            SessionId.Parse(id),
            SessionPurpose.Recording,
            deviceId,
            SessionState.Active,
            DateTimeOffset.UtcNow);

    private static string[] StringsOf(JsonElement array)
        => [.. array.EnumerateArray().Select(element => element.GetString()!)];

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

    /// <summary>
    /// Holds the driver between closing its event feeds and closing its socket, which is where a busy
    /// machine leaves it for a moment on every stop.
    /// </summary>
    private sealed class SlowToStop : IHostedLifecycleService
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => released.TrySetResult();

        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => released.Task;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
