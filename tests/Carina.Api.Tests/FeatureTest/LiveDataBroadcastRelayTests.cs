using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Carina.Api.Authentication;
using Carina.Api.Live;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Driver;
using Carina.Infrastructure.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed record AskedOfTheRelay(string Channel, string Uri, string? Text, string? Charset);

/// <summary>
/// The relay as the HTTP surface sees it: it keeps what it was asked and answers what it is told to.
/// </summary>
internal sealed class ScriptedRelay : IBroadcasterRelay
{
    public ConcurrentQueue<AskedOfTheRelay> Asked { get; } = new();

    public RelayedText Transmitted { get; set; } = new(RelayResult.Answered, 200, "1");

    public RelayedResource Fetched { get; set; } = RelayedResource.Failed(RelayResult.NameNotResolved);

    public Task<RelayedText> TransmitAsync(string channel, string uri, string text, string charset, CancellationToken cancellationToken)
    {
        Asked.Enqueue(new AskedOfTheRelay(channel, uri, text, charset));

        return Task.FromResult(Transmitted);
    }

    public Task<RelayedResource> FetchAsync(string channel, string uri, CancellationToken cancellationToken)
    {
        Asked.Enqueue(new AskedOfTheRelay(channel, uri, null, null));

        return Task.FromResult(Fetched);
    }
}

/// <summary>
/// The driver as the HTTP surface asks it which card answered: it counts the asking and answers what it is told to.
/// </summary>
internal sealed class ScriptedCardReader : IConditionalAccessCardReader
{
    private int asked;

    public int Asked => Volatile.Read(ref asked);

    public DriverCall<ConditionalAccessCardDto> Answer { get; set; } =
        DriverCall<ConditionalAccessCardDto>.Reached(new ConditionalAccessCardDto { CaSystemId = 5, CardId = "0A0B0C0D0E0F" });

    public Task<DriverCall<ConditionalAccessCardDto>> GetConditionalAccessCardAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref asked);

        return Task.FromResult(Answer);
    }
}

/// <summary>
/// The network as the application's own relay sees it: it keeps every name it is asked to resolve and every address
/// it is asked to connect to, and reaches nothing.
/// </summary>
internal sealed class UnreachedNetwork : IRelayNetwork
{
    public ConcurrentQueue<string> Asked { get; } = new();

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Asked.Enqueue(host);

        return Task.FromException<IReadOnlyList<IPAddress>>(new SocketException((int)SocketError.HostNotFound));
    }

    public IReadOnlyCollection<IPAddress> OwnAddresses() => [];

    public Task<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Asked.Enqueue(endpoint.ToString());

        return Task.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused));
    }
}

internal sealed class DataBroadcastRelayFeature : IAsyncDisposable
{
    public const string ReceiverId = "/api/live/32736-1024/data-broadcast/receiver-id";

    public const string Transmit = "/api/live/32736-1024/data-broadcast/transmit";

    public const string Fetch = "/api/live/32736-1024/data-broadcast/fetch";

    public static readonly DateTime Noon = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public DataBroadcastRelayFeature()
        : this(null)
    {
    }

    private DataBroadcastRelayFeature(string? publicOrigin)
    {
        Built = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(PublicOrigin.Key, publicOrigin ?? string.Empty);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IBroadcastServiceRepository>(Services);
                services.AddSingleton<ICandidateChannelRepository>(Candidates);
                services.AddSingleton<IConditionalAccessCardReader>(Cards);
                services.AddSingleton<IRelayNetwork>(Network);

                if (publicOrigin is null)
                {
                    services.AddSingleton<IBroadcasterRelay>(Relay);
                }
            });
        });

        Client = Built.CreateAuthenticatedClient();
        Seed(1024, ServiceCategory.Television);
        Seed(1025, ServiceCategory.Radio);
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public HeldServices Services { get; } = new();

    public HeldCandidates Candidates { get; } = new();

    public ScriptedRelay Relay { get; } = new();

    public UnreachedNetwork Network { get; } = new();

    public ScriptedCardReader Cards { get; } = new();

    public static DataBroadcastRelayFeature ThroughTheRelay(string publicOrigin) => new(publicOrigin);

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public void Allow(string said = DataBroadcastLineCookie.Allows)
        => Client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{DataBroadcastLineCookie.Name}={said}");

    public void AllowAndHand(string line = DataBroadcastLineCookie.Allows, string id = DataBroadcastReceiverIdCookie.Hands)
        => Client.DefaultRequestHeaders.Add(
            HeaderNames.Cookie,
            $"{DataBroadcastLineCookie.Name}={line}; {DataBroadcastReceiverIdCookie.Name}={id}");

    public async Task<(HttpStatusCode Status, JsonElement Body)> TransmitAsync(object body, string path = Transmit)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> TransmitAsync(string json)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client.PostAsync(new Uri(Transmit, UriKind.Relative), content);

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using JsonDocument document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    private void Seed(int serviceId, ServiceCategory category)
    {
        BroadcastService service = BroadcastService.Discover(new NetworkId(32736), new ServiceId(serviceId), $"Channel {serviceId}", category, Noon);
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(32736),
            new ServiceId(serviceId),
            TuningParameters.Terrestrial(27),
            Noon);

        candidate.Select(SelectionSource.Manual, null, Noon);
        Services.Services.Add(service);
        Candidates.Candidates.Add(candidate);
    }
}

public sealed class LiveDataBroadcastRelayTests
{
    [Fact(DisplayName = "BR-BD-011: while communication is off nothing is relayed, and both surfaces answer 403")]
    public async Task WhileCommunicationIsOffBothSurfacesAnswer403()
    {
        await using DataBroadcastRelayFeature feature = new();

        (HttpStatusCode transmitted, JsonElement body) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });
        using HttpResponseMessage fetched = await feature.Client.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2Fstartup.bml", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, transmitted);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, fetched.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-011: a transmission is relayed for the channel in the path and answered in the shape a document reads")]
    public async Task ATransmissionIsRelayedAndAnsweredInTheShapeADocumentReads()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Transmitted = new RelayedText(RelayResult.Answered, 404, "not here");

        (HttpStatusCode status, JsonElement body) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/entry", text = "応募", charset = "EUC-JP" });
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetProperty("result").GetInt32());
        Assert.Equal(404, data.GetProperty("httpStatus").GetInt32());
        Assert.Equal("not here", data.GetProperty("body").GetString());
        Assert.Equal(new AskedOfTheRelay("32736-1024", "http://bc.example.jp/entry", "応募", "EUC-JP"), Assert.Single(feature.Relay.Asked));
    }

    [Theory(DisplayName = "BR-BD-013: a failure of the relay is a result in a success, with no status and no body")]
    [InlineData(RelayResult.RefusedOrTooLarge, -1)]
    [InlineData(RelayResult.Disconnected, -2)]
    [InlineData(RelayResult.TimedOut, -3)]
    [InlineData(RelayResult.NameNotResolved, -400)]
    [InlineData(RelayResult.SecureConnectionFailed, -500)]
    public async Task AFailureOfTheRelayIsAResultInASuccess(RelayResult result, int numbered)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Transmitted = RelayedText.Failed(result);

        (HttpStatusCode status, JsonElement body) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(numbered, data.GetProperty("result").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("httpStatus").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("body").ValueKind);
    }

    [Fact(DisplayName = "BR-BD-011: a transmission that names no text asks with nothing to send")]
    public async Task ATransmissionThatNamesNoTextSendsNothing()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();

        (HttpStatusCode status, _) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", charset = "EUC-JP" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(string.Empty, Assert.Single(feature.Relay.Asked).Text);
    }

    [Theory(DisplayName = "a transmission that names no address or character set, or names anything else, is refused and relays nothing")]
    [InlineData("""{"text":"","charset":"EUC-JP"}""")]
    [InlineData("""{"uri":"http://bc.example.jp/","text":""}""")]
    [InlineData("""{"uri":"http://bc.example.jp/","text":"","charset":"EUC-JP","cookie":"x"}""")]
    [InlineData("{}")]
    public async Task ATransmissionThatNamesTooLittleIsRefused(string json)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();

        (HttpStatusCode status, _) = await feature.TransmitAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.Relay.Asked);
    }

    [Theory(DisplayName = "BR-BD-011: a channel that cannot be watched live here relays nothing and answers 404")]
    [InlineData("/api/live/32736-9999/data-broadcast/transmit")]
    [InlineData("/api/live/32736-1025/data-broadcast/transmit")]
    public async Task AChannelThatCannotBeWatchedLiveAnswers404(string path)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();

        (HttpStatusCode status, _) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" }, path);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "a transmission sent from another origin is refused and relays nothing")]
    public async Task ATransmissionFromAnotherOriginIsRefused()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        feature.Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        feature.Client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        (HttpStatusCode status, _) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "a caller who has not signed in can neither transmit nor fetch")]
    public async Task ACallerWhoHasNotSignedInCanNeitherTransmitNorFetch()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage transmitted = await anonymous.PostAsJsonAsync(
            new Uri(DataBroadcastRelayFeature.Transmit, UriKind.Relative),
            new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });
        using HttpResponseMessage fetched = await anonymous.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2F", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, transmitted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fetched.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-011: a fetched document is handed over as a module the side channel reader reads, and never stored")]
    public async Task AFetchedDocumentIsHandedOverAsAModule()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        CarouselResource document = new("http://bc.example.jp/bml/startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml/>"));
        feature.Relay.Fetched = new RelayedResource(RelayResult.Answered, 200, document);

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2Fbml%2Fstartup.bml", UriKind.Relative));
        byte[] payload = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(new byte[] { DataBroadcastFrames.ModuleKind, 0, 0, 0, 0 }, payload[..DataBroadcastFrames.ModuleHeaderLength]);
        CarouselResource read = Assert.Single(DataBroadcastFrames.ReadResources(payload.AsMemory(DataBroadcastFrames.ModuleHeaderLength))!);
        Assert.Equal(document.Path, read.Path);
        Assert.Equal("text/X-arib-bml", read.MediaType);
        Assert.Equal("<bml/>", Encoding.UTF8.GetString(read.Body.Span));
        Assert.Equal(new AskedOfTheRelay("32736-1024", "http://bc.example.jp/bml/startup.bml", null, null), Assert.Single(feature.Relay.Asked));
    }

    [Theory(DisplayName = "BR-BD-013: a fetch that was not handed over answers 502 with the result and the status the server answered")]
    [InlineData(RelayResult.NameNotResolved, null, -400)]
    [InlineData(RelayResult.RefusedOrTooLarge, null, -1)]
    [InlineData(RelayResult.Answered, 404, 1)]
    public async Task AFetchNotHandedOverAnswers502(RelayResult result, int? httpStatus, int numbered)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Fetched = new RelayedResource(result, httpStatus, null);

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2Fgone.bml", UriKind.Relative));
        (HttpStatusCode status, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(response);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal(numbered, data.GetProperty("result").GetInt32());
        Assert.Equal(httpStatus, data.GetProperty("httpStatus").ValueKind is JsonValueKind.Null ? null : data.GetProperty("httpStatus").GetInt32());
    }

    [Fact(DisplayName = "BR-BD-011: a document asking for this installation's own host, as its public origin names it, is answered -1 and nothing is asked of the network")]
    public async Task ADocumentAskingForItsOwnHostIsRefused()
    {
        await using DataBroadcastRelayFeature feature = DataBroadcastRelayFeature.ThroughTheRelay("https://Carina.Example");
        feature.Allow();

        (HttpStatusCode transmitted, JsonElement body) = await feature.TransmitAsync(new { uri = "http://carina.example./api/health", text = "x", charset = "EUC-JP" });
        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=https%3A%2F%2FCARINA.example%3A443%2Fapi%2Fhealth", UriKind.Relative));
        (HttpStatusCode fetched, JsonElement refused) = await DataBroadcastRelayFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, transmitted);
        Assert.Equal(-1, body.GetProperty("data").GetProperty("result").GetInt32());
        Assert.Equal(HttpStatusCode.BadGateway, fetched);
        Assert.Equal(-1, refused.GetProperty("data").GetProperty("result").GetInt32());
        Assert.Empty(feature.Network.Asked);
    }

    [Fact(DisplayName = "a fetch that names no address is refused and relays nothing")]
    public async Task AFetchThatNamesNoAddressIsRefused()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();

        using HttpResponseMessage response = await feature.Client.GetAsync(new Uri(DataBroadcastRelayFeature.Fetch, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Theory(DisplayName = "BR-BD-011: communication is allowed only by the browser's own cookie saying so")]
    [InlineData("refuses")]
    [InlineData("Allows")]
    [InlineData("")]
    public async Task CommunicationIsAllowedOnlyByTheCookieSayingSo(string said)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow(said);

        (HttpStatusCode status, _) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-011: the cookie that allows communication opens nothing to a caller who has not signed in")]
    public async Task TheCookieOpensNothingWithoutASession()
    {
        await using DataBroadcastRelayFeature feature = new();
        using HttpClient anonymous = feature.Anonymous();
        anonymous.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{DataBroadcastLineCookie.Name}={DataBroadcastLineCookie.Allows}");

        using HttpResponseMessage fetched = await anonymous.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2F", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, fetched.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }
}
