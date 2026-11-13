using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.DataBroadcast;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class TransmitLiveDataBroadcastActionTests
{
    [Fact(DisplayName = "BR-BD-011: while communication is off nothing is relayed, and a transmission answers 403")]
    public async Task WhileCommunicationIsOffATransmissionAnswers403()
    {
        await using DataBroadcastRelayFeature feature = new();

        (HttpStatusCode transmitted, JsonElement body) = await feature.TransmitAsync(new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });

        Assert.Equal(HttpStatusCode.Forbidden, transmitted);
        Assert.False(body.GetProperty("status").GetBoolean());
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

    [Fact(DisplayName = "a caller who has not signed in cannot transmit")]
    public async Task ACallerWhoHasNotSignedInCannotTransmit()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Allow();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage transmitted = await anonymous.PostAsJsonAsync(
            new Uri(DataBroadcastRelayFeature.Transmit, UriKind.Relative),
            new { uri = "http://bc.example.jp/", text = "", charset = "EUC-JP" });

        Assert.Equal(HttpStatusCode.Unauthorized, transmitted.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "BR-BD-011: a document transmitting to this installation's own host, as its public origin names it, is answered -1 and nothing is asked of the network")]
    public async Task ADocumentTransmittingToItsOwnHostIsRefused()
    {
        await using DataBroadcastRelayFeature feature = DataBroadcastRelayFeature.ThroughTheRelay("https://Carina.Example");
        feature.Allow();

        (HttpStatusCode transmitted, JsonElement body) = await feature.TransmitAsync(new { uri = "http://carina.example./api/health", text = "x", charset = "EUC-JP" });

        Assert.Equal(HttpStatusCode.OK, transmitted);
        Assert.Equal(-1, body.GetProperty("data").GetProperty("result").GetInt32());
        Assert.Empty(feature.Network.Asked);
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
}
