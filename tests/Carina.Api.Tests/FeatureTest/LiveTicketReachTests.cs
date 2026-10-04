using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Events;
using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveTicketReachTests
{
    private static readonly DateTime At = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Uri Handshake = new("ws://localhost" + LiveWire.Path + "?network=32736&service=1024&profile=720p30");

    private static readonly Uri Exit = new("/api/live/32736-1024/stream", UriKind.Relative);

    private readonly PipedSupply supply = new();

    [Theory]
    [InlineData("/api/live/ws")]
    [InlineData("/api/live/sessions")]
    [InlineData("/api/live/channels")]
    [InlineData("/api/live/profiles")]
    [InlineData("/api/live/departures")]
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

    [Fact]
    public async Task TheOneSurfaceALiveTicketOpensIsTheChannelHandedOverAsItIs()
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage opened = await OpenedAsync(player);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, opened.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ATicketForOneChannelOpensTheChannelHandedOverOnNoOther()
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri("/api/live/32736-1025/stream", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Theory(DisplayName = "a player handed the live ticket in the path is handed the channel, whatever name ends the path")]
    [InlineData("a-channel%20a-programme.ts")]
    [InlineData("")]
    [InlineData("a-name.mp4")]
    public async Task APlayerHandedTheLiveTicketInThePathIsHandedTheChannel(string name)
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, null);
        using HttpResponseMessage opened = await OpenedAsync(
            player,
            new Uri($"/api/live/32736-1024/with-ticket/{ticket}/{name}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(LiveStreamDelivery.MediaType, opened.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "a live ticket in the path opens the channel it was issued for and no other")]
    public async Task ALiveTicketInThePathOpensTheChannelItWasIssuedForAndNoOther()
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, null);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri($"/api/live/32736-1025/with-ticket/{ticket}/a-channel.ts", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact(DisplayName = "something that is not the shape of a ticket in the live path is refused without being sent to a sign-in screen")]
    public async Task SomethingThatIsNotATicketInTheLivePathIsRefused()
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, ticket);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri("/api/live/32736-1024/with-ticket/short/a-channel.ts", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Null(refused.Headers.Location);
    }

    [Fact(DisplayName = "a live ticket in the query is refused, because it shows as the title in a player")]
    public async Task ALiveTicketInTheQueryIsRefused()
    {
        await using AuthProbe probe = Wiring(out _);
        string ticket = await IssuedAsync(probe);

        using HttpClient player = Carrying(probe, null);
        using HttpResponseMessage refused = await player.GetAsync(
            new Uri($"{Exit}?ticket={ticket}", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
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

    private static byte[] Mouthful() => [.. Enumerable.Range(0, 4_000).Select(at => (byte)(at % 251))];

    private Task<HttpResponseMessage> OpenedAsync(HttpClient player) => OpenedAsync(player, Exit);

    private async Task<HttpResponseMessage> OpenedAsync(HttpClient player, Uri at)
    {
        Task<HttpResponseMessage> opening = player.GetAsync(at, HttpCompletionOption.ResponseHeadersRead);

        while (!opening.IsCompleted)
        {
            if (supply.Opened.Count > 0)
            {
                await supply.Opened[^1].WriteAsync(Mouthful());
            }

            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromMilliseconds(20)));
        }

        return await opening;
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
