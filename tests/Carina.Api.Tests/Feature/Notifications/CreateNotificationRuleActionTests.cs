using System.Net;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class CreateNotificationRuleActionTests
{
    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("POST", NotificationFeature.Rules)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("POST", NotificationFeature.Rules)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("POST", NotificationFeature.Rules)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("POST", NotificationFeature.Rules)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

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

    [Theory(DisplayName = "D-10: A set of rules cannot tell of the kinds told of one programme asked for by itself")]
    [InlineData("programmeStarting")]
    [InlineData("watchedProgrammeMoved")]
    [InlineData("watchedProgrammeGone")]
    public async Task ARuleNamesNoKindToldOfOneProgramme(string kind)
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        feature.HeldRules.Rules.Clear();
        Guid[] destinations = [livingRoom];
        (_, JsonElement made) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "失敗だけ", events = new[] { "recordingFailed" }, destinations });
        string held = $"{NotificationFeature.Rules}/{made.GetProperty("data").GetProperty("id").GetString()}";

        (HttpStatusCode making, _) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "番組の開始", events = new[] { kind }, destinations });
        (HttpStatusCode changing, _) = await feature.PatchAsync(held, new { events = new[] { "recordingFailed", kind } });

        Assert.Equal(HttpStatusCode.BadRequest, making);
        Assert.Equal(HttpStatusCode.BadRequest, changing);
        Assert.Equal([NoticeKind.RecordingFailed], Assert.Single(feature.HeldRules.Rules).Events);
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

    [Fact(DisplayName = "BR-NT-004: A destination named twice in a set of rules is held once, when the set is made and when it is changed")]
    public async Task ADestinationNamedTwiceIsHeldOnce()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        feature.HeldRules.Rules.Clear();

        (HttpStatusCode made, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Rules,
            new { name = "録画の終わり", events = new[] { "recordingCompleted" }, destinations = new[] { study, livingRoom, study } });
        NotificationRule held = Assert.Single(feature.HeldRules.Rules);
        Guid[] heldWhenMade = [.. held.Destinations.Select(destination => destination.Value)];
        (HttpStatusCode changed, JsonElement after) = await feature.PatchAsync(
            NotificationFeature.RuleOf(held.Id.Value),
            new { destinations = new[] { livingRoom, livingRoom } });

        Assert.Equal(HttpStatusCode.Created, made);
        Assert.Equal([study, livingRoom], Ids(body.GetProperty("data").GetProperty("destinations")));
        Assert.Equal([study, livingRoom], heldWhenMade);
        Assert.Equal(HttpStatusCode.OK, changed);
        Assert.Equal([livingRoom], Ids(after.GetProperty("data").GetProperty("destinations")));
        Assert.Equal([livingRoom], Assert.Single(feature.HeldRules.Rules).Destinations.Select(destination => destination.Value));
    }

    private static Guid[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(id => id.GetGuid())];

    private static string[] Words(JsonElement list) => [.. list.EnumerateArray().Select(word => word.GetString() ?? string.Empty)];
}
