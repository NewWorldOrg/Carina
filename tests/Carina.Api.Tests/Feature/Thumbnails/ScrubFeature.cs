using System.Net.Http.Headers;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.Thumbnails;

internal sealed class ScrubFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();

    public ScrubFeature(ScrubFrame answer)
    {
        Frames = new HeldFrames(answer);

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IScrubFrames>();
                services.AddSingleton<IScrubFrames>(Frames);
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Stranger = configured.WithTestScheme().CreateClient();
    }

    public HeldFrames Frames { get; }

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public static Uri For(RecordingId id, string? at = null)
        => new(
            at is null ? $"/api/videos/{id.Wire}/scrub" : $"/api/videos/{id.Wire}/scrub?at={at}",
            UriKind.Relative);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
    }
}
