using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class CarriedSoundsTests
{
    [Fact]
    public void WhatWasCountedIsKnownAndHoldsTheSoundsItCounted()
    {
        CarriedSounds carried = CarriedSounds.Counted(2);

        Assert.True(carried.Known);
        Assert.Equal(string.Empty, carried.Note);
        Assert.True(carried.Holds(SoundTrack.Main));
        Assert.True(carried.Holds(SoundTrack.Secondary));
    }

    [Fact]
    public void ABroadcastOfOneSoundHoldsNoSecondaryOne()
    {
        CarriedSounds carried = CarriedSounds.Counted(1);

        Assert.True(carried.Holds(SoundTrack.Main));
        Assert.False(carried.Holds(SoundTrack.Secondary));
    }

    [Fact]
    public void WhatCouldNotBeReadIsNotKnownAndHoldsNothingAndSaysWhy()
    {
        CarriedSounds carried = CarriedSounds.Unread("  the programme said nothing  ");

        Assert.False(carried.Known);
        Assert.Empty(carried.Tracks);
        Assert.False(carried.Holds(SoundTrack.Main));
        Assert.Equal("the programme said nothing", carried.Note);
    }

    [Fact]
    public void ALongComplaintIsKeptToItsEnd()
    {
        CarriedSounds carried = CarriedSounds.Unread(new string('x', CarriedSounds.LongestNote + 40) + "here");

        Assert.Equal(CarriedSounds.LongestNote, carried.Note.Length);
        Assert.EndsWith("here", carried.Note, StringComparison.Ordinal);
    }
}
