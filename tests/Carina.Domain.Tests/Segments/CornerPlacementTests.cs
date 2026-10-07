using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class CornerPlacementTests
{
    [Fact(DisplayName = "one picture a second is kept, and a second one in the same second is dropped")]
    public void OnePictureASecondIsKeptAndASecondInTheSameSecondDropped()
    {
        CornerPlacement placement = new();

        Assert.Equal(new CornerPlace(true, 0), placement.Place(TimeSpan.FromMilliseconds(33)));
        Assert.Equal(new CornerPlace(true, 0), placement.Place(TimeSpan.FromMilliseconds(1001)));
        Assert.Equal(new CornerPlace(false, 0), placement.Place(TimeSpan.FromMilliseconds(1034)));
        Assert.Equal(new CornerPlace(true, 0), placement.Place(TimeSpan.FromMilliseconds(2002)));
        Assert.Equal(3, placement.Placed);
    }

    [Fact(DisplayName = "a picture that skips seconds is kept after a copy for each second skipped")]
    public void APictureThatSkipsSecondsIsKeptAfterACopyForEach()
    {
        CornerPlacement placement = new();
        placement.Place(TimeSpan.FromMilliseconds(33));

        Assert.Equal(new CornerPlace(true, 18), placement.Place(TimeSpan.FromSeconds(19.5)));
        Assert.Equal(20, placement.Placed);
    }

    [Fact(DisplayName = "the first picture coming late is kept after a copy for each second before it, and a time before zero is the first second")]
    public void TheFirstPictureComingLateFillsTheSecondsBeforeIt()
    {
        Assert.Equal(new CornerPlace(true, 2), new CornerPlacement().Place(TimeSpan.FromSeconds(2.2)));
        Assert.Equal(new CornerPlace(true, 0), new CornerPlacement().Place(TimeSpan.FromMilliseconds(-20)));
    }
}
