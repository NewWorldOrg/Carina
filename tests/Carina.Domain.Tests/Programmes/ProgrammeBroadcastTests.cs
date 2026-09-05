using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeBroadcastTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-24)]
    [InlineData(-23)]
    [InlineData(0)]
    [InlineData(9 * 24)]
    [InlineData(10 * 24)]
    public void BrEv001AStartFromADayBehindToTenDaysAheadIsWithinTheHorizon(int hours)
        => Assert.True(Broadcast(Now.AddHours(hours)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001AStartFurtherBackThanADayIsOutsideTheHorizon()
        => Assert.False(Broadcast(Now.AddDays(-1).AddSeconds(-1)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001AStartFurtherAheadThanTenDaysIsOutsideTheHorizon()
        => Assert.False(Broadcast(Now.AddDays(10).AddSeconds(1)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001AStartOnTheDayTheBroadcastCalendarBeganIsOutsideTheHorizon()
        => Assert.False(
            Broadcast(new DateTime(1858, 11, 17, 0, 0, 0, DateTimeKind.Utc)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001TheHorizonReachesPastTheCoverageAimedFor()
        => Assert.True(ProgrammeBroadcast.FurthestAhead > new CollectionSettings().WantedCoverage);

    [Fact]
    public void BrEv001TheHorizonIsJudgedAgainstAUtcClock()
        => Assert.Throws<ArgumentException>(() =>
            Broadcast(Now).StartsWithinTheHorizonAt(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Local)));

    private static ProgrammeBroadcast Broadcast(DateTime startsAt)
        => new(
            new ProgrammeId(new NetworkId(32739), new ServiceId(1049), new EventId(47289)),
            new TransportStreamId(32739),
            startsAt,
            startsAt.AddMinutes(30),
            "Now",
            string.Empty,
            IsShadow: false);
}
