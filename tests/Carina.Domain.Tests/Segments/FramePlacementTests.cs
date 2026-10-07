using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class FramePlacementTests
{
    private static readonly FrameClock Broadcast = FrameClock.Of(30000, 1001, TimeSpan.FromMilliseconds(33));

    [Fact(DisplayName = "frames whose times are rounded to the millisecond each land on their own frame")]
    public void FramesRoundedToTheMillisecondLandOnTheirOwnFrame()
    {
        FramePlacement placement = new(Broadcast);

        List<FramePlace> places =
        [
            .. Enumerable.Range(0, 300)
                .Select(frame => placement.Place(TimeSpan.FromMilliseconds(Math.Round(Broadcast.At(frame).TotalMilliseconds)))),
        ];

        Assert.All(places, place => Assert.Equal(new FramePlace(true, 0, null), place));
        Assert.Equal(300, placement.Placed);
    }

    [Fact(DisplayName = "a frame up to half a frame off lands on the frame nearest it")]
    public void AFrameUpToHalfAFrameOffLandsOnTheNearest()
    {
        FramePlacement placement = new(Broadcast);
        placement.Place(Broadcast.At(0));

        FramePlace place = placement.Place(Broadcast.At(1) + TimeSpan.FromMilliseconds(15));

        Assert.Equal(new FramePlace(true, 0, null), place);
        Assert.Equal(2, placement.Placed);
    }

    [Fact(DisplayName = "a frame that skips one or two frames is kept after copies of the one before, and that is not a gap")]
    public void AFrameThatSkipsAFewIsKeptAfterCopiesWithoutAGap()
    {
        FramePlacement placement = new(Broadcast);
        placement.Place(Broadcast.At(0));

        FramePlace place = placement.Place(Broadcast.At(3));

        Assert.Equal(new FramePlace(true, 2, null), place);
        Assert.Equal(4, placement.Placed);
    }

    [Fact(DisplayName = "frames skipped for a tenth of a second or more are a gap from the first skipped to the one kept")]
    public void FramesSkippedForATenthOrMoreAreAGap()
    {
        FramePlacement placement = new(Broadcast);
        placement.Place(Broadcast.At(0));

        FramePlace place = placement.Place(Broadcast.At(541));

        Assert.Equal(540, place.Repeats);
        Assert.Equal(new LearningDataGap(Broadcast.At(1), Broadcast.At(541)), place.Gap);
    }

    [Fact(DisplayName = "a frame that lands where one is already placed, or before, is dropped")]
    public void AFrameThatLandsOnOneAlreadyPlacedIsDropped()
    {
        FramePlacement placement = new(Broadcast);
        placement.Place(Broadcast.At(0));
        placement.Place(Broadcast.At(1));

        Assert.False(placement.Place(Broadcast.At(1) + TimeSpan.FromMilliseconds(5)).Kept);
        Assert.False(placement.Place(Broadcast.At(0)).Kept);
        Assert.False(placement.Place(TimeSpan.Zero).Kept);
        Assert.Equal(2, placement.Placed);
        Assert.Equal(Broadcast.At(2), placement.PlacedThrough);
    }
}
