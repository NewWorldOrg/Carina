using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

using Carina.Api.Events;
using Carina.Contracts;
using Carina.Infrastructure.Events;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class AppEventStreamTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly Uri Events = new(AppEventStream.Path, UriKind.Relative);

    [Fact]
    public void ASignalFrameCarriesABareDataLineSoAnEventSourceDispatchesItWithoutAPayload()
    {
        Assert.Equal("event: tuners\ndata\n\n", AppEventStream.Frame(AppEvents.Tuners));
    }

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
}
