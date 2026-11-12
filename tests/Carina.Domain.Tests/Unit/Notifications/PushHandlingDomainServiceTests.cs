using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class PushHandlingDomainServiceTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly HeldProgrammeWatches watches = new();

    [Theory(DisplayName = "D-11: a notice that is not the start of a programme asked for is kept for 24 hours at normal urgency")]
    [InlineData(NoticeKind.RecordingStarted)]
    [InlineData(NoticeKind.RecordingFailed)]
    [InlineData(NoticeKind.EncodeCompleted)]
    [InlineData(NoticeKind.DeviceDisconnected)]
    [InlineData(NoticeKind.WatchedProgrammeMoved)]
    [InlineData(NoticeKind.WatchedProgrammeGone)]
    public async Task AnyOtherNoticeIsKeptForADayAtNormalUrgency(NoticeKind kind)
    {
        ProgrammeWatch watch = Watch(At.AddMinutes(3), At.AddMinutes(33));

        PushHandling handling = await Service().OfAsync(Told(kind, watch.Id.Value), At, CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(24), handling.Lifetime);
        Assert.Equal(PushUrgency.Normal, handling.Urgency);
        Assert.Equal(PushHandling.Usual, handling);
    }

    [Fact(DisplayName = "D-11: the notice that a programme asked for begins is urgent, and is kept until that programme ends")]
    public async Task TheStartOfAProgrammeIsUrgentAndKeptUntilItEnds()
    {
        ProgrammeWatch watch = Watch(At.AddMinutes(3), At.AddMinutes(33));

        PushHandling handling = await Service().OfAsync(
            Told(NoticeKind.ProgrammeStarting, watch.Id.Value),
            At,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(33), handling.Lifetime);
        Assert.Equal(PushUrgency.High, handling.Urgency);
    }

    [Fact(DisplayName = "D-11: the time kept is counted from when the message is sent, so a later try is kept for less")]
    public async Task ALaterTryIsKeptForLess()
    {
        ProgrammeWatch watch = Watch(At.AddMinutes(3), At.AddMinutes(33));
        Notice notice = Told(NoticeKind.ProgrammeStarting, watch.Id.Value);

        PushHandling later = await Service().OfAsync(notice, At.AddMinutes(13).AddSeconds(30), CancellationToken.None);

        Assert.Equal(new TimeSpan(0, 19, 30), later.Lifetime);
    }

    [Fact(DisplayName = "D-11: once the programme has ended its start is not kept at all, and is still urgent")]
    public async Task OnceTheProgrammeHasEndedItsStartIsNotKept()
    {
        ProgrammeWatch watch = Watch(At.AddMinutes(3), At.AddMinutes(33));
        Notice notice = Told(NoticeKind.ProgrammeStarting, watch.Id.Value);

        PushHandling atTheEnd = await Service().OfAsync(notice, At.AddMinutes(33), CancellationToken.None);
        PushHandling after = await Service().OfAsync(notice, At.AddHours(2), CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, atTheEnd.Lifetime);
        Assert.Equal(TimeSpan.Zero, after.Lifetime);
        Assert.Equal(PushUrgency.High, after.Urgency);
    }

    [Fact(DisplayName = "D-11: the start of a programme whose row is no longer held is not kept, and is still urgent")]
    public async Task TheStartOfAProgrammeNoLongerHeldIsNotKept()
    {
        PushHandling handling = await Service().OfAsync(
            Told(NoticeKind.ProgrammeStarting, Guid.NewGuid()),
            At,
            CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, handling.Lifetime);
        Assert.Equal(PushUrgency.High, handling.Urgency);
    }

    [Fact(DisplayName = "a handling is never kept for less than nothing, and names an urgency there is")]
    public void AHandlingIsNeverKeptForLessThanNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PushHandling(TimeSpan.FromSeconds(-1), PushUrgency.Normal));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PushHandling(TimeSpan.Zero, (PushUrgency)0));
    }

    private PushHandlingDomainService Service() => new(watches);

    private static Notice Told(NoticeKind kind, Guid source)
        => Notice.Take(NoticeId.New(), kind, source, At, new NoticeWords("A title", "A body", null), At);

    private ProgrammeWatch Watch(DateTime starts, DateTime ends)
    {
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32000), new ServiceId(1024), new EventId(4100)),
            new WatchedBroadcast(starts, ends, "A programme", null),
            3,
            [NotificationDestinationId.New()],
            At.AddHours(-1));
        watches.Watches.Add(watch);

        return watch;
    }
}
