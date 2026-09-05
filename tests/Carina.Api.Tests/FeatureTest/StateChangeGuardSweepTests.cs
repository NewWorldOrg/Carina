using System.Net;
using System.Text;

using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class StateChangeGuardSweepTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const int TheStateChangingSurfacesThisRepositoryHadWhenTheSweepWasWritten = 36;

    private const string Elsewhere = "https://elsewhere.example";

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
        foreach (RoutedSurface surface in Inventory().Where(ChangesState))
        {
            Assert.Contains(StateChangeGuard.Origin, EndpointRules.GuardsRequiredBy(surface.Method, carriesABody: false));
        }
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
        => EndpointRules.GuardsRequiredBy(surface.Method, carriesABody: false).Count > 0;

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
}
