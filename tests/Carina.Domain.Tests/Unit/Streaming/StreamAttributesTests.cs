using Carina.Domain.Base;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class StreamAttributesTests
{
    [Fact]
    public void AScanTheStreamNeverSettledIsNotOneOfTheTwoWaysAPictureIsDrawn()
    {
        Assert.Equal(0, (int)ScanType.Undetermined);
        Assert.Equal(0, (int)AudioMode.Undetermined);
    }

    [Fact]
    public void AnAttributeSetIsRefusedAValueOutsideTheOnesNamed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StreamAttributes(new VideoSize(1, 1), (ScanType)99, FrameRate.Of(30, 1), AudioMode.Stereo));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StreamAttributes(new VideoSize(1, 1), ScanType.Progressive, FrameRate.Of(30, 1), (AudioMode)99));
    }
}
