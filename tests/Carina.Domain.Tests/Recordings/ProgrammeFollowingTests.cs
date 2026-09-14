using Carina.Domain.Programmes;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class ProgrammeFollowingTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 20, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime WindowEnd = new(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Ahead = TimeSpan.FromMinutes(20);

    [Fact]
    public void AProgrammeThatRunsLaterTakesTheRecordingWithIt()
    {
        WindowMove move = Assert.IsType<WindowMove>(Next(WindowEnd.AddMinutes(10)));

        Assert.Equal(WindowEnd.AddMinutes(10), move.EndsAt);
        Assert.False(move.EndUndecided);
    }

    [Fact]
    public void TheMarginTheReservationAskedForIsCarriedOntoTheNewEnd()
    {
        WindowMove move = Assert.IsType<WindowMove>(
            Next(WindowEnd, marginAfter: TimeSpan.FromMinutes(3)));

        Assert.Equal(WindowEnd.AddMinutes(3), move.EndsAt);
    }

    [Fact]
    public void AProgrammeThatSaysItWillFinishSoonerDoesNotCutTheRecordingShort()
    {
        Assert.Null(Next(WindowEnd.AddMinutes(-10)));
        Assert.Null(Next(WindowEnd));
        Assert.Null(Next(WindowEnd.AddTicks(-1)));
    }

    [Fact]
    public void AnEndThatHasNotMovedIsNotAskedForTwice()
    {
        WindowMove move = Assert.IsType<WindowMove>(Next(WindowEnd.AddMinutes(10)));

        Assert.Null(Next(WindowEnd.AddMinutes(10), windowEnd: move.EndsAt));
    }

    [Fact]
    public void AGuideThatHasGoneQuietMovesNothing()
    {
        Assert.Null(Next(WindowEnd.AddMinutes(10), standing: GuideStanding.NothingKnown));
        Assert.Null(Next(null, standing: GuideStanding.NothingKnown));
    }

    [Fact]
    public void ABroadcastTheGuideNoLongerAnnouncesMovesNothingEither()
    {
        Assert.Null(Next(WindowEnd.AddMinutes(10), standing: GuideStanding.NoLongerAnnounced));
        Assert.Null(Next(null, standing: GuideStanding.NoLongerAnnounced));
    }

    [Fact]
    public void AnEndNobodyHasAnnouncedIsHeldAHorizonAheadOfNow()
    {
        WindowMove move = Assert.IsType<WindowMove>(Next(null, windowEnd: Now.AddMinutes(5)));

        Assert.Equal(Now + Ahead, move.EndsAt);
        Assert.True(move.EndUndecided);
    }

    [Fact]
    public void AHorizonIsRenewedOnlyOnceHalfOfItHasBeenSpent()
    {
        Assert.Null(Next(null, windowEnd: Now + Ahead));
        Assert.Null(Next(null, windowEnd: Now + TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1)));
        Assert.NotNull(Next(null, windowEnd: Now + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void AWindowThatHasAlreadyRunOutIsStillCarriedForwardWhileTheProgrammeIsAnnounced()
    {
        WindowMove move = Assert.IsType<WindowMove>(Next(null, windowEnd: Now.AddMinutes(-5)));

        Assert.Equal(Now + Ahead, move.EndsAt);
        Assert.True(move.EndUndecided);
    }

    [Fact]
    public void AHorizonIsLongerThanNoTimeAtAllAndAMarginRunsForwards()
    {
        Assert.Equal(
            "undecidedEndAhead",
            Assert.Throws<ArgumentOutOfRangeException>(() => Next(null, ahead: TimeSpan.Zero)).ParamName);
        Assert.Equal(
            "marginAfter",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Next(null, marginAfter: TimeSpan.FromSeconds(-1))).ParamName);
    }

    [Fact]
    public void EveryMomentItIsGivenIsAUtcOne()
    {
        Assert.Equal(
            "windowEnd",
            Assert.Throws<ArgumentException>(
                () => Next(WindowEnd.AddMinutes(10), windowEnd: DateTime.SpecifyKind(WindowEnd, DateTimeKind.Local)))
                .ParamName);
        Assert.Equal(
            "now",
            Assert.Throws<ArgumentException>(
                () => Next(null, now: DateTime.SpecifyKind(Now, DateTimeKind.Unspecified))).ParamName);
        Assert.Equal(
            "announcedEnd",
            Assert.Throws<ArgumentException>(
                () => Next(DateTime.SpecifyKind(WindowEnd.AddMinutes(10), DateTimeKind.Local))).ParamName);
    }

    private static WindowMove? Next(
        DateTime? announcedEnd,
        GuideStanding standing = GuideStanding.Announced,
        TimeSpan? marginAfter = null,
        DateTime? windowEnd = null,
        DateTime? now = null,
        TimeSpan? ahead = null)
        => ProgrammeFollowing.Next(
            standing,
            announcedEnd,
            marginAfter ?? TimeSpan.Zero,
            windowEnd ?? WindowEnd,
            now ?? Now,
            ahead ?? Ahead);
}
