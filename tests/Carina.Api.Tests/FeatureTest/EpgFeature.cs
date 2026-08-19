using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class EpgFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();
    private readonly WebApplicationFactory<Program> configured;
    private readonly WebApplicationFactory<Program> authenticated;

    public EpgFeature(
        IReadOnlyList<BroadcastStream>? streams = null,
        IDriverClient? driver = null,
        CollectionSettings? collection = null,
        TimeProvider? clock = null)
    {
        Streams = new HeldStreams(streams ?? []);
        configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStreamVisitRepository>(Visits);
                services.AddSingleton<IProgrammeRepository>(Programmes);
                services.AddSingleton<IArchivedProgrammeRepository>(Archived);
                services.AddSingleton<ICollectionEpochRepository>(Epochs);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IAtomicWrite, UnguardedWrites>();
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IBroadcastStreamDirectory>(Streams);

                if (driver is not null)
                {
                    services.AddSingleton(driver);
                }

                if (collection is not null)
                {
                    services.AddSingleton(collection);
                }

                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            }));
        authenticated = configured.WithTestScheme();
        Client = authenticated.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
    }

    public HttpClient Client { get; }

    public HeldStreamVisits Visits { get; } = new();

    public HeldProgrammes Programmes { get; } = new();

    public HeldArchive Archived { get; } = new();

    public HeldEpochs Epochs { get; } = new();

    public HeldStreams Streams { get; }

    public HeldCandidates Candidates { get; } = new();

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            body ?? new { });

        return await ReadAsync(response);
    }

    public RescanNoticeBoard Board() => authenticated.Services.GetRequiredService<RescanNoticeBoard>();

    public Task CollectionSettled() => authenticated.Services.GetRequiredService<CollectionBoost>().Settled;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(
        HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }
}
