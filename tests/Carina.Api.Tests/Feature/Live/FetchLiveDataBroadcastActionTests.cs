using System.Net;
using System.Text.Json;

using Carina.Api.Live;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class FetchLiveDataBroadcastActionTests
{
    [Fact(DisplayName = "BR-BD-011: while communication is off nothing is relayed, and a fetch answers 403")]
    public async Task WhileCommunicationIsOffAFetchAnswers403()
    {
        await using DataBroadcastRelayFeature feature = new();

        using HttpResponseMessage fetched = await feature.Client.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2Fstartup.bml", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, fetched.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "a caller who has not signed in cannot fetch")]
    public async Task ACallerWhoHasNotSignedInCannotFetch()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage fetched = await anonymous.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=http%3A%2F%2Fbc.example.jp%2F", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, fetched.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-011: a fetched document is handed over as a module the side channel reader reads, and never stored")]
    public async Task AFetchedDocumentIsHandedOverAsAModule()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        CarouselResource document = new("http://bc.example.jp/bml/startup.bml", "text/X-arib-bml", ResourceForm.Text, System.Text.Encoding.UTF8.GetBytes("<bml/>"));
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
        Assert.Equal("<bml/>", System.Text.Encoding.UTF8.GetString(read.Body.Span));
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

    [Fact(DisplayName = "BR-BD-011: a document fetching from this installation's own host, as its public origin names it, is answered -1 and nothing is asked of the network")]
    public async Task ADocumentFetchingFromItsOwnHostIsRefused()
    {
        await using DataBroadcastRelayFeature feature = DataBroadcastRelayFeature.ThroughTheRelay("https://Carina.Example");
        feature.Allow();

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri(DataBroadcastRelayFeature.Fetch + "?uri=https%3A%2F%2FCARINA.example%3A443%2Fapi%2Fhealth", UriKind.Relative));
        (HttpStatusCode fetched, JsonElement refused) = await DataBroadcastRelayFeature.ReadAsync(response);

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
