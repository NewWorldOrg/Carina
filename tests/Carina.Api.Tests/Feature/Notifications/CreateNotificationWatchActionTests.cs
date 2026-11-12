using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class CreateNotificationWatchActionTests
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

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("POST", NotificationFeature.Watches)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("POST", NotificationFeature.Watches)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("POST", NotificationFeature.Watches)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("POST", NotificationFeature.Watches)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

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
