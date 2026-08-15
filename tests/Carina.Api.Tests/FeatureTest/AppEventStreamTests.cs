using System.Net;
using System.Net.Http.Headers;

using Carina.Api.Events;
using Carina.Contracts;
using Carina.Infrastructure.Events;

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
        using var client = factory.WithTestScheme().CreateClient();
        using var response = await client.GetAsync(Events);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAuthenticatedListenerIsAnsweredWithAnEventStream()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppEventStream.ContentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ASignalRaisedOnTheHubArrivesOnTheOpenStream()
    {
        using var authenticated = factory.WithTestScheme();
        var hub = authenticated.Services.GetRequiredService<AppEventHub>();

        using var client = authenticated.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");

        using var response = await client.GetAsync(Events, HttpCompletionOption.ResponseHeadersRead);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);

        hub.Signal(AppEventName.Tuners);

        var name = await reader.ReadLineAsync();

        Assert.Equal("event: tuners", name);
    }

    [Fact]
    public async Task TheStreamIsAbsentFromTheDocumentThatNamesIt()
    {
        var document = await ServedOpenApi.FetchAsync(factory);

        Assert.DoesNotContain(
            AppEventStream.Path,
            document["paths"]!.AsObject().Select(path => path.Key),
            StringComparer.Ordinal);
    }
}
