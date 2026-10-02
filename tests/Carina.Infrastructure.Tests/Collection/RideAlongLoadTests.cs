using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Tests.Reservations;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class RideAlongLoadTests
{
    private static readonly DateTime Noon = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan EachLook = TimeSpan.FromMilliseconds(10);

    [Fact(DisplayName = "a ride that has read nothing yet has cost nothing")]
    public void ARideThatHasReadNothingYetHasCostNothing()
    {
        RideAlongLoad load = new(new SteppingClock(Noon, EachLook));

        Assert.Equal(0, load.Bytes);
        Assert.Equal(TimeSpan.Zero, load.Reading);
        Assert.Equal(TimeSpan.Zero, load.Writing);
        Assert.Equal(0, load.Allocated);
    }

    [Fact(DisplayName = "what a ride read, how long reading it took and what that allocated add up over the ride")]
    public void WhatARideReadAndHowLongReadingItTookAddUpOverTheRide()
    {
        RideAlongLoad load = new(new SteppingClock(Noon, EachLook));

        RideAlongMark first = load.Mark();
        byte[] kept = new byte[64 * 1024];

        load.Read(first, 12_032);

        RideAlongMark second = load.Mark();

        load.Read(second, 188);

        Assert.Equal(12_220, load.Bytes);
        Assert.Equal(EachLook * 2, load.Reading);
        Assert.True(load.Allocated >= kept.Length, $"the ride is said to have allocated {load.Allocated} byte(s)");
        Assert.Equal(TimeSpan.Zero, load.Writing);
    }

    [Fact(DisplayName = "how long writing the guide down took is kept apart from the reading")]
    public void HowLongWritingTheGuideDownTookIsKeptApartFromTheReading()
    {
        RideAlongLoad load = new(new SteppingClock(Noon, EachLook));

        load.Wrote(load.Mark());
        load.Wrote(load.Mark());

        Assert.Equal(EachLook * 2, load.Writing);
        Assert.Equal(TimeSpan.Zero, load.Reading);
        Assert.Equal(0, load.Bytes);
    }

    [Fact(DisplayName = "how long the ride has gone on is measured from when it began")]
    public void HowLongTheRideHasGoneOnIsMeasuredFromWhenItBegan()
    {
        RideAlongLoad load = new(new SteppingClock(Noon, EachLook));

        Assert.Equal(EachLook, load.Elapsed);
        Assert.Equal(EachLook * 2, load.Elapsed);
    }
}
