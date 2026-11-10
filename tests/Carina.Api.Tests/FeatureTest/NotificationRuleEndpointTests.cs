using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationRuleEndpointTests
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

    [Fact(DisplayName = "D-9: Registering the first destination makes one set of rules that tells it of failures and trouble, and a later destination makes none")]
    public async Task TheFirstDestinationMakesTheUsualRule()
    {
        await using NotificationFeature feature = new();

        Guid first = await feature.RegisteredAsync();
        await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Rules);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement usual = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("失敗と異常", usual.GetProperty("name").GetString());
        Assert.True(usual.GetProperty("enabled").GetBoolean());
        Assert.Equal(UsualEvents, Words(usual.GetProperty("events")));
        Assert.Equal([first], Ids(usual.GetProperty("destinations")));
    }

    [Fact(DisplayName = "D-9: A destination registered while a set of rules is already held makes no other")]
    public async Task ADestinationRegisteredBesideARuleMakesNoOther()
    {
        await using NotificationFeature feature = new();
        NotificationRule held = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("手で作ったもの"),
            [NoticeKind.RecordingCompleted],
            [NotificationDestinationId.New()],
            NotificationFeature.Noon);
        feature.HeldRules.Rules.Add(held);

        await feature.RegisteredAsync();

        Assert.Same(held, Assert.Single(feature.HeldRules.Rules));
    }

    [Fact(DisplayName = "BR-NT-004: A set of rules is made of a name, the kinds it tells of and the destinations it tells, and is turned on unless it says otherwise")]
    public async Task ARuleIsMadeOfANameKindsAndDestinations()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        feature.HeldRules.Rules.Clear();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = " 録画の終わり ", events = new[] { "recordingCompleted", "recordingStarted", "recordingCompleted" }, destinations = new[] { study, livingRoom } });
        (HttpStatusCode off, JsonElement turnedOff) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "切ってあるもの", enabled = false, events = new[] { "encodeStarted" }, destinations = new[] { study } });
        (_, JsonElement listed) = await feature.GetAsync(NotificationFeature.Rules);

        Assert.Equal(HttpStatusCode.Created, status);
        JsonElement made = body.GetProperty("data");
        Assert.Equal("録画の終わり", made.GetProperty("name").GetString());
        Assert.True(made.GetProperty("enabled").GetBoolean());
        Assert.Equal(["recordingStarted", "recordingCompleted"], Words(made.GetProperty("events")));
        Assert.Equal([study, livingRoom], Ids(made.GetProperty("destinations")));
        Assert.Equal(NotificationFeature.Noon, made.GetProperty("createdAt").GetDateTime());
        Assert.Equal(HttpStatusCode.Created, off);
        Assert.False(turnedOff.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(
            ["録画の終わり", "切ってあるもの"],
            listed.GetProperty("data").GetProperty("items").EnumerateArray().Select(rule => rule.GetProperty("name").GetString()));
    }

    [Fact(DisplayName = "BR-NT-004: A set of rules that tells of nothing is kept, and one that tells nobody is refused")]
    public async Task ARuleTellsAtLeastOneDestination()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        feature.HeldRules.Rules.Clear();

        (HttpStatusCode nothing, JsonElement kept) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "空", events = Array.Empty<string>(), destinations = new[] { livingRoom } });
        (HttpStatusCode nobody, _) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "宛先なし", events = new[] { "recordingFailed" }, destinations = Array.Empty<Guid>() });
        (HttpStatusCode unsaid, _) = await feature.PostAsync(NotificationFeature.Rules, new { name = "宛先なし", events = new[] { "recordingFailed" } });
        (HttpStatusCode unknown, _) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "知らない宛先", events = new[] { "recordingFailed" }, destinations = new[] { livingRoom, Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Created, nothing);
        Assert.Empty(kept.GetProperty("data").GetProperty("events").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, nobody);
        Assert.Equal(HttpStatusCode.BadRequest, unsaid);
        Assert.Equal(HttpStatusCode.BadRequest, unknown);
        Assert.Single(feature.HeldRules.Rules);
    }

    [Fact(DisplayName = "BR-NT-004: A set of rules tells only of the kinds the ledger holds, and carries a name of one to sixty-four letters")]
    public async Task ARuleNamesOnlyKnownKinds()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        feature.HeldRules.Rules.Clear();
        Guid[] destinations = [livingRoom];

        (HttpStatusCode unknownWord, _) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "知らない出来事", events = new[] { "diskAlmostFull" }, destinations });
        (HttpStatusCode unknownNumber, _) = await feature.PostAsync(NotificationFeature.Rules, new { name = "知らない番号", events = new[] { 99 }, destinations });
        (HttpStatusCode unsaid, _) = await feature.PostAsync(NotificationFeature.Rules, new { name = "出来事なし", destinations });
        (HttpStatusCode blank, _) = await feature.PostAsync(NotificationFeature.Rules, new { name = " ", events = Array.Empty<string>(), destinations });
        (HttpStatusCode tooLong, _) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = new string('a', 65), events = Array.Empty<string>(), destinations });

        Assert.Equal(HttpStatusCode.BadRequest, unknownWord);
        Assert.Equal(HttpStatusCode.BadRequest, unknownNumber);
        Assert.Equal(HttpStatusCode.BadRequest, unsaid);
        Assert.Equal(HttpStatusCode.BadRequest, blank);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong);
        Assert.Empty(feature.HeldRules.Rules);
    }

    [Fact(DisplayName = "BR-NT-006: The twenty-first set of rules is refused as a conflict, and the twenty stay as they were")]
    public async Task TheTwentyFirstRuleIsRefused()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        feature.HeldRules.Rules.Clear();
        Guid[] destinations = [livingRoom];

        for (int made = 1; made <= NotificationRule.MostRules; made++)
        {
            (HttpStatusCode taken, _) = await feature.PostAsync(NotificationFeature.Rules, new { name = $"{made} 本目", events = Array.Empty<string>(), destinations });
            Assert.Equal(HttpStatusCode.Created, taken);
        }

        (HttpStatusCode status, _) = await feature.PostAsync(NotificationFeature.Rules, new { name = "21 本目", events = Array.Empty<string>(), destinations });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(20, feature.HeldRules.Rules.Count);
    }

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

    [Fact(DisplayName = "A set of rules is removed once, and the destinations it told stay")]
    public async Task ARuleIsRemovedOnce()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        Guid usual = Assert.Single(feature.HeldRules.Rules).Id.Value;

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(NotificationFeature.RuleOf(usual));
        (HttpStatusCode again, _) = await feature.DeleteAsync(NotificationFeature.RuleOf(usual));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(usual, body.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, again);
        Assert.Empty(feature.HeldRules.Rules);
        Assert.Single(feature.HeldDestinations.Destinations);
    }

    private static string[] Words(JsonElement list) => [.. list.EnumerateArray().Select(word => word.GetString() ?? string.Empty)];

    private static Guid[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(id => id.GetGuid())];
}
