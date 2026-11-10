using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Notifications;

public sealed class ProgrammeWatchLookTests
{
    private const string Name = "港町レシピ帖 #15「灯台守のパン」";

    private const string Station = "みなと総合1";

    private static readonly DateTime Starts = new(2026, 11, 9, 10, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = Starts.AddMinutes(45);

    private static readonly DateTime Asked = Starts.AddHours(-6);

    private static readonly ProgrammeId Watched = new(new NetworkId(32000), new ServiceId(1024), new EventId(4100));

    private static readonly WatchedBroadcast AsAsked = new(Starts, Ends, Name, Station);

    private static readonly WatchedGuide Unchanged = new(GuideStanding.Announced, AsAsked);

    private static readonly WatchedGuide Silent = new(GuideStanding.NothingKnown, null);

    private static readonly WatchedGuide Gone = new(GuideStanding.NoLongerAnnounced, null);

    private static readonly IReadOnlySet<NoticeKind> NothingTold = new HashSet<NoticeKind>();

    [Fact(DisplayName = "BR-NS-003: a programme that waits is left alone before its minutes are reached")]
    public void AProgrammeThatWaitsIsLeftAloneBeforeItsMinutesAreReached()
    {
        ProgrammeWatch watch = Waiting();

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, Unchanged, NothingTold, Starts.AddMinutes(-3).AddSeconds(-1));

        Assert.Equal(ProgrammeWatchStep.Nothing, step);
        Assert.Equal(ProgrammeWatchState.Waiting, watch.State);
    }

    [Theory(DisplayName = "BR-NS-003: a programme that waits is told of once its minutes before the start are reached, and while it is on air")]
    [InlineData(-180)]
    [InlineData(0)]
    [InlineData(600)]
    public void AProgrammeThatWaitsIsToldOfOnceItsMinutesAreReached(int secondsFromTheStart)
    {
        ProgrammeWatch watch = Waiting();
        DateTime at = Starts.AddSeconds(secondsFromTheStart);

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, Silent, NothingTold, at);

        Assert.Equal(new ProgrammeWatchStep(true, NoticeKind.ProgrammeStarting), step);
        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
        Assert.Equal(at, watch.NotifiedAt);
    }

    [Theory(DisplayName = "BR-NS-003: a programme that waits and has ended is passed over and told to nobody")]
    [InlineData(GuideStanding.NothingKnown)]
    [InlineData(GuideStanding.NoLongerAnnounced)]
    public void AProgrammeThatWaitsAndHasEndedIsPassedOver(GuideStanding standing)
    {
        ProgrammeWatch watch = Waiting();

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, new WatchedGuide(standing, null), NothingTold, Ends);

        Assert.Equal(new ProgrammeWatchStep(true, null), step);
        Assert.Equal(ProgrammeWatchState.Skipped, watch.State);
    }

    [Fact(DisplayName = "BR-NS-003: a programme the guide stops naming before it begins is cancelled and told of once")]
    public void AProgrammeTheGuideStopsNamingBeforeItBeginsIsCancelled()
    {
        ProgrammeWatch watch = Waiting();

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, Gone, NothingTold, Starts.AddHours(-1));

        Assert.Equal(new ProgrammeWatchStep(true, NoticeKind.WatchedProgrammeGone), step);
        Assert.Equal(ProgrammeWatchState.Cancelled, watch.State);
    }

    [Fact(DisplayName = "BR-NS-003: a programme under way is told of from what was copied, whatever the guide has stopped naming")]
    public void AProgrammeUnderWayIsToldOfFromWhatWasCopied()
    {
        ProgrammeWatch watch = Waiting();

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, Gone, NothingTold, Starts.AddMinutes(10));

        Assert.Equal(NoticeKind.ProgrammeStarting, step.Tells);
        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
    }

    [Fact(DisplayName = "BR-NS-003: a programme that moves before it is told of is told of by its new time")]
    public void AProgrammeThatMovesBeforeItIsToldOfIsToldOfByItsNewTime()
    {
        ProgrammeWatch watch = Waiting();
        WatchedGuide later = new(GuideStanding.Announced, AsAsked with { StartsAt = Starts.AddMinutes(30), EndsAt = Ends.AddMinutes(30) });

        ProgrammeWatchStep followed = ProgrammeWatchLook.At(watch, later, NothingTold, Starts.AddMinutes(-2));
        ProgrammeWatchStep still = ProgrammeWatchLook.At(watch, later, NothingTold, Starts.AddMinutes(26));
        ProgrammeWatchStep due = ProgrammeWatchLook.At(watch, later, NothingTold, Starts.AddMinutes(27));

        Assert.Equal(new ProgrammeWatchStep(true, null), followed);
        Assert.Equal(ProgrammeWatchStep.Nothing, still);
        Assert.Equal(NoticeKind.ProgrammeStarting, due.Tells);
        Assert.Equal(Starts.AddMinutes(30), watch.StartsAt);
    }

    [Fact(DisplayName = "BR-NS-003: a programme that moves after it was told of stays told of, and the move is told once")]
    public void AProgrammeThatMovesAfterItWasToldOfStaysToldOf()
    {
        ProgrammeWatch watch = ToldOf();
        WatchedGuide later = new(GuideStanding.Announced, AsAsked with { StartsAt = Starts.AddMinutes(20), EndsAt = Ends.AddMinutes(20) });
        WatchedGuide laterStill = new(GuideStanding.Announced, AsAsked with { StartsAt = Starts.AddMinutes(40), EndsAt = Ends.AddMinutes(40) });

        ProgrammeWatchStep first = ProgrammeWatchLook.At(watch, later, NothingTold, Starts.AddMinutes(-1));
        ProgrammeWatchStep again = ProgrammeWatchLook.At(watch, later, Told(NoticeKind.WatchedProgrammeMoved), Starts);
        ProgrammeWatchStep second = ProgrammeWatchLook.At(watch, laterStill, Told(NoticeKind.WatchedProgrammeMoved), Starts.AddMinutes(1));

        Assert.Equal(new ProgrammeWatchStep(true, NoticeKind.WatchedProgrammeMoved), first);
        Assert.Equal(ProgrammeWatchStep.Nothing, again);
        Assert.Equal(new ProgrammeWatchStep(true, null), second);
        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
        Assert.Equal(Starts.AddMinutes(40), watch.StartsAt);
    }

    [Fact(DisplayName = "BR-NS-003: a start that drifts by a minute or less after the programme was told of is followed without a word")]
    public void AStartThatDriftsByAMinuteOrLessIsFollowedWithoutAWord()
    {
        ProgrammeWatch watch = ToldOf();
        WatchedGuide drifted = new(GuideStanding.Announced, AsAsked with { StartsAt = Starts.AddSeconds(60) });

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, drifted, NothingTold, Starts.AddMinutes(-1));

        Assert.Equal(new ProgrammeWatchStep(true, null), step);
    }

    [Fact(DisplayName = "D-5: a programme the guide stops naming after it was told of, before it begins, is told of once more")]
    public void AProgrammeTheGuideStopsNamingAfterItWasToldOfIsToldOfOnceMore()
    {
        ProgrammeWatch watch = ToldOf();

        ProgrammeWatchStep first = ProgrammeWatchLook.At(watch, Gone, NothingTold, Starts.AddMinutes(-1));
        ProgrammeWatchStep again = ProgrammeWatchLook.At(watch, Gone, Told(NoticeKind.WatchedProgrammeGone), Starts.AddSeconds(-30));
        ProgrammeWatchStep underWay = ProgrammeWatchLook.At(ToldOf(), Gone, NothingTold, Starts);

        Assert.Equal(new ProgrammeWatchStep(false, NoticeKind.WatchedProgrammeGone), first);
        Assert.Equal(ProgrammeWatchStep.Nothing, again);
        Assert.Equal(ProgrammeWatchStep.Nothing, underWay);
        Assert.Equal(ProgrammeWatchState.Notified, watch.State);
    }

    [Theory(DisplayName = "BR-NS-003: a programme that was passed over or cancelled, or told of and ended, is looked at no more")]
    [InlineData(ProgrammeWatchState.Skipped, -30)]
    [InlineData(ProgrammeWatchState.Cancelled, -30)]
    [InlineData(ProgrammeWatchState.Notified, 45)]
    public void AProgrammeThatIsSettledIsLookedAtNoMore(ProgrammeWatchState state, int minutesFromTheStart)
    {
        ProgrammeWatch watch = Read(state);
        WatchedGuide later = new(GuideStanding.Announced, AsAsked with { StartsAt = Starts.AddHours(2), EndsAt = Ends.AddHours(2) });

        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, later, NothingTold, Starts.AddMinutes(minutesFromTheStart));

        Assert.Equal(ProgrammeWatchStep.Nothing, step);
        Assert.Equal(Starts, watch.StartsAt);
    }

    [Fact(DisplayName = "D-5: the guide says of a programme what it names, and nothing of one it only holds a place for")]
    public void TheGuideSaysOfAProgrammeWhatItNames()
    {
        DateTime heard = Asked.AddHours(1);

        WatchedGuide named = WatchedGuide.Of(AProgramme(isShadow: false, heard), heard, Station);
        WatchedGuide placeholder = WatchedGuide.Of(AProgramme(isShadow: true, heard), heard, Station);
        WatchedGuide dropped = WatchedGuide.Of(AProgramme(isShadow: false, heard.AddHours(-1)), heard, Station);
        WatchedGuide neverHeard = WatchedGuide.Of(null, null, null);

        Assert.Equal(new WatchedGuide(GuideStanding.Announced, AsAsked), named);
        Assert.Equal(new WatchedGuide(GuideStanding.NothingKnown, null), placeholder);
        Assert.Equal(new WatchedGuide(GuideStanding.NoLongerAnnounced, null), dropped);
        Assert.Equal(new WatchedGuide(GuideStanding.NothingKnown, null), neverHeard);
    }

    private static IReadOnlySet<NoticeKind> Told(params NoticeKind[] kinds) => kinds.ToHashSet();

    private static ProgrammeWatch Waiting()
        => ProgrammeWatch.Ask(ProgrammeWatchId.New(), Watched, AsAsked, 3, [NotificationDestinationId.New()], Asked);

    private static ProgrammeWatch ToldOf() => Read(ProgrammeWatchState.Notified);

    private static ProgrammeWatch Read(ProgrammeWatchState state)
        => ProgrammeWatch.Rehydrate(
            ProgrammeWatchId.New(),
            Watched,
            AsAsked,
            3,
            [NotificationDestinationId.New()],
            state,
            state is ProgrammeWatchState.Notified ? Starts.AddMinutes(-3) : null,
            Asked,
            Starts.AddMinutes(-3));

    private static Programme AProgramme(bool isShadow, DateTime lastHeardAt)
        => Programme.Rehydrate(
            Watched,
            new TransportStreamId(32000),
            Starts,
            Ends,
            Name,
            string.Empty,
            isShadow,
            Asked,
            sounds: 1,
            audio: AudioMode.Stereo,
            lastHeardAt: lastHeardAt);
}
