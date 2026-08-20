using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class AnonymousReachTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Screen = "text/html";

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
}
