using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class ListNotificationDestinationsActionTests
{
    private const string Fingerprint = "fingerprint";

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("GET", NotificationFeature.Destinations)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A read from another origin is left alone")]
    [InlineData("GET", NotificationFeature.Destinations)]
    public async Task AReadFromAnotherOriginIsLeftAlone(string method, string path)
        => await NotificationGuard.AReadFromAnotherOriginIsLeftAloneAsync(method, path);

    [Fact(DisplayName = "BR-ND-012: A destination whose address cannot be opened says so in the list, and a trial of it is refused without sending")]
    public async Task AnUnreadableDestinationSaysSo()
    {
        await using NotificationFeature feature = new();
        NotificationDestination sealedElsewhere = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("居間"),
            WebhookTarget.Slack,
            [1, 2, 3, 4],
            "wxyz",
            NotificationFeature.Noon);
        feature.HeldDestinations.Destinations.Add(sealedElsewhere);

        (HttpStatusCode listed, JsonElement list) = await feature.GetAsync(NotificationFeature.Destinations);
        (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(sealedElsewhere.Id.Value));

        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.False(list.GetProperty("data").GetProperty("items")[0].GetProperty("readable").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, tried);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "BR-NT-008: The list names a browser by its kind and the fingerprint of its endpoint, and a webhook carries no fingerprint")]
    public async Task TheListNamesABrowserByItsFingerprint()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        await feature.RegisteredAsync();
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(1);
        await feature.SubscribedAsync(browser);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Destinations);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement items = body.GetProperty("data").GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("slack", items[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty(Fingerprint).ValueKind);
        Assert.Equal("push", items[1].GetProperty("kind").GetString());
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), items[1].GetProperty(Fingerprint).GetString());
        Assert.True(items[1].GetProperty("readable").GetBoolean());
        AssertNothingOf(browser, body.GetRawText());
    }

    [Fact(DisplayName = "BR-ND-012: A browser whose subscription cannot be opened says so in the list, and a trial of it is refused without sending")]
    public async Task ABrowserWhoseSubscriptionCannotBeOpenedSaysSo()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        NotificationDestination sealedElsewhere = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("書斎のブラウザ"),
            PushSubscription.Read(NotificationFeature.PushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten)!,
            [1, 2, 3, 4],
            NotificationFeature.Noon);
        feature.HeldDestinations.Destinations.Add(sealedElsewhere);

        (HttpStatusCode listed, JsonElement list) = await feature.GetAsync(NotificationFeature.Destinations);
        (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(sealedElsewhere.Id.Value));

        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.False(list.GetProperty("data").GetProperty("items")[0].GetProperty("readable").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, tried);
        Assert.Empty(feature.End.Heard);
    }

    private static string HashOf(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(endpoint)));

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
