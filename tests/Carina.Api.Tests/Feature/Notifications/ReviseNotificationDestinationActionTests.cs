using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class ReviseNotificationDestinationActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("PATCH", NotificationGuard.OneDestination)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("PATCH", NotificationGuard.OneDestination)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("PATCH", NotificationGuard.OneDestination)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("PATCH", NotificationGuard.OneDestination)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NT-003: A change that names only the name keeps the address, and an empty address keeps the held one")]
    public async Task AChangeOfTheNameKeepsTheAddress()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        byte[] before = feature.HeldDestinations.Destinations[0].SealedUrl;
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = "書斎", url = "" });

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement changed = body.GetProperty("data");
        Assert.Equal("書斎", changed.GetProperty("name").GetString());
        Assert.Equal("slack", changed.GetProperty("kind").GetString());
        Assert.Equal("wxyz", changed.GetProperty("tail").GetString());
        Assert.Equal(NotificationFeature.Noon.AddMinutes(5), changed.GetProperty("changedAt").GetDateTime());
        Assert.Same(before, feature.HeldDestinations.Destinations[0].SealedUrl);
    }

    [Fact(DisplayName = "BR-NT-002: A change of the address changes the kind with it")]
    public async Task AChangeOfTheAddressChangesTheKind()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { url = NotificationFeature.DiscordUrl });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("discord", body.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal("abcd", body.GetProperty("data").GetProperty("tail").GetString());
        Assert.Equal("居間", body.GetProperty("data").GetProperty("name").GetString());
        Assert.DoesNotContain(NotificationFeature.Secret, body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NS-002: A change of the address clears the last result and leaves a destination that was off still off, and a change of the name alone clears nothing")]
    public async Task AChangeOfTheAddressClearsTheLastResult()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        feature.HeldDestinations.Destinations[0].NoteFailed(NoticeFailure.Rejected, NotificationFeature.Noon);

        (_, JsonElement renamed) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = "書斎" });
        (HttpStatusCode status, JsonElement pointed) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { url = NotificationFeature.DiscordUrl });

        Assert.Equal("rejected", renamed.GetProperty("data").GetProperty("lastFailure").GetString());
        Assert.Equal(NotificationFeature.Noon, renamed.GetProperty("data").GetProperty("lastResultAt").GetDateTime());
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, pointed.GetProperty("data").GetProperty("lastFailure").ValueKind);
        Assert.Equal(JsonValueKind.Null, pointed.GetProperty("data").GetProperty("lastResultAt").ValueKind);
        Assert.False(pointed.GetProperty("data").GetProperty("enabled").GetBoolean());
    }

    [Fact(DisplayName = "BR-NT-001: A change to a bad address or a bad name is refused and nothing is changed")]
    public async Task ABadChangeIsRefused()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

        (HttpStatusCode badUrl, JsonElement body) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { name = "書斎", url = "https://hooks.slack.com.evil.example/services/made-up-token-wxyz" });
        (HttpStatusCode badName, _) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = new string('a', 65) });
        (HttpStatusCode unknown, _) = await feature.PatchAsync(NotificationFeature.OneOf(Guid.NewGuid()), new { name = "書斎" });

        Assert.Equal(HttpStatusCode.BadRequest, badUrl);
        Assert.DoesNotContain("evil.example", body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, badName);
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal("居間", feature.HeldDestinations.Destinations[0].Name.Value);
    }

    [Fact(DisplayName = "BR-NS-002: Turning a destination off gives up what was waiting for it, and turning it on again sends none of that")]
    public async Task TurningOffGivesUpWhatWasWaiting()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        Guid other = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        NoticeOnItsWay waiting = feature.Waiting(id);
        NoticeOnItsWay elsewhere = feature.Waiting(other);

        (HttpStatusCode off, JsonElement offBody) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = false });
        (HttpStatusCode on, JsonElement onBody) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, off);
        Assert.False(offBody.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, on);
        Assert.True(onBody.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(NotificationDeliveryState.GivenUp, waiting.Delivery.State);
        Assert.Equal(NoticeFailure.DestinationDisabled, waiting.Delivery.LastFailure);
        Assert.Equal(NotificationDeliveryState.Queued, elsewhere.Delivery.State);
        Assert.Empty(feature.End.Heard);
    }

    [Theory(DisplayName = "BR-NC-002: A destination given a new address starts with room, whether the old address had asked for a wait or had been sent thirty notices in the minute")]
    [InlineData(1, 429)]
    [InlineData(30, 204)]
    public async Task ANewAddressStartsWithRoom(int sent, int answered)
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        feature.End.Status = answered;
        feature.End.AnswerHeaders = answered == 429 ? "Retry-After: 600\r\n" : string.Empty;

        for (int index = 0; index < sent; index++)
        {
            feature.Waiting(id);
        }

        await feature.PassAsync();
        feature.End.Status = 204;
        feature.End.AnswerHeaders = string.Empty;

        (HttpStatusCode held, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = "書斎" });
        (HttpStatusCode renamed, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        int heardBefore = feature.End.Heard.Count;
        await feature.PatchAsync(NotificationFeature.OneOf(id), new { url = NotificationFeature.DiscordUrl });
        (HttpStatusCode pointed, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.TooManyRequests, held);
        Assert.Equal(HttpStatusCode.TooManyRequests, renamed);
        Assert.Equal(sent, heardBefore);
        Assert.Equal(HttpStatusCode.OK, pointed);
        Assert.True(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal(sent + 1, feature.End.Heard.Count);
    }

    [Fact(DisplayName = "BR-NT-008: A change of a browser's row takes its name and whether it is on, and one that names an address is refused and changes nothing")]
    public async Task AChangeOfABrowsersRowTakesNoAddress()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        byte[] before = held.SealedUrl;

        (HttpStatusCode named, JsonElement renamed) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = "寝室のブラウザ" });
        (HttpStatusCode turned, JsonElement off) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = false });
        (HttpStatusCode addressed, JsonElement refusal) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { name = "玄関のブラウザ", enabled = true, url = NotificationFeature.SlackUrl });
        (HttpStatusCode emptied, _) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { url = string.Empty });

        Assert.Equal(HttpStatusCode.OK, named);
        Assert.Equal("寝室のブラウザ", renamed.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("push", renamed.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.OK, turned);
        Assert.False(off.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, addressed);
        Assert.DoesNotContain(NotificationFeature.Secret, refusal.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, emptied);
        Assert.Equal("寝室のブラウザ", held.Name.Value);
        Assert.False(held.Enabled);
        Assert.Equal(WebhookTarget.Push, held.Target);
        Assert.Same(before, held.SealedUrl);
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), held.EndpointHash);
    }

    private static string HashOf(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(endpoint)));
}
