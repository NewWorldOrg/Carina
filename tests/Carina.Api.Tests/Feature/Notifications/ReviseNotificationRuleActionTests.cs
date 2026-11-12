using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class ReviseNotificationRuleActionTests
{
    private static readonly string[] UsualEvents =
    [
        "recordingTruncated",
        "recordingFailed",
        "encodeFailed",
        "reservationCompeting",
        "reservationMissed",
        "programmeMoved",
        "qualityIncidentDetected",
        "qualityIncidentResolved",
        "deviceDisconnected",
    ];

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("PATCH", NotificationGuard.OneRule)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("PATCH", NotificationGuard.OneRule)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("PATCH", NotificationGuard.OneRule)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("PATCH", NotificationGuard.OneRule)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NT-004: A change to a set of rules changes what it names and leaves the rest as it was")]
    public async Task AChangeLeavesWhatItDoesNotName()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        NotificationRule usual = Assert.Single(feature.HeldRules.Rules);
        string rule = NotificationFeature.RuleOf(usual.Id.Value);
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(5);

        (HttpStatusCode renamed, JsonElement afterName) = await feature.PatchAsync(rule, new { name = "失敗だけ" });
        (HttpStatusCode retold, JsonElement afterEvents) = await feature.PatchAsync(rule, new { events = new[] { "encodeFailed" } });
        (HttpStatusCode resent, JsonElement afterDestinations) = await feature.PatchAsync(rule, new { destinations = new[] { study } });
        (HttpStatusCode switched, JsonElement afterSwitch) = await feature.PatchAsync(rule, new { enabled = false });

        Assert.Equal(HttpStatusCode.OK, renamed);
        Assert.Equal("失敗だけ", afterName.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(UsualEvents, Words(afterName.GetProperty("data").GetProperty("events")));
        Assert.Equal(HttpStatusCode.OK, retold);
        Assert.Equal(["encodeFailed"], Words(afterEvents.GetProperty("data").GetProperty("events")));
        Assert.Equal([livingRoom], Ids(afterEvents.GetProperty("data").GetProperty("destinations")));
        Assert.Equal(HttpStatusCode.OK, resent);
        Assert.Equal([study], Ids(afterDestinations.GetProperty("data").GetProperty("destinations")));
        Assert.Equal(["encodeFailed"], Words(afterDestinations.GetProperty("data").GetProperty("events")));
        Assert.Equal(HttpStatusCode.OK, switched);
        JsonElement last = afterSwitch.GetProperty("data");
        Assert.False(last.GetProperty("enabled").GetBoolean());
        Assert.Equal("失敗だけ", last.GetProperty("name").GetString());
        Assert.Equal(NotificationFeature.Noon.AddMinutes(5), last.GetProperty("changedAt").GetDateTime());
        Assert.Equal(NotificationFeature.Noon, last.GetProperty("createdAt").GetDateTime());
    }

    [Fact(DisplayName = "BR-NT-004: A change that would leave a set of rules telling nobody, or names what is not held, is refused and changes nothing")]
    public async Task ABadChangeChangesNothing()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        NotificationRule usual = Assert.Single(feature.HeldRules.Rules);
        string rule = NotificationFeature.RuleOf(usual.Id.Value);
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(5);

        (HttpStatusCode nobody, _) = await feature.PatchAsync(rule, new { name = "別の名前", destinations = Array.Empty<Guid>() });
        (HttpStatusCode unknownDestination, _) = await feature.PatchAsync(rule, new { name = "別の名前", destinations = new[] { Guid.NewGuid() } });
        (HttpStatusCode unknownKind, _) = await feature.PatchAsync(rule, new { name = "別の名前", events = new[] { 99 } });
        (HttpStatusCode blank, _) = await feature.PatchAsync(rule, new { name = " ", enabled = false });
        (HttpStatusCode noSuch, _) = await feature.PatchAsync(NotificationFeature.RuleOf(Guid.NewGuid()), new { name = "別の名前" });

        Assert.Equal(HttpStatusCode.BadRequest, nobody);
        Assert.Equal(HttpStatusCode.BadRequest, unknownDestination);
        Assert.Equal(HttpStatusCode.BadRequest, unknownKind);
        Assert.Equal(HttpStatusCode.BadRequest, blank);
        Assert.Equal(HttpStatusCode.NotFound, noSuch);
        Assert.Equal("失敗と異常", usual.Name.Value);
        Assert.True(usual.Enabled);
        Assert.Equal([livingRoom], usual.Destinations.Select(destination => destination.Value));
        Assert.Equal(NotificationFeature.Noon, usual.ChangedAt);
    }

    private static string[] Words(JsonElement list) => [.. list.EnumerateArray().Select(word => word.GetString() ?? string.Empty)];

    private static Guid[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(id => id.GetGuid())];
}
