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

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(2, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false)]
    public void AStreamCarriesAWholeStreamPlacementOnlyWhereTheSoundItPointsAtIsThere(
        int counted,
        int ordinal,
        bool carried)
    {
        Assert.Equal(carried, CarriedSounds.Counted(counted).Carries(SoundPlacement.WholeStream(ordinal)));
    }

    [Fact]
    public void APlacementOnOneChannelIsCarriedWhereTheStreamItPointsAtIsThere()
    {
        Assert.True(CarriedSounds.Counted(1).Carries(SoundPlacement.OneChannelOf(0, SoundChannel.Left)));
        Assert.False(CarriedSounds.Counted(0).Carries(SoundPlacement.OneChannelOf(0, SoundChannel.Right)));
    }

    [Fact]
    public void WhatCouldNotBeReadCarriesNoPlacementAtAll()
    {
        CarriedSounds carried = CarriedSounds.Unread("the programme said nothing");

        Assert.False(carried.Carries(SoundPlacement.WholeStream(0)));
        Assert.False(carried.Carries(SoundPlacement.OneChannelOf(0, SoundChannel.Left)));
    }

    [Fact]
    public void APlacementIsAskedOfSomethingRatherThanOfNothing()
    {
        Assert.Throws<ArgumentNullException>(() => CarriedSounds.Counted(1).Carries(null!));
    }

    [Fact]
    public void ALongComplaintIsKeptToItsEnd()
    {
        CarriedSounds carried = CarriedSounds.Unread(new string('x', CarriedSounds.LongestNote + 40) + "here");

        Assert.Equal(CarriedSounds.LongestNote, carried.Note.Length);
        Assert.EndsWith("here", carried.Note, StringComparison.Ordinal);
    }
}
