using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Reservations;
using Carina.TestSupport;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Services;

internal sealed class CatalogFeature : IAsyncDisposable
{
    public const int Terrestrial = 53;
    public const int OtherTerrestrial = 55;
    public const int ThirdTerrestrial = 57;
    public const int SatelliteSlot = 5;
    public const int SatelliteStream = 50001;

    public const string OneService = "/api/services/1-101";

    private readonly TestingWebApplicationFactory factory = new();

    public CatalogFeature()
        => Client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<IStationLogoRepository>(Logos);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IDriverClient>(Driver);
                services.AddSingleton<IRecalculationNotice>(Notices);
            }))
            .CreateAuthenticatedClient();

    public HttpClient Client { get; }

    public HeldServices Services { get; } = new();

    public HeldLogos Logos { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public TunerHoldingDriverClient Driver { get; } = new();

    public CountedNotices Notices { get; } = new();

    public CandidateChannel Seed(int serviceId, string name, params TuningParameters[] tunings)
    {
        BroadcastService service = BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(serviceId),
            name,
            ServiceCategory.Television,
            TunerHoldingDriverClient.At);

        Services.Services.Add(service);
        Logos.Services.Add(service);

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

    public void Measure(int candidate, int cnrMilliDecibels)
        => Candidates.Candidates[candidate].RecordTuningSuccess(
            SignalMeasurement.WithLock(TunerHoldingDriverClient.At, cnrMilliDecibels),
            TunerHoldingDriverClient.At);

    public void Refuse(int candidate, int times)
    {
        for (int failure = 0; failure < times; failure++)
        {
            Candidates.Candidates[candidate].RecordTuningFailure(
                RotationBackoff.Default,
                TunerHoldingDriverClient.At);
        }
    }

    public async Task SelectTheWeakerOfTwoChannelsAsync()
    {
        Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        Measure(0, 12_000);
        Measure(1, 29_000);

        await PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = Candidates.Candidates[0].Id.Value });
    }

    public Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)));

    public Task<(HttpStatusCode Status, JsonElement Body)> DeleteAsync(string path)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { }),
        });

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

    public static StationLogo Logo(int logoId)
        => StationLogo.Collect(
            new NetworkId(1),
            new LogoId(logoId),
            0x05,
            3,
            64,
            36,
            [0x89, 0x50, 0x4E, 0x47],
            new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));

    public static IReadOnlyList<string?> Receptions(JsonElement listed)
        => [.. listed.EnumerateArray().Select(service => service.GetProperty("reception").GetString())];

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
