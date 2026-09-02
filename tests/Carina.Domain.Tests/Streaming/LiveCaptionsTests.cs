using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveCaptionsTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00];

    [Fact]
    public void TheCanvasRidesTheCaptionHeaderAsTwoBytesASideAtTheStartOfTheClock()
    {
        LiveFrame frame = LiveCaptions.Canvas(new VideoSize(1920, 1080));

        Assert.Equal(LiveChannel.CaptionHeader, frame.Channel);
        Assert.Equal(LivePts.Start, frame.Pts);
        Assert.Equal([0x07, 0x80, 0x04, 0x38], frame.Payload.ToArray());
    }

    [Fact]
    public void TheCanvasIsReadBackFromItsHeader()
    {
        Assert.Equal(new VideoSize(1440, 1080), LiveCaptions.CanvasOf(LiveCaptions.Canvas(new VideoSize(1440, 1080))));
    }

    [Fact]
    public void ACanvasWiderThanTwoBytesCanSayIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveCaptions.Canvas(new VideoSize(65_536, 1080)));
    }

    [Fact]
    public void AFrameThatIsNotACaptionHeaderNamesNoCanvas()
    {
        Assert.Null(LiveCaptions.CanvasOf(new LiveFrame(LiveChannel.Caption, LivePts.Start, new byte[] { 0x07, 0x80, 0x04, 0x38 })));
        Assert.Null(LiveCaptions.CanvasOf(new LiveFrame(LiveChannel.CaptionHeader, LivePts.Start, new byte[] { 0x07, 0x80 })));
        Assert.Null(LiveCaptions.CanvasOf(new LiveFrame(LiveChannel.CaptionHeader, LivePts.Start, new byte[] { 0x00, 0x00, 0x04, 0x38 })));
    }

    [Fact]
    public void ACaptionThatIsShownIsPlacedInEightBytesAndThenThePictureFollowsUntouched()
    {
        LiveFrame frame = LiveCaptions.Shown(LivePts.Of(90_000UL), new CaptionPicture(0x0102, 0x0304, 0x0506, 0x0708, Png));

        Assert.Equal(LiveChannel.Caption, frame.Channel);
        Assert.Equal(90_000UL, frame.Pts.Value);
        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], frame.Payload[..8].ToArray());
        Assert.Equal(Png, frame.Payload[8..].ToArray());
    }

    [Fact]
    public void ACaptionThatIsShownIsReadBackWhole()
    {
        CaptionPicture drawn = new(120, 900, 1200, 96, Png);

        CaptionPicture? read = LiveCaptions.PictureOf(LiveCaptions.Shown(LivePts.Of(7UL), drawn));

        Assert.NotNull(read);
        Assert.Equal((120, 900, 1200, 96), (read.Left, read.Top, read.Width, read.Height));
        Assert.Equal(Png, read.Png.ToArray());
    }

    [Fact]
    public void ClearingTheCaptionIsTheCaptionChannelCarryingNothingAtTheMomentItGoes()
    {
        LiveFrame frame = LiveCaptions.Cleared(LivePts.Of(180_000UL));

        Assert.Equal(LiveChannel.Caption, frame.Channel);
        Assert.Equal(180_000UL, frame.Pts.Value);
        Assert.True(frame.Payload.IsEmpty);
        Assert.True(LiveCaptions.Clears(frame));
        Assert.Null(LiveCaptions.PictureOf(frame));
    }

    [Fact]
    public void ACaptionThatIsShownDoesNotClear()
    {
        Assert.False(LiveCaptions.Clears(LiveCaptions.Shown(LivePts.Start, new CaptionPicture(0, 0, 1, 1, Png))));
    }

    [Fact]
    public void AnEmptyFrameOnAnotherChannelClearsNothing()
    {
        Assert.False(LiveCaptions.Clears(new LiveFrame(LiveChannel.Control, LivePts.Start, ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void APlacementWithNoPictureBehindItIsNotACaption()
    {
        Assert.Null(LiveCaptions.PictureOf(new LiveFrame(LiveChannel.Caption, LivePts.Start, new byte[8])));
        Assert.Null(LiveCaptions.PictureOf(new LiveFrame(LiveChannel.Caption, LivePts.Start, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1 })));
        Assert.Null(LiveCaptions.PictureOf(new LiveFrame(LiveChannel.Picture, LivePts.Start, new byte[] { 0, 0, 0, 0, 0, 1, 0, 1, 1 })));
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(65_536, 0, 1, 1)]
    [InlineData(0, 65_536, 1, 1)]
    [InlineData(0, 0, 65_536, 1)]
    [InlineData(0, 0, 1, 65_536)]
    public void APictureIsPlacedAndMeasuredInTwoBytesASideAndIsAtLeastAPixel(int left, int top, int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptionPicture(left, top, width, height, Png));
    }

    [Fact]
    public void APictureWithNoBytesIsNotAPicture()
    {
        Assert.Throws<ArgumentException>(() => new CaptionPicture(0, 0, 1, 1, ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void TheFurthestEdgeIsWhatTwoBytesCanSay()
    {
        Assert.Equal(65_535, CaptionPicture.FurthestEdge);
        Assert.Equal(4, LiveCaptions.CanvasLength);
        Assert.Equal(8, LiveCaptions.PlacementLength);
    }
}
