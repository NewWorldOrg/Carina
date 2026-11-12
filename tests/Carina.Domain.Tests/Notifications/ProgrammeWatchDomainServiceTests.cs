using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Notifications;

public sealed class ProgrammeWatchDomainServiceTests
{
    private const string Name = "港町レシピ帖 #15「灯台守のパン」";

    private const string Station = "みなと総合1";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Starts = new(2026, 11, 9, 10, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = Starts.AddHours(-6);

    private static readonly ProgrammeId Watched = new(new NetworkId(32000), new ServiceId(1024), new EventId(4100));

    private readonly HeldProgrammes programmes = new();

    private readonly HeldServices stations = new();

    private readonly HeldNotificationSettings settings = new();

    private readonly HeldNotificationDestinations destinations = new();

    private readonly HeldProgrammeWatches watches = new();

    private ProgrammeWatchDomainService Service
        => new(programmes, stations, settings, destinations, watches, new FixedTimeProvider(new DateTimeOffset(Now)));

    [Fact(DisplayName = "BR-NT-007: a programme of the guide that has not begun is considered with what the guide and the station say of it")]
    public async Task AProgrammeOfTheGuideIsConsidered()
    {
        Announce(isShadow: false);
        stations.Services.Add(BroadcastService.Discover(Watched.NetworkId, Watched.ServiceId, Station, ServiceCategory.Television, Now));

        ProgrammeWatchConsidered considered = await Service.ConsiderAsync(Watched, Starts, 5, Cancel);

        Assert.Null(considered.Refusal);
        Assert.Equal(
            new ProgrammeWatchCandidate(Watched, new WatchedBroadcast(Starts, Starts.AddMinutes(45), Name, Station), 5, Now),
            considered.Candidate);
    }

    [Fact(DisplayName = "BR-NT-005: a programme asked for with no lead is told of at the lead of the settings, 3 minutes while none is held")]
    public async Task WithNoLeadTheLeadOfTheSettingsIsTaken()
    {
        Announce(isShadow: false);

        Assert.Equal(3, (await Service.ConsiderAsync(Watched, Starts, null, Cancel)).Candidate?.LeadMinutes);

        settings.Settings = NotificationSettings.Set(10, Now);

        Assert.Equal(10, (await Service.ConsiderAsync(Watched, Starts, null, Cancel)).Candidate?.LeadMinutes);
        Assert.Null((await Service.ConsiderAsync(Watched, Starts, null, Cancel)).Candidate?.Broadcast.StationName);
    }

    [Fact(DisplayName = "BR-NT-007: a programme the guide does not hold, or holds starting at another time, is not asked for")]
    public async Task AProgrammeTheGuideDoesNotHoldIsNotAskedFor()
    {
        Assert.Equal(ProgrammeWatchRefusal.NoSuchProgramme, (await Service.ConsiderAsync(Watched, Starts, 3, Cancel)).Refusal);

        Announce(isShadow: true);

        Assert.Equal(
            ProgrammeWatchRefusal.NoSuchProgramme,
            (await Service.ConsiderAsync(Watched, Starts.AddMinutes(1), 3, Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-NT-007: a programme the guide carries as a shadow is refused, with nothing to ask for")]
    public async Task AShadowProgrammeIsRefused()
    {
        Announce(isShadow: true);

        ProgrammeWatchConsidered shadow = await Service.ConsiderAsync(Watched, Starts, 2, Cancel);

        Assert.Equal(ProgrammeWatchRefusal.Shadow, shadow.Refusal);
        Assert.Null(shadow.Candidate);
    }

    [Fact(DisplayName = "BR-NT-007: with no destinations named, a programme is told to every destination turned on")]
    public async Task WithNoDestinationsNamedEveryDestinationTurnedOnIsTold()
    {
        NotificationDestination first = Destination();
        NotificationDestination off = Destination();
        off.Disable(Now);
        NotificationDestination second = Destination();

        ProgrammeWatchPlaced placed = await Service.PlaceAsync(Candidate(), null, Cancel);

        Assert.Null(placed.Refusal);
        Assert.Null(placed.Replaced);
        Assert.Equal([first.Id, second.Id], placed.Watch?.Destinations);
        Assert.Equal(ProgrammeWatchState.Waiting, placed.Watch?.State);
        Assert.Equal(5, placed.Watch?.LeadMinutes);
        Assert.Equal(Now, placed.Watch?.CreatedAt);
        Assert.Empty(watches.Watches);
    }

    [Fact(DisplayName = "BR-NT-007: with no destinations named and none turned on, a programme is not asked for")]
    public async Task WithNoneTurnedOnAProgrammeIsNotAskedFor()
    {
        Destination().Disable(Now);

        Assert.Equal(ProgrammeWatchRefusal.NoDestinationTurnedOn, (await Service.PlaceAsync(Candidate(), null, Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-NT-007: the destinations named are each registered and turned on, at least one, each told once")]
    public async Task TheDestinationsNamedAreRegisteredAndTurnedOn()
    {
        NotificationDestination first = Destination();
        NotificationDestination off = Destination();
        off.Disable(Now);
        NotificationDestination second = Destination();

        Assert.Equal(
            [second.Id, first.Id],
            (await Service.PlaceAsync(Candidate(), [second.Id.Value, first.Id.Value, second.Id.Value], Cancel)).Watch?.Destinations);
        Assert.Equal(ProgrammeWatchRefusal.DestinationsNotTurnedOn, (await Service.PlaceAsync(Candidate(), [], Cancel)).Refusal);
        Assert.Equal(
            ProgrammeWatchRefusal.DestinationsNotTurnedOn,
            (await Service.PlaceAsync(Candidate(), [first.Id.Value, off.Id.Value], Cancel)).Refusal);
        Assert.Equal(
            ProgrammeWatchRefusal.DestinationsNotTurnedOn,
            (await Service.PlaceAsync(Candidate(), [first.Id.Value, Guid.NewGuid()], Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-NT-007: a programme that waits, or was told of and is not over, is not asked for again")]
    public async Task AProgrammeThatStandsIsNotAskedForAgain()
    {
        NotificationDestination destination = Destination();
        ProgrammeWatch waiting = Held(destination);

        Assert.Equal(ProgrammeWatchRefusal.AlreadyAsked, (await Service.PlaceAsync(Candidate(), null, Cancel)).Refusal);

        waiting.Notify(Now);
        await watches.SaveAsync(waiting, Cancel);

        Assert.Equal(ProgrammeWatchRefusal.AlreadyAsked, (await Service.PlaceAsync(Candidate(), null, Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-NT-007: a programme whose row was withdrawn is asked for again under a new id, in place of the old row")]
    public async Task AWithdrawnProgrammeIsAskedForAgainInPlaceOfTheOldRow()
    {
        NotificationDestination destination = Destination();
        ProgrammeWatch withdrawn = Held(destination);
        withdrawn.Cancel(Now);
        await watches.SaveAsync(withdrawn, Cancel);

        ProgrammeWatchPlaced placed = await Service.PlaceAsync(Candidate(), null, Cancel);

        Assert.Null(placed.Refusal);
        Assert.Equal(withdrawn.Id, placed.Replaced?.Id);
        Assert.NotEqual(withdrawn.Id, placed.Watch?.Id);
    }

    [Fact(DisplayName = "BR-NT-007: with no destination turned on, that is said before a programme that stands in the way is")]
    public async Task NoDestinationIsSaidBeforeAProgrammeThatStands()
    {
        NotificationDestination destination = Destination();
        Held(destination);
        destination.Disable(Now);

        Assert.Equal(ProgrammeWatchRefusal.NoDestinationTurnedOn, (await Service.PlaceAsync(Candidate(), null, Cancel)).Refusal);
        Assert.Equal(
            ProgrammeWatchRefusal.DestinationsNotTurnedOn,
            (await Service.PlaceAsync(Candidate(), [destination.Id.Value], Cancel)).Refusal);
    }

    private static ProgrammeWatchCandidate Candidate()
        => new(Watched, new WatchedBroadcast(Starts, Starts.AddMinutes(45), Name, Station), 5, Now);

    private void Announce(bool isShadow)
        => programmes.Programmes.Add(
            Programme.Rehydrate(
                Watched,
                new TransportStreamId(32000),
                Starts,
                Starts.AddMinutes(45),
                Name,
                string.Empty,
                isShadow,
                Now));

    private NotificationDestination Destination()
    {
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("A channel"),
            WebhookTarget.Slack,
            [1, 2, 3],
            "wxyz",
            Now.AddSeconds(destinations.Destinations.Count));
        destinations.Destinations.Add(destination);

        return destination;
    }

    private ProgrammeWatch Held(NotificationDestination destination)
    {
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            Watched,
            new WatchedBroadcast(Starts, Starts.AddMinutes(45), Name, Station),
            3,
            [destination.Id],
            Now.AddHours(-1));
        watches.Watches.Add(watch);

        return watch;
    }
}
