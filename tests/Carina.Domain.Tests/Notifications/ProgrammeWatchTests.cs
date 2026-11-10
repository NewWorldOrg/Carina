using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Notifications;

public sealed class ProgrammeWatchTests
{
    private const string Name = "港町レシピ帖 #15「灯台守のパン」";

    private const string Station = "みなと総合1";

    private static readonly DateTime Starts = new(2026, 11, 9, 10, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = Starts.AddMinutes(45);

    private static readonly DateTime Asked = Starts.AddHours(-6);

    private static readonly ProgrammeId Watched = new(new NetworkId(32000), new ServiceId(1024), new EventId(4100));

    private static readonly WatchedBroadcast Announced = new(Starts, Ends, Name, Station);

    private static readonly NotificationDestinationId First = NotificationDestinationId.New();

    private static readonly NotificationDestinationId Second = NotificationDestinationId.New();

    [Fact(DisplayName = "D-5: a programme asked to be told of waits, holding what the guide said and where to tell")]
    public void AProgrammeAskedToBeToldOfWaits()
    {
        ProgrammeWatchId id = ProgrammeWatchId.New();

        ProgrammeWatch watch = ProgrammeWatch.Ask(id, Watched, Announced, 5, [First, Second], Asked);

        Assert.Equal(id, watch.Id);
        Assert.Equal(Watched, watch.Programme);
        Assert.Equal(Starts, watch.StartsAt);
        Assert.Equal(Ends, watch.EndsAt);
        Assert.Equal(Name, watch.ProgrammeName);
        Assert.Equal(Station, watch.StationName);
        Assert.Equal(5, watch.LeadMinutes);
        Assert.Equal([First, Second], watch.Destinations);
        Assert.Equal(ProgrammeWatchState.Waiting, watch.State);
        Assert.Null(watch.NotifiedAt);
        Assert.Equal(Asked, watch.CreatedAt);
        Assert.Equal(Asked, watch.ChangedAt);
    }

    [Theory(DisplayName = "BR-NT-005: a notice is asked for 0, 1, 3, 5 or 10 minutes before the programme")]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(-1, false)]
    [InlineData(2, false)]
    [InlineData(15, false)]
    public void TheMinutesBeforeAreOneOfThoseOffered(int leadMinutes, bool offered)
    {
        if (offered)
        {
            Assert.Equal(leadMinutes, Waiting(leadMinutes).LeadMinutes);

            return;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Waiting(leadMinutes));
    }

    [Fact(DisplayName = "D-5: a programme is asked to be told of to at least one destination, each once")]
    public void AProgrammeIsToldOfToAtLeastOneDestination()
    {
        Assert.Throws<ArgumentException>(() => ProgrammeWatch.Ask(ProgrammeWatchId.New(), Watched, Announced, 3, [], Asked));
        Assert.Equal(
            [First, Second],
            ProgrammeWatch.Ask(ProgrammeWatchId.New(), Watched, Announced, 3, [First, Second, First], Asked).Destinations);
    }

    [Theory(DisplayName = "D-5: only a programme that has not begun is asked to be told of")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3600)]
    public void OnlyAProgrammeThatHasNotBegunIsAskedFor(int secondsAfterTheStart)
    {
        Assert.Throws<InvalidOperationException>(
            () => ProgrammeWatch.Ask(
                ProgrammeWatchId.New(),
                Watched,
                Announced,
                3,
                [First],
                Starts.AddSeconds(secondsAfterTheStart)));
    }

    [Fact(DisplayName = "a broadcast that ends no later than it begins, or is named too long, is not held")]
    public void ABroadcastThatDoesNotStandIsNotHeld()
    {
        Assert.Throws<ArgumentException>(() => Asking(Announced with { EndsAt = Starts }));
        Assert.Throws<ArgumentException>(() => Asking(Announced with { ProgrammeName = new string('a', Programme.NameMaxLength + 1) }));
        Assert.Throws<ArgumentException>(
            () => Asking(Announced with { StationName = new string('a', BroadcastService.NameMaxLength + 1) }));
        Assert.Throws<ArgumentException>(
            () => Asking(Announced with { StartsAt = DateTime.SpecifyKind(Starts, DateTimeKind.Local) }));
        Assert.Null(Asking(Announced with { StationName = null }).StationName);
    }

    [Theory(DisplayName = "BR-NC-003: a programme is due once the minutes before its start have passed, on air from its start, over at its end")]
    [InlineData(-181, ProgrammeWatchMoment.NotYet)]
    [InlineData(-180, ProgrammeWatchMoment.Soon)]
    [InlineData(-1, ProgrammeWatchMoment.Soon)]
    [InlineData(0, ProgrammeWatchMoment.OnAir)]
    [InlineData(2699, ProgrammeWatchMoment.OnAir)]
    [InlineData(2700, ProgrammeWatchMoment.Over)]
    public void TheMomentFollowsTheClock(int secondsFromTheStart, ProgrammeWatchMoment moment)
    {
        Assert.Equal(moment, Waiting(3).MomentAt(Starts.AddSeconds(secondsFromTheStart)));
    }

    [Fact(DisplayName = "BR-NS-003: a waiting programme is notified once, and the time is kept")]
    public void AWaitingProgrammeIsNotifiedOnce()
    {
        ProgrammeWatch watch = Waiting(3);
        DateTime at = Starts.AddMinutes(-3);

        watch.Notify(at);

        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
        Assert.Equal(at, watch.NotifiedAt);
        Assert.Equal(Starts, watch.ToldStartsAt);
        Assert.False(watch.HasMovedSinceTold);
        Assert.Equal(at, watch.ChangedAt);
        Assert.Throws<InvalidOperationException>(() => watch.Notify(at));
        Assert.Throws<InvalidOperationException>(() => watch.Skip(at));
        Assert.Throws<InvalidOperationException>(() => watch.Cancel(at));
    }

    [Fact(DisplayName = "BR-NS-003: a waiting programme that was over before it was told of is skipped")]
    public void AWaitingProgrammeThatIsOverIsSkipped()
    {
        ProgrammeWatch watch = Waiting(3);

        watch.Skip(Ends);

        Assert.Equal(ProgrammeWatchState.Skipped, watch.State);
        Assert.Null(watch.NotifiedAt);
        Assert.Throws<InvalidOperationException>(() => watch.Notify(Ends));
    }

    [Fact(DisplayName = "BR-NS-003: a waiting programme is cancelled, and a cancelled one is not told of")]
    public void AWaitingProgrammeIsCancelled()
    {
        ProgrammeWatch watch = Waiting(3);

        watch.Cancel(Asked.AddMinutes(1));

        Assert.Equal(ProgrammeWatchState.Cancelled, watch.State);
        Assert.Equal(Asked.AddMinutes(1), watch.ChangedAt);
        Assert.Throws<InvalidOperationException>(() => watch.Notify(Starts));
        Assert.Throws<InvalidOperationException>(() => watch.Cancel(Starts));
    }

    [Fact(DisplayName = "BR-NS-003: a waiting programme follows the guide to its new time")]
    public void AWaitingProgrammeFollowsTheGuide()
    {
        ProgrammeWatch watch = Waiting(3);
        DateTime at = Asked.AddHours(1);

        bool changed = watch.Follow(new WatchedBroadcast(Starts.AddMinutes(30), Ends.AddMinutes(40), "港町レシピ帖 #15", null), at);

        Assert.True(changed);
        Assert.False(watch.HasMovedSinceTold);
        Assert.Null(watch.ToldStartsAt);
        Assert.Equal(Starts.AddMinutes(30), watch.StartsAt);
        Assert.Equal(Ends.AddMinutes(40), watch.EndsAt);
        Assert.Equal("港町レシピ帖 #15", watch.ProgrammeName);
        Assert.Equal(Station, watch.StationName);
        Assert.Equal(ProgrammeWatchState.Waiting, watch.State);
        Assert.Equal(at, watch.ChangedAt);
        Assert.Equal(ProgrammeWatchMoment.NotYet, watch.MomentAt(Starts.AddMinutes(-3)));
    }

    [Theory(DisplayName = "BR-NS-003: a start within a minute of the start that was told is followed without being a move")]
    [InlineData(60, false)]
    [InlineData(-60, false)]
    [InlineData(61, true)]
    [InlineData(-61, true)]
    public void AStartThatBarelyMovesIsNotAMove(int seconds, bool moved)
    {
        ProgrammeWatch watch = Waiting(3);
        watch.Notify(Starts.AddMinutes(-3));

        Assert.True(watch.Follow(Announced with { StartsAt = Starts.AddSeconds(seconds) }, Starts.AddMinutes(-2)));

        Assert.Equal(moved, watch.HasMovedSinceTold);
        Assert.Equal(Starts.AddSeconds(seconds), watch.StartsAt);
        Assert.Equal(Starts, watch.ToldStartsAt);
        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
    }

    [Fact(DisplayName = "BR-NS-003: the start that was told stays while the row follows the guide step by step")]
    public void TheStartThatWasToldStaysWhileTheRowFollowsTheGuide()
    {
        ProgrammeWatch watch = Waiting(3);
        watch.Notify(Starts.AddMinutes(-3));

        watch.Follow(Announced with { StartsAt = Starts.AddSeconds(40) }, Starts.AddMinutes(-2));
        bool afterOneStep = watch.HasMovedSinceTold;
        watch.Follow(Announced with { StartsAt = Starts.AddSeconds(80) }, Starts.AddMinutes(-1));

        Assert.False(afterOneStep);
        Assert.True(watch.HasMovedSinceTold);
        Assert.Equal(Starts, watch.ToldStartsAt);
        Assert.Equal(Starts.AddSeconds(80), watch.StartsAt);
    }

    [Fact(DisplayName = "a guide that names the programme with nothing keeps the name held, and one that says the same changes nothing")]
    public void AGuideThatSaysNothingNewChangesNothing()
    {
        ProgrammeWatch watch = Waiting(3);

        Assert.False(watch.Follow(Announced with { ProgrammeName = string.Empty }, Asked.AddHours(1)));

        Assert.Equal(Name, watch.ProgrammeName);
        Assert.Equal(Asked, watch.ChangedAt);
    }

    [Fact(DisplayName = "BR-NS-003: a programme that was skipped or cancelled no longer follows the guide")]
    public void ASettledProgrammeNoLongerFollowsTheGuide()
    {
        ProgrammeWatch cancelled = Waiting(3);
        cancelled.Cancel(Asked);
        ProgrammeWatch skipped = Waiting(3);
        skipped.Skip(Ends);

        Assert.Throws<InvalidOperationException>(() => cancelled.Follow(Announced, Asked));
        Assert.Throws<InvalidOperationException>(() => skipped.Follow(Announced, Ends));
    }

    [Fact(DisplayName = "D-5: a destination taken away is no longer told, and a waiting programme left with none is cancelled")]
    public void ADestinationTakenAwayIsNoLongerTold()
    {
        ProgrammeWatch watch = ProgrammeWatch.Ask(ProgrammeWatchId.New(), Watched, Announced, 3, [First, Second], Asked);
        DateTime at = Asked.AddMinutes(5);

        Assert.True(watch.StopTelling(First, at));

        Assert.Equal([Second], watch.Destinations);
        Assert.Equal(ProgrammeWatchState.Waiting, watch.State);
        Assert.Equal(at, watch.ChangedAt);
        Assert.False(watch.StopTelling(First, at.AddMinutes(1)));
        Assert.Equal(at, watch.ChangedAt);

        Assert.True(watch.StopTelling(Second, at.AddMinutes(2)));

        Assert.Empty(watch.Destinations);
        Assert.Equal(ProgrammeWatchState.Cancelled, watch.State);
    }

    [Fact(DisplayName = "D-5: a programme already told of keeps the destinations it was told to")]
    public void AProgrammeAlreadyToldOfKeepsItsDestinations()
    {
        ProgrammeWatch watch = Waiting(3);
        watch.Notify(Starts);

        Assert.False(watch.StopTelling(First, Starts.AddMinutes(1)));

        Assert.Equal([First], watch.Destinations);
        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
    }

    [Fact(DisplayName = "D-5: a programme stands in the way of asking again while it waits, or was told of and is not over")]
    public void AProgrammeStandsInTheWayOfAskingAgain()
    {
        ProgrammeWatch waiting = Waiting(3);
        ProgrammeWatch notified = Waiting(3);
        notified.Notify(Starts.AddMinutes(-3));
        ProgrammeWatch cancelled = Waiting(3);
        cancelled.Cancel(Asked);
        ProgrammeWatch skipped = Waiting(3);
        skipped.Skip(Ends);

        Assert.True(waiting.StandsAt(Ends.AddDays(1)));
        Assert.True(notified.StandsAt(Ends.AddSeconds(-1)));
        Assert.False(notified.StandsAt(Ends));
        Assert.False(cancelled.StandsAt(Asked));
        Assert.False(skipped.StandsAt(Ends));
    }

    [Fact(DisplayName = "a row read back is refused when its state and the time it was told of disagree")]
    public void ARowWhoseStateAndTimeDisagreeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Read(ProgrammeWatchState.Notified, null, [First]));
        Assert.Throws<ArgumentException>(() => Read(ProgrammeWatchState.Waiting, Starts, [First]));
        Assert.Throws<ArgumentException>(() => Told(Starts, null));
        Assert.Throws<ArgumentException>(() => Told(null, Starts));
        Assert.Equal(Starts.AddMinutes(-5), Told(Starts, Starts.AddMinutes(-5)).ToldStartsAt);
        Assert.Throws<ArgumentException>(() => Read(ProgrammeWatchState.Waiting, null, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read((ProgrammeWatchState)9, null, [First]));
        Assert.Empty(Read(ProgrammeWatchState.Cancelled, null, []).Destinations);
        Assert.Equal(Starts, Read(ProgrammeWatchState.Notified, Starts, [First]).NotifiedAt);
    }

    private static ProgrammeWatch Waiting(int leadMinutes)
        => ProgrammeWatch.Ask(ProgrammeWatchId.New(), Watched, Announced, leadMinutes, [First], Asked);

    private static ProgrammeWatch Asking(WatchedBroadcast announced)
        => ProgrammeWatch.Ask(ProgrammeWatchId.New(), Watched, announced, 3, [First], Asked);

    private static ProgrammeWatch Read(
        ProgrammeWatchState state,
        DateTime? notifiedAt,
        IReadOnlyList<NotificationDestinationId> destinations)
        => ProgrammeWatch.Rehydrate(
            ProgrammeWatchId.New(),
            Watched,
            Announced,
            3,
            destinations,
            state,
            notifiedAt,
            notifiedAt is null ? null : Starts,
            Asked,
            Asked);

    private static ProgrammeWatch Told(DateTime? notifiedAt, DateTime? toldStartsAt)
        => ProgrammeWatch.Rehydrate(
            ProgrammeWatchId.New(),
            Watched,
            Announced,
            3,
            [First],
            notifiedAt is null ? ProgrammeWatchState.Waiting : ProgrammeWatchState.Notified,
            notifiedAt,
            toldStartsAt,
            Asked,
            Asked);
}
