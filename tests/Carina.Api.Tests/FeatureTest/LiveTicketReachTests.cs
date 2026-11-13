using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveTicketReachTests
{
    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private readonly PipedSupply supply = new();

    [Theory]
    [InlineData("/api/live/ws")]
    [InlineData("/api/live/sessions")]
    [InlineData("/api/live/channels")]
    [InlineData("/api/live/profiles")]
    [InlineData("/api/live/departures")]
    [InlineData("/api/live/32736-1024/data-broadcast/fetch?uri=http%3A%2F%2Fbc.example.jp%2F")]
    [InlineData("/api/live/32736-1024/data-broadcast/receiver-id")]
    [InlineData("/api/live/32736-1024/data-broadcast/ait?uri=http%3A%2F%2Fbc.example.jp%2Fapp.ait")]
    [InlineData(AppEventStream.Path)]
    public async Task ALiveTicketOpensNoOtherSurfaceABrowserReachesWithItsCookie(string path)
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage response = await player.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    private static HttpClient Carrying(AuthProbe probe, string? ticket)
    {
        HttpClient player = probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        if (ticket is not null)
        {
            player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
        }

        return player;
    }

    private static async Task<string> IssuedAsync(AuthProbe probe)
    {
        await probe.SignedInAsync();

        using HttpResponseMessage answer = await probe.Client.PostAsJsonAsync(
            new Uri("/api/live/ticket", UriKind.Relative),
            new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;
    }

    private AuthProbe Wiring(out Recording recording)
    {
        HeldServices services = new();
        HeldCandidates candidates = new();
        HeldRecordings recordings = new();
        NetworkId network = new(32736);
        ServiceId service = new(1024);
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            network,
            service,
            TuningParameters.Terrestrial(27),
            At);

        candidate.Select(SelectionSource.Manual, null, At);
        services.Services.Add(BroadcastService.Discover(network, service, "Watched", ServiceCategory.Television, At));
        candidates.Candidates.Add(candidate);

        recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        recordings.Recordings.Add(recording);

        return AuthProbe.OverHttp(wired =>
        {
            wired.AddSingleton<IBroadcastServiceRepository>(services);
            wired.AddSingleton<ICandidateChannelRepository>(candidates);
            wired.AddSingleton<IRecordingDirectory>(recordings);
            wired.AddSingleton<ILiveSupply>(supply);
        });
    }
}
