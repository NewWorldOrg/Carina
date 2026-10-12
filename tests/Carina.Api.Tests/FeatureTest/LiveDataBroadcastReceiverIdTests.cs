using System.Net;
using System.Text.Json;

using Carina.Api.Live;
using Carina.Contracts;
using Carina.Domain.Driver;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveDataBroadcastReceiverIdTests
{
    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> AskAsync(
        HttpClient client,
        string path = DataBroadcastRelayFeature.ReceiverId)
    {
        HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        (HttpStatusCode status, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(response);

        return (status, body, response);
    }

    [Fact(DisplayName = "BR-BD-014: with communication allowed and the identifier handed over, the card's CA system and ID are named and never stored")]
    public async Task TheCardIsNamedWhereTheBrowserHandsItOver()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.AllowAndHand();

        (HttpStatusCode status, JsonElement body, HttpResponseMessage response) = await AskAsync(feature.Client);
        using HttpResponseMessage answered = response;
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(5, data.GetProperty("caSystemId").GetInt32());
        Assert.Equal("0A0B0C0D0E0F", data.GetProperty("cardId").GetString());
        Assert.Equal(["caSystemId", "cardId"], data.EnumerateObject().Select(property => property.Name));
        Assert.Contains("no-store", answered.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1, feature.Cards.Asked);
    }

    [Theory(DisplayName = "BR-BD-014: the identifier is kept back, and the driver not asked, unless both of the browser's cookies say so")]
    [InlineData(DataBroadcastLineCookie.Allows, "keeps")]
    [InlineData(DataBroadcastLineCookie.Allows, "Hands")]
    [InlineData(DataBroadcastLineCookie.Allows, "")]
    [InlineData("refuses", DataBroadcastReceiverIdCookie.Hands)]
    public async Task TheIdentifierIsKeptBackUnlessBothCookiesSaySo(string line, string id)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.AllowAndHand(line, id);

        (HttpStatusCode status, JsonElement body, HttpResponseMessage response) = await AskAsync(feature.Client);
        using HttpResponseMessage answered = response;

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(0, feature.Cards.Asked);
    }

    [Fact(DisplayName = "BR-BD-014: the cookie that hands the identifier over does nothing while communication is off")]
    public async Task HandingTheIdentifierOverAloneIsNotEnough()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.Client.DefaultRequestHeaders.Add(
            HeaderNames.Cookie,
            $"{DataBroadcastReceiverIdCookie.Name}={DataBroadcastReceiverIdCookie.Hands}");

        (HttpStatusCode status, _, HttpResponseMessage response) = await AskAsync(feature.Client);
        using HttpResponseMessage answered = response;

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, feature.Cards.Asked);
    }

    [Theory(DisplayName = "BR-BD-014: a channel that cannot be watched live here names no card and answers 404")]
    [InlineData("/api/live/32736-9999/data-broadcast/receiver-id")]
    [InlineData("/api/live/32736-1025/data-broadcast/receiver-id")]
    public async Task AChannelThatCannotBeWatchedLiveNamesNoCard(string path)
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.AllowAndHand();

        (HttpStatusCode status, _, HttpResponseMessage response) = await AskAsync(feature.Client, path);
        using HttpResponseMessage answered = response;

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, feature.Cards.Asked);
    }

    [Fact(DisplayName = "BR-BD-014: where no card has answered the driver, there is no identifier and the answer is 404")]
    public async Task WhereNoCardHasAnsweredThereIsNoIdentifier()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.AllowAndHand();
        feature.Cards.Answer = DriverCall<ConditionalAccessCardDto>.Refused(
            new DriverProblem("No card has answered", ["No card has answered this driver's reader since it started."]));

        (HttpStatusCode status, JsonElement body, HttpResponseMessage response) = await AskAsync(feature.Client);
        using HttpResponseMessage answered = response;

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    [Fact(DisplayName = "BR-BD-014: where the driver could not be asked, the answer is 503")]
    public async Task WhereTheDriverCouldNotBeAskedTheAnswerIs503()
    {
        await using DataBroadcastRelayFeature feature = new();
        feature.AllowAndHand();
        feature.Cards.Answer = DriverCall<ConditionalAccessCardDto>.Unreachable("The driver's socket could not be reached.");

        (HttpStatusCode status, _, HttpResponseMessage response) = await AskAsync(feature.Client);
        using HttpResponseMessage answered = response;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    [Fact(DisplayName = "BR-BD-014: the cookies open nothing to a caller who has not signed in")]
    public async Task TheCookiesOpenNothingWithoutASession()
    {
        await using DataBroadcastRelayFeature feature = new();
        using HttpClient anonymous = feature.Anonymous();
        anonymous.DefaultRequestHeaders.Add(
            HeaderNames.Cookie,
            $"{DataBroadcastLineCookie.Name}={DataBroadcastLineCookie.Allows}; {DataBroadcastReceiverIdCookie.Name}={DataBroadcastReceiverIdCookie.Hands}");

        (HttpStatusCode status, _, HttpResponseMessage response) = await AskAsync(anonymous);
        using HttpResponseMessage answered = response;

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(0, feature.Cards.Asked);
    }
}
