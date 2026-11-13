using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class ReplacePushSubscriptionActionTests
{
    private const string Fingerprint = "fingerprint";

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("PUT", $"{NotificationFeature.PushSubscriptions}/00000000-0000-0000-0000-000000000001")]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("PUT", $"{NotificationFeature.PushSubscriptions}/00000000-0000-0000-0000-000000000001")]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("PUT", $"{NotificationFeature.PushSubscriptions}/00000000-0000-0000-0000-000000000001")]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("PUT", $"{NotificationFeature.PushSubscriptions}/00000000-0000-0000-0000-000000000001")]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-ND-015: A row is given a new subscription by its id, which keeps its name and whether it is on and clears its last result")]
    public async Task ARowIsGivenANewSubscriptionByItsId()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver before = new();
        using PushReceiver after = new();
        Guid id = await feature.SubscribedAsync(before);
        feature.End.Status = 410;
        feature.Waiting(id);
        NotificationDeliveries gone = await feature.PassAsync();
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        bool wasOn = held.Enabled;
        NoticeFailure? failedWith = held.LastFailure;

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(id),
            NotificationFeature.Subscription(after, NotificationFeature.OtherPushEndpoint));

        Assert.Equal(1, gone.GivenUp);
        Assert.False(wasOn);
        Assert.Equal(NoticeFailure.Rejected, failedWith);
        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal("push", data.GetProperty("kind").GetString());
        Assert.Equal("書斎のブラウザ", data.GetProperty("name").GetString());
        Assert.Equal(HashOf(NotificationFeature.OtherPushEndpoint), data.GetProperty(Fingerprint).GetString());
        Assert.False(data.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("lastResultAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("lastFailure").ValueKind);
        AssertNothingOf(after, body.GetRawText());
        Assert.Equal(after.KeyAsWritten, Opened(feature, held).KeyAsWritten);
    }

    [Fact(DisplayName = "BR-ND-015: A new subscription whose endpoint another row holds is refused, and both rows stay as they were")]
    public async Task ANewSubscriptionAnotherRowHoldsIsRefused()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver first = new();
        using PushReceiver second = new();
        Guid one = await feature.SubscribedAsync(first);
        Guid other = await feature.SubscribedAsync(second, "居間のブラウザ", NotificationFeature.OtherPushEndpoint);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(other),
            NotificationFeature.Subscription(second));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(
            [(one, HashOf(NotificationFeature.PushEndpoint)), (other, HashOf(NotificationFeature.OtherPushEndpoint))],
            feature.HeldDestinations.Destinations.Select(held => (held.Id.Value, held.EndpointHash!)));
        Assert.Equal(first.KeyAsWritten, Opened(feature, feature.HeldDestinations.Destinations[0]).KeyAsWritten);
    }

    [Fact(DisplayName = "BR-ND-015: A row given the endpoint it already holds takes the new keys")]
    public async Task ARowGivenItsOwnEndpointTakesTheNewKeys()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver before = new();
        using PushReceiver after = new();
        Guid id = await feature.SubscribedAsync(before);

        (HttpStatusCode status, _) = await feature.PutAsync(NotificationFeature.SubscriptionOf(id), NotificationFeature.Subscription(after));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(after.KeyAsWritten, Opened(feature, Assert.Single(feature.HeldDestinations.Destinations)).KeyAsWritten);
    }

    [Fact(DisplayName = "BR-ND-015: A new subscription is for a browser's row, so an id nobody holds and the row of a webhook are not found")]
    public async Task ANewSubscriptionIsForABrowsersRow()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid webhook = await feature.RegisteredAsync();
        byte[] before = feature.HeldDestinations.Destinations[0].SealedUrl;

        (HttpStatusCode unknown, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(Guid.NewGuid()),
            NotificationFeature.Subscription(browser));
        (HttpStatusCode ofAWebhook, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(webhook),
            NotificationFeature.Subscription(browser));

        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(HttpStatusCode.NotFound, ofAWebhook);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(WebhookTarget.Slack, held.Target);
        Assert.Same(before, held.SealedUrl);
        Assert.Null(held.EndpointHash);
    }

    [Fact(DisplayName = "BR-NT-008: A new subscription with a bad endpoint or bad keys is refused and the row keeps what it held")]
    public async Task ABadNewSubscriptionIsRefused()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);

        (HttpStatusCode elsewhere, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(id),
            NotificationFeature.Subscription(browser, "https://elsewhere.example/made-up-registration-qrst"));
        (HttpStatusCode keyless, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(id),
            new { endpoint = NotificationFeature.OtherPushEndpoint, keys = new { p256dh = browser.KeyAsWritten } });

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere);
        Assert.Equal(HttpStatusCode.BadRequest, keyless);
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), Assert.Single(feature.HeldDestinations.Destinations).EndpointHash);
    }

    private static string HashOf(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(endpoint)));

    private static PushSubscription Opened(NotificationFeature feature, NotificationDestination held)
        => feature.Built.Services.GetRequiredService<PushSubscriptionSeal>().Open(held.SealedUrl)!;

    private static void AssertNothingOf(PushReceiver browser, string text)
    {
        Assert.DoesNotContain("made-up-registration", text, StringComparison.Ordinal);
        Assert.DoesNotContain("googleapis", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mozilla", text, StringComparison.Ordinal);
        Assert.DoesNotContain(browser.KeyAsWritten, text, StringComparison.Ordinal);
        Assert.DoesNotContain(browser.SecretAsWritten, text, StringComparison.Ordinal);
        Assert.DoesNotContain("p256dh", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", text, StringComparison.OrdinalIgnoreCase);
    }
}
