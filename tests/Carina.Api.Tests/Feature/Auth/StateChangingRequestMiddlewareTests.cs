using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Api.Tests.Unit;

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
public sealed class StateChangingRequestMiddlewareTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const int TheStateChangingSurfacesThisRepositoryHadWhenTheSweepWasWritten = 36;

    private const string Elsewhere = "https://elsewhere.example";

    private static readonly Uri Restart = new("/api/driver/restart", UriKind.Relative);

    private static readonly Uri Status = new("/api/driver/status", UriKind.Relative);

    [Fact]
    public async Task BrLa002EveryStateChangingSurfaceRefusesARequestNamingNoOrigin()
    {
        await SweepAsync(
            (surface, client) => SendingAsync(client, surface, origin: null, BodyOrdinarilySentTo(surface)),
            (surface, status) => status == HttpStatusCode.Forbidden
                ? null
                : $"{surface} answered {(int)status} to a request naming no origin");
    }

    [Fact]
    public async Task BrLa002EveryStateChangingSurfaceRefusesARequestNamingAnotherOrigin()
    {
        await SweepAsync(
            (surface, client) => SendingAsync(client, surface, Elsewhere, BodyOrdinarilySentTo(surface)),
            (surface, status) => status == HttpStatusCode.Forbidden
                ? null
                : $"{surface} answered {(int)status} to a request naming another origin");
    }

    [Fact]
    public async Task BrLa002EveryStateChangingSurfaceRefusesABodyAFormCouldHavePosted()
    {
        await SweepAsync(
            (surface, client) => SendingAsync(client, surface, Here(client), Form()),
            (surface, status) => status == HttpStatusCode.UnsupportedMediaType
                ? null
                : $"{surface} answered {(int)status} to a form body");
    }

    [Fact]
    public async Task BrLa002EverySurfaceAskedForAJsonBodyRefusesARequestCarryingNone()
    {
        await SweepAsync(
            (surface, client) => SendingAsync(client, surface, Here(client), content: null),
            (surface, status) =>
            {
                bool asksForJson = EndpointRules.GuardsRequiredBy(surface.Method, carriesABody: false)
                    .Contains(StateChangeGuard.JsonBody);

                if (asksForJson && status != HttpStatusCode.UnsupportedMediaType)
                {
                    return $"{surface} answered {(int)status} to a request carrying no content type";
                }

                if (!asksForJson && status is HttpStatusCode.UnsupportedMediaType or HttpStatusCode.Forbidden)
                {
                    return $"{surface} answered {(int)status} to a bodiless request that named this origin";
                }

                return null;
            });
    }

    [Fact]
    public void TheSweepReadsTheSameGuardTableForEveryMethodItSends()
    {
        IReadOnlyList<RoutedSurface> changing = [.. Inventory().Where(ChangesState)];

        Assert.True(
            changing.Count >= TheStateChangingSurfacesThisRepositoryHadWhenTheSweepWasWritten,
            $"the sweep read {changing.Count} surfaces, which is fewer than it was written against");
        Assert.All(
            changing,
            surface => Assert.Contains(
                StateChangeGuard.Origin,
                EndpointRules.GuardsRequiredBy(surface.Method, carriesABody: false)));
    }

    [Fact]
    public async Task ARequestFromThisOriginCarryingJsonReachesTheEndpoint()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(Restart, Json());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ARequestNamingNoOriginIsRefused()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);

        using HttpResponseMessage response = await client.PostAsync(Restart, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Theory]
    [InlineData("https://elsewhere.example")]
    [InlineData("http://localhost.elsewhere.example")]
    [InlineData("null")]
    public async Task ARequestNamingAnotherOriginIsRefused(string origin)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, origin);

        using HttpResponseMessage response = await client.PostAsync(Restart, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AFormPostIsRefusedEvenWhereTheEndpointReadsNoBody()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using var form = new StringContent("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await client.PostAsync(Restart, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Fact]
    public async Task ARequestCarryingNoContentTypeAtAllIsRefused()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(Restart, content: null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data")]
    public async Task ABodyAnotherSiteCanPostWithoutBeingLetThroughFirstIsRefused(string type)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using var content = new StringContent("anything=1", Encoding.UTF8, type);

        using HttpResponseMessage response = await client.PostAsync(Restart, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Theory]
    [InlineData("DELETE", "/api/auth/sessions/anything")]
    [InlineData("PUT", "/api/auth/oidc-config")]
    public async Task ADestructiveRequestNamingAnotherOriginIsRefusedWhateverItsMethod(string method, string path)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("DELETE", "/api/auth/sessions/anything")]
    [InlineData("PUT", "/api/auth/oidc-config")]
    public async Task ADestructiveRequestCarryingAFormIsRefusedWhateverItsMethod(string method, string path)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using var form = new StringContent("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await SendingAsync(client, method, path, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task AReadFromAnotherOriginIsLeftAlone()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        using HttpResponseMessage response = await client.GetAsync(Status);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task AMethodTheEndpointDoesNotAnswerIsStillToldSo()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(Status, content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private async Task SweepAsync(
        Func<RoutedSurface, HttpClient, Task<HttpResponseMessage>> asking,
        Func<RoutedSurface, HttpStatusCode, string?> judging)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        var wrong = new List<string>();
        int asked = 0;

        foreach (RoutedSurface surface in Inventory().Where(ChangesState))
        {
            using HttpResponseMessage response = await asking(surface, client);

            asked++;

            if (judging(surface, response.StatusCode) is { } complaint)
            {
                wrong.Add(complaint);
            }
        }

        Assert.Empty(wrong);
        Assert.True(
            asked >= TheStateChangingSurfacesThisRepositoryHadWhenTheSweepWasWritten,
            $"the sweep asked {asked} surfaces, which is fewer than it was written against");
    }

    private static bool ChangesState(RoutedSurface surface)
        => !surface.OpenToTheFrame && EndpointRules.GuardsRequiredBy(surface.Method, carriesABody: false).Count > 0;

    private static async Task<HttpResponseMessage> SendingAsync(
        HttpClient client,
        RoutedSurface surface,
        string? origin,
        HttpContent? content)
    {
        using var asking = new HttpRequestMessage(
            new HttpMethod(surface.Method),
            new Uri(RouteInventory.SamplePath(surface.Pattern), UriKind.Relative))
        {
            Content = content,
        };

        if (origin is not null)
        {
            asking.Headers.TryAddWithoutValidation(HeaderNames.Origin, origin);
        }

        return await client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
    }

    private static string Here(HttpClient client) => client.BaseAddress!.GetLeftPart(UriPartial.Authority);

    private static HttpContent? BodyOrdinarilySentTo(RoutedSurface surface)
        => HttpMethods.IsDelete(surface.Method) ? null : Json();

    private static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    private static StringContent Form() => new("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded");

    private IReadOnlyList<RoutedSurface> Inventory() => RouteInventory.Of(factory);

    private static async Task<HttpResponseMessage> SendingAsync(
        HttpClient client,
        string method,
        string path,
        HttpContent content)
    {
        using var asking = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        return await client.SendAsync(asking);
    }

    private static async Task<bool> SucceededAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("status").GetBoolean();
    }
}
