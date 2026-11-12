using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class RemoveNotificationDestinationActionTests
{
    private static readonly DateTime Starts = NotificationFeature.Noon.AddHours(2);

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("DELETE", NotificationGuard.OneDestination)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("DELETE", NotificationGuard.OneDestination)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("DELETE", NotificationGuard.OneDestination)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("DELETE", NotificationGuard.OneDestination)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NA-002: A refused write registers nothing, removes nothing and sends nothing")]
    public async Task ARefusedWriteChangesAndSendsNothing()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        using HttpClient elsewhere = feature.FromElsewhere();

        (HttpStatusCode removed, _) = await feature.DeleteAsync(NotificationFeature.OneOf(id), elsewhere);

        Assert.Equal(HttpStatusCode.Forbidden, removed);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(id, held.Id.Value);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "BR-NT-004: Removing a destination takes it out of the sets of rules, and removes a set left with nowhere to send")]
    public async Task RemovingTakesItOutOfTheRules()
    {
        await using NotificationFeature feature = new();
        Guid gone = await feature.RegisteredAsync();
        Guid kept = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        feature.HeldRules.Rules.Clear();
        NotificationRule both = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("両方へ"),
            [NoticeKind.RecordingFailed],
            [new NotificationDestinationId(gone), new NotificationDestinationId(kept)],
            NotificationFeature.Noon);
        NotificationRule only = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("片方だけ"),
            [NoticeKind.EncodeFailed],
            [new NotificationDestinationId(gone)],
            NotificationFeature.Noon);
        feature.HeldRules.Rules.AddRange([both, only]);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(NotificationFeature.OneOf(gone));
        (HttpStatusCode again, _) = await feature.DeleteAsync(NotificationFeature.OneOf(gone));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(gone, body.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, again);
        Assert.Equal([kept], feature.HeldDestinations.Destinations.Select(destination => destination.Id.Value));
        NotificationRule left = Assert.Single(feature.HeldRules.Rules);
        Assert.Same(both, left);
        Assert.Equal([kept], left.Destinations.Select(destination => destination.Value));
    }

    [Fact(DisplayName = "BR-ND-013: Removing a destination takes it out of the programmes that wait, withdraws one left with nowhere to send, and leaves the ones told of as they were")]
    public async Task RemovingADestinationTakesItOutOfTheProgrammesThatWait()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        Guid both = await feature.AskedAsync(feature.Announced(4001, Starts), Starts);
        string alone = feature.Announced(4002, Starts.AddHours(1), "作り物の番組 その二");
        (_, JsonElement asked) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme = alone, programmeStartsAt = Starts.AddHours(1), destinations = new[] { livingRoom } });
        Guid only = asked.GetProperty("data").GetProperty("id").GetGuid();
        ProgrammeWatch told = Settled(feature, 4003, NotificationFeature.Noon.AddMinutes(2), ProgrammeWatchState.Notified, livingRoom);
        feature.HeldWatches.Watches.Add(told);
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, _) = await feature.DeleteAsync(NotificationFeature.OneOf(livingRoom));
        (_, JsonElement body) = await feature.GetAsync(NotificationFeature.Watches);

        Assert.Equal(HttpStatusCode.OK, status);
        Dictionary<Guid, JsonElement> rows = body.GetProperty("data").GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid());
        Assert.Equal("waiting", rows[both].GetProperty("state").GetString());
        Assert.Equal([study], Ids(rows[both].GetProperty("destinations")));
        Assert.Equal(NotificationFeature.Noon.AddMinutes(5), rows[both].GetProperty("changedAt").GetDateTime());
        Assert.Equal("cancelled", rows[only].GetProperty("state").GetString());
        Assert.Empty(Ids(rows[only].GetProperty("destinations")));
        Assert.Equal("notified", rows[told.Id.Value].GetProperty("state").GetString());
        Assert.Equal([livingRoom], Ids(rows[told.Id.Value].GetProperty("destinations")));
        Assert.Empty(feature.HeldDeliveries.Rows);
    }

    [Fact(DisplayName = "BR-NT-004: Removing a browser takes it out of the sets of rules, and removes a set left with nowhere to send")]
    public async Task RemovingABrowserTakesItOutOfTheSetsOfRules()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);

        (HttpStatusCode status, _) = await feature.DeleteAsync(NotificationFeature.OneOf(id));
        (HttpStatusCode again, _) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", NotificationFeature.PushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten));

        Assert.True(status is HttpStatusCode.OK or HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Created, again);
        Assert.NotEqual(id, Assert.Single(feature.HeldDestinations.Destinations).Id.Value);
    }

    private static ProgrammeWatch Settled(
        NotificationFeature feature,
        int carried,
        DateTime startsAt,
        ProgrammeWatchState state,
        Guid destination)
    {
        DateTime asked = startsAt.AddHours(-3);

        return ProgrammeWatch.Rehydrate(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32_736), new ServiceId(1024), new EventId(carried)),
            new WatchedBroadcast(startsAt, startsAt.AddMinutes(30), "作り物の番組", NotificationFeature.Station),
            3,
            [new NotificationDestinationId(destination)],
            state,
            state is ProgrammeWatchState.Notified ? startsAt.AddMinutes(-3) : null,
            state is ProgrammeWatchState.Notified ? startsAt : null,
            asked,
            startsAt.AddMinutes(-3));
    }

    private static Guid[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(id => id.GetGuid())];

    private static object Asked(string name, string endpoint, string? key, string? secret)
        => new { name, endpoint, keys = new { p256dh = key, auth = secret } };
}
