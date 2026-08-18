using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class TunerHoldingDriverClient : IDriverClient
{
    public static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    public IReadOnlyList<TunerSnapshot> Tuners { get; set; } =
    [
        new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
        new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Idle),
    ];

    public string? Unreachable { get; set; }

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken)
        => Task.FromResult(Unreachable is { } failure
            ? DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable(failure)
            : DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(Tuners));

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(
        IReadOnlyList<TunerConfigEntry> tuners,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<DriverRestartDto>> RequestRestartAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerSnapshot>> ToggleTunerAsync(
        string deviceId,
        bool disabled,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class CatalogFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();

    public CatalogFeature()
        => Client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IDriverClient>(Driver);
            }))
            .CreateAuthenticatedClient();

    public HttpClient Client { get; }

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public TunerHoldingDriverClient Driver { get; } = new();

    public CandidateChannel Seed(int serviceId, string name, params TuningParameters[] tunings)
    {
        Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            TunerHoldingDriverClient.At));

        foreach (TuningParameters tuning in tunings)
        {
            Candidates.Candidates.Add(CandidateChannel.Discover(
                CandidateChannelId.New(),
                new NetworkId(1),
                new ServiceId(serviceId),
                tuning,
                TunerHoldingDriverClient.At));
        }

        return Candidates.Candidates[^1];
    }

    public Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)));

    public Task<(HttpStatusCode Status, JsonElement Body)> DeleteAsync(string path)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative)));

    public Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string path, object body)
        => SendAsync(new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        });

    public Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object body)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            using HttpResponseMessage response = await Client.SendAsync(request);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return (response.StatusCode, document.RootElement.Clone());
        }
    }
}
