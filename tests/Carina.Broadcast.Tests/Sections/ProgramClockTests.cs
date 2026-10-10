using Carina.Broadcast.Sections;

namespace Carina.Broadcast.Tests.Sections;

public sealed class ProgramClockTests
{
    private const long Second = 90_000;

    [Fact]
    public void TheClockIsNowhereUntilItIsFirstHeard()
    {
        ProgramClock clock = new();

        Assert.Null(clock.Now);
        Assert.Null(clock.Place(Second));
    }

    [Fact(DisplayName = "BR-BD-004: the clock is followed through its wrap, so it only reads forwards")]
    public void TheClockIsFollowedThroughItsWrap()
    {
        ProgramClock clock = new();

        Assert.Equal(ProgramClock.Modulus - Second, clock.Follow(ProgramClock.Modulus - Second));
        Assert.Equal(ProgramClock.Modulus + Second, clock.Follow(Second));
    }

    [Fact]
    public void AReadingALittleBehindStaysBehindRatherThanComingAroundAgain()
    {
        ProgramClock clock = new();
        clock.Follow(Second);

        Assert.Equal(-Second, clock.Follow(ProgramClock.Modulus - Second));
    }

    [Fact(DisplayName = "BR-BD-003: a moment read on the 33-bit clock is placed nearest to where the clock is, across the wrap")]
    public void AMomentIsPlacedNearestToWhereTheClockIs()
    {
        ProgramClock clock = new();
        clock.Follow(ProgramClock.Modulus - Second);

        Assert.Equal(ProgramClock.Modulus + (2 * Second), clock.Place(2 * Second));
        Assert.Equal(ProgramClock.Modulus - (3 * Second), clock.Place(ProgramClock.Modulus - (3 * Second)));
        Assert.Equal(ProgramClock.Modulus - Second, clock.Now);
    }
}
