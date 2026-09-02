using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LiveTicketReachTests
{
    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Uri Handshake = new("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30");

    [Theory]
    [InlineData("/api/live/ws")]
    [InlineData("/api/live/sessions")]
    [InlineData("/api/live/channels")]
    [InlineData(AppEventStream.Path)]
    public async Task ALiveTicketOpensNoSurfaceABrowserReachesWithItsCookie(string path)
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage response = await player.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ALiveTicketOfferedOnTheWireHandshakeIsRefusedBeforeItBecomesAWebSocket()
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        WebSocketClient client = probe.Wired.Server.CreateWebSocketClient();

        client.ConfigureRequest += request => request.Headers[HeaderNames.Authorization] = $"Bearer {ticket}";

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(Handshake, new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token));

        Assert.Contains("401", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALiveTicketOpensNoRecordingAndIsSpentByTheAttempt()
    {
        await using AuthProbe probe = Wiring(out Recording recording);
        string ticket = await IssuedAsync(probe);
        Uri bytes = new($"/api/videos/{recording.Id.Wire}", UriKind.Relative);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage first = await player.GetAsync(bytes);
        using HttpResponseMessage again = await player.GetAsync(bytes);

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Null(first.Headers.Location);
    }

    private static HttpClient Carrying(AuthProbe probe, string ticket)
    {
        HttpClient player = probe.Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);

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

    private static AuthProbe Wiring(out Recording recording)
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
        });
    }
}
