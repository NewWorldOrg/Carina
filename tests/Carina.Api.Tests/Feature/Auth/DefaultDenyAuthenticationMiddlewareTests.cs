using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;
using Carina.Api.Events;
using Carina.Api.Tests.FeatureTest;
using Carina.Api.Tests.Unit;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Scans;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Auth;

[Trait("Category", "Feature")]
public sealed class DefaultDenyAuthenticationMiddlewareTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const int TheSurfacesThisRepositoryHadWhenTheSweepWasWritten = 50;

    private const string Caller = "203.0.113.77";

    private const string Screen = "text/html";

    private const string Spoofed = "someone-else@elsewhere.example";

    private static readonly Uri Sessions = new("/api/auth/sessions", UriKind.Relative);

    private static readonly Uri Logout = new("/api/auth/logout", UriKind.Relative);

    private static readonly Uri Password = new("/api/auth/password", UriKind.Relative);

    private static readonly Uri Me = new("/api/auth/me", UriKind.Relative);

    private static readonly Uri Status = new("/api/driver/status", UriKind.Relative);

    private static readonly string[] EdgeIdentityHeaders =
    [
        "X-Forwarded-User",
        "X-Forwarded-Email",
        "X-Forwarded-Preferred-Username",
        "X-Forwarded-Groups",
        "X-Auth-Request-User",
        "X-Auth-Request-Email",
    ];

    private readonly RecordedStartup log = new();

    public static TheoryData<string> EveryBusinessSurface =>
    [
        "/api/driver/status",
        "/api/tuners",
        "/api/tuners/detected",
        "/api/tuners/health",
        "/api/tuners/scan-runs",
        "/api/services",
        "/api/recordings",
        "/api/encoding/jobs/durations",
        "/api/encoding/profiles/defaults",
        "/api/encoding/settings",
        "/api/quality/candidate-scores",
        "/api/quality/incidents",
        "/api/quality/supply-health",
        "/api/quality/trends",
        "/api/segments/settings",
        "/api/data-broadcast/settings",
        "/api/segments/status",
        AppEventStream.Path,
    ];

    [Fact]
    public async Task NoneOfTheSessionSurfacesAnswerACallerWithoutACookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage listed = await probe.Client.GetAsync(Sessions);
        using HttpResponseMessage signedOut = await probe.Client.PostAsJsonAsync(Logout, new { });
        using HttpResponseMessage changed = await probe.Client.PostAsJsonAsync(
            Password,
            new { currentPassword = AuthProbe.Password, newPassword = "a replacement password" });

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
    }

    [Fact]
    public async Task NoSurfaceAnswersACallerCarryingAGoodSessionWithTheRefusalThatMeansThereIsNone()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        var refused = new List<string>();
        int asked = 0;

        foreach (RoutedSurface surface in RouteInventory.Of(probe.Wired))
        {
            string path = RouteInventory.SamplePath(surface.Pattern);

            if (AnonymousSurfaces.WhileDeveloping.Admit(surface.Method, path))
            {
                continue;
            }

            await probe.SignedInAsync();

            using var asking = new HttpRequestMessage(
                new HttpMethod(surface.Method),
                new Uri(path, UriKind.Relative));

            if (!HttpMethods.IsGet(surface.Method) && !HttpMethods.IsHead(surface.Method))
            {
                asking.Content = AuthProbe.Json();
            }

            using HttpResponseMessage response = await probe.Client.SendAsync(
                asking,
                HttpCompletionOption.ResponseHeadersRead);

            asked++;

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                refused.Add($"{surface.Method} {path} answered 401 to a caller carrying a session");
            }
        }

        Assert.Empty(refused);
        Assert.True(
            asked >= TheSurfacesThisRepositoryHadWhenTheSweepWasWritten,
            $"the sweep asked {asked} surfaces, which is fewer than it was written against");
    }

    [Fact(DisplayName = "BR-AU-020: an API request with no session cookie is written down as refused for carrying none")]
    public async Task BrAu020AnApiRequestWithNoSessionCookieIsWrittenDownAsRefusedForCarryingNone()
    {
        await using AuthProbe probe = Probe().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        SaidLine said = Assert.Single(Written());

        Assert.Equal(nameof(SignInMoment.RefusedWithoutASessionCookie), said.Only("Moment"));
        Assert.Equal("/api/auth/me", said.Only("Route"));
        Assert.Equal(SignInRecord.Absent, said.Only("SessionCookie"));
    }

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceRefusesAClientCarryingNoCredentials(string path)
    {
        await using var probe = SeamProbe.CarryingNoCredentials();

        using HttpResponseMessage response = await probe.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ThrowingAFindingAwayRefusesAClientCarryingNoCredentialsBeforeAnythingIsRead()
    {
        await using var probe = SeamProbe.CarryingNoCredentials();

        using HttpResponseMessage response = await probe.PostAsync(
            $"/api/recordings/integrity/findings/{Guid.NewGuid()}/delete");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [MemberData(nameof(EveryBusinessSurface))]
    public async Task ASurfaceAnswersAClientCarryingCredentials(string path)
    {
        await using var probe = SeamProbe.CarryingCredentials();

        using HttpResponseMessage response = await probe.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheCatalogArrivesForACallerThatHasSignedIn()
    {
        await using var probe = SeamProbe.CarryingCredentials();
        probe.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Reachable",
            ServiceCategory.Television,
            TunerHoldingDriverClient.At));

        using HttpResponseMessage response = await probe.GetAsync("/api/services");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Reachable",
            body.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnUnknownPathReachesRoutingOnceTheCallerHasSignedIn()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AWrongMethodOnAKnownPathReachesRoutingOnceTheCallerHasSignedIn()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/driver/status", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownPathIsRefusedBeforeRoutingHasAnEndpointToAnswer404About()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AWrongMethodOnAKnownPathIsRefusedBeforeRoutingHasAnEndpointToAnswer405About()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/driver/status", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsHandedOutWithoutCredentialsInDevelopmentBecauseTheClientIsGeneratedFromIt()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheDocumentIsNotServedAtAllOutsideDevelopmentRatherThanLeftToASeamThatMayAdmit()
    {
        using var deployed = new TestingWebApplicationFactory();
        using HttpClient client = deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheDocumentIsRefusedOutsideDevelopmentToACallerCarryingNoCredentials()
    {
        using var deployed = new TestingWebApplicationFactory();
        using HttpClient client = deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/tuners")]
    [InlineData("PUT", "/api/tuners")]
    [InlineData("GET", "/api/tuners/detected")]
    [InlineData("PATCH", "/api/tuners/adapter0")]
    [InlineData("GET", "/api/tuners/scan-runs")]
    [InlineData("POST", "/api/tuners/scan")]
    [InlineData("GET", "/api/services")]
    [InlineData("GET", "/api/programs")]
    [InlineData("GET", "/api/programs/search")]
    [InlineData("GET", "/api/programs/1")]
    [InlineData("GET", "/api/epg/collection-status")]
    [InlineData("POST", "/api/epg/collect-now")]
    [InlineData("POST", "/api/epg/rebuild")]
    [InlineData("POST", "/api/epg/archive/forget-service")]
    [InlineData("GET", "/api/driver/status")]
    [InlineData("POST", "/api/driver/restart")]
    public async Task ASurfaceThatPredatesTheDenialRefusesACallerCarryingNoCredentials(
        string method,
        string path)
    {
        using HttpResponseMessage response = await AskAsync(method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(AppEventStream.Path)]
    [InlineData(ProgrammeFeedStream.Path)]
    public async Task AConnectionMeantToStayOpenIsJudgedBeforeItIsOpened(string path)
    {
        using HttpResponseMessage response = await AskAsync("GET", path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/api/programs/1/thumbnail")]
    [InlineData("/api/recordings/1/thumbnail.jpg")]
    [InlineData("/api/recordings/1/stream.ts")]
    [InlineData("/api/services/1-101/logo")]
    [InlineData("/_next/image")]
    [InlineData("/_next/data/build/programs.json")]
    public async Task ContentIsRefusedRatherThanTreatedAsSomethingTheBuildProduced(string path)
    {
        using HttpResponseMessage response = await AskAsync("GET", path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheOneSurfaceHandingBackDataWithoutCredentialsSaysOnlyWhetherItIsAlive()
    {
        using HttpResponseMessage response = await AskAsync("GET", "/api/health");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["degraded", "status"],
            body.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Theory]
    [InlineData("/_next/static/chunks/main-0f3a.js")]
    [InlineData("/favicon.ico")]
    [InlineData("/manifest.webmanifest")]
    public async Task WhatTheBuildProducedIsCarriedPastTheDenialToTheRoleThatServesIt(string path)
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/thumbnails/1.jpg")]
    [InlineData("/logos/1.png")]
    [InlineData("/recordings/1.ts")]
    public async Task WhatWasRecordedIsRefusedBecauseItIsContentRatherThanSomethingTheBuildProduced(string path)
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task TheScreenSayingTheSessionIsOverIsReachedWithoutTheCredentialsTheCallerNoLongerHas()
    {
        using HttpClient browser = Browsing(factory.WithTestScheme());

        using HttpResponseMessage response = await browser.GetAsync(
            new Uri(LoginRedirect.LoggedOut, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task AScreenAskedForTooEarlyIsReachedOnceTheLocalAccountHasOpenedASession()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        using HttpClient browser = Browsing(probe.Wired);

        using HttpResponseMessage sentAway = await browser.GetAsync(new Uri("/guide", UriKind.Relative));
        string next = ReturnTargetOf(sentAway.Headers.Location!.OriginalString);

        using HttpResponseMessage signedIn = await browser.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username = FirstCredentials.Username, password = AuthProbe.Password });
        using HttpResponseMessage arrived = await browser.GetAsync(new Uri(next, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, sentAway.StatusCode);
        Assert.Equal("/guide", next);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, arrived.StatusCode);
        Assert.Null(arrived.Headers.Location);
    }

    [Fact]
    public async Task AnAuthenticatedRequestReachesTheWorkedExample()
    {
        await using TestingWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

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
        await using TestingWebApplicationFactory factory = new();
        using HttpClient client = factory.WithTestScheme().CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/driver/status", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AHeaderNamingAUserIsNotACredential()
    {
        using HttpClient client = Spoofing(factory.WithTestScheme().CreateClient());

        using HttpResponseMessage response = await client.GetAsync(Status);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AHeaderNamingAUserChangesNothingAboutAnAnsweredRequest()
    {
        using HttpClient plain = factory.CreateAuthenticatedClient();
        using HttpClient spoofing = Spoofing(factory.CreateAuthenticatedClient());

        using HttpResponseMessage answered = await plain.GetAsync(Status);
        using HttpResponseMessage spoofed = await spoofing.GetAsync(Status);
        string body = await spoofed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, spoofed.StatusCode);
        Assert.Equal(await ConnectionAsync(answered), ConnectionOf(body));
        Assert.DoesNotContain(Spoofed, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AHeaderNamingAUserDoesNotTakeOverASessionThatAlreadyNamesSomebodyElse()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        Spoofing(probe.Client);

        using HttpResponseMessage response = await probe.Client.GetAsync(
            new Uri("/api/auth/me", UriKind.Relative));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            FirstCredentials.Username,
            body.RootElement.GetProperty("data").GetProperty("subject").GetString());
    }

    [Fact]
    public async Task AHeaderNamingAUserDoesNotOpenTheDocumentOutsideDevelopment()
    {
        using var deployed = new TestingWebApplicationFactory();
        using HttpClient client = Spoofing(deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .WithTestScheme()
            .CreateClient());

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/programs", "/login?next=%2Fprograms")]
    [InlineData("/programs?type=terrestrial", "/login?next=%2Fprograms%3Ftype%3Dterrestrial")]
    [InlineData("/settings/tuners", "/login?next=%2Fsettings%2Ftuners")]
    public async Task AScreenAskedForWithoutCredentialsIsSentToTheLoginScreen(string path, string sentTo)
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(sentTo, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TheLoginScreenIsNotSentToItself()
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/login", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACallForDataIsRefusedRatherThanSentToAScreenEvenWhenItSaysItTakesHtml()
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/programs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/api/videos/1")]
    [InlineData("/api/videos/1/play")]
    [InlineData("/api/videos/1/thumbnail")]
    [InlineData("/api/videos/1/scrub")]
    [InlineData("/api/live/ws")]
    [InlineData("/api/live/channels")]
    [InlineData("/api/events")]
    public async Task WhatIsWatchedIsRefusedRatherThanSentToTheLoginScreenEvenWhenAskedForAsAPage(string path)
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APlayerIsRefusedRatherThanHandedALoginScreenItCannotRead()
    {
        using HttpClient client = Browser();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/videos/1", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private AuthProbe Probe() => AuthProbe.OverHttp(Heard);

    private void Heard(IServiceCollection services)
    {
        services.AddSingleton<ILoggerProvider>(log);
        services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(Caller)));
    }

    private IReadOnlyList<SaidLine> Written() => log.By<SignInRecord>();

    private async Task<HttpResponseMessage> AskAsync(string method, string path)
    {
        WebApplicationFactory<Program> guarded = factory.WithTestScheme();
        using HttpClient client = guarded.CreateClient();
        using var asking = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        return await client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
    }

    private static string ReturnTargetOf(string sentTo)
        => Uri.UnescapeDataString(sentTo[(sentTo.IndexOf('=', StringComparison.Ordinal) + 1)..]);

    private static HttpClient Browsing(WebApplicationFactory<Program> wired)
    {
        HttpClient client = wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Screen));

        return client;
    }

    private static HttpClient Spoofing(HttpClient client)
    {
        foreach (string header in EdgeIdentityHeaders)
        {
            client.DefaultRequestHeaders.Add(header, Spoofed);
        }

        return client;
    }

    private static async Task<string?> ConnectionAsync(HttpResponseMessage response)
        => ConnectionOf(await response.Content.ReadAsStringAsync());

    private static string? ConnectionOf(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("data").GetProperty("connection").GetString();
    }

    private HttpClient Browser()
    {
        HttpClient client = factory
            .WithTestScheme()
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        return client;
    }
}
