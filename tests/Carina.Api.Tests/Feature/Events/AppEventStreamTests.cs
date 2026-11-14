using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

using Carina.Api.Events;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Infrastructure.Events;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Events;

[Trait("Category", "Feature")]
public sealed class AppEventStreamTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly Uri Events = new(AppEventStream.Path, UriKind.Relative);

    [Fact]
    public async Task TheStreamIsBehindTheSameDenialAsEveryOtherSurfaceOnceASchemeIsRegistered()
    {
        using HttpClient client = factory.WithTestScheme().CreateClient();
        using HttpResponseMessage response = await client.GetAsync(Events);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAuthenticatedListenerIsAnsweredWithAnEventStream()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using HttpResponseMessage response = await client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ASignalRaisedOnTheHubArrivesOnTheOpenStream()
    {
        using WebApplicationFactory<Program> authenticated = factory.WithTestScheme();
        AppEventHub hub = authenticated.Services.GetRequiredService<AppEventHub>();

        using HttpClient client = authenticated.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");

        using HttpResponseMessage response = await client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);

        hub.Signal(AppEventName.Tuners);

        string? name = await reader.ReadLineAsync();

        Assert.Equal("event: tuners", name);
    }

    [Fact]
    public async Task TheStreamIsAbsentFromTheDocumentThatNamesIt()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        Assert.DoesNotContain(
            AppEventStream.Path,
            document["paths"]!.AsObject().Select(path => path.Key),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AListenerCarryingNoCookieIsRefusedRatherThanHandedAStreamThatCarriesTheRefusal()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();
        probe.Client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(AppEventStream.ContentType));

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(AppEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AStreamOpenedForACallerIsMarkedSoNothingInFrontHandsItToTheNextCaller()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task AStreamIsOpenedForACallerCarryingASessionCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        await probe.SignedInAsync();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AStreamIsRefusedToACallerCarryingNoCookie()
    {
        await using AuthProbe probe = AuthProbe.OverHttp().WithAnAccount();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AStreamThatIsAlreadyOpenKeepsCarryingSignalsAfterItsSessionIsEnded()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession session = await probe.SignedInAsync();
        AppEventHub hub = probe.Wired.Services.GetRequiredService<AppEventHub>();

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);

        session.Revoke(DateTime.UtcNow);

        hub.Signal(AppEventName.Tuners);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("event: tuners", await reader.ReadLineAsync());
    }

    [Fact]
    public async Task TheNextConnectionAfterThatSessionEndedIsRefused()
    {
        await using AuthProbe probe = AuthProbe.OverHttp();
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Events,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
