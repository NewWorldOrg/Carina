using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SoundPlacementTests
{
    private const int Block = 170;

    [Fact(DisplayName = "blocks that follow one another carry straight on, whatever the rounding of their times")]
    public void BlocksThatFollowOneAnotherCarryStraightOn()
    {
        SoundPlacement placement = new();
        List<SoundPlace> places = [];

        for (int block = 0; block < 100; block++)
        {
            TimeSpan at = TimeSpan.FromMilliseconds(Math.Round(block * Block * 1000.0 / SoundReader.SampleRate));

            places.Add(placement.Place(at, Block));
        }

        Assert.All(places, place => Assert.Equal(new SoundPlace(0, 0, null), place));
        Assert.Equal(100L * Block, placement.Placed);
    }

    [Theory(DisplayName = "a block a few milliseconds early or late carries straight on")]
    [InlineData(-9)]
    [InlineData(-4)]
    [InlineData(6)]
    [InlineData(10)]
    public void ABlockAFewMillisecondsOffCarriesStraightOn(int milliseconds)
    {
        SoundPlacement placement = new();
        placement.Place(TimeSpan.Zero, 8000);

        SoundPlace place = placement.Place(TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(milliseconds), Block);

        Assert.Equal(new SoundPlace(0, 0, null), place);
        Assert.Equal(8000 + Block, placement.Placed);
    }

    [Fact(DisplayName = "a block that starts well after the sound so far is put at its own time after silence, and the silence is a gap")]
    public void ABlockThatStartsLaterIsPutAtItsOwnTimeAfterSilenceKeptAsAGap()
    {
        SoundPlacement placement = new();
        placement.Place(TimeSpan.Zero, 12 * 8000);

        SoundPlace place = placement.Place(TimeSpan.FromSeconds(30), Block);

        Assert.Equal(18L * 8000, place.Silence);
        Assert.Equal(0, place.Dropped);
        Assert.Equal(new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30)), place.Gap);
        Assert.Equal((30L * 8000) + Block, placement.Placed);
    }

    [Fact(DisplayName = "silence put in for less than a tenth of a second is not a gap")]
    public void SilenceShorterThanATenthIsNotAGap()
    {
        SoundPlacement placement = new();
        placement.Place(TimeSpan.Zero, 8000);

        SoundPlace place = placement.Place(TimeSpan.FromMilliseconds(1050), Block);

        Assert.Equal(400, place.Silence);
        Assert.Null(place.Gap);
    }

    [Fact(DisplayName = "a block that starts on sound already placed loses the pairs that overlap it")]
    public void ABlockThatOverlapsLosesTheOverlap()
    {
        SoundPlacement placement = new();
        placement.Place(TimeSpan.Zero, 10 * 8000);

        SoundPlace place = placement.Place(TimeSpan.FromMilliseconds(9980), Block);

        Assert.Equal(new SoundPlace(0, 160, null), place);
        Assert.Equal((10 * 8000) + Block - 160, placement.Placed);
    }

    [Fact(DisplayName = "a block that lies wholly on sound already placed is dropped whole")]
    public void ABlockWhollyOverlappedIsDropped()
    {
        SoundPlacement placement = new();
        placement.Place(TimeSpan.Zero, 10 * 8000);

        SoundPlace place = placement.Place(TimeSpan.FromSeconds(5), Block);

        Assert.Equal(new SoundPlace(0, Block, null), place);
        Assert.Equal(10 * 8000, placement.Placed);
    }

    [Fact(DisplayName = "the sound starting late is put after silence from zero")]
    public void SoundStartingLateIsPutAfterSilenceFromZero()
    {
        SoundPlacement placement = new();

        SoundPlace place = placement.Place(TimeSpan.FromMilliseconds(500), Block);

        Assert.Equal(4000, place.Silence);
        Assert.Equal(new LearningDataGap(TimeSpan.Zero, TimeSpan.FromMilliseconds(500)), place.Gap);
    }

    [Fact(DisplayName = "sound before zero is dropped")]
    public void SoundBeforeZeroIsDropped()
    {
        SoundPlacement placement = new();

        SoundPlace place = placement.Place(TimeSpan.FromMilliseconds(-50), 1000);

        Assert.Equal(new SoundPlace(0, 400, null), place);
        Assert.Equal(600, placement.Placed);
    }
}
