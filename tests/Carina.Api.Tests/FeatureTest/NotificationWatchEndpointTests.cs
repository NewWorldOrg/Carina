using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationWatchEndpointTests
{
    private static readonly DateTime Starts = NotificationFeature.Noon.AddHours(2);

    private static readonly string[] LettersOfAnAddress =
    [
        NotificationFeature.Secret,
        "/services/T0000",
        "/api/webhooks/1234",
        "hooks.slack.com",
        "discord.com",
    ];

    [Fact(DisplayName = "BR-NT-007: A programme that has not begun is asked to be told of, with the usual minutes and every destination that is turned on")]
    public async Task AProgrammeIsAskedToBeToldOfWithTheUsualMinutesAndEveryDestinationTurnedOn()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        Guid turnedOff = await feature.RegisteredAsync("玄関");
        await feature.PatchAsync(NotificationFeature.OneOf(turnedOff), new { enabled = false });
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Created, status);
        JsonElement made = body.GetProperty("data");
        Assert.Equal(programme, made.GetProperty("programmeId").GetString());
        Assert.Equal(32_736, made.GetProperty("networkId").GetInt32());
        Assert.Equal(1024, made.GetProperty("serviceId").GetInt32());
        Assert.Equal(4001, made.GetProperty("eventId").GetInt32());
        Assert.Equal(Starts, made.GetProperty("startsAt").GetDateTime());
        Assert.Equal(Starts.AddMinutes(30), made.GetProperty("endsAt").GetDateTime());
        Assert.Equal("作り物の番組", made.GetProperty("programmeName").GetString());
        Assert.Equal(NotificationFeature.Station, made.GetProperty("stationName").GetString());
        Assert.Equal(3, made.GetProperty("leadMinutes").GetInt32());
        Assert.Equal(new[] { livingRoom, study }.Order(), Ids(made.GetProperty("destinations")).Order());
        Assert.Equal("waiting", made.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, made.GetProperty("notifiedAt").ValueKind);
        Assert.Equal(NotificationFeature.Noon, made.GetProperty("createdAt").GetDateTime());
        ProgrammeWatch held = Assert.Single(feature.HeldWatches.Watches);
        Assert.Equal(made.GetProperty("id").GetGuid(), held.Id.Value);
        Assert.Equal(ProgrammeWatchState.Waiting, held.State);
    }

    [Fact(DisplayName = "BR-NT-005: A programme asked to be told of without minutes takes the minutes the settings hold")]
    public async Task AProgrammeAskedWithoutMinutesTakesTheMinutesOfTheSettings()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        feature.HeldSettings.Settings = NotificationSettings.Set(10, NotificationFeature.Noon);
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(10, body.GetProperty("data").GetProperty("leadMinutes").GetInt32());
    }

    [Fact(DisplayName = "BR-NT-007: A programme is asked to be told of with minutes and destinations of its own")]
    public async Task AProgrammeIsAskedWithMinutesAndDestinationsOfItsOwn()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        Guid study = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts, leadMinutes = 0, destinations = new[] { study, study } });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(0, body.GetProperty("data").GetProperty("leadMinutes").GetInt32());
        Assert.Equal([study], Ids(body.GetProperty("data").GetProperty("destinations")));
    }

    [Theory(DisplayName = "BR-NT-005: A programme asked to be told of some other number of minutes before is refused")]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(15)]
    public async Task MinutesThatAreNotOfferedAreRefused(int leadMinutes)
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts, leadMinutes });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Theory(DisplayName = "BR-NT-007: A programme asked to be told of to no destination, to one that is not registered or to one that is turned off is refused")]
    [InlineData("none")]
    [InlineData("unregistered")]
    [InlineData("turnedOff")]
    public async Task DestinationsThatCannotBeToldAreRefused(string named)
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        Guid turnedOff = await feature.RegisteredAsync("玄関", NotificationFeature.DiscordUrl);
        await feature.PatchAsync(NotificationFeature.OneOf(turnedOff), new { enabled = false });
        string programme = feature.Announced(4001, Starts);
        Guid[] destinations = named switch
        {
            "none" => [],
            "unregistered" => [livingRoom, Guid.NewGuid()],
            _ => [livingRoom, turnedOff],
        };

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts, destinations });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Fact(DisplayName = "BR-NT-007: A programme asked to be told of while no destination is turned on is refused as a conflict")]
    public async Task AProgrammeAskedWhileNoDestinationIsTurnedOnIsRefused()
    {
        await using NotificationFeature feature = new();
        Guid turnedOff = await feature.RegisteredAsync();
        await feature.PatchAsync(NotificationFeature.OneOf(turnedOff), new { enabled = false });
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Theory(DisplayName = "BR-NT-007: A request that names no programme, or names it in a form that cannot be read, is refused")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("32736-1024")]
    [InlineData("a-b-c")]
    public async Task AProgrammeThatCannotBeReadIsRefused(string? programme)
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact(DisplayName = "BR-NT-007: A request that does not say when the programme starts is refused")]
    public async Task ARequestWithoutTheStartIsRefused()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode status, _) = await feature.PostAsync(NotificationFeature.Watches, new { programme });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Fact(DisplayName = "BR-NT-007: A programme the guide does not hold, or holds at another start, is not found")]
    public async Task AProgrammeTheGuideDoesNotHoldIsNotFound()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);

        (HttpStatusCode unknown, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme = "32736-1024-4999", programmeStartsAt = Starts });
        (HttpStatusCode elsewhen, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts.AddDays(7) });

        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(HttpStatusCode.NotFound, elsewhen);
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Theory(DisplayName = "BR-NT-007: A programme that has begun or has ended is refused as a conflict")]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(45)]
    public async Task AProgrammeThatHasBegunIsRefused(int minutesIn)
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);
        feature.Clock.Now = Starts.AddMinutes(minutesIn);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Fact(DisplayName = "BR-NT-007: A programme asked to be told of a second before it begins is taken")]
    public async Task AProgrammeAskedForASecondBeforeItBeginsIsTaken()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);
        feature.Clock.Now = Starts.AddSeconds(-1);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Created, status);
        ProgrammeWatch held = Assert.Single(feature.HeldWatches.Watches);
        Assert.Equal(ProgrammeWatchState.Waiting, held.State);
        Assert.Equal(Starts, held.StartsAt);
    }

    [Fact(DisplayName = "BR-NT-007: A programme carried as a shadow of another service's broadcast is refused as a conflict, as it is when asked to be recorded")]
    public async Task AShadowOfAnotherServicesBroadcastIsRefused()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts, shadow: true);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("shadow", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.HeldWatches.Watches);
    }

    [Fact(DisplayName = "BR-NT-007: A programme already waiting to be told of is not asked for a second time")]
    public async Task AProgrammeAlreadyWaitingIsNotAskedAgain()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);
        Guid first = await feature.AskedAsync(programme, Starts);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts, leadMinutes = 10 });

        Assert.Equal(HttpStatusCode.Conflict, status);
        ProgrammeWatch held = Assert.Single(feature.HeldWatches.Watches);
        Assert.Equal(first, held.Id.Value);
        Assert.Equal(3, held.LeadMinutes);
    }

    [Fact(DisplayName = "BR-NT-007: A programme already told of and not yet over is not asked for a second time")]
    public async Task AProgrammeAlreadyToldOfIsNotAskedAgain()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);
        feature.HeldWatches.Watches.Add(Settled(feature, 4001, Starts, ProgrammeWatchState.Notified, livingRoom));
        feature.Clock.Now = Starts.AddMinutes(-2);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Single(feature.HeldWatches.Watches);
    }

    [Theory(DisplayName = "BR-NT-007: A row told of stands in the way of the number's next broadcast until its own broadcast ends, and gives way from the moment it ends")]
    [InlineData(-1, HttpStatusCode.Conflict, ProgrammeWatchState.Notified)]
    [InlineData(0, HttpStatusCode.Created, ProgrammeWatchState.Waiting)]
    public async Task ARowToldOfGivesWayFromTheMomentItsBroadcastEnds(
        int secondsFromTheEnd,
        HttpStatusCode expected,
        ProgrammeWatchState left)
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        DateTime before = Starts.AddHours(-1);
        string programme = feature.Announced(4001, Starts);
        feature.HeldWatches.Watches.Add(Settled(feature, 4001, before, ProgrammeWatchState.Notified, livingRoom));
        feature.Clock.Now = before.AddMinutes(30).AddSeconds(secondsFromTheEnd);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(expected, status);
        Assert.Equal(left, Assert.Single(feature.HeldWatches.Watches).State);
    }

    [Fact(DisplayName = "BR-NT-007: A programme whose asking was withdrawn is asked for again, and the row withdrawn gives way to the new one")]
    public async Task AProgrammeWithdrawnIsAskedForAgain()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        string programme = feature.Announced(4001, Starts);
        Guid first = await feature.AskedAsync(programme, Starts);
        await feature.DeleteAsync(NotificationFeature.WatchOf(first));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts, leadMinutes = 5 });

        Assert.Equal(HttpStatusCode.Created, status);
        ProgrammeWatch held = Assert.Single(feature.HeldWatches.Watches);
        Assert.NotEqual(first, held.Id.Value);
        Assert.Equal(body.GetProperty("data").GetProperty("id").GetGuid(), held.Id.Value);
        Assert.Equal(ProgrammeWatchState.Waiting, held.State);
        Assert.Equal(5, held.LeadMinutes);
    }

    [Fact(DisplayName = "BR-NT-007: A broadcast that reuses the number of one that went by is asked for, and the old row gives way")]
    public async Task ABroadcastReusingANumberIsAskedFor()
    {
        await using NotificationFeature feature = new();
        Guid livingRoom = await feature.RegisteredAsync();
        DateTime lastWeek = Starts.AddDays(-7);
        string programme = feature.Announced(4001, Starts);
        feature.HeldWatches.Watches.Add(Settled(feature, 4001, lastWeek, ProgrammeWatchState.Skipped, livingRoom));

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.Watches,
            new { programme, programmeStartsAt = Starts });

        Assert.Equal(HttpStatusCode.Created, status);
        ProgrammeWatch held = Assert.Single(feature.HeldWatches.Watches);
        Assert.Equal(Starts, held.StartsAt);
        Assert.Equal(ProgrammeWatchState.Waiting, held.State);
    }

    [Fact(DisplayName = "BR-NS-003: The programmes asked to be told of are listed with their states, the earliest start first")]
    public async Task TheProgrammesAskedAreListedTheEarliestFirst()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        Guid later = await feature.AskedAsync(feature.Announced(4002, Starts.AddHours(3), "作り物の番組 その二"), Starts.AddHours(3));
        Guid sooner = await feature.AskedAsync(feature.Announced(4001, Starts), Starts);
        Guid withdrawn = await feature.AskedAsync(feature.Announced(4003, Starts.AddHours(1), "作り物の番組 その三"), Starts.AddHours(1));
        await feature.DeleteAsync(NotificationFeature.WatchOf(withdrawn));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Watches);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        Assert.Equal([sooner, withdrawn, later], items.Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(["waiting", "cancelled", "waiting"], items.Select(item => item.GetProperty("state").GetString()));
        Assert.Equal("作り物の番組 その二", items[2].GetProperty("programmeName").GetString());
    }

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

    [Fact(DisplayName = "BR-NS-003: A programme nobody asked to be told of is not found, and an id of all zeroes is refused")]
    public async Task AProgrammeNobodyAskedForIsNotFound()
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode unknown, _) = await feature.DeleteAsync(NotificationFeature.WatchOf(Guid.NewGuid()));
        (HttpStatusCode zeroes, _) = await feature.DeleteAsync(NotificationFeature.WatchOf(Guid.Empty));

        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(HttpStatusCode.BadRequest, zeroes);
    }

    [Fact(DisplayName = "BR-NA-003: No answer about a programme asked to be told of carries the address of a destination")]
    public async Task NoAnswerCarriesTheAddressOfADestination()
    {
        await using NotificationFeature feature = new();
        await feature.RegisteredAsync();
        await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        string programme = feature.Announced(4001, Starts);
        List<string> answered = [];

        (_, JsonElement made) = await feature.PostAsync(NotificationFeature.Watches, new { programme, programmeStartsAt = Starts });
        answered.Add(made.GetRawText());
        (_, JsonElement refused) = await feature.PostAsync(NotificationFeature.Watches, new { programme, programmeStartsAt = Starts });
        answered.Add(refused.GetRawText());
        (_, JsonElement listed) = await feature.GetAsync(NotificationFeature.Watches);
        answered.Add(listed.GetRawText());
        (_, JsonElement withdrawn) = await feature.DeleteAsync(
            NotificationFeature.WatchOf(made.GetProperty("data").GetProperty("id").GetGuid()));
        answered.Add(withdrawn.GetRawText());

        Assert.All(LettersOfAnAddress, letters =>
            Assert.DoesNotContain(answered, answer => answer.Contains(letters, StringComparison.Ordinal)));
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
}
