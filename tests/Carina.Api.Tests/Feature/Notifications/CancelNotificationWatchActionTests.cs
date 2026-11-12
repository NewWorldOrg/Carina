using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class CancelNotificationWatchActionTests
{
    private static readonly DateTime Starts = NotificationFeature.Noon.AddHours(2);

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("DELETE", NotificationGuard.OneWatch)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("DELETE", NotificationGuard.OneWatch)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("DELETE", NotificationGuard.OneWatch)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("DELETE", NotificationGuard.OneWatch)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NS-003: A programme that waits is withdrawn, and withdrawing it again changes nothing")]
    public async Task AProgrammeThatWaitsIsWithdrawn()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        Guid id = await feature.AskedAsync(feature.Announced(4001, Starts), Starts);
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(NotificationFeature.WatchOf(id));
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(9);
        (HttpStatusCode again, JsonElement repeated) = await feature.DeleteAsync(NotificationFeature.WatchOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(id, body.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal("cancelled", body.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal(NotificationFeature.Noon.AddMinutes(5), body.GetProperty("data").GetProperty("changedAt").GetDateTime());
        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal(NotificationFeature.Noon.AddMinutes(5), repeated.GetProperty("data").GetProperty("changedAt").GetDateTime());
        ProgrammeWatch held = Assert.Single(feature.HeldWatches.Watches);
        Assert.Equal(ProgrammeWatchState.Cancelled, held.State);
    }

    [Theory(DisplayName = "BR-NS-003: A programme already told of, or let go by, is not withdrawn")]
    [InlineData(ProgrammeWatchState.Notified)]
    [InlineData(ProgrammeWatchState.Skipped)]
    public async Task AProgrammeAlreadySettledIsNotWithdrawn(ProgrammeWatchState state)
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        ProgrammeWatch settled = Settled(feature, 4001, Starts, state, livingRoom);
        feature.HeldWatches.Watches.Add(settled);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(NotificationFeature.WatchOf(settled.Id.Value));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(state, Assert.Single(feature.HeldWatches.Watches).State);
    }

    [Fact(DisplayName = "BR-NS-003: A programme nobody asked to be told of is not found, and an id of all zeroes is refused")]
    public async Task AProgrammeNobodyAskedForIsNotFound()
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode unknown, _) = await feature.DeleteAsync(NotificationFeature.WatchOf(Guid.NewGuid()));
        (HttpStatusCode zeroes, _) = await feature.DeleteAsync(NotificationFeature.WatchOf(Guid.Empty));

        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(HttpStatusCode.BadRequest, zeroes);
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
}
